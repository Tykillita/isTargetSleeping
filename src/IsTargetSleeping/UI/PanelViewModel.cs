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

public enum PanelView { Main, Activity, Settings, Home, Widgets, Tray, Clipboard, Camera, Editor, Chat, Agents, Processes }

/// Una opción del selector segmentado.
public sealed record Segment(string Title, bool IsSelected, ICommand Pick);

/// Un icono del mini dock de la vista corta: la página a la que lleva.
public sealed record PagerIcon(string Glyph, string Title, bool IsSelected, ICommand Pick);

/// Una categoría de la barra lateral de Ajustes: icono blanco sobre un cuadrado de color, como en Nusa.
/// Un monitor en la página Pantalla: un rectángulo con su número, su resolución y si el notch vive en él.
public sealed record ScreenTile(string Number, string Resolution, string PrimaryLabel, string StatusLabel, string AccessibleName, bool IsOn, bool CanSelect, ICommand Toggle);

public sealed record SettingsNavItem(string Title, string Glyph, System.Windows.Media.Brush Tint, bool IsSelected, bool ShowLabel, ICommand Pick);

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

/// Un anillo de uso (memoria, GPU): título, «10,9 / 16 GB», porcentaje y fracción.
/// El estado de un motor para la píldora de su cabecera: el texto y el color del punto.
public sealed record EngineStateInfo(string Title, Brush Dot);

public sealed record EngineRingInfo(string Title, string Value, string Percent, double Fraction, bool Alert);

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
    /// El chip del widget: el cargado no se deshabilita (se ve entero), aunque no haya nada que cargar.
    public bool ChipEnabled => CanLoad || IsLoaded;
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
/// Una fila de «Memoria ahora»: qué es, cuánto y su barra (0 … 1).
public sealed record MemoryRow(string Label, string Value, double Fraction, bool ShowDivider);
public sealed record GpuRow(string Name, string Value, string Detail, double Fraction, double ModelFraction, bool ShowDivider)
{
    /// Lo que lee un lector de pantalla: la fila entera como una frase, no sus tres textos sueltos.
    public string Spoken => $"{Name}: {Value}. {Detail}";
}

