using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping;

// MARK: - cómo arranca Ollama en este PC

public enum BackendKind { Service, Task, App, Binary, Missing }

/// Cada PC lo arranca distinto, y apagarlo mal no sirve: un servicio con
/// reinicio automático o una tarea programada lo vuelven a lanzar si solo se
/// mata el proceso. Por eso primero se detecta el mecanismo y se apaga por ese
/// mismo camino.
///  - Service: un servicio de Windows (NSSM, WinSW, `sc create`…) que ejecuta Ollama.
///  - Task: una tarea programada que ejecuta `ollama serve`.
///  - App: la app oficial de ollama.com (`ollama app.exe` en la bandeja).
///  - Binary: solo el binario (`ollama serve` a mano o desde un script).
public sealed record Backend(BackendKind Kind, string Id = "", string Name = "", string? Log = null)
{
    public static readonly Backend Missing = new(BackendKind.Missing);

    /// Nombre corto para el selector de ajustes.
    public string KindLabel => Kind switch
    {
        BackendKind.Service => tr("Servicio"),
        BackendKind.Task => tr("Tarea"),
        BackendKind.App => "Ollama app",
        BackendKind.Binary => "ollama serve",
        _ => "-",
    };

    public string Summary => Kind switch
    {
        BackendKind.Service or BackendKind.Task => Name,
        BackendKind.Missing => tr("no instalado"),
        _ => KindLabel,
    };

    /// Identidad estable para recordarlo entre arranques.
    public string? Key => Kind switch
    {
        BackendKind.Service => $"service:{Id}",
        BackendKind.Task => $"task:{Id}",
        BackendKind.App => "app",
        BackendKind.Binary => "binary",
        _ => null,
    };

    public string? LogPath => Kind switch
    {
        BackendKind.App => Path.Combine(Paths.OllamaAppData, "server.log"),
        BackendKind.Binary => Paths.OwnLog,
        BackendKind.Service or BackendKind.Task => Log,
        _ => null,
    };
}

// MARK: - servicios de Windows

public static class Services
{
    private static List<Backend>? cache;
    private static DateTime cachedAt;

    /// Servicios cuyo ejecutable (o el programa que envuelve, en NSSM) es Ollama.
    public static List<Backend> Ollama()
    {
        if (cache is not null && DateTime.Now - cachedAt < TimeSpan.FromSeconds(20)) return cache;
        var list = new List<Backend>();
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            foreach (var name in root?.GetSubKeyNames() ?? [])
            {
                using var key = root!.OpenSubKey(name);
                if (key?.GetValue("ImagePath") is not string image) continue;
                if (((key.GetValue("Type") as int?) ?? 0) is var type && (type & 0x30) == 0) continue;
                using var parameters = key.OpenSubKey("Parameters");
                var app = parameters?.GetValue("Application") as string ?? "";
                var args = parameters?.GetValue("AppParameters") as string ?? "";
                var joined = $"{image} {app} {args}".ToLowerInvariant();
                if (!joined.Contains("ollama") || joined.Contains("ollama app")) continue;
                var log = parameters?.GetValue("AppStderr") as string ?? parameters?.GetValue("AppStdout") as string;
                var display = key.GetValue("DisplayName") as string;
                list.Add(new Backend(BackendKind.Service, name,
                    string.IsNullOrWhiteSpace(display) || display.StartsWith('@') ? name : display, log));
            }
        }
        catch { }
        cache = list.OrderBy(b => b.Id, StringComparer.OrdinalIgnoreCase).ToList();
        cachedAt = DateTime.Now;
        return cache;
    }

    private const uint Running = 4, StartPending = 2;

    public static bool IsRunning(string name)
    {
        var scm = Win32.OpenSCManager(null, null, Win32.SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) return false;
        try
        {
            var svc = Win32.OpenService(scm, name, Win32.SERVICE_QUERY_STATUS);
            if (svc == IntPtr.Zero) return false;
            try
            {
                return Win32.QueryServiceStatus(svc, out var st) && st.dwCurrentState is Running or StartPending;
            }
            finally { Win32.CloseServiceHandle(svc); }
        }
        finally { Win32.CloseServiceHandle(scm); }
    }

    public static string? Start(string name) => Control(name, start: true);
    public static string? Stop(string name) => Control(name, start: false);

    /// Con la cuenta del usuario si el servicio lo permite; si Windows responde
    /// «acceso denegado», se pide elevación (UAC) solo para esa orden.
    private static string? Control(string name, bool start)
    {
        var scm = Win32.OpenSCManager(null, null, Win32.SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) return new Win32Exception().Message;
        try
        {
            var svc = Win32.OpenService(scm, name, start ? Win32.SERVICE_START : Win32.SERVICE_STOP);
            if (svc == IntPtr.Zero)
            {
                int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                return err == 5 ? Elevated(start ? "start" : "stop", name) : new Win32Exception(err).Message;
            }
            try
            {
                bool ok = start ? Win32.StartService(svc, 0, IntPtr.Zero) : Win32.ControlService(svc, Win32.SERVICE_CONTROL_STOP, out _);
                if (ok) return null;
                int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                // 1056: ya en marcha · 1062: no estaba en marcha.
                if (err is 1056 or 1062) return null;
                return err == 5 ? Elevated(start ? "start" : "stop", name) : new Win32Exception(err).Message;
            }
            finally { Win32.CloseServiceHandle(svc); }
        }
        finally { Win32.CloseServiceHandle(scm); }
    }

    private static string? Elevated(string verb, string name)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("sc.exe", $"{verb} \"{name}\"")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (p is null) return tr("Windows pidió permisos de administrador y no se concedieron.");
            p.WaitForExit(30_000);
            return p.ExitCode is 0 or 1056 or 1062 ? null : $"sc {verb}: {p.ExitCode}";
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            return tr("Windows pidió permisos de administrador y no se concedieron.");
        }
        catch (Exception e) { return e.Message; }
    }
}

