using IsTargetSleeping;

internal static class TeamBreakerTests
{
    public static void Run(Action<string, bool> check)
    {
        var s = new TeamSchedule { Id = "b1", Title = "Pruebas", Prompt = "x", Root = @"C:\p", Kind = ScheduleKind.EveryHours, EveryHours = 1 };
        for (int i = 1; i <= TeamSchedule.FailureLimit - 1; i++) s = s.WithOutcome("Failed");
        check("nueve fallos seguidos todavía no la apagan", s.Enabled && s.ConsecutiveFailures == 9 && !s.PausedByFailures);
        s = s.WithOutcome("Failed");
        check("el décimo fallo seguido la apaga sola", !s.Enabled && s.ConsecutiveFailures == 10 && s.PausedByFailures && s.LastOutcome == "Failed");

        var recovering = new TeamSchedule { Id = "b2", Prompt = "x", Root = @"C:\p" };
        for (int i = 0; i < 6; i++) recovering = recovering.WithOutcome("Failed");
        recovering = recovering.WithOutcome("Done");
        check("un éxito pone la cuenta a cero", recovering.ConsecutiveFailures == 0 && recovering.Enabled);
        recovering = recovering.WithOutcome("Failed").WithOutcome("Cancelled").WithOutcome("Missed");
        check("cancelar o perderse una ejecución no suma ni resta fallos", recovering.ConsecutiveFailures == 1 && recovering.LastOutcome == "Missed");

        var back = s.Toggled();
        check("reactivarla a mano empieza de cero", back.Enabled && back.ConsecutiveFailures == 0 && !back.PausedByFailures);
        check("apagarla a mano no toca la cuenta", back.WithOutcome("Failed").Toggled() is { Enabled: false, ConsecutiveFailures: 1, PausedByFailures: false });

        var dir = Path.Combine(Path.GetTempPath(), "its-breaker-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new TeamStore(dir);
            store.UpsertSchedule(s);
            var loaded = store.LoadSchedules().Single(x => x.Id == "b1");
            check("la cuenta de fallos se guarda con la tarea", loaded is { ConsecutiveFailures: 10, Enabled: false, PausedByFailures: true });
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        check("una tarea cada hora ya respeta un mínimo de una hora entre ejecuciones", new TeamSchedule { Kind = ScheduleKind.EveryHours, EveryHours = 0 }.NextAfter(new DateTime(2026, 11, 4, 12, 0, 0)) == new DateTime(2026, 11, 4, 13, 0, 0));
    }
}
