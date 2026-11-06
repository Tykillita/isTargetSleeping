using System.Windows.Input;
using System.Windows.Media;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// Un modelo de la lista de LM Studio o del lanzador: tocarlo lo carga o lo descarga.
public sealed record LaunchRow(string Engine, string Name, string Meta, bool Loaded, bool Busy, bool CanRun, ICommand Run)
{
    public bool HasMeta => Meta.Length > 0;
    /// Reproducir para cargar, parar para descargar (Segoe Fluent).
    public string Glyph => Loaded ? "" : "";
    public string Help => Loaded ? tr("Descargar de la memoria") : tr("Cargar en memoria");
    public string AutomationName => $"{Help}: {Name}";
    public double Opacity => CanRun ? 1 : 0.45;
}

/// Un motor con sus modelos: su estado, su interruptor y sus filas.
public sealed record LaunchGroup(string Name, string State, bool IsOn, bool CanToggle, ICommand Toggle, Brush Dot, IReadOnlyList<LaunchRow> Rows, string Empty)
{
    public bool HasRows => Rows.Count > 0;
    public bool NoRows => !HasRows;
    public IReadOnlyList<LaunchRow> Top2 => Rows.Take(2).ToList();
    public IReadOnlyList<LaunchRow> Top5 => Rows.Take(5).ToList();
    public string ToggleLabel => IsOn ? tr("Apagar") : tr("Encender");
    public string ToggleHelp => IsOn ? tr("Apagar %@", Name) : tr("Encender %@", Name);
}

/// LM Studio: su servidor local, el modelo cargado y la lista de modelos descargados (con el servidor encendido).
public sealed class LmStudioWidget(PanelViewModel panel) : WidgetModel(WidgetCatalog.Find("lmstudio")!)
{
    private readonly LmStudioEngine engine = panel.Supervisor.LmStudio;
    private bool demo;
    private string signature = "";

    public PanelViewModel Panel => panel;
    public bool Installed => demo || engine.Installed;
    /// Con el servidor encendido y algo que enseñar (un modelo cargado o la lista): si no, el widget se ve vacío y la galería prefiere los datos de ejemplo.
    public bool HasContent => Installed && State == Power.On && (engine.Model is not null || engine.Models.Count > 0);
    public bool NotInstalled => !Installed;
    private Power State => demo ? Power.On : engine.Power;
    public bool IsOn => State is Power.On or Power.Starting;
    public bool CanToggle => Installed && !State.IsTransition() && !engine.IsBusy;
    public string StateTitle => !Installed ? tr("Sin LM Studio") : State switch
    {
        Power.On => tr("Encendido"), Power.Starting => tr("Arrancando…"), Power.Stopping => tr("Apagando…"), _ => tr("Apagado"),
    };
    public string ModelText => !Installed ? "" : demo ? "qwen2.5-coder-14b" : State != Power.On ? tr("Servidor apagado") : engine.Model ?? tr("Sin modelo cargado");
    public string Port => $"localhost:{engine.Port}";
    public string HeroHelp => IsOn ? tr("Apagar %@", "LM Studio") : tr("Encender %@", "LM Studio");
    public string DetailLine => demo ? $"9,4 GB · {tr("se duerme en %d min", 18)}" : engine.IdleLine;
    public string Hint => !Installed ? tr("No encuentro la herramienta lms. Instala LM Studio y abre su CLI una vez (lms bootstrap).") : "";
    public string? Error => engine.Error;
    public bool HasError => Error is not null && !demo;
    public bool CanUnload => Installed && State == Power.On && (demo || engine.Model is not null);
    public string AutoFreeTitle => tr("Se duerme solo");
    /// Lo que dice la tarjeta de la siesta: lo que falta para dormirse, o que no hay cuenta atrás.
    public string IdleNote => DetailLine.Length > 0 ? DetailLine : tr("Ningún modelo en memoria");
    public string UnloadLabel => tr("Descargar modelos");
    public string UnloadHelp => tr("Saca los modelos de la memoria sin apagar el servidor");
    public ICommand ToggleCommand => toggle ??= new Command(() => { if (!demo && CanToggle) panel.Supervisor.ToggleEngine(engine); });
    public ICommand UnloadCommand => unload ??= new Command(() => { if (!demo) _ = engine.Sleep(); });
    private ICommand? toggle, unload;
    public Brush Dot => Theme.Brush(State switch { Power.On => Theme.Current.On, Power.Starting or Power.Stopping => Theme.Current.Warm, _ => Theme.Current.Muted });
    /// Cuánto de la siesta automática ha pasado (0–1), para la barra: 0 sin cuenta atrás.
    public double IdleFraction => demo ? 0.35 : engine.IdleSeconds is { } s && engine.IdleMinutes > 0 ? Math.Clamp(s / (engine.IdleMinutes * 60.0), 0, 1) : 0;
    public bool HasIdle => demo || (State == Power.On && engine.IdleSeconds is not null && engine.IdleMinutes > 0);

