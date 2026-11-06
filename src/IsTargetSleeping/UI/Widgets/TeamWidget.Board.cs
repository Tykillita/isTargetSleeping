using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

public enum BoardTone { Quiet, Live, Good, Warn, Bad }

/// Una línea del detalle de una tarjeta desplegada: un paso (quién, qué) y cuánto tardó, o cuándo ocurrió.
/// Una línea del registro en vivo de la ejecución: qué hace el agente y hace cuánto.
public sealed record TeamLogLine(string Text, string When, bool IsLast);

public sealed record TeamBoardLine(string Text, string When, bool Current);

/// Un botón de acción de una tarjeta desplegada: la principal va a ancho completo y las demás, en una fila de igual ancho debajo.
public sealed record TeamBoardAction(string Label, ICommand Command, string Help, bool IsPrimary = false, bool IsDanger = false, string? Glyph = null, bool IconOnly = false, bool Divider = false)
{
    /// Solo icono: el texto queda en el tooltip y en el nombre accesible. Con icono y texto (la acción principal) se pintan los dos.
    public string? Text => IconOnly ? null : Label;
}

/// Una tarjeta del tablero: una tarea (en marcha, en cola, esperando una decisión o ya terminada) con su agente y su estado. Al pulsarla se
/// despliega con el detalle de los pasos y las acciones que tocan: aprobar o rechazar un plan, reintentar una tarea fallida, abrirla.
public sealed class TeamBoardCard
{
    public TeamBoardCard(string title, string excerpt, TeamAgent agent, StepState state, bool concern, string status, string glyph, BoardTone tone, string meta, string help, ICommand toggle)
    {
        Title = title; Excerpt = excerpt; Agent = agent; State = state; Concern = concern; Status = status; Glyph = glyph; Tone = tone; Meta = meta; Help = help; Toggle = toggle;
    }

    public string Title { get; }
    public string Excerpt { get; }
    public bool HasExcerpt => Excerpt.Length > 0 && !IsExpanded;
    public TeamAgent Agent { get; }
    public StepState State { get; }
    public bool Concern { get; }
    public string Status { get; }
    public string Glyph { get; }
    public BoardTone Tone { get; }
    public bool IsLive => Tone == BoardTone.Live;
    public bool IsGood => Tone == BoardTone.Good;
    public bool IsWarn => Tone == BoardTone.Warn;
    public bool IsBad => Tone == BoardTone.Bad;
    public string Meta { get; }
    public bool HasMeta => Meta.Length > 0;
    /// Cuándo terminó (UTC): ordena «Hecho» de la más reciente a la más antigua.
    public DateTime When { get; init; }
    public string Help { get; }
    public ICommand Toggle { get; }

    // Lo que enseña al desplegarse.
    public bool IsExpanded { get; init; }
    public string AgentLabel { get; init; } = "";
    /// El texto largo (el plan que espera, el informe o la respuesta) y los pasos con sus tiempos.
    public string DetailText { get; init; } = "";
    public bool HasDetailText => DetailText.Length > 0;
    public IReadOnlyList<TeamBoardLine> Lines { get; init; } = [];
    public bool HasLines => Lines.Count > 0;
    public string Chevron => IsExpanded ? "" : "";

    // Acciones (null = no toca en esta tarjeta).
    public ICommand? Open { get; init; }
    public ICommand? Approve { get; init; }
    public ICommand? Reject { get; init; }
    public ICommand? Retry { get; init; }
    public ICommand? Cancel { get; init; }
    public bool ShowOpen => Open is not null;
    public bool ShowApprove => Approve is not null;
    public bool ShowReject => Reject is not null;
    public bool ShowRetry => Retry is not null;
    public bool ShowCancel => Cancel is not null;
    public string OpenLabel { get; init; } = tr("Abrir");
    public string OpenHelp { get; init; } = "";
    public string ApproveLabel => tr("Aprobar");
    public string RejectLabel => tr("Rechazar");
    public string RetryLabel => tr("Reintentar");
    public string CancelLabel => tr("Cancelar");

