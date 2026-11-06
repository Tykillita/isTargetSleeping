using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// «Equipo»: reparte una tarea entre Claude y Codex, la lanza en una rama aparte y la deja lista para fusionar, sin salir del notch:
/// plan editable, avance en vivo (tres vistas), informe, diff, seguimientos, historial y tareas programadas. Esta clase solo
/// presenta lo que hace <see cref="TeamService"/>; nada corre sin que el usuario confirme, y nada se fusiona solo.
public sealed partial class TeamWidget : WidgetModel
{
    private static readonly TeamAgent[] Cycle = [TeamAgent.Sonnet, TeamAgent.Opus, TeamAgent.Codex, TeamAgent.Haiku];
    private readonly Prefs prefs;
    private readonly AgentsWidget? agents;
    private readonly bool demo;
    private readonly TeamService service;
    /// El servicio de Team de la app (para que la mascota publique su estado y obedezca cancelar/aprobar).
    public TeamService Service => service;
    private readonly TeamScheduler scheduler;
    private readonly Dictionary<string, (bool Found, DateTime At)> installed = [];
    private readonly Dictionary<string, (bool? LoggedIn, DateTime At)> sessions = [];
    private readonly HashSet<string> checking = [];
    private IReadOnlyList<CodexModel> models = [];
    private DateTime modelsAt, lastSchedulerTick = DateTime.MinValue;

    private string blockedText = "";
    // Entre pulsar cancelar y que el agente muera pasan segundos: se dice «Cancelando…» para que no parezca que no pasó nada.
    private bool cancelling;
    private string prompt = "", status = "", followUp = "", diffText = "", planDraft = "";
    private bool armed, mergeArmed, discardArmed, showDiff, schedulesEnabled;
    private int historyFilter;
    private string? diffFile, pendingFor;
    private TeamPresetItem selected;
    private TeamTab tab;
    private TaskProfile profile = new(TaskKind.Unknown, false, 0, false, false);
    private List<TeamStepRow> preview = [], live = [], recorded = [];
    private string liveKey = "";
    private RunView? run, demoRun;
    private List<TeamHistoryEntry> history = [];
    private List<TeamSchedule> schedules = [];
    private List<FileChange> files = [];
    private TeamHistoryEntry? entry;
    private TeamScheduleEditor? editor;

    public TeamWidget(Prefs prefs, bool demo, TeamService service, AgentsWidget? agents = null) : base(WidgetCatalog.Find("team")!)
    {
        this.agents = agents;
        if (agents is not null) agents.TeamSessions = TeamSessionSnapshots;
        this.prefs = prefs;
        this.demo = demo;
        this.service = service;
        scheduler = new TeamScheduler(service, () => Models);
        PresetItems =
        [
            new(TeamPreset.Auto, tr("Auto"), tr("Elige quién trabaja según la tarea y te explica por qué.")),
            new(TeamPreset.Saving, tr("Ahorro"), tr("Gasta menos: casi todo lo hace Codex y Claude solo planifica y revisa.")),
            new(TeamPreset.Quality, tr("Calidad"), tr("Claude planifica siempre con su mejor modelo (cuesta más), Codex escribe y Claude lo revisa.")),
            new(TeamPreset.Design, tr("Diseño"), tr("Claude se encarga del aspecto de las pantallas; Codex, de la lógica y las pruebas.")),
            new(TeamPreset.Speed, tr("Velocidad"), tr("Un solo agente y sin segunda revisión: más rápido, con más margen de error.")),
            new(TeamPreset.ClaudeOnly, tr("Solo Claude"), tr("Todo lo hace Claude (Sonnet).")),
            new(TeamPreset.CodexOnly, tr("Solo Codex"), tr("Todo lo hace Codex.")),
        ];
        selected = PresetItems.FirstOrDefault(p => p.Value.ToString() == prefs.StoredList(PrefKeys.TeamPreset)?.FirstOrDefault()) ?? PresetItems[0];
        Suggestions =
        [
            new(tr("Hacer una pregunta"), tr("Explícame con palabras sencillas qué es una rama en git y para qué sirve."), UseSuggestion),
            new(tr("Pulir un botón"), tr("Pule el botón Guardar de Save.xaml: alinea el texto y revisa el foco del teclado. Debe verse igual en claro y oscuro."), UseSuggestion),
            new(tr("Arreglar un error"), tr("Arregla el cierre inesperado al guardar en Prefs.cs: debe guardar los ajustes sin perder los anteriores. Añade una prueba."), UseSuggestion),
            new(tr("Rediseñar una pantalla"), tr("Rediseña la pantalla de ajustes en Settings.xaml: debe tener jerarquía clara, estados al pasar el ratón y foco visible."), UseSuggestion),
        ];

        Launch = new Command(StartJob);
        Cancel = new Command(() => { cancelling = true; service.Cancel(); Raise(); });
        Merge = new Command(() => { if (!mergeArmed) { mergeArmed = true; discardArmed = false; Raise(); } else _ = MergeAsync(); });
        CopyReport = new Command(() =>
        {
            try { if (EntryReport.Length > 0) System.Windows.Clipboard.SetText(EntryReport); else return; }
            catch (System.Runtime.InteropServices.COMException) { return; }   // el portapapeles estaba ocupado: no se dice «Copiado»
            reportCopied = true;
            Raise();
            copiedTimer?.Stop();
            copiedTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
            copiedTimer.Tick += (_, _) => { copiedTimer?.Stop(); reportCopied = false; Raise(); };
            copiedTimer.Start();
        });
        Discard = new Command(() => { if (!discardArmed) { discardArmed = true; mergeArmed = false; Raise(); } else _ = DiscardAsync(); });
        OpenFolder = new Command(() => { try { if (entry is { } e && Directory.Exists(e.TreeDir)) Process.Start(new ProcessStartInfo("explorer.exe", e.TreeDir) { UseShellExecute = true }); } catch { } });
        ToggleDiff = new Command(() => { showDiff = !showDiff; diffFile = null; if (showDiff) _ = LoadDiff(); Raise(); });
        SendFollowUp = new Command(SendFollowUpNow);
        PickFolder = new Command(() => Pick(path => { prefs.SetList(PrefKeys.TeamRoot, [path]); status = ""; Raise(); }));
        ToggleReviewPlan = new Command(() => { prefs.Toggle(PrefKeys.TeamReviewPlan, false); Raise(); });
        ApprovePlan = new Command(() => service.Approve(planDraft));
        RejectPlan = new Command(() => service.Reject());
        ShowTask = new Command(() => SetTab(TeamTab.Task));
        ShowHistory = new Command(() => { historyFilter = 0; SetTab(TeamTab.History); });
        ShowSchedules = new Command(() => SetTab(TeamTab.Schedules));
        ShowPlan = new Command(() => SetTab(TeamTab.Plan));
        ClearHistory = new Command(ClearHistoryNow);
        NewSchedule = new Command(() => { editor = new TeamScheduleEditor { Root = Root }; Raise(); });
        SaveSchedule = new Command(SaveEditor);
        CancelEdit = new Command(() => { editor = null; Raise(); });
        PickEditorFolder = new Command(() => Pick(path => { if (editor is { } e) e.Root = path; }));

        if (!demo)
        {
            service.Changed += () => Post(OnServiceChanged);
            service.Finished += OnFinished;
            prefs.Changed += () => Post(Raise);
            RefreshHistory();
            RefreshSchedules();
            entry = history.FirstOrDefault(h => h.State == "Open");
            RebuildRecorded();
        }
        Rebuild();
    }

