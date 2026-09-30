using static IsTargetSleeping.L10n;

namespace IsTargetSleeping;

/// Una descarga en curso (`ollama pull`).
public sealed class PullJob(string name)
{
    public string Name { get; } = name;
    public string Status { get; set; } = "";
    public double? Fraction { get; set; }
    public CancellationTokenSource Cancel { get; } = new();
}

/// Estado para la interfaz. Vive en el hilo de la interfaz: el trabajo que
/// bloquea (detección, procesos) va a Task.Run y vuelve aquí.
public sealed class OllamaController
{
    public Power Power { get; private set; } = Power.Off;
    public Backend Backend { get; private set; } = Backend.Missing;
    public string? Version { get; private set; }
    public List<LoadedModel> Loaded { get; private set; } = [];
    public List<InstalledModel> Installed { get; private set; } = [];
    public string? BusyModel { get; private set; }
    /// Mecanismos instalados y el elegido para encender (ajustes).
    public List<Backend> Mechanisms { get; private set; } = [];
    public Backend? Preferred { get; private set; }
    public SystemMemory Memory { get; private set; } = SystemMemory.Current();
    /// RAM que ocupan los runners de los modelos cargados (0 si no hay ninguno).
    public long ModelMemory { get; private set; }
    /// VRAM en uso y la parte de los runners (null sin GPU dedicada).
    public GpuUsage? Gpu { get; private set; }
    /// Segundos sin uso del modelo cargado (null si no hay modelo o no se mide).
    public double? IdleSeconds { get; private set; }
    /// Última liberación automática, para contarlo en el panel.
    public (string[] Models, DateTime At)? AutoReleased { get; private set; }
    public PullJob? Pulling { get; private set; }

    /// Historial y estadísticas (en memoria salvo que la app le dé un archivo).
    public StatsStore Stats { get; }
    /// Qué apps están conectadas a la API (se muestrea cada segundo).
    public ClientTracker Clients { get; } = new();

    private string? error;
    public string? Error
    {
        get => error;
        set { error = value; Changed?.Invoke(); }
    }

    private int idleReleaseMinutes = Defaults.GetInt("idleReleaseMinutes");
    /// Minutos sin uso tras los que se libera la memoria; 0 = nunca.
    public int IdleReleaseMinutes
    {
        get => idleReleaseMinutes;
        set
        {
            idleReleaseMinutes = value;
            if (!IsDemo) Defaults.Set("idleReleaseMinutes", value);
            tracker.Touch();
            Changed?.Invoke();
        }
    }

    public bool IsDemo { get; private set; }
    /// Solo para pruebas: límite en segundos que manda sobre los minutos.
    public double? IdleLimitOverride { get; set; }

    public double? IdleLimit => IdleLimitOverride ?? (idleReleaseMinutes > 0 ? idleReleaseMinutes * 60 : null);

    /// Lo medido en los procesos runner; si no se puede leer, lo que dice la API.
    public long ModelRam => Math.Min(Memory.Used, ModelMemory > 0 ? ModelMemory : Loaded.Sum(m => m.RamBytes));

    public event Action? Changed;
    public event Action<Power>? PowerChanged;
    public Action<string[]>? OnAutoRelease;
    /// El usuario encendió o apagó a mano (panel, atajo, enlace): el modo juego lo respeta.
    public event Action<bool>? UserPower;
    /// El vigilante actuó (reinicio o rendición) y por qué (crash o hang).
    public event Action<WatchdogAction, string>? WatchdogActed;
    /// Terminó una descarga: el modelo y el error (null si fue bien).
    public event Action<string, string?>? PullFinished;

    /// Lo activa el supervisor: en modo juego o con el ajuste apagado, el vigilante no actúa.
    public bool WatchdogEnabled { get; set; } = true;
    public bool GameMode { get; set; }

    private (Power Target, DateTime Since)? transition;
    /// El último mecanismo con el que Ollama estuvo encendido. Apagado, la
    /// detección solo ve lo instalado (p. ej. la app y además un servicio) y
    /// podría encenderlo por otro camino distinto al que usas.
    private Backend? lastActive;
    private System.Windows.Threading.DispatcherTimer? timer;
    private Timer? clientTimer;
    private bool refreshing;
    private readonly IdleTracker tracker = new();
    private readonly Watchdog watchdog = new();
    private bool releasing;
    /// Si el usuario quiere Ollama encendido: lo decide él, no una caída.
    private bool wantOn;
    private DateTime lastRefresh = DateTime.Now;
    private HashSet<string> lastLoaded = [];
    /// Modelos que cargó el propio panel: no cuentan como «despertado por».
    private readonly HashSet<string> ownLoads = [];

