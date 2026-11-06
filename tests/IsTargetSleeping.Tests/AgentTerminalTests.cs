using IsTargetSleeping;

internal static class AgentTerminalTests
{
    private const string Id = "01234567-89ab-cdef-0123-456789abcdef";

    private static AgentSnapshot Session(AgentState state, string provider = "Claude Code", string project = @"C:\dev\app", string id = Id) =>
        new(id, project, provider, state, null, null, null, null, null, [], DateTime.UtcNow);

    public static void Run(Action<string, bool> check)
    {
        bool Exists(string _) => true;
        check("una sesión terminada se puede retomar en una terminal", AgentTerminal.CanOpen(Session(AgentState.Finished), Exists));
        check("una sesión que trabaja no (dos escritores en el mismo archivo)", !AgentTerminal.CanOpen(Session(AgentState.Working), Exists));
        check("sin un id que el CLI entienda no", !AgentTerminal.CanOpen(Session(AgentState.Finished, id: "no-es-un-guid"), Exists));
        check("si la carpeta ya no existe no", !AgentTerminal.CanOpen(Session(AgentState.Finished), _ => false));

        var claude = Session(AgentState.Finished);
        check("Claude Code se retoma con --resume", AgentTerminal.CliLine(claude) == $"claude --resume {Id}");
        check("Codex se retoma con resume", AgentTerminal.CliLine(Session(AgentState.Finished, "Codex")) == $"codex resume {Id}");

        var (wtFile, wtArgs) = AgentTerminal.Command(claude, windowsTerminal: true);
        check("con Windows Terminal abre en la carpeta del proyecto y deja la ventana abierta", wtFile == "wt.exe" && wtArgs == $"-d \"C:\\dev\\app\" powershell.exe -NoExit -Command \"claude --resume {Id}\"");
        var (psFile, psArgs) = AgentTerminal.Command(Session(AgentState.Finished, project: @"C:\dev\it's app"), windowsTerminal: false);
        check("sin Windows Terminal usa PowerShell y escapa las comillas simples de la ruta", psFile == "powershell.exe" && psArgs.Contains("'C:\\dev\\it''s app'") && psArgs.EndsWith($"claude --resume {Id}\""));
    }
}
