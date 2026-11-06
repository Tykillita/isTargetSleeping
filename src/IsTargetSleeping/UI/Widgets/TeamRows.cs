using System.ComponentModel;
using System.Windows.Input;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

public enum TeamViewStyle { Pipeline, Timeline, Crew }
public enum TeamTab { Task, History, Schedules, Board, Plan }

/// Un agente en miniatura (la tubería de una tarea ya hecha, en el historial) y un día de la semana (el calendario de una programada).
public sealed record TeamMini(TeamAgent Agent, bool Ok);
public sealed record TeamDay(string Letter, bool On);

/// Una plantilla para empezar una tarea programada sin rellenar todo a mano.
public sealed class TeamTemplate(string title, string help, string glyph, Action open)
{
    public string Title { get; } = title;
    public string Help { get; } = help;
    public string Glyph { get; } = glyph;
    public ICommand Open { get; } = new Command(open);
}

/// Un reparto elegible en la lista (el widget lo guarda por su nombre).
public sealed record TeamPresetItem(TeamPreset Value, string Name, string Hint)
{
    /// Lo que se ve con la lista cerrada: solo el nombre (el nombre accesible y el tooltip dicen que es el reparto).
    public string Label => Name;
}

/// Lo que se ve de una ejecución, venga de la cola real o de una demostración (capturas): la interfaz pinta esto y no sabe de dónde sale.
public sealed record RunView(IReadOnlyList<TeamStep> Steps, StepState[] States, TeamAgent[] Agents, string?[] Messages, long[] Tokens, double[] Seconds,
    DateTime Started, double CostUsd, string Title, bool Live)
{
    public long TotalTokens => Tokens.Sum();
}

/// Un paso del plan, tal como lo ve la interfaz: quién lo hace (se puede cambiar antes de lanzar), por qué y cómo va.
public sealed class TeamStepRow : INotifyPropertyChanged
{
    private readonly Action<TeamStepRow> cycle, cycleModel;
    private readonly Func<TeamStepRow, (string Name, string Slug, string Effort)?> resolve;
    private ICommand? cycleCommand, modelCommand;
    private double seconds;
    private int count = 1;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    public TeamStepRow(TeamStep step, int index, TeamAgent agent, Action<TeamStepRow> cycle, Action<TeamStepRow> cycleModel,
        Func<TeamStepRow, (string Name, string Slug, string Effort)?> resolve)
    {
        (Step, Index, Agent) = (step, index, agent);
        (this.cycle, this.cycleModel, this.resolve) = (cycle, cycleModel, resolve);
    }

    public int Index { get; }
    /// «Paso 2 de 6»: la posición y el total los pone la lista.
    public int Total { set { count = value; Raise(); } }
    public TeamStep Step { get; }
    public TeamAgent Agent { get; private set; }
    public StepState State { get; private set; }
    public Verdict? Judged { get; private set; }
    public long Tokens { get; private set; }
    public string? Message { get; private set; }
    public bool Overridden { get; set; }
    /// El modelo de Codex elegido a mano (su slug); null = el que corresponde al nivel del paso.
    public string? ModelOverride { get; set; }
    /// Se pinta el momento en que empezó el paso en marcha (para el reloj en vivo).
    public DateTime? RunningSince { get; private set; }

    public ICommand Cycle => cycleCommand ??= new Command(() => cycle(this));
    public ICommand CycleModel => modelCommand ??= new Command(() => cycleModel(this));
    public (string Name, string Slug, string Effort)? Model => IsCodex ? resolve(this) : null;

