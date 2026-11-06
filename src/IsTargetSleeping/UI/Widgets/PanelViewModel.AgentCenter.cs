using System.Windows.Input;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// Lo compartido del centro de agentes: las reglas «Siempre» y el registro de decisiones, los mismos para el servidor del hook y para la página.
public static class AgentCenter
{
    private static HookRules? rules;
    private static DecisionLog? log;
    public static HookRules Rules => rules ??= new HookRules(Path.Combine(Paths.DataDir, "hook-rules.json"));
    public static DecisionLog Log => log ??= new DecisionLog(Path.Combine(Paths.DataDir, "decisions.jsonl"));
}

public sealed record DecisionRow(string Who, string Detail, string VerdictText, string TimeText, bool Risky, string RiskText, string VerdictKind);
public sealed record AlertRow(string Title, string Detail, bool Severe)
{
    public bool HasDetail => Detail.Length > 0;
}
public sealed record RuleRow(string Title, string Detail, string RevokeLabel, ICommand Revoke);

/// La página «Agentes» del notch: lo que espera tu respuesta, las sesiones, lo que se decidió y las reglas «Siempre» que puedes revocar.
public sealed partial class PanelViewModel
{
    private int agentsTab;   // 0 ahora, 1 historial, 2 reglas
    public bool ShowingAgents => view == PanelView.Agents;
    /// El dock enseña la página mientras se aprueban permisos desde el notch o si el usuario la pidió.
    public bool AgentsDockOn => Prefs.AgentApprovals || Prefs.Switch(PrefKeys.AgentsDock, false);
    public string NotchTabAgents => tr("Agentes");
    public string AgentsPageTitle => tr("Centro de agentes");
    public string AgentsSummary => PendingApprovals.Count == 0 && agentAlerts.Count > 0
        ? (agentAlerts.Count == 1 ? tr("1 aviso de tus agentes") : tr("%d avisos de tus agentes", agentAlerts.Count))
        : PendingApprovals.Count > 0
        ? (PendingApprovals.Count == 1 ? tr("1 petición espera tu respuesta") : tr("%d peticiones esperan tu respuesta", PendingApprovals.Count))
        : tr("Todo en calma: ningún agente espera nada de ti.");
    public bool AgentsTabNow => agentsTab == 0;
    public bool AgentsTabHistory => agentsTab == 1;
    public bool AgentsTabRules => agentsTab == 2;
    public List<Segment> AgentsTabs => [
        new Segment(tr("Ahora"), agentsTab == 0, new Command(() => PickAgentsTab(0))),
        new Segment(tr("Historial"), agentsTab == 1, new Command(() => PickAgentsTab(1))),
        new Segment(tr("Reglas"), agentsTab == 2, new Command(() => PickAgentsTab(2))),
    ];

    private void PickAgentsTab(int tab) { agentsTab = tab; Notify(); }

    /// La petición que se enseña y las que esperan turno, para la lista de la página.
    public IReadOnlyList<AgentApproval> PendingApprovals => Approval is null ? [] : [Approval, .. approvalQueue];
    public bool HasPending => PendingApprovals.Count > 0;
    public bool NoPending => !HasPending;
    public string NoPendingText => tr("Cuando un agente pida permiso aparecerá aquí y en una tarjeta del notch.");
    public string SessionsTitle => tr("Sesiones");
    public IReadOnlyList<AgentSnapshot> AgentSessions => Hub.Agents.Sessions.Concat(Hub.Agents.RecentSessions.Where(r => Hub.Agents.Sessions.All(s => AgentLogs.Key(s) != AgentLogs.Key(r)))).Take(6).ToList();
    public string ResumeInTerminalLabel => tr("Retomar en la terminal");
    public ICommand ResumeInTerminal => resumeInTerminal ??= new ParamCommand(p =>
    {
        if (p is AgentSnapshot session && !AgentTerminal.Open(session)) SetAgentApprovalsStatus(tr("No se pudo abrir la terminal."));
    });
    private ICommand? resumeInTerminal;
    public bool HasAgentSessions => AgentSessions.Count > 0;

    public string HistoryEmptyText => tr("Aún no hay decisiones. Aquí verás qué pidió cada agente y qué respondiste.");
    public string ClearHistoryLabel => tr("Borrar el historial");
    public ICommand ClearHistory => clearHistory ??= new Command(() => { AgentCenter.Log.Clear(); Notify(); });
    private ICommand? clearHistory;
    public IReadOnlyList<DecisionRow> DecisionRows => AgentCenter.Log.Recent(40).Select(e => new DecisionRow(
        $"{(e.Agent == "claude" ? "Claude Code" : e.Agent)} · {e.Project}".TrimEnd(' ', (char)0xB7),
        $"{e.Tool}: {e.Detail}",
        e.Verdict switch { "allow" => tr("Permitido"), "deny" => tr("Denegado"), "always" => tr("Siempre"), "auto" => tr("Automático"), _ => tr("Terminal") },
        e.At.Date == DateTime.Today ? e.At.ToString("HH:mm") : e.At.ToString("dd/MM HH:mm"),
        e.Risk is not null, e.Risk is null ? "" : tr(e.Risk), e.Verdict)).ToList();
    public bool HasDecisions => DecisionRows.Count > 0;
    public bool NoDecisions => !HasDecisions;

