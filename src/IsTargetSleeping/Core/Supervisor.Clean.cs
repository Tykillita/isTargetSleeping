using static IsTargetSleeping.L10n;

namespace IsTargetSleeping;

/// Liberar RAM (lo que hacía Mem Reduct): el agente con privilegios y las reglas automáticas.
public sealed partial class Supervisor
{
    public AgentStatus Agent { get; private set; } = AgentStatus.NotInstalled;
    public bool AgentBusy { get; private set; }
    public string? AgentError { get; private set; }
    public bool Cleaning { get; private set; }
    public (long Freed, DateTime At, CleanReason Reason)? LastClean { get; private set; }
    public CleanResult? LastCleanResult { get; private set; }
    private readonly CancellationTokenSource cleaningLifetime = new();
    private int gameCleanGeneration;

    private readonly CleanRuleTracker rules = new(DateTime.Now);
    private bool cleanDemo;

    public CleanAreas Areas
    {
        get => cleanDemo || !Defaults.Has(PrefKeys.CleanAreas) ? CleanAreas.Default : (CleanAreas)(Defaults.GetInt(PrefKeys.CleanAreas) & (int)CleanAreas.All);
        set { if (!cleanDemo) Defaults.Set(PrefKeys.CleanAreas, (int)value); Changed?.Invoke(); }
    }

    public int CleanThreshold
    {
        get => cleanDemo ? 60 : Defaults.GetInt(PrefKeys.CleanThreshold);
        set { if (!cleanDemo) Defaults.Set(PrefKeys.CleanThreshold, value); Changed?.Invoke(); }
    }

    public int CleanInterval
    {
        get => cleanDemo ? 0 : Defaults.GetInt(PrefKeys.CleanInterval);
        set { if (!cleanDemo) Defaults.Set(PrefKeys.CleanInterval, value); rules.ResetInterval(DateTime.Now); Changed?.Invoke(); }
    }

    /// Pausa mínima entre limpiezas por porcentaje mientras la RAM siga por encima (min).
    public int CleanCooldown
    {
        get => cleanDemo || !Defaults.Has(PrefKeys.CleanCooldown) ? CleanRuleTracker.DefaultCooldownMinutes
            : Math.Max(1, Defaults.GetInt(PrefKeys.CleanCooldown));
        set { if (!cleanDemo) Defaults.Set(PrefKeys.CleanCooldown, Math.Max(1, value)); Changed?.Invoke(); }
    }

    public void ToggleArea(CleanAreas area) => Areas ^= area;

    /// Estado del agente (disco y COM: fuera del hilo de la interfaz).
    public async Task RefreshCleaner()
    {
        if (cleanDemo) return;
        Agent = await Task.Run(MemoryAgent.Status);
        Changed?.Invoke();
    }

    // MARK: agente

    public async Task InstallAgent()
    {
        if (AgentBusy) return;
        AgentBusy = true;
        AgentError = null;
        Changed?.Invoke();
        var error = await Task.Run(MemoryAgent.Install);
        AgentError = error;
        AgentBusy = false;
        AppLog.Write(error is null ? "agente de memoria instalado" : $"agente de memoria: {error}");
        await RefreshCleaner();
    }

    public async Task UninstallAgent()
    {
        if (AgentBusy) return;
        AgentBusy = true;
        AgentError = null;
        Changed?.Invoke();
        AgentError = await Task.Run(MemoryAgent.Uninstall);
        AgentBusy = false;
        await Task.Delay(1500);   // el agente borra su carpeta un momento después de salir
        await RefreshCleaner();
    }

    // MARK: limpiar

    /// Procesos que no se tocan: los modelos (runners de Ollama, llama-server, LM
    /// Studio), el propio Ollama, el juego en curso y esta app.
    private List<int> ProtectedPids()
    {
        return [.. GetProcessProtectionPids()];
    }

