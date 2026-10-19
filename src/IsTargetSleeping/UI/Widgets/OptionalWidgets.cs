using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Input;
using System.Windows;
using System.Windows.Threading;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

public sealed class AgentsWidget : WidgetModel
{
    private readonly Prefs prefs;
    private DateTime nextRead;
    private bool reading;
    private bool historyRead;
    private string? selectedSession;
    private bool showingRecent;
    private string recentSearch = "";
    private string? providerFilter, projectFilter, stateFilter;
    private bool detailsExpanded;
    private IReadOnlyList<AgentSnapshot> filteredRecentSessions = [];
    private IReadOnlyList<AgentSnapshot> visibleSessions = [];
    public ObservableCollection<AgentSnapshot> Sessions { get; } = [];
    public ObservableCollection<AgentSnapshot> RecentSessions { get; } = [];
    public IReadOnlyList<AgentSnapshot> FilteredRecentSessions => filteredRecentSessions;
    public IReadOnlyList<AgentSnapshot> VisibleSessions => visibleSessions;
    public int VisibleSessionCount => visibleSessions.Count;
    public int ActiveSessionCount => Sessions.Count;
    public int RecentSessionCount => RecentSessions.Count;
    public bool HasSessions => VisibleSessions.Any();
    public bool NoSessions => !HasSessions;
    public bool ShowingRecent => showingRecent;
    public bool ShowRecentFilters => Enabled && showingRecent;
    public bool HasRecentFilter => !string.IsNullOrWhiteSpace(recentSearch) || providerFilter is not null || projectFilter is not null || stateFilter is not null;
    public string RecentSearch { get => recentSearch; set { if (recentSearch == value) return; recentSearch = value ?? ""; FilterChanged(); } }
    public IReadOnlyList<string> ProviderOptions => FilterOptions(RecentSessions.Select(s => s.Provider));
    public IReadOnlyList<string> ProjectOptions => FilterOptions(RecentSessions.Select(s => s.ProjectName));
    public IReadOnlyList<string> StateOptions => FilterOptions(RecentSessions.Select(s => s.StateFilterLabel));
    public string SelectedProviderFilter { get => providerFilter ?? tr("Todos"); set => SetFilter(ref providerFilter, value); }
    public string SelectedProjectFilter { get => projectFilter ?? tr("Todos"); set => SetFilter(ref projectFilter, value); }
    public string SelectedStateFilter { get => stateFilter ?? tr("Todos"); set => SetFilter(ref stateFilter, value); }
    public string ProviderFilterName => tr("Filtrar por agente");
    public string ProjectFilterName => tr("Filtrar por proyecto");
    public string StateFilterName => tr("Filtrar por estado");
    public string SearchName => tr("Buscar sesiones recientes");
    public string ActiveTabLabel => tr("Activas · %d", ActiveSessionCount);
    public string RecentTabLabel => tr("Recientes · %d", RecentSessionCount);
    public string ActiveTabHelp => tr("Mostrar sesiones activas");
    public string RecentTabHelp => tr("Mostrar sesiones terminadas o inactivas de las últimas 12 horas");
    public string SearchHint => tr("Buscar nombre, proyecto o actividad");
    public AgentSnapshot? Current
    {
        get => VisibleSessions.FirstOrDefault(s => AgentLogs.Key(s) == selectedSession) ?? VisibleSessions.FirstOrDefault();
        set { if (value is not null && selectedSession != AgentLogs.Key(value)) { selectedSession = AgentLogs.Key(value); detailsExpanded = false; Raise(); } }
    }
    public string CurrentProject => Current?.ProjectName ?? "";
    public string CurrentName => Current?.Name ?? "";
    public string CurrentProvider => Current is { IsAutomation: true } automation
        ? tr("Automatización · %@", automation.Provider) : Current?.Provider ?? "";
    public string CurrentState => Current?.StateLabel ?? "";
    public string CurrentToolAction => Current?.ToolAction ?? "";
    public string CurrentTool => Current?.Tool ?? "";
    public string CurrentFile => Current?.File ?? "";
    public string CurrentMessage => Current?.Message ?? "";
    public string CurrentChanges => Current?.Changes ?? "";
    public bool HasChanges => CurrentChanges.Length > 0;
    public bool HasTool => CurrentTool.Length > 0;
    public bool HasFile => CurrentFile.Length > 0;
    public bool HasMessage => CurrentMessage.Length > 0;
    public bool HasRecentActivity => Current?.RecentActivity.Count > 0;
    public bool CurrentWorking => Current?.State == AgentState.Working;
    public bool CurrentNeedsAttention => Current?.State == AgentState.NeedsAttention;
    public bool CurrentFinished => Current?.State == AgentState.Finished;
    public string ActivityLabel => tr(showingRecent ? "Sesiones recientes" : "Sesiones activas");
    public string EmptyTitle => !Enabled ? tr("Agentes de IA desactivados")
        : showingRecent && HasRecentFilter ? tr("Sin coincidencias") : tr(showingRecent ? "No hay sesiones recientes." : "No hay agentes activos.");
    public string EmptyHint => Enabled && !showingRecent ? tr("Consulta aquí las sesiones terminadas o inactivas de las últimas 12 horas.") : "";
    public string RecentActivityLabel => tr("Actividad reciente");
    public string MessageLabel => tr(Current?.IsAutomation == true ? "Resultado de automatización" : "Mensaje reciente");
    public string ToolLabel => tr("Herramienta");
    public string FileLabel => tr("Archivo");
    public string CurrentActivityTime => Current is { } current
        ? tr("Última actividad: %@", current.LastActivity.ToLocalTime().ToString("HH:mm")) + (current.InactivityLabel.Length > 0 ? $" · {current.InactivityLabel}" : "") : "";
    public string CurrentWorkSummary => Current?.WorkSummary ?? "";
    public bool HasWorkSummary => CurrentWorkSummary.Length > 0;
    public string CurrentOverview => Current is { IsAutomation: true } automation ? automation.Message ?? CurrentWorkSummary
        : CurrentWorkSummary.Length > 0 ? CurrentWorkSummary : CurrentToolAction.Length > 0 ? CurrentToolAction : Current?.Message ?? "";
    public bool HasOverview => CurrentOverview.Length > 0;
    public bool DetailsExpanded => detailsExpanded;
    public string DetailsToggleLabel => tr(detailsExpanded ? "Ocultar detalles" : "Ver detalles");
    public string DetailsToggleHelp => tr(detailsExpanded ? "Oculta el detalle de esta sesión" : "Muestra herramientas, archivos y actividad de esta sesión");
    public ICommand SelectSession { get; }
    public ICommand SelectActive { get; }
    public ICommand SelectRecent { get; }
    public ICommand ToggleDetails { get; }
    public bool Enabled => prefs.Switch(PrefKeys.AgentsEnabled, false);
    public string EmptyText => !Enabled ? tr("Activa Agentes de IA en Ajustes › Notch.")
        : showingRecent && HasRecentFilter ? tr("No hay sesiones que coincidan. Prueba con otros filtros.")
        : showingRecent ? tr("Las sesiones terminadas y las que llevan cinco minutos sin actividad se conservan hasta 12 horas.")
        : tr("Claude Code y Codex aparecerán aquí cuando trabajen.");
    public override bool Always => Enabled;

