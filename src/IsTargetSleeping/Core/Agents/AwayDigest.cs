namespace IsTargetSleeping;

public enum AwayKind { AgentDone, PermissionAsked, Alert }

/// Lo que pasó con tus agentes mientras estuviste fuera: cuántos terminaron, cuántos permisos pidieron, cuántos avisos saltaron y cuántas peticiones siguen esperándote.
public sealed record AwaySummary(int AgentsDone, int Permissions, int Alerts, int StillPending, TimeSpan Away)
{
    public bool Any => AgentsDone + Permissions + Alerts + StillPending > 0;
}

/// Detecta que te fuiste (sin teclado ni ratón un buen rato) y que volviste, y cuenta lo que pasó entremedias. Lógica pura: el que la usa le da
/// los segundos de inactividad y los eventos; al volver devuelve el resumen (o null si no pasó nada que contar).
public sealed class AwayTracker
{
    /// Sin entrada del usuario durante tanto se considera que se fue.
    public const double AwayAfter = 600;
    /// Por debajo de esto, ha vuelto.
    public const double ReturnBelow = 3;

    private bool away;
    private DateTime awaySince;
    private int done, permissions, alerts;

    public bool IsAway => away;

    public void Record(AwayKind kind)
    {
        if (!away) return;
        switch (kind)
        {
            case AwayKind.AgentDone: done++; break;
            case AwayKind.PermissionAsked: permissions++; break;
            default: alerts++; break;
        }
    }

    /// Avanza el reloj. Devuelve el resumen justo en el momento en que vuelves, si hay algo que contar.
    public AwaySummary? Update(double idleSeconds, int stillPending, DateTime now)
    {
        if (!away)
        {
            if (idleSeconds < AwayAfter) return null;
            away = true;
            awaySince = now - TimeSpan.FromSeconds(idleSeconds);
            done = permissions = alerts = 0;
            return null;
        }
        if (idleSeconds >= ReturnBelow) return null;
        away = false;
        var summary = new AwaySummary(done, permissions, alerts, stillPending, now - awaySince);
        return summary.Any ? summary : null;
    }
}