/// Todo lo que muestra el panel, calculado a partir del estado: la interfaz
/// (ContentView.xaml) solo se enlaza a estas propiedades.
public sealed partial class PanelViewModel : INotifyPropertyChanged
{
    public Supervisor Supervisor { get; }
    public OllamaController Ollama { get; }
    public Prefs Prefs { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public PanelViewModel(Supervisor supervisor, PanelView view = PanelView.Main, Func<CancellationToken, Task<ProcessSample>>? captureProcesses = null,
        Func<ProcessActionRequest, Task<ProcessActionPreview>>? previewProcesses = null,
        Func<ProcessActionRequest, bool, Task<ProcessActionResult>>? executeProcesses = null)
    {
        Supervisor = supervisor;
        Ollama = supervisor.Ollama;
        Prefs = supervisor.Prefs;
        this.view = view;
        Ollama.Changed += Notify;
        Prefs.Changed += Notify;
        Supervisor.Changed += Notify;
        Theme.Changed += Notify;

        CornerCommand = new Command(() => { if (this.view == PanelView.Editor) CloseEditor(); else Show(ShowingBrand ? PanelView.Settings : BackView); });
        SideCommand = new Command(() => Show(this.view == PanelView.Activity ? PanelView.Settings : PanelView.Activity));
        OpenProcesses = new Command(() => { activityTab = ActivityTab.Processes; Show(PanelView.Activity); ProcessesRequested?.Invoke(); });
        Procs = new ProcessViewModel(captureProcesses, Supervisor.GetProcessProtectionPids, persistSettings: !Ollama.IsDemo,
            execute: executeProcesses ?? Supervisor.ExecuteProcessAction, preview: previewProcesses,
            installAgent: async () => { await Supervisor.InstallAgent(); return Supervisor.AgentError; },
            agentStatus: () => Supervisor.Agent);
        Procs.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ProcessViewModel.Summary)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProcessesSubtitle))); };
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
        ToggleUpdateCheck = new Command(() => { Prefs.Toggle(PrefKeys.UpdateCheck, false); _ = Supervisor.CheckUpdates(); });
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
            if (p is string name && name.Trim().Length > 0) { Ollama.Pull(name); addingModel = false; PullModelName = ""; Notify(); }
        });
        CancelPull = new Command(Ollama.CancelPull);
        ToggleTrayPin = new Command(() => { Prefs.SetTrayPinned(!TrayPin.Pinned); Notify(); });
        OpenTrayFallback = new Command(() => Paths.Open("ms-settings:taskbar"));
        ToggleReducedMotion = new Command(Prefs.ToggleReducedMotion);
        ToggleNotchMinimal = new Command(Prefs.ToggleNotchMinimal);
        ToggleHideFullscreen = new Command(Prefs.ToggleHideInFullscreen);
        ToggleLyricsSetting = new Command(() =>
        {
            Prefs.ToggleLyrics();
            Prefs.SetLyricsView(Prefs.Lyrics);
            Hub.Media.SetLyricsOpen(Prefs.Lyrics);   // activadas, la música se abre con la letra; apagadas, sin ella
            Hub.Media.RefreshLyrics(Prefs.Lyrics);
        });
        ToggleLyricsPillSetting = new Command(Prefs.ToggleLyricsPill);
        ToggleNotchMascot = new Command(Prefs.ToggleNotchMascot);
        SetGlassLevel = new ParamCommand(v => { if (v is double d) Prefs.SetGlassLevel(d); });
        ToggleNotchDock = new Command(() => Prefs.Toggle(PrefKeys.LegacyPanel, false));
        ToggleTaskbarPet = new Command(() => Prefs.Toggle(PrefKeys.TaskbarPet, false));
        TogglePetInteractive = new Command(() => Prefs.Toggle(PrefKeys.TaskbarPetInteractive));
        CleanNow = new Command(() =>
        {
            if (Supervisor.Agent == AgentStatus.Ready) _ = Supervisor.Clean(CleanReason.Manual, fromPanel: true);
            else
            {
                // Sin el agente (o desactualizado) no hay nada que limpiar: se enseña dónde activarlo. En el notch, que no
                // se vea nada al pulsar el botón de la versión corta o del dock sería como si no funcionara.
                settingsTab = SettingsTab.Memory;
                Show(PanelView.Settings);
                AttentionRequested?.Invoke();
            }
        });
        CleanAndSleep = new Command(() => _ = Supervisor.Clean(CleanReason.Manual, fromPanel: true, sleepModels: true));
        AgentAction = new Command(() => _ = Supervisor.InstallAgent());
        RemoveAgent = new Command(() => _ = Supervisor.UninstallAgent());
        PullSuggestions = new[] { "llama3.2", "qwen3", "gemma3", "phi4" }
            .Select(name => new Segment(name, false, new Command(() => { Ollama.Pull(name); addingModel = false; PullModelName = ""; Notify(); })))
            .ToList();
        OpenFullFromPeek = new Command(() => AttentionRequested?.Invoke());   // fija el notch abierto en la vista de siempre (ya no se abre con un clic en la vista corta)
        InitWidgets();
        InitTray();
        InitClipboard();
        InitCamera();
        InitPet();
        InitChat();
        InitEditor();
        Rebuild();
    }

    public void Notify()
    {
        Rebuild();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    /// Con el notch, Ajustes, Widgets y Procesos se abren en la ventana de la app en vez de dentro del panel.
    public bool WindowHost { get; set; }
    public event Action<PanelView>? WindowRequested;
    public bool OpensInWindow(PanelView v) => WindowHost && v is PanelView.Settings or PanelView.Widgets or PanelView.Processes;

    public void Show(PanelView v)
    {
        if (OpensInWindow(v)) { WindowRequested?.Invoke(v); return; }
        if (v == PanelView.Processes) activityTab = ActivityTab.Processes;
        if (view == PanelView.Camera && v != PanelView.Camera) Camera.StopPreview();
        view = v;
        confirmingDelete = null;
        UpdateProcessSampling();
        Notify();
    }

    /// Algo pide que se vea el panel (la pestaña Procesos de Actividad): quien lo aloja lo muestra o lo abre.
    public event Action? ProcessesRequested;
    /// Algo pide que se vea el panel entero (el notch se abre y se fija).
    public event Action? AttentionRequested;
    public ICommand OpenProcesses { get; }
    /// La lista se comparte entre la pestaña independiente y Actividad.
    public ProcessViewModel Procs { get; }
    private bool panelVisible;
    public bool ProcessSamplingActive => panelVisible && ShowingActivityProcesses && !Ollama.IsDemo;
    public string ProcessesTitle => tr("Reconocimiento de procesos");
    public string OpenProcessesLabel => tr("Ver todos los procesos");
    public string ProcessesIntro => tr("Identifica qué aplicaciones usan la RAM y finaliza las que quieras cerrar.");
    public string ProcessesSubtitle => Procs.Summary;

    public void SetPanelVisible(bool visible)
    {
        panelVisible = visible;
        UpdateProcessSampling();
    }

    private void UpdateProcessSampling()
    {
        Hub.SetVisible(panelVisible && (ShowingHome || previewWidget is not null), previewWidget is null ? WidgetIds : [.. WidgetIds, previewWidget]);
        Hub.SetPeekVisible(peekVisible, PeekIds);
        Procs.SetSamplingActive(ProcessSamplingActive);
    }

    // MARK: pestañas de Ajustes y de Actividad

    public enum SettingsTab { Ollama, Memory, Notch, Display, Alerts, Calendar, Tray, Pet, General }
    public enum ActivityTab { Memory, Processes, History }
    private SettingsTab settingsTab;
    private ActivityTab activityTab;
    public bool ShowingSettingsOllama => settingsTab == SettingsTab.Ollama;
    public bool ShowingSettingsMemory => settingsTab == SettingsTab.Memory;
    public bool ShowingSettingsNotch => settingsTab == SettingsTab.Notch;
    public bool ShowingSettingsDisplay => settingsTab == SettingsTab.Display;
    public bool ShowingSettingsAlerts => settingsTab == SettingsTab.Alerts;
    public bool ShowingSettingsCalendar => settingsTab == SettingsTab.Calendar;
    public bool ShowingSettingsTray => settingsTab == SettingsTab.Tray;
    public bool ShowingSettingsPet => settingsTab == SettingsTab.Pet;
    public bool ShowingSettingsGeneral => settingsTab == SettingsTab.General;
    public bool ShowingActivityMemory => activityTab == ActivityTab.Memory;
    public bool ShowingActivityProcesses => activityTab == ActivityTab.Processes && (ShowingActivity || ShowingProcesses);
    public bool ShowingActivityHistory => activityTab == ActivityTab.History;
    public string TabsLabel => tr("Secciones");
    public SettingsTab CurrentSettingsTab { get => settingsTab; set { settingsTab = value; Notify(); } }
    public ActivityTab CurrentActivityTab { get => activityTab; set { activityTab = value; UpdateProcessSampling(); Notify(); } }
    private static System.Windows.Media.Brush Tint(string hex)
    {
        var brush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
    private (SettingsTab Tab, string Title, string Glyph, string Color)[] SettingsPages =>
    [
        (SettingsTab.General, tr("General"), "", "#8E8E93"),
        (SettingsTab.Notch, tr("Notch"), "", "#0A84FF"),
        (SettingsTab.Display, tr("Pantalla"), "", "#5E5CE6"),
        (SettingsTab.Alerts, tr("Avisos"), "", "#FF453A"),
        (SettingsTab.Calendar, tr("Calendario y tiempo"), "", "#FF9F0A"),
        (SettingsTab.Tray, tr("Bandeja y portapapeles"), "", "#30B0C7"),
        (SettingsTab.Ollama, "Ollama", "", "#5E5CE6"),
        (SettingsTab.Memory, tr("Memoria"), PillCatalog.Find("ram")!.Glyph, "#32D74B"),
        (SettingsTab.Pet, tr("Mascota"), "", "#FF6482"),
    ];
    /// La barra lateral de Ajustes; con poco ancho (la bandeja) solo los iconos, con el nombre en el tooltip.
    public List<SettingsNavItem> SettingsNav => SettingsPages.Select(t => new SettingsNavItem(t.Title, t.Glyph, Tint(t.Color),
        settingsTab == t.Tab, WideLayout, new Command(() => CurrentSettingsTab = t.Tab))).ToList();
    public string SettingsPageTitle => SettingsPages.First(t => t.Tab == settingsTab).Title;
    public List<Segment> ActivityTabs => new[]
    {
        (ActivityTab.Memory, tr("Memoria")), (ActivityTab.Processes, tr("Procesos")), (ActivityTab.History, tr("Historial")),
    }.Select(t => new Segment(t.Item2, activityTab == t.Item1, new Command(() => CurrentActivityTab = t.Item1))).ToList();
    public string NotchSectionTitle => tr("Notch y bandeja");
    public string PetSectionTitle => tr("Mascota");

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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SettingsBodyMax)));
        }
    }
    /// El alto de lo que se desplaza en Ajustes (la columna de la derecha): el del cuerpo menos el margen y, en el notch, el pie; la barra de pestañas de Ajustes no entra y no se desplaza.
    public double SettingsBodyMax => double.IsInfinity(bodyMaxHeight) ? double.PositiveInfinity : Math.Max(120, bodyMaxHeight - (wideLayout ? 70 : 28));

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

    // MARK: cabecera

    private PanelView view;
    public bool ShowingMain => view == PanelView.Main;
    public bool ShowingActivity => view == PanelView.Activity;
    public bool ShowingProcesses => view == PanelView.Processes;
    public bool ShowingActivityPanel => ShowingActivity || ShowingProcesses;
    public bool ShowingSettings => view == PanelView.Settings;
    public bool ShowingSecondary => !ShowingBrand;

    public string HeaderTitle => view switch
    {
        PanelView.Settings => tr("Ajustes"),
        PanelView.Activity => tr("Actividad"),
        PanelView.Processes => tr("Procesos"),
        PanelView.Widgets => tr("Widgets"),
        PanelView.Tray => tr("Bandeja"),
        PanelView.Clipboard => tr("Portapapeles"),
        PanelView.Camera => tr("Cámara"),
        PanelView.Chat => tr("Chat"),
        PanelView.Agents => tr("Agentes"),
        PanelView.Editor => EditTitle,
        _ => AppInfo.Name,
    };

    public string HeaderGlyph => view switch
    {
        PanelView.Settings => "",   // Settings
        PanelView.Processes => ChartGlyph,
        PanelView.Widgets => "",
        PanelView.Tray => "",
        PanelView.Clipboard => "",
        PanelView.Camera => "\uE722",
        PanelView.Chat => "\uE8BD",
        PanelView.Agents => "\uE716",
        PanelView.Editor => "",
        _ => "",   // AreaChart
    };
    public Brush LogoBrush => B(P == Power.On ? T.On : T.Muted);

    public string Subtitle
    {
        get
        {
            if (view == PanelView.Settings) return $"v{AppInfo.Version}";
            if (view == PanelView.Activity) return tr("Historial de %d días, en este PC", StatsStore.KeepDays);
            if (ShowingProcesses) return ProcessesIntro;
            if (view == PanelView.Widgets) return tr("Elige y ordena lo que ve el notch");
            if (view == PanelView.Editor) return EditSubtitle;
            if (view == PanelView.Clipboard) return tr("Lo último que copiaste, solo en esta sesión");
            if (view == PanelView.Camera) return tr("Vista previa privada y local");
            if (view == PanelView.Chat) return tr("Modelo local, solo en este PC");
            if (view == PanelView.Agents) return tr("Permisos, decisiones y reglas de tus agentes");
            if (view == PanelView.Tray) return Tray.Count == 0 ? tr("Archivos de paso, en este PC") : TraySummary;
            return $"v{AppInfo.Version}";   // la versión de esta app; nada de Ollama bajo el nombre
        }
    }

    // Los dos botones de la cabecera. La esquina es siempre la misma: el engranaje en
    // la vista principal y la X (volver) en Actividad y en Ajustes. El de al lado lleva
    // a la otra vista: Actividad desde la principal y desde Ajustes, Ajustes desde Actividad.
    private const string SettingsGlyph = "", ChartGlyph = "", CloseGlyph = "";
    public string CornerGlyph => ShowingBrand ? SettingsGlyph : CloseGlyph;
    public Brush CornerBackground => ShowingBrand ? Brushes.Transparent : B(T.Track);
    public string CornerHelp => view switch
    {
        PanelView.Settings => tr("Cerrar ajustes"),
        PanelView.Activity => tr("Cerrar actividad"),
        PanelView.Processes => tr("Cerrar procesos"),
        PanelView.Widgets => tr("Cerrar widgets"),
        PanelView.Tray => tr("Cerrar bandeja"),
        PanelView.Clipboard => tr("Cerrar portapapeles"),
        PanelView.Camera => tr("Cerrar cámara"),
        PanelView.Chat => tr("Cerrar el chat"),
        PanelView.Agents => tr("Cerrar agentes"),
        PanelView.Editor => tr("Cerrar el editor"),
        _ => tr("Ajustes"),
    };
    public string SideGlyph => ShowingActivity || ShowingProcesses ? SettingsGlyph : ChartGlyph;
    public string SideHelp => ShowingActivity || ShowingProcesses ? tr("Ajustes") : tr("Actividad");

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

    public string DismissLabel => tr("Cerrar aviso");
    public string OpenLogLabel => tr("Abrir el log de Ollama");
    public string OpenEngineLogLabel => tr("Abrir el log de llama.cpp");
    public string HeroHelp =>P == Power.On ? tr("Apagar Ollama") : P == Power.Missing ? tr("Descargar Ollama") : tr("Encender Ollama");

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
    // Los anillos del widget de Ollama: memoria del PC y de la GPU con su porcentaje.
    public EngineRingInfo RamRing => new(tr("Memoria"), UsedLabel, $"{UsedFraction * 100:0}%", UsedFraction, UsedFraction >= 0.9);
    public EngineRingInfo GpuRing => new(tr("GPU"), GpuUsedLabel, $"{GpuUsedFraction * 100:0}%", GpuUsedFraction, GpuUsedFraction >= 0.9);
    public EngineStateInfo StatePill => new(HeroTitle, B(HeroColor));
    public double IdleFraction => Ollama.IdleLimit is { } limit && Ollama.IdleSeconds is { } seconds && limit > 0 ? Math.Clamp(seconds / limit, 0, 1) : 0;
    public bool HasIdleBar => Ollama.Loaded.Count > 0 && Ollama.IdleLimit is not null && Ollama.IdleSeconds is not null;
    public string AutoFreeTitle => tr("Se libera solo");
    public string InstalledTitle => tr("Modelos instalados");
    public string InstalledEmpty => P == Power.On ? tr("No hay modelos instalados.") : tr("Enciende Ollama para ver tus modelos.");
    public IReadOnlyList<ModelRow> ChipModels => Models.Take(8).ToList();
    public bool HasChipModels => Models.Count > 0;
    public bool NoChipModels => !HasChipModels;

    public bool HasLoaded => Ollama.Loaded.Count > 0;
    public bool NoLoaded => !HasLoaded;
    public string LoadedName => Ollama.Loaded.FirstOrDefault()?.Name ?? "";
    public bool FreeBusy => Ollama.Loaded.FirstOrDefault() is { } m && Ollama.BusyModel == m.Name;
    public bool FreeIdle => !FreeBusy;
    public bool FreeEnabled => Ollama.BusyModel is null;
    /// Los botones de la versión corta y de los widgets no desaparecen mientras trabajan (los de al lado saltarían): se apagan y cambian de texto.
    public bool FreeCanClick => FreeEnabled && FreeIdle;
    public string FreeModelLabel => tr("Liberar modelo");   // sin el nombre del modelo al lado, «Liberar» a secas no se entiende
    public string FreeButtonText => FreeBusy ? tr("Liberando…") : FreeModelLabel;
    public string CleanButtonText => IsCleaning ? tr("Liberando…") : CleanLabel;
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
    public double ModelsMaxHeight => Ollama.Installed.Count > (WideLayout ? 4 : 6) ? (WideLayout ? 250 : 300) : double.PositiveInfinity;

    /// El dock tipo notch lo pide: dos columnas, más ancho y menos largo.
    private bool wideLayout;
    public bool WideLayout
    {
        get => wideLayout;
        set
        {
            if (wideLayout == value) return;
            wideLayout = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }
    /// El panel de la bandeja lleva una sombra negra abajo y una línea sobre el pie; el notch de cristal no (se funde).
    public bool FlatFooter => !wideLayout;
    /// En el notch el pie va al final de cada página (dentro del desplazamiento); en el panel de la bandeja, fijo abajo.
    public bool NotchFooter => wideLayout && view is PanelView.Widgets or PanelView.Settings or PanelView.Processes;
    /// En el notch el pie queda dentro de la esquina redondeada (26 px de radio): la misma distancia al lado y abajo, para que sea simétrico con la curva.
    public Thickness FooterMargin => wideLayout ? new Thickness(22, 2, 22, 19) : new Thickness(14, 9, 14, 9);
    public ObservableCollection<ModelRow> Models { get; } = [];
    private string modelsSignature = "";
    private string? confirmingDelete;

    public bool CanAddModel => P == Power.On && Ollama.Pulling is null;
    public string AddModelHelp => tr("Descargar un modelo");
    public string AddModelGlyph => addingModel ? "" : "";   // Cancel / Add
    private bool addingModel;
    public bool AddingModel => addingModel && CanAddModel;
    private string pullModelName = "";
    public string PullModelName
    {
        get => pullModelName;
        set
        {
            if (pullModelName == value) return;
            pullModelName = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PullModelName)));
        }
    }
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
    public string MemoryNowTitle => tr("Memoria ahora");
    public List<MemoryRow> MemoryRows { get; private set; } = [];
    public string GpuAdaptersTitle => tr("GPU por adaptador");
    public List<GpuRow> GpuRows { get; private set; } = [];
    public bool HasGpuRows => GpuRows.Count > 0;
    public string GpuAdaptersHint => tr("VRAM dedicada de cada GPU y la parte de los modelos. «No disponible»: Windows o el controlador no entregan el dato. Solo informa; no activa ningún apagado.");
    public string MemoryNowHint => tr("Comprometida: lo que los programas tienen reservado entre la RAM y los archivos de paginación. Si llega al límite, Windows no puede dar más memoria aunque quede RAM libre.");
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
        MemoryRows = MemoryNow(MemoryBreakdown.Current());
        GpuRows = GpuAdapterRows(Ollama.GpuAdapters);
    }

    /// Una fila por GPU dedicada: lo que no se pudo medir se dice, no se estima.
    internal static List<GpuRow> GpuAdapterRows(IReadOnlyList<AdapterUsage> adapters) => adapters.Select((a, i) =>
    {
        long total = a.Adapter.Total;
        string totalText = $"{Math.Round(total / 1_073_741_824.0)} GB";
        string value = a.Used is { } used ? $"{used.MemoryGB()} / {totalText}" : tr("No disponible · %@ en total", totalText);
        string detail = a.Used is { } u && a.Model is { } m
            ? $"{tr("Modelo")} {Num(m)} · {tr("Resto")} {Num(Math.Max(0, u - m))}"
            : tr("Modelo: no disponible");
        return new GpuRow(a.Adapter.ShortName, value, detail, a.Used is { } f ? (double)f / total : 0,
            a.Used is not null && a.Model is { } mf ? (double)mf / total : 0, i > 0);
    }).ToList();

    /// Lo que también enseña Mem Reduct (física, paginación y caché del sistema) más la comprometida.
    internal static List<MemoryRow> MemoryNow(MemoryBreakdown m)
    {
        static string Percent(double fraction) => $"{Math.Round(fraction * 100):0} %";
        return
        [
            new(tr("RAM física"), m.PhysicalTotal > 0
                ? tr("%@ · %@ libres de %@", Percent(m.PhysicalFraction), m.PhysicalAvailable.MemoryGB(), m.PhysicalTotal.MemoryGB())
                : tr("No disponible"), m.PhysicalFraction, false),
            new(tr("Memoria comprometida"), m.CommitLimit > 0
                ? tr("%@ · %@ de %@", Percent(m.CommitFraction), Num(m.Committed), m.CommitLimit.MemoryGB())
                : tr("No disponible"), m.CommitFraction, true),
            new(tr("Archivos de paginación"), m.PageFileTotal > 0
                ? tr("%@ · %@ de %@", Percent(m.PageFileFraction), Num(m.PageFileUsed), m.PageFileTotal.MemoryGB())
                : tr("Sin archivo de paginación"), m.PageFileFraction, true),
            new(tr("Caché del sistema"), m.CachePeak > 0
                ? tr("%@ · pico %@", m.CacheCurrent.MemoryGB(), m.CachePeak.MemoryGB())
                : tr("No disponible"), m.CacheFraction, true),
        ];
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
    public string AreasTitle => tr("Zonas que se limpian");
    public List<SwitchRow> AreaRows { get; private set; } = [];
    public string ThresholdTitle => tr("Al pasar de este uso de RAM");
    public List<Segment> ThresholdSegments { get; private set; } = [];
    public string CooldownTitle => tr("Si sigue por encima, repetir como mucho cada");
    public List<Segment> CooldownSegments { get; private set; } = [];
    public List<Segment> ThemeSegments { get; private set; } = [];
    public string ThemeTitle => tr("Aspecto del notch");
    public string ThemeDetail => tr("Cristal negro (el de siempre) o claro con contornos: fondo blanco, texto negro y cada pieza con su línea fina.");
    private void SetThemeStyle(bool outline)
    {
        if (Prefs.OutlineTheme == outline) return;
        Prefs.SetOutlineTheme(outline);
        Theme.OutlineStyle = outline;
        Theme.Apply();
    }
    public List<Segment> TeamViewSegments { get; private set; } = [];
    public string TeamViewTitle => tr("Estilo del widget Equipo");
    public string TeamViewDetail => tr("Cómo se dibuja el avance de Claude y Codex: tubería de nodos, línea de tiempo con barras o la mesa del equipo con retratos.");
    public bool ShowCooldown => Supervisor.CleanThreshold > 0;
    public string IntervalTitle => tr("Además, limpiar cada");
    public List<Segment> IntervalSegments { get; private set; } = [];
    public List<SwitchRow> CleanRuleRows { get; private set; } = [];
    public string ProtectionHint => tr("Las automáticas limpian las mismas zonas que el botón. El porcentaje actúa aunque no toque el intervalo, y si la RAM sigue por encima repite tras la pausa mínima. Se protegen los modelos, el juego y la app que estás usando con sus procesos.");


    public string NotificationsTitle => tr("Notificaciones");
    public List<SwitchRow> NotificationRows { get; private set; } = [];
    public List<SwitchRow> LocalFeatureRows { get; private set; } = [];
    public double GlassLevel => Prefs.GlassLevel;
    public string GlassLevelText => tr("Nivel de cristal: %@", $"{Math.Round(GlassLevel * 100):0}%");
    public ICommand SetGlassLevel { get; }
    public string HiddenAppsTitle => tr("Ocultar en apps");
    public string HiddenAppsDetail => tr("Una app por línea (por ejemplo «chrome» o «obs64.exe»). El notch no se muestra mientras esas apps tengan el foco; fijado con un clic, sí.");
    public string HiddenAppsText
    {
        get => string.Join(Environment.NewLine, Prefs.List(PrefKeys.HiddenApps));
        set => Prefs.SetList(PrefKeys.HiddenApps, (value ?? "").Split(["\r\n", "\n", ";"], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => Path.GetFileNameWithoutExtension(x.Trim())).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
    }
    public string WeatherCityTitle => tr("Ciudad para el clima");
    public string WeatherCountryTitle => tr("País para el clima");
    // Se elige de dos listas (país con su bandera y ciudad), no se escribe: el notch no recibe bien el teclado, y «Ciudad, País» tampoco lo entendía el buscador.
    public IReadOnlyList<WeatherCountryItem> WeatherCountries => WeatherPlaces.Countries.Select(c => new WeatherCountryItem(c.Code, c.Name(Effective))).ToList();
    public IReadOnlyList<WeatherCityItem> WeatherCities => WeatherPlaces.InCountry(WeatherCountryCode).Select(p => new WeatherCityItem(p.Id, p.City(Effective))).ToList();
    public string? WeatherCountryCode
    {
        get => WeatherPlaces.Resolve(Prefs.WeatherCity)?.Country;
        set
        {
            if (value is null || value == WeatherCountryCode || WeatherPlaces.InCountry(value).FirstOrDefault() is not { } first) return;
            Prefs.SetWeatherCity(first.Id);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WeatherCities)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WeatherCityId)));
        }
    }
    public string? WeatherCityId
    {
        get => WeatherPlaces.Resolve(Prefs.WeatherCity)?.Id;
        set { if (value is not null && WeatherPlaces.Find(value) is not null && value != WeatherCityId) Prefs.SetWeatherCity(value); }
    }

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
    public bool ShowTrayFallback => !TrayPin.Available;
    public string TrayFallbackTitle => tr("Fijar el ícono desde Windows");
    public string TrayFallbackDetail => tr("Si queda tras la ^, actívalo en los ajustes de Windows: Personalización › Barra de tareas › Otros iconos de la bandeja del sistema (Windows 11) o Elige los iconos que aparecerán en la barra de tareas (Windows 10).");
    public string TrayFallbackOpen => tr("Abrir ajustes de la barra de tareas");
    public ICommand OpenTrayFallback { get; }
    public string NotchDockTitle => tr("Versión legacy (panel de la bandeja)");
    public string NotchDockDetail => tr("Usa el panel que sale de la bandeja en lugar del notch. El notch es la versión principal; esto se apaga desde aquí mismo.");
    public bool NotchDockOn => Prefs.Switch(PrefKeys.LegacyPanel, false);
    public ICommand ToggleNotchDock { get; }
    /// Lo que enseña la pastilla colapsada: el modelo cargado (o que no hay) y la RAM en uso.
    public string NotchModel => Ollama.Loaded.Count switch
    {
        0 => tr("Sin modelo"),
        1 => Ollama.Loaded[0].Name,
        var n => $"{Ollama.Loaded[0].Name} +{n - 1}",
    };
    public string NotchRam => Mem.Used.MemoryGB();
    public string NotchTabHome => tr("Inicio");
    public string NotchTabActivity => tr("Actividad");
    public string NotchTabSettings => tr("Ajustes");
    public string NotchTabProcesses => tr("Procesos");
    public PanelView CurrentView => view;
    public string NotchHelp => tr("%@: %@, %@. Abrir el panel", AppInfo.Name, PillText, NotchModel);
    /// Alto máximo del cuerpo (que se desplaza) según el alto del área de trabajo, en DIP.
    public static double BodyMaxFor(double workHeightDip, bool wide = false) =>
        Math.Max(240, Math.Min(wide ? Math.Min(workHeightDip * 0.6, 440) : workHeightDip * 0.75, workHeightDip - 150));
    public string ReducedMotionTitle => tr("Movimiento reducido");
    public string ReducedMotionDetail => tr("El ícono de la bandeja sin animaciones: punto ámbar fijo mientras Ollama cambia. Por defecto, como Windows");
    public bool ReducedMotionOn => Prefs.ReducedMotion;
    public ICommand ToggleReducedMotion { get; }
    public string NotchMinimalTitle => tr("Notch mínimo");
    public string NotchMinimalDetail => tr("Una pastilla más pequeña cuando no pasa nada: una franja negra fina. La música, los avisos y el ratón la despiertan.");
    public bool NotchMinimalOn => Prefs.NotchMinimal;
    public string ScreensIntro => tr("En qué pantalla vive el notch. Con «Seguir al cursor» hay uno solo que pasa a la pantalla donde está el ratón; con «Pantallas elegidas» hay uno en cada pantalla marcada.");
    public string ScreensClickHint => ScreensPickable
        ? tr("Pulsa una tarjeta para activar o quitar el notch. Debe quedar al menos un monitor activo.")
        : tr("El notch sigue el cursor. Elige «Pantallas elegidas» para marcar monitores.");
    public string ScreenSelectionSummary { get; private set; } = "";
    public string AllScreensLabel => tr("Todos los monitores");
    public string PrimaryScreenLabel => tr("Solo el principal");
    public List<Segment> ScreenModeSegments { get; private set; } = [];
    public List<ScreenTile> ScreenTiles { get; private set; } = [];
    public bool ScreensPickable => (Prefs.StoredList(PrefKeys.NotchScreenMode)?.FirstOrDefault() ?? NotchScreens.Selected) == NotchScreens.Selected;
    public ICommand SelectAllScreens { get; private set; } = null!;
    public ICommand SelectPrimaryScreen { get; private set; } = null!;
    public string VisibilitySectionTitle => tr("Visibilidad y animación");
    public string LyricsTitle => tr("Letras sincronizadas");
    public string LyricsDetail => tr("Busca la letra de lo que suena en lrclib.net: manda el título y el artista de la canción. Se activan al pulsar el botón de letras de la página de música.");
    public bool LyricsOn => Prefs.Lyrics;
    public ICommand ToggleLyricsSetting { get; private set; } = null!;
    public string LyricsPillTitle => tr("Letra en el notch cerrado");
    public string LyricsPillDetail => tr("Con letras, el notch cerrado enseña la línea que se canta en vez de la hora.");
    public bool LyricsPillOn => Prefs.LyricsPill;
    public ICommand ToggleLyricsPillSetting { get; private set; } = null!;
    public string HideFullscreenTitle => tr("Ocultar a pantalla completa");
    public string HideFullscreenDetail => tr("Juegos, vídeos y presentaciones que llenan la pantalla.");
    public bool HideFullscreenOn => Prefs.HideInFullscreen;
    public ICommand ToggleHideFullscreen { get; }
    public string OpenModeTitle => tr("Abrir el notch");
    public string OpenModeDetail => tr("Con un clic, la vista corta se abre al pulsar la pastilla; con el ratón, al pasar por encima.");
    public List<Segment> OpenModeSegments { get; private set; } = [];
    public string AnimSpeedTitle => tr("Velocidad de animación");
    public List<Segment> AnimSpeedSegments { get; private set; } = [];
    public ICommand ToggleNotchMinimal { get; }
    public string NotchMascotTitle => tr("Mascota en el notch");
    public string NotchMascotDetail => tr("Si dejas de usar el equipo un rato, aparece dormida en el notch cerrado; al volver te saluda y deja paso al notch normal. Los avisos salen a su lado; la música y abrir el notch la apartan.");
    public bool NotchMascotOn => Prefs.NotchMascot;
    public ICommand ToggleNotchMascot { get; }
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
    public string UpdateCheckDetail => tr("Si lo activas, busca al arrancar y cada 24 horas en GitHub (%@). Tú decides cuándo instalar.", AppInfo.UpdateRepo);
    public bool UpdateCheckOn => Prefs.Switch(PrefKeys.UpdateCheck, false);
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

    // MARK: listas (solo se regeneran si cambian, para no perder el «hover»)

    private string segmentsSignature = "";

    private void Rebuild()
    {
        var busy = Ollama.BusyModel;
        var loaded = Ollama.Loaded.Select(m => m.Name).ToHashSet();
        if (confirmingDelete is not null && !Ollama.Installed.Any(m => m.Name == confirmingDelete)) confirmingDelete = null;
        var signature = string.Join("|", Ollama.Installed.Select(m => $"{m.Name}:{m.Bytes}:{loaded.Contains(m.Name)}"))
                        + $"#{busy}#{P}#{L10n.Effective}#{confirmingDelete}#{Ollama.Pulling?.Name}#{Prefs.MainModel}";
        RebuildWidgets();
        RebuildTray();
        RebuildClipboard();
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
        var screenMode = Prefs.StoredList(PrefKeys.NotchScreenMode)?.FirstOrDefault() ?? NotchScreens.Selected;
        var chosenScreens = Prefs.StoredList(PrefKeys.NotchScreens);
        var screenSignature = NotchScreens.PreferenceSignature(screenMode, chosenScreens);
        var engineIdle = string.Join(",", engines.Select(e => $"{e.Id}{e.IdleMinutes}{e.Power}"));
        var cleanSwitches = string.Join(",", new[] { PrefKeys.CleanOnCritical, PrefKeys.CleanOnGame, PrefKeys.CleanHotKey }.Select(k => Prefs.Switch(k)))
                            + Prefs.Switch(PrefKeys.TrayPercent, false);
        var segSignature = $"{Ollama.IdleReleaseMinutes}#{pref}#{string.Join(",", Ollama.Mechanisms.Select(b => b.Key))}#{Prefs.Language}#{Prefs.OutlineTheme}"
                           + $"#{string.Join(",", games)}#{string.Join(",", ignored)}#{notices}#{engineIdle}#{Supervisor.UpdatesAvailable}"
                           + $"#{Supervisor.Areas}#{Supervisor.CleanThreshold}#{Supervisor.CleanCooldown}#{Supervisor.CleanInterval}#{cleanSwitches}"
                           + $"#{Prefs.PetPlacement}#{Prefs.PetSpecies}#{string.Join(",", ActivityIds)}"
                           + $"#{Prefs.Switch(PrefKeys.AgentsEnabled, false)}#{Prefs.Switch(PrefKeys.WeatherEnabled, false)}#{Prefs.Switch(PrefKeys.UsageEnabled, false)}#{Prefs.Switch(PrefKeys.OcrEnabled, false)}"
                           + $"#{Prefs.StoredList(PrefKeys.TeamView)?.FirstOrDefault()}#{screenSignature}";
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
        var screens = Screens.List();
        bool picking = screenMode == NotchScreens.Selected;
        var chosen = chosenScreens ?? [];
        var selectedScreens = NotchScreens.Select(screens, screenMode, chosen);
        ScreenSelectionSummary = selectedScreens is null
            ? tr("El notch sigue al cursor y aparece en la pantalla donde está el ratón.")
            : tr("Notch activo en %@ de %@ monitores.", selectedScreens.Count, screens.Count);
        ScreenModeSegments = [new Segment(tr("Seguir al cursor"), !picking, new Command(() => Prefs.SetList(PrefKeys.NotchScreenMode, [NotchScreens.FollowCursor]))),
            new Segment(tr("Pantallas elegidas"), picking, new Command(() => Prefs.SetList(PrefKeys.NotchScreenMode, [NotchScreens.Selected])))];
        ScreenTiles = screens.Select(sc =>
        {
            var number = Screens.Number(sc, screens);
            var role = sc.Primary ? tr("Principal") : tr("Secundaria");
            bool isOn = selectedScreens?.Any(selected => selected.Id == sc.Id) == true;
            var status = picking ? (isOn ? tr("Notch activo") : tr("Sin notch")) : tr("Sigue al cursor");
            return new ScreenTile(number, $"{sc.Bounds.Width}×{sc.Bounds.Height}", role, status,
            tr("Pantalla %@, %@, %@, %@", number, role, $"{sc.Bounds.Width}×{sc.Bounds.Height}", status), isOn, picking,
            new Command(() =>
            {
                if (!picking) return;
                var stored = Prefs.StoredList(PrefKeys.NotchScreens);
                var current = stored is { Count: > 0 } list ? list : screens.Where(x => x.Primary).Select(x => x.Id).ToList();
                var next = current.Contains(sc.Id) ? current.Where(id => id != sc.Id).ToList()
                    : stored is { Count: > 0 } ? [.. current, sc.Id] : [sc.Id];
                if (next.Count == 0) return;   // siempre queda al menos una pantalla
                Prefs.SetList(PrefKeys.NotchScreens, next);
            }));
        }).ToList();
        SelectAllScreens = new Command(() => { Prefs.SetList(PrefKeys.NotchScreens, screens.Select(x => x.Id)); Prefs.SetList(PrefKeys.NotchScreenMode, [NotchScreens.Selected]); });
        SelectPrimaryScreen = new Command(() =>
        {
            if (screens.FirstOrDefault(x => x.Primary) is not { } primary) return;
            Prefs.SetList(PrefKeys.NotchScreens, [primary.Id]);
            Prefs.SetList(PrefKeys.NotchScreenMode, [NotchScreens.Selected]);
        });
        bool clickMode = Prefs.OpenOnClick, fastMode = Prefs.AnimSpeed < 1;
        OpenModeSegments = [new Segment(tr("Al pasar el ratón"), !clickMode, new Command(() => Prefs.SetList(PrefKeys.NotchOpenMode, ["hover"]))),
            new Segment(tr("Con un clic"), clickMode, new Command(() => Prefs.SetList(PrefKeys.NotchOpenMode, ["click"])))];
        AnimSpeedSegments = [new Segment(tr("Normal"), !fastMode, new Command(() => Prefs.SetList(PrefKeys.NotchAnimSpeed, ["normal"]))),
            new Segment(tr("Rápida"), fastMode, new Command(() => Prefs.SetList(PrefKeys.NotchAnimSpeed, ["fast"])))];
        ThemeSegments = [new Segment(tr("Cristal negro"), !Prefs.OutlineTheme, new Command(() => SetThemeStyle(false))), new Segment(tr("Claro con contornos"), Prefs.OutlineTheme, new Command(() => SetThemeStyle(true)))];
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
        LocalFeatureRows =
        [
            new(tr("Agentes de IA"), tr("Lee metadatos de sesiones locales de Claude Code y Codex; apagado por defecto."),
                Prefs.Switch(PrefKeys.AgentsEnabled, false), new Command(() => Prefs.Toggle(PrefKeys.AgentsEnabled, false)), false),
            new(tr("Clima con Open-Meteo"), tr("Envía solo la ciudad elegida; apagado por defecto."),
                Prefs.Switch(PrefKeys.WeatherEnabled, false), new Command(() => Prefs.Toggle(PrefKeys.WeatherEnabled, false)), true),
            new(tr("Uso de IA"), tr("Lee sesiones locales de Claude Code, Codex y Gemini; Muse y Ollama local si están disponibles, y Grok API si está configurado. Apagado por defecto."),
                Prefs.Switch(PrefKeys.UsageEnabled, false), new Command(() => Prefs.Toggle(PrefKeys.UsageEnabled, false)), true),
            new(tr("OCR de pantalla"), tr("Ctrl+Alt+I selecciona una región y copia el texto reconocido; el procesamiento es local."),
                Prefs.Switch(PrefKeys.OcrEnabled, false), new Command(() => Prefs.Toggle(PrefKeys.OcrEnabled, false)), true),
        ];

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
        var teamView = Enum.TryParse<TeamViewStyle>(Prefs.StoredList(PrefKeys.TeamView)?.FirstOrDefault(), out var chosenView) ? chosenView : TeamViewStyle.Pipeline;
        TeamViewSegments = Enum.GetValues<TeamViewStyle>().Select(v => new Segment(
            v switch { TeamViewStyle.Pipeline => tr("Tubería"), TeamViewStyle.Timeline => tr("Línea de tiempo"), _ => tr("Mesa de equipo") },
            v == teamView, new Command(() => Prefs.SetList(PrefKeys.TeamView, [v.ToString()])))).ToList();
        CooldownSegments = Choices([5, 10, 15, 30, 60], Supervisor.CleanCooldown, m => $"{m} min", m => Supervisor.CleanCooldown = m);
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

    /// Opciones fijas y, si el valor actual no está (p. ej. uno guardado por una versión anterior), también ese.
    private static List<Segment> Choices(int[] choices, int current, Func<int, string> label, Action<int> pick) =>
        choices.Append(current).Distinct().Order().Select(m => new Segment(
            m == 0 ? tr("Nunca") : label(m), m == current, new Command(() => pick(m)))).ToList();

    private static List<Segment> Segments(int current, Action<int> pick) =>
        IdleChoices.Select(m => new Segment(
            m == 0 ? tr("Nunca") : m >= 120 ? $"{m / 60} h" : $"{m} min",
            m == current,
            new Command(() => pick(m)))).ToList();
}

public sealed record WeatherCountryItem(string Code, string Name);
public sealed record WeatherCityItem(string Id, string Name);