    // MARK: presentación general

    /// Para avisar en el notch cuando termina una tarea (el hub lo reenvía como cualquier otro aviso).
    public event Action<string, string>? Finished;

    public override bool Always => NeedsTick;
    /// El widget necesita latir aunque el notch esté cerrado: hay algo en marcha o tareas programadas esperando su hora.
    public bool NeedsTick => !demo && (service.Busy || schedulesEnabled || Ready);

    public bool Motion => !prefs.ReducedMotion;
    public TeamViewStyle ViewStyle => Enum.TryParse<TeamViewStyle>(prefs.StoredList(PrefKeys.TeamView)?.FirstOrDefault(), out var v) ? v : TeamViewStyle.Pipeline;
    public bool IsPipeline => ViewStyle == TeamViewStyle.Pipeline;
    public bool IsTimeline => ViewStyle == TeamViewStyle.Timeline;
    public bool IsCrew => ViewStyle == TeamViewStyle.Crew;

    public bool IsTaskTab => tab == TeamTab.Task;
    public bool IsHistoryTab => tab == TeamTab.History;
    public bool IsScheduleTab => tab == TeamTab.Schedules;
    public ICommand ShowTask { get; }
    public ICommand ShowHistory { get; }
    public ICommand ShowSchedules { get; }
    public bool IsPlanTab => tab == TeamTab.Plan;
    public ICommand ShowPlan { get; }
    public string PlanTabLabel => tr("Plan");
    public IReadOnlyList<Segment> Tabs => [new(TaskTabLabel, IsTaskTab, ShowTask), new(BoardTabLabel, IsBoardTab, ShowBoard), new(PlanTabLabel, IsPlanTab, ShowPlan), new(HistoryTabLabel, IsHistoryTab, ShowHistory), new(ScheduleTabLabel, IsScheduleTab, ShowSchedules)];
    public string TaskTabLabel => tr("Tarea");
    public string HistoryTabLabel => history.Count > 0 ? $"{tr("Historial")} · {history.Count}" : tr("Historial");
    public string ScheduleTabLabel => schedules.Count > 0 ? $"{tr("Programadas")} · {schedules.Count}" : tr("Programadas");
    public string TabsHelp => tr("Secciones del equipo");

    private void SetTab(TeamTab to) { tab = to; if (to is TeamTab.History or TeamTab.Board) RefreshHistory(); if (to == TeamTab.Schedules) RefreshSchedules(); Raise(); }

    // MARK: tarea

    public IReadOnlyList<TeamPresetItem> PresetItems { get; }
    public IReadOnlyList<TeamSuggestion> Suggestions { get; }
    public TeamPresetItem SelectedPreset
    {
        get => selected;
        set
        {
            if (value is null || value == selected) return;
            selected = value;
            prefs.SetList(PrefKeys.TeamPreset, [value.Value.ToString()]);
            Rebuild();
        }
    }
    public string PresetHint => selected.Hint;
    public string PresetHelp => tr("Cómo se reparte el trabajo entre Claude y Codex");

    public string PromptHelp => tr("Qué quieres que haga el equipo");
    public string PromptHint => tr("Describe la tarea: los archivos y el criterio de hecho ayudan.");
    public bool ShowPromptHint => prompt.Length == 0;
    public string Prompt
    {
        get => prompt;
        set { prompt = value; armed = false; Rebuild(); }
    }
    public bool ShowSuggestions => Idle && prompt.Trim().Length == 0 && run is null && entry is null;
    public string SuggestionsTitle => tr("Prueba con un ejemplo");

    public bool ReviewPlanOn => prefs.Switch(PrefKeys.TeamReviewPlan, false);
    public ICommand ToggleReviewPlan { get; }
    public string ReviewPlanTitle => tr("Revisar el plan antes de implementar");
    public string ReviewPlanHelp => tr("El equipo se para tras planificar y te enseña el plan para que lo apruebes o lo edites.");

    private string Root => prefs.StoredList(PrefKeys.TeamRoot)?.FirstOrDefault() is { Length: > 0 } t && Directory.Exists(t) ? t
        : prefs.StoredList(PrefKeys.CodeRoot)?.FirstOrDefault() is { Length: > 0 } r && Directory.Exists(r) ? r : "";
    private string RootName => Path.GetFileName(Root.TrimEnd('\\', '/'));
    private bool RootOk => demo || Root.Length > 0 && TeamWorktree.IsRepo(Root);
    public string RootLabel => Root.Length == 0 ? (demo ? "isTargetSleeping" : tr("Elegir carpeta")) : RootName;
    public string RootHelp => Root.Length == 0 ? tr("Elegir la carpeta del proyecto (un repositorio de git)") : tr("Proyecto: %@. Pulsa para cambiarlo.", Root);
    public ICommand PickFolder { get; }

    public bool HasProfile => prompt.Trim().Length > 0;
    /// Lo que el router detectó de la tarea: la mitad del «por qué» del reparto.
    public IReadOnlyList<string> ContextChips => !HasProfile ? [] : new[]
    {
        KindName(profile.Kind),
        profile.Visual ? tr("visual") : null,
        profile.Files == 0 ? tr("sin archivos") : profile.Files == 1 ? tr("1 archivo") : tr("%d archivos", profile.Files),
        profile.Clear ? tr("criterio claro") : tr("falta criterio"),
        profile.Risky ? tr("riesgo") : null,
    }.OfType<string>().ToList();
    public bool ShowChips => HasProfile && Idle;
    public string PlanHint => !HasProfile ? tr("Escribe una tarea y verás qué agente hace cada paso.") : tr("Pulsa un agente o un modelo para cambiarlo antes de lanzar.");

    // MARK: pasos y avance

    /// Los pasos que se pintan: la ejecución en marcha, el plan previsto de lo que se escribe, o el de la última ejecución.
    public IReadOnlyList<TeamStepRow> Steps => run is not null ? live : HasProfile ? preview : recorded;
    public IReadOnlyList<StepState> Segments => Steps.Select(r => r.State).ToList();
    public bool HasSteps => Steps.Count > 0;
    public bool NoSteps => !HasSteps;
    /// El paso que ocupa el centro: el que trabaja, el último que terminó o el primero.
    public TeamStepRow? Focus => Steps.FirstOrDefault(r => r.Running) ?? Steps.LastOrDefault(r => r.State != StepState.Pending) ?? Steps.FirstOrDefault();
    public bool HasFocus => Focus is not null;
    public double TrackFraction => Steps.Count <= 1 ? (Steps.Any(r => r.State == StepState.Done) ? 1 : 0)
        : Math.Clamp((Steps.Count(r => r.State is StepState.Done or StepState.Failed) + (Focus?.Running == true ? 0.5 : 0)) / (Steps.Count - 1.0), 0, 1);
    /// Lo que dice la pastilla cerrada: el paso en curso y cuántos van; vacío si no hay tarea en marcha.
    private DateTime? readyAt;
    private string readyText = "";
    /// La última tarea o respuesta terminó hace poco y nadie la ha mirado todavía.
    public bool Ready => !Busy && readyAt is { } t && DateTime.Now - t < TimeSpan.FromMinutes(3);
    public string ReadyText => readyText;
    public string PillText => Ready ? readyText : !Busy ? "" : Focus is { } f ? $"{Steps.Count(r => r.State is StepState.Done or StepState.Failed)}/{Steps.Count} {f.AgentName}" : tr("Equipo trabajando…");
    public double ProgressFraction => Steps.Count == 0 ? -1 : (double)Steps.Count(r => r.State is StepState.Done or StepState.Failed) / Steps.Count;
    public bool Working => run?.Live == true || demoRun?.States.Any(s => s == StepState.Running) == true;
    public string RunTitle => run?.Title ?? "";
    public string ElapsedText => run is null ? "" : FormatSeconds((DateTime.Now - run.Started).TotalSeconds);
    public long TokenValue => run?.TotalTokens ?? entry?.Tokens ?? 0;
    public string TokenSuffix => tr("tokens");
    public bool HasTokens => TokenValue > 0;
    public string Footer => CostText;

