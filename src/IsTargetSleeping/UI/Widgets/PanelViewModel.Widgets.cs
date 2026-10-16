using System.Windows.Input;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// Una fila de la página Widgets: activar o quitar uno y subirlo o bajarlo en el Home.
public sealed record WidgetRow(string Title, string Glyph, string Detail, bool IsOn, bool CanToggle,
    ICommand Toggle, ICommand Up, ICommand Down, bool ShowDivider, string? Scope = null)
{
    /// El mismo widget sale en la lista del Home y en la de la vista corta: el nombre accesible dice de cuál es.
    public string SwitchName => Scope is null ? Title : $"{Title} · {Scope}";
    public string UpHelp => tr("Subir %@", SwitchName);
    public string DownHelp => tr("Bajar %@", SwitchName);
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
    public List<WidgetRow> PeekRows { get; private set; } = [];
    public List<WidgetRow> PillRows { get; private set; } = [];
    public List<FavoriteRow> ActivityRows { get; private set; } = [];
    public List<FavoriteRow> FavoriteRows { get; private set; } = [];
    private string widgetsSignature = "";

    public bool ShowingHome => view == PanelView.Home;
    public bool ShowingWidgets => view == PanelView.Widgets;
    /// La vista que lleva el logo y la píldora de estado en la cabecera.
    public bool ShowingBrand => view is PanelView.Main or PanelView.Home;
    /// A donde vuelve la «X» de Actividad, Ajustes y Widgets: el Home en el notch, el panel de siempre en la bandeja.
    private PanelView BackView => WideLayout ? PanelView.Home : PanelView.Main;

    public List<string> WidgetIds => WidgetLayout.Normalize(Prefs.StoredList(PrefKeys.NotchWidgets));
    public List<string> PeekIds => WidgetLayout.Normalize(Prefs.StoredList(PrefKeys.NotchPeekWidgets), WidgetCatalog.DefaultPeek);
    public List<string> PillIds => PillCatalog.Normalize(Prefs.StoredList(PrefKeys.NotchPill));
    public List<string> ActivityIds => ActivityCatalog.Normalize(Prefs.StoredList(PrefKeys.NotchActivities));
    public List<string> FavoriteIds => FavoriteCatalog.Normalize(Prefs.StoredList(PrefKeys.NotchFavorites));

    public string WidgetsEmpty => tr("No hay widgets en el Home. Actívalos en la página Widgets.");
    public bool NoHomeWidgets => HomeWidgets.Count == 0;
    public string WidgetsIntro => tr("Elige qué ve el Home del notch y en qué orden. Caben %d celdas.", WidgetCatalog.MaxCells);
    public string WidgetsCells => tr("%d de %d celdas en uso", WidgetLayout.Cells(WidgetIds), WidgetCatalog.MaxCells);
    public string WidgetsFull => tr("Quita otro widget para hacer sitio.");
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
        _ => "",
    };

    public string ActivitiesTitle => tr("Actividades en vivo");
    public string ActivitiesIntro => tr("Lo que pasa ahora, junto a los datos de la pastilla cerrada. Caben %d a la vez.", ActivityCatalog.Max);

    /// Lo que está pasando ahora y el usuario deja ver: la pastilla cerrada lo enseña a su lado.
    public List<Activity> LiveActivities
    {
        get
        {
            var now = new List<Activity>();
            void Add(string id, string text, string? glyph = null) => now.Add(new Activity(id, glyph ?? ActivityCatalog.Find(id)!.Glyph, text, ActivityCatalog.Find(id)!.Priority));
            if (Hub.Volume.Recent) Add("volume", Hub.Volume.Text, Hub.Volume.Muted ? "\uE74F" : null);
            if (IsCleaning) Add("clean", tr("Liberando…"));
            if (P is Power.Starting or Power.Stopping) Add("power", P == Power.Starting ? tr("Encendiendo…") : tr("Apagando…"));
            if (Mem.Pressure == SystemMemory.Level.Critical) Add("pressure", tr("RAM crítica"));
            if (Hub.Pomodoro.Running) Add("pomodoro", Hub.Pomodoro.Remaining);
            if (Hub.Awake.On) Add("awake", "");
            if (Hub.Media.IsPlaying) Add("media", Hub.Media.MediaTitle);
            return ActivityQueue.Pick(now, ActivityIds);
        }
    }

    public string FavoritesTitle => tr("Favoritos");
    public string FavoritesIntro => tr("Hasta %d acciones de un toque junto a la navegación del dock.", FavoriteCatalog.Max);
    public string NotchTabOllama => tr("Ollama");
    public string NotchTabWidgets => tr("Widgets");
    public string AwakeHelp => Hub.Awake.ToggleHelp;
    public string PomodoroHelp => Hub.Pomodoro.ToggleHelp;

    private void InitWidgets()
    {
        Hub = new WidgetHub(this);
        // Los nombres del dock («Pausar el temporizador», «Dejar que Windows suspenda») cambian solo cuando cambia el estado.
        Hub.Awake.PropertyChanged += (_, _) => Notify();
        Hub.Pomodoro.StateChanged += Notify;
        Hub.Media.PropertyChanged += (_, _) => Notify();
        Hub.Volume.Changed += Notify;
    }

    private void RebuildWidgets()
    {
        var ids = WidgetIds;
        var peek = PeekIds;
        var pill = PillIds;
        var favorites = FavoriteIds;
        var activities = ActivityIds;
        var signature = $"{string.Join(",", ids)}#{string.Join(",", peek)}#{string.Join(",", pill)}#{string.Join(",", favorites)}#{string.Join(",", activities)}#{Effective}";
        if (signature == widgetsSignature) return;
        widgetsSignature = signature;

        HomeWidgets = Hub.Slots(ids, peek: false);
        PeekWidgets = Hub.Slots(peek, peek: true);
        WidgetRows = BuildRows(ids, PrefKeys.NotchWidgets, null);
        PeekRows = BuildRows(peek, PrefKeys.NotchPeekWidgets, PeekScope);
        Hub.SetPillNeeds(PillCatalog.WidgetsNeeded(pill));
        // La música solo se lee si algo la necesita: el widget en alguna lista o su actividad permitida.
        if (ids.Contains("media") || peek.Contains("media") || activities.Contains("media")) Hub.Media.Start();
        if (activities.Contains("volume")) Hub.Volume.Start();
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
        FavoriteRows = FavoriteCatalog.All.Select((info, i) =>
        {
            var id = info.Id;
            var on = favorites.Contains(id);
            return new FavoriteRow(info.Title, info.Glyph, on, on || favorites.Count < FavoriteCatalog.Max,
                new Command(() => Prefs.SetList(PrefKeys.NotchFavorites, FavoriteCatalog.Toggle(favorites, id))), i > 0);
        }).ToList();
    }

    private List<WidgetRow> BuildRows(List<string> ids, string key, string? scope) =>
        WidgetCatalog.All.Select((info, i) =>
        {
            var id = info.Id;
            var on = ids.Contains(id);
            return new WidgetRow(info.Title, info.Glyph,
                info.Cells == 2 ? tr("2 celdas") : tr("1 celda"),
                on, on || WidgetLayout.CanAdd(ids, id),
                new Command(() => Prefs.SetList(key, on ? WidgetLayout.Remove(ids, id) : WidgetLayout.Add(ids, id))),
                new Command(() => Prefs.SetList(key, WidgetLayout.Move(ids, id, -1))),
                new Command(() => Prefs.SetList(key, WidgetLayout.Move(ids, id, +1))),
                i > 0, scope);
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