    private List<LaunchRow> rows = [];
    public IReadOnlyList<LaunchRow> Rows => rows;
    public IReadOnlyList<LaunchRow> Top6 => rows.Take(6).ToList();
    public bool HasRows => rows.Count > 0;
    public bool NoRows => !HasRows;
    public string RowsTitle => tr("Modelos descargados");
    public string RowsEmpty => !Installed ? "" : State == Power.On ? tr("No hay modelos descargados.") : tr("Enciende el servidor para ver tus modelos.");

    public void LoadDemo()
    {
        demo = true;
        rows =
        [
            new("lmstudio", "qwen2.5-coder-14b", "Q4_K_M · qwen2", true, false, true, new Command(() => { })),
            new("lmstudio", "llama-3.1-8b-instruct", "Q5_K_M · llama", false, false, true, new Command(() => { })),
            new("lmstudio", "gemma-3-12b-it", "Q4_K_M · gemma3", false, false, true, new Command(() => { })),
            new("lmstudio", "mistral-7b-instruct", "Q4_0 · llama", false, false, true, new Command(() => { })),
        ];
        Raise();
    }

    public override void Tick(DateTime now)
    {
        if (demo) return;
        var next = engine.Models.Select(m => new LaunchRow("lmstudio", m.Id, m.Meta, m.Loaded, engine.IsBusy, State == Power.On && !engine.IsBusy,
            new Command(() => _ = m.Loaded ? engine.UnloadModel(m.Id) : engine.LoadModel(m.Id)))).ToList();
        var sig = $"{State}|{engine.Model}|{engine.IdleLine}|{engine.IsBusy}|{engine.Error}|{Installed}|{string.Join(",", next.Select(r => $"{r.Name}:{r.Loaded}"))}";
        rows = next;
        if (sig == signature && engine.IdleSeconds is null) return;
        signature = sig;
        Raise();
    }
}

/// Lanzador de modelos locales: Ollama y LM Studio en un sitio, con un toque para cargar (o descargar) un modelo.
public sealed class LauncherWidget(PanelViewModel panel) : WidgetModel(WidgetCatalog.Find("launcher")!)
{
    private bool demo;
    private string signature = "";
    private List<LaunchGroup> groups = [];

    public IReadOnlyList<LaunchGroup> Groups => groups;
    /// Todos los modelos juntos, los cargados primero: para los diseños de una sola lista.
    public IReadOnlyList<LaunchRow> All => groups.SelectMany(g => g.Rows).OrderByDescending(r => r.Loaded).ToList();
    public IReadOnlyList<LaunchRow> Top3 => All.Take(3).ToList();
    public IReadOnlyList<LaunchRow> Top6 => All.Take(6).ToList();
    public bool HasModels => groups.Any(g => g.HasRows);
    public bool NoModels => !HasModels;
    /// El texto cuando no hay ningún modelo que lanzar.
    public string EmptyTitle => groups.Count == 0 ? tr("Sin motores locales") : tr("Sin modelos a la vista");
    public string EmptyText => groups.Count == 0 ? tr("Instala Ollama o LM Studio y tus modelos aparecerán aquí.") : tr("Enciende un motor para ver sus modelos.");
    public int LoadedCount => groups.Sum(g => g.Rows.Count(r => r.Loaded));
    public string Summary => LoadedCount == 0 ? tr("Nada en memoria") : tr(LoadedCount == 1 ? "1 modelo en memoria" : "%d modelos en memoria", LoadedCount);
    public string LoadedName => All.FirstOrDefault(r => r.Loaded)?.Name ?? "";
    public bool HasLoaded => LoadedName.Length > 0;
    public string PowerOnTitle => tr("Encender un motor");

