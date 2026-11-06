using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Media;
using IsTargetSleeping.UI.Widgets;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// Una fila de la página Widgets: activar o quitar uno y subirlo o bajarlo en el Home.
public sealed record WidgetRow(string Title, string Glyph, string Detail, bool IsOn, bool CanToggle,
    ICommand Toggle, ICommand Up, ICommand Down, bool ShowDivider, string? Scope = null, ICommand? Resize = null, bool CanResize = false,
    string? WidgetId = null, ICommand? DropBefore = null, bool CanDrag = false)
{
    /// El mismo widget sale en la lista del Home y en la de la vista corta: el nombre accesible dice de cuál es.
    public string SwitchName => Scope is null ? Title : $"{Title} · {Scope}";
    public string UpHelp => tr("Subir %@", SwitchName);
    public string DownHelp => tr("Bajar %@", SwitchName);
    public string ResizeHelp => tr("Cambiar el tamaño de %@", SwitchName);
}

/// Un icono pequeño dentro de una carpeta: el glifo del widget, encendido si está en la lista.
public sealed record FolderMini(string Glyph, bool IsOn, ImageSource? Icon = null);

/// Una carpeta de widgets, cuadrada y de cristal: sus iconos en miniatura, cuántos hay activados y su orden de apertura.
public sealed record WidgetFolderTile(string Title, bool IsOpen, string Name, IReadOnlyList<FolderMini> Minis, ICommand Open, string AutomationId);

/// Las carpetas de una lista de la página Widgets. Al tocar una se abre encima de la página (FolderOverlay).
public sealed record WidgetFolders(IReadOnlyList<WidgetFolderTile> Tiles);

/// Una opción dentro de una carpeta abierta: un icono con su nombre, como las apps de una carpeta del iPhone.
public sealed record FolderOption(string Title, string Glyph, bool IsOn, string Name, ICommand Open, string AutomationId, ImageSource? Icon = null);

/// La carpeta abierta: su nombre grande y sus opciones.
public sealed record FolderOverlay(string Title, IReadOnlyList<FolderOption> Options, ICommand Close);

/// Cómo se ve un widget con un ancho: la ranura real, pintada con la plantilla del Home (o de la vista corta), sin respuesta al ratón.
public sealed record WidgetPreview(WidgetSlot Slot, string Caption, bool IsCurrent);

/// Una pieza de la pastilla cerrada tal como la pinta el notch: el estado (con su punto), el modelo, la hora, un dato en mono o una actividad en vivo.
public sealed record PillPart(string Kind, string Text, string Glyph = "", System.Windows.Media.Brush? Dot = null, double Progress = -1, string Tone = "", bool Highlight = false)
{
    public bool HasGlyph => Glyph.Length > 0 && Progress < 0;
    public bool HasRing => Progress >= 0;
}

/// La pastilla cerrada con sus datos y, resaltado, el que se está mirando.
public sealed record PillPreview(IReadOnlyList<PillPart> Parts);

/// Un botón redondo del dock (o el clavo de fijar).
public sealed record DockPart(string Glyph, bool Highlight, ImageSource? Icon = null);

/// Una cápsula del dock con sus botones, como la pinta el notch.
public sealed record DockPreview(IReadOnlyList<DockPart> Parts);

/// La ventana de una opción: activarla, elegir su tamaño y su posición, y ver cómo queda.
public sealed record OptionSheet(string Title, string Glyph, string Detail, bool IsOn, bool CanToggle, ICommand Toggle, string SwitchName, string SwitchLabel,
    string PreviewTitle, IReadOnlyList<object> Previews,
    string SizeTitle, IReadOnlyList<Segment> Sizes, string SizeHint,
    string MoveTitle, ICommand? Up, ICommand? Down, string UpHelp, string DownHelp,
    string CloseLabel, ICommand Close)
{
    public bool HasDetail => Detail.Length > 0;
    public bool HasSizes => Sizes.Count > 0;
    public bool HasSizeHint => SizeHint.Length > 0;
    public bool HasMove => Up is not null && Down is not null;
}

/// Una acción de la tira de favoritos del dock.
public sealed record FavoriteRow(string Title, string Glyph, bool IsOn, bool CanToggle, ICommand Toggle, bool ShowDivider, ImageSource? Icon = null);

