using System.Globalization;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping;

/// Tipos de aviso, cada uno con su interruptor en Ajustes › Notificaciones.
public enum Notice { Sleep, Watchdog, Pressure, Game, Download, Update, Clean }

public static class NoticeExt
{
    public static string Key(this Notice n) => $"notify{n}";

    /// Todos activados salvo «RAM liberada» (Mem Reduct tampoco lo avisa por defecto).
    public static bool DefaultOn(this Notice n) => n != Notice.Clean;

    public static string Title(this Notice n) => n switch
    {
        Notice.Sleep => tr("Modelo dormido solo"),
        Notice.Watchdog => tr("Caídas y reinicios de Ollama"),
        Notice.Pressure => tr("Memoria crítica"),
        Notice.Game => tr("Modo juego"),
        Notice.Download => tr("Descargas terminadas"),
        Notice.Clean => tr("RAM liberada"),
        _ => tr("Actualizaciones"),
    };
}

/// Las reglas automáticas alrededor de Ollama: modo juego, los otros motores,
/// los avisos, la presión de memoria, los enlaces y las actualizaciones. Vive en
/// el hilo de la interfaz, como OllamaController.
public sealed partial class Supervisor
{
    public OllamaController Ollama { get; }
    public Prefs Prefs { get; }
    public List<IEngine> Engines { get; }
    public LlamaCppEngine LlamaCpp { get; }
    public LmStudioEngine LmStudio { get; }

    public event Action? Changed;
    /// Un aviso para Windows (el Notifier decide si se muestra).
    public event Action<Notice, string, string>? Notify;
    /// Enlaces `show` y `activity`: abrir el panel (y en qué vista).
    public event Action<bool>? ShowPanel;

    private readonly GameDetector detector = new();
    public GameModeTracker Game { get; } = new();
    private System.Windows.Threading.DispatcherTimer? timer;
    private bool scanning;
    private bool deferredGameExit;
    private SystemMemory.Level lastPressure = SystemMemory.Level.Normal;
    private bool pressureWarned;

    public const string OllamaId = "ollama";

    public Supervisor(OllamaController ollama, Prefs prefs)
    {
        Ollama = ollama;
        Prefs = prefs;
        LlamaCpp = new LlamaCppEngine();
        LmStudio = new LmStudioEngine();
        Engines = [LlamaCpp, LmStudio];
        foreach (var e in Engines)
        {
            e.Changed += () => Changed?.Invoke();
            e.AutoSlept += OnEngineSlept;
        }

        ollama.OnAutoRelease = names =>
        {
            var bytes = ollama.Stats.Last(StatKind.Nap)?.Bytes ?? 0;
            Notify?.Invoke(Notice.Sleep, tr("Modelo dormido"),
                tr("%@ llevaba %d min sin uso. Se liberaron %@.", string.Join(", ", names), ollama.IdleReleaseMinutes, bytes.Gigabytes()));
        };
        ollama.WatchdogActed += (action, reason) =>
        {
            if (action == WatchdogAction.GiveUp)
                Notify?.Invoke(Notice.Watchdog, tr("Ollama sigue cayéndose"), tr("Lo reinicié %d veces en 10 min. Me rindo: revisa el log.", Watchdog.MaxRestarts));
            else
                Notify?.Invoke(Notice.Watchdog, reason == "hang" ? tr("Ollama se colgó") : tr("Ollama se cayó"), tr("Lo estoy reiniciando con %@.", ollama.Backend.Summary));
        };
        ollama.PullFinished += (model, failure) =>
        {
            if (failure is null) Notify?.Invoke(Notice.Download, tr("Descarga terminada"), tr("%@ ya está instalado.", model));
            else Notify?.Invoke(Notice.Download, tr("La descarga falló"), $"{model}: {failure}");
        };
        ollama.UserPower += _ => Game.Touch(OllamaId);
        var previous = ollama.Power;
        ollama.PowerChanged += p =>
        {
            // Solo al encenderlo (desde la app, un atajo, un enlace, el modo juego o el
            // vigilante): si ya estaba encendido al abrir la app, no se carga nada.
            if (p == Power.On && previous == Power.Starting) LoadMainModel();
            previous = p;
        };
        ollama.Changed += CheckPressure;
        ApplyWatchdog();
        prefs.Changed += ApplyWatchdog;
    }