// MARK: - tareas programadas

public static class ScheduledTasks
{
    private static List<Backend>? cache;
    private static DateTime cachedAt;

    private static dynamic Connect()
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        service.Connect();
        return service;
    }

    /// Tareas del Programador que ejecutan `ollama serve`. Se salta \Microsoft,
    /// que tiene cientos de tareas del sistema.
    public static List<Backend> Ollama()
    {
        if (cache is not null && DateTime.Now - cachedAt < TimeSpan.FromSeconds(30)) return cache;
        var list = new List<Backend>();
        try
        {
            void Walk(dynamic folder)
            {
                foreach (dynamic task in folder.GetTasks(1))
                {
                    try
                    {
                        foreach (dynamic action in task.Definition.Actions)
                        {
                            if ((int)action.Type != 0) continue;   // TASK_ACTION_EXEC
                            string joined = $"{action.Path} {action.Arguments}".ToLowerInvariant();
                            if (joined.Contains("ollama") && joined.Contains("serve"))
                            {
                                list.Add(new Backend(BackendKind.Task, (string)task.Path, (string)task.Name));
                                break;
                            }
                        }
                    }
                    catch { }
                }
                foreach (dynamic sub in folder.GetFolders(0))
                {
                    if (((string)sub.Path).StartsWith(@"\Microsoft", StringComparison.OrdinalIgnoreCase)) continue;
                    Walk(sub);
                }
            }
            Walk(Connect().GetFolder("\\"));
        }
        catch { }
        cache = list.OrderBy(b => b.Id, StringComparer.OrdinalIgnoreCase).ToList();
        cachedAt = DateTime.Now;
        return cache;
    }

    private static dynamic? Get(string path)
    {
        try { return Connect().GetFolder("\\").GetTask(path); } catch { return null; }
    }

    public static bool IsRunning(string path)
    {
        try { return Get(path) is { } task && (int)task.State == 4; } catch { return false; }   // TASK_STATE_RUNNING
    }

    public static string? Start(string path)
    {
        try
        {
            var task = Get(path);
            if (task is null) return tr("No encuentro la tarea %@.", path);
            task.Run(null);
            return null;
        }
        catch (Exception e) { return e.Message; }
    }

    public static string? Stop(string path)
    {
        try { Get(path)?.Stop(0); return null; }
        catch (Exception e) { return e.Message; }
    }
}

// MARK: - detección

public static class Detector
{
    private const string AppProcess = "ollama app";

    public static int? RunningAppPid()
    {
        foreach (var p in Process.GetProcessesByName(AppProcess))
        {
            using (p) return p.Id;
        }
        return null;
    }