/// Los widgets del Home, la página para elegirlos y los favoritos configurables.
public sealed partial class PanelViewModel
{
    public WidgetHub Hub { get; private set; } = null!;
    public List<WidgetSlot> HomeWidgets { get; private set; } = [];
    public List<WidgetSlot> PeekWidgets { get; private set; } = [];
    /// Páginas de la vista al pasar el ratón: cada una lleva PeekPerPage widgets (1 por defecto; 2 lado a lado; 4 como siempre).
    private int peekPage;
    public int PeekPerPage => Prefs.StoredList(PrefKeys.NotchPeekLayout)?.FirstOrDefault() is "2" ? 2 : Prefs.StoredList(PrefKeys.NotchPeekLayout)?.FirstOrDefault() is "4" ? 4 : 1;
    public List<Segment> PeekDots { get; private set; } = [];
    private bool peekIsMedia;
    /// La vista de letras está a la vista: la página de música del hover con la letra al lado (el notch se ensancha).
    public bool LyricsShown => Hub.Media.LyricsOpen && PeekPerPage == 1 && PeekWidgets.Any(w => WidgetLayout.BaseId(w.Id) == "media");   // lo que se ve de verdad, no un dato guardado: así el notch nunca queda más estrecho que la letra
    public string LyricsHelp => tr("Letras");
    /// La cabecera (batería, fecha y hora) solo sale en la vista de un widget; la página de música empieza arriba, con la cabecera junto a la portada,
    /// y las demás dejan sitio a la cabecera y se centran en lo que queda.
    public bool ShowPeekHeader => PeekPerPage == 1 && !LyricsShown;
    public System.Windows.VerticalAlignment PeekContentAlign => peekIsMedia ? System.Windows.VerticalAlignment.Top : System.Windows.VerticalAlignment.Center;
    public System.Windows.Thickness PeekContentMargin => PeekPerPage != 1 ? new(8, 4, 8, 4) : peekIsMedia ? new(0, 10, 0, 0) : new(0, 30, 0, 0);
    /// El mini dock de la vista corta: un icono por página (y el de Ajustes), que sale al pasar el ratón por los puntos.
    public List<PagerIcon> PeekIcons { get; private set; } = [];
    public ICommand OpenFullFromPeek { get; private set; } = null!;
    public string PeekOpenFullLabel => tr("Abrir el notch completo");
    public List<Segment> PeekLayoutSegments { get; private set; } = [];
    public bool HasPeekPages => PeekDots.Count > 1;
    /// Las flechas de página se esconden con la letra abierta (en Nusa la vista de letras va sin ellas).
    public bool ShowPageArrows => HasPeekPages && !LyricsShown;
    public string PeekLayoutTitle => tr("Widgets por página");
    public string PeekLayoutDetail => tr("Cuántos widgets se ven a la vez al pasar el ratón; el resto está en otras páginas.");
    public string PrevPageLabel => tr("Página anterior");
    public string NextPageLabel => tr("Página siguiente");
    public bool SelectPeekWidget(string id)
    {
        if (PeekWidgets.Any(widget => WidgetLayout.BaseId(widget.Id) == id)) return true;
        var ordered = OrderedPeekIds(PeekIds);
        int index = ordered.FindIndex(widget => WidgetLayout.BaseId(widget) == id);
        if (index < 0) return false;
        GoPeekPage(index / PeekPerPage);
        return true;
    }
    public void GoPeekPage(int page, int? slide = null)
    {
        if (page == peekPage || page < 0 || page >= PeekDots.Count) return;
        int direction = slide ?? (page > peekPage ? 1 : -1);
        PeekPageChanging?.Invoke();
        peekPage = page;
        RebuildPeekPages(PeekIds);
        Notify();
        PeekPageChanged?.Invoke(direction);
    }
    /// Justo antes de cambiar de página: la vista fotografía la que sale para deslizarla fuera mientras entra la nueva.
    public event Action? PeekPageChanging;
    /// Cambió la página de la vista corta (+1 hacia delante, -1 hacia atrás): la vista desliza su contenido.
    public event Action<int>? PeekPageChanged;
    /// Con `wrap` (flechas y teclas), pasar de la última página a la primera y de la primera a la última; la rueda se queda en los extremos.
    public void StepPeekPage(int delta, bool wrap = false)
    {
        int n = PeekDots.Count;
        if (wrap && n > 1) GoPeekPage((peekPage + delta + n) % n, Math.Sign(delta));
        else GoPeekPage(Math.Clamp(peekPage + delta, 0, n - 1));
    }
    public List<WidgetRow> WidgetRows { get; private set; } = [];
    public List<FavoriteRow> SystemMetricRows { get; private set; } = [];
    public string SystemMetricsTitle => tr("Gráficas de Sistema");
    public string SystemMetricsDetail => tr("Elige dos métricas para el widget compacto; al ensancharlo aparecen las seis.");
    public List<WidgetRow> PeekRows { get; private set; } = [];
    public WidgetFolders HomeFolders { get; private set; } = new([]);
    public WidgetFolders PeekFolders { get; private set; } = new([]);
    public WidgetFolders MetricFolders { get; private set; } = new([]);
    public WidgetFolders ActivityFolders { get; private set; } = new([]);
    public WidgetFolders PillFolders { get; private set; } = new([]);
    public WidgetFolders DockFolders { get; private set; } = new([]);
    public WidgetFolders FavoriteFolders { get; private set; } = new([]);
    /// De dónde sale cada lista de carpetas: lo que hace falta para abrir una carpeta y la ventana de cada una de sus opciones.
    private sealed record FolderSource(IReadOnlyList<WidgetFolder> Folders, Func<string, object?> Row, Func<string, string> Glyph, Func<string, bool> IsOn,
        string Kind = "", bool Peek = false, IReadOnlyList<string>? Ids = null);
    private readonly Dictionary<string, FolderSource> folderSources = [];
    private (string Scope, string Folder)? openOverlay;
    private string? openOption;
    /// El widget cuya vista previa está a la vista: sus datos se leen mientras tanto, aunque el Home esté cerrado.
    private string? previewWidget;
    public FolderOverlay? OverlayFolder { get; private set; }
    public OptionSheet? Sheet { get; private set; }
    public bool HasOverlayFolder => OverlayFolder is not null;
    public bool HasSheet => Sheet is not null;
    public ICommand CloseOverlay { get; private set; } = null!;
    public ICommand CloseSheet { get; private set; } = null!;
    /// Escape: cierra primero la ventana de la opción y luego la carpeta; dice si había algo que cerrar.
    public bool CloseTopOverlay()
    {
        if (openOption is not null) openOption = null;
        else if (openOverlay is not null) openOverlay = null;
        else return false;
        Notify();
        return true;
    }
    internal void CloseAllOverlays()
    {
        if (openOverlay is null && openOption is null) return;
        openOverlay = null;
        openOption = null;
        Notify();
    }
    public List<WidgetRow> PillRows { get; private set; } = [];
    public List<FavoriteRow> ActivityRows { get; private set; } = [];
    public List<FavoriteRow> FavoriteRows { get; private set; } = [];
    public List<FavoriteRow> DockRows { get; private set; } = [];
    public List<string> DockIds => DockCatalog.Normalize(Prefs.StoredList(PrefKeys.NotchDockPages));
    private List<string>? widgetDraft;
    public bool CustomizingWidgets => widgetDraft is not null;
    public bool NotCustomizingWidgets => !CustomizingWidgets;
    public ICommand BeginCustomize { get; private set; } = null!;
    public ICommand SaveCustomize { get; private set; } = null!;
    public ICommand CancelCustomize { get; private set; } = null!;
    public string DockTitle => tr("Botones del dock");
    public string DockIntro => tr("Inicio, Widgets y Ajustes siempre están. Añade aquí las páginas de Ollama y de Actividad.");
    public bool CanAddFavoriteApp { get; private set; }
    public ICommand AddFavoriteApp { get; private set; } = null!;
    public string AddAppLabel => tr("Añadir una app…");
    public string AddAppHelp => tr("Elige una app o un archivo para abrirlo con un toque desde el dock");
    private string widgetsSignature = "";

    public bool ShowingHome => view == PanelView.Home;
    public bool ShowingWidgets => view == PanelView.Widgets;
    /// La vista que lleva el logo y la píldora de estado en la cabecera.
    public bool ShowingBrand => view is PanelView.Main or PanelView.Home;
    /// A donde vuelve la «X» de Actividad, Ajustes y Widgets: el Home en el notch, el panel de siempre en la bandeja.
    private PanelView BackView => WideLayout ? PanelView.Home : PanelView.Main;

    public List<string> WidgetIds => WidgetLayout.Normalize(widgetDraft ?? Prefs.StoredList(PrefKeys.NotchWidgets), max: WidgetCatalog.MaxHomeCells, home: true);
    public List<string> PeekIds => WidgetLayout.Normalize(Prefs.StoredList(PrefKeys.NotchPeekWidgets), WidgetCatalog.DefaultPeek);
    public List<string> PillIds => PillCatalog.Normalize(Prefs.StoredList(PrefKeys.NotchPill));
    public List<string> ActivityIds => ActivityCatalog.Normalize(Prefs.StoredList(PrefKeys.NotchActivities));
    public List<string> FavoriteIds => FavoriteCatalog.Normalize(Prefs.StoredList(PrefKeys.NotchFavorites));