    public bool Pending => State == StepState.Pending;
    public bool Running => State == StepState.Running;
    public bool Done => State == StepState.Done && Judged is null or Verdict.Approved;
    public bool Failed => State == StepState.Failed;
    public bool Concern => State == StepState.Done && Judged is Verdict.Warning or Verdict.Blocked;
    public bool IsCodex => Agent == TeamAgent.Codex;
    public bool CanChange => State == StepState.Pending;
    public bool HasModel => IsCodex;
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message) && Step.Role != TeamRole.Review;
    public string ReportText => Message ?? "";
    public int Position => Index + 1;
    public bool IsFirst => Index == 0;
    public bool IsLast => Position == count;
    /// El hilo de la tubería se enciende hacia dentro (desde que empieza el paso) y hacia fuera (cuando termina).
    public bool LeftLit => State != StepState.Pending;
    public bool RightLit => State is StepState.Done or StepState.Failed;
    /// Potencia de Codex en 1–3 puntos (Ligero, Estándar, Frontera); los pasos de Claude no los llevan.
    public int Pips => IsCodex ? (int)Step.Tier + 1 : 0;
    public double Fraction => State switch
    {
        StepState.Done or StepState.Failed => 1,
        StepState.Running => Math.Clamp((Seconds) / Math.Max(30, Step.TimeoutMinutes * 60.0), 0.04, 0.96),
        _ => 0,
    };

    public double Seconds => seconds + (RunningSince is { } t ? (DateTime.Now - t).TotalSeconds : 0);
    public string AgentName => TeamWidget.AgentName(Agent);
    public string RoleName => TeamWidget.RoleName(Step.Role);
    public string Why => tr(Step.Why);
    public string TimeText => State == StepState.Pending ? "" : TeamWidget.FormatSeconds(Seconds);
    public string TokensText => Tokens > 0 ? tr("%@ tokens", Number(Tokens)) : "";
    public string ModelLabel => Model is { } m ? $"{m.Name} · {EffortName(m.Effort)}" : tr("modelo por defecto");
    private static string EffortName(string effort) => effort.ToLowerInvariant() switch { "low" => tr("bajo"), "medium" => tr("medio"), "high" => tr("alto"), "xhigh" => tr("muy alto"), _ => effort };
    public string ModelName => Model?.Name ?? "";
    public string ModelHelp => tr("Cambiar el modelo de Codex para «%@» (ahora %@)", RoleName, ModelLabel);
    public string ChangeHelp => tr("Cambiar quién hace «%@» (ahora %@)", RoleName, AgentName);
    public string Help => tr("%@ · %@: %@. %@", RoleName, IsCodex ? $"{AgentName} ({ModelLabel})" : AgentName, StateText, Why);
    public string NodeName => tr("Paso %d de %d: %@, %@, %@", Position, count, RoleName, IsCodex ? $"{AgentName} {ModelName}" : AgentName, StateText);
    public string StateText => State switch
    {
        StepState.Running => tr("trabajando"), StepState.Failed => tr("falló"),
        StepState.Done => Judged switch { Verdict.Approved => tr("aprobado"), Verdict.Warning => tr("con reparos"), Verdict.Blocked => tr("bloqueado"), _ => tr("hecho") },
        _ => tr("pendiente"),
    };
    /// El glifo de Segoe Fluent Icons de cada estado (para quien lo lee sin colores).
    public string Glyph => State switch
    {
        StepState.Running => "", StepState.Failed => "",
        StepState.Done => Judged is Verdict.Warning or Verdict.Blocked ? "" : "",
        _ => "",
    };

    public void SetAgent(TeamAgent value) { Agent = value; Raise(); }

    public void Reset()
    {
        (State, Judged, Tokens, Message, RunningSince, seconds) = (StepState.Pending, null, 0, null, null, 0);
        Raise();
    }

    /// Pone al día el paso con lo que informa la ejecución.
    public void Update(StepState state, TeamAgent agent, long tokens, double secondsDone, DateTime? runningSince, string? message)
    {
        (State, Agent, Tokens, seconds, RunningSince, Message) = (state, agent, tokens, secondsDone, runningSince, message);
        Judged = Step.Role == TeamRole.Review && state == StepState.Done && message is not null ? TeamRun.ParseVerdict(message) : null;
        Raise();
    }

    public void Refresh() { if (Running) Raise(); }
}

