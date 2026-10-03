using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

// Ephemeral native SCM fixture. It owns one child Ollama handle and never
// enumerates or terminates other Ollama instances.
internal static class Program
{
    private const uint Stopped = 1, StartPending = 2, StopPending = 3, Running = 4;
    private static readonly ManualResetEvent StopRequested = new(false);
    private static readonly object StatusGate = new(), LogGate = new();
    private static readonly ServiceMainDelegate MainCallback = ServiceMain;
    private static readonly HandlerDelegate HandlerCallback = Handler;
    private static FixtureConfig Configuration = null!;
    private static IntPtr StatusHandle;
    private static uint CurrentState = StartPending, Checkpoint;
    private static Process? OwnedChild;
    private static string EventFile = "";

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2 || !Regex.IsMatch(args[0], "^isTargetSleeping-BackendVerification-Service-[a-f0-9]{12}$"))
                throw new ArgumentException("Expected the exclusive fixture service name and config path.");
            string fixtureRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
            var fixtureDirectory = new DirectoryInfo(fixtureRoot);
            if (!fixtureDirectory.Name.Equals("service", StringComparison.OrdinalIgnoreCase)
                || fixtureDirectory.Parent?.Name is not ("backend-verification" or "backend-integration"))
                throw new ArgumentException("Fixture must live under backend-verification/service or backend-integration/service.");
            string configPath = Path.GetFullPath(args[1]);
            if (!configPath.StartsWith(fixtureRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Configuration must stay inside the fixture directory.");
            Configuration = JsonSerializer.Deserialize<FixtureConfig>(File.ReadAllText(configPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new ArgumentException("No configuration.");
            if (Configuration.ServiceName != args[0] || Configuration.Port is < 49152 or > 65535)
                throw new ArgumentException("Unexpected service identity or loopback port.");
            string childPath = Path.GetFullPath(Configuration.OllamaExe);
            string expectedChild = Path.Combine(AppContext.BaseDirectory, "ollama.exe");
            if (!childPath.Equals(expectedChild, StringComparison.OrdinalIgnoreCase) || !File.Exists(childPath))
                throw new ArgumentException("Only the private copied Ollama executable is accepted.");
            Configuration.RunDirectory = Path.GetFullPath(Configuration.RunDirectory);
            string allowedDataRoot = Path.Combine(fixtureRoot, "data") + Path.DirectorySeparatorChar;
            if (!Configuration.RunDirectory.StartsWith(allowedDataRoot, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Run data must stay under the fixture data directory.");
            Directory.CreateDirectory(Path.Combine(Configuration.RunDirectory, "logs"));
            EventFile = Path.Combine(Configuration.RunDirectory, "logs", "host-events.jsonl");
            Log(new { Event = "host-start", Service = Configuration.ServiceName, HostPid = Environment.ProcessId });
            var table = new[]
            {
                new ServiceTableEntry { Name = Configuration.ServiceName, Callback = MainCallback },
                new ServiceTableEntry(),
            };
            if (!StartServiceCtrlDispatcher(table)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            try { Log(new { Event = "host-failure", Error = e.ToString() }); } catch { }
            return 1;
        }
    }

    private static void ServiceMain(uint count, IntPtr arguments)
    {
        try
        {
            StatusHandle = RegisterServiceCtrlHandlerEx(Configuration.ServiceName, HandlerCallback, IntPtr.Zero);
            if (StatusHandle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            SetState(StartPending);
            string profile = Path.Combine(Configuration.RunDirectory, "profile");
            string models = Path.Combine(Configuration.RunDirectory, "models");
            string temp = Path.Combine(Configuration.RunDirectory, "tmp");
            string local = Path.Combine(profile, "AppData", "Local");
            string roaming = Path.Combine(profile, "AppData", "Roaming");
            foreach (string dir in new[] { profile, models, temp, local, roaming }) Directory.CreateDirectory(dir);
            var info = new ProcessStartInfo(Configuration.OllamaExe, "serve")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Configuration.RunDirectory,
            };
            info.Environment["OLLAMA_HOST"] = $"127.0.0.1:{Configuration.Port}";
            info.Environment["OLLAMA_MODELS"] = models;
            // Windows Go resolves its home directory from USERPROFILE; keep all
            // child profile/temp output in the fixture, including generated keys.
            info.Environment["USERPROFILE"] = profile;
            info.Environment["LOCALAPPDATA"] = local;
            info.Environment["APPDATA"] = roaming;
            info.Environment["TEMP"] = temp;
            info.Environment["TMP"] = temp;
            OwnedChild = Process.Start(info) ?? throw new InvalidOperationException("No Ollama child was created.");
            var output = Pump(OwnedChild.StandardOutput, "ollama-stdout.log");
            var error = Pump(OwnedChild.StandardError, "ollama-stderr.log");
            Log(new
            {
                Event = "child-start", Pid = OwnedChild.Id,
                CreationUtc = OwnedChild.StartTime.ToUniversalTime().ToString("O"),
                Executable = Configuration.OllamaExe, Port = Configuration.Port,
                Models = models,
            });
            SetState(Running);
            while (!StopRequested.WaitOne(100) && !OwnedChild.HasExited) { }
            bool manualStop = StopRequested.WaitOne(0);
            if (!OwnedChild.HasExited)
            {
                SetState(StopPending);
                // This retained Process/handle identifies only our own child.
                OwnedChild.Kill(entireProcessTree: true);
            }
            bool exited = OwnedChild.WaitForExit(10_000);
            if (exited) Task.WaitAll([output, error], 5_000);
            Log(new
            {
                Event = "child-stop", Pid = OwnedChild.Id, ManualStop = manualStop,
                Exited = exited, ExitCode = exited ? OwnedChild.ExitCode : (int?)null,
            });
            if (manualStop && exited) SetState(Stopped);
            else SetState(Stopped, 1066, 1);
        }
        catch (Exception e)
        {
            Log(new { Event = "service-failure", Error = e.ToString() });
            try
            {
                if (OwnedChild is { HasExited: false })
                {
                    OwnedChild.Kill(entireProcessTree: true);
                    OwnedChild.WaitForExit(10_000);
                }
            }
            catch (Exception cleanup) { Log(new { Event = "child-cleanup-failure", Error = cleanup.ToString() }); }
            if (StatusHandle != IntPtr.Zero) SetState(Stopped, 1066, 2);
        }
        finally { OwnedChild?.Dispose(); }
    }

    private static async Task Pump(StreamReader reader, string file)
    {
        using var writer = new StreamWriter(Path.Combine(Configuration.RunDirectory, "logs", file), append: true);
        writer.AutoFlush = true;
        while (await reader.ReadLineAsync() is { } line)
            await writer.WriteLineAsync($"{DateTimeOffset.UtcNow:O} {line}");
    }

    private static uint Handler(uint control, uint eventType, IntPtr eventData, IntPtr context)
    {
        if (control is 1 or 5)
        {
            Log(new { Event = "stop-request", Control = control });
            SetState(StopPending);
            StopRequested.Set();
        }
        else if (control == 4) SetState(CurrentState);
        return 0;
    }

    private static void SetState(uint state, uint win32 = 0, uint specific = 0)
    {
        lock (StatusGate)
        {
            CurrentState = state;
            bool pending = state is StartPending or StopPending;
            var status = new ServiceStatus
            {
                Type = 0x10, State = state, Accepted = state == Running ? 5u : 0u,
                Win32ExitCode = win32, SpecificExitCode = specific,
                CheckPoint = pending ? ++Checkpoint : 0, WaitHint = pending ? 15_000u : 0,
            };
            if (!SetServiceStatus(StatusHandle, ref status))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            Log(new { Event = "status", State = state, Win32 = win32, Specific = specific });
        }
    }

    private static void Log(object item)
    {
        if (EventFile.Length == 0) return;
        lock (LogGate)
            File.AppendAllText(EventFile, JsonSerializer.Serialize(new { Utc = DateTimeOffset.UtcNow, Data = item }) + Environment.NewLine);
    }

    private sealed class FixtureConfig
    {
        public string ServiceName { get; set; } = "";
        public string OllamaExe { get; set; } = "";
        public string RunDirectory { get; set; } = "";
        public int Port { get; set; }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ServiceMainDelegate(uint argc, IntPtr argv);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint HandlerDelegate(uint control, uint eventType, IntPtr eventData, IntPtr context);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ServiceTableEntry
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string? Name;
        public ServiceMainDelegate? Callback;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint Type, State, Accepted, Win32ExitCode, SpecificExitCode, CheckPoint, WaitHint;
    }
    [DllImport("advapi32.dll", EntryPoint = "StartServiceCtrlDispatcherW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartServiceCtrlDispatcher([In] ServiceTableEntry[] table);
    [DllImport("advapi32.dll", EntryPoint = "RegisterServiceCtrlHandlerExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr RegisterServiceCtrlHandlerEx(string name, HandlerDelegate callback, IntPtr context);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetServiceStatus(IntPtr service, ref ServiceStatus status);
}
