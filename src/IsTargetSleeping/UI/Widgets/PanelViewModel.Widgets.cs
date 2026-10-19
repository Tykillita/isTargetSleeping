using System.Diagnostics;
using System.Windows.Input;
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
    public string ResizeHelp => tr("Cambiar el ancho de %@", SwitchName);
}

/// Una acción de la tira de favoritos del dock.
public sealed record FavoriteRow(string Title, string Glyph, bool IsOn, bool CanToggle, ICommand Toggle, bool ShowDivider);

/// Los widgets del Home, la página para elegirlos y los favoritos configurables.
public sealed partial class PanelViewModel
{
    public WidgetHub Hub { get; private set; } = null!;
    public List<WidgetSlot> HomeWidgets { get; private set; } = [];
    public List<WidgetSlot> PeekWidgets { get; private set; } = [];
    public List<WidgetRow> WidgetRows { get; private set; } = [];
    public List<SwitchRow> SystemMetricRows { get; private set; } = [];
    public string SystemMetricsTitle => tr("Gráficas de Sistema");
    public string SystemMetricsDetail => tr("Elige dos métricas para el widget compacto; al ensancharlo aparecen las seis.");
    public List<WidgetRow> PeekRows { get; private set; } = [];
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

    public List<string> WidgetIds => WidgetLayout.Normalize(widgetDraft ?? Prefs.StoredList(PrefKeys.NotchWidgets));
    public List<string> PeekIds => WidgetLayout.Normalize(Prefs.StoredList(PrefKeys.NotchPeekWidgets), WidgetCatalog.DefaultPeek);
    public List<string> PillIds => PillCatalog.Normalize(Prefs.StoredList(PrefKeys.NotchPill));
    public List<string> ActivityIds => ActivityCatalog.Normalize(Prefs.StoredList(PrefKeys.NotchActivities));
    public List<string> FavoriteIds => FavoriteCatalog.Normalize(Prefs.StoredList(PrefKeys.NotchFavorites));