/// Una fila del historial.
public sealed class TeamHistoryRow(TeamHistoryEntry entry, Action<TeamHistoryEntry> select, bool selected)
{
    public TeamHistoryEntry Entry { get; } = entry;
    public ICommand Select { get; } = new Command(() => select(entry));
    public bool IsSelected { get; } = selected;
    public string Title => entry.Title;
    public bool Scheduled => entry.ScheduleId is not null;
    public bool Open => entry.State == "Open";
    public bool Good => entry.Outcome == nameof(TeamOutcome.Done);
    public bool Bad => entry.Outcome is nameof(TeamOutcome.Failed) or nameof(TeamOutcome.Cancelled);
    public bool Concern => !Good && !Bad;
    public string Glyph => Good ? "" : Bad ? "" : "";
    public string When => TeamWidget.WhenText(entry.Started);
    public string Files => entry.Files == 0 ? tr("sin cambios") : entry.Files == 1 ? tr("1 archivo") : tr("%d archivos", entry.Files);
    public bool HasChanges => entry.Files > 0;
    public string Added => $"+{entry.Added}";
    public string Removed => $"−{entry.Removed}";
    public string Tokens => entry.Tokens > 0 ? tr("%@ tokens", Number(entry.Tokens)) : "";
    public bool HasTokens => entry.Tokens > 0;
    public IReadOnlyList<TeamMini> Minis => entry.Steps.Select(s => new TeamMini(Enum.TryParse<TeamAgent>(s.Agent, out var a) ? a : TeamAgent.Sonnet, s.Ok)).ToList();
    public string StateText => entry.State switch { "Merged" => tr("Fusionada"), "Discarded" => tr("Descartada"), _ => tr("Abierta") };
    public bool IsOpenState => entry.State == "Open";
    public bool IsMerged => entry.State == "Merged";
    public bool IsDiscarded => entry.State == "Discarded";
    public string Detail => string.Join(" · ", new[] { Files, HasTokens ? Tokens : null }.OfType<string>());
    public string Help => tr("%@ · %@ · %@", entry.Title, TeamWidget.OutcomeText(entry.Outcome), When);
}

/// Un archivo cambiado por una rama del equipo.
public sealed class TeamFileRow(FileChange change, Action<string> pick, bool selected)
{
    public string Path => change.Path;
    public string Name => System.IO.Path.GetFileName(change.Path);
    public string Folder => System.IO.Path.GetDirectoryName(change.Path)?.Replace('\\', '/') ?? "";
    public string Stat => $"+{change.Added} −{change.Removed}";
    public bool IsSelected { get; } = selected;
    public ICommand Pick { get; } = new Command(() => pick(change.Path));
    public string Help => tr("Ver el cambio de %@", change.Path);
}

/// Un ejemplo para empezar: rellena la tarea y enseña cómo reparte el router.
public sealed class TeamSuggestion(string title, string prompt, Action<string> use)
{
    public string Title { get; } = title;
    public ICommand Use { get; } = new Command(() => use(prompt));
    public string Help => tr("Usar de ejemplo: %@", prompt);
}