    public string RulesIntro => tr("Con «Siempre», un comando igual en el mismo proyecto se permite solo. Aquí puedes quitar cualquier regla. Lo delicado nunca se guarda.");
    public string RulesEmptyText => tr("No has creado ninguna regla «Siempre».");
    public IReadOnlyList<RuleRow> RuleRows => AgentCenter.Rules.Rules.Select(r => new RuleRow(
        $"{r.Project} · {r.Tool}", r.Command.Length > 0 ? r.Command : tr("Cualquier uso de esta herramienta"), tr("Revocar"),
        new Command(() => { AgentCenter.Rules.Remove(r.Key); Notify(); }))).ToList();
    public bool HasRules => RuleRows.Count > 0;
    public bool NoRules => !HasRules;

    // Cuánto espera tu respuesta a un permiso antes de dejarlo a la terminal.
    public int DecisionSeconds => (int)AgentHooks.DecisionWait(Prefs.StoredList(PrefKeys.AgentDecisionSeconds)?.FirstOrDefault()).TotalSeconds;
    public string DecisionTitle => tr("Cuánto esperar tu respuesta");
    public string DecisionDetail => tr("Pasado ese plazo, un permiso se deja a la terminal de Claude Code. Claude Code corta el hook a los 5 minutos, así que ese es el máximo.");
    public List<Segment> DecisionSegments => [
        new Segment(tr("30 s"), DecisionSeconds == 30, new Command(() => Prefs.SetList(PrefKeys.AgentDecisionSeconds, ["30"]))),
        new Segment(tr("1 min"), DecisionSeconds == 60, new Command(() => Prefs.SetList(PrefKeys.AgentDecisionSeconds, ["60"]))),
        new Segment(tr("2 min"), DecisionSeconds == 120, new Command(() => Prefs.SetList(PrefKeys.AgentDecisionSeconds, ["120"]))),
        new Segment(tr("Máximo"), DecisionSeconds >= 280, new Command(() => Prefs.SetList(PrefKeys.AgentDecisionSeconds, ["280"]))),
    ];