    public AgentsWidget(Prefs prefs) : base(WidgetCatalog.Find("agents")!)
    {
        this.prefs = prefs;
        Sessions.CollectionChanged += (_, _) => { RebuildVisibleSessions(); Raise(); };
        RecentSessions.CollectionChanged += (_, _) => { RebuildVisibleSessions(); Raise(); };
        SelectActive = new Command(() => SelectView(false));
        SelectRecent = new Command(() => SelectView(true));
        ToggleDetails = new Command(() => { detailsExpanded = !detailsExpanded; Raise(); });
        SelectSession = new ParamCommand(value =>
        {
            if (value is AgentSnapshot session && selectedSession != AgentLogs.Key(session))
            {
                selectedSession = AgentLogs.Key(session);
                detailsExpanded = false;
                Raise();
            }
        });
        RebuildVisibleSessions();
    }

    private bool MatchesRecentFilter(AgentSnapshot session)
    {
        if (providerFilter is not null && !string.Equals(session.Provider, providerFilter, StringComparison.OrdinalIgnoreCase)) return false;
        if (projectFilter is not null && !string.Equals(session.ProjectName, projectFilter, StringComparison.OrdinalIgnoreCase)) return false;
        if (stateFilter is not null && !string.Equals(session.StateFilterLabel, stateFilter, StringComparison.Ordinal)) return false;
        if (string.IsNullOrWhiteSpace(recentSearch)) return true;
        var query = recentSearch.Trim();
        return string.Join(" ", session.Name, session.ProjectName, session.Provider, session.AutomationId, session.Message,
                session.Tool, session.WorkSummary, string.Join(" ", session.RecentActivity.Select(a => a.Tool ?? a.Message)))
            .Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<string> FilterOptions(IEnumerable<string> values) => new[] { tr("Todos") }
        .Concat(values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)).ToArray();