    public string WidgetsEmpty => tr("No hay widgets en el Home. Actívalos en la página Widgets.");
    public string CustomizeWidgetsLabel => tr("Personalizar widgets");
    public string SaveWidgetsLabel => tr("Guardar");
    public string CancelWidgetsLabel => tr("Descartar");
    public bool NoHomeWidgets => HomeWidgets.Count == 0;
    public string WidgetsIntro => tr("Elige qué ve el Home del notch y en qué orden. Caben %d celdas.", WidgetCatalog.MaxCells);
    public string WidgetsCells => tr("%d de %d celdas en uso", WidgetLayout.Cells(WidgetIds), WidgetCatalog.MaxCells);
    public string WidgetsFull => tr("Quita otro widget para hacer sitio.");
    public string WidgetShelfIntro => tr("Arrastra aquí para añadir; arrastra los widgets del Home para reordenarlos.");
    public bool WidgetsAreFull => WidgetLayout.Cells(WidgetIds) >= WidgetCatalog.MaxCells;
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
        "clock" => Hub.ClockInfo.Time,
        "battery" => Hub.BatteryInfo.HasBattery ? Hub.BatteryInfo.Label : "—",
        "pomodoro" => Hub.Pomodoro.Remaining,
        // Vacío mientras no suena nada: la pastilla esconde entonces ese dato.
        "media" => Hub.Media.IsPlaying ? MediaPillText(Hub.Media.MediaTitle, Hub.Media.Artist) : "",
        _ => "",
    };

    public string ActivitiesTitle => tr("Actividades en vivo");
    public string ActivitiesIntro => tr("Lo que pasa ahora, a los lados del notch cerrado: lo de Ollama a la izquierda y lo demás a la derecha. Caben hasta %d a la vez.", ActivityCatalog.MaxLeft + ActivityCatalog.MaxRight);

    /// Lo que está pasando ahora y el usuario deja ver: la pastilla cerrada lo enseña a su lado.
    public List<Activity> LiveActivities
    {
        get
        {
            var now = new List<Activity>();
            void Add(string id, string text, string? glyph = null, double progress = -1) => now.Add(new Activity(id, glyph ?? ActivityCatalog.Find(id)!.Glyph, text, ActivityCatalog.Find(id)!.Priority, ActivityCatalog.Find(id)!.Left, progress));
            if (Hub.Volume.Recent) Add("volume", Hub.Volume.Text, Hub.Volume.Muted ? "\uE74F" : null);
            if (IsCleaning) Add("clean", tr("Liberando…"));
            if (P is Power.Starting or Power.Stopping) Add("power", P == Power.Starting ? tr("Encendiendo…") : tr("Apagando…"));
            if (Mem.Pressure == SystemMemory.Level.Critical) Add("pressure", tr("RAM crítica"));
            if (Hub.Pomodoro.Running) Add("pomodoro", Hub.Pomodoro.Remaining);
            if (Hub.Awake.On) Add("awake", "");
            if (Hub.Huds.Brightness is { } brightness) Add("brightness", tr("Brillo %@", brightness));
            if (Hub.Huds.LocksText is { } locks) Add("locks", locks);
            if (Hub.Huds.Bluetooth is { } bluetooth) Add("bluetooth", bluetooth);
            var agent = Hub.Agents.Sessions.FirstOrDefault(s => s.State is AgentState.Working or AgentState.NeedsAttention);
            if (agent is not null) Add("agents", agent.State == AgentState.NeedsAttention ? tr("Agente necesita atención") : tr("Agente trabajando"));
            if (Hub.ClockInfo.HasCurrentEvent) Add("meeting", $"{Hub.ClockInfo.CurrentEventTitle} · {Hub.ClockInfo.CurrentEventRemaining}", progress: Hub.ClockInfo.CurrentEventProgress);
            // Si la música es un dato de la pastilla, no se repite como actividad.
            if (Hub.Media.IsPlaying && !PillIds.Contains("media")) Add("media", MediaPillText(Hub.Media.MediaTitle, Hub.Media.Artist));
            var (left, right) = ActivityQueue.PickSides(now, ActivityIds);
            return [.. left, .. right];
        }
    }

    /// Lo que enseña la pastilla de la canción que suena: «título · artista» (o solo el título si no hay artista).
    public static string MediaPillText(string title, string artist) => artist.Length > 0 ? $"{title} · {artist}" : title;

    public string FavoritesTitle => tr("Favoritos");
    public string FavoritesIntro => tr("Hasta %d acciones de un toque junto a la navegación del dock.", FavoriteCatalog.Max);
    public string NotchTabOllama => tr("Ollama");
    public string NotchTabWidgets => tr("Widgets");
    public string AwakeHelp => Hub.Awake.ToggleHelp;
    public string PomodoroHelp => Hub.Pomodoro.ToggleHelp;

    private void InitWidgets()
    {
        BeginCustomize = new Command(BeginCustomizeWidgets);
        SaveCustomize = new Command(SaveCustomizeWidgets);
        CancelCustomize = new Command(CancelCustomizeWidgets);
        Hub = new WidgetHub(this);
        // Los nombres del dock («Pausar el temporizador», «Dejar que Windows suspenda») cambian solo cuando cambia el estado.
        Hub.Awake.PropertyChanged += (_, _) => Notify();
        Hub.Pomodoro.StateChanged += Notify;
        Hub.Media.PropertyChanged += (_, _) => Notify();
        Hub.Volume.Changed += Notify;
    }

    public void BeginCustomizeWidgets() { if (widgetDraft is null) { widgetDraft = WidgetIds; Notify(); } }
    public void CancelCustomizeWidgets() { if (widgetDraft is not null) { widgetDraft = null; Notify(); } }
    public void SaveCustomizeWidgets()
    {
        if (widgetDraft is null) return;
        var saved = WidgetLayout.Normalize(widgetDraft);
        widgetDraft = null;
        Prefs.SetList(PrefKeys.NotchWidgets, saved);
    }
    public void MoveWidgetBefore(string source, string target)
    {
        if (widgetDraft is null) return;
        if (!widgetDraft.Any(x => WidgetLayout.BaseId(x) == target))
        {
            if (!widgetDraft.Any(x => WidgetLayout.BaseId(x) == WidgetLayout.BaseId(source)) && WidgetLayout.CanAdd(widgetDraft, source))
                widgetDraft = WidgetLayout.Add(widgetDraft, source);
            Notify();
            return;
        }
        widgetDraft = WidgetLayout.MoveBefore(widgetDraft, source, target);
        Notify();
    }
    public void AddWidgetToDraft(string id)
    {
        if (widgetDraft is null || !WidgetLayout.CanAdd(widgetDraft, id)) return;
        widgetDraft = WidgetLayout.Add(widgetDraft, id);
        Notify();
    }
    public void ResizeWidgetInDraft(string id)
    {
        if (widgetDraft is null) return;
        var resized = WidgetLayout.ToggleWidth(widgetDraft, id);
        if (resized.SequenceEqual(widgetDraft)) return;
        widgetDraft = resized;
        Notify();
    }
    private void StoreWidgetLayout(string key, List<string> values)
    {
        if (key == PrefKeys.NotchWidgets && widgetDraft is not null) { widgetDraft = values; Notify(); }
        else Prefs.SetList(key, values);
    }

    private void RebuildWidgets()
    {
        var ids = WidgetIds;
        var peek = PeekIds;
        var pill = PillIds;
        var favorites = FavoriteIds;
        var activities = ActivityIds;
        var dockPages = DockIds;
        var signature = $"{string.Join(",", ids)}#{string.Join(",", peek)}#{string.Join(",", pill)}#{string.Join(",", favorites)}#{string.Join(",", activities)}#{string.Join(",", dockPages)}#{string.Join(",", SystemMetricCatalog.Normalize(Prefs.List(PrefKeys.SystemMetrics)))}#{Prefs.Switch(PrefKeys.AgentsEnabled, false)}#{Prefs.Switch(PrefKeys.WeatherEnabled, false)}#{Prefs.Switch(PrefKeys.OcrEnabled, false)}#{Effective}#{CustomizingWidgets}";
        if (signature == widgetsSignature) return;
        widgetsSignature = signature;

        HomeWidgets = Hub.Slots(ids, peek: false, customizing: CustomizingWidgets, resize: ResizeWidgetInDraft);
        PeekWidgets = Hub.Slots(peek, peek: true);
        WidgetRows = BuildRows(ids, PrefKeys.NotchWidgets, null);
        var systemMetrics = SystemMetricCatalog.Normalize(Prefs.List(PrefKeys.SystemMetrics));
        SystemMetricRows = SystemMetricCatalog.All.Select((id, i) => new SwitchRow(id switch
        {
            "cpu" => tr("CPU"), "ram" => "RAM", "gpu" => tr("GPU"), "disk" => tr("Disco"),
            "battery" => tr("Batería"), _ => tr("Red"),
        }, null, systemMetrics.Contains(id),
            new Command(() => Prefs.SetList(PrefKeys.SystemMetrics, SystemMetricCatalog.Toggle(Prefs.List(PrefKeys.SystemMetrics), id))), i > 0)).ToList();
        PeekRows = BuildRows(peek, PrefKeys.NotchPeekWidgets, PeekScope);
        Hub.SetPillNeeds(PillCatalog.WidgetsNeeded(pill));
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
                new Command(() => Prefs.SetList(PrefKeys.NotchFavorites, FavoriteCatalog.Toggle(favorites, id))), i > 0);
        }).ToList();
        CanAddFavoriteApp = favorites.Count < FavoriteCatalog.Max;
        AddFavoriteApp = new Command(() =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = AddAppLabel, Filter = tr("Apps y accesos directos") + "|*.exe;*.lnk|*.*|*.*" };
            if (dialog.ShowDialog() == true) Prefs.SetList(PrefKeys.NotchFavorites, FavoriteCatalog.Toggle(favorites, FavoriteCatalog.AppId(dialog.FileName)));
        });
        UpdateProcessSampling();
    }

    private List<WidgetRow> BuildRows(List<string> ids, string key, string? scope) =>
        WidgetCatalog.All.Select((info, i) =>
        {
            var id = info.Id;
            var token = ids.FirstOrDefault(x => WidgetLayout.BaseId(x) == id);
            var on = token is not null;
            return new WidgetRow(info.Title, info.Glyph,
                info.Cells == 2 ? tr("2 celdas") : tr("%@ celda", WidgetLayout.Span(token ?? id)),
                on, on || WidgetLayout.CanAdd(ids, id),
                new Command(() => StoreWidgetLayout(key, on ? WidgetLayout.Remove(ids, id) : WidgetLayout.Add(ids, id))),
                new Command(() => StoreWidgetLayout(key, WidgetLayout.Move(ids, id, -1))),
                new Command(() => StoreWidgetLayout(key, WidgetLayout.Move(ids, id, +1))),
                i > 0, scope, new Command(() => StoreWidgetLayout(key, WidgetLayout.ToggleWidth(ids, id))), on && WidgetLayout.CanResize(id),
                id, new ParamCommand(v => { if (v is string source) MoveWidgetBefore(source, id); }), key == PrefKeys.NotchWidgets && widgetDraft is not null);
        }).ToList();

    private bool peekVisible;
    /// El notch enseña la vista corta: sus widgets (el de sistema, p. ej.) empiezan a leer datos.
    public void SetPeekVisible(bool visible)
    {
        peekVisible = visible;
        UpdateProcessSampling();
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
        _ => nameof(NotchTabHome),
    };
}