    /// `fromPanel`: el botón del panel ya enseña el resultado; lo demás (atajo, enlace, reglas) avisa.
    public async Task Clean(CleanReason reason, bool fromPanel = false, bool sleepModels = false)
    {
        if (Cleaning || Processing || Ollama.IsDemo) return;
        if (Agent != AgentStatus.Ready)
        {
            if (reason == CleanReason.Manual)
                Ollama.Error = Agent == AgentStatus.Outdated
                    ? tr("Actualiza el agente de memoria en Ajustes › Liberar RAM (pide permiso de administrador).")
                    : tr("Activa la limpieza de RAM en Ajustes (pide permiso de administrador una vez).");
            return;
        }
        Cleaning = true;
        rules.Attempted(DateTime.Now);
        bool completed = false;
        Changed?.Invoke();
        try
        {
            if (sleepModels)
            {
                await Ollama.ReleaseAll();
                foreach (var e in Engines.Where(e => e.Power == Power.On && e.Model is not null)) await e.Sleep();
            }
            // Las automáticas limpian lo mismo que el botón: las zonas elegidas en Ajustes.
            var areas = Areas;
            var before = MemoryReading(SystemMemory.Current());
            var keep = await Task.Run(ProtectedPids);
            var identitySample = await new ProcessMonitor().CaptureAsync(keep.ToHashSet());
            var spec = new CleanSpec(areas, keep)
            {
                RequestId = Guid.NewGuid(),
                KeepIdentities = identitySample.Processes.Where(p => keep.Contains(p.Pid) && p.Identity.IsValid)
                    .Select(p => p.Identity).ToArray(),
            };
            var result = await Task.Run(() => MemoryAgent.Run(spec));
            while (result.Outcome == CleanOutcome.Pending && result.Completion is { } pending)
            {
                LastCleanResult = result;
                Changed?.Invoke();
                result = await pending;
            }
            result = result with { Before = result.Before ?? before,
                Immediate = result.Immediate ?? MemoryReading(SystemMemory.Current()) };
            LastCleanResult = result;
            var detail = reason.ToString().ToLowerInvariant();
            if (result.Outcome == CleanOutcome.Pending)
            {
                LastCleanResult = result;
                Ollama.Error = tr("El agente sigue activo. La limpieza permanece pendiente.");
                return;
            }
            completed = result.Completed;
            if (result.Outcome == CleanOutcome.Failed)
            {
                LastCleanResult = result;
                AppLog.Write($"limpieza ({detail}) falló: {result.Error ?? string.Join(", ", result.Errors.Select(e => e.Operation))}");
                if (reason == CleanReason.Manual) Ollama.Error = result.Error ?? tr("Fallaron las operaciones de limpieza. Revisa el resultado y el log.");
            }
            await Task.Delay(TimeSpan.FromSeconds(5), cleaningLifetime.Token);
            var five = MemoryReading(SystemMemory.Current());
            result = result with { AfterFiveSeconds = five, Freed = result.Before!.Used - five.Used };
            if (completed) LastClean = (result.Freed, DateTime.Now, reason);
            LastCleanResult = result;
            Ollama.Stats.Add(new StatEvent(DateTime.Now, StatKind.Clean, Bytes: result.Freed, Detail: detail, Clean: result));
            AppLog.Write($"limpieza ({detail}): {result.Outcome} · cambio observado {result.Freed.MemoryGB()} · zonas {areas} · tratados {result.ProcessesTreated} · fallaron {result.Failed}");
            if (!fromPanel && result.Outcome is CleanOutcome.Success or CleanOutcome.Partial)
                Notify?.Invoke(Notice.Clean, result.Outcome == CleanOutcome.Partial ? tr("Limpieza parcial") : tr("Limpieza terminada"),
                    tr("Cambio observado: %@ · %@", result.Freed.MemoryGB(), ReasonText(reason)));
            _ = ObserveThirtySeconds(result);
        }
        catch (OperationCanceledException) when (cleaningLifetime.IsCancellationRequested) { }
        catch (Exception e)
        {
            AppLog.Write($"limpieza: {e.Message}");
            if (reason == CleanReason.Manual) Ollama.Error = e.Message;
        }
        finally
        {
            rules.Completed(DateTime.Now, completed);
            Cleaning = LastCleanResult?.Outcome == CleanOutcome.Pending;
            _ = Ollama.Refresh();
            Changed?.Invoke();
        }
    }