    public string WidgetsEmpty => tr("No hay widgets en el Home. Actívalos en la página Widgets.");
    public string SaveWidgetsLabel => tr("Guardar");
    public string CancelWidgetsLabel => tr("Descartar");
    public bool NoHomeWidgets => HomeWidgets.Count == 0;
    public string WidgetsIntro => tr("Elige qué ve el Home del notch y en qué orden. Se ven %d celdas por página; las demás, al pasar de página con las flechas (caben %d).", PanelViewModel.HomePageCells, WidgetCatalog.MaxHomeCells);
    public string WidgetsCells => tr("%d de %d celdas en uso", WidgetLayout.Area(WidgetIds), WidgetCatalog.MaxHomeCells);
    public string WidgetsFull => tr("Quita otro widget para hacer sitio.");
    public string WidgetShelfIntro => tr("Arrastra aquí para añadir; arrastra los widgets del Home para reordenarlos.");
    public bool WidgetsAreFull => WidgetLayout.Area(WidgetIds) >= WidgetCatalog.MaxHomeCells;
    public string PeekTitle => tr("Vista al pasar el ratón");
    public string PeekIntro => tr("Lo que se ve al acercar el ratón, antes de hacer clic. Para que sea corta, elige widgets pequeños.");
    public string PeekCells => tr("%d de %d celdas en uso", WidgetLayout.Cells(PeekIds), WidgetCatalog.MaxCells);
    public string PeekEmpty => tr("La vista corta está vacía. Elige widgets en la página Widgets.");
    public bool NoPeekWidgets => PeekWidgets.Count == 0;
    public string PeekScope => tr("vista al pasar el ratón");
    public string PillTitle => tr("Pastilla cerrada");
    public string PillIntro => tr("Lo que enseña el notch cuando está cerrado. Caben %d datos.", PillCatalog.Max);
    public string PillCount => tr("%d de %d datos", PillIds.Count, PillCatalog.Max);
    public string PillScope => tr("pastilla");