    // Control de avisos de agentes: nivel (todo / solo lo importante / nada) y posponer. Decide si algo suena, suena la campana o se abre el notch.
    public AgentNotifyLevel NotifyLevel => AgentNotifyPolicy.Parse(Prefs.StoredList(PrefKeys.AgentNotify)?.FirstOrDefault());
    public DateTime? SnoozedUntil => DateTime.TryParse(Prefs.StoredList(PrefKeys.AgentSnooze)?.FirstOrDefault(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var until) && until > DateTime.Now ? until : null;
    public bool NotifyAllows(AgentNotifyKind kind) => AgentNotifyPolicy.Allows(NotifyLevel, SnoozedUntil, DateTime.Now, kind);
    public string AgentNotifyTitle => tr("Avisos de tus agentes");
    public string AgentNotifyDetail => tr("Decide si algo suena, suena la campana o se abre el notch. Las peticiones y los avisos siempre quedan en la página Agentes.");
    public List<Segment> AgentNotifySegments => [
        new Segment(tr("Todo"), NotifyLevel == AgentNotifyLevel.All, new Command(() => Prefs.SetList(PrefKeys.AgentNotify, ["all"]))),
        new Segment(tr("Solo lo importante"), NotifyLevel == AgentNotifyLevel.Important, new Command(() => Prefs.SetList(PrefKeys.AgentNotify, ["important"]))),
        new Segment(tr("Nada"), NotifyLevel == AgentNotifyLevel.Nothing, new Command(() => Prefs.SetList(PrefKeys.AgentNotify, ["nothing"]))),
    ];
    public string SnoozeLabel => SnoozedUntil is { } until ? tr("En silencio hasta las %@", until.ToString("HH:mm")) : tr("Posponer");
    public bool IsSnoozed => SnoozedUntil is not null;
    public string SnoozeEndLabel => tr("Reactivar");
    public ICommand SnoozeEnd => new Command(() => Prefs.SetList(PrefKeys.AgentSnooze, [""]));
    public List<Segment> SnoozeSegments => [
        new Segment(tr("30 min"), false, new Command(() => Snooze(DateTime.Now.AddMinutes(30)))),
        new Segment(tr("1 h"), false, new Command(() => Snooze(DateTime.Now.AddHours(1)))),
        new Segment(tr("Hasta mañana"), false, new Command(() => Snooze(AgentNotifyPolicy.TomorrowMorning(DateTime.Now)))),
    ];
    private void Snooze(DateTime until) => Prefs.SetList(PrefKeys.AgentSnooze, [until.ToString("o")]);

    // «Mientras no estabas»: al volver tras un buen rato fuera, un resumen de lo que hicieron tus agentes. Sale en la página Agentes, como mensaje de la mascota y con la campana.
    private readonly AwayTracker away = new();
    private AwaySummary? awayDigest;
    private System.Windows.Threading.DispatcherTimer? awayTimer;
    public bool HasAwayDigest => awayDigest is not null;
    public string AwayDigestTitle => awayDigest is null ? "" : tr("Mientras no estabas (%@)", AwayLabel(awayDigest.Away));
    public string AwayDigestText => awayDigest is null ? "" : AwayText(awayDigest);
    public string AwayDismissLabel => tr("Entendido");
    public ICommand DismissAwayDigest => dismissAway ??= new Command(() => { awayDigest = null; Notify(); });
    private ICommand? dismissAway;

    private static string AwayLabel(TimeSpan span)
    {
        int minutes = Math.Max(1, (int)span.TotalMinutes);
        return minutes < 60 ? tr("%d min", minutes) : minutes % 60 == 0 ? tr("%d h", minutes / 60) : tr("%d h %d min", minutes / 60, minutes % 60);
    }

    private static string AwayText(AwaySummary s)
    {
        var parts = new List<string>();
        if (s.AgentsDone > 0) parts.Add(s.AgentsDone == 1 ? tr("1 agente terminó") : tr("%d agentes terminaron", s.AgentsDone));
        if (s.Permissions > 0) parts.Add(s.Permissions == 1 ? tr("pidió 1 permiso") : tr("pidieron %d permisos", s.Permissions));
        if (s.Alerts > 0) parts.Add(s.Alerts == 1 ? tr("saltó 1 aviso") : tr("saltaron %d avisos", s.Alerts));
        if (s.StillPending > 0) parts.Add(s.StillPending == 1 ? tr("1 petición sigue esperando tu respuesta") : tr("%d peticiones siguen esperando tu respuesta", s.StillPending));
        return string.Join(" · ", parts);
    }

    /// Enseña el resumen: banner en la página Agentes, mensaje de la mascota en el chat y la campana del notch.
    public void ShowAwayDigest(AwaySummary summary)
    {
        awayDigest = summary;
        ChatLines.Add(new($"{AwayDigestTitle}: {AwayText(summary)}", false));
        if (NotifyAllows(AgentNotifyKind.AwayDigest)) RaiseNotice(AwayDigestTitle);
        Notify();
    }

    private void StartAway()
    {
        awayTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        awayTimer.Tick += (_, _) =>
        {
            if (away.Update(UserIdle.Seconds(), PendingApprovals.Count, DateTime.Now) is { } summary && !Supervisor.Game.Active) ShowAwayDigest(summary);
        };
        awayTimer.Start();
    }

    // La luz del notch según los agentes. El estado sale de los permisos pendientes, los avisos y las sesiones; «terminó» dura unos segundos.
    private DateTime? agentFinishedAt;
    private AgentGlowState? glowOverride;
    public bool AgentGlowOn => Prefs.AgentGlow;
    public ICommand ToggleAgentGlow => agentGlowToggle ??= new Command(Prefs.ToggleAgentGlow);
    private ICommand? agentGlowToggle;
    public string AgentGlowTitle => tr("Luz del notch según tus agentes");
    public string AgentGlowDetail => tr("El borde del notch respira en azul mientras trabajan, en ámbar si algo espera tu respuesta, en rojo si hay un aviso y destella en verde al terminar.");
    public AgentGlowState AgentGlowNow
    {
        get
        {
            if (!Prefs.AgentGlow) return AgentGlowState.Off;
            if (glowOverride is { } forced) return forced;
            var now = DateTime.UtcNow;
            var sessions = Hub.Agents.Sessions;
            var finished = Hub.Agents.RecentSessions.Where(s => s.State == AgentState.Finished).Select(s => (DateTime?)s.LastActivity.ToUniversalTime()).DefaultIfEmpty(null).Max();
            var done = agentFinishedAt is { } hook && (finished is null || hook > finished) ? hook.ToUniversalTime() : finished;
            return AgentGlowRules.Of(HasApproval, agentAlerts.Any(a => a.Kind is AgentAlertKind.Loop or AgentAlertKind.LimitHit or AgentAlertKind.Stalled),
                sessions.Any(s => s.State == AgentState.NeedsAttention), sessions.Any(s => s.State == AgentState.Working), done, now);
        }
    }
    /// Para pruebas y capturas: fuerza un estado de la luz (null la devuelve a lo real).
    public void SetAgentGlowForTest(AgentGlowState? state) { glowOverride = state; Notify(); }
    internal void NoteAgentFinished() => agentFinishedAt = DateTime.Now;

    // Avisos de agentes: atascado, en bucle, límite de uso a punto de acabarse o agotado. Se calculan cada 20 s con lo que ya se lee de los logs y del uso de IA.
    private IReadOnlyList<AgentAlert> agentAlerts = [];
    private readonly HashSet<string> seenAlertKeys = [];
    private System.Windows.Threading.DispatcherTimer? alertTimer;
    public IReadOnlyList<AgentAlert> AgentAlerts => agentAlerts;
    public bool HasAgentAlerts => agentAlerts.Count > 0;
    public IReadOnlyList<AlertRow> AgentAlertRows => agentAlerts.Select(AlertRowOf).ToList();

    private static AlertRow AlertRowOf(AgentAlert a)
    {
        var countdown = IsTargetSleeping.AgentAlerts.Countdown(a.Until, DateTimeOffset.Now);
        return a.Kind switch
        {
            AgentAlertKind.Stalled => new(tr("%@ lleva %d min sin dar señales", a.Who, a.Number), tr("Puede estar atascado o esperando un comando largo."), false),
            AgentAlertKind.Loop => new(tr("%@ repite lo mismo: %@", a.Who, a.Subject), tr("%d veces seguidas. Quizá esté en un bucle.", a.Number), true),
            AgentAlertKind.LimitNear => new(tr("%@: el límite de %@ va por el %d %%", a.Who, a.Subject, a.Number), countdown.Length > 0 ? tr("Se reinicia en %@", countdown) : "", false),
            AgentAlertKind.RoutinePaused => new(tr("La rutina «%@» se pausó tras %d fallos seguidos", a.Who, a.Number), tr("Revisa qué falla y vuelve a encenderla desde el widget Equipo."), true),
            _ => new(tr("%@: el límite de %@ está agotado", a.Who, a.Subject), countdown.Length > 0 ? tr("Vuelve en %@", countdown) : "", true),
        };
    }

    /// Para pruebas y capturas: pone unos avisos concretos.
    public void SetAgentAlerts(IEnumerable<AgentAlert> alerts)
    {
        agentAlerts = alerts.ToList();
        Notify();
    }

    private void StartAgentAlerts()
    {
        // La lista de sesiones de la página se repinta cuando cambian las sesiones que se leen de los logs (y su botón de retomar).
        Hub.Agents.Sessions.CollectionChanged += (_, _) => { if (ShowingAgents) PropertyChanged?.Invoke(this, new(nameof(AgentSessions))); };
        Hub.Agents.RecentSessions.CollectionChanged += (_, _) => { if (ShowingAgents) PropertyChanged?.Invoke(this, new(nameof(AgentSessions))); };
        alertTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        alertTimer.Tick += (_, _) => RefreshAgentAlerts();
        alertTimer.Start();
    }

    private void RefreshAgentAlerts()
    {
        var limits = Hub.Usage.Rows.SelectMany(r => r.Limits.Select(l => new LimitInfo(r.Name, l.Label, l.Fraction, l.ResetsAt)));
        var found = IsTargetSleeping.AgentAlerts.Detect(Hub.Agents.Sessions, limits, DateTime.UtcNow).ToList();
        // Las rutinas del equipo que se apagaron solas por fallar sin parar (disyuntor) también son un aviso.
        foreach (var row in Hub.Team.ScheduleRows.Where(r => r.Schedule.PausedByFailures))
            found.Add(new(AgentAlertKind.RoutinePaused, $"routine|{row.Schedule.Id}", row.Title, "", row.Schedule.ConsecutiveFailures, null));
        var changed = !found.Select(a => a.Key).SequenceEqual(agentAlerts.Select(a => a.Key));
        agentAlerts = found;
        // Cada aviso nuevo suena una vez en el notch (la campana); mientras dure no se repite, y si desaparece y vuelve, avisa otra vez.
        seenAlertKeys.IntersectWith(found.Select(a => a.Key));
        foreach (var alert in found.Where(a => seenAlertKeys.Add(a.Key)))
        {
            if (NotifyAllows(AgentNotifyPolicy.KindOf(alert.Kind))) RaiseNotice(AlertRowOf(alert).Title);
            away.Record(AwayKind.Alert);
        }
        if (changed) Notify();
    }

    /// El servidor del hook decidió algo: se repinta lo que depende del registro y de las reglas.
    public void RefreshAgentCenter()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) { dispatcher.BeginInvoke(RefreshAgentCenter); return; }
        Notify();
    }
}