    public OllamaController(StatsStore? stats = null)
    {
        Stats = stats ?? new StatsStore(null);
    }

    public void StartPolling()
    {
        _ = Refresh();
        timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        timer.Tick += async (_, _) => await Refresh();
        timer.Start();
        // Cada segundo: qué apps tienen una conexión abierta con la API.
        int port = OllamaApi.Base.Port;
        clientTimer = new Timer(_ =>
        {
            try
            {
                var apps = TcpClients.Connected(port).Select(AppIdentity.Of).OfType<ClientApp>().ToList();
                if (apps.Count > 0) Clients.Seen(apps, DateTime.Now);
            }
            catch { }
        }, null, 1000, 1000);
    }

    public async Task Refresh()
    {
        if (refreshing || IsDemo) return;
        refreshing = true;
        try
        {
            // Mientras se apaga no se vuelve a detectar: la app o el servicio pueden
            // desaparecer a mitad y la detección caería en otro mecanismo.
            var v = await OllamaApi.Version();
            Version = v;
            if (transition?.Target != Power.Stopping)
            {
                var detected = await Task.Run(Detector.Detect);
                if (v is not null) { Backend = detected; lastActive = detected; }
                else Backend = lastActive ?? detected;
            }

            if (v is not null)
            {
                var l = OllamaApi.Loaded();
                var i = OllamaApi.Installed();
                Loaded = await l;
                var list = await i;
                if (list.Count > 0 || Installed.Count == 0) Installed = list;
            }
            else Loaded = [];

            Memory = SystemMemory.Current();
            var runners = Loaded.Count > 0 ? await Task.Run(ProcessCpu.RunnerPids) : [];
            ModelMemory = runners.Count > 0 ? await Task.Run(() => runners.Sum(pid => Procs.PrivateBytes(pid) ?? 0)) : 0;
            Gpu = await Task.Run(() => IsTargetSleeping.Gpu.Read(runners));
            var (all, pref) = await Task.Run(() => (Detector.Available(), Detector.Preferred));
            Mechanisms = all;
            Preferred = pref;
            SetPower(Derive(v is not null));
            await Watch(v is not null);
            TrackWakes();
            await TrackIdle();
            Record();
        }
        finally
        {
            refreshing = false;
            Changed?.Invoke();
        }
    }

    /// Con qué se enciende a partir de ahora. Si está apagado, cuenta ya.
    public void Prefer(Backend b)
    {
        Detector.SetPreferred(b);
        Preferred = b;
        if (!Power.IsUp() && !Power.IsTransition())
        {
            Backend = b;
            lastActive = b;
        }
        Changed?.Invoke();
    }

    // MARK: vigilante

    private async Task Watch(bool up)
    {
        if (up && Power == Power.On && transition is null) wantOn = true;
        bool armed = wantOn && WatchdogEnabled && !GameMode && transition is null;
        bool serverAlive = false;
        if (!up && armed)
        {
            serverAlive = await Task.Run(() => Detector.ServerPids().Count > 0);
            // La app de Ollama cerrada entera (y sin servidor) es «Salir» desde su menú, no una caída.
            if (!serverAlive && Backend.Kind == BackendKind.App && Detector.RunningAppPid() is null)
            {
                wantOn = false;
                armed = false;
            }
        }
        var action = watchdog.Sample(armed, up, serverAlive, DateTime.Now);
        var reason = serverAlive ? "hang" : "crash";
        switch (action)
        {
            case WatchdogAction.Restart:
            case WatchdogAction.KillAndRestart:
                Stats.Add(new StatEvent(DateTime.Now, StatKind.Crash, Detail: reason));
                Stats.Add(new StatEvent(DateTime.Now, StatKind.Restart, Detail: reason));
                WatchdogActed?.Invoke(action, reason);
                Restart(kill: action == WatchdogAction.KillAndRestart);
                break;
            case WatchdogAction.GiveUp:
                wantOn = false;
                Stats.Add(new StatEvent(DateTime.Now, StatKind.GiveUp, Detail: reason));
                error = tr("Ollama se cayó %d veces en 10 min. No lo reinicio más: revisa el log.", Watchdog.MaxRestarts);
                WatchdogActed?.Invoke(action, reason);
                break;
        }
    }

