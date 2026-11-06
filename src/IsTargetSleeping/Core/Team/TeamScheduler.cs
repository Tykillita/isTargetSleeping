namespace IsTargetSleeping;

/// Lanza las tareas programadas cuando les toca. No tiene reloj propio: alguien llama a <see cref="Tick"/> (el widget cada pocos
/// segundos mientras la app está abierta, o `--team-run` desde el Programador de tareas de Windows). Lee de disco en cada vuelta, así que
/// ve los cambios de otro proceso y no repite una tarea que ya lanzó el otro.
/// Reglas de seguridad de lo que corre sin que nadie mire: nunca fusiona, siempre con tope de tokens, sin pedir aprobación del plan,
/// y una tarea que sigue en marcha o en cola no se apila otra vez.
public sealed class TeamScheduler(TeamService service, Func<IReadOnlyList<CodexModel>>? models = null)
{
    public enum Launch { Started, SkippedMissed, SkippedBusy, SkippedInvalid, StorageError }

    public static TeamPreset PresetOf(TeamSchedule schedule) => Enum.TryParse<TeamPreset>(schedule.Preset, out var p) ? p : TeamPreset.Auto;

    /// La tarea ya enrutada, como la lanzaría el usuario a mano.
    public TeamJob BuildJob(TeamSchedule schedule)
    {
        var profile = TeamRouter.Profile(schedule.Prompt);
        var plan = TeamRouter.Route(profile, PresetOf(schedule));
        var list = models?.Invoke() ?? [];
        return new TeamJob(schedule.Prompt.Trim(), schedule.Root, plan.Preset, [.. plan.Steps.Select(s => CodexModels.Apply(s, list))], profile)
        {
            MaxTokens = schedule.MaxTokens,
            ScheduleId = schedule.Id,
            ScheduleTitle = schedule.Title,
        };
    }

    /// Mira todas las programadas y lanza las que tocan. Devuelve lo que hizo con cada una (solo las que no estaban «todavía no»).
    public List<(TeamSchedule Schedule, Launch Result)> Tick(DateTime now)
    {
        var results = new List<(TeamSchedule, Launch)>();
        foreach (var schedule in service.Store.LoadSchedules())
        {
            var check = schedule.Check(now);
            if (check == ScheduleCheck.NotYet) continue;
            if (service.IsQueuedOrRunning(schedule.Id)) { results.Add((schedule, Launch.SkippedBusy)); continue; }
            results.Add((schedule, Start(schedule.Id, now, runNow: false)));
        }
        return results;
    }

    /// «Ejecutar ahora»: sin esperar a su hora.
    public Launch RunNow(string id, DateTime now) => Start(id, now, runNow: true);

    private Launch Start(string id, DateTime now, bool runNow)
    {
        if (service.IsQueuedOrRunning(id)) return Launch.SkippedBusy;
        var claim = service.Store.ClaimSchedule(id, now, runNow, out var schedule, out var lease);
        if (claim == ScheduleClaimResult.Busy || claim == ScheduleClaimResult.NotDue) return Launch.SkippedBusy;
        if (claim == ScheduleClaimResult.Missed) return Launch.SkippedMissed;
        if (claim == ScheduleClaimResult.StorageError) return Launch.StorageError;
        if (claim == ScheduleClaimResult.NotFound || schedule is null) return Launch.SkippedInvalid;
        if (schedule.Prompt.Trim().Length == 0 || !Directory.Exists(schedule.Root) || !TeamWorktree.IsRepo(schedule.Root))
        {
            var saved = service.Store.UpsertSchedule(schedule.WithOutcome("Failed"));
            lease?.Dispose();
            if (!saved) return Launch.StorageError;
            return Launch.SkippedInvalid;
        }
        try
        {
            service.Enqueue(BuildJob(schedule) with { ScheduleLease = lease });
            return Launch.Started;
        }
        catch
        {
            lease?.Dispose();
            throw;
        }
    }
}