    public IReadOnlyList<TeamBoardAction> PrimaryActions => new[]
    {
        Approve is null ? null : new TeamBoardAction(ApproveLabel, Approve, ApproveLabel, true, Glyph: "\uE73E"),
        Retry is null ? null : new TeamBoardAction(RetryLabel, Retry, RetryLabel, true, Glyph: "\uE72C"),
    }.OfType<TeamBoardAction>().ToList();

    // Junto a una acción principal las secundarias van con icono, agrupadas en una pastilla (en la columna estrecha no caben tres botones
    // con texto); si son las únicas, con texto.
    public IReadOnlyList<TeamBoardAction> SecondaryActions
    {
        get
        {
            bool icons = Approve is not null || Retry is not null;
            return new[]
            {
                Open is null ? null : new TeamBoardAction(OpenLabel, Open, OpenHelp.Length > 0 ? OpenHelp : OpenLabel, Glyph: icons ? (Approve is not null ? "\uE70F" : "\uE8A7") : null, IconOnly: icons),
                Reject is null ? null : new TeamBoardAction(RejectLabel, Reject, RejectLabel, false, true, icons ? "\uE711" : null, icons),
                Cancel is null ? null : new TeamBoardAction(CancelLabel, Cancel, CancelLabel, false, true, icons ? "\uE711" : null, icons),
            }.OfType<TeamBoardAction>().Select((action, i) => action with { Divider = i > 0 }).ToList();
        }
    }
    public bool HasPrimary => PrimaryActions.Count > 0;
    public bool HasSecondary => SecondaryActions.Count > 0;
}

/// Una columna del tablero (o un grupo de la lista): lo que necesita al usuario, lo que está en marcha y lo ya hecho.
public sealed class TeamBoardColumn(string title, string glyph, string tone, string empty, IReadOnlyList<TeamBoardCard> cards, int? total = null, string? moreLabel = null, ICommand? more = null)
{
    public string Title { get; } = title;
    public string Glyph { get; } = glyph;
    /// «need», «run» o «done»: el color de su cabecera.
    public string Tone { get; } = tone;
    public IReadOnlyList<TeamBoardCard> Cards { get; } = cards;
    /// El total de la columna, aunque solo se enseñen las más recientes.
    public int Count => total ?? Cards.Count;
    /// «Ver más» / «Ver menos»: solo cuando la columna tiene más tarjetas de las que enseña por defecto.
    public bool HasMore => more is not null;
    public string MoreLabel { get; } = moreLabel ?? "";
    public ICommand? More { get; } = more;
    public bool HasCards => Cards.Count > 0;
    public bool NoCards => Cards.Count == 0;
    public string EmptyText { get; } = empty;
}

/// El tablero del equipo: todas las tareas de un vistazo, agrupadas por lo que toca hacer con ellas.
public sealed partial class TeamWidget
{
    private bool boardList;
    private ICommand? showBoard;
    // Las tarjetas desplegadas (por identificador): se recuerdan aunque el tablero se vuelva a pintar.
    private readonly HashSet<string> expandedCards = [];
    // «Hecho» enseña las 5 más recientes; «Ver más» las despliega todas.
    private const int DoneShown = 5;
    private bool doneAll;
    private ICommand? toggleDone;

    public bool IsBoardTab => tab == TeamTab.Board;
    public ICommand ShowBoard => showBoard ??= new Command(() => SetTab(TeamTab.Board));
    public string BoardTabLabel => tr("Tablero");
    public bool BoardIsList => boardList;
    public bool BoardIsBoard => !boardList;
    public string BoardViewsHelp => tr("Cómo ver el tablero");

    // Sesiones de Claude Code y Codex que no se lanzaron desde aquí (terminal, app de Codex, Claude Code en escritorio o en el editor): las mismas
    // que enseña el widget AI Agents, que lee sus registros locales en solo lectura y solo si el usuario lo activó.
    private List<AgentSnapshot> demoSessions = [];
    public bool BoardOfferExternal => !demo && agents is not null && !agents.Enabled;
    public string ExternalOfferText => tr("Para ver aquí también las sesiones de Claude Code y Codex que lances desde otras apps, activa la lectura de sus registros locales (solo lectura, nada sale de tu PC).");
    public string ExternalOfferLabel => tr("Activar");
    public ICommand EnableExternal => enableExternal ??= new Command(() => { prefs.SetSwitch(PrefKeys.AgentsEnabled, true); Raise(); });
    private ICommand? enableExternal;