    /// Reinicio por el último mecanismo: se para del todo (y se mata si está colgado) y se enciende.
    private void Restart(bool kill)
    {
        var target = Backend;
        if (target.Kind == BackendKind.Missing) return;
        transition = (Power.Starting, DateTime.Now);
        SetPower(Power.Starting);
        _ = Run();

        async Task Run()
        {
            var failure = await Task.Run(() =>
            {
                if (kill) foreach (var pid in Detector.ServerPids()) Procs.KillTree(pid);
                Switch.Stop(target);
                return Switch.Start(target);
            });
            if (failure is not null)
            {
                error = failure;
                transition = null;
            }
            await Refresh();
        }
    }

    // MARK: estadísticas y «quién lo despertó»

    /// Un modelo que aparece en memoria sin que lo cargue el panel: lo despertó una app.
    private void TrackWakes()
    {
        var now = Loaded.Select(m => m.Name).ToHashSet();
        foreach (var name in now.Except(lastLoaded))
        {
            if (ownLoads.Remove(name)) continue;
            var app = Clients.Recent(DateTime.Now).FirstOrDefault();
            Stats.Add(new StatEvent(DateTime.Now, StatKind.Wake, app?.Name, Loaded.First(m => m.Name == name).Bytes, app?.Path, name));
        }
        lastLoaded = now;
    }

    private void Record()
    {
        var now = DateTime.Now;
        if (Loaded.Count > 0) Stats.AddLoaded(Math.Min(10, (now - lastRefresh).TotalSeconds), now);
        lastRefresh = now;
        Stats.Sample(new MemorySample(now, Memory.Total, Memory.Used, ModelRam));
    }

    /// La app que despertó al modelo cargado ahora (si se sabe).
    public StatEvent? LastWake => Stats.Last(StatKind.Wake) is { Subject: not null } w && Loaded.Count > 0 ? w : null;

    private void Nap(IEnumerable<LoadedModel> models, string detail)
    {
        var list = models.ToList();
        if (list.Count == 0) return;
        Stats.Add(new StatEvent(DateTime.Now, StatKind.Nap, string.Join(", ", list.Select(m => m.Name)), list.Sum(m => m.Bytes), detail));
    }

    private async Task TrackIdle()
    {
        if (Power != Power.On || Loaded.Count == 0)
        {
            IdleSeconds = null;
            tracker.Touch();
            return;
        }
        var models = Loaded.Select(m => m.Name).ToHashSet();
        var cpu = await Task.Run(() =>
        {
            var output = new Dictionary<int, double>();
            foreach (var pid in ProcessCpu.RunnerPids())
                if (ProcessCpu.Seconds(pid) is { } s) output[pid] = s;
            return output;
        });
        tracker.Sample(cpu, models);
        var idle = tracker.IdleSeconds();
        IdleSeconds = idle;

        if (IdleLimit is not { } limit || idle < limit || BusyModel is not null || releasing) return;
        releasing = true;
        var released = Loaded.ToList();
        var names = released.Select(m => m.Name).ToArray();
        foreach (var name in names)
        {
            if (await OllamaApi.SetKeepAlive(name, 0) is { } failure) error = failure;
        }
        AutoReleased = (names, DateTime.Now);
        Nap(released, "auto");
        OnAutoRelease?.Invoke(names);
        tracker.Touch();
        releasing = false;
        Loaded = await OllamaApi.Loaded();
        lastLoaded = Loaded.Select(m => m.Name).ToHashSet();
        Memory = SystemMemory.Current();
        ModelMemory = Loaded.Count > 0 ? await Task.Run(ProcessCpu.RunnerMemory) : 0;
        IdleSeconds = null;
    }

    private Power Derive(bool up)
    {
        if (transition is { } t)
        {
            var elapsed = (DateTime.Now - t.Since).TotalSeconds;
            switch (t.Target)
            {
                case Power.Starting:
                    if (up) { transition = null; return Power.On; }
                    if (elapsed > 90)
                    {
                        transition = null;
                        error = tr("Ollama no respondió en 90 s. Revisa el log (clic derecho en el ícono).");
                        return Power.Off;
                    }
                    return Power.Starting;
                case Power.Stopping:
                    if (!up) { transition = null; return Power.Off; }
                    if (elapsed > 30)
                    {
                        transition = null;
                        error = tr("Ollama sigue respondiendo. Puede que lo haya arrancado otro programa.");
                        return Power.On;
                    }
                    return Power.Stopping;
                default:
                    transition = null;
                    break;
            }
        }
        if (up) return Power.On;
        return Backend.Kind == BackendKind.Missing ? Power.Missing : Power.Off;
    }