    /// La ejecución en marcha como una sesión más de Agentes de IA (con lo último que hizo el agente), para que ese widget también enseñe que se trabaja.
    private IReadOnlyList<AgentSnapshot> TeamSessionSnapshots()
    {
        if (demo || service.Current is not { } a) return [];
        var agent = Focus?.Agent ?? TeamAgent.Sonnet;
        var log = a.Log;
        return
        [
            new AgentSnapshot("team-" + a.Id, a.Job.Root, agent == TeamAgent.Codex ? "Codex" : "Claude Code",
                service.Pending is not null ? AgentState.NeedsAttention : AgentState.Working, null, null, null, null, log.LastOrDefault()?.Text,
                log.TakeLast(6).Select(l => new AgentActivity(null, null, l.Text)).ToList(), a.LastMove.ToUniversalTime())
            { Title = tr("Equipo: %@", a.Job.Title) },
        ];
    }

    // MARK: registro en vivo — lo que hace el agente ahora, en frases cortas, y cuánto hace que se movió (para no pensar que se ha parado)
    private List<TeamLogLine>? demoLog;
    public IReadOnlyList<TeamLogLine> LiveLog
    {
        get
        {
            if (demoLog is not null) return demoLog;
            if (service.Current is not { } a) return [];
            var lines = a.Log.TakeLast(4).ToList();
            return lines.Select((l, i) => new TeamLogLine(l.Text, AgeText(l.At), i == lines.Count - 1)).ToList();
        }
    }
    public bool HasLiveLog => Busy && !HasPending && LiveLog.Count > 0;
    /// Con el registro en marcha manda él; el último informe de un paso solo se enseña cuando no hay registro.
    public bool ShowFocusMessage => !HasLiveLog;
    public string LiveTitle => tr("Ahora");
    private double SecondsSinceMove => demoLog is not null ? 4 : service.Current is { } a ? (DateTime.Now - a.LastMove).TotalSeconds : 0;
    public string LastMoveText => SecondsSinceMove < 3 ? tr("Último movimiento: ahora") : tr("Último movimiento hace %@", FormatSeconds(SecondsSinceMove));
    public bool HasLiveHint => HasLiveLog && SecondsSinceMove >= 90;
    public string LiveHint => tr("Sigue trabajando: los pasos largos pueden tardar varios minutos sin dar señales.");
    private static string AgeText(DateTime at)
    {
        var seconds = (DateTime.Now - at).TotalSeconds;
        return seconds < 3 ? tr("ahora") : tr("hace %@", FormatSeconds(seconds));
    }

    /// Cuánto de lo gastado fue de Claude (el resto, de Codex): un reparto, no una cifra de ahorro.
    public double ClaudeShare
    {
        get
        {
            if (run is { } r) { long all = r.Tokens.Sum(); return all == 0 ? 0 : (double)r.Tokens.Where((_, i) => r.Agents[i] != TeamAgent.Codex).Sum() / all; }
            if (entry is { } e) { long all = e.Steps.Sum(s => s.Tokens); return all == 0 ? 0 : (double)e.Steps.Where(s => s.Agent != nameof(TeamAgent.Codex)).Sum(s => s.Tokens) / all; }
            return 0;
        }
    }
    public bool HasShare => TokenValue > 0;
    public string ShareText => tr("Claude %d %% · Codex %d %%", (int)Math.Round(ClaudeShare * 100), 100 - (int)Math.Round(ClaudeShare * 100));
    public string CostText
    {
        get
        {
            double cost = run?.CostUsd ?? entry?.CostUsd ?? 0;
            return cost > 0 ? tr("≈ $%@ (solo lo que informa Claude)", cost.ToString("0.00", CultureInfo.InvariantCulture)) : "";
        }
    }
    public bool Busy => service.Current is not null || demoRun?.Live == true;
    public bool Idle => !Busy;
    /// Una pregunta ya respondida no necesita el plan de un solo paso (su respuesta es lo que se busca): sin él, la respuesta sale justo debajo del formulario y no al final de una página larga.
    public bool ShowPlanCard => !(Idle && entry is { State: "Answered" } && !demo);
    public int QueuedCount => demo ? 0 : service.Queued.Count;
    public bool HasQueue => QueuedCount > 0;
    public string QueueText => tr("%d en cola", QueuedCount);

    // MARK: lanzar

    public ICommand Launch { get; }
    public ICommand Cancel { get; }
    public string LaunchLabel => armed ? tr("Confirmar") : Busy ? tr("A la cola") : tr("Lanzar");
    public string LaunchHelp => armed
        ? tr("Se crea una rama aparte en %@; tu código no cambia hasta que la fusiones.", RootName)
        : Busy ? tr("Ya hay una tarea en marcha: esta se pondrá en cola") : tr("Revisar y lanzar la tarea");
    public string CancelHelp => tr("Cancelar la tarea");

    /// Una pregunta sencilla no necesita proyecto; cualquier otra tarea, sí (un repositorio de git).
    public bool IsQuestion => profile.Kind == TaskKind.Question && prompt.Trim().Length > 0;
    public bool CanLaunch => HasProfile && (IsQuestion || RootOk) && preview.Count > 0 && !demo && PlanReady;

    /// Por qué no se lanza todavía: se enseña al pulsar enviar o Intro, en vez de no hacer nada.
    private string LaunchBlocked =>
        prompt.Trim().Length == 0 ? tr("Escribe antes qué quieres que haga el equipo o qué quieres preguntar.")
        : !IsQuestion && Root.Length == 0 ? tr("Elige la carpeta de un proyecto (un repositorio de git) o escribe una pregunta sencilla: esas no la necesitan.")
        : !IsQuestion && !RootOk ? tr("%@ no es un repositorio de git. Elige otra carpeta o escribe una pregunta sencilla: esas no necesitan proyecto.", RootName)
        : Warning.Length > 0 ? Warning : tr("Ahora no se puede lanzar.");
    private bool PlanReady => !(preview.Any(r => r.Agent == TeamAgent.Codex) && (!Has("codex") || NoSession("codex")))
        && !(preview.Any(r => r.Agent != TeamAgent.Codex) && (!Has("claude") || NoSession("claude")));

