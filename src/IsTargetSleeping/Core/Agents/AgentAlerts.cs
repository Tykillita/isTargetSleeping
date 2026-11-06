namespace IsTargetSleeping;

public enum AgentAlertKind { Stalled, Loop, LimitNear, LimitHit, RoutinePaused }

/// Una ventana de uso de un proveedor (la «5 h» o la semanal de Claude, por ejemplo), con cuánto lleva gastado (0 a 1) y cuándo se reinicia.
public sealed record LimitInfo(string Provider, string Label, double Fraction, DateTimeOffset? ResetsAt);

/// Algo que merece tu atención de un agente aunque no te haya pedido nada. `Key` identifica el aviso para no repetirlo mientras dure.
public sealed record AgentAlert(AgentAlertKind Kind, string Key, string Who, string Subject, int Number, DateTimeOffset? Until);

/// Detecta, a partir de lo que ya se lee de los logs y del uso de IA: un agente que lleva rato sin dar señales (atascado o esperando un comando largo),
/// uno que repite lo mismo con el mismo archivo y un límite de uso que se acaba o ya se acabó (con cuándo vuelve). Es una función pura: no toca nada.
public static class AgentAlerts
{
    public static readonly TimeSpan StallAfter = TimeSpan.FromMinutes(2);
    public const int LoopAt = 6;
    public const double NearAt = 0.9;

    public static IReadOnlyList<AgentAlert> Detect(IEnumerable<AgentSnapshot> sessions, IEnumerable<LimitInfo> limits, DateTime now)
    {
        var alerts = new List<AgentAlert>();
        var utc = now.ToUniversalTime();
        foreach (var s in sessions)
        {
            if (s.State != AgentState.Working) continue;
            var age = utc - s.LastActivity.ToUniversalTime();
            // Desde los 5 minutos la sesión pasa a «sin actividad»: ahí el aviso ya lo da su propio estado.
            if (age >= StallAfter && age < AgentLogs.InactiveAfter)
                alerts.Add(new(AgentAlertKind.Stalled, $"stall|{AgentLogs.Key(s)}", s.Name, "", (int)age.TotalMinutes, null));
            if (s.RepeatCount >= LoopAt)
                alerts.Add(new(AgentAlertKind.Loop, $"loop|{AgentLogs.Key(s)}|{s.RepeatSubject}", s.Name, s.RepeatSubject ?? "", s.RepeatCount, null));
        }
        foreach (var l in limits)
        {
            if (l.Fraction >= 1)
                alerts.Add(new(AgentAlertKind.LimitHit, $"limit-hit|{l.Provider}|{l.Label}", l.Provider, l.Label, 100, l.ResetsAt));
            else if (l.Fraction >= NearAt)
                alerts.Add(new(AgentAlertKind.LimitNear, $"limit-near|{l.Provider}|{l.Label}", l.Provider, l.Label, (int)Math.Round(l.Fraction * 100), l.ResetsAt));
        }
        return alerts;
    }

    /// «en 12 min» / «en 1 h 5 min»: lo que falta para que vuelva a funcionar.
    public static string Countdown(DateTimeOffset? until, DateTimeOffset now)
    {
        if (until is not { } at || at <= now) return "";
        var left = at - now;
        int minutes = Math.Max(1, (int)Math.Ceiling(left.TotalMinutes));
        return minutes < 60 ? L10n.tr("%d min", minutes)
            : minutes % 60 == 0 ? L10n.tr("%d h", minutes / 60) : L10n.tr("%d h %d min", minutes / 60, minutes % 60);
    }
}
