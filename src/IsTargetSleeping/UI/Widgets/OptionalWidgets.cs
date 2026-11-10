using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private readonly AgentsWatcher watcher = new();
    private bool updatingSessions;
    private string? selectedSession;
    private bool showingRecent;
    private readonly HashSet<string> seenAutomationCompletions = new(StringComparer.Ordinal);
    private readonly Queue<string> seenAutomationOrder = new();
    private readonly Queue<string> automationNotices = new();
    private string? currentAutomationNotice;
    private DateTime automationNoticeUntil;
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
    // El resumen de un vistazo: cuántos agentes trabajan (o, si ninguno, cuántos esperan algo de ti) y las primeras sesiones.
    public int WorkingCount => Sessions.Count(s => s.State == AgentState.Working);
    public int AttentionCount => Sessions.Count(s => s.State == AgentState.NeedsAttention);
    public bool HasAttention => AttentionCount > 0;
    public int HeadNumber => WorkingCount > 0 ? WorkingCount : AttentionCount > 0 ? AttentionCount : Sessions.Count;
    public string HeadCaption => WorkingCount > 0 ? tr(WorkingCount == 1 ? "agente trabajando" : "agentes trabajando")
        : AttentionCount > 0 ? tr(AttentionCount == 1 ? "espera tu respuesta" : "esperan tu respuesta")
        : Sessions.Count > 0 ? tr("sin actividad") : tr("en reposo");
    public string AttentionText => AttentionCount == 1 ? tr("1 espera tu respuesta") : tr("%d esperan tu respuesta", AttentionCount);
    public bool HeadWorking => WorkingCount > 0;
    public IReadOnlyList<AgentSnapshot> Top2 => VisibleSessions.Take(2).ToList();
    public IReadOnlyList<AgentSnapshot> Top3 => VisibleSessions.Take(3).ToList();
    public bool HasSessions => VisibleSessions.Any();
    public bool NoSessions => !HasSessions;
    public bool ShowingRecent => showingRecent;
    public bool ShowRecentFilters => Enabled && showingRecent;
    public bool HasRecentFilter => !string.IsNullOrWhiteSpace(recentSearch) || providerFilter is not null || projectFilter is not null || stateFilter is not null;
    public string RecentSearch { get => recentSearch; set { if (recentSearch == value) return; recentSearch = value ?? ""; FilterChanged(); } }
    // Las listas de los filtros son siempre la misma instancia mientras no cambien sus valores: si cada lectura devolviera una lista nueva, el
    // desplegable la tomaría por otra, se quedaría sin elemento elegido y mandaría un nulo que borraba el filtro al instante.
    private IReadOnlyList<string> providerOptions = [], projectOptions = [], stateOptions = [];
    public IReadOnlyList<string> ProviderOptions => providerOptions;
    public IReadOnlyList<string> ProjectOptions => projectOptions;
    public IReadOnlyList<string> StateOptions => stateOptions;
    // Un nulo es el desplegable vaciando su selección al cambiarle la lista, no una elección: se ignora.
    public string SelectedProviderFilter { get => providerFilter ?? tr("Todos"); set { if (value is not null) SetFilter(ref providerFilter, value); } }
    public string SelectedProjectFilter { get => projectFilter ?? tr("Todos"); set { if (value is not null) SetFilter(ref projectFilter, value); } }
    public string SelectedStateFilter { get => stateFilter ?? tr("Todos"); set { if (value is not null) SetFilter(ref stateFilter, value); } }
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
    /// La insignia del agente de la sesión elegida: Codex lleva la suya y Claude Code la otra.
    public bool CurrentIsCodex => string.Equals(Current?.Provider, "Codex", StringComparison.OrdinalIgnoreCase);
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
    public bool CanEnable => !Enabled;
    public string EnableText => tr("Activar Agentes de IA");
    public ICommand EnableCommand => enable ??= new Command(() => prefs.SetSwitch(PrefKeys.AgentsEnabled, true));
    private ICommand? enable;
    public ICommand SelectSession { get; }
    public ICommand SelectActive { get; }
    /// Las sesiones del propio equipo Claude + Codex mientras trabaja: sus pasos de Claude se lanzan sin guardar la sesión (no dejan registro que
    /// leer) y los de Codex corren en una copia de trabajo de la app, así que el widget Equipo las aporta aquí para que se vea que se está trabajando.
    public Func<IReadOnlyList<AgentSnapshot>>? TeamSessions { get; set; }

    // Seguir una sesión real que ya no trabaja: una pregunta por el CLI, en solo lectura (ver AgentResume).
    private string resumeText = "", resumeStatus = "";
    private bool resuming;
    public string ResumeText { get => resumeText; set { resumeText = value ?? ""; Raise(); } }
    public bool CanResumeCurrent => Enabled && Current is { } c && AgentResume.CanResume(c);
    public bool Resuming => resuming;
    public bool CanSendResume => CanResumeCurrent && !resuming && resumeText.Trim().Length > 0;
    public bool ShowResumeHint => resumeText.Length == 0;
    public string ResumeHint => tr("Seguir esta sesión…");
    public string ResumeLabel => tr(resuming ? "Preguntando…" : "Preguntar");
    public string ResumeHelp => tr("Retoma la sesión real con todo su contexto, en solo lectura (no edita archivos ni ejecuta comandos). Gasta tokens de esa sesión.");
    public string ResumeStatus => resumeStatus;
    public bool HasResumeStatus => resumeStatus.Length > 0;
    public ICommand SendResume { get; private set; } = null!;

    private void ResumeNow()
    {
        if (!CanSendResume || Current is not { } session) return;
        var text = resumeText.Trim();
        resuming = true;
        resumeStatus = "";
        Raise();
        _ = Task.Run(() => AgentResume.RunAsync(session, text)).ContinueWith(task => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            resuming = false;
            var result = task.Status == TaskStatus.RanToCompletion ? task.Result : new AgentResult(false, "", Error: task.Exception?.GetBaseException().Message);
            if (result.Ok) { resumeText = ""; resumeStatus = tr("Hecho: el mensaje nuevo sale arriba en unos segundos."); nextRead = DateTime.MinValue; }
            else resumeStatus = tr("No se pudo seguir la sesión: %@", result.Error ?? "");
            Raise();
        }));
    }
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
        Sessions.CollectionChanged += (_, _) => { if (!updatingSessions) { RebuildVisibleSessions(); Raise(); } };
        RecentSessions.CollectionChanged += (_, _) => { if (!updatingSessions) { RebuildVisibleSessions(); Raise(); } };
        SendResume = new Command(ResumeNow);
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
        RefreshOptions();
        RebuildVisibleSessions();
    }

    public void Dispose() { if (!reading) watcher.Dispose(); }

    /// Sesiones de muestra para la galería de widgets (no se leen del disco).
    public void LoadDemo()
    {
        var now = DateTime.UtcNow;
        Sessions.Clear(); RecentSessions.Clear();
        Sessions.Add(new AgentSnapshot("demo-1", @"C:\Proyectos\isTargetSleeping", "Claude Code", AgentState.Working, "Edit", @"C:\Proyectos\isTargetSleeping\App.xaml", 24, 6,
            tr("Ajustando el diseño del Home"), [new AgentActivity("Read", @"C:\Proyectos\isTargetSleeping\App.xaml", null, true), new AgentActivity("Edit", @"C:\Proyectos\isTargetSleeping\App.xaml", null)], now) { Title = tr("Rediseño del Home") });
        Sessions.Add(new AgentSnapshot("demo-2", @"C:\Proyectos\isTargetSleeping", "Codex", AgentState.NeedsAttention, null, null, null, null,
            tr("Necesita tu visto bueno para seguir"), [], now.AddMinutes(-3)) { Title = tr("Pruebas del notch") });
        RecentSessions.Add(new AgentSnapshot("demo-3", @"C:\Proyectos\web", "Claude Code", AgentState.Finished, null, null, 12, 2,
            tr("Terminado"), [], now.AddHours(-2)) { Title = tr("Revisión de la web") });
        RebuildVisibleSessions();
        Raise();
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

    /// Rehace las listas de los filtros con lo que hay ahora, pero conserva la instancia si no cambió nada.
    public void RefreshOptions()
    {
        static IReadOnlyList<string> Keep(IReadOnlyList<string> current, IReadOnlyList<string> next) => current.SequenceEqual(next) ? current : next;
        providerOptions = Keep(providerOptions, FilterOptions(RecentSessions.Select(s => s.Provider)));
        projectOptions = Keep(projectOptions, FilterOptions(RecentSessions.Select(s => s.ProjectName)));
        stateOptions = Keep(stateOptions, FilterOptions(RecentSessions.Select(s => s.StateFilterLabel)));
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
            if (!reading) watcher.Dispose();
            if (Sessions.Count > 0) Sessions.Clear();
            if (RecentSessions.Count > 0) RecentSessions.Clear();
            seenAutomationCompletions.Clear(); seenAutomationOrder.Clear(); automationNotices.Clear(); currentAutomationNotice = null;
            historyRead = false; showingRecent = false; selectedSession = null; Raise(); return;
        }
        if (reading || now < nextRead) return;
        reading = true;
        nextRead = now.AddSeconds(2);
        var includeRecentHistory = !historyRead;
        _ = Task.Run(() => watcher.ReadRecent(includeRecentHistory)).ContinueWith(task => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            reading = false;
            if (!Enabled) { watcher.Dispose(); Sessions.Clear(); RecentSessions.Clear(); seenAutomationCompletions.Clear(); seenAutomationOrder.Clear(); automationNotices.Clear(); currentAutomationNotice = null; historyRead = false; showingRecent = false; selectedSession = null; Raise(); return; }
            if (task.Status == TaskStatus.RanToCompletion)
            {
                var newCompletions = AgentLogs.NewlyFinishedAutomations(task.Result, seenAutomationCompletions);
                foreach (var completed in newCompletions) seenAutomationOrder.Enqueue(AgentLogs.Key(completed));
                while (seenAutomationOrder.Count > 2048) seenAutomationCompletions.Remove(seenAutomationOrder.Dequeue());
                if (!includeRecentHistory)
                    foreach (var completed in newCompletions) automationNotices.Enqueue(completed.Name);
                while (automationNotices.Count > 16) automationNotices.Dequeue();
                historyRead = true;
                var priorSession = selectedSession;
                var snapshots = task.Result;
                // Los pasos del equipo no se cuentan dos veces: ya salen en la sesión del equipo.
                snapshots = snapshots.Where(s => !TeamService.OwnsPath(s.Project)).ToList();
                var active = snapshots.Where(s => AgentLogs.IsActive(s, DateTime.UtcNow))
                    .OrderByDescending(s => s.State == AgentState.NeedsAttention).ThenByDescending(s => s.LastActivity).ToList();
                if (TeamSessions?.Invoke() is { Count: > 0 } team) active.InsertRange(0, team);
                var recent = AgentLogs.RecentSessions(RecentSessions, snapshots, DateTime.UtcNow);
                updatingSessions = true;
                try
                {
                    if (!Sessions.SequenceEqual(active)) { Sessions.Clear(); foreach (var session in active) Sessions.Add(session); }
                    if (!RecentSessions.SequenceEqual(recent)) { RecentSessions.Clear(); foreach (var session in recent) RecentSessions.Add(session); }
                }
                finally { updatingSessions = false; }
                RefreshOptions();
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

    public string? AutomationNotice(DateTime now)
    {
        if (currentAutomationNotice is not null && now < automationNoticeUntil) return currentAutomationNotice;
        currentAutomationNotice = automationNotices.TryDequeue(out var next) ? next : null;
        automationNoticeUntil = now.AddSeconds(6);
        return currentAutomationNotice;
    }
}

public sealed class AgentsWatcher : IDisposable
{
    private readonly record struct Change(string Path, string? OldPath, bool Codex, WatcherChangeTypes Type);
    private readonly Dictionary<string, (long Length, DateTime Modified, DateTime Created, AgentSnapshot Snapshot)> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (FileInfo File, bool Codex)> files = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<Change> changes = new();
    private FileSystemWatcher? claudeWatcher, codexWatcher;
    private string? claudeRoot, codexRoot, language;
    private DateTime nextRescan;
    private int needsRescan = 1;

    public List<AgentSnapshot> ReadRecent(bool includeRecentHistory = false)
    {
        if (language != L10n.Effective) { cache.Clear(); language = L10n.Effective; }
        var now = DateTime.UtcNow;
        var historyCutoff = now - AgentLogs.RecentWindow;
        var liveCutoff = now - TimeSpan.FromMinutes(5);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var claudeHome = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (string.IsNullOrWhiteSpace(claudeHome)) claudeHome = Path.Combine(userProfile, ".claude");
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(codexHome)) codexHome = Path.Combine(userProfile, ".codex");
        var claudeSessions = Path.GetFullPath(Path.Combine(claudeHome, "projects"));
        var codexSessions = Path.GetFullPath(Path.Combine(codexHome, "sessions"));
        EnsureFiles(claudeSessions, codexSessions, now);
        var cutoff = includeRecentHistory ? historyCutoff : liveCutoff;
        var recentFiles = files.Values.Where(f => f.File.LastWriteTimeUtc >= cutoff && f.File.LastWriteTimeUtc <= now)
            .OrderByDescending(f => f.File.LastWriteTimeUtc).ToList();
        var present = recentFiles.Select(f => f.File.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in cache.Keys.Where(path => !present.Contains(path)).ToArray()) cache.Remove(path);
        var claudeScheduledTasks = ReadClaudeScheduledTasks();
        var result = new List<AgentSnapshot>();
        foreach (var entry in recentFiles)
        {
            try
            {
                var file = entry.File;
                long length = file.Length;
                var modified = file.LastWriteTimeUtc;
                var created = file.CreationTimeUtc;
                AgentSnapshot snapshot;
                if (cache.TryGetValue(file.FullName, out var known) && known.Length == length && known.Modified == modified && known.Created == created)
                    snapshot = known.Snapshot;
                else
                {
                    using var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (stream.Length > 524_288) stream.Seek(-524_288, SeekOrigin.End);
                    using var reader = new StreamReader(stream);
                    var lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    string project = entry.Codex ? "Codex" : Path.GetFileName(Path.GetDirectoryName(file.FullName)) ?? "";
                    snapshot = AgentLogs.Parse(Path.GetFileNameWithoutExtension(file.Name), project, entry.Codex ? "Codex" : "Claude Code", lines, modified);
                    file.Refresh();
                    if (file.Exists && file.Length == length && file.LastWriteTimeUtc == modified && file.CreationTimeUtc == created)
                        cache[file.FullName] = (length, modified, created, snapshot);
                    else cache.Remove(file.FullName);
                }
                if (entry.Codex && snapshot.AutomationId is { } codexId && ReadCodexAutomationName(codexId) is { } codexName)
                    snapshot = snapshot with { Title = codexName };
                else if (!entry.Codex && snapshot.Title is { } title && claudeScheduledTasks.TryGetValue(title, out var claudeName))
                    snapshot = snapshot with { Title = claudeName, AutomationId = snapshot.AutomationId ?? $"claude-local:{claudeName}" };
                result.Add(snapshot);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return result.Where(s => s.State is AgentState.Working or AgentState.NeedsAttention or AgentState.Finished)
            .OrderBy(s => s.State == AgentState.Finished)
            .ThenByDescending(s => s.State == AgentState.NeedsAttention)
            .ThenByDescending(s => s.LastActivity).ToList();
    }

    private void EnsureFiles(string claudeSessions, string codexSessions, DateTime now)
    {
        if (!string.Equals(claudeRoot, claudeSessions, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(codexRoot, codexSessions, StringComparison.OrdinalIgnoreCase))
        {
            claudeWatcher?.Dispose(); codexWatcher?.Dispose();
            claudeWatcher = codexWatcher = null;
            claudeRoot = claudeSessions; codexRoot = codexSessions;
            files.Clear(); cache.Clear(); nextRescan = default;
            while (changes.TryDequeue(out _)) { }
            Interlocked.Exchange(ref needsRescan, 1);
        }
        claudeWatcher ??= Watch(claudeSessions, codex: false);
        codexWatcher ??= Watch(codexSessions, codex: true);
        if (claudeWatcher is null && Directory.Exists(claudeSessions) || codexWatcher is null && Directory.Exists(codexSessions))
            Interlocked.Exchange(ref needsRescan, 1);

        if (Interlocked.Exchange(ref needsRescan, 0) != 0 || now >= nextRescan)
        {
            while (changes.TryDequeue(out _)) { }
            files.Clear();
            var historyCutoff = now - AgentLogs.RecentWindow;
            Scan(claudeSessions, codex: false, historyCutoff);
            Scan(codexSessions, codex: true, historyCutoff);
            // shortcut: a directory move can evade file events; the five-minute rescan repairs the index.
            nextRescan = now.AddMinutes(5);
            return;
        }
        while (changes.TryDequeue(out var change)) Apply(change);
    }

    private FileSystemWatcher? Watch(string root, bool codex)
    {
        if (!Directory.Exists(root)) return null;
        try
        {
            var watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            watcher.Created += (_, e) => Queue(e, codex);
            watcher.Changed += (_, e) => Queue(e, codex);
            watcher.Deleted += (_, e) => Queue(e, codex);
            watcher.Renamed += (_, e) => Queue(e, codex);
            watcher.Error += (_, _) => Interlocked.Exchange(ref needsRescan, 1);
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    private void Queue(FileSystemEventArgs e, bool codex)
    {
        var oldPath = (e as RenamedEventArgs)?.OldFullPath;
        if (IsSessionLog(e.FullPath, codex) || oldPath is not null && IsSessionLog(oldPath, codex))
            changes.Enqueue(new(e.FullPath, oldPath, codex, e.ChangeType));
        else if (e.ChangeType != WatcherChangeTypes.Changed) Interlocked.Exchange(ref needsRescan, 1);
    }

    private static bool IsSessionLog(string path, bool codex)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)
            && (!codex || name.StartsWith("rollout-", StringComparison.OrdinalIgnoreCase));
    }

    private void Scan(string root, bool codex, DateTime historyCutoff)
    {
        try
        {
            if (!Directory.Exists(root)) return;
            var pattern = codex ? "rollout-*.jsonl" : "*.jsonl";
            foreach (var path in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
            {
                try
                {
                    var file = new FileInfo(path);
                    file.Refresh();
                    if (file.Exists && file.LastWriteTimeUtc >= historyCutoff) files[file.FullName] = (file, codex);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private void Apply(Change change)
    {
        var root = change.Codex ? codexRoot : claudeRoot;
        if (root is null) return;
        if (change.OldPath is { } oldPath && oldPath.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            Remove(oldPath);
        if (!change.Path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
        if (change.Type == WatcherChangeTypes.Deleted) { Remove(change.Path); return; }
        try
        {
            var file = new FileInfo(change.Path);
            file.Refresh();
            if (file.Exists && IsSessionLog(file.FullName, change.Codex))
            {
                files[file.FullName] = (file, change.Codex);
                cache.Remove(file.FullName);
            }
            else Remove(file.FullName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { Remove(change.Path); }
    }

    private void Remove(string path) { files.Remove(path); cache.Remove(path); }

    public void Dispose()
    {
        claudeWatcher?.Dispose(); codexWatcher?.Dispose();
        claudeWatcher = codexWatcher = null;
        claudeRoot = codexRoot = null;
        files.Clear(); cache.Clear(); nextRescan = default;
        while (changes.TryDequeue(out _)) { }
        Interlocked.Exchange(ref needsRescan, 1);
    }

    private static string? ReadCodexAutomationName(string id)
    {
        if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains('/') || id.Contains('\\') || id is "." or "..") return null;
        var home = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(home)) home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        try
        {
            var file = Path.Combine(home, "automations", id, "automation.toml");
            foreach (var line in File.ReadLines(file).Take(80))
            {
                var match = Regex.Match(line, "^\\s*name\\s*=\\s*(\"(?:\\\\.|[^\"])*\")");
                if (match.Success) return JsonSerializer.Deserialize<string>(match.Groups[1].Value);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return null;
    }

    private static Dictionary<string, string> ReadClaudeScheduledTasks()
    {
        var root = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (string.IsNullOrWhiteSpace(root)) root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var scheduledTasks = Path.Combine(root, "scheduled-tasks");
        if (!Directory.Exists(scheduledTasks)) return result;
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(scheduledTasks))
            {
                try
                {
                    using var reader = File.OpenText(Path.Combine(directory, "SKILL.md"));
                    if (reader.ReadLine()?.Trim() != "---") continue;
                    for (var i = 0; i < 40 && reader.ReadLine() is { } line && line.Trim() != "---"; i++)
                    {
                        if (!line.TrimStart().StartsWith("name:", StringComparison.Ordinal)) continue;
                        var name = line[(line.IndexOf(':') + 1)..].Trim().Trim('"', '\'');
                        if (name.Length == 0) break;
                        result[name] = name;
                        result[Path.GetFileName(directory)] = name;
                        break;
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return result;
    }
}

public sealed class WeatherWidget : WidgetModel
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private const string CacheKey = "weatherForecastCache";
    private sealed record CachedForecast(string City, WeatherForecast Forecast, DateTime Updated);

    private readonly Prefs prefs;
    private DateTime nextRead, updatedAt;
    private string requestedCity = "";
    private bool reading, cacheChecked, demo;

    public WeatherWidget(Prefs prefs) : base(WidgetCatalog.Find("weather")!)
    {
        this.prefs = prefs;
        requestedCity = prefs.WeatherCity;
        if (!prefs.IsDemo && prefs.Switch(PrefKeys.WeatherEnabled, false) && requestedCity.Length > 0) RestoreCache(requestedCity);
    }

    public WeatherForecast? Forecast { get; private set; }
    public bool HasForecast => Forecast is not null;
    public bool NoForecast => Forecast is null;
    public string UpdatedText => Forecast is null || updatedAt == default ? "" : tr("actualizado %@", AiUsage.Ago(updatedAt, DateTime.Now));
    public string Status => !prefs.Switch(PrefKeys.WeatherEnabled, false) ? tr("Activa Clima en Ajustes › Notch.")
        : prefs.WeatherCity.Length == 0 ? tr("Elige una ciudad en Ajustes › Notch.")
        : Forecast is null ? tr("No se pudo consultar el tiempo.") : "";
    public string Current => Forecast is null ? "" : $"{Forecast.Temperature:0}° · {Description(Forecast.Code)}";
    public string TemperatureText => Forecast is null ? "" : $"{Forecast.Temperature:0}°";
    public string ConditionText => Forecast is null ? "" : Description(Forecast.Code);
    /// Las temperaturas de las próximas horas entre 0 y 1 (la menor abajo, la mayor arriba), para dibujar su curva.
    public IReadOnlyList<double> HourCurve
    {
        get
        {
            if (Hours.Count < 2) return [];
            double lo = Hours.Min(h => h.Temperature), hi = Hours.Max(h => h.Temperature), span = Math.Max(1, hi - lo);
            return Hours.Select(h => 0.12 + 0.76 * (h.Temperature - lo) / span).ToList();
        }
    }
    /// Las tres horas que siguen a la actual, para la vista corta (la completa enseña las ocho).
    public IReadOnlyList<WeatherHour> NextHours => Forecast?.NextHours ?? [];
    public bool IsNight => Forecast?.IsNight ?? (DateTime.Now.Hour is < 6 or >= 19);
    public int ConditionCode => Forecast?.Code ?? 0;
    /// Máxima y mínima de las próximas horas («↑ 27° ↓ 19°»): es lo que da la previsión por horas, no el día entero.
    public string HighLow => Forecast is { Hours.Count: > 0 } f ? $"↑ {Math.Max(f.Temperature, f.Hours.Max(h => h.Temperature)):0}°  ↓ {Math.Min(f.Temperature, f.Hours.Min(h => h.Temperature)):0}°" : "";

    /// Solo para las capturas del arnés y las pruebas: una previsión de ejemplo, sin red.
    public void LoadDemo(bool english)
    {
        demo = true;
        var start = new DateTime(2026, 10, 21, 22, 0, 0);
        int[] codes = [45, 3, 2, 61, 61, 80, 95, 0];
        double[] temps = [25, 24, 24, 23, 22, 22, 21, 21];
        Forecast = new WeatherForecast(english ? "Panama City" : "Ciudad de Panamá", 25, 45, codes.Select((c, i) => new WeatherHour(start.AddHours(i + 1), temps[i], c)).ToList());
        updatedAt = DateTime.Now;
        PopulateHours(markFirstNow: true);
        Raise();
    }
    public ObservableCollection<WeatherHour> Hours { get; } = [];

    public override void Tick(DateTime now)
    {
        if (demo) return;
        var city = prefs.WeatherCity;
        bool cityChanged = city != requestedCity;
        if (city != requestedCity)
        {
            requestedCity = city; Forecast = null; Hours.Clear(); nextRead = default; cacheChecked = false;
        }
        if (!prefs.Switch(PrefKeys.WeatherEnabled, false) || city.Length == 0)
        {
            if (Forecast is not null || cityChanged) { Forecast = null; Hours.Clear(); Raise(); }
            cacheChecked = false;
            return;
        }
        if (!cacheChecked)
        {
            RestoreCache(city);
            if (cityChanged || Forecast is not null) Raise();
        }
        if (reading || now < nextRead) return;
        reading = true; nextRead = now.AddMinutes(30);
        _ = Load(city).ContinueWith(task => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            reading = false;
            if (!prefs.Switch(PrefKeys.WeatherEnabled, false) || prefs.WeatherCity != city) return;
            if (task.Status == TaskStatus.RanToCompletion && task.Result is { } fresh)
            {
                Forecast = Localize(fresh, city);
                updatedAt = DateTime.Now;
                SaveCache(city);
            }
            else nextRead = DateTime.Now.AddMinutes(5);
            PopulateHours();
            Raise();
        }));
    }

    private void RestoreCache(string city)
    {
        cacheChecked = true;
        var json = Defaults.GetString(CacheKey);
        if (json is not { Length: > 0 and <= 131072 }) return;
        CachedForecast? cached;
        try { cached = JsonSerializer.Deserialize<CachedForecast>(json); }
        catch (JsonException) { Defaults.Remove(CacheKey); return; }
        if (cached is null) { Defaults.Remove(CacheKey); return; }
        if (!string.Equals(cached.City, city, StringComparison.OrdinalIgnoreCase)) return;
        if (cached.Updated == default || cached.Forecast is not { Hours.Count: > 0 and <= 8 } forecast
            || string.IsNullOrWhiteSpace(forecast.City) || !double.IsFinite(forecast.Temperature)
            || forecast.Code is < 0 or > 99 || forecast.Hours.Any(hour => hour is null || hour.Time == default
                || !double.IsFinite(hour.Temperature) || hour.Code is < 0 or > 99))
        {
            Defaults.Remove(CacheKey);
            return;
        }
        Forecast = Localize(forecast, city);
        updatedAt = cached.Updated;
        PopulateHours();
    }

    private void SaveCache(string city) => Defaults.Set(CacheKey, JsonSerializer.Serialize(new CachedForecast(city, Forecast!, updatedAt)));

    private static WeatherForecast Localize(WeatherForecast forecast, string city) =>
        forecast with { City = WeatherPlaces.Resolve(city)?.City(L10n.Effective == "es" ? "es" : "en") ?? forecast.City };

    private void PopulateHours(bool markFirstNow = false)
    {
        Hours.Clear();
        if (Forecast is { } forecast)
            foreach (var (hour, index) in forecast.Hours.Take(8).Select((hour, index) => (hour, index)))
                Hours.Add(markFirstNow && index == 0 ? hour with { IsNow = true } : hour);
    }

    private static async Task<WeatherForecast?> Load(string city)
    {
        var language = L10n.Effective == "es" ? "es" : "en";
        string lat, lon, name;
        if (WeatherPlaces.Resolve(city) is { } place)
        {
            // De la lista: ya se sabe dónde está, solo se envían las coordenadas.
            lat = place.Lat.ToString(System.Globalization.CultureInfo.InvariantCulture);
            lon = place.Lon.ToString(System.Globalization.CultureInfo.InvariantCulture);
            name = place.City(language);
        }
        else
        {
            // Un nombre guardado a mano de cuando se escribía: se busca como antes.
            var geo = await Http.GetStringAsync("https://geocoding-api.open-meteo.com/v1/search?name=" + WebUtility.UrlEncode(city) + $"&count=1&language={language}&format=json");
            using var doc = JsonDocument.Parse(geo);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0) return null;
            var location = results[0];
            lat = location.GetProperty("latitude").GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
            lon = location.GetProperty("longitude").GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
            name = location.GetProperty("name").GetString() ?? city;
        }
        var json = await Http.GetStringAsync($"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,weather_code&hourly=temperature_2m,weather_code&forecast_days=2&timezone=auto");
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
    private int resetRequest;
    private bool running = true;
    private bool editing;

    public string Text { get => text; set { if (text == value) return; text = value; save.Stop(); save.Start(); RaiseProperty(nameof(Text)); RaiseProperty(nameof(HasText)); RaiseProperty(nameof(StateLabel)); } }
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
    public int Speed { get => speed; set { speed = Math.Clamp(value, 5, 60); if (persist) Defaults.Set(PrefKeys.TeleprompterSpeed, speed); RaiseProperty(nameof(Speed)); RaiseProperty(nameof(SpeedLabel)); RaiseProperty(nameof(SpeedFraction)); } }
    public int TextSize { get => size; set { size = Math.Clamp(value, 16, 48); if (persist) Defaults.Set(PrefKeys.TeleprompterSize, size); RaiseProperty(nameof(TextSize)); RaiseProperty(nameof(SizeLabel)); RaiseProperty(nameof(SizeFraction)); } }
    public double SpeedFraction => (Speed - 5) / 55.0;
    public double SizeFraction => (TextSize - 16) / 32.0;
    public string SpeedLabel => tr("Velocidad: %@", Speed);
    public string SizeLabel => tr("Tamaño: %@", TextSize);
    public bool Running { get => running; set { if (running == value) return; running = value; RaiseProperty(nameof(Running)); RaiseProperty(nameof(RunGlyph)); RaiseProperty(nameof(RunHelp)); RaiseProperty(nameof(StateLabel)); } }
    public bool Editing { get => editing; private set { if (editing == value) return; editing = value; RaiseProperty(nameof(Editing)); RaiseProperty(nameof(EditGlyph)); RaiseProperty(nameof(EditHelp)); RaiseProperty(nameof(StateLabel)); } }
    public int ResetRequest => resetRequest;
    public string RunGlyph => Running ? "\uE769" : "\uE768";
    public string RunHelp => Running ? tr("Pausar teleprompter") : tr("Iniciar teleprompter");
    public string EditGlyph => Editing ? "\uE73E" : "\uE70F";
    public string EditHelp => Editing ? tr("Terminar edición") : tr("Editar guion");
    public string StateLabel => Editing ? tr("Editando") : !HasText ? tr("Sin guion") : Running ? tr("Leyendo") : tr("En pausa");
    public string RestartHelp => tr("Volver al principio");
    public string EmptyHint => tr("Escribe o pega tu guion");
    public bool ReducedMotion => prefs.ReducedMotion;
    public ICommand SetSpeed { get; }
    public ICommand SetSize { get; }
    public ICommand Toggle { get; }
    public ICommand ToggleEdit { get; }
    public ICommand Restart { get; }
    public TeleprompterWidget(Prefs prefs, bool persist) : base(WidgetCatalog.Find("teleprompter")!)
    {
        this.prefs = prefs;
        this.persist = persist;
        Toggle = new Command(() => { Editing = false; Running = !Running; });
        ToggleEdit = new Command(() => { if (!Editing) Running = false; Editing = !Editing; });
        Restart = new Command(() => { resetRequest++; RaiseProperty(nameof(ResetRequest)); });
        if (persist) text = Defaults.GetString(PrefKeys.TeleprompterText) ?? "";
        speed = Math.Clamp(Defaults.GetInt(PrefKeys.TeleprompterSpeed, 25), 5, 60);
        size = Math.Clamp(Defaults.GetInt(PrefKeys.TeleprompterSize, 28), 16, 48);
        SetSpeed = new ParamCommand(v => { if (v is double d) Speed = (int)Math.Round(5 + d * 55); });
        SetSize = new ParamCommand(v => { if (v is double d) TextSize = (int)Math.Round(16 + d * 32); });
        save.Tick += (_, _) => { save.Stop(); if (this.persist) Defaults.Set(PrefKeys.TeleprompterText, text); };
    }
}