    /// Un aviso útil antes de lanzar: falta un CLI o su sesión, la carpeta no es de git, o el mismo agente escribe y revisa.
    public string Warning
    {
        get
        {
            if (!HasProfile || demo) return "";
            if (Root.Length > 0 && !RootOk && !IsQuestion) return tr("%@ no es un repositorio de git.", RootName);
            if (preview.Any(r => r.Agent == TeamAgent.Codex) && !Has("codex")) return tr("Codex no está instalado: npm install -g @openai/codex");
            if (preview.Any(r => r.Agent != TeamAgent.Codex) && !Has("claude")) return tr("Claude Code no está instalado.");
            if (preview.Any(r => r.Agent == TeamAgent.Codex) && NoSession("codex")) return tr("Codex no tiene sesión iniciada: en una terminal, codex login");
            if (preview.Any(r => r.Agent != TeamAgent.Codex) && NoSession("claude")) return tr("Claude Code no tiene sesión iniciada: en una terminal, claude auth login (la app de escritorio usa otra sesión)");
            if (TeamRouter.SelfReviewed(preview.Select(r => r.Step with { Agent = r.Agent }))) return tr("El mismo agente escribe y revisa: pierdes la revisión cruzada.");
            return "";
        }
    }
    public bool HasWarning => Warning.Length > 0;

    public bool HasStatus => Status.Length > 0;
    /// «Listo para una tarea» no se enseña en la tarjeta (sobra); sí los avisos y lo que pasa con la tarea.
    public bool ShowStatusLine => !Busy && HasStatus && Status != tr("Listo para una tarea.");
    /// El estado es un aviso (falta proyecto o no es un repositorio, o se intentó lanzar sin poder): se pinta en color de aviso.
    public bool StatusIsWarning => !Busy && !demo && (status.Length > 0 ? status == blockedText : !IsQuestion && prompt.Trim().Length > 0 && (Root.Length == 0 || !RootOk));
    public string StatusGlyph => StatusIsWarning ? "\uE7BA" : IsQuestion ? "\uE946" : "\uE73E";
    public string Status => status.Length > 0 ? status
        : Working && cancelling ? tr("Cancelando…")
        : Working && HasPending ? tr("Esperando tu visto bueno")
        : Working ? (Focus is { } f ? $"{f.AgentName} · {f.RoleName}…" : tr("Trabajando…"))
        : IsQuestion ? tr("Pregunta sencilla: se responde sin proyecto ni rama.")
        : Root.Length == 0 && !demo ? tr("Elige la carpeta del proyecto.") : !RootOk ? tr("%@ no es un repositorio de git.", RootName)
        : entry is not null ? "" : tr("Listo para una tarea.");   // con un resultado delante, la tarjeta ya lo cuenta

    // MARK: plan pendiente de aprobación

    public bool HasPending => service.Pending is not null || demoRun is not null && pendingFor is not null;
    public string PendingTitle => tr("El plan espera tu visto bueno");
    public string PendingHelp => tr("Edítalo si quieres: lo que dejes aquí es lo que recibe quien implementa.");
    public string PlanDraft { get => planDraft; set { planDraft = value; } }
    // El plan se enseña con formato (títulos, listas, negritas); «Editar» lo cambia por el texto en bruto para retocarlo.
    private bool planEditing;
    public bool PlanEditing => planEditing;
    public bool PlanPreview => !planEditing;
    public string EditPlanLabel => tr(planEditing ? "Ver con formato" : "Editar");
    public string EditPlanHelp => tr(planEditing ? "Volver a ver el plan con formato" : "Editar el texto del plan antes de aprobarlo");
    private ICommand? editPlan;
    public ICommand EditPlan => editPlan ??= new Command(() => { planEditing = !planEditing; Raise(); });
    public ICommand ApprovePlan { get; }
    public ICommand RejectPlan { get; }
    public string ApproveLabel => tr("Aprobar y seguir");
    public string RejectLabel => tr("Rechazar");

    // MARK: resultado de una ejecución

    public bool HasEntry => entry is not null;
    /// El resultado se enseña en reposo; con una tarea en marcha lo que importa es su avance.
    public bool ShowResult => entry is not null && Idle;
    public string EntryTitle => entry?.Title ?? "";
    public bool EntryGood => entry?.Outcome == nameof(TeamOutcome.Done);
    public bool EntryBad => entry?.Outcome is nameof(TeamOutcome.Failed) or nameof(TeamOutcome.Cancelled);
    public bool EntryConcern => entry is not null && !EntryGood && !EntryBad;
    public string EntryGlyph => EntryGood ? "" : EntryBad ? "" : "";
    public string EntryOutcome => entry is null ? "" : OutcomeText(entry.Outcome) is { Length: > 0 } o ? char.ToUpper(o[0]) + o[1..] : "";
    public string EntryVerdict => entry?.Verdict switch { "Approved" => tr("aprobado por el otro modelo"), "Warning" => tr("con reparos del otro modelo"), "Blocked" => tr("bloqueado por el otro modelo"), _ => "" };
    public bool HasVerdict => EntryVerdict.Length > 0;
    public string EntryFiles => entry is null || entry.Files == 0 ? tr("sin archivos cambiados") : (entry.Files == 1 ? tr("1 archivo") : tr("%d archivos", entry.Files));
    public string EntryAdded => entry is null ? "" : $"+{entry.Added}";
    public string EntryRemoved => entry is null ? "" : $"−{entry.Removed}";
    public bool HasChanges => entry is { Files: > 0 };
    public string EntryWhen => entry is null ? "" : $"{WhenText(entry.Started)} · {FormatSeconds(entry.Seconds)}";
    public string EntryReport => entry?.Report ?? "";
    public bool HasReport => EntryReport.Length > 0;
    /// La tarea fue una pregunta: el resultado es el texto de la respuesta (sin rama, cambios ni fusión).
    public bool EntryIsAnswer => entry is { State: "Answered" };
    public bool ShowEntryFacts => !EntryIsAnswer;
    public bool ShowEntryAnswer => EntryIsAnswer && EntryReport.Length > 0;
    public bool ShowEntryReport => !EntryIsAnswer && HasReport;
    public bool EntryOpen => entry is { State: "Open" } e && (demo || Directory.Exists(e.TreeDir));
    public bool EntryScheduled => entry?.ScheduleId is not null;
    public string EntryState => entry?.State switch { "Merged" => tr("Fusionado en tu rama"), "Discarded" => tr("Descartado"), _ => "" };
    public bool HasEntryState => EntryState.Length > 0;
    public string EntryBranch => entry is null ? "" : $"team/{entry.BranchId}";

