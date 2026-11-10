using System.Windows.Input;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// Un punto del paginador de la galería: el widget que enseña.
public sealed record GalleryDot(string Name, bool Selected, ICommand Select);

/// Un tamaño que se puede elegir en la galería («1×1», «2×1»…).
public sealed record GallerySizeChip(string Label, bool Selected, ICommand Select);

/// Vista lateral decorativa del carrusel: conserva la plantilla del widget, pero no activa sus datos ni controles.
public sealed record GalleryPreviewCard(WidgetSlot Slot, double Width, double Height, double Left, double Top);

/// La galería de widgets, como la de iOS: un widget cada vez, con su nombre y para qué sirve, la vista previa viva en el tamaño elegido, el paginador
/// y un botón para añadirlo al Home con ese tamaño.
public sealed partial class PanelViewModel
{
    private string galleryId = "clock";
    private int gallerySize;
    private WidgetSlot? galleryPreview;
    private string gallerySignature = "";
    private string galleryCardsSignature = "";
    private GalleryPreviewCard? galleryPreviousCard, galleryNextCard;

    public event Action<string, int>? GallerySelectionRequested;

    /// Lo que enseña la galería de cada widget: si ya tiene información de verdad (una canción, la batería, notas escritas, el tiempo…), esa; mientras no,
    /// datos de muestra en su propia instancia (nunca se mezclan con los de verdad). Se mira cada vez que se elige otro widget o tamaño.
    private readonly Dictionary<string, WidgetModel> galleryDemos = [];
    /// Pide los datos de verdad al elegir un widget: la música, la batería y el audio se leen solos; el clima, si está activado.
    private void GalleryRefreshData(string id)
    {
        // El widget elegido lee sus datos mientras se mira (el sistema, el uso de IA, los modelos…): así los de verdad aparecen solos.
        if (previewWidget != id) System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { previewWidget = id; UpdateProcessSampling(); });
        switch (id)
        {
            case "media": Hub.Media.Start(); break;
            case "battery": Hub.BatteryInfo.Tick(DateTime.Now); break;
            case "audio": Hub.Audio.Sessions.Refresh(); break;
            case "weather": Hub.Weather.Tick(DateTime.Now); break;
            case "system": Hub.SystemStats.Tick(DateTime.Now); break;
            case "usage": Hub.Usage.Tick(DateTime.Now); break;
            case "lmstudio": Hub.LmStudio.Tick(DateTime.Now); break;
            case "launcher": Hub.Launcher.Tick(DateTime.Now); break;
        }
    }

    private readonly HashSet<string> galleryDemoShown = [];

    /// ¿Tiene ya información de verdad que enseñar?
    private bool GalleryHasReal(string id) => id switch
    {
        "media" => Hub.Media.HasSession,
        "battery" => Hub.BatteryInfo.HasDevices,
        "audio" => Hub.Audio.HasSessions,
        "agents" => Hub.Agents.Enabled && Hub.Agents.ActiveSessionCount > 0,   // las recientes solas dejan la vista «activos» con su aviso de vacío, cortado en una celda
        // «De verdad» es que tenga algo que enseñar: una carpeta sin archivo abierto, un servidor apagado o una tarea ya terminada se ven vacíos.
        "usage" => Hub.Usage.Rows.Any(r => r.HasLimits),
        "code" => Hub.Code.HasRoot && Hub.Code.HasTabs,
        "team" => Hub.Team.Busy || Hub.Team.Ready,
        "lmstudio" => Hub.LmStudio.HasContent,
        "launcher" => Hub.Launcher.HasModels,
        "notes" => Hub.Notes.HasContent,
        "todo" => Hub.Todo.HasContent,
        "shelf" => Hub.Shelf.HasContent,
        "teleprompter" => !string.IsNullOrWhiteSpace(Hub.Teleprompter.Text),
        "weather" => Hub.Weather.HasForecast,
        "led" => Hub.Leds.HasDevices,
        _ => true,
    };

    /// Si la galería está enseñando datos de muestra de un widget y llegan los de verdad (la música empieza a sonar, se leen las baterías…), se cambia a ellos.
    private void HookGallery()
    {
        Hub.ModelCreated += widget =>
        {
            if (widget.Id is not ("media" or "battery" or "audio" or "weather" or "agents" or "usage" or "code" or "team" or "lmstudio" or "launcher")) return;
            widget.PropertyChanged += (_, _) =>
            {
                if (galleryId == widget.Id && galleryDemoShown.Contains(widget.Id) && GalleryHasReal(widget.Id))
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(Notify);
            };
        };
    }

    private WidgetModel? GalleryModelFor(string id)
    {
        var real = GalleryHasReal(id);
        if (real) galleryDemoShown.Remove(id); else galleryDemoShown.Add(id);
        if (real) return Hub.Find(id);
        var key = $"{id}/{L10n.Effective}";
        if (galleryDemos.TryGetValue(key, out var cached)) return cached;
        WidgetModel? demo = null;
        var demoPrefs = new Prefs();
        demoPrefs.LoadDemo();
        demoPrefs.SetSwitch(PrefKeys.AgentsEnabled, true);
        demoPrefs.SetSwitch(PrefKeys.WeatherEnabled, true);
        switch (id)
        {
            case "media": var media = new MediaWidget(); media.LoadIdlePreview(); demo = media; break;
            case "battery": var battery = new BatteryWidget(); battery.LoadDemo(); demo = battery; break;
            case "audio": var audio = new AudioWidget(demoPrefs); audio.LoadDemo(); demo = audio; break;
            case "agents": var agents = new AgentsWidget(demoPrefs); agents.LoadDemo(); demo = agents; break;
            case "usage": var usage = new UsageWidget(demoPrefs); usage.LoadDemo(); demo = usage; break;
            case "lmstudio": var lm = new LmStudioWidget(this); lm.LoadDemo(); demo = lm; break;
            case "launcher": var launcher = new LauncherWidget(this); launcher.LoadDemo(); demo = launcher; break;
            case "code": demo = DemoCode(demoPrefs); break;
            case "team":
                var team = new TeamWidget(demoPrefs, demo: true, new TeamService(new TeamStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "its-team-demo"))), new AgentsWidget(demoPrefs));
                team.LoadDemo("running"); demo = team; break;
            case "led": demo = LedWidget.Demo(); break;
            case "weather": var weather = new WeatherWidget(demoPrefs); weather.LoadDemo(L10n.Effective != "es"); demo = weather; break;
            case "todo": var todo = new TodoWidget(false); todo.LoadDemo(); demo = todo; break;
            case "shelf": var shelf = new ShelfWidget(false, demoPrefs); shelf.LoadDemo(); demo = shelf; break;
            case "notes": var notes = new NotesWidget(false); notes.LoadDemo(); demo = notes; break;
            case "teleprompter":
                demo = new TeleprompterWidget(demoPrefs, false) { Text = tr("Hola, bienvenidos. Hoy voy a enseñaros cómo el notch reparte una tarea entre Claude y Codex, con un plan claro y sin perder el hilo de lo que pasa en cada paso. Empecemos.") };
                break;
        }
        if (demo is not null) galleryDemos[key] = demo;
        return demo ?? Hub.Find(id);
    }

    /// Un proyecto de ejemplo en la carpeta temporal para que el widget Código enseñe su árbol y un archivo abierto mientras no haya una carpeta de verdad.
    private static CodeWidget DemoCode(Prefs demoPrefs)
    {
        bool en = L10n.Effective != "es";
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"its-gallery-code-{(en ? "en" : "es")}");
        var files = new Dictionary<string, string>
        {
            ["README.md"] = en ? "# My project\n\nA small sample app.\n" : "# Mi proyecto\n\nUna app pequeña de ejemplo.\n",
            ["Demo.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0-windows</TargetFramework><UseWPF>true</UseWPF></PropertyGroup></Project>\n",
            ["App.xaml"] = "<Application />\n",
            [@"src\Program.cs"] = $"using System;\n\nnamespace Demo;\n\npublic static class Program\n{{\n    public static void Main()\n    {{\n        Console.WriteLine(\"{(en ? "Hello" : "Hola")}, notch\");\n    }}\n}}\n",
            [@"src\Notch.cs"] = "namespace Demo;\n\npublic sealed class Notch\n{\n    public bool Open { get; set; }\n}\n",
            [@"docs\notas.txt"] = "Ideas para el viernes.\n",
        };
        try
        {
            foreach (var (name, text) in files)
            {
                var path = System.IO.Path.Combine(dir, name);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                if (!System.IO.File.Exists(path)) System.IO.File.WriteAllText(path, text);
            }
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { }
        demoPrefs.SetList(PrefKeys.CodeRoot, [dir]);
        var code = new CodeWidget(demoPrefs);
        code.OpenFile(System.IO.Path.Combine(dir, @"src\Program.cs"));
        return code;
    }

    private List<WidgetInfo> GalleryItems => WidgetCatalog.All.ToList();
    private WidgetInfo GalleryInfo => GalleryItems.FirstOrDefault(w => w.Id == galleryId) ?? GalleryItems.FirstOrDefault() ?? WidgetCatalog.All[0];
    private IReadOnlyList<(int Cols, int Rows)> GallerySizesOf => WidgetLayout.Sizes(GalleryInfo.Id);
    private (int Cols, int Rows) GalleryChoice => GallerySizesOf[Math.Clamp(gallerySize, 0, GallerySizesOf.Count - 1)];

    public string GalleryTitle => GalleryInfo.Title;
    public string GalleryDescription => GalleryInfo.Id switch
    {
        "ollama" => tr("Enciende Ollama y consulta modelo y memoria."),
        "system" => tr("Consulta CPU, RAM, GPU y disco en vivo."),
        "clock" => tr("Consulta la hora, eventos y calendario."),
        "pomodoro" => tr("Programa sesiones de enfoque y descanso."),
        "awake" => tr("Evita la suspensión mientras trabajas."),
        "notes" => tr("Anota ideas y consúltalas desde el Home."),
        "todo" => tr("Lista de tareas con contador diario y repetición."),
        "shelf" => tr("Bandeja temporal de archivos: suéltalos, arrástralos fuera o ábrelos."),
        "battery" => tr("Mira la batería del PC y accesorios Bluetooth."),
        "code" => tr("Explora proyectos, código y tecnologías."),
        "media" => tr("Controla la reproducción y su portada."),
        "audio" => tr("Ajusta el volumen y el audio por dispositivo."),
        "agents" => tr("Revisa tus sesiones de Claude Code y Codex."),
        "lmstudio" => tr("Controla LM Studio y sus modelos locales."),
        "launcher" => tr("Inicia modelos locales de Ollama y LM Studio."),
        "usage" => tr("Cuotas de Claude, Codex, Gemini y Muse; gasto de Grok API y actividad de Ollama."),
        "weather" => tr("Tiempo actual y pronóstico local."),
        "teleprompter" => tr("Lee y desplaza tu guion mientras grabas."),
        "team" => tr("Coordina tareas entre Claude Code y Codex."),
        "led" => tr("Ajusta color, brillo y efectos LED."),
        _ => "",
    };

    /// La vista previa: el widget de verdad, con su plantilla, en el tamaño elegido.
    public WidgetSlot? GalleryPreview
    {
        get
        {
            var (cols, rows) = GalleryChoice;
            var signature = $"{GalleryInfo.Id}@{cols}x{rows}";
            if (galleryPreview is null || signature != gallerySignature)
            {
                gallerySignature = signature;
                GalleryRefreshData(GalleryInfo.Id);
                galleryPreview = GalleryModelFor(GalleryInfo.Id) is { } model ? new WidgetSlot(model, cols, cols, false, rows: rows, grid: true, gallery: true) : null;
            }
            else if (galleryPreview is not null && GalleryModelFor(GalleryInfo.Id) is { } current && !ReferenceEquals(current, galleryPreview.Model))
                galleryPreview = new WidgetSlot(current, cols, cols, false, rows: rows, grid: true, gallery: true);   // ya hay información de verdad: pasa a enseñarla
            return galleryPreview;
        }
    }
    public double GalleryWidth => GalleryChoice.Cols * GridPanel.CellWidth;
    public double GalleryHeight => GalleryChoice.Rows * GridPanel.RowHeight;
    public double GallerySidePreviewWidth => 240;
    public double GallerySideGap => 14;
    public double GallerySideInset => GallerySidePreviewWidth + GallerySideGap;
    public double GalleryCarouselCenterLeft => GallerySideInset;
    public double GalleryCarouselWidth => GalleryWidth + GallerySideInset * 2;
    public bool GalleryUsesNoteAccent => GalleryInfo.Id == "notes";

    public GalleryPreviewCard? GalleryPreviousCard { get { EnsureGalleryCards(); return galleryPreviousCard; } }
    public GalleryPreviewCard? GalleryNextCard { get { EnsureGalleryCards(); return galleryNextCard; } }

    private void EnsureGalleryCards()
    {
        var items = GalleryItems;
        if (items.Count < 2) { galleryPreviousCard = galleryNextCard = null; return; }
        var at = Math.Max(0, items.FindIndex(w => w.Id == GalleryInfo.Id));
        var previous = items[(at - 1 + items.Count) % items.Count];
        var next = items[(at + 1) % items.Count];
        var previousSize = WidgetLayout.Sizes(previous.Id).OrderBy(s => s.Cols * s.Rows).ThenBy(s => s.Rows).First();
        var nextSize = WidgetLayout.Sizes(next.Id).OrderBy(s => s.Cols * s.Rows).ThenBy(s => s.Rows).First();
        var signature = $"{GalleryInfo.Id}@{GalleryChoice.Cols}x{GalleryChoice.Rows}/{WideLayout}/{L10n.Effective}/{previous.Id}:{GalleryHasReal(previous.Id)}/{next.Id}:{GalleryHasReal(next.Id)}";
        if (signature == galleryCardsSignature) return;
        galleryCardsSignature = signature;
        galleryPreviousCard = MakeGalleryCard(previous.Id, previousSize, true);
        galleryNextCard = MakeGalleryCard(next.Id, nextSize, false);
    }

    private GalleryPreviewCard? MakeGalleryCard(string id, (int Cols, int Rows) size, bool previous)
    {
        if (GalleryModelFor(id) is not { } model) return null;
        var slot = new WidgetSlot(model, size.Cols, size.Cols, false, rows: size.Rows, grid: true, gallery: true);
        double scale = Math.Min(1, GallerySidePreviewWidth / slot.CardWidth);
        var width = slot.CardWidth * scale;
        var height = size.Rows * GridPanel.RowHeight * scale;
        var left = previous ? GallerySideInset - GallerySideGap - width : GallerySideInset + GalleryWidth + GallerySideGap;
        return new(slot, width, height, left, Math.Max(0, (GalleryHeight - height) / 2));
    }

    public List<GalleryDot> GalleryDots => GalleryItems.Select(w => new GalleryDot(w.Title, w.Id == GalleryInfo.Id, new Command(() =>
    {
        var items = GalleryItems;
        RequestGallerySelection(w.Id, items.FindIndex(item => item.Id == w.Id) < items.FindIndex(item => item.Id == GalleryInfo.Id) ? -1 : 1);
    }))).ToList();
    public List<GallerySizeChip> GallerySizes => GallerySizesOf
        .Select((s, i) => new GallerySizeChip($"{s.Cols}×{s.Rows}", i == Math.Clamp(gallerySize, 0, GallerySizesOf.Count - 1), new Command(() => { gallerySize = i; Notify(); }))).ToList();

    public ICommand GalleryPrev => new Command(() => StepGallery(-1));
    public ICommand GalleryNext => new Command(() => StepGallery(1));
    public string GalleryPrevHelp => tr("Widget anterior");
    public string GalleryNextHelp => tr("Widget siguiente");
    public string GalleryTitleLabel => tr("Galería de widgets");
    public string GalleryIntro => tr("Mira cada widget en cada tamaño antes de añadirlo al Home o como página.");
    public string GallerySizeLabel => tr("Tamaño");

    private List<List<string>> GalleryPages => PanelViewModel.HomePages(WidgetIds);
    private bool GalleryWidgetIsHome => GalleryPages.FirstOrDefault()?.Any(x => WidgetLayout.BaseId(x) == GalleryInfo.Id) == true;
    private bool GalleryWidgetIsAdded => WidgetIds.Any(x => WidgetLayout.BaseId(x) == GalleryInfo.Id);
    private bool GalleryAddsToHome => GalleryPages.Count <= 1 && WidgetLayout.Area(WidgetIds) + GalleryChoice.Cols * GalleryChoice.Rows <= PanelViewModel.HomePageCells;
    private bool GalleryChoiceIsHome => PanelViewModel.HomePages(GalleryResult()).FirstOrDefault()?.Any(x => WidgetLayout.BaseId(x) == GalleryInfo.Id) == true;
    private string GalleryToken => WidgetLayout.Token(GalleryInfo.Id, GalleryChoice.Cols, GalleryChoice.Rows, home: true);
    private List<string> GalleryResult()
    {
        var ids = WidgetIds;
        var index = ids.FindIndex(x => WidgetLayout.BaseId(x) == GalleryInfo.Id);
        if (index >= 0)
        {
            var next = ids.ToList();
            next[index] = GalleryToken;
            return WidgetLayout.Area(next) <= WidgetCatalog.MaxHomeCells ? next : ids;
        }
        return WidgetLayout.CanAdd(ids, GalleryToken, WidgetCatalog.MaxHomeCells, home: true) ? [.. ids, GalleryToken] : ids;
    }
    // Si ya está guardado con este tamaño, el botón lo quita (en lugar de quedar apagado).
    private bool GalleryHereAlready => WidgetIds.Contains(GalleryToken);
    public bool GalleryCanAdd => GalleryHereAlready || !GalleryResult().SequenceEqual(WidgetIds);
    public bool GalleryAlreadyAdded => GalleryHereAlready;
    public string GalleryAddLabel => GalleryHereAlready ? (GalleryWidgetIsHome ? tr("Quitar del Home") : tr("Quitar de su página"))
        : GalleryCanAdd ? GalleryWidgetIsAdded
            ? (GalleryChoiceIsHome ? tr("Usar este tamaño en el Home") : tr("Usar este tamaño en la página"))
            : GalleryAddsToHome ? tr("Añadir al Home") : tr("Añadir como página")
        : tr("No cabe: quita otro widget");
    public ICommand GalleryAdd => new Command(() =>
    {
        if (GalleryHereAlready) { StoreWidgetLayout(PrefKeys.NotchWidgets, WidgetIds.Where(x => x != GalleryToken).ToList()); return; }
        var next = GalleryResult();
        if (!next.SequenceEqual(WidgetIds)) StoreWidgetLayout(PrefKeys.NotchWidgets, next);
    });

    public void CommitGallerySelection(string id) => PickGallery(id);
    internal void CommitGallerySelection(string id, WidgetSlot? preview) => PickGallery(id, preview);
    private void RequestGallerySelection(string id, int direction)
    {
        if (GalleryInfo.Id == id) return;
        if (GallerySelectionRequested is { } requested) requested(id, direction);
        else PickGallery(id);
    }
    private void PickGallery(string id, WidgetSlot? preview = null)
    {
        galleryId = id;
        gallerySize = 0;
        var size = GalleryChoice;
        bool reusePreview = preview is { } slot && slot.Id == id && slot.Span == size.Cols && slot.Rows == size.Rows;
        galleryPreview = reusePreview ? preview : null;
        gallerySignature = reusePreview ? $"{id}@{size.Cols}x{size.Rows}" : "";
        Notify();
    }
    private void StepGallery(int delta)
    {
        var items = GalleryItems;
        var at = Math.Max(0, items.FindIndex(w => w.Id == GalleryInfo.Id));
        RequestGallerySelection(items[(at + delta + items.Count) % items.Count].Id, delta);
    }
}