/// Una fila de tareas programadas.
public sealed class TeamScheduleRow(TeamSchedule schedule, Action<TeamSchedule> edit, Action<TeamSchedule> toggle, Action<TeamSchedule> run, Action<TeamSchedule> delete)
{
    public TeamSchedule Schedule { get; } = schedule;
    public ICommand Edit { get; } = new Command(() => edit(schedule));
    public ICommand Toggle { get; } = new Command(() => toggle(schedule));
    public ICommand RunNow { get; } = new Command(() => run(schedule));
    public ICommand Delete { get; } = new Command(() => delete(schedule));
    public string Title => schedule.Title.Length > 0 ? schedule.Title : schedule.Prompt.Split('\n', 2)[0];
    public bool Enabled => schedule.Enabled;
    public string Frequency => TeamWidget.FrequencyText(schedule);
    public string Next => schedule.PausedByFailures ? tr("pausada tras %d fallos seguidos", TeamSchedule.FailureLimit) : !schedule.Enabled ? tr("apagada") : schedule.Next is { } n ? tr("próxima: %@", TeamWidget.WhenText(n, future: true)) : tr("sin próxima");
    public string Last => schedule.LastRun is { } l ? tr("última: %@ · %@", TeamWidget.WhenText(l), TeamWidget.OutcomeText(schedule.LastOutcome)) : tr("todavía no se ha ejecutado");
    public string Project => System.IO.Path.GetFileName(schedule.Root.TrimEnd('\\', '/'));
    public bool Bad => schedule.LastOutcome is "Failed" or "Missed" or "Cancelled";
    public bool LastGood => schedule.LastOutcome == "Done";
    public bool ShowDays => schedule.Kind is ScheduleKind.Daily or ScheduleKind.Weekdays or ScheduleKind.Weekly;
    public IReadOnlyList<TeamDay> Days => [.. new[] { tr("L"), tr("M"), tr("X"), tr("J"), tr("V"), tr("S"), tr("D") }.Select((letter, i) =>
        new TeamDay(letter, schedule.Kind == ScheduleKind.Daily || schedule.Kind == ScheduleKind.Weekdays && i < 5 || schedule.Kind == ScheduleKind.Weekly && (schedule.DaysMask & (1 << i)) != 0))];
    public string NextLabel => tr("Próxima");
    public string NextWhen => schedule.PausedByFailures ? tr("pausada tras %d fallos", TeamSchedule.FailureLimit) : !schedule.Enabled ? tr("apagada") : schedule.Next is { } n ? TeamWidget.WhenText(n, future: true) : tr("sin próxima");
    public string LastText => schedule.LastRun is { } l ? $"{TeamWidget.WhenText(l)} · {TeamWidget.OutcomeText(schedule.LastOutcome)}" : tr("todavía no se ha ejecutado");
    public string LastLabel => tr("Última");
    public string ToggleHelp => schedule.Enabled ? tr("Apagar «%@»", Title) : tr("Encender «%@»", Title);
    public string RunHelp => tr("Ejecutar «%@» ahora", Title);
    public string EditHelp => tr("Editar «%@»", Title);
    public string DeleteHelp => tr("Borrar «%@»", Title);
}