    public ICommand Merge { get; }
    public ICommand Discard { get; }
    /// Copia la respuesta (el Markdown tal cual, para pegarlo donde haga falta).
    public ICommand CopyReport { get; }
    public string AnswerLabel => tr("Respuesta");
    // Al copiar, el botón dice «Copiado» con un gancho verde unos segundos y vuelve a «Copiar».
    private bool reportCopied;
    private System.Windows.Threading.DispatcherTimer? copiedTimer;
    public bool ReportCopied => reportCopied;
    public string CopyReportLabel => tr(reportCopied ? "Copiado" : "Copiar");
    public string CopyReportGlyph => reportCopied ? "" : "";
    public string CopyReportHelp => tr("Copiar la respuesta");
    public ICommand OpenFolder { get; }
    public ICommand ToggleDiff { get; }
    public ICommand SendFollowUp { get; }
    public string MergeLabel => mergeArmed ? tr("¿Fusionar?") : tr("Fusionar");
    public string MergeHelp => mergeArmed ? tr("Pulsa otra vez: se fusiona la rama %@ en la rama actual de %@.", EntryBranch, entry is null ? "" : Path.GetFileName(entry.Root.TrimEnd('\\', '/'))) : tr("Traer los cambios de esta tarea a tu rama actual");
    public string DiscardLabel => discardArmed ? tr("¿Descartar?") : tr("Descartar");
    public string DiscardHelp => discardArmed ? tr("Pulsa otra vez: se borra la rama y la copia de trabajo de esta tarea.") : tr("Descartar la rama y la copia de trabajo de esta tarea");
    public string OpenLabel => tr("Abrir carpeta");
    public string OpenHelp => tr("Abrir la copia de trabajo de esta tarea en el Explorador");
    public string DiffLabel => showDiff ? tr("Ocultar cambios") : tr("Ver cambios");
    public string DiffHelp => tr("Ver qué archivos cambió el equipo y el diff, sin abrir otra app");
    public bool ShowDiff => showDiff && HasEntry;
    public string DiffText => diffText;
    public bool HasDiffText => diffText.Length > 0;
    public IReadOnlyList<TeamFileRow> ChangedFiles => files.Select(f => new TeamFileRow(f, PickFile, f.Path == diffFile)).ToList();

    public string FollowUp { get => followUp; set { followUp = value; Raise(); } }
    public bool ShowFollowUpHint => followUp.Length == 0;
    public string FollowUpHint => EntryIsAnswer ? tr("Haz otra pregunta sobre esta respuesta…") : tr("Pide un cambio sobre esta rama…");
    public string FollowUpLabel => EntryIsAnswer ? tr("Preguntar") : tr("Pedir cambios");
    public string FollowUpHelp => EntryIsAnswer ? tr("Sigue la conversación: el mismo modelo recuerda la pregunta y la respuesta anteriores")
        : tr("El equipo sigue en la misma rama, con lo ya hecho como contexto, y el otro modelo revisa");
    /// Se puede seguir una tarea con la rama abierta o una pregunta ya respondida.
    public bool ShowFollowUpBox => EntryOpen || EntryIsAnswer;
    public bool CanFollowUp => ShowFollowUpBox && followUp.Trim().Length > 0 && !demo;

    // La pregunta anterior de esta conversación (si la hay y sigue en el historial): una pastilla que la abre.
    private (string Id, TeamHistoryEntry? Parent) parentCache;
    private TeamHistoryEntry? ParentEntry
    {
        get
        {
            if (entry?.ParentId is not { } parentId) return null;
            if (parentCache.Id != entry.Id) parentCache = (entry.Id, service.Store.LoadHistory().FirstOrDefault(h => h.Id == parentId));
            return parentCache.Parent;
        }
    }
    public bool HasEntryParent => EntryIsAnswer && ParentEntry is not null;
    public string EntryParentText => ParentEntry is { } parent ? tr("En respuesta a «%@»", parent.Title.Length > 38 ? parent.Title[..38] + "…" : parent.Title) : "";
    public string EntryParentHelp => tr("Abrir la pregunta anterior de esta conversación");
    private ICommand? openParent;
    public ICommand OpenParent => openParent ??= new Command(() => { if (ParentEntry is { } parent) SelectEntry(parent); });

    // MARK: historial

    private IEnumerable<TeamHistoryEntry> FilteredHistory => history.Where(h => historyFilter switch { 1 => h.State == "Open", 2 => h.ScheduleId is not null, _ => true });
    public IReadOnlyList<TeamHistoryRow> HistoryRows => FilteredHistory.Select(h => new TeamHistoryRow(h, SelectEntry, h.Id == entry?.Id)).ToList();
    public IReadOnlyList<Segment> HistoryFilters => new[] { tr("Todas"), tr("Abiertas"), tr("Programadas") }.Select((label, i) => new Segment(label, i == historyFilter, new Command(() => { historyFilter = i; Raise(); }))).ToList();
    public string HistorySummary => history.Count == 0 ? "" : tr("%d tareas · %@ tokens · %d abiertas", history.Count, Number(history.Sum(h => h.Tokens)), history.Count(h => h.State == "Open"));
    public bool HasFilteredHistory => FilteredHistory.Any();
    public bool ShowFilteredEmpty => HasHistory && !HasFilteredHistory;
    public string FilteredEmpty => tr("Ninguna tarea cumple este filtro.");
    public bool HasHistory => history.Count > 0;
    public bool NoHistory => !HasHistory;
    public string HistoryEmpty => tr("Aquí aparecerán las tareas del equipo: lo que hicieron, lo que gastaron y su rama.");
    public ICommand ClearHistory { get; }
    public string ClearHistoryLabel => tr("Borrar historial");
    public string ClearHistoryHelp => tr("Borra la lista; no toca ninguna rama ni tu código");

    // MARK: tareas programadas

