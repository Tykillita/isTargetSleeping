using static IsTargetSleeping.L10n;

namespace IsTargetSleeping;

/// Liberar RAM (lo que hacía Mem Reduct): el agente con privilegios, las reglas
/// automáticas y la convivencia con Mem Reduct.
public sealed partial class Supervisor
{
    public AgentStatus Agent { get; private set; } = AgentStatus.NotInstalled;
    public bool AgentBusy { get; private set; }
    public string? AgentError { get; private set; }
    public bool Cleaning { get; private set; }
    public (long Freed, DateTime At, CleanReason Reason)? LastClean { get; private set; }

    public bool MemReductInstalled { get; private set; }
    public bool MemReductRunning { get; private set; }
    public bool MemReductReplaced { get; private set; }
    public MemReductConfig? MemReductConfig { get; private set; }

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
        set { if (!cleanDemo) Defaults.Set(PrefKeys.CleanInterval, value); rules.Cleaned(DateTime.Now); Changed?.Invoke(); }
    }

    public void ToggleArea(CleanAreas area) => Areas ^= area;

    /// Estado del agente y de Mem Reduct (disco, registro y COM: fuera del hilo de la interfaz).
    public async Task RefreshCleaner()
    {
        if (cleanDemo) return;
        var (agent, installed, running, replaced, config) = await Task.Run(() =>
            (MemoryAgent.Status(), MemReduct.Installed, MemReduct.Running, MemReduct.Replaced, MemReduct.Config()));
        Agent = agent;
        MemReductInstalled = installed;
        MemReductRunning = running;
        MemReductReplaced = replaced;
        MemReductConfig = config;
        Changed?.Invoke();
    }

    // MARK: agente

    public async Task InstallAgent(bool closeMemReduct = false)
    {
        if (AgentBusy) return;
        AgentBusy = true;
        AgentError = null;
        Changed?.Invoke();
        var error = await Task.Run(() => MemoryAgent.Install(closeMemReduct));
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
        var pids = MemoryAgent.ModelPids();
        if (CurrentGame is { Pid: > 0 } game) pids.Add(game.Pid);
        return [.. pids];
    }

    /// `fromPanel`: el botón del panel ya enseña el resultado; lo demás (atajo, enlace, reglas) avisa.
    public async Task Clean(CleanReason reason, bool fromPanel = false, bool sleepModels = false)
    {
        if (Cleaning || Ollama.IsDemo) return;
        if (Agent != AgentStatus.Ready)
        {
            if (reason == CleanReason.Manual)
                Ollama.Error = Agent == AgentStatus.Outdated
                    ? tr("Actualiza el agente de memoria en Ajustes › Liberar RAM (pide permiso de administrador).")
                    : tr("Activa la limpieza de RAM en Ajustes (pide permiso de administrador una vez).");
            return;
        }
        Cleaning = true;
        Changed?.Invoke();
        try
        {
            if (sleepModels)
            {
                await Ollama.ReleaseAll();
                foreach (var e in Engines.Where(e => e.Power == Power.On && e.Model is not null)) await e.Sleep();
            }
            var areas = Areas;
            long before = SystemMemory.Current().Used;
            var keep = await Task.Run(ProtectedPids);
            var (failed, error) = await Task.Run(() => MemoryAgent.Run(new CleanSpec(areas, keep)));
            await Task.Delay(300);
            long freed = Math.Max(0, before - SystemMemory.Current().Used);
            rules.Cleaned(DateTime.Now);
            var detail = reason.ToString().ToLowerInvariant();
            if (error is not null)
            {
                AppLog.Write($"limpieza ({detail}) falló: {error}");
                if (reason == CleanReason.Manual) Ollama.Error = error;
                return;
            }
            LastClean = (freed, DateTime.Now, reason);
            Ollama.Stats.Add(new StatEvent(DateTime.Now, StatKind.Clean, null, freed, detail));
            AppLog.Write($"limpieza ({detail}): {freed.MemoryGB()} liberados · zonas {areas}{(failed != CleanAreas.None ? $" · fallaron {failed}" : "")} · protegidos {keep.Count}");
            if (!fromPanel)
                Notify?.Invoke(Notice.Clean, tr("RAM liberada"), tr("%@ liberados · %@", freed.MemoryGB(), ReasonText(reason)));
        }
        finally
        {
            Cleaning = false;
            _ = Ollama.Refresh();
            Changed?.Invoke();
        }
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
        if (Agent != AgentStatus.Ready || Cleaning || Ollama.IsDemo) return;
        rules.ThresholdPercent = CleanThreshold;
        rules.IntervalMinutes = CleanInterval;
        rules.OnCritical = Prefs.Switch(PrefKeys.CleanOnCritical);
        var mem = Ollama.Memory;
        double used = mem.Total > 0 ? 100.0 * mem.Used / mem.Total : 0;
        if (rules.Sample(used, mem.Pressure == SystemMemory.Level.Critical, DateTime.Now) is { } reason)
            await Clean(reason);
    }

    /// Al empezar a jugar: se espera a que Ollama y los motores terminen de apagarse.
    private void CleanSoon(CleanReason reason)
    {
        if (Agent != AgentStatus.Ready) return;
        var once = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        once.Tick += async (_, _) => { once.Stop(); await Clean(reason); };
        once.Start();
    }

    // MARK: Mem Reduct

    /// Copia sus reglas y zonas (su % y su intervalo solo si los tenía activados).
    public void ImportMemReduct()
    {
        if (MemReduct.Config() is not { } c) return;
        Areas = c.Areas;
        CleanThreshold = c.AutoEnabled ? c.AutoPercent : 0;
        CleanInterval = c.IntervalEnabled ? c.IntervalMinutes : 0;
        Prefs.SetSwitch(Notice.Clean.Key(), c.NotifyResults);
        AppLog.Write($"importado de Mem Reduct: {CleanThreshold} %, cada {CleanInterval} min, zonas {c.Areas}");
        Changed?.Invoke();
    }

    /// Importa, lo quita del inicio, apaga su limpieza automática y lo cierra (lo
    /// cierra el agente: Mem Reduct corre como administrador).
    public async Task ReplaceMemReduct()
    {
        ImportMemReduct();
        await Task.Run(MemReduct.Replace);
        if (Agent == AgentStatus.Ready)
            await Task.Run(() => MemoryAgent.Run(new CleanSpec(CleanAreas.None, [], CloseMemReduct: true)));
        else
            await InstallAgent(closeMemReduct: true);
        AppLog.Write("Mem Reduct reemplazado");
        await RefreshCleaner();
    }

    public async Task RestoreMemReduct()
    {
        await Task.Run(MemReduct.Restore);
        AppLog.Write("vuelta a Mem Reduct");
        await RefreshCleaner();
    }

    private void LoadCleanDemo()
    {
        cleanDemo = true;
        Agent = AgentStatus.Ready;
        LastClean = (1_900_000_000, DateTime.Now.AddMinutes(-2), CleanReason.Threshold);
        MemReductInstalled = true;
        MemReductRunning = true;
        MemReductConfig = new MemReductConfig(true, 60, true, 6, CleanAreas.Default, false);
    }
}
