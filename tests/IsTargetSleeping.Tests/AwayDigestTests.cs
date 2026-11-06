using IsTargetSleeping;

internal static class AwayDigestTests
{
    public static void Run(Action<string, bool> check)
    {
        var t = new DateTime(2026, 11, 4, 12, 0, 0);
        var tracker = new AwayTracker();
        check("con el usuario activo no está ausente", tracker.Update(5, 0, t) is null && !tracker.IsAway);
        check("tras 9 minutos sin tocar nada aún no se fue", tracker.Update(540, 0, t) is null && !tracker.IsAway);
        tracker.Record(AwayKind.AgentDone);
        check("lo que pasa antes de irse no cuenta", tracker.Update(601, 0, t.AddSeconds(61)) is null && tracker.IsAway);
        tracker.Record(AwayKind.AgentDone);
        tracker.Record(AwayKind.AgentDone);
        tracker.Record(AwayKind.PermissionAsked);
        tracker.Record(AwayKind.Alert);
        check("durante la ausencia no devuelve nada", tracker.Update(900, 0, t.AddMinutes(10)) is null);
        var back = tracker.Update(0.5, 1, t.AddMinutes(12));
        check("al volver cuenta lo que pasó", back is { AgentsDone: 2, Permissions: 1, Alerts: 1, StillPending: 1, Any: true });
        check("dice cuánto estuvo fuera", back!.Away >= TimeSpan.FromMinutes(11));
        check("después de volver ya no está ausente y no repite el resumen", !tracker.IsAway && tracker.Update(0.5, 0, t.AddMinutes(13)) is null);

        var quiet = new AwayTracker();
        quiet.Update(700, 0, t);
        check("si no pasó nada, no hay resumen", quiet.Update(1, 0, t.AddMinutes(5)) is null);
        var waiting = new AwayTracker();
        waiting.Update(700, 0, t);
        check("una petición que sigue esperando cuenta aunque no se anotara nada", waiting.Update(1, 2, t.AddMinutes(5)) is { StillPending: 2 });
        var short_ = new AwayTracker();
        short_.Update(100, 0, t);
        short_.Record(AwayKind.AgentDone);
        check("un descanso corto no cuenta como ausencia", short_.Update(0.2, 0, t.AddSeconds(100)) is null);
        var again = new AwayTracker();
        again.Update(700, 0, t); again.Record(AwayKind.Alert); again.Update(0, 0, t.AddMinutes(1));
        again.Update(700, 0, t.AddMinutes(30));
        check("cada ausencia empieza con la cuenta a cero", again.Update(0, 0, t.AddMinutes(40)) is null);
    }
}