    public IReadOnlyList<TeamScheduleRow> ScheduleRows => schedules.Select(s => new TeamScheduleRow(s, EditSchedule, ToggleSchedule, RunSchedule, DeleteSchedule)).ToList();
    public bool HasSchedules => schedules.Count > 0;
    public bool NoSchedules => !HasSchedules;
    public string SchedulesEmpty => tr("Deja tareas repetidas: un informe cada mañana, una revisión semanal… El equipo las hace solo y deja cada rama lista para que la revises.");
    public string SchedulesNote => tr("Corren mientras la app está abierta (o con isTargetSleeping --team-due desde el Programador de tareas de Windows). Nunca fusionan solas y paran al llegar a su tope de tokens.");
    public ICommand NewSchedule { get; }
    public string NewScheduleLabel => tr("Nueva tarea programada");
    public string ScheduleSummary => schedules.Count == 0 ? "" : schedules.Where(s => s.Enabled && s.Next is not null).OrderBy(s => s.Next).FirstOrDefault() is { } next
        ? tr("%d activas de %d · próxima: %@", schedules.Count(s => s.Enabled), schedules.Count, WhenText(next.Next!.Value, future: true)) : tr("%d activas de %d", schedules.Count(s => s.Enabled), schedules.Count);
    public IReadOnlyList<TeamTemplate> ScheduleTemplates => [
        new(tr("Informe diario"), tr("Cada mañana, un resumen de lo que cambió"), "\uE787", () => OpenTemplate(tr("Informe diario"), tr("Resume los commits de ayer en informe.md: debe listar qué cambió y por qué."), ScheduleKind.Weekdays, "08:30")),
        new(tr("Revisión semanal"), tr("Cada lunes, una revisión de pendientes y dependencias"), "\uE81C", () => OpenTemplate(tr("Revisión semanal"), tr("Revisa las dependencias y los TODO del proyecto y deja las conclusiones en revision.md: debe proponer qué actualizar primero."), ScheduleKind.Weekly, "09:00")),
        new(tr("Pruebas por la noche"), tr("Cada noche, corre las pruebas y cuenta qué falla"), "\uE823", () => OpenTemplate(tr("Pruebas por la noche"), tr("Corre las pruebas del proyecto y escribe en pruebas.md qué falla y por qué: debe incluir el error de cada prueba."), ScheduleKind.Daily, "23:00")),
    ];
    public string MissionLabel => tr("Tarea");
    public string PlanLabel => tr("Plan");
    public string ResultLabel => tr("Resultado");
    public string WhatLabel => tr("Qué");
    public string WhenLabel => tr("Cuándo");
    public string HowLabel => tr("Cómo");
    public string NewScheduleShort => tr("Nueva");
    public string TokensUnit => tr("mil tokens");
    public string ProgressText => Steps.Count == 0 ? "" : run is not null || demoRun is not null
        ? $"{tr("%d de %d", Steps.Count(r => r.State is StepState.Done or StepState.Failed), Steps.Count)} · {ElapsedText}" : tr("%d pasos", Steps.Count);
    public bool HasEditor => editor is not null;
    public TeamScheduleEditor? Editor => editor;
    public ICommand SaveSchedule { get; }
    public ICommand CancelEdit { get; }
    public ICommand PickEditorFolder { get; }
    public IReadOnlyList<string> KindItems => [tr("Una sola vez"), tr("Cada día"), tr("De lunes a viernes"), tr("Cada semana"), tr("Cada cierto número de horas")];
    /// Los nombres completos de los días: la inicial sola no dice nada a un lector de pantalla (y T/T, S/S son ambiguas).
    public IReadOnlyList<string> DayNames => [tr("Lunes"), tr("Martes"), tr("Miércoles"), tr("Jueves"), tr("Viernes"), tr("Sábado"), tr("Domingo")];
    public IReadOnlyList<string> DayLetters => [tr("L"), tr("M"), tr("X"), tr("J"), tr("V"), tr("S"), tr("D")];
    public string EditorTitle => editor?.Id.Length > 0 ? tr("Editar tarea programada") : tr("Nueva tarea programada");
    public string SaveLabel => tr("Guardar");
    public string CancelLabel => tr("Cancelar");
    public string EditorHint => tr("Qué debe hacer el equipo cada vez (con archivos y criterio de hecho).");
    public string TimeLabel => tr("Hora (HH:mm)");
    public string EveryLabel => tr("Cada cuántas horas");
    public string TokensLabel => tr("Tope de tokens (miles)");
    public string CatchUpLabel => tr("Recuperar si la app estaba cerrada (hasta 12 h tarde)");
    public string TitleLabel => tr("Nombre (opcional)");
    public string PickFolderLabel => RootHelp;

    // MARK: ciclo de vida

    public override void Tick(DateTime now)
    {
        if (demo) return;
        if (now - lastSchedulerTick > TimeSpan.FromSeconds(15))
        {
            lastSchedulerTick = now;
            var results = scheduler.Tick(now);
            if (results.Any(r => r.Result is TeamScheduler.Launch.Started or TeamScheduler.Launch.SkippedMissed or TeamScheduler.Launch.SkippedInvalid or TeamScheduler.Launch.StorageError))
            {
                if (results.Any(r => r.Result == TeamScheduler.Launch.StorageError))
                    status = tr("No se pudo guardar la tarea programada: %@", service.Store.LastError ?? tr("error desconocido"));
                RefreshSchedules();
            }
        }
        if (run is not null) { SyncLive(); foreach (var row in live) row.Refresh(); RaiseProperty(nameof(Footer)); RaiseProperty(nameof(ElapsedText)); foreach (var name in new[] { nameof(LiveLog), nameof(HasLiveLog), nameof(ShowFocusMessage), nameof(LastMoveText), nameof(HasLiveHint) }) RaiseProperty(name); }
    }

    private void OnServiceChanged()
    {
        var current = service.Current;
        if (current is null) { run = null; liveKey = ""; cancelling = false; }
        else
        {
            if (current.Id != liveKey) { liveKey = current.Id; live = BuildLive(current.Job); planDraft = ""; pendingFor = null; cancelling = false; }
            SyncLive();
        }
        if (service.Pending is { } pending) { if (pending.Text != pendingFor) { pendingFor = pending.Text; planDraft = pending.Text; planEditing = false; } }
        else pendingFor = null;
        Raise();
    }

    private static RunView Snapshot(ActiveRun a) => new(a.Job.Steps, [.. a.States], [.. a.Agents], [.. a.Messages], [.. a.StepTokens],
        a.StepSeconds.Select((s, i) => s + (a.StepStarted[i] is { } t ? (DateTime.Now - t).TotalSeconds : 0)).ToArray(), DateTime.Now - a.Elapsed, a.CostUsd, a.Job.Title, true);

    private void SyncLive()
    {
        if (service.Current is not { } a) return;
        run = Snapshot(a);
        for (int i = 0; i < live.Count && i < a.States.Length; i++)
            live[i].Update(a.States[i], a.Agents[i], a.StepTokens[i], a.StepSeconds[i], a.StepStarted[i], a.Messages[i]);
    }

    private List<TeamStepRow> BuildLive(TeamJob job)
    {
        var rows = job.Steps.Select((s, i) => new TeamStepRow(s, i, s.Agent, _ => { }, _ => { }, Resolve) { ModelOverride = s.Model }).ToList();
        foreach (var r in rows) r.Total = rows.Count;
        return rows;
    }

    private void OnFinished(TeamHistoryEntry finished)
    {
        Post(() =>
        {
            entry = finished;
            // El notch cerrado enseña un rato que ya está lista (la pastilla y su actividad en vivo).
            readyAt = DateTime.Now;
            readyText = finished.State == "Answered" && finished.Outcome == nameof(TeamOutcome.Done) ? tr("Respuesta lista")
                : finished.Outcome == nameof(TeamOutcome.Done) ? tr("Tarea lista") : tr("Equipo: necesita tu atención");
            showDiff = false; files = []; diffText = ""; mergeArmed = discardArmed = false; followUp = ""; status = finished.StoreError ?? "";
            RefreshHistory();
            RefreshSchedules();
            RebuildRecorded();
            Raise();
        });
        if (finished.State == "Answered" && finished.Outcome == nameof(TeamOutcome.Done))
            Finished?.Invoke(tr("Equipo · respuesta lista"), finished.Report.Length > 220 ? finished.Report[..220].TrimEnd() + "…" : finished.Report);
        else
            Finished?.Invoke(finished.ScheduleId is null ? tr("Equipo") : tr("Equipo · tarea programada"), $"{finished.Title} — {OutcomeText(finished.Outcome)}");
    }

    private void RefreshHistory()
    {
        if (demo) return;
        history = service.Store.LoadHistory();
        if (service.Store.LastError is { } error) status = tr("No se pudo leer el historial: %@", error);
    }

    private void RefreshSchedules()
    {
        if (!demo)
        {
            schedules = service.Store.LoadSchedules();
            if (service.Store.LastError is { } error) status = tr("No se pudo leer la programación: %@", error);
        }
        schedulesEnabled = schedules.Any(s => s.Enabled);
    }