    /// El modelo principal, si hay, sigue instalado y no está ya en memoria.
    private void LoadMainModel()
    {
        if (Prefs.MainModel is not { } main) return;
        if (!Ollama.Installed.Any(m => m.Name == main) || Ollama.Loaded.Any(m => m.Name == main)) return;
        Ollama.Load(main);
    }

    private void ApplyWatchdog()
    {
        Ollama.WatchdogEnabled = Prefs.Switch(PrefKeys.Watchdog);
        Ollama.GameMode = Game.Active;
    }

    public void Start()
    {
        timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        timer.Tick += async (_, _) => await Tick();
        timer.Start();
        _ = Tick();
        _ = CheckUpdates();
        _ = RefreshCleaner();
        var daily = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(24) };
        daily.Tick += async (_, _) => await CheckUpdates();
        daily.Start();
    }

    private async Task Tick()
    {
        ProcessMonitor.GetForegroundPid();
        foreach (var e in Engines) await e.Refresh();
        await ScanGames();
        modelProtectionPids = await Task.Run(MemoryAgent.ModelPids);
        await CheckCleanRules();
    }

    // MARK: modo juego

    public bool GameModeEnabled => Prefs.Switch(PrefKeys.GameMode);

    private async Task ScanGames()
    {
        if (scanning || Processing) return;
        scanning = true;
        try
        {
            GameInfo? game = null;
            if (GameModeEnabled)
            {
                var custom = Prefs.List(PrefKeys.CustomGames);
                var ignored = Prefs.List(PrefKeys.IgnoredGames);
                game = await Task.Run(() => detector.Scan(custom, ignored));
            }
            CurrentGame = game;
            Apply(Game.Sample(game?.Name, DateTime.Now));
        }
        catch { }
        finally { scanning = false; }
    }

    /// El juego visto en la última muestra (con su .exe, para «No es un juego»).
    public GameInfo? CurrentGame { get; private set; }

    private void Apply(GameTransition transition)
    {
        if (transition == GameTransition.Enter) EnterGame();
        else if (transition == GameTransition.Exit) ExitGame();
    }

    private void EnterGame()
    {
        var on = new List<string>();
        if (Ollama.Power is Power.On or Power.Starting) on.Add(OllamaId);
        on.AddRange(Engines.Where(e => e.Installed && e.Power is Power.On or Power.Starting).Select(e => e.Id));
        Game.Stopped(on);
        Ollama.GameMode = true;
        if (on.Contains(OllamaId)) Ollama.Turn(false, napReason: "game");
        foreach (var e in Engines.Where(e => on.Contains(e.Id))) _ = e.SetOn(false);
        Ollama.Stats.Add(new StatEvent(DateTime.Now, StatKind.GameOn, Game.Game));
        if (Prefs.Switch(PrefKeys.CleanOnGame)) CleanSoon(CleanReason.Game);
        Notify?.Invoke(Notice.Game, tr("Modo juego"), on.Count > 0
            ? tr("Jugando a %@. Apagué %@ para liberar memoria.", Game.Game ?? "", string.Join(", ", on.Select(Label)))
            : tr("Jugando a %@.", Game.Game ?? ""));
        Changed?.Invoke();
    }

    private void ExitGame()
    {
        gameCleanGeneration++;
        if (Processing)
        {
            deferredGameExit = true;
            Ollama.GameMode = false;
            return;
        }
        deferredGameExit = false;
        var game = Ollama.Stats.Last(StatKind.GameOn)?.Subject;
        Ollama.GameMode = false;
        bool restore = Prefs.Switch(PrefKeys.GameRestore);
        var list = Game.ToRestore(id => id == OllamaId ? Ollama.Power is Power.On or Power.Starting
            : Engines.FirstOrDefault(e => e.Id == id)?.Power is Power.On or Power.Starting);
        if (restore)
        {
            if (list.Contains(OllamaId)) Ollama.Turn(true);
            foreach (var e in Engines.Where(e => list.Contains(e.Id))) _ = e.SetOn(true);
        }
        Ollama.Stats.Add(new StatEvent(DateTime.Now, StatKind.GameOff, game));
        Notify?.Invoke(Notice.Game, tr("Fin del modo juego"), restore && list.Count > 0
            ? tr("Volví a encender %@.", string.Join(", ", list.Select(Label)))
            : tr("Ya no estás jugando."));
        Changed?.Invoke();
    }

    private string Label(string id) => id == OllamaId ? "Ollama" : Engines.FirstOrDefault(e => e.Id == id)?.Name ?? id;

    /// «Encender igualmente» durante el juego.
    public void PowerAnyway() => Ollama.SetOn(true);

    /// «No es un juego»: se ignora ese .exe y se sale del modo juego.
    public void IgnoreCurrentGame()
    {
        if (CurrentGame is { } g)
        {
            var ignored = Prefs.List(PrefKeys.IgnoredGames);
            if (!ignored.Contains(g.Exe, StringComparer.OrdinalIgnoreCase)) Prefs.SetList(PrefKeys.IgnoredGames, [.. ignored, g.Exe]);
        }
        CurrentGame = null;
        Apply(Game.ForceExit());
    }

    public void SetGameMode(bool on)
    {
        Prefs.SetSwitch(PrefKeys.GameMode, on);
        if (!on) Apply(Game.ForceExit());
        Changed?.Invoke();
    }

    public void AddGame(string exe)
    {
        var list = Prefs.List(PrefKeys.CustomGames);
        if (!list.Contains(exe, StringComparer.OrdinalIgnoreCase)) Prefs.SetList(PrefKeys.CustomGames, [.. list, exe]);
    }

    public void RemoveGame(string exe)
    {
        Prefs.SetList(PrefKeys.CustomGames, Prefs.List(PrefKeys.CustomGames).Where(g => !g.Equals(exe, StringComparison.OrdinalIgnoreCase)));
        Prefs.SetList(PrefKeys.IgnoredGames, Prefs.List(PrefKeys.IgnoredGames).Where(g => !g.Equals(exe, StringComparison.OrdinalIgnoreCase)));
    }

    // MARK: otros motores

    public void ToggleEngine(IEngine engine)
    {
        Game.Touch(engine.Id);
        _ = engine.SetOn(engine.Power is not (Power.On or Power.Starting));
    }

    private void OnEngineSlept(IEngine engine, long bytes)
    {
        Ollama.Stats.Add(new StatEvent(DateTime.Now, StatKind.Nap, $"{engine.Name} · {engine.Model}", bytes, "auto"));
        Notify?.Invoke(Notice.Sleep, tr("%@ dormido", engine.Name),
            tr("Llevaba %d min sin uso. Se liberaron %@.", engine.IdleMinutes, bytes.Gigabytes()));
    }

    // MARK: presión de memoria (con histéresis: se avisa otra vez solo tras volver a normal)

    private void CheckPressure()
    {
        var level = Ollama.Memory.Pressure;
        if (level == lastPressure) return;
        lastPressure = level;
        if (level == SystemMemory.Level.Normal) pressureWarned = false;
        if (level != SystemMemory.Level.Critical || pressureWarned || Ollama.IsDemo) return;
        pressureWarned = true;
        var detail = Ollama.Loaded.Count > 0
            ? tr("%@ ocupa %@. Duérmelo desde el panel o con %@.", Ollama.Loaded[0].Name, Ollama.ModelRam.MemoryGB(), HotKey.Sleep.Display)
            : tr("Windows se está quedando sin memoria.");
        Notify?.Invoke(Notice.Pressure, tr("Memoria crítica"), detail);
    }

    // MARK: enlaces istargetsleeping://

    public void Execute(string command)
    {
        AppLog.Write($"orden: {command}");
        if (command == "show") { ShowPanel?.Invoke(false); return; }
        if (command == "sleep") { Sleep(); return; }
        if (command == "clean") { _ = Clean(CleanReason.Manual); return; }
        if (command.StartsWith("url:") && Links.Parse(command[4..]) is { } link) Execute(link);
    }

    public void Execute(Link link)
    {
        switch (link.Action)
        {
            case LinkAction.On: if (Ollama.Power is Power.Off) Ollama.SetOn(true); break;
            case LinkAction.Off: if (Ollama.Power is Power.On or Power.Starting) Ollama.SetOn(false); break;
            case LinkAction.Toggle: if (!Ollama.Power.IsTransition()) Ollama.Toggle(); break;
            case LinkAction.Sleep: Sleep(); break;
            case LinkAction.Clean: _ = Clean(CleanReason.Manual); break;
            case LinkAction.Load: if (Ollama.Power == Power.On && link.Model is { } m) Ollama.Load(m); break;
            case LinkAction.Show: ShowPanel?.Invoke(false); break;
            case LinkAction.Activity: ShowPanel?.Invoke(true); break;
        }
    }

    /// Ctrl+Alt+S y `sleep`: duerme el modelo (y los otros motores) sin apagar nada.
    public void Sleep()
    {
        _ = Ollama.ReleaseAll();
        foreach (var e in Engines.Where(e => e.Power == Power.On && e.Model is not null)) _ = e.Sleep();
    }

    // MARK: actualizaciones

    public bool UpdatesAvailable => AppInfo.UpdateRepo.Length > 0;
    public UpdateInfo? Update { get; private set; }
    public double? UpdateProgress { get; private set; }
    public UpdateStatus UpdateStatus { get; private set; }
    public UpdateStage UpdateStage { get; private set; }
    public string? UpdateMessage { get; private set; }
    public DateTimeOffset? UpdateRetryAt { get; private set; }
    public bool UpdateBusy => UpdateStage != UpdateStage.Idle;
    private CancellationTokenSource? updateCancellation;
    private UpdateInfo? notified;

    public async Task CheckUpdates(bool manual = false)
    {
        if (!UpdatesAvailable || (!manual && !Prefs.Switch(PrefKeys.UpdateCheck)) || Ollama.IsDemo
            || UpdateStatus == UpdateStatus.Checking || UpdateBusy) return;
        if (UpdateRetryAt > DateTimeOffset.UtcNow)
        {
            UpdateStatus = UpdateStatus.RateLimited;
            Changed?.Invoke();
            return;
        }
        UpdateStatus = UpdateStatus.Checking;
        UpdateMessage = null;
        Changed?.Invoke();
        var result = await Updater.Check(AppInfo.UpdateApi, AppInfo.UpdateRepo, AppInfo.Version);
        UpdateStatus = result.Status;
        UpdateMessage = result.Error;
        UpdateRetryAt = result.RetryAt;
        // Un fallo de red no elimina una actualización que ya se había encontrado.
        if (result.Status is not (UpdateStatus.Error or UpdateStatus.RateLimited)) Update = result.Info;
        AppLog.Write($"update check: {result.Status}");
        if (Update is { } u && notified?.Version != u.Version)
        {
            notified = u;
            Notify?.Invoke(Notice.Update, tr("Nueva versión de %@", AppInfo.Name), tr("La %@ está lista para instalar desde el panel.", u.Version));
        }
        Changed?.Invoke();
    }

    public async Task InstallUpdate()
    {
        if (Update is not { } u || UpdateBusy || UpdateStatus == UpdateStatus.Checking) return;
        using var cancellation = new CancellationTokenSource();
        updateCancellation = cancellation;
        PreparedUpdate? prepared = null;
        bool handedOff = false;
        UpdateStage = UpdateStage.Downloading;
        UpdateProgress = 0;
        UpdateMessage = null;
        Changed?.Invoke();
        try
        {
            var progress = new Progress<UpdateTransfer>(p =>
            {
                if (updateCancellation != cancellation || UpdateStage == UpdateStage.Applying) return;
                UpdateStage = p.Stage;
                UpdateProgress = p.Fraction;
                Changed?.Invoke();
            });
            prepared = await Updater.Download(u, progress, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            UpdateStage = UpdateStage.Applying;
            Changed?.Invoke();
            Ollama.Stats.Flush();
            await Task.Run(() => UpdateInstaller.Start(prepared));
            handedOff = true;
            System.Windows.Application.Current?.Shutdown();
        }
        catch (OperationCanceledException)
        {
            UpdateMessage = tr("Descarga cancelada.");
        }
        catch (UnauthorizedAccessException)
        {
            UpdateMessage = tr("No hay permisos para actualizar esta carpeta. Descarga la versión desde GitHub.");
        }
        catch (Exception e)
        {
            UpdateMessage = tr("No se pudo actualizar: %@", e.Message);
        }
        finally
        {
            // Si el auxiliar tomó el relevo, su sesión sigue en uso hasta confirmar el arranque.
            if (!handedOff && prepared is not null)
                Updater.DeleteStaging(prepared.Directory);
            updateCancellation = null;
            UpdateProgress = null;
            UpdateStage = UpdateStage.Idle;
            Changed?.Invoke();
        }
    }

    public void CancelUpdate()
    {
        if (UpdateStage is UpdateStage.Downloading or UpdateStage.Verifying) updateCancellation?.Cancel();
    }

    public void ReportUpdateRecovery(string message)
    {
        UpdateMessage = message;
        UpdateStatus = UpdateStatus.Error;
        Changed?.Invoke();
    }

    /// Capturas de los estados de actualización, sin red ni instalación.
    public void LoadUpdateDemo(UpdateStage stage = UpdateStage.Idle, bool error = false)
    {
        if (!Ollama.IsDemo) return;
        Update = new("1.4.0", "", "", AppInfo.RepoUrl + "/releases/tag/v1.4.0");
        UpdateStatus = UpdateStatus.Available;
        UpdateStage = stage;
        UpdateProgress = stage == UpdateStage.Downloading ? 0.42 : null;
        UpdateMessage = error ? tr("No hay permisos para actualizar esta carpeta. Descarga la versión desde GitHub.") : null;
        Changed?.Invoke();
    }

    /// Demo: un juego en marcha y un llama-server con modelo, para las capturas.
    public void LoadDemo(bool game = false)
    {
        LoadCleanDemo();
        LlamaCpp.LoadDemo();
        LmStudio.LoadDemo();
        if (game)
        {
            var t0 = DateTime.Now.AddSeconds(-10);
            Game.Sample("Hollow Knight", t0);
            Game.Sample("Hollow Knight", DateTime.Now);
        }
    }

    public static string Ago(DateTime at)
    {
        var span = DateTime.Now - at;
        if (span.TotalMinutes < 1) return tr("hace un momento");
        if (span.TotalHours < 1) return tr("hace %d min", (int)span.TotalMinutes);
        if (span.TotalDays < 1) return tr("hace %d h", (int)span.TotalHours);
        if (span.TotalDays < 2) return tr("ayer a las %@", at.ToString("t", CultureInfo.CurrentCulture));
        return at.ToString("d MMM", CultureInfo.CurrentCulture);
    }
}