    private void SetPower(Power p)
    {
        if (p == Power) return;
        Power = p;
        PowerChanged?.Invoke(p);
        Changed?.Invoke();
    }

    /// Encender o apagar a mano (panel, menú, atajo, enlace).
    public void Toggle() => SetOn(!(Power.IsUp() || Power == Power.Starting));

    public void SetOn(bool on)
    {
        if (Backend.Kind != BackendKind.Missing) UserPower?.Invoke(on);
        Turn(on);
    }

    /// Encender o apagar por una regla (modo juego): no cuenta como decisión del usuario.
    public void Turn(bool on, string? napReason = null)
    {
        error = null;
        if (Backend.Kind == BackendKind.Missing)
        {
            Paths.Open(Paths.Download);
            Changed?.Invoke();
            return;
        }
        wantOn = on;
        if (!on && napReason is not null) Nap(Loaded, napReason);
        var target = Backend;
        transition = (on ? Power.Starting : Power.Stopping, DateTime.Now);
        SetPower(on ? Power.Starting : Power.Stopping);
        _ = Run();

        async Task Run()
        {
            var failure = await Task.Run(() => on ? IsTargetSleeping.Switch.Start(target) : IsTargetSleeping.Switch.Stop(target));
            if (failure is not null)
            {
                error = failure;
                transition = null;
            }
            await Refresh();
        }
    }

    /// Estado de ejemplo para las capturas del README (`--snapshot … demo`): unos
    /// modelos de ejemplo, sin tocar Ollama ni los ajustes. La memoria total es la
    /// real de este PC, para que la captura nunca muestre otra RAM que la tuya; el
    /// uso es una proporción de ejemplo (modelo ~35 %, resto ~30 %).
    public void LoadDemo(bool modelLoaded = true)
    {
        IsDemo = true;
        Power = Power.On;
        Backend = new Backend(BackendKind.App, @"C:\Users\demo\AppData\Local\Programs\Ollama\ollama app.exe");
        Mechanisms = [new Backend(BackendKind.Task, @"\Ollama serve", "Ollama serve"), Backend];
        Preferred = Backend;
        Version = "0.34.1";

        var real = SystemMemory.Current();
        long model = modelLoaded ? (long)(real.Total * 0.35) : 0;
        long other = (long)(real.Total * 0.30);
        Loaded = modelLoaded ? [new LoadedModel("qwen3.8:8b", model, 0, 32_768)] : [];
        ModelMemory = model;
        Installed =
        [
            new InstalledModel("gemma4:4b", 3_340_000_000, "Q4_K_M", false, true),
            new InstalledModel("llava:7b", 4_730_000_000, "Q4_0", true, false),
            new InstalledModel("qwen3.8:8b", 5_210_000_000, "Q4_K_M", true, true),
        ];
        Memory = real with { Used = model + other, Pressure = SystemMemory.Level.Normal };
        // La GPU real si la hay; el uso, de ejemplo (modelo ~55 %, resto ~10 %).
        if (IsTargetSleeping.Gpu.Adapter is { } gpu && modelLoaded)
            Gpu = new GpuUsage((long)(gpu.Total * 0.65), (long)(gpu.Total * 0.55));
        idleReleaseMinutes = 30;
        IdleSeconds = modelLoaded ? 7 * 60 + 12 : null;
        error = null;

        // Historial de ejemplo para la vista Actividad.
        var now = DateTime.Now;
        var rnd = new Random(7);
        for (int i = 180; i >= 0; i--)
        {
            double wave = 0.5 + 0.5 * Math.Sin(i / 14.0);
            long m = i < 40 ? model : (long)(model * wave * (i % 90 < 60 ? 1 : 0));
            Stats.Sample(new MemorySample(now.AddSeconds(-i * 10), real.Total, m + other + (long)(rnd.NextDouble() * real.Total * 0.04), m));
        }
        void Add(double hoursAgo, StatKind kind, string? subject = null, long bytes = 0, string? detail = null, string? model = null) =>
            Stats.Add(new StatEvent(now.AddHours(-hoursAgo), kind, subject, bytes, detail, model));
        Add(50, StatKind.Wake, "Cursor", 5_210_000_000, model: "qwen3.8:8b");
        Add(49, StatKind.Nap, "qwen3.8:8b", 5_210_000_000, "auto");
        Add(30, StatKind.Wake, "Obsidian", 3_340_000_000, model: "gemma4:4b");
        Add(29, StatKind.Nap, "gemma4:4b", 3_340_000_000, "auto");
        Add(26, StatKind.GameOn, "Hollow Knight");
        Add(24.5, StatKind.GameOff, "Hollow Knight");
        Add(20, StatKind.Download, "gemma4:4b", 3_340_000_000);
        Add(8, StatKind.Wake, "Obsidian", 5_210_000_000, model: "qwen3.8:8b");
        Add(7.4, StatKind.Nap, "qwen3.8:8b", 5_210_000_000, "manual");
        Add(5, StatKind.Crash, detail: "crash");
        Add(5, StatKind.Restart, detail: "crash");
        Add(3, StatKind.Wake, "OpenCode", 5_210_000_000, model: "qwen3.8:8b");
        Add(2.6, StatKind.Nap, "qwen3.8:8b", 5_210_000_000, "auto");
        Add(0.2, StatKind.Wake, "Obsidian", 5_210_000_000, model: "qwen3.8:8b");
        Stats.AddLoaded(14.5 * 3600, now);
    }

