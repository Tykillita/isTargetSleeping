using IsTargetSleeping;

internal static class AgentNotifyTests
{
    public static void Run(Action<string, bool> check)
    {
        var now = new DateTime(2026, 11, 4, 12, 0, 0);
        bool Allows(AgentNotifyLevel level, AgentNotifyKind kind, DateTime? snooze = null) => AgentNotifyPolicy.Allows(level, snooze, now, kind);
        var kinds = Enum.GetValues<AgentNotifyKind>();

        check("«Todo» deja pasar todo", kinds.All(k => Allows(AgentNotifyLevel.All, k)));
        check("«Nada» no deja pasar nada", kinds.All(k => !Allows(AgentNotifyLevel.Nothing, k)));
        check("«Solo lo importante» deja permisos, preguntas, planes, avisos graves y el resumen",
            new[] { AgentNotifyKind.Permission, AgentNotifyKind.Question, AgentNotifyKind.Plan, AgentNotifyKind.AlertSevere, AgentNotifyKind.AwayDigest }.All(k => Allows(AgentNotifyLevel.Important, k)));
        check("«Solo lo importante» calla los avisos suaves, el «terminó» y la charla ambiente",
            new[] { AgentNotifyKind.AlertMild, AgentNotifyKind.Finished, AgentNotifyKind.Ambient }.All(k => !Allows(AgentNotifyLevel.Important, k)));
        check("posponer pone todo en silencio hasta la hora", !Allows(AgentNotifyLevel.All, AgentNotifyKind.Permission, now.AddMinutes(30)));
        check("pasada la hora, vuelve al nivel de siempre", Allows(AgentNotifyLevel.All, AgentNotifyKind.Permission, now.AddMinutes(-1)));
        check("el nivel efectivo con posponer es «Nada»", AgentNotifyPolicy.Effective(AgentNotifyLevel.All, now.AddHours(1), now) == AgentNotifyLevel.Nothing);

        check("se guarda y se lee el nivel", AgentNotifyPolicy.Parse(AgentNotifyPolicy.Key(AgentNotifyLevel.Important)) == AgentNotifyLevel.Important
            && AgentNotifyPolicy.Parse("nothing") == AgentNotifyLevel.Nothing && AgentNotifyPolicy.Parse(null) == AgentNotifyLevel.All && AgentNotifyPolicy.Parse("x") == AgentNotifyLevel.All);
        check("cada tipo de petición y de aviso tiene su clase", AgentNotifyPolicy.KindOf(HookKind.Question) == AgentNotifyKind.Question && AgentNotifyPolicy.KindOf(HookKind.Plan) == AgentNotifyKind.Plan
            && AgentNotifyPolicy.KindOf(HookKind.Permission) == AgentNotifyKind.Permission && AgentNotifyPolicy.KindOf(AgentAlertKind.Loop) == AgentNotifyKind.AlertSevere
            && AgentNotifyPolicy.KindOf(AgentAlertKind.LimitHit) == AgentNotifyKind.AlertSevere && AgentNotifyPolicy.KindOf(AgentAlertKind.Stalled) == AgentNotifyKind.AlertMild
            && AgentNotifyPolicy.KindOf(AgentAlertKind.LimitNear) == AgentNotifyKind.AlertMild && AgentNotifyPolicy.KindOf(AgentAlertKind.RoutinePaused) == AgentNotifyKind.AlertSevere);
        check("«hasta mañana» es las 8 de la mañana siguiente", AgentNotifyPolicy.TomorrowMorning(now) == new DateTime(2026, 11, 5, 8, 0, 0)
            && AgentNotifyPolicy.TomorrowMorning(new DateTime(2026, 11, 4, 3, 0, 0)) == new DateTime(2026, 11, 4, 8, 0, 0));
    }
}