    private IEnumerable<AgentSnapshot> ExternalSessions
    {
        get
        {
            IEnumerable<AgentSnapshot> all = demo ? demoSessions : agents is { Enabled: true } a ? a.Sessions.Concat(a.RecentSessions) : [];
            return all.Where(s => !TeamService.OwnsPath(s.Project)).GroupBy(AgentLogs.Key).Select(g => g.First()).OrderByDescending(s => s.LastActivity);
        }
    }

    private static void OpenInExplorer(string folder)
    {
        try { if (Directory.Exists(folder)) Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true }); } catch { }
    }
    public IReadOnlyList<Segment> BoardViews =>
    [
        new(tr("Tablero"), !boardList, new Command(() => { boardList = false; Raise(); })),
        new(tr("Lista"), boardList, new Command(() => { boardList = true; Raise(); })),
    ];

    public string BoardSummary
    {
        get
        {
            var columns = BoardColumns;
            return tr("%d por atender · %d en marcha · %d hechas", columns[0].Count, columns[1].Count, columns[2].Count);
        }
    }

    // «Hecho»: de la más reciente a la más antigua, 5 por defecto y todas con «Ver más».
    private IReadOnlyList<TeamBoardCard> DoneCards(List<TeamBoardCard> done)
    {
        var sorted = done.OrderByDescending(c => c.When);
        return (doneAll ? sorted : sorted.Take(DoneShown)).ToList();
    }
    private string? DoneMoreLabel(int total) => total <= DoneShown ? null : doneAll ? tr("Ver menos") : tr("Ver más (%d)", total - DoneShown);
    private ICommand? DoneMore(int total) => total <= DoneShown ? null : toggleDone ??= new Command(() => { doneAll = !doneAll; Raise(); });

    private ICommand ToggleCard(string key) => new Command(() => { if (!expandedCards.Remove(key)) expandedCards.Add(key); Raise(); });

    /// Un nuevo intento de una tarea que falló: se lanza otra vez con su mismo texto, proyecto y reparto.
    private void Retry(TeamHistoryEntry e)
    {
        if (demo) return;
        var task = e.Task.Length > 0 ? e.Task : e.Title;
        service.Enqueue(TeamService.RetryJob(e, task, Models, ReviewPlanOn));
        expandedCards.Remove(e.Id);
        entry = null; tab = TeamTab.Task; status = "";
        Rebuild();
    }

    /// Tiempos cortos para las líneas del detalle: «45 s», «1 min 30 s» no caben junto al nombre del paso.
    private static string K(long tokens) => (tokens / 1000.0).ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo(Effective == "es" ? "es-ES" : "en-US")) + "k";
    private static string Compact(double seconds) => FormatSeconds(seconds);

    /// El resumen de una tarjeta es una línea de texto sin formato: el Markdown de la respuesta («**negrita**», listas, enlaces) se queda en el detalle.
    internal static string PlainText(string markdown)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(markdown, @"\[([^\]]+)\]\([^)]*\)", "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^\s{0,3}(#{1,6}\s+|[-*]\s+|\d+\.\s+)", "", System.Text.RegularExpressions.RegexOptions.Multiline);
        return text.Replace("**", "").Replace("__", "").Replace("`", "").Trim();
    }

    private static string StepRole(string role) => Enum.TryParse<TeamRole>(role, out var r) ? RoleName(r) : role;

    public IReadOnlyList<TeamBoardColumn> BoardColumns
    {
        get
        {
            var need = new List<TeamBoardCard>();
            var running = new List<TeamBoardCard>();
            var done = new List<TeamBoardCard>();
            var goTask = ShowTask;

            // Mientras el plan espera el visto bueno, la tarea ya sale en «Necesita tu atención»: no se repite como «En marcha».
            if (Busy && !HasPending)
            {
                var agent = Focus?.Agent ?? TeamAgent.Sonnet;
                running.Add(new(RunTitle, LiveLog.LastOrDefault()?.Text ?? Status, agent, StepState.Running, false, ProgressText.Length > 0 ? ProgressText : tr("Trabajando…"), "", BoardTone.Live,
                    TokenValue > 0 ? K(TokenValue) : "", tr("Abrir la tarea en marcha"), ToggleCard("run"))
                {
                    IsExpanded = expandedCards.Contains("run"),
                    AgentLabel = AgentName(agent),
                    Lines = Steps.Select(r => new TeamBoardLine($"{r.RoleName} · {r.AgentName}", r.TimeText.Length > 0 ? Compact(r.Seconds) : "", r.Running)).ToList(),
                    Open = goTask,
                    Cancel = Cancel,
                    OpenHelp = tr("Abrir la tarea en marcha"),
                });
            }
            foreach (var job in demo ? [] : service.Queued)
                running.Add(new(job.Title, "", job.Steps.FirstOrDefault()?.Agent ?? TeamAgent.Sonnet, StepState.Pending, false, tr("En cola"), "", BoardTone.Quiet,
                    "", tr("Abrir la tarea en cola"), ToggleCard("q" + job.Title)) { IsExpanded = expandedCards.Contains("q" + job.Title), Open = goTask, OpenHelp = tr("Abrir la tarea en cola"), Cancel = new Command(() => service.Remove(job)) });

            if (HasPending)
            {
                var agent = Focus?.Agent ?? TeamAgent.Sonnet;
                need.Add(new(RunTitle, PendingHelp, agent, StepState.Done, true, tr("Plan por aprobar"), "", BoardTone.Warn, "", tr("Plan por aprobar"), ToggleCard("plan"))
                {
                    IsExpanded = expandedCards.Contains("plan"),
                    AgentLabel = AgentName(agent),
                    DetailText = planDraft,
                    Approve = ApprovePlan,
                    Reject = RejectPlan,
                    Open = goTask,
                    OpenLabel = tr("Editar"),
                    OpenHelp = tr("Editar el plan antes de aprobarlo"),
                });
            }

            foreach (var e in history.Take(40))
            {
                var agentName = e.Steps.LastOrDefault(s => s.Role is "Implement" or "Answer")?.Agent ?? e.Steps.LastOrDefault()?.Agent;
                var agent = Enum.TryParse<TeamAgent>(agentName, out var a) ? a : TeamAgent.Sonnet;
                var meta = string.Join(" · ", new[]
                {
                    e.Files > 0 ? (e.Files == 1 ? tr("1 archivo") : tr("%d archivos", e.Files)) : null,
                    e.Tokens > 0 ? K(e.Tokens) : null,
                }.OfType<string>());
                var lines = e.Steps.TakeLast(6).Select(s => new TeamBoardLine($"{StepRole(s.Role)} · {s.Agent}", Compact(s.Seconds), false)).ToList();
                if (lines.Count > 0) lines[^1] = lines[^1] with { Current = true };
                bool failed = e.Outcome is "Failed" or "Cancelled";
                var open = new Command(() => SelectEntry(e));
                var help = tr("Abrir «%@»", e.Title);
                ICommand? retry = failed ? new Command(() => Retry(e)) : null;
                TeamBoardCard Card(string status, string glyph, BoardTone tone, StepState state, bool concern, string excerpt) => new(e.Title, excerpt, agent, state, concern, status, glyph, tone, meta, help, ToggleCard(e.Id))
                {
                    IsExpanded = expandedCards.Contains(e.Id),
                    When = e.Started.ToUniversalTime(),
                    AgentLabel = AgentName(agent),
                    DetailText = e.Report,
                    Lines = lines,
                    Open = open,
                    Retry = retry,
                    OpenLabel = e.State == "Answered" ? tr("Preguntar") : tr("Abrir"),
                    OpenHelp = e.State == "Answered" ? tr("Abrir la respuesta para hacerle otra pregunta") : help,
                };

                if (e.State == "Open")
                {
                    var (status, tone, glyph, state, concern) = e.Outcome switch
                    {
                        "Done" => (tr("Listo para fusionar"), BoardTone.Good, "", StepState.Done, false),
                        "Failed" => (tr("Falló"), BoardTone.Bad, "", StepState.Failed, false),
                        "Cancelled" => (tr("Cancelada"), BoardTone.Quiet, "", StepState.Done, false),
                        _ => (tr("Necesita tu decisión"), BoardTone.Warn, "", StepState.Done, true),
                    };
                    need.Add(Card(status, glyph, tone, state, concern, PlainText(e.Report)));
                }
                else if (done.Count < 40)
                {
                    var (status, tone) = e.State switch
                    {
                        "Merged" => (tr("Fusionada"), BoardTone.Good),
                        "Answered" => (tr("Respondida"), BoardTone.Good),
                        _ => e.Outcome == "Cancelled" ? (tr("Cancelada"), BoardTone.Quiet) : (failed ? tr("Falló") : tr("Descartada"), failed ? BoardTone.Bad : BoardTone.Quiet),
                    };
                    done.Add(Card(status, tone == BoardTone.Bad ? "" : tone == BoardTone.Good ? "" : "", tone, e.Outcome == "Failed" ? StepState.Failed : StepState.Done, false, e.State == "Answered" ? PlainText(e.Report) : ""));
                }
            }

            // Sesiones de otras apps (Claude Code, Codex): según su estado, a la columna que toque.
            foreach (var s in ExternalSessions.Take(30))
            {
                var key = "ext:" + AgentLogs.Key(s);
                bool codex = s.Provider.Contains("codex", StringComparison.OrdinalIgnoreCase);
                var agent = codex ? TeamAgent.Codex : TeamAgent.Sonnet;
                var origin = codex ? "Codex" : "Claude Code";
                var meta = string.Join(" · ", new[] { origin, s.ProjectName, AgentLogs.AgeLabel(s.LastActivity, DateTime.UtcNow) }.Where(x => !string.IsNullOrWhiteSpace(x)));
                var activity = s.RecentActivity.TakeLast(6).Select(a => new TeamBoardLine(a.Summary, "", false)).ToList();
                if (activity.Count > 0) activity[^1] = activity[^1] with { Current = true };
                var (status, glyph, tone, state, concern) = s.State switch
                {
                    AgentState.Working => (tr("Trabajando"), "\uE895", BoardTone.Live, StepState.Running, false),
                    AgentState.NeedsAttention => (tr("Necesita tu atención"), "\uE7BA", BoardTone.Warn, StepState.Done, true),
                    AgentState.Finished => (tr("Terminó"), "\uE73E", BoardTone.Good, StepState.Done, false),
                    _ => (tr("Sin actividad"), "\uE823", BoardTone.Quiet, StepState.Done, false),
                };
                var folder = s.Project;
                var card = new TeamBoardCard(s.Name, s.Message ?? "", agent, state, concern, status, glyph, tone, meta, tr("Abrir «%@»", s.Name), ToggleCard(key))
                {
                    IsExpanded = expandedCards.Contains(key),
                    When = s.LastActivity.ToUniversalTime(),
                    AgentLabel = tr("%@ · sesión fuera del equipo", origin),
                    DetailText = s.WorkSummary,
                    Lines = activity,
                    Open = Directory.Exists(folder) ? new Command(() => OpenInExplorer(folder)) : null,
                    OpenLabel = tr("Abrir carpeta"),
                    OpenHelp = tr("Abrir la carpeta del proyecto de esta sesión"),
                };
                switch (s.State)
                {
                    case AgentState.Working: running.Add(card); break;
                    case AgentState.NeedsAttention: need.Add(card); break;
                    default: if (done.Count < 40) done.Add(card); break;
                }
            }

            return
            [
                new(tr("Necesita tu atención"), "", "need", tr("Nada pendiente de ti."), need),
                new(tr("En marcha"), "", "run", tr("Ninguna tarea trabajando."), running),
                new(tr("Hecho"), "", "done", tr("Aún no hay tareas terminadas."), DoneCards(done), done.Count, DoneMoreLabel(done.Count), DoneMore(done.Count)),
            ];
        }
    }
}
