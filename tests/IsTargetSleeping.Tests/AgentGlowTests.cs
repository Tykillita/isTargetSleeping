using IsTargetSleeping;

internal static class AgentGlowTests
{
    public static void Run(Action<string, bool> check)
    {
        var now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        AgentGlowState Of(bool pending = false, bool alert = false, bool attention = false, bool working = false, DateTime? finished = null) =>
            AgentGlowRules.Of(pending, alert, attention, working, finished, now);

        check("sin nada que contar, la luz está apagada", Of() == AgentGlowState.Off);
        check("trabajando: azul", Of(working: true) == AgentGlowState.Working);
        check("un permiso pendiente: ámbar, por encima de todo", Of(pending: true, alert: true, working: true) == AgentGlowState.Attention);
        check("una sesión que pide atención: ámbar", Of(attention: true, working: true) == AgentGlowState.Attention);
        check("un aviso (atasco, bucle, límite): rojo", Of(alert: true, working: true) == AgentGlowState.Alert);
        check("con aviso y atención a la vez, manda el aviso", Of(alert: true, attention: true) == AgentGlowState.Alert);
        check("terminó hace 3 s: destello verde", Of(finished: now.AddSeconds(-3)) == AgentGlowState.Done);
        check("terminó hace 20 s: ya no", Of(finished: now.AddSeconds(-20)) == AgentGlowState.Off);
        check("si sigue trabajando, el azul manda sobre el destello", Of(working: true, finished: now.AddSeconds(-1)) == AgentGlowState.Working);
        check("un «terminó» del futuro no cuenta", Of(finished: now.AddSeconds(5)) == AgentGlowState.Off);
        check("cada estado tiene su color y todos son distintos", new[] { AgentGlowState.Working, AgentGlowState.Attention, AgentGlowState.Alert, AgentGlowState.Done }
            .Select(AgentGlowRules.Color).Distinct().Count() == 4);
        check("la urgencia late más rápido que el trabajo", AgentGlowRules.PulseSeconds(AgentGlowState.Attention) < AgentGlowRules.PulseSeconds(AgentGlowState.Working)
            && AgentGlowRules.PulseSeconds(AgentGlowState.Done) == 0);
    }
}