    private void SetFilter(ref string? filter, string value)
    {
        var next = value == tr("Todos") ? null : value;
        if (filter == next) return;
        filter = next;
        FilterChanged();
    }

    private void FilterChanged()
    {
        RebuildVisibleSessions();
        var visible = VisibleSessions;
        if (!visible.Any(s => AgentLogs.Key(s) == selectedSession)) selectedSession = visible.FirstOrDefault() is { } first ? AgentLogs.Key(first) : null;
        Raise();
    }

    private void SelectView(bool recent)
    {
        if (showingRecent == recent) return;
        showingRecent = recent;
        RebuildVisibleSessions();
        selectedSession = VisibleSessions.FirstOrDefault() is { } first ? AgentLogs.Key(first) : null;
        detailsExpanded = false;
        Raise();
    }

    private void RebuildVisibleSessions()
    {
        filteredRecentSessions = RecentSessions.Where(MatchesRecentFilter).ToArray();
        visibleSessions = showingRecent ? filteredRecentSessions : Sessions.ToArray();
    }

    public override void Tick(DateTime now)
    {
        if (!Enabled)
        {
            if (Sessions.Count > 0) Sessions.Clear();
            if (RecentSessions.Count > 0) RecentSessions.Clear();
            historyRead = false; showingRecent = false; selectedSession = null; Raise(); return;
        }
        if (reading || now < nextRead) return;
        reading = true;
        nextRead = now.AddSeconds(2);
        var includeRecentHistory = !historyRead;
        _ = Task.Run(() => AgentsWatcher.ReadRecent(includeRecentHistory)).ContinueWith(task => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            reading = false;
            if (!Enabled) { Sessions.Clear(); RecentSessions.Clear(); historyRead = false; showingRecent = false; selectedSession = null; Raise(); return; }
            if (task.Status == TaskStatus.RanToCompletion)
            {
                historyRead = true;
                var priorSession = selectedSession;
                var snapshots = task.Result;
                var active = snapshots.Where(s => AgentLogs.IsActive(s, DateTime.UtcNow))
                    .OrderByDescending(s => s.State == AgentState.NeedsAttention).ThenByDescending(s => s.LastActivity).ToArray();
                var recent = AgentLogs.RecentSessions(RecentSessions, snapshots, DateTime.UtcNow);
                Sessions.Clear(); foreach (var session in active) Sessions.Add(session);
                RecentSessions.Clear(); foreach (var session in recent) RecentSessions.Add(session);
                if (providerFilter is not null && !ProviderOptions.Contains(providerFilter)) providerFilter = null;
                if (projectFilter is not null && !ProjectOptions.Contains(projectFilter)) projectFilter = null;
                if (stateFilter is not null && !StateOptions.Contains(stateFilter)) stateFilter = null;
                RebuildVisibleSessions();
                selectedSession = VisibleSessions.FirstOrDefault(s => AgentLogs.Key(s) == priorSession) is { } current
                    ? AgentLogs.Key(current) : VisibleSessions.FirstOrDefault() is { } first ? AgentLogs.Key(first) : null;
            }
            Raise();
        }));
    }

    public string StateText(AgentSnapshot s) => s.State switch
    {
        AgentState.Working => tr("Trabajando"), AgentState.NeedsAttention => tr("Necesita tu atención"),
        AgentState.Finished => tr("Terminó"), AgentState.Inactive => tr("Sin actividad"), _ => tr("Estado desconocido"),
    };
    public string Detail(AgentSnapshot s) => string.Join(" · ", new[] { s.Tool, s.File is null ? null : Path.GetFileName(s.File) }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

public static class AgentsWatcher
{
    // shortcut: rescans recent log paths every 2 seconds and tails 512 KB per file; switch to incremental FileSystemWatcher reads if recursive scans become costly.
    public static List<AgentSnapshot> ReadRecent(bool includeRecentHistory = false)
    {
        var now = DateTime.UtcNow;
        var historyCutoff = now - AgentLogs.RecentWindow;
        var liveCutoff = now - TimeSpan.FromMinutes(5);
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions"),
        };
        var files = new List<FileInfo>();
        foreach (var root in roots)
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                var pattern = root.Contains(".claude", StringComparison.OrdinalIgnoreCase) ? "*.jsonl" : "rollout-*.jsonl";
                files.AddRange(Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
                    .Select(p => new FileInfo(p)).Where(f => f.LastWriteTimeUtc <= now && f.LastWriteTimeUtc >= (includeRecentHistory ? historyCutoff : liveCutoff)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        var liveFiles = files.Where(f => f.LastWriteTimeUtc >= liveCutoff);
        IEnumerable<FileInfo> recentFiles = includeRecentHistory ? files.Where(f => f.LastWriteTimeUtc < liveCutoff) : Enumerable.Empty<FileInfo>();
        files = liveFiles.Concat(recentFiles).DistinctBy(f => f.FullName, StringComparer.OrdinalIgnoreCase).ToList();
        var result = new List<AgentSnapshot>();
        foreach (var file in files.OrderByDescending(f => f.LastWriteTimeUtc))
        {
            try
            {
                using var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > 524_288) stream.Seek(-524_288, SeekOrigin.End);
                using var reader = new StreamReader(stream);
                var lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var codex = file.FullName.Contains(".codex", StringComparison.OrdinalIgnoreCase);
                string project = Path.GetFileName(Path.GetDirectoryName(file.FullName)) ?? "";
                if (codex) project = "Codex";
                result.Add(AgentLogs.Parse(Path.GetFileNameWithoutExtension(file.Name), project, codex ? "Codex" : "Claude Code", lines, file.LastWriteTimeUtc));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return result.Where(s => s.State is AgentState.Working or AgentState.NeedsAttention or AgentState.Finished)
            .OrderBy(s => s.State == AgentState.Finished)
            .ThenByDescending(s => s.State == AgentState.NeedsAttention)
            .ThenByDescending(s => s.LastActivity).ToList();
    }
}

public sealed class WeatherWidget(Prefs prefs) : WidgetModel(WidgetCatalog.Find("weather")!)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private DateTime nextRead;
    private string requestedCity = "";
    private bool reading;
    public WeatherForecast? Forecast { get; private set; }
    public bool HasForecast => Forecast is not null;
    public bool NoForecast => Forecast is null;
    public string Status => !prefs.Switch(PrefKeys.WeatherEnabled, false) ? tr("Activa Tiempo en Ajustes › Notch.")
        : prefs.WeatherCity.Length == 0 ? tr("Elige una ciudad en Ajustes › Notch.")
        : Forecast is null ? tr("No se pudo consultar el tiempo.") : "";
    public string Current => Forecast is null ? "" : $"{Forecast.Temperature:0}° · {Description(Forecast.Code)}";
    public ObservableCollection<WeatherHour> Hours { get; } = [];

    public override void Tick(DateTime now)
    {
        var city = prefs.WeatherCity;
        if (city != requestedCity)
        {
            requestedCity = city; Forecast = null; Hours.Clear(); nextRead = default;
        }
        if (!prefs.Switch(PrefKeys.WeatherEnabled, false) || city.Length == 0)
        {
            if (Forecast is not null) { Forecast = null; Hours.Clear(); Raise(); }
            return;
        }
        if (reading || now < nextRead) return;
        reading = true; nextRead = now.AddMinutes(30);
        _ = Load(city).ContinueWith(task => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            reading = false;
            if (!prefs.Switch(PrefKeys.WeatherEnabled, false) || prefs.WeatherCity != city) return;
            if (task.Status != TaskStatus.RanToCompletion) nextRead = DateTime.Now.AddMinutes(5);
            Forecast = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
            Hours.Clear(); if (Forecast is { } result) foreach (var hour in result.Hours.Take(8)) Hours.Add(hour);
            Raise();
        }));
    }

    private static async Task<WeatherForecast?> Load(string city)
    {
        var language = L10n.Effective == "es" ? "es" : "en";
        var geo = await Http.GetStringAsync("https://geocoding-api.open-meteo.com/v1/search?name=" + WebUtility.UrlEncode(city) + $"&count=1&language={language}&format=json");
        using var doc = JsonDocument.Parse(geo);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0) return null;
        var location = results[0];
        var lat = location.GetProperty("latitude").GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var lon = location.GetProperty("longitude").GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var name = location.GetProperty("name").GetString() ?? city;
        var json = await Http.GetStringAsync($"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,weather_code&hourly=temperature_2m,weather_code&forecast_days=1&timezone=auto");
        return WeatherForecast.Parse(name, json);
    }

    public static string Description(int code) => code switch
    {
        0 => tr("Despejado"), 1 or 2 => tr("Parcialmente nublado"), 3 => tr("Nublado"), 45 or 48 => tr("Niebla"),
        >= 51 and <= 67 => tr("Llovizna o lluvia"), >= 71 and <= 77 => tr("Nieve"), >= 80 and <= 82 => tr("Chubascos"),
        >= 95 => tr("Tormenta"), _ => tr("Tiempo"),
    };
}

