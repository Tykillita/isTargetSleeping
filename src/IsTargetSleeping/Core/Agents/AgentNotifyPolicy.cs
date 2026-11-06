namespace IsTargetSleeping;

public enum AgentNotifyLevel { All, Important, Nothing }
public enum AgentNotifyKind { Permission, Question, Plan, AlertSevere, AlertMild, Finished, AwayDigest, Ambient }

/// Cuánto te molestan tus agentes: «Todo» (permisos, preguntas, planes, avisos, que un agente termine y la charla ambiente), «Solo lo importante» (permisos, preguntas,
/// planes, avisos graves y el resumen al volver) o «Nada» (en silencio: lo que pidan queda en la página Agentes sin abrir el notch ni sonar). Posponer deja el nivel
/// en «Nada» hasta una hora. Lo que decide es solo si algo suena, suena la campana o se abre el notch: las peticiones y los avisos siempre están en la página Agentes.
public static class AgentNotifyPolicy
{
    public static AgentNotifyLevel Parse(string? value) => value switch { "important" => AgentNotifyLevel.Important, "nothing" => AgentNotifyLevel.Nothing, _ => AgentNotifyLevel.All };
    public static string Key(AgentNotifyLevel level) => level switch { AgentNotifyLevel.Important => "important", AgentNotifyLevel.Nothing => "nothing", _ => "all" };

    public static AgentNotifyLevel Effective(AgentNotifyLevel level, DateTime? snoozedUntil, DateTime now) =>
        snoozedUntil is { } until && now < until ? AgentNotifyLevel.Nothing : level;

    public static bool Allows(AgentNotifyLevel level, DateTime? snoozedUntil, DateTime now, AgentNotifyKind kind) => Effective(level, snoozedUntil, now) switch
    {
        AgentNotifyLevel.All => true,
        AgentNotifyLevel.Important => kind is AgentNotifyKind.Permission or AgentNotifyKind.Question or AgentNotifyKind.Plan or AgentNotifyKind.AlertSevere or AgentNotifyKind.AwayDigest,
        _ => false,
    };

    public static AgentNotifyKind KindOf(HookKind kind) => kind switch { HookKind.Question => AgentNotifyKind.Question, HookKind.Plan => AgentNotifyKind.Plan, _ => AgentNotifyKind.Permission };

    /// Los atascos y los límites que casi se acaban son un aviso suave; un bucle o un límite agotado, grave.
    public static AgentNotifyKind KindOf(AgentAlertKind kind) => kind is AgentAlertKind.Loop or AgentAlertKind.LimitHit or AgentAlertKind.RoutinePaused ? AgentNotifyKind.AlertSevere : AgentNotifyKind.AlertMild;

    /// «Hasta mañana»: las 8 de la mañana siguiente (o la de hoy, si aún no han dado).
    public static DateTime TomorrowMorning(DateTime now) => now.Hour < 8 ? now.Date.AddHours(8) : now.Date.AddDays(1).AddHours(8);
}
