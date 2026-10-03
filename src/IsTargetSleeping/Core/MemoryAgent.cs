using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Runtime.InteropServices;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping;

public enum AgentStatus { Unavailable, NotInstalled, Ready, Outdated }

/// La app corre sin privilegios. Liberar RAM necesita administrador, así que lo
/// hace un agente pequeño (isTargetSleeping.MemoryAgent.exe, dentro de la app) que
/// se instala una vez —un aviso de UAC— en Program Files y se registra como tarea
/// programada con los privilegios más altos. Después la app lanza esa tarea con la
/// orden como argumento y lee el código de salida: sin más avisos de UAC.
public static class MemoryAgent
{
    private const string Resource = "IsTargetSleeping.MemoryAgent.exe";
    private const string ExeName = "isTargetSleeping.MemoryAgent.exe";
    private const string Folder = @"\isTargetSleeping";
    private const string TaskName = "Liberar RAM";

    public static string InstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "isTargetSleeping", "cleaner");

    public static string InstalledExe => Path.Combine(InstallDir, ExeName);

    /// El agente va dentro de la app (lo mete build.ps1); una compilación sin él no ofrece limpiar.
    public static bool Embedded => typeof(MemoryAgent).Assembly.GetManifestResourceInfo(Resource) is not null;

    private static dynamic Connect()
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        service.Connect();
        return service;
    }

    private static dynamic? FindTask()
    {
        dynamic? service = null, folder = null;
        try { service = Connect(); folder = service.GetFolder(Folder); return folder.GetTask(TaskName); }
        catch { return null; }
        finally { ReleaseCom(folder); ReleaseCom(service); }
    }
    private static void ReleaseCom(object? value)
    { try { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); } catch { } }

    /// Bloquea (COM y disco): llamar fuera del hilo de la interfaz.
    public static AgentStatus Status()
    {
        if (!Embedded) return AgentStatus.Unavailable;
        var task = FindTask();
        try { if (!File.Exists(InstalledExe) || task is null) return AgentStatus.NotInstalled; }
        finally { ReleaseCom(task); }
        try
        {
            var installed = FileVersionInfo.GetVersionInfo(InstalledExe).ProductVersion?.Split('+')[0];
            return installed == AppInfo.Version ? AgentStatus.Ready : AgentStatus.Outdated;
        }
        catch { return AgentStatus.Outdated; }
    }

    // MARK: instalar y quitar (con UAC)

    /// Saca el agente de la app a una carpeta temporal y lo lanza elevado para que se
    /// instale. Devuelve null si fue bien o un mensaje para el usuario.
    public static string? Install()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"{AppInfo.Name}-agent-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            var exe = Path.Combine(dir, ExeName);
            using (var input = typeof(MemoryAgent).Assembly.GetManifestResourceStream(Resource))
            {
                if (input is null) return tr("Esta compilación no trae el agente de memoria.");
                using var output = File.Create(exe);
                input.CopyTo(output);
            }
            using var id = WindowsIdentity.GetCurrent();
            var args = $"--install {id.User!.Value}";
            return Elevated(exe, args);
        }
        catch (Exception e) { return e.Message; }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    public static string? Uninstall() =>
        File.Exists(InstalledExe) ? Elevated(InstalledExe, "--uninstall") : null;

    private static string? Elevated(string exe, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            if (p is null) return tr("Windows pidió permisos de administrador y no se concedieron.");
            if (!p.WaitForExit(120_000)) return tr("El agente de memoria no respondió.");
            return p.ExitCode switch
            {
                0 => null,
                AgentExit.NotElevated => tr("Tu cuenta de Windows necesita ser administradora para liberar RAM."),
                var code => tr("El agente de memoria falló (código %d).", code),
            };
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            return tr("Windows pidió permisos de administrador y no se concedieron.");
        }
        catch (Exception e) { return e.Message; }
    }

    // MARK: limpiar (sin UAC)

    /// Procesos que la limpieza no toca: los modelos (runners de Ollama,
    /// llama-server, LM Studio), el propio Ollama y esta app. Bloquea.
    public static HashSet<int> ModelPids()
    {
        var pids = new HashSet<int>(ProcessCpu.RunnerPids()) { Environment.ProcessId };
        foreach (var p in Procs.ByName("llama-server", "ollama", "ollama app", "ollama_llama_server", "LM Studio", "lms"))
            pids.Add(p.Pid);
        return pids;
    }

    /// Una limpieza completa sin la app abierta (--clean): mide antes y después.
    public static CleanResult CleanNow(CleanAreas areas, IEnumerable<int> keep)
    {
        var roots = keep.ToHashSet();
        if (new GameDetector().Scan(Defaults.GetList(PrefKeys.CustomGames), Defaults.GetList(PrefKeys.IgnoredGames)) is { Pid: > 0 } game)
            roots.Add(game.Pid);
        if (ProcessMonitor.GetForegroundPid() is { } foreground) roots.Add(foreground);
        if (ProcessMonitor.LastExternalForegroundPid is { } previousForeground) roots.Add(previousForeground);
        var captured = new ProcessMonitor().Capture(roots);
        var protectedPids = ProcessMonitor.ExpandProtection(roots, captured.Processes);
        var identities = captured.Processes.Where(p => protectedPids.Contains(p.Pid) && p.Identity.IsValid)
            .Select(p => p.Identity).ToArray();
        var unknown = protectedPids.Where(pid => !identities.Any(i => i.Pid == pid)).ToArray();
        var before = MemorySample(SystemMemory.Current());
        var result = Run(new CleanSpec(areas, unknown) { RequestId = Guid.NewGuid(), KeepIdentities = identities });
        if (result.Outcome == CleanOutcome.Pending && result.Completion is { } completion)
            result = completion.GetAwaiter().GetResult();
        result = result with { Before = result.Before ?? before, Immediate = result.Immediate ?? MemorySample(SystemMemory.Current()) };
        if (result.Outcome == CleanOutcome.Pending) return result;
        Thread.Sleep(5000);
        var five = MemorySample(SystemMemory.Current());
        Thread.Sleep(25_000);
        return result with { Freed = result.Before!.Used - five.Used,
            AfterFiveSeconds = five, AfterThirtySeconds = MemorySample(SystemMemory.Current()) };
    }

    private static CleanMemorySample MemorySample(SystemMemory memory) => new(DateTimeOffset.UtcNow,
        memory.Used, memory.Available, memory.Committed, memory.CommitLimit,
        (int)(100.0 * memory.Used / Math.Max(1, memory.Total)), memory.PhysicalPressure == SystemMemory.Level.Critical);

    /// Launches a concrete scheduler instance and reads only its request-correlated report.
    /// A dedicated worker owns the cross-process mutex until that instance ends, including
    /// after the 130-second UI wait. No scheduler-wide LastTaskResult is used.
    public static CleanResult Run(CleanSpec spec)
    {
        var initial = new TaskCompletionSource<CleanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<CleanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() => Track(spec, initial, completed)) { IsBackground = true, Name = "Memory agent instance" };
        worker.Start();
        return initial.Task.GetAwaiter().GetResult();
    }

    private static void Track(CleanSpec original, TaskCompletionSource<CleanResult> initial,
        TaskCompletionSource<CleanResult> completion)
    {
        dynamic? running = null;
        bool owned = false;
        Mutex? mutex = null;
        var spec = original with { RequestId = original.RequestId == Guid.Empty ? Guid.NewGuid() : original.RequestId };
        CleanResult Failure(string message) => new(0, spec.Areas, message)
        { RequestId = spec.RequestId, Requested = spec.Areas, Outcome = CleanOutcome.Failed };
        try
        {
            mutex = new Mutex(false, @"Global\isTargetSleeping.MemoryClean.v2");
            try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
            if (!owned)
            {
                var busy = Failure(tr("Ya hay una limpieza de RAM en curso."));
                initial.TrySetResult(busy); completion.TrySetResult(busy); return;
            }
            var task = FindTask();
            try
            {
                if (task is null) throw new InvalidOperationException(tr("Activa la limpieza de RAM en Ajustes (pide permiso de administrador una vez)."));
                if ((int)task.State == 4) throw new InvalidOperationException(tr("Ya hay una limpieza de RAM en curso."));
                running = task.Run(spec.Format());
            }
            finally { ReleaseCom(task); }
            if (running is null) throw new InvalidOperationException(tr("El agente de memoria no respondió."));
            // Reading this property also verifies that COM returned an actual instance.
            _ = (string)running.InstanceGuid;
            var clock = Stopwatch.StartNew();
            bool pending = false;
            while (true)
            {
                Thread.Sleep(250);
                var report = AgentReports.ReadClean(spec.RequestId);
                if (AgentRunTracking.Evaluate(spec.RequestId, clock.Elapsed, false, report) == AgentTrackingState.Completed)
                {
                    initial.TrySetResult(report!); completion.TrySetResult(report!); return;
                }
                bool ended = false;
                try { running.Refresh(); ended = (int)running.State is 0 or 1 or 3; }
                catch (COMException e) when ((uint)e.HResult is 0x8004130B or 0x80070002) { ended = true; }
                catch (COMException) { /* Keep the mutex while the scheduler is temporarily unavailable. */ }
                var state = AgentRunTracking.Evaluate(spec.RequestId, clock.Elapsed, ended, null);
                if (state == AgentTrackingState.MissingReport)
                    throw new InvalidOperationException(tr("El agente terminó sin un informe válido. Actualiza el agente desde Ajustes."));
                if (!pending && state == AgentTrackingState.Pending)
                {
                    pending = true;
                    initial.TrySetResult(new CleanResult(0, CleanAreas.None, null)
                    { RequestId = spec.RequestId, Requested = spec.Areas, Outcome = CleanOutcome.Pending, Completion = completion.Task });
                }
            }
        }
        catch (Exception e) { var result = Failure(e.Message); initial.TrySetResult(result); completion.TrySetResult(result); }
        finally
        {
            ReleaseCom(running);
            if (owned) mutex!.ReleaseMutex();
            mutex?.Dispose();
        }
    }

    /// Explicit UAC path; termination can never be requested through the automatic clean task.
    public static ProcessActionResult TerminateElevated(ProcessActionRequest request) =>
        ExecuteProcessActionAsync(request, elevated: true).GetAwaiter().GetResult();

    private static ProcessActionResult TrackElevated(ProcessActionRequest request,
        TaskCompletionSource<ProcessActionResult> initial, TaskCompletionSource<ProcessActionResult> completion)
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        ProcessActionResult Failed(string reason, ProcessActionOutcome outcome = ProcessActionOutcome.Failed) =>
            new(request.Id, outcome, started, DateTimeOffset.UtcNow,
                request.Targets.Select(t => new ProcessTargetResult(t, outcome switch
                { ProcessActionOutcome.Pending => ProcessTargetOutcome.Pending, ProcessActionOutcome.Cancelled => ProcessTargetOutcome.Blocked,
                    _ => ProcessTargetOutcome.Denied }, reason)).ToArray());
        if (!File.Exists(InstalledExe)) return Failed(tr("Instala el agente de memoria desde Ajustes para reintentar como administrador."));
        try
        {
            string argument = ProcessActionProtocol.Format(request);
            using var process = Process.Start(new ProcessStartInfo(InstalledExe)
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = "--terminate " + argument,
            });
            if (process is null) return Failed(tr("Windows pidió permisos de administrador y no se concedieron."), ProcessActionOutcome.Cancelled);
            var clock = Stopwatch.StartNew();
            bool pending = false;
            while (!process.WaitForExit(250))
            {
                if (!pending && clock.Elapsed >= TimeSpan.FromSeconds(130))
                {
                    pending = true;
                    initial.TrySetResult(Failed(tr("El agente sigue activo. La limpieza permanece pendiente."), ProcessActionOutcome.Pending)
                        with { Completion = completion.Task });
                }
            }
            return AgentReports.ReadTerminate(request.Id) ?? Failed(tr("El agente terminó sin un informe válido. Actualiza el agente desde Ajustes."));
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)
        { return Failed(tr("Windows pidió permisos de administrador y no se concedieron."), ProcessActionOutcome.Cancelled); }
        catch (Exception e) { return Failed(e.Message); }
    }

    /// Serializes normal/elevated process actions with clean requests from every app/CLI.
    /// Elevation is used only after the caller obtains the separate UAC retry consent.
    public static Task<ProcessActionResult> ExecuteProcessActionAsync(ProcessActionRequest request, bool elevated = false,
        CancellationToken cancellationToken = default)
    {
        var initial = new TaskCompletionSource<ProcessActionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<ProcessActionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            bool owned = false;
            Mutex? mutex = null;
            var started = DateTimeOffset.UtcNow;
            try
            {
                mutex = new Mutex(false, @"Global\isTargetSleeping.MemoryClean.v2");
                try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
                ProcessActionResult result;
                if (!owned)
                    result = new(request.Id, ProcessActionOutcome.Failed, started, DateTimeOffset.UtcNow,
                        request.Targets.Select(t => new ProcessTargetResult(t, ProcessTargetOutcome.Blocked,
                            tr("Ya hay una limpieza de RAM en curso."))).ToArray());
                else if (cancellationToken.IsCancellationRequested)
                    result = new(request.Id, ProcessActionOutcome.Cancelled, started, DateTimeOffset.UtcNow,
                        request.Targets.Select(t => new ProcessTargetResult(t, ProcessTargetOutcome.Blocked, "Cancelled")).ToArray());
                else result = elevated ? TrackElevated(request, initial, completion) : ProcessActions.Execute(request, cancellationToken);
                initial.TrySetResult(result); completion.TrySetResult(result);
            }
            catch (Exception e)
            {
                var result = new ProcessActionResult(request.Id, ProcessActionOutcome.Failed, started, DateTimeOffset.UtcNow,
                    request.Targets.Select(t => new ProcessTargetResult(t, ProcessTargetOutcome.Failed, e.Message)).ToArray());
                initial.TrySetResult(result); completion.TrySetResult(result);
            }
            finally { if (owned) mutex!.ReleaseMutex(); mutex?.Dispose(); }
        }) { IsBackground = true, Name = "Process action instance" };
        worker.Start();
        return initial.Task;
    }
}
