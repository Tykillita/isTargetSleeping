using IsTargetSleeping;

internal static class CleanCoordinationTests
{
    public static void Run(Action<string, bool> check)
    {
        var start = new DateTime(2026, 10, 1, 10, 0, 0);
        var critical = new CleanRuleTracker(start) { OnCritical = true, ThresholdPercent = 60, IntervalMinutes = 3 };
        check("presión crítica se atiende desde el inicio", critical.Sample(98, true, start) == CleanReason.Critical);
        check("no inicia otra solicitud mientras la primera está activa", critical.Sample(98, true, start.AddMinutes(10)) is null);
        critical.Completed(start, true);
        check("éxito consume umbral y crítica coincidentes y respeta pausa", critical.Sample(98, true, start.AddMinutes(2)) is null);
        check("intervalo se calcula desde la limpieza completada", critical.Sample(98, true, start.AddMinutes(3)) == CleanReason.Interval);
        critical.Completed(start.AddMinutes(3), true);

        var priority = new CleanRuleTracker(start) { OnCritical = true, ThresholdPercent = 60, IntervalMinutes = 3 };
        check("crítica tiene prioridad sobre umbral e intervalo", priority.Sample(98, true, start.AddMinutes(3)) == CleanReason.Critical);
        priority.Completed(start.AddMinutes(3), true);
        var threshold = new CleanRuleTracker(start) { ThresholdPercent = 60, IntervalMinutes = 3 };
        check("umbral tiene prioridad sobre intervalo", threshold.Sample(86, false, start.AddMinutes(3)) == CleanReason.Threshold);

        var gate = new CleanRuleTracker(start) { OnCritical = true, ThresholdPercent = 60, IntervalMinutes = 3 };
        check("compromiso crítico sin presión física no ejecuta reglas", gate.Sample(70, true, start.AddHours(1), physicalHigh: false) is null);
        check("presión física alta permite el umbral guardado", gate.Sample(86, false, start.AddHours(1)) == CleanReason.Threshold);

        foreach (var reason in new[] { CleanReason.Critical, CleanReason.Threshold, CleanReason.Interval })
        {
            var disabled = new CleanRuleTracker(start)
            { OnCritical = reason == CleanReason.Critical, ThresholdPercent = reason == CleanReason.Threshold ? 60 : 0, IntervalMinutes = reason == CleanReason.Interval ? 3 : 0 };
            disabled.Sample(98, reason == CleanReason.Critical, start.AddMinutes(3));
            disabled.Completed(start.AddMinutes(3), false);
            disabled.OnCritical = false; disabled.ThresholdPercent = disabled.IntervalMinutes = 0;
            check($"desactivar {reason} cancela su reintento", disabled.Sample(98, true, start.AddMinutes(10)) is null);
        }
        var escalation = new CleanRuleTracker(start) { IntervalMinutes = 3, OnCritical = true };
        escalation.Sample(86, false, start.AddMinutes(3));
        escalation.Completed(start.AddMinutes(3), false);
        check("una presión crítica nueva tiene prioridad sobre el reintento de intervalo", escalation.Sample(98, true, start.AddMinutes(6)) == CleanReason.Critical);

        var retry = new CleanRuleTracker(start) { OnCritical = true };
        retry.Sample(98, true, start);
        retry.Completed(start, false);
        check("el intento fallido no repite antes de tres minutos", retry.Sample(98, true, start.AddMinutes(2.99)) is null);
        check("el segundo intento comienza a los tres minutos", retry.Sample(98, true, start.AddMinutes(3)) == CleanReason.Critical);
        retry.Completed(start.AddMinutes(3), false);
        check("el segundo fallo espera seis minutos", retry.Sample(98, true, start.AddMinutes(8.99)) is null);
        check("el tercer intento comienza tras seis minutos", retry.Sample(98, true, start.AddMinutes(9)) == CleanReason.Critical);
        retry.Completed(start.AddMinutes(9), false);
        check("tres fallos agotan el episodio", retry.Sample(98, true, start.AddHours(2)) is null);
        retry.Sample(80, false, start.AddHours(2), physicalHigh: false);
        check("la recuperación física rearma tras tres fallos", retry.Sample(98, true, start.AddHours(2).AddSeconds(1)) == CleanReason.Critical);

        var noWork = new CleanRuleTracker(start) { OnCritical = true };
        noWork.Sample(98, true, start);
        noWork.Completed(start, new CleanResult(0, CleanAreas.None, null) { Outcome = CleanOutcome.NoWork }.Completed);
        check("sin trabajo consume el disparador sin entrar en bucle", noWork.Sample(98, true, start.AddHours(1)) is null);
        noWork.Attempted(start.AddHours(2));
        noWork.Completed(start.AddHours(2), true);
        check("una petición manual completada mantiene la pausa automática", !noWork.CanAttempt(start.AddHours(2).AddSeconds(179)));

        var game = new GameModeTracker();
        game.Sample("Test game", start); game.Sample("Test game", start.AddSeconds(5));
        game.Stopped(["ollama", "llamacpp"]);
        game.ForceExit();
        game.Touch("ollama");
        check("una finalización confirmada durante salida aplazada no restaura ese motor", game.ToRestore(_ => false).SequenceEqual(["llamacpp"]));
        var failedGame = new GameModeTracker();
        failedGame.Sample("Test game", start); failedGame.Sample("Test game", start.AddSeconds(5));
        failedGame.Stopped(["ollama"]); failedGame.ForceExit();
        check("una finalización fallida conserva la intención anterior de restaurar", failedGame.ToRestore(_ => false).SequenceEqual(["ollama"]));
        check("modo juego espera confirmación del apagado", GameCleanGate.Decide(true, true, false, false, TimeSpan.FromSeconds(29)) == GameCleanDecision.Wait);
        check("modo juego permite limpiar con apagado confirmado", GameCleanGate.Decide(true, true, false, true, TimeSpan.FromSeconds(29)) == GameCleanDecision.Ready);
        check("modo juego cancela al terminar el juego", GameCleanGate.Decide(false, true, false, true, TimeSpan.Zero) == GameCleanDecision.Cancelled);
        check("modo juego cancela al cambiar la opción", GameCleanGate.Decide(true, false, false, true, TimeSpan.Zero) == GameCleanDecision.Cancelled);
        check("modo juego cancela ante fallo del apagado", GameCleanGate.Decide(true, true, true, true, TimeSpan.Zero) == GameCleanDecision.Cancelled);
        check("modo juego cancela al vencer treinta segundos", GameCleanGate.Decide(true, true, false, true, TimeSpan.FromSeconds(30)) == GameCleanDecision.Cancelled);

        var clock = DateTime.Now;
        var store = new StatsStore(null);
        var five = new CleanMemorySample(DateTimeOffset.Now, 1_100, 900, 500, 2_000, 55, false);
        var report = new CleanResult(-100, CleanAreas.None, null)
        {
            RequestId = Guid.NewGuid(), Outcome = CleanOutcome.Success, AfterFiveSeconds = five,
            Before = five with { Used = 1_000, Available = 1_000 },
        };
        store.Add(new(clock, StatKind.Clean, Bytes: -100, Clean: report));
        store.Add(new(clock, StatKind.Clean, Bytes: 999, Clean: report with { RequestId = Guid.NewGuid(), Outcome = CleanOutcome.Failed }));
        store.Add(new(clock, StatKind.Clean, Bytes: 999, Clean: report with { RequestId = Guid.NewGuid(), Outcome = CleanOutcome.NoWork }));
        check("el agregado conserva cambios negativos sin contar fallos ni ausencia de trabajo", store.Week(clock).Cleaned == -100 && store.Week(clock).Cleans == 1);
        store.UpdateClean(report with { AfterThirtySeconds = five with { Used = 800 } });
        check("la observación a treinta segundos conserva el agregado de cinco segundos", store.Week(clock).Cleaned == -100 && store.Last(StatKind.Clean) != null);

        string history = Path.Combine(Path.GetTempPath(), $"ist-history-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(history, $$$"""{"Events":[{"At":"{{{clock:O}}}","Kind":"Clean","Bytes":2048,"Detail":"manual"}],"Loaded":{}}""");
            var old = new StatsStore(history);
            check("el historial anterior sin contratos nuevos sigue cargando", old.Week(clock).Cleaned == 2048 && old.Recent(1)[0].Clean == null);
            old.Add(new(clock, StatKind.Clean, Bytes: -100, Clean: report));
            var reloaded = new StatsStore(history);
            check("conviven informes nuevos y eventos anteriores al guardar", reloaded.Week(clock).Cleaned == 1948 && reloaded.Recent(5).Any(e => e.Clean?.RequestId == report.RequestId));
        }
        finally { File.Delete(history); }
    }
}
