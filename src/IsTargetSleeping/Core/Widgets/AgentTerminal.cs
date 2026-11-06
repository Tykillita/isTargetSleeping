using System.Diagnostics;

namespace IsTargetSleeping;

/// Retomar una sesión de Claude Code o Codex en una terminal de verdad (Windows Terminal si está, si no PowerShell), en la carpeta del proyecto y
/// con `claude --resume <id>` / `codex resume <id>`. A diferencia de `AgentResume` (una pregunta en solo lectura), aquí sigues tú en el CLI.
/// Solo para sesiones que nadie usa ahora (dos escritores en el mismo archivo de sesión se pisarían).
public static class AgentTerminal
{
    public static bool CanOpen(AgentSnapshot session, Func<string, bool>? folderExists = null) => AgentResume.CanResume(session, folderExists);

    /// Lo que se ejecuta dentro de la terminal. El id es siempre un GUID (lo garantiza `ResumeId`), así que no hay nada que escapar.
    public static string CliLine(AgentSnapshot session) => session.IsCodex ? $"codex resume {session.ResumeId}" : $"claude --resume {session.ResumeId}";

    /// El archivo y los argumentos para lanzarlo. Con Windows Terminal: `wt -d <carpeta> powershell -NoExit -Command <cli>`; sin él, PowerShell directo
    /// con `Set-Location` (las comillas simples de la ruta se duplican).
    public static (string File, string Args) Command(AgentSnapshot session, bool windowsTerminal)
    {
        var cli = CliLine(session);
        return windowsTerminal
            ? ("wt.exe", $"-d \"{session.Project}\" powershell.exe -NoExit -Command \"{cli}\"")
            : ("powershell.exe", $"-NoExit -Command \"Set-Location -LiteralPath '{session.Project.Replace("'", "''")}'; {cli}\"");
    }

    /// Abre la terminal. Devuelve false si no se pudo (sin PowerShell, o el sistema la rechazó).
    public static bool Open(AgentSnapshot session)
    {
        if (!CanOpen(session)) return false;
        bool wt = TeamProcess.Find("wt") is not null || File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe"));
        var (file, args) = Command(session, wt);
        try { Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true, WorkingDirectory = session.Project }); return true; }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
    }
}
