using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace IsTargetSleeping.Agent;

/// Agente de memoria de isTargetSleeping. Tres órdenes:
///   --clean &lt;spec&gt;              libera RAM (lo lanza la tarea programada, elevada)
///   --install &lt;SID&gt; [--close-memreduct]   se copia a Program Files y registra la tarea (con UAC, una vez)
///   --uninstall                    quita la tarea y su carpeta (con UAC)
/// El código de salida es el resultado: la app lo lee de la tarea.
public static class Program
{
    public const string TaskPath = @"\isTargetSleeping\Liberar RAM";

    public static string InstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "isTargetSleeping", "cleaner");

    public static int Main(string[] args)
    {
        if (!IsElevated()) return AgentExit.NotElevated;
        try
        {
            return args switch
            {
                ["--clean", var spec] => Clean(spec),
                ["--terminate", var spec] => Terminate(spec),
                ["--install", var sid, .. var rest] => Install(sid, rest.Contains("--close-memreduct")),
                ["--uninstall"] => Uninstall(),
                _ => AgentExit.BadSpec,
            };
        }
        catch { return 1; }
    }

    private static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static int Clean(string text)
    {
        if (CleanSpec.Parse(text) is not { } spec) return AgentExit.BadSpec;
        if (spec.CloseMemReduct) CloseMemReduct();
        CleanResult result;
        try { result = MemoryCleaner.Clean(spec); }
        catch (Exception e)
        {
            result = new(0, spec.Areas, e.Message)
            { RequestId = spec.RequestId, Requested = spec.Areas, Outcome = CleanOutcome.Failed,
                Errors = [new("Clean", Message: e.Message)] };
        }
        if (spec.RequestId != Guid.Empty) AgentReports.Write(result);
        return AgentExit.Done | (int)result.Failed;
    }

    private static int Terminate(string text)
    {
        if (ProcessActionProtocol.Parse(text) is not { } request) return AgentExit.BadSpec;
        // UAC launches only the trusted installed agent. The scheduled task always
        // prepends --clean, and its strict parser rejects this separate protocol.
        var expected = Path.Combine(InstallDir, "isTargetSleeping.MemoryAgent.exe");
        if (!string.Equals(Path.GetFullPath(Environment.ProcessPath!), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase))
            return AgentExit.BadSpec;
        ProcessActionResult result;
        try { result = ProcessActions.Execute(request); }
        catch (Exception e)
        {
            var now = DateTimeOffset.UtcNow;
            result = new(request.Id, ProcessActionOutcome.Failed, now, now,
                request.Targets.Select(t => new ProcessTargetResult(t, ProcessTargetOutcome.Failed, e.Message)).ToArray());
        }
        AgentReports.Write(result);
        return result.Outcome is ProcessActionOutcome.Success or ProcessActionOutcome.NoWork ? 0 : 1;
    }

    /// Cierra Mem Reduct (corre como administrador: la app normal no puede). Solo el
    /// memreduct.exe instalado en Program Files.
    private static void CloseMemReduct()
    {
        var installedPaths = new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(folder => Path.Combine(Environment.GetFolderPath(folder), "Mem Reduct", "memreduct.exe"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcessesByName("memreduct"))
        {
            using (p)
            {
                try
                {
                    var path = p.MainModule?.FileName ?? "";
                    if (installedPaths.Contains(path) && ProcessMonitor.QueryIdentity(p.Id) is { } identity)
                        ProcessActions.Execute(new(Guid.NewGuid(), ProcessActionMode.Single, [identity]));
                }
                catch { }
            }
        }
    }

    // MARK: instalar y desinstalar

    private static int Install(string sid, bool closeMemReduct)
    {
        // Solo un SID de usuario (S-1-5-21-…): va dentro del XML de la tarea.
        if (!sid.StartsWith("S-1-5-", StringComparison.Ordinal) || sid.Length > 100 || !sid.All(c => char.IsAsciiDigit(c) || c is 'S' or '-'))
            return AgentExit.BadSpec;
        Directory.CreateDirectory(InstallDir);
        SecureDirectory(InstallDir);
        Directory.CreateDirectory(AgentReports.DirectoryPath);
        SecureDirectory(AgentReports.DirectoryPath);
        var target = Path.Combine(InstallDir, Path.GetFileName(Environment.ProcessPath!));
        if (!string.Equals(Path.GetFullPath(Environment.ProcessPath!), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 0; ; i++)
            {
                try { File.Copy(Environment.ProcessPath!, target, overwrite: true); break; }
                catch (IOException) when (i < 20) { Thread.Sleep(250); }   // un agente anterior aún acabando
            }
        }

        var xml = Path.Combine(InstallDir, "task.xml");
        int code = RegisterTask(xml, target, sid, withSecurity: true);
        if (code != 0) code = RegisterTask(xml, target, sid, withSecurity: false);
        try { File.Delete(xml); } catch { }
        if (code == 0 && closeMemReduct) CloseMemReduct();
        return code;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string text, uint revision,
        out IntPtr descriptor, out uint size);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetFileSecurity(string path, uint information, IntPtr descriptor);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    private static void SecureDirectory(string path)
    {
        // Protected DACL: administrators and SYSTEM write; normal users only read
        // and traverse. Children inherit the same permissions (including reports).
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Agent directory must not be a reparse point.");
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(
            "D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;GRGX;;;BU)", 1, out var descriptor, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (!SetFileSecurity(path, 0x80000004 /* DACL_SECURITY_INFORMATION | PROTECTED_DACL_SECURITY_INFORMATION */, descriptor))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { LocalFree(descriptor); }
    }

    /// La tarea: sin disparadores (solo se lanza a petición), con los privilegios más
    /// altos del usuario, y su acción recibe la orden como $(Arg0). El descriptor de
    /// seguridad deja al usuario leerla y lanzarla, nada más.
    private static int RegisterTask(string xmlPath, string exe, string sid, bool withSecurity)
    {
        var security = withSecurity ? $"<SecurityDescriptor>D:(A;;FA;;;BA)(A;;FA;;;SY)(A;;GRGX;;;{sid})</SecurityDescriptor>" : "";
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.3" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Author>isTargetSleeping</Author>
                <Description>isTargetSleeping: libera la RAM cuando lo pides, sin volver a pedir permiso de administrador.</Description>
                {security}
              </RegistrationInfo>
              <Principals>
                <Principal id="Author">
                  <UserId>{sid}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <ExecutionTimeLimit>PT2M</ExecutionTimeLimit>
                <Priority>5</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(exe)}</Command>
                  <Arguments>--clean "$(Arg0)"</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
        File.WriteAllText(xmlPath, xml, Encoding.Unicode);
        return Schtasks($"/Create /TN \"{TaskPath}\" /XML \"{xmlPath}\" /F");
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existing, string? replacement, int flags);

    private static int Uninstall()
    {
        Schtasks($"/Delete /TN \"{TaskPath}\" /F");
        var parent = Path.GetDirectoryName(InstallDir)!;
        // Lo que no sea el .exe en uso se borra ya.
        foreach (var file in Directory.GetFiles(InstallDir))
        {
            if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(Environment.ProcessPath!), StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(file); } catch { }
        }
        // El .exe en uso no se puede borrar: por si acaso (antivirus analizándolo), que
        // Windows lo borre al reiniciar, y además un cmd oculto lo intenta varias veces
        // en cuanto este proceso termine.
        MoveFileEx(Environment.ProcessPath!, null, 0x4 /* MOVEFILE_DELAY_UNTIL_REBOOT */);
        MoveFileEx(InstallDir, null, 0x4);
        MoveFileEx(parent, null, 0x4);
        var attempt = $"rd /s /q \"{InstallDir}\" 2>nul & rd \"{parent}\" 2>nul";
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            $"/c ping 127.0.0.1 -n 2 >nul & {attempt} & ping 127.0.0.1 -n 3 >nul & {attempt} & ping 127.0.0.1 -n 6 >nul & {attempt}")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Environment.SystemDirectory,
            WindowStyle = ProcessWindowStyle.Hidden,
        })?.Dispose();
        return 0;
    }

    private static int Schtasks(string args)
    {
        using var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit(30_000);
        return p.ExitCode;
    }
}
