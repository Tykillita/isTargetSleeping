using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping;

public enum AgentStatus { Unavailable, NotInstalled, Ready, Outdated }

/// El resultado de una limpieza: lo liberado (medido por la app antes y después)
/// y las zonas que fallaron.
public sealed record CleanResult(long Freed, CleanAreas Failed, string? Error);

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
        try { return Connect().GetFolder(Folder).GetTask(TaskName); } catch { return null; }
    }

    /// Bloquea (COM y disco): llamar fuera del hilo de la interfaz.
    public static AgentStatus Status()
    {
        if (!Embedded) return AgentStatus.Unavailable;
        if (!File.Exists(InstalledExe) || FindTask() is null) return AgentStatus.NotInstalled;
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
    public static string? Install(bool closeMemReduct)
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
            var args = $"--install {id.User!.Value}{(closeMemReduct ? " --close-memreduct" : "")}";
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
        long before = SystemMemory.Current().Used;
        var (failed, error) = Run(new CleanSpec(areas, keep.ToList()));
        Thread.Sleep(300);
        return new CleanResult(Math.Max(0, before - SystemMemory.Current().Used), failed, error);
    }

    /// Lanza la tarea con la orden y espera su código de salida (máximo 30 s). Bloquea.
    public static (CleanAreas Failed, string? Error) Run(CleanSpec spec)
    {
        try
        {
            var task = FindTask();
            if (task is null) return (CleanAreas.None, tr("Activa la limpieza de RAM en Ajustes (pide permiso de administrador una vez)."));
            DateTime before = (DateTime)task.LastRunTime;
            task.Run(spec.Format());
            var deadline = DateTime.Now.AddSeconds(30);
            while (DateTime.Now < deadline)
            {
                Thread.Sleep(250);
                task = FindTask();
                if (task is null) break;
                if ((int)task.State != 4 /* TASK_STATE_RUNNING */ && (DateTime)task.LastRunTime > before)
                {
                    int code = (int)task.LastTaskResult;
                    if (AgentExit.IsDone(code)) return (AgentExit.Failed(code), null);
                    if (code == 267009 /* SCHED_S_TASK_RUNNING */) continue;
                    return (spec.Areas, code == AgentExit.NotElevated
                        ? tr("Tu cuenta de Windows necesita ser administradora para liberar RAM.")
                        : tr("El agente de memoria falló (código %d).", code));
                }
            }
            return (spec.Areas, tr("El agente de memoria no respondió."));
        }
        catch (Exception e) { return (spec.Areas, e.Message); }
    }
}