    public static CleanMemorySample MemoryReading(SystemMemory memory) => new(DateTimeOffset.Now,
        memory.Used, memory.Available, memory.Committed, memory.CommitLimit,
        memory.Total > 0 ? (int)(100 * memory.Used / memory.Total) : 0,
        memory.PhysicalPressure == SystemMemory.Level.Critical);

    private async Task ObserveThirtySeconds(CleanResult result)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(25), cleaningLifetime.Token);
            result = result with { AfterThirtySeconds = MemoryReading(SystemMemory.Current()) };
            Ollama.Stats.UpdateClean(result);
            if (LastCleanResult?.RequestId == result.RequestId) LastCleanResult = result;
            Changed?.Invoke();
        }
        catch (OperationCanceledException) { }
    }

    public string ReasonText(CleanReason reason) => reason switch
    {
        CleanReason.Threshold => tr("la RAM pasó del %d %%", CleanThreshold),
        CleanReason.Interval => tr("cada %d min", CleanInterval),
        CleanReason.Critical => tr("presión crítica"),
        CleanReason.Game => tr("al empezar a jugar"),
        _ => tr("a mano"),
    };

    private async Task CheckCleanRules()
    {
        if (Agent != AgentStatus.Ready || Cleaning || Processing || Ollama.IsDemo) return;
        rules.ThresholdPercent = CleanThreshold;
        rules.ThresholdCooldownMinutes = CleanCooldown;
        rules.IntervalMinutes = CleanInterval;
        rules.OnCritical = Prefs.Switch(PrefKeys.CleanOnCritical);
        var mem = SystemMemory.Current();
        double used = mem.Total > 0 ? 100.0 * mem.Used / mem.Total : 0;
        if (rules.Sample(used, mem.PhysicalPressure == SystemMemory.Level.Critical, DateTime.Now) is { } reason)
            await Clean(reason);
    }

    /// Al empezar a jugar: se espera a que Ollama y los motores terminen de apagarse.
    private void CleanSoon(CleanReason reason)
    {
        if (Agent != AgentStatus.Ready) return;
        int generation = ++gameCleanGeneration;
        _ = WaitForGameShutdown();
        async Task WaitForGameShutdown()
        {
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                while (deadline.Elapsed < TimeSpan.FromSeconds(30))
                {
                    bool stopped = !Ollama.IsPowerActionRunning && Ollama.Power is Power.Off or Power.Missing
                        && Engines.All(e => e.Power is Power.Off or Power.Missing && !e.IsBusy);
                    if (stopped) stopped = await Task.Run(() => Detector.ServerPids().Count == 0
                        && ProcessCpu.RunnerPids().Count == 0 && Procs.ByName("llama-server").Count == 0);
                    var decision = GameCleanGate.Decide(generation == gameCleanGeneration && Game.Active && CurrentGame != null,
                        Prefs.Switch(PrefKeys.CleanOnGame), Ollama.Error != null || Engines.Any(e => e.Error != null), stopped, deadline.Elapsed);
                    if (decision == GameCleanDecision.Cancelled) return;
                    if (decision == GameCleanDecision.Ready)
                    {
                        while (!rules.CanAttempt(DateTime.Now) || Cleaning || Processing)
                        {
                            if (generation != gameCleanGeneration || !Game.Active || CurrentGame is null
                                || !Prefs.Switch(PrefKeys.CleanOnGame)) return;
                            await Task.Delay(500, cleaningLifetime.Token);
                        }
                        if (Ollama.IsPowerActionRunning || Ollama.Power is Power.On or Power.Starting or Power.Stopping
                            || Engines.Any(e => e.IsBusy || e.Power is Power.On or Power.Starting or Power.Stopping)) return;
                        await Clean(reason);
                        return;
                    }
                    await Task.Delay(500, cleaningLifetime.Token);
                }
                AppLog.Write("limpieza al jugar omitida: los motores no confirmaron el apagado en 30 s");
            }
            catch (OperationCanceledException) { }
        }
    }

    private void LoadCleanDemo()
    {
        cleanDemo = true;
        Agent = AgentStatus.Ready;
        LastClean = (1_900_000_000, DateTime.Now.AddMinutes(-2), CleanReason.Threshold);
    }
}