/// El formulario para crear o cambiar una tarea programada.
public sealed class TeamScheduleEditor : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private string title = "", prompt = "", time = "09:00", every = "6", tokensK = "300", root = "";
    private int kind, preset;
    private int days = 1;
    private bool catchUp;

    public string Id { get; init; } = "";
    public DateTime Created { get; init; } = DateTime.Now;
    public DateTime? LastRun { get; init; }
    public string? LastOutcome { get; init; }
    public int ConsecutiveFailures { get; init; }
    public string Title { get => title; set { title = value; Raise(nameof(Title)); } }
    public string Prompt { get => prompt; set { prompt = value; Raise(nameof(Prompt)); Raise(nameof(Valid)); } }
    public string Time { get => time; set { time = value; Raise(nameof(Time)); Raise(nameof(Valid)); } }
    public string Every { get => every; set { every = value; Raise(nameof(Every)); Raise(nameof(Valid)); } }
    public string TokensK { get => tokensK; set { tokensK = value; Raise(nameof(TokensK)); Raise(nameof(Valid)); } }
    public string Root { get => root; set { root = value; Raise(nameof(Root)); Raise(nameof(RootLabel)); Raise(nameof(Valid)); } }
    public int Kind { get => kind; set { kind = value; Raise(nameof(Kind)); Raise(nameof(KindSegments)); Raise(nameof(ShowTime)); Raise(nameof(ShowEvery)); Raise(nameof(ShowDays)); Raise(nameof(Valid)); } }
    public int Preset { get => preset; set { preset = value; Raise(nameof(Preset)); } }
    public bool CatchUp { get => catchUp; set { catchUp = value; Raise(nameof(CatchUp)); } }
    public int Days { get => days; set { days = value; for (int i = 0; i < 7; i++) Raise($"Day{i}"); Raise(nameof(Valid)); } }

    public bool Day0 { get => Has(0); set => Set(0, value); }
    public bool Day1 { get => Has(1); set => Set(1, value); }
    public bool Day2 { get => Has(2); set => Set(2, value); }
    public bool Day3 { get => Has(3); set => Set(3, value); }
    public bool Day4 { get => Has(4); set => Set(4, value); }
    public bool Day5 { get => Has(5); set => Set(5, value); }
    public bool Day6 { get => Has(6); set => Set(6, value); }
    private bool Has(int i) => (days & (1 << i)) != 0;
    private void Set(int i, bool on) { days = on ? days | (1 << i) : days & ~(1 << i); Raise($"Day{i}"); Raise(nameof(Valid)); }

    public ICommand ToggleCatchUp => new Command(() => CatchUp = !CatchUp);
    /// Las cinco frecuencias como pastillas elegibles (más claras que un desplegable).
    public IReadOnlyList<Segment> KindSegments => new[] { tr("Una vez"), tr("Cada día"), tr("Laborables"), tr("Semanal"), tr("Cada N h") }
        .Select((label, i) => new Segment(label, i == kind, new Command(() => Kind = i))).ToList();
    public ScheduleKind KindValue => (ScheduleKind)Math.Clamp(kind, 0, 4);
    public bool ShowTime => KindValue is ScheduleKind.Daily or ScheduleKind.Weekdays or ScheduleKind.Weekly or ScheduleKind.Once;
    public bool ShowEvery => KindValue == ScheduleKind.EveryHours;
    public bool ShowDays => KindValue == ScheduleKind.Weekly;
    public string RootLabel => root.Length == 0 ? tr("Elegir carpeta") : System.IO.Path.GetFileName(root.TrimEnd('\\', '/'));
    public bool Valid => prompt.Trim().Length > 0 && root.Length > 0
        && (!ShowTime || TimeSpan.TryParse(time, out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
        && (!ShowEvery || int.TryParse(every, out var h) && h is >= 1 and <= 168)
        && (!ShowDays || days != 0) && long.TryParse(tokensK, out var k) && k is >= 10 and <= 5000;

    public static TeamScheduleEditor From(TeamSchedule s) => new()
    {
        Id = s.Id, Created = s.Created, LastRun = s.LastRun, LastOutcome = s.LastOutcome, ConsecutiveFailures = s.ConsecutiveFailures,
        title = s.Title, prompt = s.Prompt, root = s.Root, time = s.Time, every = s.EveryHours.ToString(), tokensK = (s.MaxTokens / 1000).ToString(),
        kind = (int)s.Kind, days = s.DaysMask, catchUp = s.CatchUp, preset = Math.Max(0, (int)(Enum.TryParse<TeamPreset>(s.Preset, out var p) ? p : TeamPreset.Auto)),
    };

    public TeamSchedule ToSchedule(bool enabled = true)
    {
        var kindValue = KindValue;
        TimeSpan.TryParse(time, out var t);
        return new TeamSchedule
        {
            Id = Id.Length > 0 ? Id : Guid.NewGuid().ToString("N")[..8], Title = title.Trim(), Prompt = prompt.Trim(), Root = root,
            Preset = ((TeamPreset)Math.Clamp(preset, 0, 6)).ToString(), Kind = kindValue, Time = $"{(int)t.TotalHours:00}:{t.Minutes:00}", DaysMask = days,
            EveryHours = int.TryParse(every, out var h) ? Math.Clamp(h, 1, 168) : 6, CatchUp = catchUp, MaxTokens = (long.TryParse(tokensK, out var k) ? k : 300) * 1000,
            At = kindValue == ScheduleKind.Once ? NextTime(t) : null, Enabled = enabled, Created = Created, LastRun = LastRun, LastOutcome = LastOutcome, ConsecutiveFailures = ConsecutiveFailures,
        };
    }

    // «Una sola vez» a las HH:mm: hoy si todavía no pasó, mañana si ya pasó.
    private static DateTime NextTime(TimeSpan t) => DateTime.Today.Add(t) > DateTime.Now ? DateTime.Today.Add(t) : DateTime.Today.AddDays(1).Add(t);
}