    /// Saca de la memoria todo lo cargado.
    public async Task ReleaseAll()
    {
        if (Power != Power.On || IsDemo) return;
        var models = Loaded.ToList();
        foreach (var name in models.Select(m => m.Name))
        {
            if (await OllamaApi.SetKeepAlive(name, 0) is { } failure) error = failure;
        }
        Nap(models, "manual");
        tracker.Touch();
        await Refresh();
    }

    public void Load(string model) => KeepAlive(model, -1);
    public void Unload(string model) => KeepAlive(model, 0);

    private void KeepAlive(string model, int value)
    {
        if (BusyModel is not null) return;
        error = null;
        BusyModel = model;
        tracker.Touch();
        if (value != 0) { AutoReleased = null; ownLoads.Add(model); }
        else Nap(Loaded.Where(m => m.Name == model), "manual");
        Changed?.Invoke();
        _ = Run();

        async Task Run()
        {
            if (await OllamaApi.SetKeepAlive(model, value) is { } failure) error = failure;
            BusyModel = null;
            await Refresh();
        }
    }

    // MARK: descargar y borrar modelos

    public void Pull(string model)
    {
        model = model.Trim();
        if (model.Length == 0 || Pulling is not null || Power != Power.On) return;
        error = null;
        var job = new PullJob(model) { Status = "pulling manifest" };
        Pulling = job;
        Changed?.Invoke();
        _ = Run();

        async Task Run()
        {
            string? failure;
            var progress = new Progress<PullProgress>(p =>
            {
                job.Status = p.Status;
                job.Fraction = p.Fraction;
                Changed?.Invoke();
            });
            try { failure = await OllamaApi.Pull(model, progress, job.Cancel.Token); }
            catch (OperationCanceledException) { failure = null; }
            bool cancelled = job.Cancel.IsCancellationRequested;
            Pulling = null;
            if (failure is not null) error = tr("No se pudo descargar %@: %@", model, failure);
            else if (!cancelled)
            {
                var size = (await OllamaApi.Installed()).FirstOrDefault(m => Same(m.Name, model))?.Bytes ?? 0;
                Stats.Add(new StatEvent(DateTime.Now, StatKind.Download, model, size));
            }
            if (!cancelled) PullFinished?.Invoke(model, failure);
            Installed = await OllamaApi.Installed();
            await Refresh();
        }
    }

    /// «llama3.2» es «llama3.2:latest».
    private static bool Same(string a, string b)
    {
        static string Norm(string n) => n.Contains(':') ? n : n + ":latest";
        return string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);
    }

    public void CancelPull() => Pulling?.Cancel.Cancel();

    public void DeleteModel(string model)
    {
        if (BusyModel is not null || IsDemo) return;
        error = null;
        BusyModel = model;
        Changed?.Invoke();
        _ = Run();

        async Task Run()
        {
            var bytes = Installed.FirstOrDefault(m => m.Name == model)?.Bytes ?? 0;
            // Cargado: primero se saca de la memoria.
            if (Loaded.Any(m => m.Name == model))
            {
                Nap(Loaded.Where(m => m.Name == model), "manual");
                await OllamaApi.SetKeepAlive(model, 0);
            }
            if (await OllamaApi.Delete(model) is { } failure) error = tr("No se pudo borrar %@: %@", model, failure);
            else Stats.Add(new StatEvent(DateTime.Now, StatKind.Delete, model, bytes));
            BusyModel = null;
            // Aquí sí se acepta una lista vacía: se borró el último.
            Installed = await OllamaApi.Installed();
            await Refresh();
        }
    }
}