    public void LoadDemo()
    {
        demo = true;
        ICommand none = new Command(() => { });
        var dot = Theme.Brush(Theme.Current.On);
        groups =
        [
            new("Ollama", tr("Encendido"), true, true, none, dot,
            [
                new("ollama", "llama3.2:3b", "2,0 GB · Q4_K_M", true, false, true, none),
                new("ollama", "qwen2.5-coder:7b", "4,7 GB · Q4_K_M", false, false, true, none),
                new("ollama", "gemma3:12b", "8,1 GB · Q4_K_M", false, false, true, none),
                new("ollama", "mistral:7b", "4,1 GB · Q4_0", false, false, true, none),
            ], ""),
            new("LM Studio", tr("Encendido"), true, true, none, dot,
            [
                new("lmstudio", "qwen2.5-coder-14b", "Q4_K_M · qwen2", false, false, true, none),
                new("lmstudio", "llama-3.1-8b-instruct", "Q5_K_M · llama", false, false, true, none),
            ], ""),
        ];
        Raise();
    }

    public override void Tick(DateTime now)
    {
        if (demo) return;
        var next = new List<LaunchGroup>();
        var ollama = panel.Ollama;
        if (ollama.Power != Power.Missing)
        {
            bool on = ollama.Power == Power.On;
            var loaded = ollama.Loaded.Select(m => m.Name).ToHashSet();
            var rows = ollama.Installed.Select(m => new LaunchRow("ollama", m.Name, string.Join(" · ", new[] { m.Bytes.Gigabytes(), m.Quantization }.Where(s => !string.IsNullOrEmpty(s))),
                loaded.Contains(m.Name), ollama.BusyModel == m.Name, on && ollama.BusyModel is null,
                new Command(() => { if (loaded.Contains(m.Name)) ollama.Unload(m.Name); else ollama.Load(m.Name); })))
                .OrderByDescending(r => r.Loaded).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
            next.Add(Group("Ollama", ollama.Power, !ollama.Power.IsTransition(), new Command(() => ollama.Toggle()), on ? rows : []));
        }
        var lm = panel.Supervisor.LmStudio;
        if (lm.Installed)
        {
            bool on = lm.Power == Power.On;
            var rows = lm.Models.Select(m => new LaunchRow("lmstudio", m.Id, m.Meta, m.Loaded, lm.IsBusy, on && !lm.IsBusy,
                new Command(() => _ = m.Loaded ? lm.UnloadModel(m.Id) : lm.LoadModel(m.Id)))).ToList();
            next.Add(Group("LM Studio", lm.Power, !lm.Power.IsTransition() && !lm.IsBusy, new Command(() => panel.Supervisor.ToggleEngine(lm)), rows));
        }
        var sig = string.Join("|", next.Select(g => $"{g.Name}:{g.State}:{g.IsOn}:{string.Join(",", g.Rows.Select(r => $"{r.Name}{r.Loaded}{r.Busy}{r.CanRun}"))}"));
        groups = next;
        if (sig == signature) return;
        signature = sig;
        Raise();
    }

    private static LaunchGroup Group(string name, Power power, bool canToggle, ICommand toggle, IReadOnlyList<LaunchRow> rows) => new(name,
        power switch { Power.On => tr("Encendido"), Power.Starting => tr("Arrancando…"), Power.Stopping => tr("Apagando…"), _ => tr("Apagado") },
        power is Power.On or Power.Starting, canToggle, toggle,
        Theme.Brush(power switch { Power.On => Theme.Current.On, Power.Starting or Power.Stopping => Theme.Current.Warm, _ => Theme.Current.Muted }), rows,
        power == Power.On ? tr("No hay modelos descargados.") : tr("Enciéndelo para ver sus modelos."));
}