    /// El texto de cada dato de la pastilla cerrada.
    public string PillValue(string id) => id switch
    {
        "state" => PillText,
        "model" => NotchModel,
        "ram" => NotchRam,
        "vram" => ShowGpu ? GpuNow.Used.MemoryGB() : "—",
        "cpu" => $"CPU {Hub.SystemStats.CpuLabel}",
        // La pastilla enseña la hora en 12 h con «AM» o «PM» (siempre en ese formato, para que la ventana la separe del resto: «12:54 AM»).
        "clock" => DateTime.Now.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture),
        "battery" => Hub.BatteryInfo.HasBattery ? Hub.BatteryInfo.Label : "—",
        "pomodoro" => Hub.Pomodoro.Remaining,
        // Vacío mientras el equipo no trabaja: la pastilla esconde entonces ese dato.
        "team" => Hub.Team.PillText,
        // Vacío sin proveedores con sesión: la pastilla esconde entonces ese dato.
        "usage" => Hub.Usage.PillText,
        // Vacío mientras no suena nada: la pastilla esconde entonces ese dato.
        "media" => Hub.Media.IsPlaying ? MediaPillText(Hub.Media.MediaTitle, Hub.Media.Artist) : "",
        _ => "",
    };

    public string ActivitiesTitle => tr("Actividades en vivo");
    public string ActivitiesIntro => tr("El punto de cámara va a la izquierda del notch (aparece cuando está en uso; actívalo para verlo también apagado). Las demás actividades salen a la derecha de una en una: las rápidas (volumen, brillo, liberar RAM…) pasan por delante y las que duran se turnan.");

    /// Lo que está pasando ahora y el usuario deja ver: la pastilla cerrada lo enseña a su lado.
    public List<Activity> LiveActivities
    {
        get
        {
            var now = new List<Activity>();
            void Add(string id, string text, string? glyph = null, double progress = -1) => now.Add(new Activity(id, glyph ?? ActivityCatalog.Find(id)!.Glyph, text, ActivityCatalog.Find(id)!.Priority, ActivityCatalog.Find(id)!.Left, progress));
            // El volumen, el brillo, Bloq. Mayús/Núm y el idioma ya no son un icono pequeño: el notch cerrado los enseña como un aviso propio (ver CurrentHud).
            if (Camera.IsInUse || ActivityIds.Contains("camera")) Add("camera", "");
            if (DateTime.UtcNow < noticeUntil) Add("notice", noticeTitle);
            // La música es un indicador más de la derecha (su ecualizador), si el usuario la quiere ahí: como actividad o como dato de la pastilla.
            if (Hub.Media.IsPlaying) Add("media", MediaPillText(Hub.Media.MediaTitle, Hub.Media.Artist));
            if (IsCleaning) Add("clean", tr("Liberando…"));
            if (P is Power.Starting or Power.Stopping) Add("power", P == Power.Starting ? tr("Encendiendo…") : tr("Apagando…"));
            if (Mem.Pressure == SystemMemory.Level.Critical) Add("pressure", tr("RAM crítica"));
            if (Hub.Pomodoro.Running) Add("pomodoro", Hub.Pomodoro.Remaining);
            if (Hub.Awake.On) Add("awake", "");
            var automationNotice = Hub.Agents.AutomationNotice(DateTime.UtcNow);
            if (automationNotice is { Length: > 0 }) Add("agents", tr("Tarea: %@", automationNotice));
            else
            {
                var agent = Hub.Agents.Sessions.FirstOrDefault(s => s.State is AgentState.Working or AgentState.NeedsAttention);
                var worry = agentAlerts.FirstOrDefault(a => a.Kind is AgentAlertKind.Stalled or AgentAlertKind.Loop);
                if (worry is not null) Add("agents", worry.Kind == AgentAlertKind.Loop ? tr("Agente en bucle") : tr("Agente sin señales"));
                else if (agent is not null) Add("agents", agent.State == AgentState.NeedsAttention ? tr("Agente necesita atención") : tr("Agente trabajando"));
            }
            if (Hub.Team.Busy) Add("team", Hub.Team.PillText, progress: Hub.Team.ProgressFraction);
            else if (Hub.Team.Ready) Add("team", Hub.Team.ReadyText, "\uE73E");
            if (Hub.ClockInfo.HasCurrentEvent) Add("meeting", $"{Hub.ClockInfo.CurrentEventTitle} · {Hub.ClockInfo.CurrentEventRemaining}", progress: Hub.ClockInfo.CurrentEventProgress);
            // A la derecha sale una a la vez: las rápidas pasan por delante y las que duran se turnan cada 4 segundos.
            var (left, right) = ActivityQueue.PickSides(now, PillIds.Contains("media") ? [.. ActivityIds, "media"] : ActivityIds, rotation: (int)(Environment.TickCount64 / 4000), cameraInUse: Camera.IsInUse);
            return [.. left, .. right];
        }
    }

    /// El aviso del sistema que se enseña ahora en la pastilla cerrada (volumen, brillo, Bloq. Mayús/Núm, idioma del teclado), o null.
    public HudInfo? CurrentHud
    {
        get
        {
            var ids = ActivityIds;
            var events = new List<(DateTime At, HudInfo Info)>();
            if (ids.Contains("volume") && Hub.Volume.ChangedAt != default)
                events.Add((Hub.Volume.ChangedAt, new HudInfo(HudKind.Volume, tr("Volumen"), Hub.Volume.Muted ? 0 : Hub.Volume.Level, Muted: Hub.Volume.Muted)));
            if (ids.Contains("brightness") && Hub.Huds.BrightnessInfo is { } brightness)
                events.Add((brightness.At, new HudInfo(HudKind.Brightness, tr("Brillo"), brightness.Percent / 100.0)));
            if (ids.Contains("bluetooth") && Hub.Huds.BluetoothInfo is { } bt)
            {
                var sub = !bt.Connected ? tr("Desconectado") : bt.Percent is { } percent ? $"{tr("Conectado")} · {percent}%" : tr("Conectado");
                events.Add((bt.At, new HudInfo(HudKind.Bluetooth, bt.Name, On: bt.Connected, Sub: sub, Device: bt.Kind, Battery: bt.Percent / 100.0)));
            }
            if (ids.Contains("locks"))
            {
                if (Hub.Huds.LockInfo is { } lockInfo)
                    events.Add((lockInfo.At, new HudInfo(lockInfo.Caps ? HudKind.CapsLock : HudKind.NumLock, tr(lockInfo.Caps ? "Bloq. Mayús" : "Bloq. Núm"), On: lockInfo.On)));
                if (Hub.Huds.LanguageInfo is { } language)
                    events.Add((language.At, new HudInfo(HudKind.Language, language.Name, Chip: language.Chip)));
            }
            var hud = HudPicker.Latest(events, DateTime.Now);
            // Sin otro aviso del sistema a la vista, con letras sincronizadas el notch cerrado enseña la línea que se canta (como en Nusa).
            if (hud is null && Prefs.Lyrics && Hub.Media.LyricsOpen && Prefs.LyricsPill && Hub.Media.IsPlaying)   // LyricsOpen: el botón de letras del widget de música también las quita del notch cerrado
            {
                switch (Hub.Media.LyricStatus)
                {
                    case LyricsStatus.Ready:
                    {
                        // Antes de la primera línea (la introducción) se enseña ya, en gris como una sombra; al empezar a cantarse se pone blanca (como en Nusa).
                        bool before = Hub.Media.LyricIndex < 0;
                        if ((before ? Hub.Media.LyricLines.FirstOrDefault()?.Text : Hub.Media.CurrentLyric) is { Length: > 0 } line) hud = new HudInfo(HudKind.Lyric, line, Dim: before);
                        break;
                    }
                    // Mientras se busca la letra y cuando la canción no tiene, la pastilla lo dice en gris (como en Nusa) en vez de volver a la hora.
                    case LyricsStatus.Loading: hud = new HudInfo(HudKind.Lyric, tr("Cargando letra…"), Dim: true); break;
                    case LyricsStatus.None: hud = new HudInfo(HudKind.Lyric, tr("Letra no disponible"), Dim: true); break;
                }
            }
            return hud;
        }
    }

    private DateTime noticeUntil;
    private string noticeTitle = "";

    /// Llegó un aviso de la app: la pastilla cerrada enseña una campana unos segundos (el aviso nativo de Windows sale igual).
    public void RaiseNotice(string title) => RaiseNotice(title, 7);

    public void RaiseNotice(string title, int seconds)
    {
        // Los avisos nacen en hilos de fondo (la limpieza, el vigilante…) y esto repinta la pastilla: va al hilo de la interfaz.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) { dispatcher.BeginInvoke(() => RaiseNotice(title, seconds)); return; }
        noticeTitle = title;
        noticeUntil = DateTime.UtcNow.AddSeconds(seconds);
        PropertyChanged?.Invoke(this, new(nameof(LiveActivities)));
    }

    /// Hay un aviso reciente (la campana con punto rojo de la cabecera de la vista corta): dura lo mismo que en la pastilla cerrada, unos segundos.
    public DateTime? NoticeStamp => noticeUntil == default ? null : noticeUntil;
    public bool UnreadNotice => DateTime.UtcNow < noticeUntil;
    public void ClearNotice() => noticeUntil = default;

    /// La música en la pastilla (portada a la izquierda, ecualizador a la derecha): suena algo y el usuario la quiere ahí, como dato de la pastilla o
    /// como actividad «Música sonando». No es una actividad más: no ocupa el turno de la derecha ni cambia de sitio la hora.
    public bool ShowMusicInPill => Hub.Media.IsPlaying && (PillIds.Contains("media") || ActivityIds.Contains("media"));

    /// Lo que enseña la pastilla de la canción que suena: «título · artista» (o solo el título si no hay artista).
    public static string MediaPillText(string title, string artist) => artist.Length > 0 ? $"{title} · {artist}" : title;

    public string TeamCancelHelp => tr("Cancelar la tarea del equipo");
    public string FavoritesTitle => tr("Favoritos");
    public string FavoritesIntro => tr("Hasta %d acciones de un toque junto a la navegación del dock.", FavoriteCatalog.Max);
    public string NotchTabOllama => tr("Ollama");
    public string NotchTabWidgets => tr("Widgets");
    public string AwakeHelp => Hub.Awake.ToggleHelp;
    public string PomodoroHelp => Hub.Pomodoro.ToggleHelp;

    private void InitWidgets()
    {
        CloseOverlay = new Command(CloseAllOverlays);
        CloseSheet = new Command(() => { openOption = null; Notify(); });
        BeginCustomize = new Command(BeginCustomizeWidgets);
        SaveCustomize = new Command(SaveCustomizeWidgets);
        CancelCustomize = new Command(CancelCustomizeWidgets);
        Hub = new WidgetHub(this);
        HookGallery();
        // Los nombres del dock («Pausar el temporizador», «Dejar que Windows suspenda») cambian solo cuando cambia el estado.
        Hub.Awake.PropertyChanged += (_, _) => Notify();
        Hub.Pomodoro.StateChanged += Notify;
        Hub.Media.PropertyChanged += (_, _) => Notify();
        Hub.Volume.Changed += () => PropertyChanged?.Invoke(this, new(nameof(LiveActivities)));
    }

    public void BeginCustomizeWidgets() { if (widgetDraft is null) { widgetDraft = WidgetIds; Notify(); } }
    public void CancelCustomizeWidgets() { if (widgetDraft is not null) { widgetDraft = null; Notify(); } }
    public void SaveCustomizeWidgets()
    {
        if (widgetDraft is null) return;
        var saved = WidgetLayout.Normalize(widgetDraft, max: WidgetCatalog.MaxHomeCells, home: true);
        widgetDraft = null;
        Prefs.SetList(PrefKeys.NotchWidgets, saved);
    }
    public void MoveWidgetBefore(string source, string target)
    {
        if (widgetDraft is null) return;
        if (!widgetDraft.Any(x => WidgetLayout.BaseId(x) == target))
        {
            if (!widgetDraft.Any(x => WidgetLayout.BaseId(x) == WidgetLayout.BaseId(source)) && WidgetLayout.CanAdd(widgetDraft, source, WidgetCatalog.MaxHomeCells, home: true))
                widgetDraft = WidgetLayout.Add(widgetDraft, source, WidgetCatalog.MaxHomeCells, home: true);
            Notify();
            return;
        }
        widgetDraft = WidgetLayout.MoveBefore(widgetDraft, source, target, WidgetCatalog.MaxHomeCells, home: true);
        Notify();
    }
    public void AddWidgetToDraft(string id)
    {
        if (widgetDraft is null || !WidgetLayout.CanAdd(widgetDraft, id, WidgetCatalog.MaxHomeCells, home: true)) return;
        widgetDraft = WidgetLayout.Add(widgetDraft, id, WidgetCatalog.MaxHomeCells, home: true);
        Notify();
    }
    public void ResizeWidgetInDraft(string id)
    {
        if (widgetDraft is null) return;
        var resized = WidgetLayout.ToggleWidth(widgetDraft, id, WidgetCatalog.MaxHomeCells, home: true);
        if (resized.SequenceEqual(widgetDraft)) return;
        widgetDraft = resized;
        Notify();
    }
    private void StoreWidgetLayout(string key, List<string> values)
    {
        if (key == PrefKeys.NotchWidgets && widgetDraft is not null) { widgetDraft = values; Notify(); }
        else Prefs.SetList(key, values);
    }

    private void RebuildPeekPages(List<string> peek)
    {
        // Si suena música, su widget es la primera página.
        var ordered = OrderedPeekIds(peek);
        var pages = ordered.Chunk(PeekPerPage).ToList();
        peekIsMedia = PeekPerPage == 1 && pages.Count > 0 && WidgetLayout.BaseId(pages[Math.Clamp(peekPage, 0, pages.Count - 1)][0]) == "media";
        peekPage = Math.Clamp(peekPage, 0, Math.Max(0, pages.Count - 1));
        PeekWidgets = Hub.Slots(pages.Count == 0 ? [] : pages[peekPage], peek: true, compact: PeekPerPage == 1);
        PeekDots = Enumerable.Range(0, pages.Count).Select(i => new Segment(tr("Página %d", i + 1), i == peekPage, new Command(() => GoPeekPage(i)))).ToList();
        PeekIcons = pages.Select((page, i) => new PagerIcon(WidgetCatalog.Find(WidgetLayout.BaseId(page[0]))?.Glyph ?? "",
            string.Join(" + ", page.Select(id => WidgetCatalog.Find(WidgetLayout.BaseId(id))?.Title ?? id)), i == peekPage, new Command(() => GoPeekPage(i)))).ToList();
    }

    // MARK: páginas del Home (el notch abierto muestra pocos widgets a la vez y se pasa de página con flechas)

    /// Lo que cabe en una página del Home, en celdas (ancho × alto): como en las primeras versiones, cuatro.
    public const int HomePageCells = 4;
    private int homePage;
    public List<Segment> HomeDots { get; private set; } = [];
    public bool HasHomePages => HomeDots.Count > 1 && !CustomizingWidgets;
    /// El paginador vive en la cabecera: así las flechas se ven siempre, también con un widget alto como Equipo.
    public bool ShowHomePager => ShowingHome && HasHomePages;
    /// Cambió la página del Home (+1 hacia delante, -1 hacia atrás): la vista desliza su contenido.
    public event Action<int>? HomePageChanged;
    public ICommand PrevHomePage => new Command(() => StepHomePage(-1));
    public ICommand NextHomePage => new Command(() => StepHomePage(1));

    /// Reparte los widgets en páginas de HomePageCells celdas, sin cambiar su orden; uno más grande que la página va solo.
    public static List<List<string>> HomePages(IEnumerable<string> ids)
    {
        var pages = new List<List<string>>();
        int used = 0;
        foreach (var id in ids)
        {
            int area = WidgetLayout.Span(id) * WidgetLayout.Rows(id);
            if (pages.Count == 0 || used + area > HomePageCells) { pages.Add([]); used = 0; }
            pages[^1].Add(id);
            used += area;
        }
        return pages;
    }

    public void StepHomePage(int delta)
    {
        int n = HomeDots.Count;
        if (n < 2) return;
        homePage = (homePage + delta + n) % n;
        Notify();
        HomePageChanged?.Invoke(Math.Sign(delta));
    }

    private void RebuildHomePages(List<string> ids)
    {
        var pages = HomePages(ids);
        homePage = Math.Clamp(homePage, 0, Math.Max(0, pages.Count - 1));
        HomeDots = Enumerable.Range(0, pages.Count).Select(i => new Segment(tr("Página %d", i + 1), i == homePage, new Command(() => { int from = homePage; homePage = i; Notify(); HomePageChanged?.Invoke(i >= from ? 1 : -1); }))).ToList();
        // Editando se ve la lista entera (con sus controles); si no, solo la página actual.
        var shown = CustomizingWidgets || pages.Count == 0 ? ids : WidgetLayout.FillPage(pages[homePage], HomePageCells);
        HomeWidgets = Hub.Slots(shown, peek: false, customizing: CustomizingWidgets, resize: ResizeWidgetInDraft);
    }

    private List<string> OrderedPeekIds(List<string> peek) => Hub.Media.HasSession
        ? [.. peek.Where(id => WidgetLayout.BaseId(id) == "media"), .. peek.Where(id => WidgetLayout.BaseId(id) != "media")]
        : peek;

    private void RebuildWidgets()
    {
        if (Hub.Media.LyricsEnabled is null) Hub.Media.SetLyricsOpen(Prefs.LyricsView);   // la vista de letra vuelve a como se dejó (cerrada si no se abrió)
        Hub.Media.LyricsEnabled ??= () => Prefs.Lyrics;
        Hub.Media.ToggleLyricsView ??= new Command(() =>
        {
            // El botón de letras es el consentimiento: la primera vez activa las letras (mandan el título y el artista a lrclib.net).
            if (!Prefs.Lyrics) Prefs.ToggleLyrics();
            Hub.Media.RefreshLyrics(true);
            Hub.Media.SetLyricsOpen(!Hub.Media.LyricsOpen);
            Prefs.SetLyricsView(Hub.Media.LyricsOpen);
            Notify();
        });
        Hub.Media.RefreshLyrics();
        var ids = WidgetIds;
        var peek = PeekIds;
        var pill = PillIds;
        var favorites = FavoriteIds;
        var activities = ActivityIds;
        var dockPages = DockIds;
        var signature = $"{string.Join(",", ids)}#{string.Join(",", peek)}#{string.Join(",", pill)}#{string.Join(",", favorites)}#{string.Join(",", activities)}#{string.Join(",", dockPages)}#{string.Join(",", SystemMetricCatalog.Normalize(Prefs.List(PrefKeys.SystemMetrics)))}#{Prefs.Switch(PrefKeys.AgentsEnabled, false)}#{Prefs.Switch(PrefKeys.WeatherEnabled, false)}#{Prefs.Switch(PrefKeys.UsageEnabled, false)}#{Prefs.Switch(PrefKeys.OcrEnabled, false)}#{PeekPerPage}#{Hub.Media.HasSession}#{Effective}#{CustomizingWidgets}#{homePage}#{openOverlay}#{openOption}";
        if (signature == widgetsSignature) return;
        widgetsSignature = signature;

        RebuildHomePages(ids);
        RebuildPeekPages(peek);
        PeekLayoutSegments = new[] { 1, 2, 4 }.Select(n => new Segment(n == 1 ? tr("1 widget") : tr("%d widgets", n), n == PeekPerPage,
            new Command(() => Prefs.SetList(PrefKeys.NotchPeekLayout, [n.ToString()])))).ToList();
        WidgetRows = BuildRows(ids, PrefKeys.NotchWidgets, null);
        var systemMetrics = SystemMetricCatalog.Normalize(Prefs.List(PrefKeys.SystemMetrics));
        SystemMetricRows = SystemMetricCatalog.All.Select((id, i) => new FavoriteRow(id switch
        {
            "cpu" => tr("CPU"), "ram" => "RAM", "gpu" => tr("GPU"), "disk" => tr("Disco"),
            "battery" => tr("Batería"), _ => tr("Red"),
        }, MetricGlyph(id), systemMetrics.Contains(id), true,
            new Command(() => Prefs.SetList(PrefKeys.SystemMetrics, SystemMetricCatalog.Toggle(Prefs.List(PrefKeys.SystemMetrics), id))), i > 0)).ToList();
        PeekRows = BuildRows(peek, PrefKeys.NotchPeekWidgets, PeekScope);
        HomeFolders = BuildFolders("home", WidgetCatalog.Folders, id => WidgetRows.FirstOrDefault(r => r.WidgetId == id), id => WidgetCatalog.Find(id)!.Glyph, id => ids.Any(x => WidgetLayout.BaseId(x) == id), kind: "widgets", ids: ids);
        PeekFolders = BuildFolders("peek", WidgetCatalog.Folders, id => PeekRows.FirstOrDefault(r => r.WidgetId == id), id => WidgetCatalog.Find(id)!.Glyph, id => peek.Any(x => WidgetLayout.BaseId(x) == id), kind: "widgets", peek: true, ids: peek);
        // El anillo de batería del hueco izquierdo de la pastilla siempre necesita leer la batería.
        Hub.SetPillNeeds(PillCatalog.WidgetsNeeded(pill).Append("battery").Distinct());
        // La música solo se lee si algo la necesita: el widget en alguna lista o su actividad permitida.
        if (ids.Contains("media") || peek.Contains("media") || pill.Contains("media") || activities.Contains("media")) Hub.Media.Start();
        if (activities.Contains("volume")) Hub.Volume.Start();
        Hub.SetActivityNeeds(activities);
        DockRows = DockCatalog.Optional.Select((info, i) =>
        {
            var id = info.Id;
            return new FavoriteRow(info.Title, info.Glyph, dockPages.Contains(id), true,
                new Command(() => Prefs.SetList(PrefKeys.NotchDockPages, DockCatalog.Toggle(dockPages, id))), i > 0);
        }).ToList();
        ActivityRows = ActivityCatalog.All.Select((info, i) =>
        {
            var id = info.Id;
            return new FavoriteRow(info.Title, info.Glyph, activities.Contains(id), true,
                new Command(() => Prefs.SetList(PrefKeys.NotchActivities, ActivityCatalog.Toggle(activities, id))), i > 0);
        }).ToList();
        PillRows = PillCatalog.All.Select((info, i) =>
        {
            var id = info.Id;
            var on = pill.Contains(id);
            return new WidgetRow(info.Title, info.Glyph, info.Detail, on, on || pill.Count < PillCatalog.Max,
                new Command(() => Prefs.SetList(PrefKeys.NotchPill, PillCatalog.Toggle(pill, id))),
                new Command(() => Prefs.SetList(PrefKeys.NotchPill, WidgetLayout.Move(pill, id, -1))),
                new Command(() => Prefs.SetList(PrefKeys.NotchPill, WidgetLayout.Move(pill, id, +1))),
                i > 0, PillScope);
        }).ToList();
        // Las acciones de siempre y, después, las apps del usuario (que aquí solo se pueden quitar).
        FavoriteRows = FavoriteCatalog.All.Concat(favorites.Where(FavoriteCatalog.IsApp).Select(id => FavoriteCatalog.Find(id)!)).Select((info, i) =>
        {
            var id = info.Id;
            var on = favorites.Contains(id);
            return new FavoriteRow(info.Title, info.Glyph, on, on || favorites.Count < FavoriteCatalog.Max,
                new Command(() => Prefs.SetList(PrefKeys.NotchFavorites, FavoriteCatalog.Toggle(favorites, id))), i > 0, FavoriteIcon(id));
        }).ToList();
        CanAddFavoriteApp = favorites.Count < FavoriteCatalog.Max;
        AddFavoriteApp = new Command(() =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = AddAppLabel, Filter = tr("Apps y accesos directos") + "|*.exe;*.lnk|*.*|*.*" };
            if (dialog.ShowDialog() == true) Prefs.SetList(PrefKeys.NotchFavorites, FavoriteCatalog.Toggle(favorites, FavoriteCatalog.AppId(dialog.FileName)));
        });
        var favoriteApps = favorites.Where(FavoriteCatalog.IsApp).ToList();
        var favoriteIds = FavoriteCatalog.All.Select(f => f.Id).Concat(favoriteApps).ToList();
        MetricFolders = BuildFolders("metrics", SystemMetricCatalog.Folders, Lookup(SystemMetricCatalog.All, SystemMetricRows), MetricGlyph, systemMetrics.Contains, "metrics");
        ActivityFolders = BuildFolders("activities", ActivityCatalog.Folders, Lookup(ActivityCatalog.All.Select(a => a.Id).ToList(), ActivityRows), id => ActivityCatalog.Find(id)!.Glyph, activities.Contains, "activities");
        PillFolders = BuildFolders("pill", PillCatalog.Folders, Lookup(PillCatalog.All.Select(p => p.Id).ToList(), PillRows), id => PillCatalog.Find(id)!.Glyph, pill.Contains, "pill");
        DockFolders = BuildFolders("dock", DockCatalog.Folders, Lookup(DockCatalog.Optional.Select(d => d.Id).ToList(), DockRows), id => DockCatalog.Find(id)!.Glyph, dockPages.Contains, "dock");
        FavoriteFolders = BuildFolders("favorites", favoriteApps.Count == 0 ? FavoriteCatalog.Folders : [.. FavoriteCatalog.Folders, new WidgetFolder("apps", "Mis apps", favoriteApps)],
            Lookup(favoriteIds, FavoriteRows), id => FavoriteCatalog.Find(id)?.Glyph ?? "", favorites.Contains, "favorites");
        BuildOverlay();
        UpdateProcessSampling();
    }

    private static string MetricGlyph(string id) => id switch
    {
        "cpu" => PillCatalog.Find("cpu")!.Glyph, "ram" => PillCatalog.Find("ram")!.Glyph, "gpu" => PillCatalog.Find("vram")!.Glyph,
        "battery" => PillCatalog.Find("battery")!.Glyph, "disk" => "\uEDA2", _ => "\uE774",
    };

    private static ImageSource? FavoriteIcon(string id) =>
        FavoriteCatalog.IsApp(id) ? AppIcons.For(FavoriteCatalog.AppPath(id), large: true) : null;

    /// Cada fila con su id (las listas se construyen en el orden del catálogo), para ponerlas dentro de su carpeta.
    private static Func<string, object?> Lookup(IReadOnlyList<string> ids, IEnumerable<object> rows)
    {
        var byId = ids.Zip(rows, (id, row) => (id, row)).ToDictionary(x => x.id, x => x.row);
        return id => byId.GetValueOrDefault(id);
    }

    private List<WidgetRow> BuildRows(List<string> ids, string key, string? scope) =>
        WidgetCatalog.All.Select((info, i) =>
        {
            // El Home admite doce celdas (cuatro a la vista y el resto al desplazarte); la vista corta, cuatro.
            bool home = key == PrefKeys.NotchWidgets;
            int max = home ? WidgetCatalog.MaxHomeCells : WidgetCatalog.MaxCells;
            var id = info.Id;
            var token = ids.FirstOrDefault(x => WidgetLayout.BaseId(x) == id);
            var on = token is not null;
            return new WidgetRow(info.Title, info.Glyph,
                WidgetLayout.CanResize(id, home) ? (WidgetLayout.Span(token ?? id) == 2 ? tr("2 celdas") : tr("%@ celda", 1)) : info.Cells == 2 ? tr("2 celdas") : tr("%@ celda", 1),
                on, on || WidgetLayout.Fit(ids, id, max, home) is not null,
                new Command(() => StoreWidgetLayout(key, on ? WidgetLayout.Remove(ids, id) : WidgetLayout.Fit(ids, id, max, home) is { } fit ? [.. ids, fit] : [.. ids])),
                new Command(() => StoreWidgetLayout(key, WidgetLayout.Move(ids, id, -1))),
                new Command(() => StoreWidgetLayout(key, WidgetLayout.Move(ids, id, +1))),
                i > 0, scope, new ParamCommand(v => { if (v is int width) StoreWidgetLayout(key, WidgetLayout.SetWidth(ids, id, width, max, home)); }), on && WidgetLayout.CanResize(id, home),
                id, new ParamCommand(v => { if (v is string source) MoveWidgetBefore(source, id); }), key == PrefKeys.NotchWidgets && widgetDraft is not null);
        }).ToList();

    /// Las carpetas de una lista: una abierta a la vez, como en el iPhone; volver a tocarla la cierra.
    private WidgetFolders BuildFolders(string scope, IReadOnlyList<WidgetFolder> folders, Func<string, object?> row, Func<string, string> glyph, Func<string, bool> isOn,
        string kind = "", bool peek = false, IReadOnlyList<string>? ids = null)
    {
        folderSources[scope] = new FolderSource(folders, row, glyph, isOn, kind, peek, ids);
        var tiles = folders.Select(folder =>
        {
            var on = folder.Widgets.Count(isOn);
            return new WidgetFolderTile(folder.Title, openOverlay == (scope, folder.Id),
                tr("%@: %d de %d activados", folder.Title, on, folder.Widgets.Count),
                folder.Widgets.Select(w => new FolderMini(glyph(w), isOn(w), FolderIcon(w, row(w)))).ToList(),
                new Command(() => { openOverlay = (scope, folder.Id); openOption = null; Notify(); }), $"folder-{scope}-{folder.Id}");
        }).ToList();
        return new WidgetFolders(tiles);
    }

    private static string RowTitle(object row) => row switch { WidgetRow w => w.Title, FavoriteRow f => f.Title, _ => "" };

    private static ImageSource? FolderIcon(string id, object? row) =>
        (row as FavoriteRow)?.Icon ?? WidgetLogoCatalog.For(id);

    /// La carpeta abierta y la ventana de su opción, con el estado de ahora (se rehace con cada cambio).
    private void BuildOverlay()
    {
        OverlayFolder = null; Sheet = null; previewWidget = null;
        if (openOverlay is not { } open || !folderSources.TryGetValue(open.Scope, out var source) || source.Folders.FirstOrDefault(f => f.Id == open.Folder) is not { } folder)
        {
            openOverlay = null; openOption = null;
            return;
        }
        var options = folder.Widgets.Where(id => source.Row(id) is not null).Select(id =>
        {
            var row = source.Row(id)!;
            var title = RowTitle(row);
            var on = source.IsOn(id);
            return new FolderOption(title, source.Glyph(id), on, $"{title}, {tr(on ? "activado" : "desactivado")}", new Command(() => { openOption = id; Notify(); }), $"option-{open.Scope}-{id}", FolderIcon(id, row));
        }).ToList();
        OverlayFolder = new FolderOverlay(folder.Title, options, CloseOverlay);
        if (openOption is { } chosen && folder.Widgets.Contains(chosen) && source.Row(chosen) is { } row) Sheet = BuildSheet(source, chosen, row);
        else openOption = null;
    }

    /// Un dato de la pastilla cerrada con el formato que le da el notch; sin valor (música parada, equipo ocioso) se enseña uno de ejemplo.
    private PillPart PillPartOf(string id, bool highlight = false)
    {
        var text = PillValue(id);
        if (text.Length == 0) text = id switch { "media" => tr("Canción · Artista"), "team" => "2/6", _ => PillCatalog.Find(id)?.Title ?? id };
        var glyph = PillCatalog.Find(id)?.Glyph ?? "";
        return id switch
        {
            "state" => new PillPart("state", text, "", PillForeground, Highlight: highlight),
            "model" => new PillPart("model", text, Highlight: highlight),
            "clock" => new PillPart("clock", text, Highlight: highlight),
            "media" => new PillPart("activity", text, glyph, Highlight: highlight),
            _ => new PillPart("mono", text, Highlight: highlight),
        };
    }

    /// Lo que enseña cada actividad en vivo (con el progreso de las que lo tienen), con los mismos textos que el notch.
    private (string Text, double Progress) ActivitySample(string id) => id switch
    {
        "volume" => ("60 %", -1),
        "clean" => (tr("Liberando…"), -1),
        "power" => (tr("Encendiendo…"), -1),
        "pressure" => (tr("RAM crítica"), -1),
        "pomodoro" => (Hub.Pomodoro.Remaining, -1),
        "awake" => ("", -1),
        "media" => (tr("Canción · Artista"), -1),
        "brightness" => (tr("Brillo %@", 80), -1),
        "locks" => (tr("Mayús activada"), -1),
        "bluetooth" => (tr("Auriculares"), -1),
        "team" => ("2/6", 0.4),
        "agents" => (tr("Agente trabajando"), -1),
        "meeting" => (tr("Reunión · 12 min"), 0.5),
        _ => ("", -1),
    };

    private OptionSheet BuildSheet(FolderSource source, string id, object row)
    {
        string title, detail = "", switchName;
        bool on, canToggle;
        ICommand toggle;
        ICommand? up = null, down = null, resize = null;
        string upHelp = "", downHelp = "";
        switch (row)
        {
            case WidgetRow w:
                (title, on, canToggle, toggle, switchName) = (w.Title, w.IsOn, w.CanToggle, w.Toggle, w.SwitchName);
                if (source.Kind != "widgets") detail = w.Detail;
                if (w.IsOn) (up, down, upHelp, downHelp) = (w.Up, w.Down, w.UpHelp, w.DownHelp);
                resize = w.Resize;
                break;
            case FavoriteRow f:
                (title, on, canToggle, toggle, switchName) = (f.Title, f.IsOn, f.CanToggle, f.Toggle, f.Title);
                break;
            default: throw new InvalidOperationException();
        }

        // Los widgets del Home y de la vista corta: su tamaño y cómo se ve con cada uno (la ranura real, con su plantilla y sus datos).
        var sizes = new List<Segment>();
        var previews = new List<object>();
        var sizeHint = "";
        switch (source.Kind)
        {
            case "widgets" when Hub.Find(id) is { } model:
                previewWidget = id;
                var token = source.Ids?.FirstOrDefault(x => WidgetLayout.BaseId(x) == id);
                var span = WidgetLayout.Span(token ?? id);
                bool resizable = WidgetLayout.CanResize(id);
                if (resizable)
                {
                    void Pick(int wanted) { if (on && span != wanted) resize?.Execute(wanted); }
                    sizes.Add(new Segment(tr("%@ celda", 1), span == 1, new Command(() => Pick(1))));
                    sizes.Add(new Segment(tr("2 celdas"), span == 2, new Command(() => Pick(2))));
                    if (!on) sizeHint = tr("Actívalo para cambiar el tamaño.");
                    previews.Add(new WidgetPreview(new WidgetSlot(model, 1, 1, source.Peek), tr("%@ celda", 1), span == 1));
                }
                previews.Add(new WidgetPreview(new WidgetSlot(model, 2, 2, source.Peek), tr("2 celdas"), span == 2 || !resizable));
                // Solo en su lista, los que tienen una vista grande para eso llenan la celda.
                if (!source.Peek && id is "clock" or "team" or "code" or "media")
                    previews.Add(new WidgetPreview(new WidgetSlot(model, 2, 2, false, alone: true), tr("2 celdas · solo en su lista"), false));
                break;
            case "metrics" when Hub.Find("system") is { } system:
                // Las gráficas se eligen para el widget Sistema: se ve ese widget con las que hay elegidas ahora.
                previewWidget = "system";
                previews.Add(new WidgetPreview(new WidgetSlot(system, 1, 1, false), tr("Widget Sistema · %@ celda", 1), true));
                previews.Add(new WidgetPreview(new WidgetSlot(system, 2, 2, false), tr("Widget Sistema · 2 celdas"), false));
                break;
            case "pill":
                {
                    var parts = PillIds.Select(p => PillPartOf(p, p == id)).ToList();
                    if (!PillIds.Contains(id)) parts.Add(PillPartOf(id, true));
                    previews.Add(new PillPreview(parts));
                    break;
                }
            case "activities" when ActivityCatalog.Find(id) is { } activity:
                {
                    var (text, progress) = ActivitySample(id);
                    var part = new PillPart("activity", text, activity.Glyph, null, progress, id switch { "clean" or "power" => "warm", "pressure" => "danger", _ => "" }, true);
                    var parts = PillIds.Select(p => PillPartOf(p)).ToList();
                    if (activity.Left) parts.Insert(0, part); else parts.Add(part);
                    previews.Add(new PillPreview(parts));
                    break;
                }
            case "favorites":
                {
                    var current = FavoriteIds;
                    var parts = current.Select(f => new DockPart(FavoriteCatalog.Find(f)?.Glyph ?? "", f == id, FavoriteIcon(f))).ToList();
                    if (!current.Contains(id)) parts.Add(new DockPart(FavoriteCatalog.Find(id)?.Glyph ?? "", true, FavoriteIcon(id)));
                    parts.Add(new DockPart("\uE718", false));   // fijar el notch abierto
                    previews.Add(new DockPreview(parts));
                    break;
                }
            case "dock":
                {
                    var pages = DockIds;
                    var parts = new List<DockPart> { new("\uE80F", false) };
                    foreach (var page in DockCatalog.Optional)
                        if (pages.Contains(page.Id) || page.Id == id) parts.Add(new DockPart(page.Glyph, page.Id == id));
                    parts.Add(new DockPart("\uE8A9", false));
                    parts.Add(new DockPart("\uE713", false));
                    previews.Add(new DockPreview(parts));
                    break;
                }
        }

        return new OptionSheet(title, source.Glyph(id), detail, on, canToggle, toggle, switchName, tr("Activado"),
            tr("Vista previa"), previews, tr("Tamaño"), sizes, sizeHint, tr("Posición"), up, down, upHelp, downHelp, tr("Cerrar"), CloseSheet);
    }

    private bool peekVisible;
    /// El notch enseña la vista corta: sus widgets (el de sistema, p. ej.) empiezan a leer datos.
    public void SetPeekVisible(bool visible)
    {
        peekVisible = visible;
        UpdateProcessSampling();
        if (visible) Notify();   // conserva la última página que se eligió, incluso al cerrar y reabrir por hover
    }

    /// Lo que hace cada favorito del dock. Ninguno fija el notch.
    public void RunFavorite(string id)
    {
        switch (id)
        {
            case "power": TogglePower.Execute(null); break;
            case "clean": CleanNow.Execute(null); break;
            case "free": FreeModel.Execute(null); break;
            case "processes": OpenProcesses.Execute(null); break;
            case "awake": Hub.Awake.Toggle.Execute(null); break;
            case "pomodoro": Hub.Pomodoro.Toggle.Execute(null); break;
            case "teamcancel": Hub.Team.Cancel.Execute(null); break;
            case var app when FavoriteCatalog.IsApp(app):
                try { Process.Start(new ProcessStartInfo(FavoriteCatalog.AppPath(app)) { UseShellExecute = true }); }
                catch (Exception e) { AppLog.Write($"favorito: {e.Message}"); }
                break;
        }
    }

    /// El nombre accesible (y la ayuda) de cada favorito: el de las acciones que cambian con el estado sale del modelo.
    public static string FavoriteNameProperty(string id) => id switch
    {
        "power" => nameof(HeroHelp),
        "clean" => nameof(CleanLabel),
        "free" => nameof(FreeHelp),
        "processes" => nameof(NotchTabProcesses),
        "awake" => nameof(AwakeHelp),
        "pomodoro" => nameof(PomodoroHelp),
        "teamcancel" => nameof(TeamCancelHelp),
        _ => nameof(NotchTabHome),
    };
}