public sealed class TeleprompterWidget : WidgetModel
{
    private readonly Prefs prefs;
    private readonly DispatcherTimer save = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly bool persist;
    private string text = "";
    private int speed = 25, size = 28;
    private bool running = true;

    public string Text { get => text; set { if (text == value) return; text = value; save.Stop(); save.Start(); RaiseProperty(nameof(Text)); } }
    public int Speed { get => speed; set { speed = Math.Clamp(value, 5, 60); if (persist) Defaults.Set(PrefKeys.TeleprompterSpeed, speed); RaiseProperty(nameof(Speed)); RaiseProperty(nameof(SpeedLabel)); RaiseProperty(nameof(SpeedFraction)); } }
    public int TextSize { get => size; set { size = Math.Clamp(value, 16, 48); if (persist) Defaults.Set(PrefKeys.TeleprompterSize, size); RaiseProperty(nameof(TextSize)); RaiseProperty(nameof(SizeLabel)); RaiseProperty(nameof(SizeFraction)); } }
    public double SpeedFraction => (Speed - 5) / 55.0;
    public double SizeFraction => (TextSize - 16) / 32.0;
    public string SpeedLabel => tr("Velocidad: %@", Speed);
    public string SizeLabel => tr("Tamaño: %@", TextSize);
    public bool Running { get => running; set { running = value; RaiseProperty(nameof(Running)); RaiseProperty(nameof(RunGlyph)); RaiseProperty(nameof(RunHelp)); } }
    public string RunGlyph => Running ? "" : "";
    public string RunHelp => Running ? tr("Pausar teleprompter") : tr("Iniciar teleprompter");
    public bool ReducedMotion => prefs.ReducedMotion;
    public ICommand SetSpeed { get; }
    public ICommand SetSize { get; }
    public ICommand Toggle { get; }
    public TeleprompterWidget(Prefs prefs, bool persist) : base(WidgetCatalog.Find("teleprompter")!)
    {
        this.prefs = prefs;
        this.persist = persist;
        Toggle = new Command(() => Running = !Running);
        if (persist) text = Defaults.GetString(PrefKeys.TeleprompterText) ?? "";
        speed = Math.Clamp(Defaults.GetInt(PrefKeys.TeleprompterSpeed, 25), 5, 60);
        size = Math.Clamp(Defaults.GetInt(PrefKeys.TeleprompterSize, 28), 16, 48);
        SetSpeed = new ParamCommand(v => { if (v is double d) Speed = (int)Math.Round(5 + d * 55); });
        SetSize = new ParamCommand(v => { if (v is double d) TextSize = (int)Math.Round(16 + d * 32); });
        save.Tick += (_, _) => { save.Stop(); if (this.persist) Defaults.Set(PrefKeys.TeleprompterText, text); };
    }
}
