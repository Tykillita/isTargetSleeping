namespace IsTargetSleeping;

/// Seguir una sesión real de Claude Code o Codex que ya no trabaja: se reanuda con el CLI (`claude --resume`, `codex exec resume`) y el turno
/// nuevo se añade al propio archivo de la sesión, así que el widget Agentes de IA lo recoge solo. Va en solo lectura (preguntar por lo ya hecho,
/// no volver a tocar el proyecto): Claude con herramientas que no escriben y Codex con la sandbox de solo lectura.
public static class AgentResume
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    /// Se puede seguir si la sesión no la está usando nadie ahora (ni trabajando ni esperando al usuario: dos escritores en el mismo archivo),
    /// no es una automatización, tiene un id que el CLI entienda y su carpeta de trabajo sigue existiendo.
    public static bool CanResume(AgentSnapshot session, Func<string, bool>? folderExists = null) =>
        !session.IsAutomation && session.State is AgentState.Finished or AgentState.Inactive && session.ResumeId is not null
        && Path.IsPathRooted(session.Project) && (folderExists ?? Directory.Exists)(session.Project);

    /// Los argumentos del CLI (la pregunta va por la entrada estándar).
    public static IReadOnlyList<string> Args(AgentSnapshot session) => session.IsCodex
        ? ["exec", "resume", "--json", "--skip-git-repo-check", "-c", "sandbox_mode=\"read-only\"", session.ResumeId!, "-"]
        : ["-p", "--resume", session.ResumeId!, "--output-format", "json", "--permission-mode", "default", "--allowedTools", "Read,Grep,Glob,WebSearch,WebFetch"];

    public static async Task<AgentResult> RunAsync(AgentSnapshot session, string text, CancellationToken ct = default)
    {
        var cli = session.IsCodex ? "codex" : "claude";
        if (session.ResumeId is null) return new(false, "", Error: "La sesión no tiene un id que se pueda reanudar");
        if (TeamProcess.Find(cli) is not { } exe) return new(false, "", Error: session.IsCodex ? "Codex no está instalado" : "Claude Code no está instalado");
        try
        {
            var r = await TeamProcess.RunAsync(exe, Args(session), text, session.Project, Timeout, ct);
            if (r.TimedOut) return new(false, "", Error: $"No respondió en {Timeout.TotalMinutes:0} min");
            var parsed = session.IsCodex ? CodexRunner.Parse(r.Out.Split('\n').Select(l => l.Trim())) : ClaudeRunner.Parse(r.Out);
            return parsed.Ok || r.Exit == 0 ? parsed : parsed with { Error = parsed.Error ?? r.Err.Trim() };
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return new(false, "", Error: e.Message);
        }
    }
}
