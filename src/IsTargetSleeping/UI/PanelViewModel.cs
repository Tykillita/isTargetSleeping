using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

public sealed class Command(Action action) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => action();
}

/// Un comando que recibe el CommandParameter (el texto de un campo, p. ej.).
public sealed class ParamCommand(Action<object?> action) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => action(parameter);
}

public enum PanelView { Main, Activity, Settings }

/// Una opción del selector segmentado.
public sealed record Segment(string Title, bool IsSelected, ICommand Pick);

public sealed class PetChoice(string title, ImageSource preview, bool selected, ICommand pick) : INotifyPropertyChanged
{
    public string Title { get; } = title;
    public ImageSource Preview { get; } = preview;
    public ICommand Pick { get; } = pick;
    public bool IsSelected { get; private set; } = selected;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Select(bool value)
    {
        if (IsSelected == value) return;
        IsSelected = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
    }
}

/// Un ajuste con interruptor.
public sealed record SwitchRow(string Title, string? Detail, bool IsOn, ICommand Toggle, bool ShowDivider)
{
    public bool HasDetail => Detail is not null;
}

/// Una fila de «Modelos instalados».
public sealed class ModelRow
{
    public required string Name { get; init; }
    public required string Meta { get; init; }
    public bool Vision { get; init; }
    public bool Tools { get; init; }
    public bool IsLoaded { get; init; }
    public bool IsMain { get; init; }
    public bool IsBusy { get; init; }
    public bool CanLoad { get; init; }
    public bool CanDelete { get; init; }
    public bool IsConfirming { get; init; }
    public bool IsIdle => !IsConfirming;
    public bool ShowDivider { get; init; }
    public required ICommand Load { get; init; }
    public required ICommand ToggleMain { get; init; }
    public required ICommand AskDelete { get; init; }
    public required ICommand ConfirmDelete { get; init; }
    public required ICommand CancelDelete { get; init; }
    public required string ConfirmText { get; init; }
    public FontWeight Weight => IsLoaded ? FontWeights.SemiBold : FontWeights.Normal;
    public string LoadedLabel => tr("EN USO").ToLowerInvariant();
    public string VisionLabel => tr("visión");
    public string ToolsLabel => tr("herramientas");
    public string LoadLabel => tr("Cargar");
    public string LoadHelp => tr("Carga este modelo en memoria");
    public string MainHelp => IsMain
        ? tr("Modelo principal: se carga solo al encender Ollama. Clic para quitarlo")
        : tr("Hacerlo el modelo principal: se cargará solo al encender Ollama");
    public string DeleteHelp => tr("Borrar del disco");
    public string DeleteLabel => tr("Borrar");
    public string CancelLabel => tr("Cancelar");
}

/// Una tarjeta compacta de otro motor (llama.cpp, LM Studio).
public sealed class EngineRow
{
    public required string Name { get; init; }
    public bool Experimental { get; init; }
    public string ExperimentalLabel => tr("experimental");
    public required string State { get; init; }
    public required string Detail { get; init; }
    public bool HasDetail => Detail.Length > 0;
    public bool IsOn { get; init; }
    public bool IsBusy { get; init; }
    public bool CanToggle => !IsBusy;
    public required Brush DotBrush { get; init; }
    public required ICommand Toggle { get; init; }
    public bool CanSleep { get; init; }
    public required ICommand Sleep { get; init; }
    public string SleepLabel => tr("Dormir");
    public string? Error { get; init; }
    public bool HasError => Error is not null;
}

/// Ajustes de un motor: su tiempo de inactividad.
public sealed class EngineSettingsRow
{
    public required string Name { get; init; }
    public bool Experimental { get; init; }
    public string ExperimentalLabel => tr("experimental");
    public required List<Segment> IdleSegments { get; init; }
    public required string Hint { get; init; }
    public bool CanForget { get; init; }
    public required ICommand Forget { get; init; }
    public string ForgetLabel => tr("Olvidar");
    public bool ShowDivider { get; init; }
}

public sealed record GameRow(string Name, string Path, string? Tag, ICommand Remove, bool ShowDivider)
{
    public bool HasTag => Tag is not null;
    public string RemoveHelp => tr("Quitar");
}

public sealed record LinkRow(string Url, ICommand Copy)
{
    public string CopyHelp => tr("Copiar");
}

public sealed record ClientRow(string Name, string Detail, ImageSource? Icon, bool ShowDivider)
{
    public bool HasIcon => Icon is not null;
    public bool NoIcon => Icon is null;
    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
}

public sealed record HistoryRow(string Glyph, string Text, string Time, Brush GlyphBrush);

public sealed record StatTile(string Value, string Label);
public sealed record ProcessSummaryRow(string Name, string Memory, string Count, ImageSource? Icon);