    private void ClearHistoryNow()
    {
        status = service.Store.ClearHistory() ? "" : tr("No se pudo guardar el historial: %@", service.Store.LastError ?? tr("error desconocido"));
        entry = null;
        RefreshHistory();
        RebuildRecorded();
        Raise();
    }

    private void SelectEntry(TeamHistoryEntry e)
    {
        entry = e;
        tab = TeamTab.Task;
        showDiff = false; files = []; diffText = ""; mergeArmed = discardArmed = false; followUp = ""; status = "";
        RebuildRecorded();
        Raise();
    }

    // Los pasos de la ejecución elegida, para pintar su tubería cuando no hay nada en marcha ni se está escribiendo.
    private void RebuildRecorded()
    {
        recorded = entry is null ? [] : entry.Steps.Select((s, i) =>
        {
            Enum.TryParse<TeamAgent>(s.Agent, out var agent);
            Enum.TryParse<TeamRole>(s.Role, out var role);
            var step = new TeamStep(agent, role, role == TeamRole.Implement, 10, "");
            var row = new TeamStepRow(step, i, agent, _ => { }, _ => { }, Resolve) { ModelOverride = s.Model };
            row.Update(s.Ok ? StepState.Done : StepState.Failed, agent, s.Tokens, s.Seconds, null, null);
            return row;
        }).ToList();
        foreach (var r in recorded) r.Total = recorded.Count;
    }

    private async Task LoadDiff()
    {
        if (entry is not { } e || !Directory.Exists(e.TreeDir)) return;
        var list = await TeamGit.ChangesAsync(e.TreeDir, e.BaseSha);
        var text = await TeamGit.DiffAsync(e.TreeDir, e.BaseSha, diffFile, 14_000);
        Post(() => { if (entry?.Id == e.Id) { files = list; diffText = text; Raise(); } });
    }

    private void PickFile(string path) { diffFile = diffFile == path ? null : path; _ = LoadDiff(); Raise(); }

    private async Task MergeAsync()
    {
        if (entry is not { } e) return;
        var error = await service.MergeAsync(e);
        Post(() =>
        {
            mergeArmed = false;
            status = error is null ? tr("Fusionado en tu rama actual.") : tr("No se pudo fusionar: %@", error);
            RefreshHistory();
            if (error is null) entry = history.FirstOrDefault(h => h.Id == e.Id) ?? e;
            Raise();
        });
    }

    private async Task DiscardAsync()
    {
        if (entry is not { } e) return;
        var error = await service.DiscardAsync(e);
        Post(() =>
        {
            discardArmed = false;
            status = error is null ? tr("Rama descartada.") : error;
            RefreshHistory();
            if (error is null) entry = history.FirstOrDefault(h => h.Id == e.Id) ?? e;
            Raise();
        });
    }

    private void SendFollowUpNow()
    {
        if (entry is not { } e || followUp.Trim().Length == 0) return;
        if (EntryIsAnswer)
        {
            service.Enqueue(TeamService.AnswerFollowUp(e, followUp, Models, service.Store.LoadHistory()));
            followUp = "";
            status = tr("Pregunta en cola.");
            Raise();
            return;
        }
        service.Enqueue(TeamService.FollowUp(e, followUp, Models));
        followUp = "";
        status = tr("Seguimiento en cola: sigue en la rama %@.", EntryBranch);
        Raise();
    }

    private void StartJob()
    {
        if (heavyPending.Count > 0) { status = tr("Primero decide qué hacer con las imágenes pesadas (Continuar o Cancelar)."); Raise(); return; }
        if (!CanLaunch) { status = blockedText = LaunchBlocked; Raise(); return; }
        armed = false;
        var images = attachments.Select(a => a.Path).ToList();
        var steps = preview.Select(r => TeamEffort.Apply(r.Step with { Agent = r.Agent, Model = r.Model?.Slug, Effort = r.Model?.Effort }, EffortLevel, Models)
            with { Images = images.Count > 0 && (r.Step.Role != TeamRole.Review || profile.Visual) ? images : null }).ToList();
        service.Enqueue(new TeamJob(prompt.Trim(), Root, selected.Value, steps, profile) { ReviewPlan = ReviewPlanOn, AttachmentDir = attachDir });
        // Las imágenes pasan a la tarea (que borra su carpeta al terminar): la caja queda limpia.
        attachments.Clear();
        attachDir = null;
        prompt = "";
        entry = null;
        status = "";
        Rebuild();
    }

    // MARK: programadas

    private void EditSchedule(TeamSchedule s) { editor = TeamScheduleEditor.From(s); Raise(); }

    private void OpenTemplate(string title, string text, ScheduleKind kind, string time)
    {
        editor = new TeamScheduleEditor { Root = Root, Title = title, Prompt = text, Kind = (int)kind, Time = time, Days = kind == ScheduleKind.Weekly ? 1 : 1 };
        Raise();
    }

    private void ToggleSchedule(TeamSchedule s)
    {
        status = service.Store.UpsertSchedule(s.Toggled()) ? "" : tr("No se pudo actualizar la tarea programada: %@", service.Store.LastError ?? tr("error desconocido"));
        RefreshSchedules();
        Raise();
    }

    private void RunSchedule(TeamSchedule s)
    {
        var result = scheduler.RunNow(s.Id, DateTime.Now);
        status = result == TeamScheduler.Launch.Started ? tr("«%@» en marcha.", s.Title.Length > 0 ? s.Title : s.Prompt)
            : result == TeamScheduler.Launch.StorageError ? tr("No se pudo guardar la tarea programada: %@", service.Store.LastError ?? tr("error desconocido"))
            : tr("No se pudo lanzar: revisa la carpeta o si ya está en marcha.");
        RefreshSchedules();
        Raise();
    }

    private void DeleteSchedule(TeamSchedule s)
    {
        status = service.Store.DeleteSchedule(s.Id) ? "" : tr("No se pudo actualizar la tarea programada: %@", service.Store.LastError ?? tr("error desconocido"));
        RefreshSchedules();
        Raise();
    }

    private void SaveEditor()
    {
        if (editor is not { Valid: true } e) return;
        if (!service.Store.UpsertSchedule(e.ToSchedule()))
        {
            status = tr("No se pudo actualizar la tarea programada: %@", service.Store.LastError ?? tr("error desconocido"));
            Raise();
            return;
        }
        editor = null;
        RefreshSchedules();
        Raise();
    }

    // MARK: plan previsto

    private void Rebuild()
    {
        if (demo && demoRun is not null) { Raise(); return; }
        profile = TeamRouter.Profile(prompt);
        IReadOnlyList<TeamStep> steps = prompt.Trim().Length == 0 ? [] : TeamRouter.Route(profile, selected.Value).Steps;
        if (!demo) steps = TeamRouter.AnswerWith(steps, Has("claude") && !NoSession("claude"), Has("codex") && !NoSession("codex"));
        var keep = preview.Count == steps.Count && preview.Select(r => r.Step.Role).SequenceEqual(steps.Select(s => s.Role)) ? preview : null;
        preview = steps.Select((s, i) => new TeamStepRow(s, i, keep is not null && keep[i].Overridden ? keep[i].Agent : s.Agent, CycleAgent, CycleModel, Resolve)
            { Overridden = keep is not null && keep[i].Overridden, ModelOverride = keep?[i].ModelOverride }).ToList();
        foreach (var r in preview) r.Total = preview.Count;
        armed = mergeArmed = discardArmed = false;
        Raise();
    }