    private static IEnumerable<string> InstallDirs()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "Programs", "Ollama");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ollama");
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var uninstall = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            foreach (var sub in uninstall?.GetSubKeyNames() ?? [])
            {
                using var key = uninstall!.OpenSubKey(sub);
                if ((key?.GetValue("DisplayName") as string)?.StartsWith("Ollama", StringComparison.OrdinalIgnoreCase) == true
                    && key!.GetValue("InstallLocation") is string dir && dir.Length > 0)
                    yield return dir.TrimEnd('\\');
            }
        }
    }

    public static string? InstalledApp() =>
        InstallDirs().Select(d => Path.Combine(d, "ollama app.exe")).FirstOrDefault(File.Exists);

    public static string? Binary()
    {
        var candidates = InstallDirs().Select(d => Path.Combine(d, "ollama.exe"));
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        candidates = candidates.Concat(path.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(d => Path.Combine(d.Trim('"'), "ollama.exe")));
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        candidates = candidates.Append(Path.Combine(home, "scoop", "shims", "ollama.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }

    /// Orden: lo que está en marcha manda sobre lo que solo está instalado, y
    /// entre lo instalado, el elegido en ajustes o el último con el que Ollama
    /// estuvo encendido.
    public static Backend Detect()
    {
        var running = DetectRunning();
        if (running is not null) Remember(running);
        return running ?? Recalled(PreferredKey) ?? Recalled(LastKey) ?? DetectInstalled();
    }

    private const string LastKey = "lastBackend";
    /// Elegido en ajustes: manda sobre el último usado.
    private const string PreferredKey = "preferredBackend";

    public static void SetPreferred(Backend b)
    {
        if (b.Key is { } key) Defaults.Set(PreferredKey, key);
    }

    public static Backend? Preferred => Recalled(PreferredKey);

    /// Todos los mecanismos instalados, para elegir en ajustes.
    public static List<Backend> Available()
    {
        var list = new List<Backend>();
        list.AddRange(Services.Ollama());
        list.AddRange(ScheduledTasks.Ollama());
        if (InstalledApp() is { } app) list.Add(new Backend(BackendKind.App, app));
        if (list.Count == 0 && Binary() is { } bin) list.Add(new Backend(BackendKind.Binary, bin));
        return list;
    }

    private static void Remember(Backend b)
    {
        if (b.Key is { } key && Defaults.GetString(LastKey) != key) Defaults.Set(LastKey, key);
    }

    /// Solo si sigue instalado igual que cuando se usó.
    private static Backend? Recalled(string settingsKey)
    {
        var value = Defaults.GetString(settingsKey);
        if (value is null) return null;
        if (value.StartsWith("service:")) return Services.Ollama().FirstOrDefault(b => b.Key == value);
        if (value.StartsWith("task:")) return ScheduledTasks.Ollama().FirstOrDefault(b => b.Key == value);
        if (value == "app") return InstalledApp() is { } app ? new Backend(BackendKind.App, app) : null;
        if (value == "binary") return Binary() is { } bin ? new Backend(BackendKind.Binary, bin) : null;
        return null;
    }

    private static Backend? DetectRunning()
    {
        if (RunningAppPid() is { } pid)
        {
            var path = Procs.ImagePath(pid) ?? InstalledApp() ?? "";
            return new Backend(BackendKind.App, path);
        }
        if (Services.Ollama().FirstOrDefault(s => Services.IsRunning(s.Id)) is { } svc) return svc;
        if (ScheduledTasks.Ollama().FirstOrDefault(t => ScheduledTasks.IsRunning(t.Id)) is { } task) return task;
        if (ServerPids().Count > 0 && Binary() is { } bin) return new Backend(BackendKind.Binary, bin);
        return null;
    }

    private static Backend DetectInstalled()
    {
        if (InstalledApp() is { } app) return new Backend(BackendKind.App, app);
        if (Services.Ollama().FirstOrDefault() is { } svc) return svc;
        if (ScheduledTasks.Ollama().FirstOrDefault() is { } task) return task;
        if (Binary() is { } bin) return new Backend(BackendKind.Binary, bin);
        return Backend.Missing;
    }

    /// PIDs de `ollama serve` (no los runners, que cuelgan del servidor).
    public static List<int> ServerPids() =>
        Procs.ByName("ollama").Where(p => Procs.Args(p.CommandLine).Contains("serve")).Select(p => p.Pid).ToList();
}

// MARK: - encender y apagar

public enum Power { Off, Starting, On, Stopping, Missing }

public static class PowerExt
{
    public static bool IsUp(this Power p) => p == Power.On;
    public static bool IsTransition(this Power p) => p is Power.Starting or Power.Stopping;
}

public static class Switch
{
    /// Devuelve un mensaje de error legible, o null si fue bien. Bloquea: llamar fuera del hilo de la interfaz.
    public static string? Start(Backend backend)
    {
        switch (backend.Kind)
        {
            case BackendKind.Service:
                return Services.Start(backend.Id);
            case BackendKind.Task:
                return ScheduledTasks.Start(backend.Id);
            case BackendKind.App:
                try
                {
                    // «hidden»: el mismo argumento que usa Ollama para arrancar en
                    // la bandeja sin abrir su ventana de chat.
                    Process.Start(new ProcessStartInfo(backend.Id, "hidden")
                    {
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(backend.Id) ?? "",
                    })?.Dispose();
                    return null;
                }
                catch (Exception e) { return e.Message; }
            case BackendKind.Binary:
                return StartDetached(backend.Id, "serve");
            default:
                return tr("Ollama no está instalado.");
        }
    }

    public static string? Stop(Backend backend)
    {
        switch (backend.Kind)
        {
            case BackendKind.Service:
                return Services.Stop(backend.Id);
            case BackendKind.Task:
                var failure = ScheduledTasks.Stop(backend.Id);
                KillServers();
                return failure;
            case BackendKind.App:
                if (Detector.RunningAppPid() is not { } pid) { KillServers(); return null; }
                // Primero se le pide que cierre, como «Quit Ollama» en su menú; si
                // en 2 s sigue viva, se termina con su árbol de procesos.
                Procs.CloseWindows(pid);
                for (int i = 0; i < 20 && Procs.Alive(pid); i++) Thread.Sleep(100);
                if (Procs.Alive(pid)) Procs.KillTree(pid);
                KillServers();
                return null;
            case BackendKind.Binary:
                KillServers();
                return null;
            default:
                return null;
        }
    }

    private static void KillServers()
    {
        foreach (var pid in Detector.ServerPids()) Procs.KillTree(pid);
    }

    /// Arranca desacoplado de esta app, sin ventana y con la salida en el log propio:
    /// sigue vivo aunque la app se cierre. Solo hereda el handle del log: si
    /// heredara todos, se llevaría también la tubería de la terminal que lanzó
    /// la app y esa terminal esperaría hasta que Ollama se apagara.
    public static string? StartDetached(string exe, string args, string? logPath = null, string? directory = null)
    {
        IntPtr attributes = IntPtr.Zero, handles = IntPtr.Zero;
        try
        {
            Directory.CreateDirectory(Paths.LogsDir);
            using var log = new FileStream(logPath ?? Paths.OwnLog, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            var handle = log.SafeFileHandle.DangerousGetHandle();
            Win32.SetHandleInformation(handle, 1, 1);   // HANDLE_FLAG_INHERIT

            IntPtr size = IntPtr.Zero;
            Win32.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            attributes = Marshal.AllocHGlobal(size);
            if (!Win32.InitializeProcThreadAttributeList(attributes, 1, 0, ref size)) return new Win32Exception().Message;
            handles = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(handles, handle);
            if (!Win32.UpdateProcThreadAttribute(attributes, 0, Win32.PROC_THREAD_ATTRIBUTE_HANDLE_LIST, handles,
                    IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                return new Win32Exception().Message;

            var si = new Win32.STARTUPINFOEX
            {
                StartupInfo = new Win32.STARTUPINFO
                {
                    cb = Marshal.SizeOf<Win32.STARTUPINFOEX>(),
                    dwFlags = Win32.STARTF_USESTDHANDLES,
                    hStdInput = IntPtr.Zero,
                    hStdOutput = handle,
                    hStdError = handle,
                },
                lpAttributeList = attributes,
            };
            uint flags = Win32.CREATE_NO_WINDOW | Win32.CREATE_NEW_PROCESS_GROUP | Win32.CREATE_UNICODE_ENVIRONMENT
                         | Win32.EXTENDED_STARTUPINFO_PRESENT;
            var dir = directory ?? Path.GetDirectoryName(exe);
            // Fuera del job de quien nos lanzó si se puede, para que no muera con él.
            bool ok = Win32.CreateProcessEx(null, new StringBuilder($"\"{exe}\" {args}"), IntPtr.Zero, IntPtr.Zero, true,
                          flags | Win32.CREATE_BREAKAWAY_FROM_JOB, IntPtr.Zero, dir, ref si, out var pi)
                      || Win32.CreateProcessEx(null, new StringBuilder($"\"{exe}\" {args}"), IntPtr.Zero, IntPtr.Zero, true,
                          flags, IntPtr.Zero, dir, ref si, out pi);
            if (!ok) return new Win32Exception().Message;
            Win32.CloseHandle(pi.hThread);
            Win32.CloseHandle(pi.hProcess);
            return null;
        }
        catch (IOException) { return tr("No puedo escribir el log"); }
        catch (Exception e) { return e.Message; }
        finally
        {
            if (attributes != IntPtr.Zero)
            {
                Win32.DeleteProcThreadAttributeList(attributes);
                Marshal.FreeHGlobal(attributes);
            }
            if (handles != IntPtr.Zero) Marshal.FreeHGlobal(handles);
        }
    }
}