/// Todo lo que muestra el panel, calculado a partir del estado: la interfaz
/// (ContentView.xaml) solo se enlaza a estas propiedades.
public sealed class PanelViewModel : INotifyPropertyChanged
{
    public Supervisor Supervisor { get; }
    public OllamaController Ollama { get; }
    public Prefs Prefs { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public PanelViewModel(Supervisor supervisor, PanelView view = PanelView.Main)
    {
        Supervisor = supervisor;
        Ollama = supervisor.Ollama;
        Prefs = supervisor.Prefs;
        this.view = view;
        Ollama.Changed += Notify;
        Prefs.Changed += Notify;
        Supervisor.Changed += Notify;
        Theme.Changed += Notify;

        CornerCommand = new Command(() => Show(this.view == PanelView.Main ? PanelView.Settings : PanelView.Main));
        SideCommand = new Command(() => Show(this.view == PanelView.Activity ? PanelView.Settings : PanelView.Activity));
        OpenProcesses = new Command(() => ProcessesRequested?.Invoke());
        processSummaryTimer.Tick += async (_, _) => await RefreshProcessSummary();
        TogglePower = new Command(Ollama.Toggle);
        DismissError = new Command(() => Ollama.Error = null);
        DismissHotKeyError = new Command(() => Prefs.HotKeyError = null);
        AcceptLogin = new Command(() => Prefs.AnswerLogin(true));
        DeclineLogin = new Command(() => Prefs.AnswerLogin(false));
        FreeModel = new Command(() => { if (Ollama.Loaded.FirstOrDefault() is { } m) Ollama.Unload(m.Name); });
        ToggleHotKey = new Command(() => Prefs.HotKeyEnabled = !Prefs.HotKeyEnabled);
        ToggleSleepHotKey = new Command(() => Prefs.SleepHotKeyEnabled = !Prefs.SleepHotKeyEnabled);
        ToggleLogin = new Command(() => { try { Prefs.ToggleLogin(); } catch (Exception e) { Ollama.Error = tr("Inicio de sesión: %@", e.Message); } });
        ToggleWatchdog = new Command(() => Prefs.Toggle(PrefKeys.Watchdog));
        ToggleGameMode = new Command(() => Supervisor.SetGameMode(!Supervisor.GameModeEnabled));
        ToggleGameRestore = new Command(() => Prefs.Toggle(PrefKeys.GameRestore));
        ToggleUpdateCheck = new Command(() => { Prefs.Toggle(PrefKeys.UpdateCheck); _ = Supervisor.CheckUpdates(); });
        OpenLog = new Command(() => Prefs.OpenLog?.Invoke());
        OpenEngineLog = new Command(() => Paths.OpenLog(Supervisor.LlamaCpp.LogPath));
        PowerAnyway = new Command(Supervisor.PowerAnyway);
        NotAGame = new Command(Supervisor.IgnoreCurrentGame);
        InstallUpdate = new Command(() => _ = Supervisor.InstallUpdate());
        CheckUpdatesNow = new Command(() => _ = Supervisor.CheckUpdates(manual: true));
        CancelUpdate = new Command(Supervisor.CancelUpdate);
        OpenUpdateNotes = new Command(() => { if (Supervisor.Update is { } update) Paths.Open(update.Page); });
        OpenUpdateDownloads = new Command(() => Paths.Open($"https://github.com/{AppInfo.UpdateRepo}/releases"));
        AddGame = new Command(PickGame);
        ToggleAddModel = new Command(() => { addingModel = !addingModel; Notify(); });
        PullModel = new ParamCommand(p =>
        {
            if (p is string name && name.Trim().Length > 0) { Ollama.Pull(name); addingModel = false; Notify(); }
        });
        CancelPull = new Command(Ollama.CancelPull);
        ToggleTrayPin = new Command(() => { TrayPin.SetPinned(!TrayPin.Pinned); Notify(); });
        ToggleReducedMotion = new Command(Prefs.ToggleReducedMotion);
        ToggleTaskbarPet = new Command(() => Prefs.Toggle(PrefKeys.TaskbarPet, false));
        TogglePetInteractive = new Command(() => Prefs.Toggle(PrefKeys.TaskbarPetInteractive));
        CleanNow = new Command(() =>
        {
            if (Supervisor.Agent == AgentStatus.Ready) _ = Supervisor.Clean(CleanReason.Manual, fromPanel: true);
            else Show(PanelView.Settings);
        });
        CleanAndSleep = new Command(() => _ = Supervisor.Clean(CleanReason.Manual, fromPanel: true, sleepModels: true));
        AgentAction = new Command(() => _ = Supervisor.InstallAgent());
        RemoveAgent = new Command(() => _ = Supervisor.UninstallAgent());
        ImportMemReduct = new Command(Supervisor.ImportMemReduct);
        AskReplaceMemReduct = new Command(() => { confirmingReplace = true; Notify(); });
        CancelReplaceMemReduct = new Command(() => { confirmingReplace = false; Notify(); });
        ReplaceMemReduct = new Command(() => { confirmingReplace = false; _ = Supervisor.ReplaceMemReduct(); });
        RestoreMemReduct = new Command(() => _ = Supervisor.RestoreMemReduct());
        PullSuggestions = new[] { "llama3.2", "qwen3", "gemma3", "phi4" }
            .Select(name => new Segment(name, false, new Command(() => { Ollama.Pull(name); addingModel = false; Notify(); })))
            .ToList();
        Rebuild();
    }

    public void Notify()
    {
        Rebuild();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public void Show(PanelView v)
    {
        view = v;
        confirmingDelete = null;
        UpdateProcessSampling();
        Notify();
    }

    public event Action? ProcessesRequested;
    public ICommand OpenProcesses { get; }
    private readonly ProcessMonitor processSummaryMonitor = new();
    private readonly System.Windows.Threading.DispatcherTimer processSummaryTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool panelVisible, summaryBusy;
    public bool ProcessSamplingActive => panelVisible && ShowingActivity && !Ollama.IsDemo;
    public List<ProcessSummaryRow> ProcessSummaryRows { get; private set; } = [];
    public string ProcessesTitle => tr("Reconocimiento de procesos");
    public string OpenProcessesLabel => tr("Ver todos los procesos");
    public string ProcessesHint => (processSummaryUsesPrivate is { } usePrivate
        ? (usePrivate ? tr("RAM privada residente") : tr("RAM residente total")) + " · " : "")
        + tr("Las cinco aplicaciones con más RAM. Despliega sus procesos en la ventana para ver detalles o finalizarlos.");
    public string ProcessesEmpty => tr("El listado se actualiza mientras esta vista está abierta.");
    public bool NoProcessSummary => ProcessSummaryRows.Count == 0;
    private bool? processSummaryUsesPrivate;

    public void SetPanelVisible(bool visible)
    {
        panelVisible = visible;
        UpdateProcessSampling();
    }

    private void UpdateProcessSampling()
    {
        if (ProcessSamplingActive) { processSummaryTimer.Start(); _ = RefreshProcessSummary(); }
        else processSummaryTimer.Stop();
    }

    private async Task RefreshProcessSummary()
    {
        if (!ProcessSamplingActive || summaryBusy) return;
        summaryBusy = true;
        try
        {
            var sample = await processSummaryMonitor.CaptureAsync(Supervisor.GetProcessProtectionPids());
            var icons = await AppIcons.LoadAsync(sample.Groups.Take(5).Select(g => g.Path));
            if (!ProcessSamplingActive) return;
            processSummaryUsesPrivate = sample.UsesPrivateWorkingSet;
            ProcessSummaryRows = sample.Groups.Where(g => g.RamBytes.HasValue).Take(5).Select(g =>
                new ProcessSummaryRow(g.Name, g.RamBytes!.Value.MemoryGB(), tr("%d procesos", g.Count),
                    g.Path != null ? icons.GetValueOrDefault(g.Path) : null)).ToList();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProcessSummaryRows)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NoProcessSummary)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProcessesHint)));
        }
        catch (Exception e) { AppLog.Write($"muestreo de procesos: {e.Message}"); }
        finally { summaryBusy = false; }
    }

    private Theme T => Theme.Current;
    private static Brush B(Color c) => Theme.Brush(c);
    private Power P => Ollama.Power;

    /// Alto máximo del cuerpo (lo fija la ventana según la pantalla): lo que no
    /// quepa se desplaza. Sin ventana (capturas), sin límite.
    private double bodyMaxHeight = double.PositiveInfinity;
    public double BodyMaxHeight
    {
        get => bodyMaxHeight;
        set
        {
            if (Math.Abs(value - bodyMaxHeight) < 1) return;
            bodyMaxHeight = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BodyMaxHeight)));
        }
    }

    // MARK: comandos

    public ICommand CornerCommand { get; }
    public ICommand SideCommand { get; }
    public ICommand TogglePower { get; }
    public ICommand DismissError { get; }
    public ICommand DismissHotKeyError { get; }
    public ICommand AcceptLogin { get; }
    public ICommand DeclineLogin { get; }
    public ICommand FreeModel { get; }
    public ICommand ToggleHotKey { get; }
    public ICommand ToggleSleepHotKey { get; }
    public ICommand ToggleLogin { get; }
    public ICommand ToggleWatchdog { get; }
    public ICommand ToggleGameMode { get; }
    public ICommand ToggleGameRestore { get; }
    public ICommand ToggleUpdateCheck { get; }
    public ICommand OpenLog { get; }
    public ICommand OpenEngineLog { get; }
    public ICommand PowerAnyway { get; }
    public ICommand NotAGame { get; }
    public ICommand InstallUpdate { get; }
    public ICommand AddGame { get; }
    public ICommand ToggleAddModel { get; }
    public ICommand PullModel { get; }
    public ICommand CancelPull { get; }
    public ICommand CleanNow { get; }
    public ICommand CleanAndSleep { get; }
    public ICommand AgentAction { get; }
    public ICommand RemoveAgent { get; }
    public ICommand ImportMemReduct { get; }
    public ICommand AskReplaceMemReduct { get; }
    public ICommand CancelReplaceMemReduct { get; }
    public ICommand ReplaceMemReduct { get; }
    public ICommand RestoreMemReduct { get; }

    // MARK: cabecera

    private PanelView view;
    public bool ShowingMain => view == PanelView.Main;
    public bool ShowingActivity => view == PanelView.Activity;
    public bool ShowingSettings => view == PanelView.Settings;
    public bool ShowingSecondary => !ShowingMain;

    public string HeaderTitle => view switch
    {
        PanelView.Settings => tr("Ajustes"),
        PanelView.Activity => tr("Actividad"),
        _ => AppInfo.Name,
    };

    public string HeaderGlyph => view == PanelView.Settings ? "" : "";   // Settings / AreaChart
    public Brush LogoBrush => B(P == Power.On ? T.On : T.Muted);

    public string Subtitle
    {
        get
        {
            if (view == PanelView.Settings) return $"v{AppInfo.Version}";
            if (view == PanelView.Activity) return tr("Historial de %d días, en este PC", StatsStore.KeepDays);
            if (Ollama.Version is { } v) return $"v{v} · {Ollama.Backend.Summary}";
            return Ollama.Backend.Summary;
        }
    }

    // Los dos botones de la cabecera. La esquina es siempre la misma: el engranaje en
    // la vista principal y la X (volver) en Actividad y en Ajustes. El de al lado lleva
    // a la otra vista: Actividad desde la principal y desde Ajustes, Ajustes desde Actividad.
    private const string SettingsGlyph = "", ChartGlyph = "", CloseGlyph = "";
    public string CornerGlyph => ShowingMain ? SettingsGlyph : CloseGlyph;
    public Brush CornerBackground => ShowingMain ? Brushes.Transparent : B(T.Track);
    public string CornerHelp => view switch
    {
        PanelView.Settings => tr("Cerrar ajustes"),
        PanelView.Activity => tr("Cerrar actividad"),
        _ => tr("Ajustes"),
    };
    public string SideGlyph => ShowingActivity ? SettingsGlyph : ChartGlyph;
    public string SideHelp => ShowingActivity ? tr("Ajustes") : tr("Actividad");

    public string PillText => P switch
    {
        Power.On => "ON",
        Power.Starting or Power.Stopping => "···",
        Power.Off => "OFF",
        _ => "N/D",
    };

    public Brush PillForeground => B(P switch
    {
        Power.On => T.On,
        Power.Starting or Power.Stopping => T.Warm,
        Power.Off => T.Muted,
        _ => T.Accent,
    });

    public Brush PillBackground => B(P switch
    {
        Power.On => T.OnSoft,
        Power.Starting or Power.Stopping => T.WarmSoft,
        Power.Off => T.Track,
        _ => T.AccentSoft,
    });

    // MARK: botón grande

    private Color HeroColor => P switch
    {
        Power.On => T.On,
        Power.Starting or Power.Stopping => T.Warm,
        Power.Off => T.Muted,
        _ => T.Accent,
    };

    public Brush HeroTint => B(HeroColor);
    /// El halo es vidrio neutro, sin resplandor de color: el color va solo en el aro y el símbolo.
    public Brush HeroFill => B(T.Track);
    public bool IsTransition => P.IsTransition();
    public bool CanToggle => !P.IsTransition();
    /// El símbolo de encendido (o la flecha de descarga) en una caja de 20×20.
    private static readonly Geometry PowerIcon = Geometry.Parse("M 5.1,5.2 A 8,8 0 1 0 14.9,5.2 M 10,2 V 10.5");
    private static readonly Geometry DownloadIcon = Geometry.Parse("M 10,2.5 V 16.5 M 4.5,11 L 10,16.5 L 15.5,11");
    public Geometry HeroIcon => P == Power.Missing ? DownloadIcon : PowerIcon;

    public string HeroTitle => P switch
    {
        Power.On => tr("Encendido"),
        Power.Starting => tr("Arrancando…"),
        Power.Stopping => tr("Apagando…"),
        Power.Off => tr("Apagado"),
        _ => tr("Sin Ollama"),
    };

    public Brush HeroTitleBrush => B(P == Power.Off ? T.Secondary : T.Fg);

    public string HeroDetail => P switch
    {
        Power.On => Ollama.Loaded.FirstOrDefault() is { } m
            ? tr("%@ en memoria", m.Name)
            : tr("Sin modelo en memoria. Se carga con el primer mensaje."),
        Power.Starting => tr("Esperando a la API en %@", OllamaApi.HostLabel),
        Power.Stopping => tr("Liberando la memoria del modelo"),
        Power.Off => tr("Las apps que usan Ollama no responden mientras esté apagado."),
        _ => tr("No lo encuentro en este PC. Pulsa para descargarlo de ollama.com."),
    };

    public string HeroHelp => P == Power.On ? tr("Apagar Ollama") : P == Power.Missing ? tr("Descargar Ollama") : tr("Encender Ollama");

    // MARK: avisos

    public string? ErrorText => Ollama.Error;
    public bool HasError => Ollama.Error is not null;
    public string? HotKeyErrorText => Prefs.HotKeyError;
    public bool HasHotKeyError => Prefs.HotKeyError is not null;
    public bool ShowLoginOffer => Prefs.ShouldOfferLogin;
    public string LoginOfferTitle => tr("¿Abrir al iniciar sesión?");
    public string LoginOfferDetail => tr("Siempre a mano en la bandeja.");
    public string NoLabel => tr("No");
    public string YesLabel => tr("Sí");

    // Modo juego
    public bool ShowGameBanner => Supervisor.Game.Active;
    public string GameBannerTitle => tr("Jugando a %@", Supervisor.Game.Game ?? "");
    public string GameBannerDetail => P is Power.On or Power.Starting
        ? tr("Ollama encendido a mano: se respeta hasta que termines.")
        : tr("Ollama apagado para dejarle la memoria al juego. Se enciende al salir.");
    public bool ShowPowerAnyway => P is Power.Off;
    public string PowerAnywayLabel => tr("Encender igualmente");
    public bool ShowNotAGame => Supervisor.CurrentGame is not null;
    public string NotAGameLabel => tr("No es un juego");

    // Actualización
    public bool ShowUpdate => Supervisor.Update is not null;
    public string UpdateTitle => tr("Nueva versión %@", Supervisor.Update?.Version ?? "");
    public string UpdateDetail => Supervisor.UpdateStage switch
    {
        UpdateStage.Downloading => Supervisor.UpdateProgress is { } p ? tr("Descargando… %d %%", (int)(p * 100)) : tr("Descargando…"),
        UpdateStage.Verifying => tr("Comprobando la descarga…"),
        UpdateStage.Applying => tr("Instalando y reiniciando…"),
        _ => tr("Tienes la %@. Se descarga, se comprueba y se reinicia.", AppInfo.Version),
    };
    public bool UpdateIdle => !Supervisor.UpdateBusy;
    public bool CanInstallUpdate => UpdateIdle && Supervisor.UpdateStatus != UpdateStatus.Checking;
    public bool CanCancelUpdate => Supervisor.UpdateStage is UpdateStage.Downloading or UpdateStage.Verifying;
    public string UpdateLabel => tr("Actualizar");
    public string UpdateNotesLabel => tr("Ver novedades");
    public string UpdateCancelLabel => tr("Cancelar descarga");
    public ICommand CheckUpdatesNow { get; }
    public ICommand CancelUpdate { get; }
    public ICommand OpenUpdateNotes { get; }
    public ICommand OpenUpdateDownloads { get; }
    public string UpdateDownloadsLabel => tr("Descargar desde GitHub");
    public string CheckUpdatesNowLabel => tr("Buscar actualizaciones ahora");
    public bool CanCheckUpdates => Supervisor.UpdateStatus != UpdateStatus.Checking && !Supervisor.UpdateBusy;
    public string UpdateCheckStatus => Supervisor.UpdateStatus switch
    {
        UpdateStatus.Checking => tr("Buscando actualizaciones…"),
        UpdateStatus.UpToDate => tr("Estás al día. Versión %@.", AppInfo.Version),
        UpdateStatus.Available => tr("Nueva versión %@ disponible.", Supervisor.Update?.Version ?? ""),
        UpdateStatus.NoRelease => tr("Todavía no hay versiones publicadas."),
        UpdateStatus.Incompatible => tr("La última versión no tiene un paquete compatible para este Windows."),
        UpdateStatus.RateLimited => tr("GitHub pide esperar antes de volver a buscar.")
            + (Supervisor.UpdateRetryAt is { } at ? " " + tr("Puedes volver a buscar a las %@.", at.ToLocalTime().ToString("t")) : ""),
        UpdateStatus.Error => "", // El detalle y la descarga manual se muestran debajo.
        _ => "",
    };
    public string? UpdateMessage => Supervisor.UpdateMessage;
    public bool HasUpdateMessage => UpdateMessage is not null && Supervisor.UpdateStatus != UpdateStatus.RateLimited;

    // MARK: memoria

    public bool ShowMemory => P != Power.Missing;
    public string MemoryTitle => tr("Memoria del PC");
    private SystemMemory Mem => Ollama.Memory;
    private long ModelRam => Ollama.ModelRam;

    public string PressureText => Mem.CommitCritical && Mem.PhysicalPressure == SystemMemory.Level.Normal
        ? tr("presión crítica de memoria comprometida") : Mem.Pressure switch
    {
        SystemMemory.Level.Normal => tr("presión normal"),
        SystemMemory.Level.Warning => tr("presión alta"),
        _ => tr("presión crítica"),
    };
    public bool HasCommitPressure => Mem.CommitCritical && Mem.PhysicalPressure == SystemMemory.Level.Normal;
    public string CommitPressureHint => tr("El compromiso de memoria está cerca del límite; la RAM física disponible es suficiente. No inicia limpiezas automáticas.");

    public Brush PressureBrush => B(Mem.Pressure switch
    {
        SystemMemory.Level.Normal => T.On,
        SystemMemory.Level.Warning => T.Warm,
        _ => T.Danger,
    });

    public double UsedFraction => Mem.Total > 0 ? (double)Mem.Used / Mem.Total : 0;
    public double ModelFraction => Mem.Total > 0 ? (double)ModelRam / Mem.Total : 0;
    public string LegendModel => $"{tr("Modelo")} {Num(ModelRam)}";
    public string LegendOther => $"{tr("Resto")} {Num(Math.Max(0, Mem.Used - ModelRam))}";
    public string UsedLabel => $"{Mem.Used.MemoryGB()} / {Math.Round(Mem.Installed / 1_073_741_824.0)} GB";
    private static string Num(long bytes) => bytes.MemoryGB().Replace(" GB", "");

    // GPU
    public bool ShowGpu => Gpu.Adapter is not null && Ollama.Gpu is not null;
    public string GpuTitle => $"GPU · {Gpu.Adapter?.ShortName}";
    private long GpuTotal => Gpu.Adapter?.Total ?? 1;
    private GpuUsage GpuNow => Ollama.Gpu ?? default;
    public double GpuUsedFraction => (double)GpuNow.Used / GpuTotal;
    public double GpuModelFraction => (double)GpuNow.Model / GpuTotal;
    public string GpuLegendModel => $"{tr("Modelo")} {Num(GpuNow.Model)}";
    public string GpuLegendOther => $"{tr("Resto")} {Num(Math.Max(0, GpuNow.Used - GpuNow.Model))}";
    public string GpuUsedLabel => $"{GpuNow.Used.MemoryGB()} / {Math.Round(GpuTotal / 1_073_741_824.0)} GB";

    public bool HasLoaded => Ollama.Loaded.Count > 0;
    public bool NoLoaded => !HasLoaded;
    public string LoadedName => Ollama.Loaded.FirstOrDefault()?.Name ?? "";
    public bool FreeBusy => Ollama.Loaded.FirstOrDefault() is { } m && Ollama.BusyModel == m.Name;
    public bool FreeIdle => !FreeBusy;
    public bool FreeEnabled => Ollama.BusyModel is null;
    public string FreeLabel => tr("Liberar");
    public string FreeHelp => tr("Saca el modelo de la memoria sin apagar Ollama");

    public string IdleLine
    {
        get
        {
            if (Ollama.Loaded.FirstOrDefault() is not { } m) return "";
            var parts = new List<string>();
            if (Ollama.IdleLimit is null) parts.Add(m.Bytes.MemoryGB());
            if (m.Vram > 0) parts.Add(tr("%@ en GPU", m.Vram.MemoryGB()));
            if (Ollama.IdleSeconds is { } idle && idle >= 60) parts.Add(tr("sin uso %d min", Minutes(idle)));
            if (Ollama.IdleLimit is { } limit && Ollama.IdleSeconds is { } seconds)
            {
                double left = Math.Max(0, limit - seconds);
                parts.Add(left < 60 ? tr("se libera en <1 min") : tr("se libera en %d min", (int)Math.Ceiling(left / 60)));
            }
            else if (m.Context is { } ctx) parts.Add(tr("contexto %dK", ctx / 1024));
            return string.Join(" · ", parts);
        }
    }

    public bool HasWakeLine => Ollama.LastWake is not null;
    public string WakeLine => Ollama.LastWake is { } w ? tr("Lo despertó %@ · %@", w.Subject ?? "", Supervisor.Ago(w.At)) : "";

    public string MemoryNote
    {
        get
        {
            if (Ollama.AutoReleased is { } released)
                return tr("Liberado solo a las %@ tras %d min sin uso", released.At.ToString("t"), Ollama.IdleReleaseMinutes);
            return P == Power.On ? tr("Ningún modelo en memoria") : tr("Ollama apagado · 0 GB en uso");
        }
    }

    private static int Minutes(double seconds) => Math.Max(0, (int)(seconds / 60));

    // Liberar RAM
    public bool ShowCleanButton => Supervisor.Agent != AgentStatus.Unavailable;
    public string CleanLabel => tr("Liberar RAM");
    public string CleanHelp => Supervisor.Agent == AgentStatus.Ready
        ? tr("Libera la RAM sin tocar los modelos ni el juego. Clic derecho: también dormir los modelos.")
        : tr("Actívalo en Ajustes: pide permiso de administrador una vez.");
    public string CleanAndSleepLabel => tr("Liberar RAM y dormir los modelos");
    public bool IsCleaning => Supervisor.Cleaning;
    public bool CleanIdle => !Supervisor.Cleaning;
    public bool HasLastClean => Supervisor.LastCleanResult is not null || Supervisor.LastClean is not null;
    public string LastCleanText => Supervisor.LastCleanResult is { } result
        ? result.Outcome switch
        {
            CleanOutcome.Failed => tr("La limpieza falló"),
            CleanOutcome.Partial => tr("Limpieza parcial · cambio observado %@", result.Freed.MemoryGB()),
            CleanOutcome.NoWork => tr("Sin procesos adecuados para limpiar"),
            CleanOutcome.Pending => tr("Limpieza pendiente: el agente sigue activo"),
            _ => tr("Cambio observado: %@ a los 5 s", result.Freed.MemoryGB()),
        }
        : Supervisor.LastClean is { } c
        ? tr("%@ liberados · %@", c.Freed.MemoryGB(), Supervisor.Ago(c.At))
        : "";

    // MARK: otros motores

    public ObservableCollection<EngineRow> EngineRows { get; } = [];
    public bool HasEngines => EngineRows.Count > 0;
    private string enginesSignature = "";

    // MARK: modelos

    public bool ShowModels => P != Power.Missing;
    public string ModelsTitle => tr("Modelos instalados");
    public bool ModelsEmpty => Ollama.Installed.Count == 0;
    public bool HasModels => !ModelsEmpty;
    public string ModelsEmptyText => P == Power.On ? tr("No hay modelos. Pulsa + para descargar uno.") : tr("Enciende Ollama para ver los modelos");
    public double ModelsOpacity => P == Power.On ? 1 : 0.55;
    /// Con más de 6 modelos la lista se desplaza en vez de crecer.
    public double ModelsMaxHeight => Ollama.Installed.Count > 6 ? 300 : double.PositiveInfinity;
    public ObservableCollection<ModelRow> Models { get; } = [];
    private string modelsSignature = "";
    private string? confirmingDelete;

    public bool CanAddModel => P == Power.On && Ollama.Pulling is null;
    public string AddModelHelp => tr("Descargar un modelo");
    public string AddModelGlyph => addingModel ? "" : "";   // Cancel / Add
    private bool addingModel;
    public bool AddingModel => addingModel && CanAddModel;
    public string PullPlaceholder => tr("nombre, p. ej. llama3.2");
    public string PullLabel => tr("Descargar");
    public List<Segment> PullSuggestions { get; }
    public bool IsPulling => Ollama.Pulling is not null;
    public string PullName => Ollama.Pulling?.Name ?? "";
    public string PullStatus => Ollama.Pulling is { } j ? PullStatusText(j.Status) : "";
    public double PullFraction => Ollama.Pulling?.Fraction ?? 0;
    public string PullPercent => Ollama.Pulling?.Fraction is { } f ? $"{(int)(f * 100)} %" : "";
    public string CancelLabel => tr("Cancelar");

    private static string PullStatusText(string status) => status switch
    {
        "pulling manifest" => tr("Leyendo el manifiesto…"),
        "verifying sha256 digest" => tr("Comprobando…"),
        "writing manifest" or "removing any unused layers" => tr("Terminando…"),
        "success" => tr("Listo"),
        _ when status.StartsWith("pulling") => tr("Descargando…"),
        _ => status,
    };

    // MARK: actividad

    public string WeekTitle => tr("Esta semana");
    public List<StatTile> WeekTiles { get; private set; } = [];
    public string ChartTitle => tr("Últimos 30 min");
    public List<MemorySample> Samples => Ollama.Stats.Samples();
    public string ChartTotal => tr("máx. %@", $"{Math.Round(Mem.Installed / 1_073_741_824.0)} GB");
    public string ChartStart => tr("hace 30 min");
    public string ChartEnd => tr("ahora");
    public string ChartUsed => tr("RAM en uso");
    public string ChartModel => tr("Modelo");
    public string ClientsTitle => tr("Quién lo despierta");
    public List<ClientRow> ClientRows { get; private set; } = [];
    public bool HasClients => ClientRows.Count > 0;
    public bool NoClients => !HasClients;
    public string ClientsEmpty => tr("Aún nadie. Aquí saldrán las apps que cargan un modelo (Obsidian, Cursor…).");
    public string HistoryTitle => tr("Historial");
    public List<HistoryRow> HistoryRows { get; private set; } = [];
    public bool HasHistory => HistoryRows.Count > 0;
    public bool NoHistory => !HasHistory;
    public string HistoryEmpty => tr("Sin eventos todavía.");
    public bool HasCleanObservation => Supervisor.LastCleanResult is { Before: not null };
    public string CleanObservationTitle => tr("Resultado de la limpieza");
    private static string AreaText(CleanAreas area) => area switch
    {
        CleanAreas.WorkingSets => tr("Memoria de trabajo de las apps"),
        CleanAreas.SystemFileCache => tr("Caché de archivos del sistema"),
        CleanAreas.StandbyLowPriority => tr("Lista en espera de prioridad baja"),
        CleanAreas.Standby => tr("Lista en espera completa"),
        CleanAreas.ModifiedList => tr("Páginas modificadas"),
        CleanAreas.CombineLists => tr("Combinar páginas iguales"),
        CleanAreas.RegistryCache => tr("Caché del registro"),
        CleanAreas.ModifiedFileCache => tr("Caché de archivos modificados"), _ => area.ToString(),
    };
    public string CleanObservationText
    {
        get
        {
            if (Supervisor.LastCleanResult is not { Before: { } before } r) return "";
            string Delta(CleanMemorySample? s) => s is null ? tr("pendiente") : (before.Used - s.Used).MemoryGB();
            return tr("Cambio observado · al terminar %@ · 5 s %@ · 30 s %@", Delta(r.Immediate), Delta(r.AfterFiveSeconds), Delta(r.AfterThirtySeconds))
                + "\n" + tr("Duración: %@ s", (r.DurationMilliseconds / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
                + "\n" + tr("%d procesos tratados · %d omitidos · %d fallidos", r.ProcessesTreated, r.ProcessesSkipped, r.ProcessesFailed)
                + (r.Failed != CleanAreas.None ? "\n" + tr("Zonas fallidas: %@", string.Join(", ", Enum.GetValues<CleanAreas>()
                    .Where(a => (int)a > 0 && ((int)a & ((int)a - 1)) == 0 && r.Failed.HasFlag(a)).Select(AreaText))) : "")
                + (r.Error is not null ? "\n" + r.Error : "")
                + (r.Errors.Count > 0 ? "\n" + string.Join("\n", r.Errors.Take(8).Select(e =>
                    $"{(Enum.TryParse<CleanAreas>(e.Operation, out var area) ? AreaText(area) : e.Operation == "Privilege" ? tr("Privilegio") : e.Operation)} {e.Target} · PID {e.Pid?.ToString() ?? "—"} · Win32 {e.Win32Error?.ToString() ?? "—"} · NTSTATUS {e.NtStatus?.ToString("X8") ?? "—"}")) : "")
                + (r.Errors.Count > 8 ? "\n" + tr("%d errores adicionales en el informe del agente", r.Errors.Count - 8) : "")
                + "\n" + tr("Memoria disponible: %@ · comprometida: %@ / %@", Mem.Available.MemoryGB(), Mem.Committed.MemoryGB(), Mem.CommitLimit.MemoryGB());
        }
    }

    private void BuildActivity()
    {
        var week = Ollama.Stats.Week(DateTime.Now);
        WeekTiles =
        [
            new(week.Recovered.Gigabytes(), tr("recuperados")),
            new(week.Naps.ToString(), week.Naps == 1 ? tr("siesta") : tr("siestas")),
            new(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.#} h", week.LoadedHours), tr("con modelo")),
            new(week.Restarts.ToString(), week.Restarts == 1 ? tr("reinicio") : tr("reinicios")),
        ];
        var clients = Ollama.Stats.Clients().Take(6).ToList();
        ClientRows = clients.Select((c, i) => new ClientRow(c.App,
            $"{(c.Wakes == 1 ? tr("1 vez") : tr("%d veces", c.Wakes))} · {Supervisor.Ago(c.Last)}",
            AppIcons.For(c.Path), i > 0)).ToList();
        HistoryRows = Ollama.Stats.Recent(12).Select(Describe).ToList();
    }

    private HistoryRow Describe(StatEvent e)
    {
        string gb = e.Bytes.Gigabytes();
        var (glyph, text, color) = e.Kind switch
        {
            StatKind.Nap => ("", e.Detail switch
            {
                "manual" => tr("Dormiste %@ · %@", e.Subject ?? "", gb),
                "game" => tr("El modo juego durmió %@ · %@", e.Subject ?? "", gb),
                _ => tr("%@ se durmió solo · %@", e.Subject ?? "", gb),
            }, T.Secondary),
            StatKind.Wake => ("", e.Subject is null
                ? tr("Se cargó %@", e.Model ?? "")
                : tr("%@ despertó a %@", e.Subject, e.Model ?? tr("el modelo")), T.On),
            StatKind.Crash => ("", e.Detail == "hang" ? tr("Ollama se colgó") : tr("Ollama se cayó"), T.Warm),
            StatKind.Restart => ("", tr("El vigilante reinició Ollama"), T.Secondary),
            StatKind.GiveUp => ("", tr("El vigilante se rindió"), T.Danger),
            StatKind.GameOn => ("", tr("Empezaste a jugar a %@", e.Subject ?? ""), T.Secondary),
            StatKind.GameOff => ("", tr("Terminaste de jugar a %@", e.Subject ?? ""), T.Secondary),
            StatKind.Download => ("", tr("Descargaste %@ · %@", e.Subject ?? "", gb), T.Secondary),
            StatKind.ProcessTerminated => ("", tr("Finalización de %@ · %@", e.Subject ?? tr("procesos"), ProcessOutcomeText(e.ProcessAction?.Outcome)), T.Secondary),
            StatKind.Clean when e.Clean?.Outcome == CleanOutcome.Failed => ("", tr("La limpieza falló"), T.Danger),
            StatKind.Clean when e.Clean?.Outcome == CleanOutcome.NoWork => ("", tr("Sin procesos adecuados para limpiar"), T.Secondary),
            StatKind.Clean when e.Clean != null => ("", tr("Limpieza · cambio observado %@", e.Bytes.MemoryGB()), T.Secondary),
            StatKind.Clean => ("", e.Detail == "manual"
                ? tr("Liberaste %@ de RAM", e.Bytes.MemoryGB())
                : tr("Limpieza automática · %@", e.Bytes.MemoryGB()), T.Secondary),
            _ => ("", tr("Borraste %@ · %@", e.Subject ?? "", gb), T.Secondary),
        };
        return new HistoryRow(glyph, text, Supervisor.Ago(e.At), B(color));
    }

    private static string ProcessOutcomeText(ProcessActionOutcome? outcome) => outcome switch
    {
        ProcessActionOutcome.Success => tr("completada"), ProcessActionOutcome.Partial => tr("parcial"),
        ProcessActionOutcome.NoWork => tr("sin trabajo"), ProcessActionOutcome.Pending => tr("pendiente"),
        ProcessActionOutcome.Cancelled => tr("cancelada"), _ => tr("fallida"),
    };

    // MARK: ajustes

    private static readonly int[] IdleChoices = [0, 5, 15, 30, 60];

    public string OllamaSectionDetail => Ollama.Version is { } v ? $"v{v}" : "";
    public string IdleTitle => tr("Liberar el modelo sin uso");
    public string IdleHint => tr("Ollama sigue encendido y lo recarga con el siguiente mensaje.");
    public List<Segment> IdleSegments { get; private set; } = [];

    public bool ShowMechanisms => Ollama.Mechanisms.Count > 1;
    public string MechanismTitle => tr("Encender con");
    public List<Segment> MechanismSegments { get; private set; } = [];

    public string MechanismHint
    {
        get
        {
            var pref = Ollama.Preferred ?? Ollama.Backend;
            if (P.IsUp() && Ollama.Backend.Key != pref.Key)
                return tr("Ahora corre con %@. Se usará %@ la próxima vez que lo enciendas.", Ollama.Backend.KindLabel, pref.KindLabel);
            return tr("Así se enciende desde el panel y con el atajo.");
        }
    }

    public string WatchdogTitle => tr("Reiniciar Ollama si se cae");
    public string WatchdogDetail => tr("Si deja de responder, lo reinicio (hasta 3 veces en 10 min).");
    public bool WatchdogOn => Prefs.Switch(PrefKeys.Watchdog);

    public string AutomaticTitle => tr("Automático");
    public string GameModeTitle => tr("Modo juego");
    public string GameModeDetail => tr("Apaga Ollama y los otros motores al abrir un juego de Steam, Epic, Riot, EA, GOG o Xbox.");
    public bool GameModeOn => Supervisor.GameModeEnabled;
    public string GameRestoreTitle => tr("Volver a encender al salir");
    public string GameRestoreDetail => tr("Solo lo que estaba encendido y no tocaste mientras jugabas.");
    public bool GameRestoreOn => Prefs.Switch(PrefKeys.GameRestore);
    public string GamesTitle => tr("Juegos propios");
    public string AddGameLabel => tr("Añadir juego…");
    public List<GameRow> GameRows { get; private set; } = [];
    public bool HasGameRows => GameRows.Count > 0;

    // Liberar RAM
    public string CleanSectionTitle => tr("Liberar RAM");
    public bool ShowCleanSection => Supervisor.Agent != AgentStatus.Unavailable;
    public string AgentTitle => Supervisor.Agent switch
    {
        AgentStatus.Ready => tr("Activado"),
        AgentStatus.Outdated => tr("El agente es de otra versión"),
        _ => tr("Desactivado"),
    };
    public string AgentDetail => Supervisor.Agent switch
    {
        AgentStatus.Ready => tr("Un agente pequeño en Program Files libera la RAM como administrador, sin volver a preguntar."),
        AgentStatus.Outdated => tr("Actualízalo para seguir liberando RAM (pide permiso de administrador)."),
        _ => tr("Liberar RAM necesita permisos de administrador: se piden una vez para instalar un agente pequeño."),
    };
    public Brush AgentBrush => B(Supervisor.Agent == AgentStatus.Ready ? T.On : Supervisor.Agent == AgentStatus.Outdated ? T.Warm : T.Line);
    public bool ShowAgentAction => Supervisor.Agent != AgentStatus.Ready && !Supervisor.AgentBusy;
    public string AgentActionLabel => Supervisor.Agent == AgentStatus.Outdated ? tr("Actualizar") : tr("Activar");
    public bool ShowRemoveAgent => Supervisor.Agent is AgentStatus.Ready or AgentStatus.Outdated && !Supervisor.AgentBusy;
    public string RemoveAgentLabel => tr("Quitar");
    public bool AgentBusy => Supervisor.AgentBusy;
    public string? AgentErrorText => Supervisor.AgentError;
    public bool HasAgentError => Supervisor.AgentError is not null;
    public string AreasTitle => tr("Zonas para limpieza manual");
    public List<SwitchRow> AreaRows { get; private set; } = [];
    public string ThresholdTitle => tr("Al pasar de este uso de RAM");
    public List<Segment> ThresholdSegments { get; private set; } = [];
    public string IntervalTitle => tr("Cada");
    public List<Segment> IntervalSegments { get; private set; } = [];
    public List<SwitchRow> CleanRuleRows { get; private set; } = [];
    public string ProtectionHint => tr("La limpieza automática es selectiva y solo actúa con presión física alta. Protege modelos, juego, aplicación en primer plano y sus descendientes. Las zonas avanzadas se aplican a la limpieza manual.");

    public bool ShowMemReduct => Supervisor.MemReductInstalled;
    public string MemReductDetail
    {
        get
        {
            if (Supervisor.MemReductReplaced) return tr("Reemplazado: ya no arranca con Windows y su limpieza automática está apagada.");
            var parts = new List<string>();
            if (Supervisor.MemReductConfig is { } c)
            {
                if (c.AutoEnabled) parts.Add(tr("limpia al %d %%", c.AutoPercent));
                if (c.IntervalEnabled) parts.Add(tr("cada %d min", c.IntervalMinutes));
            }
            parts.Add(Supervisor.MemReductRunning ? tr("en marcha") : tr("cerrado"));
            return string.Join(" · ", parts);
        }
    }
    public string ImportLabel => tr("Importar ajustes");
    public string ReplaceLabel => tr("Reemplazar");
    public string RestoreLabel => tr("Volver a Mem Reduct");
    public string ReplaceQuestion => tr("¿Importar sus ajustes, cerrarlo y quitarlo del inicio?");
    private bool confirmingReplace;
    public bool ShowReplaceButtons => !Supervisor.MemReductReplaced && !confirmingReplace;
    public bool ConfirmingReplace => confirmingReplace && !Supervisor.MemReductReplaced;
    public bool ShowRestore => Supervisor.MemReductReplaced;

    public string NotificationsTitle => tr("Notificaciones");
    public List<SwitchRow> NotificationRows { get; private set; } = [];

    public string IntegrationsTitle => tr("Integraciones");
    public string LinksHint => tr("Enlaces para Stream Deck, PowerToys, scripts o una nota: ábrelos con start o desde el navegador.");
    public List<LinkRow> LinkRows { get; } =
        Links.Examples.Select(e => new LinkRow(Links.Url(e), new Command(() => Copy(Links.Url(e))))).ToList();

    public string EnginesTitle => tr("Otros motores");
    public List<EngineSettingsRow> EngineSettingsRows { get; private set; } = [];
    public bool HasEngineSettings => EngineSettingsRows.Count > 0;
    public bool NoEngineSettings => !HasEngineSettings;
    public string EnginesEmpty => tr("Lanza llama-server (llama.cpp) o instala LM Studio y aparecerán aquí, con su propia siesta.");

    public string GeneralTitle => tr("General");
    public string HotKeyTitle => tr("Atajo de teclado");
    public string HotKeyDetail => tr("%@ enciende o apaga Ollama desde cualquier app", HotKey.Power.Display);
    public bool HotKeyOn => Prefs.HotKeyEnabled;
    public string SleepHotKeyTitle => tr("Atajo para dormir");
    public string SleepHotKeyDetail => tr("%@ duerme el modelo sin apagar Ollama", HotKey.Sleep.Display);
    public bool SleepHotKeyOn => Prefs.SleepHotKeyEnabled;
    public string LoginTitle => tr("Abrir al iniciar sesión");
    public string LoginDetail => tr("Siempre a mano en la bandeja del sistema");
    public bool LoginOn => Prefs.OpensAtLogin;
    public bool ShowTrayPin => TrayPin.Available;
    public string TrayPinTitle => tr("Mostrar siempre en la barra de tareas");
    public string TrayPinDetail => tr("El ícono fuera del menú ^ de íconos ocultos, junto al reloj.");
    public bool TrayPinOn => TrayPin.Pinned;
    public ICommand ToggleTrayPin { get; }
    public string ReducedMotionTitle => tr("Movimiento reducido");
    public string ReducedMotionDetail => tr("El ícono de la bandeja sin animaciones: punto ámbar fijo mientras Ollama cambia. Por defecto, como Windows");
    public bool ReducedMotionOn => Prefs.ReducedMotion;
    public ICommand ToggleReducedMotion { get; }
    public string TaskbarPetTitle => tr("Mascota junto a Inicio");
    public string TaskbarPetDetail => tr("Elige a Mira, una llama, un capibara o un gatito: duermen, comen, trabajan y barren con tus modelos.");
    public bool TaskbarPetOn => Prefs.Switch(PrefKeys.TaskbarPet, false);
    public string? TaskbarPetError => Prefs.TaskbarPetError;
    public bool HasTaskbarPetError => TaskbarPetOn && TaskbarPetError is not null;
    public ICommand ToggleTaskbarPet { get; }
    public string PetPlacementTitle => tr("Posición");
    public List<Segment> PetPlacementSegments { get; private set; } = [];
    public bool ShowPetSpecies => TaskbarPetOn && Pets.PetCatalog.All.Count > 1;
    public List<PetChoice> PetChoices { get; private set; } = [];
    public string PetInteractiveTitle => tr("Interactuar con la mascota");
    public string PetInteractiveDetail => tr("Clic: abre el panel. Clic derecho: su menú. Pasa el ratón de un lado a otro para acariciarla");
    public bool PetInteractiveOn => Prefs.Switch(PrefKeys.TaskbarPetInteractive);
    public ICommand TogglePetInteractive { get; }
    public bool ShowUpdateSetting => Supervisor.UpdatesAvailable;
    public string UpdateCheckTitle => tr("Buscar actualizaciones");
    public string UpdateCheckDetail => tr("Al arrancar y cada 24 horas en GitHub (%@). Tú decides cuándo instalar.", AppInfo.UpdateRepo);
    public bool UpdateCheckOn => Prefs.Switch(PrefKeys.UpdateCheck);
    public string LanguageTitle => tr("Idioma");
    public string LanguageHelp => tr("La app se reinicia al cambiarlo");
    public List<Segment> LanguageSegments { get; private set; } = [];
    public string LogsTitle => tr("Logs");
    public bool LogEnabled => Ollama.IsDemo || Ollama.Backend.LogPath is { } p && File.Exists(p);
    public Brush LogBrush => B(LogEnabled ? T.Accent : T.Muted);
    public bool ShowEngineLog => Supervisor.LlamaCpp.LogPath is not null;

    private static void Copy(string text)
    {
        try { Clipboard.SetText(text); } catch { }
    }

    private void PickGame()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = tr("Elige el .exe del juego"),
            Filter = tr("Programas") + " (*.exe)|*.exe",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() == true) Supervisor.AddGame(dialog.FileName);
    }

    // MARK: pie

    public string BrandName => AppInfo.Signature;
    /// Los atajos ya están en el menú y en Ajustes: el pie deja sitio a la firma.
    public string FooterText => tr("No afiliado a Ollama");

    // MARK: listas (solo se regeneran si cambian, para no perder el «hover»)

    private string segmentsSignature = "";

    private void Rebuild()
    {
        var busy = Ollama.BusyModel;
        var loaded = Ollama.Loaded.Select(m => m.Name).ToHashSet();
        if (confirmingDelete is not null && !Ollama.Installed.Any(m => m.Name == confirmingDelete)) confirmingDelete = null;
        var signature = string.Join("|", Ollama.Installed.Select(m => $"{m.Name}:{m.Bytes}:{loaded.Contains(m.Name)}"))
                        + $"#{busy}#{P}#{L10n.Effective}#{confirmingDelete}#{Ollama.Pulling?.Name}#{Prefs.MainModel}";
        if (signature != modelsSignature)
        {
            Models.Clear();
            int i = 0;
            foreach (var m in Ollama.Installed)
            {
                var name = m.Name;
                bool on = P == Power.On && busy is null;
                Models.Add(new ModelRow
                {
                    Name = name,
                    Meta = string.Join(" · ", new[] { m.Bytes.Gigabytes(), m.Quantization }.Where(s => s is not null)),
                    Vision = m.Vision,
                    Tools = m.Tools,
                    IsLoaded = loaded.Contains(name),
                    IsMain = Prefs.MainModel == name,
                    IsBusy = busy == name,
                    CanLoad = on && !loaded.Contains(name),
                    CanDelete = on && !Ollama.IsDemo,
                    IsConfirming = confirmingDelete == name,
                    ConfirmText = tr("¿Borrar %@?", m.Bytes.Gigabytes()),
                    ShowDivider = i++ > 0,
                    Load = new Command(() => Ollama.Load(name)),
                    ToggleMain = new Command(() => Prefs.SetMainModel(Prefs.MainModel == name ? null : name)),
                    AskDelete = new Command(() => { confirmingDelete = name; Notify(); }),
                    ConfirmDelete = new Command(() =>
                    {
                        confirmingDelete = null;
                        if (Prefs.MainModel == name) Prefs.SetMainModel(null);
                        Ollama.DeleteModel(name);
                    }),
                    CancelDelete = new Command(() => { confirmingDelete = null; Notify(); }),
                });
            }
            modelsSignature = signature;   // solo si se completó
        }

        var engines = Supervisor.Engines.Where(e => e.Installed).ToList();
        var engSignature = string.Join("|", engines.Select(e => $"{e.Id}:{e.Power}:{e.Model}:{e.IdleLine}:{e.IsBusy}:{e.Error}"));
        if (engSignature != enginesSignature)
        {
            EngineRows.Clear();
            foreach (var e in engines)
            {
                var engine = e;
                EngineRows.Add(new EngineRow
                {
                    Name = e.Name,
                    Experimental = e.Experimental,
                    State = e.Power switch
                    {
                        Power.On => e.Model ?? tr("Sin modelo"),
                        Power.Starting => tr("Arrancando…"),
                        Power.Stopping => tr("Apagando…"),
                        _ => tr("Dormido · puerto %d", e.Port),
                    },
                    Detail = e.IdleLine,
                    IsOn = e.Power is Power.On or Power.Starting,
                    IsBusy = e.IsBusy || e.Power.IsTransition(),
                    DotBrush = B(e.Power switch { Power.On => T.On, Power.Starting or Power.Stopping => T.Warm, _ => T.Line }),
                    Toggle = new Command(() => Supervisor.ToggleEngine(engine)),
                    CanSleep = e is LmStudioEngine && e.Power == Power.On && e.Model is not null,
                    Sleep = new Command(() => _ = engine.Sleep()),
                    Error = e.Error,
                });
            }
            enginesSignature = engSignature;
        }

        if (ShowingActivity) BuildActivity();

        var pref = (Ollama.Preferred ?? Ollama.Backend).Key;
        var games = Prefs.List(PrefKeys.CustomGames);
        var ignored = Prefs.List(PrefKeys.IgnoredGames);
        var notices = string.Join(",", Enum.GetValues<Notice>().Select(n => Prefs.Switch(n.Key(), n.DefaultOn())));
        var engineIdle = string.Join(",", engines.Select(e => $"{e.Id}{e.IdleMinutes}{e.Power}"));
        var cleanSwitches = string.Join(",", new[] { PrefKeys.CleanOnCritical, PrefKeys.CleanOnGame, PrefKeys.CleanHotKey }.Select(k => Prefs.Switch(k)))
                            + Prefs.Switch(PrefKeys.TrayPercent, false);
        var segSignature = $"{Ollama.IdleReleaseMinutes}#{pref}#{string.Join(",", Ollama.Mechanisms.Select(b => b.Key))}#{Prefs.Language}"
                           + $"#{string.Join(",", games)}#{string.Join(",", ignored)}#{notices}#{engineIdle}#{Supervisor.UpdatesAvailable}"
                           + $"#{Supervisor.Areas}#{Supervisor.CleanThreshold}#{Supervisor.CleanInterval}#{cleanSwitches}"
                           + $"#{Prefs.PetPlacement}#{Prefs.PetSpecies}";
        if (segSignature == segmentsSignature) return;
        segmentsSignature = segSignature;
        IdleSegments = Segments(Ollama.IdleReleaseMinutes, m => Ollama.IdleReleaseMinutes = m);
        MechanismSegments = Ollama.Mechanisms.Select(b => new Segment(
            Ollama.Mechanisms.Count(x => x.Kind == b.Kind) > 1 ? b.Summary : b.KindLabel,
            b.Key == pref,
            new Command(() => Ollama.Prefer(b)))).ToList();
        PetPlacementSegments = new[]
        {
            (PetPlacement.Above, tr("Sobre Inicio")), (PetPlacement.Left, tr("A su izquierda")), (PetPlacement.Walk, tr("De paseo")),
        }.Select(p => new Segment(p.Item2, Prefs.PetPlacement == p.Item1, new Command(() => Prefs.SetPetPlacement(p.Item1)))).ToList();
        var pet = Pets.PetCatalog.Find(Prefs.PetSpecies);
        // Conservar los controles (y el foco del teclado) al cambiar la selección.
        if (PetChoices.Count == 0)
            PetChoices = Pets.PetCatalog.All.Select(s => new PetChoice(s.Name, Pets.PetPreview.For(s), s == pet, new Command(() => Prefs.SetPetSpecies(s.Id)))).ToList();
        for (int i = 0; i < PetChoices.Count; i++) PetChoices[i].Select(Pets.PetCatalog.All[i] == pet);
        LanguageSegments = Enum.GetValues<Language>().Select(l => new Segment(
            l.Label(), l == Prefs.Language, new Command(() => Prefs.SetLanguage(l)))).ToList();

        GameRows = games.Select(g => (Path: g, Tag: (string?)null))
            .Concat(ignored.Select(g => (Path: g, Tag: (string?)tr("ignorado"))))
            .Select((g, i) => new GameRow(Path.GetFileNameWithoutExtension(g.Path), g.Path, g.Tag,
                new Command(() => Supervisor.RemoveGame(g.Path)), i > 0))
            .ToList();

        var kinds = Enum.GetValues<Notice>().Where(n => n != Notice.Update || Supervisor.UpdatesAvailable).ToList();
        NotificationRows = kinds.Select((n, i) => new SwitchRow(n.Title(), null, Prefs.Switch(n.Key(), n.DefaultOn()),
            new Command(() => Prefs.Toggle(n.Key(), n.DefaultOn())), i > 0)).ToList();

        var areas = Supervisor.Areas;
        (CleanAreas Area, string Title, string Hint)[] areaList =
        [
            (CleanAreas.WorkingSets, tr("Memoria de trabajo de las apps"), tr("Recomendado. Menos la de los modelos y el juego.")),
            (CleanAreas.SystemFileCache, tr("Caché de archivos del sistema"), tr("Recomendado.")),
            (CleanAreas.StandbyLowPriority, tr("Lista en espera de prioridad baja"), tr("Recomendado: lo que Windows menos echará de menos.")),
            (CleanAreas.Standby, tr("Lista en espera completa"), tr("Lento: Windows vuelve a leer de disco lo que necesite.")),
            (CleanAreas.ModifiedList, tr("Páginas modificadas"), tr("Lento: se escriben a disco.")),
            (CleanAreas.CombineLists, tr("Combinar páginas iguales"), tr("Recomendado.")),
            (CleanAreas.RegistryCache, tr("Caché del registro"), tr("Recomendado.")),
            (CleanAreas.ModifiedFileCache, tr("Caché de archivos modificados"), tr("Escribe a disco lo pendiente de cada volumen.")),
        ];
        AreaRows = areaList.Select((a, i) => new SwitchRow(a.Title,
            a.Area == CleanAreas.WorkingSets ? a.Hint : tr("Avanzado · %@", a.Hint), areas.HasFlag(a.Area),
            new Command(() => Supervisor.ToggleArea(a.Area)), i > 0)).ToList();
        ThresholdSegments = Choices([0, 50, 60, 70, 80, 90], Supervisor.CleanThreshold, m => $"{m} %", m => Supervisor.CleanThreshold = m);
        IntervalSegments = Choices([0, 5, 10, 15, 30, 60], Supervisor.CleanInterval, m => $"{m} min", m => Supervisor.CleanInterval = m);
        CleanRuleRows =
        [
            new(tr("Con presión de memoria crítica"), null, Prefs.Switch(PrefKeys.CleanOnCritical), new Command(() => Prefs.Toggle(PrefKeys.CleanOnCritical)), false),
            new(tr("Al empezar a jugar"), tr("Después de apagar Ollama por el modo juego."), Prefs.Switch(PrefKeys.CleanOnGame), new Command(() => Prefs.Toggle(PrefKeys.CleanOnGame)), true),
            new(tr("Atajo %@", HotKey.Clean.Display), tr("Libera la RAM desde cualquier app."), Prefs.Switch(PrefKeys.CleanHotKey),
                new Command(() => { Prefs.Toggle(PrefKeys.CleanHotKey); Prefs.OnHotKeyChange?.Invoke(Prefs.HotKeyEnabled); }), true),
            new(tr("Ícono con el % de RAM"), tr("El número en la bandeja con una barra: azul con Ollama encendido, ámbar o coral según la presión. También desde el clic derecho del ícono."),
                Prefs.Switch(PrefKeys.TrayPercent, false), new Command(() => Prefs.Toggle(PrefKeys.TrayPercent, false)), true),
        ];

        EngineSettingsRows = engines.Select((e, i) => new EngineSettingsRow
        {
            Name = e.Name,
            Experimental = e.Experimental,
            IdleSegments = Segments(e.IdleMinutes, m => e.IdleMinutes = m),
            Hint = e is LlamaCppEngine
                ? tr("Dormir es parar llama-server; al encenderlo se relanza con la misma línea de comandos.")
                : tr("Experimental: usa lms para arrancar y parar el servidor (puerto %d) y descargar modelos.", e.Port),
            CanForget = e is LlamaCppEngine && e.Power == Power.Off,
            Forget = new Command(() => Supervisor.LlamaCpp.Forget()),
            ShowDivider = i > 0,
        }).ToList();
    }

    /// Opciones fijas y, si el valor actual no está (p. ej. los 6 min importados de Mem Reduct), también ese.
    private static List<Segment> Choices(int[] choices, int current, Func<int, string> label, Action<int> pick) =>
        choices.Append(current).Distinct().Order().Select(m => new Segment(
            m == 0 ? tr("Nunca") : label(m), m == current, new Command(() => pick(m)))).ToList();

    private static List<Segment> Segments(int current, Action<int> pick) =>
        IdleChoices.Select(m => new Segment(
            m == 0 ? tr("Nunca") : m >= 120 ? $"{m / 60} h" : $"{m} min",
            m == current,
            new Command(() => pick(m)))).ToList();
}