    private void CycleAgent(TeamStepRow row)
    {
        if (!row.CanChange) return;
        // Una sola rotación con todo: Haiku, Sonnet, Opus y cada modelo de Codex, y de vuelta a lo que eligió el router.
        var next = TeamRouter.NextChoice(row.Overridden, row.Agent, row.ModelOverride ?? (row.IsCodex ? row.Model?.Slug : null), Models);
        if (next is { } choice) { row.SetAgent(choice.Agent); row.Overridden = true; row.ModelOverride = choice.Slug; }
        else { row.SetAgent(row.Step.Agent); row.Overridden = false; row.ModelOverride = null; }
        armed = false;
        Raise();
    }

    private void UseSuggestion(string text) => Prompt = text;

    // Los modelos reales de Codex se releen de su caché de vez en cuando (puede actualizarse con una versión nueva).
    public IReadOnlyList<CodexModel> Models
    {
        get
        {
            if (DateTime.UtcNow - modelsAt > TimeSpan.FromMinutes(1)) { models = CodexModels.Load(); modelsAt = DateTime.UtcNow; }
            return models;
        }
    }

    private (string Name, string Slug, string Effort)? Resolve(TeamStepRow row)
    {
        var list = Models;
        var model = list.FirstOrDefault(m => m.Slug == row.ModelOverride) ?? CodexModels.Pick(list, row.Step.Tier);
        return model is null ? null : (model.Name, model.Slug, TeamEffort.ForCodex(EffortLevel, model) ?? CodexModels.Effort(model, row.Step.Role, row.Step.Tier));
    }

    private void CycleModel(TeamStepRow row)
    {
        // Pulsar el nombre y pulsar el icono rotan por lo mismo (antes, en un paso de Claude el nombre no hacía nada visible).
        CycleAgent(row);
    }

    /// Sesión de cada CLI: se mira fuera del hilo de la interfaz y se recuerda un minuto. Sin saberlo (aún no mirado) no se bloquea.
    private bool NoSession(string cli)
    {
        if (!sessions.TryGetValue(cli, out var c) || DateTime.UtcNow - c.At > TimeSpan.FromMinutes(1))
        {
            if (checking.Add(cli))
                _ = TeamProcess.LoggedInAsync(cli).ContinueWith(t => Post(() =>
                {
                    sessions[cli] = (t.Result, DateTime.UtcNow);
                    checking.Remove(cli);
                    if (!Busy) Raise();
                }), TaskScheduler.Default);
        }
        return c.LoggedIn == false;
    }

    private bool Has(string cli)
    {
        if (!installed.TryGetValue(cli, out var c) || DateTime.UtcNow - c.At > TimeSpan.FromSeconds(15))
            installed[cli] = c = (TeamProcess.Find(cli) is not null, DateTime.UtcNow);
        return c.Found;
    }

    private static void Pick(Action<string> chosen)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = tr("Carpeta del proyecto (repositorio de git)") };
        if (dialog.ShowDialog() == true) chosen(dialog.FolderName);
    }

    // Los pasos corren en segundos o minutos fuera del hilo de la interfaz.
    private static void Post(Action action)
    {
        if (System.Windows.Application.Current?.Dispatcher is { } d && !d.CheckAccess()) d.BeginInvoke(action); else action();
    }

    // MARK: textos

    internal static string AgentName(TeamAgent a) => a switch { TeamAgent.Codex => "Codex", TeamAgent.Opus => "Opus", TeamAgent.Haiku => "Haiku", _ => "Sonnet" };
    internal static string RoleName(TeamRole r) => r switch
    {
        TeamRole.Clarify => tr("Aclarar"), TeamRole.Plan => tr("Planificar"), TeamRole.Design => tr("Diseñar"),
        TeamRole.Implement => tr("Implementar"), TeamRole.Answer => tr("Responder"), _ => tr("Revisar"),
    };
    private static string KindName(TaskKind k) => k switch
    {
        TaskKind.Bug => tr("error"), TaskKind.Feature => tr("función nueva"), TaskKind.Refactor => tr("refactor"), TaskKind.Test => tr("pruebas"),
        TaskKind.Docs => tr("documentación"), TaskKind.Migration => tr("migración"), TaskKind.Polish => tr("retoque visual"),
        TaskKind.Redesign => tr("rediseño"), TaskKind.Question => tr("pregunta sencilla"), _ => tr("tarea sin clasificar"),
    };

    internal static string OutcomeText(string? outcome) => outcome switch
    {
        "Done" => tr("hecho"), "NeedsUser" => tr("necesita tu decisión"), "Failed" => tr("falló"), "Cancelled" => tr("cancelado"),
        "Missed" => tr("omitida: la app estaba cerrada"), "Running" => tr("en marcha"), _ => "",
    };

    private static string OutcomeSentence(TeamHistoryEntry e) => e.Outcome switch
    {
        "Done" when e.Files == 0 => tr("Hecho, pero el equipo no cambió ningún archivo."),
        "Done" => tr("Hecho en la rama team/%@. Revísala y fusiónala si te gusta.", e.BranchId),
        "NeedsUser" => tr("Necesita tu decisión: %@", e.Report.Length > 0 ? e.Report : tr("la revisión sigue con reparos.")),
        "Cancelled" => tr("Cancelado."),
        _ => tr("Falló: %@", e.Report),
    };

    internal static string FormatSeconds(double seconds) => seconds < 90 ? $"{(int)seconds} s" : seconds < 3600 ? $"{(int)(seconds / 60)} min {(int)seconds % 60:00} s" : tr("%d h %d min", (int)(seconds / 3600), (int)(seconds % 3600 / 60));

    internal static string WhenText(DateTime when, bool future = false)
    {
        var culture = new CultureInfo(Effective == "en" ? "en-US" : "es-ES");
        var days = (when.Date - DateTime.Today).Days;
        string time = when.ToString("HH:mm", culture);
        return days switch
        {
            0 => $"{tr("hoy")} {time}",
            -1 => $"{tr("ayer")} {time}",
            1 => $"{tr("mañana")} {time}",
            _ => $"{when.ToString(Math.Abs(days) < 7 ? "ddd" : "d MMM", culture)} {time}",
        };
    }

    internal static string FrequencyText(TeamSchedule s)
    {
        var culture = new CultureInfo(Effective == "en" ? "en-US" : "es-ES");
        return s.Kind switch
        {
            ScheduleKind.Once => tr("una sola vez, %@", s.At is { } at ? WhenText(at, future: true) : s.Time),
            ScheduleKind.Daily => tr("cada día a las %@", s.Time),
            ScheduleKind.Weekdays => tr("de lunes a viernes a las %@", s.Time),
            ScheduleKind.Weekly => tr("%@ a las %@", string.Join(", ", Enumerable.Range(0, 7).Where(i => (s.DaysMask & (1 << i)) != 0)
                .Select(i => culture.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)((i + 1) % 7)))), s.Time),
            _ => tr("cada %d h", s.EveryHours),
        };
    }
}
