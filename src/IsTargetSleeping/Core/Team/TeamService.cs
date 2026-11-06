using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping;

/// Una tarea para el equipo: ya enrutada (pasos con su agente y modelo) y con sus topes.
public sealed record TeamJob(string Task, string Root, TeamPreset Preset, IReadOnlyList<TeamStep> Steps, TaskProfile Profile)
{
    /// Enseñar el plan al usuario antes de implementar, para que lo apruebe o lo edite.
    public bool ReviewPlan { get; init; }
    /// Responder leyendo la carpeta del proyecto (solo lectura) aunque la frase no hable de «este proyecto»: lo piden los agentes propios.
    public bool ReadsRoot { get; init; }
    /// Tope de tokens (entrada nueva + salida); 0 = sin tope.
    public long MaxTokens { get; init; }
    public string? ScheduleId { get; init; }
    /// El nombre que el usuario dio a la tarea programada: encabeza el título en el historial.
    public string? ScheduleTitle { get; init; }
    /// Carpeta con las imágenes adjuntas de esta tarea: se borra al terminar (el seguimiento no las hereda).
    public string? AttachmentDir { get; init; }
    /// Bloqueo compartido que mantiene única la ejecución programada mientras el trabajo está en cola o en curso.
    public IDisposable? ScheduleLease { get; init; }
    /// Un seguimiento: se sigue trabajando en la rama y la copia de trabajo de una ejecución anterior.
    public TeamHistoryEntry? FollowUpOf { get; init; }
    /// La conversación anterior de una pregunta de seguimiento («Pregunta: … Respuesta: …»), tomada al crear el trabajo: así no se pierde si el
    /// historial se recorta o se borra mientras espera en la cola.
    public string? ThreadContext { get; init; }

    public string Title
    {
        get
        {
            var line = Task.Trim().Split('\n', 2)[0].Trim();
            var title = line.Length > 80 ? line[..80] + "…" : line;
            return string.IsNullOrWhiteSpace(ScheduleTitle) ? title : $"{ScheduleTitle.Trim()}: {title}";
        }
    }
}

/// Un plan que espera el visto bueno del usuario.
public sealed record PlanApproval(TeamStep Step, string Text);

/// Una línea del registro en vivo de una ejecución: qué está haciendo ahora el agente.
public sealed record ActivityLine(DateTime At, string Text);

/// La ejecución en marcha, tal como la ve la interfaz.
public sealed class ActiveRun(string id, TeamJob job, DateTime started)
{
    public string Id { get; } = id;
    public TeamJob Job { get; } = job;
    public DateTime Started { get; } = started;
    /// Lo que lleva trabajando: sin contar el rato que el plan espera el visto bueno del usuario.
    public TimeSpan Elapsed => DateTime.Now - Started - Paused - (WaitingSince is { } since ? DateTime.Now - since : TimeSpan.Zero);
    internal DateTime? WaitingSince { get; set; }
    internal TimeSpan Paused { get; set; }
    internal string WorkingDir { get; set; } = "";
    internal string BaseSha { get; set; } = "";
    internal string BranchId { get; set; } = "";
    internal bool Quick { get; set; }
    internal bool ReadsProject { get; set; }
    private readonly List<ActivityLine> log = [];
    /// Lo último que ha hecho el agente (las últimas líneas, de la más vieja a la más nueva): es lo que evita pensar que se ha colgado.
    public IReadOnlyList<ActivityLine> Log { get { lock (log) return [.. log]; } }
    public DateTime LastMove { get; private set; } = started;
    internal void Push(string text)
    {
        lock (log)
        {
            if (log.Count > 0 && log[^1].Text == text) return;
            log.Add(new ActivityLine(DateTime.Now, text));
            if (log.Count > 24) log.RemoveAt(0);
        }
        LastMove = DateTime.Now;
    }
    public long Tokens { get; internal set; }
    public double CostUsd { get; internal set; }
    /// Agente que de verdad está haciendo cada paso, y su estado.
    public StepState[] States { get; } = new StepState[job.Steps.Count];
    public TeamAgent[] Agents { get; } = [.. job.Steps.Select(s => s.Agent)];
    /// Lo último que dijo cada paso al terminar (su informe, recortado).
    public string?[] Messages { get; } = new string?[job.Steps.Count];
    public long[] StepTokens { get; } = new long[job.Steps.Count];
    public DateTime?[] StepStarted { get; } = new DateTime?[job.Steps.Count];
    public double[] StepSeconds { get; } = new double[job.Steps.Count];
}

/// El motor del equipo sin interfaz: una cola de tareas que se ejecutan de una en una, cada una en su rama y copia de trabajo,
/// con historial en disco. Lo usan el widget, el programador y `--team-run`. Los eventos llegan desde hilos de fondo.
public sealed class TeamService
{
    private readonly Func<TeamStep, string, string, CancellationToken, Task<AgentResult>> exec;
    private readonly Func<string, string> baseDir;
    private readonly Queue<TeamJob> queue = new();
    // De todo el proceso: dos servicios que arrancan en el mismo segundo no pueden repartirse el mismo nombre de rama.
    private static readonly HashSet<string> usedIds = [];
    private readonly object gate = new();
    private CancellationTokenSource? cts;
    private TaskCompletionSource<string?>? decision;
    private Task pump = Task.CompletedTask;
    private bool pumping;

    public TeamService(TeamStore store, Func<TeamStep, string, string, CancellationToken, Task<AgentResult>>? exec = null, Func<string, string>? baseDir = null)
    {
        Store = store;
        this.exec = exec ?? ((step, prompt, dir, ct) => TeamProcess.RunAgentAsync(step, prompt, dir, ct, PushActivity));
        this.baseDir = baseDir ?? BaseDir;
    }

    public TeamStore Store { get; }
    public event Action? Changed;

    private long lastActivityTick;
    /// Una frase de lo que hace el agente ahora: va al registro de la ejecución y avisa a la interfaz (como mucho unas cuatro veces por segundo).
    private void PushActivity(string text)
    {
        Current?.Push(text);
        var now = Environment.TickCount64;
        if (now - Interlocked.Exchange(ref lastActivityTick, now) > 250) Changed?.Invoke();
    }
    public event Action<StepProgress>? Progress;
    public event Action<TeamHistoryEntry>? Finished;

    public ActiveRun? Current { get; private set; }
    public PlanApproval? Pending { get; private set; }
    public IReadOnlyList<TeamJob> Queued { get { lock (gate) return [.. queue]; } }
    // `working` cubre el instante entre sacar una tarea de la cola y asignar `Current`: sin él, Busy parpadeaba a falso.
    private volatile bool working;
    public bool Busy => working || Current is not null || Queued.Count > 0;

    /// Quita una tarea que aún espera en la cola (la que corre se para con <see cref="Cancel"/>). Falso si ya empezó.
    public bool Remove(TeamJob job)
    {
        lock (gate)
        {
            var kept = queue.Where(j => !ReferenceEquals(j, job)).ToList();
            if (kept.Count == queue.Count) return false;
            queue.Clear();
            foreach (var j in kept) queue.Enqueue(j);
        }
        CleanupAttachments(job);
        job.ScheduleLease?.Dispose();
        Changed?.Invoke();
        return true;
    }

    /// Las copias de trabajo viven fuera del repo del usuario, en su carpeta de datos.
    public static string BaseDir(string root) =>
        Path.Combine(Paths.DataDir, "team", Path.GetFileName(root.TrimEnd('\\', '/')) + "-" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(root.ToLowerInvariant())))[..8]);

    /// ¿Esta carpeta de trabajo es de una tarea del propio equipo (una rama aparte, sus adjuntos o una pregunta)? Las sesiones de Codex que
    /// dejan esas tareas ya salen en el tablero como tarjetas del equipo: no se repiten como sesiones sueltas.
    public static bool OwnsPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var p = path.Replace('/', '\\').TrimEnd('\\') + "\\";
        var team = Path.Combine(Paths.DataDir, "team").Replace('/', '\\').TrimEnd('\\') + "\\";
        return p.StartsWith(team, StringComparison.OrdinalIgnoreCase) || p.Contains("\\its-team-quick\\", StringComparison.OrdinalIgnoreCase);
    }

    /// Termina cuando no queda nada en marcha ni en cola.
    public Task WhenIdle() { lock (gate) return pump; }

    public void Enqueue(TeamJob job)
    {
        // La interfaz ya lo evita, pero «Reintentar» y las tareas programadas llegan por aquí: una tarea vacía arrancaría el agente y gastaría tokens.
        if (string.IsNullOrWhiteSpace(job.Task)) { AppLog.Write("equipo: tarea vacía, no se encola"); CleanupAttachments(job); job.ScheduleLease?.Dispose(); return; }
        lock (gate)
        {
            queue.Enqueue(job);
            if (!pumping) { pumping = true; pump = Task.Run(Pump); }
        }
        Changed?.Invoke();
    }

    public void Cancel() => cts?.Cancel();

    public void Approve(string? editedText = null) => decision?.TrySetResult(string.IsNullOrWhiteSpace(editedText) ? Pending?.Text : editedText);
    public void Reject() => decision?.TrySetResult(null);

    /// Cuántas tareas hay en cola de una programada (para no apilar la misma).
    public bool IsQueuedOrRunning(string scheduleId)
    {
        lock (gate) return Current?.Job.ScheduleId == scheduleId || queue.Any(j => j.ScheduleId == scheduleId);
    }

    private async Task Pump()
    {
        while (true)
        {
            TeamJob job;
            lock (gate)
            {
                if (!queue.TryDequeue(out job!)) { pumping = false; working = false; return; }
                working = true;
            }
            try { await RunJob(job); }
            catch (Exception e) { AppLog.Write($"equipo: {e.Message}"); }
            finally
            {
                CleanupAttachments(job);
                job.ScheduleLease?.Dispose();
                Current = null; Pending = null; Changed?.Invoke();
            }
        }
    }

    private string NewId(DateTime now)
    {
        var stamp = now.ToString("yyMMdd-HHmmss");
        var id = stamp;
        lock (usedIds)
        {
            for (int n = 2; !usedIds.Add(id) || Store.LoadHistory().Any(e => e.Id == id || e.BranchId == id); n++) id = $"{stamp}-{n}";
        }
        return id;
    }

    private async Task RunJob(TeamJob job)
    {
        var started = DateTime.Now;
        var id = NewId(started);
        var run = new ActiveRun(id, job, started);
        cts = new CancellationTokenSource();
        var ct = cts.Token;
        Current = run;
        Changed?.Invoke();

        var parent = job.FollowUpOf;
        string dir, baseSha, branchId;
        bool created = false;
        // Una pregunta no toca ningún proyecto: se responde en una carpeta vacía y no deja rama ni copia de trabajo.
        bool quick = job.Profile.Kind == TaskKind.Question;
        // Salvo que hable del proyecto elegido («¿qué hace este programa?»): entonces se responde leyendo su carpeta, solo lectura, sin rama.
        var readsProject = quick && job.Root.Length > 0 && Directory.Exists(job.Root) && (job.ReadsRoot || TeamRouter.RefersToProject(job.Task));
        IReadOnlyList<TeamStep> planSteps = readsProject ? job.Steps.Select(s => s.Role == TeamRole.Answer ? s with { ReadsProject = true } : s).ToList() : job.Steps;
        if (quick)
        {
            dir = readsProject ? job.Root : Path.Combine(Path.GetTempPath(), "its-team-quick", id);
            Directory.CreateDirectory(dir);
            (baseSha, branchId) = ("", "");
        }
        else if (parent is not null && Directory.Exists(parent.TreeDir)) { (dir, baseSha, branchId) = (parent.TreeDir, parent.BaseSha, parent.BranchId); }
        else
        {
            var (made, error) = await TeamWorktree.CreateAsync(job.Root, id, baseDir(job.Root), ct);
            if (made is null) { Finish(Entry(job, id, id, "", "", started, "Failed", [], error ?? "git", 0, 0, [], "", "Discarded"), job); return; }
            (dir, branchId, created) = (made, id, true);
            baseSha = await TeamGit.HeadAsync(dir, ct);
        }
        run.WorkingDir = dir;
        run.BaseSha = baseSha;
        run.BranchId = branchId;
        run.Quick = quick;
        run.ReadsProject = readsProject;

        var times = new List<TimeSpan>();
        var runner = new TeamRun(async (step, prompt, c) =>
        {
            var clock = Stopwatch.StartNew();
            try { return await exec(step, prompt, dir, c); }
            finally { lock (times) times.Add(clock.Elapsed); }
        }, gate: job.ReviewPlan ? Gate : null, maxTokens: job.MaxTokens, context: job.ThreadContext ?? parent?.Report ?? "");
        runner.Progress += p =>
        {
            if (p.Index < run.States.Length)
            {
                run.States[p.Index] = p.State;
                run.Agents[p.Index] = p.Agent;
                if (p.State == StepState.Running)
                {
                    run.StepStarted[p.Index] = DateTime.Now;
                    run.Push($"{p.Agent} · {TeamActivity.RoleName(job.Steps[p.Index].Role)}");
                }
                else
                {
                    if (run.StepStarted[p.Index] is { } t0) run.StepSeconds[p.Index] += (DateTime.Now - t0).TotalSeconds;
                    run.StepStarted[p.Index] = null;
                    if (p.Result is { } r)
                    {
                        run.Push(tr(p.State == StepState.Failed ? "%@ falló" : "%@ terminado", TeamActivity.RoleName(job.Steps[p.Index].Role)));
                        run.Messages[p.Index] = Clip(r.Message, 600);
                        run.StepTokens[p.Index] += r.InputTokens + r.OutputTokens;
                        run.Tokens += r.InputTokens + r.OutputTokens;
                        run.CostUsd += r.CostUsd ?? 0;
                    }
                }
            }
            Progress?.Invoke(p);
            Changed?.Invoke();
        };

        TeamRunResult result;
        try { result = await runner.RunAsync(new TeamPlan(job.Profile, job.Preset, planSteps), job.Task, ct); }
        catch (OperationCanceledException) { result = new(TeamOutcome.Cancelled, "cancelado", []); }
        for (int i = 0; i < result.Records.Count && i < times.Count; i++)
            TeamLog.Append(Path.Combine(Store.Folder, "team-runs.jsonl"), id, result.Records[i], times[i]);

        // Todo lo que dejaron los agentes queda guardado en la rama (también si algo salió a medias), para poder verlo o descartarlo.
        string? commitError = null;
        List<FileChange> changes = [];
        string state = "Open";
        if (quick)
        {
            state = "Answered";
            if (!readsProject) try { Directory.Delete(dir, true); } catch { }
        }
        else
        {
            var commit = await TeamWorktree.CommitAsync(dir, $"Equipo{(result.Outcome == TeamOutcome.Done ? "" : " (incompleto)")}: {job.Title}");
            commitError = commit.Error;
            changes = await TeamGit.ChangesAsync(dir, baseSha);
            if (created && changes.Count == 0)
            {
                await TeamWorktree.DiscardAsync(job.Root, dir, branchId);
                state = "Discarded";
            }
        }
        var steps = result.Records.Select((r, i) => new TeamStepSummary(r.Step.Profile?.Name ?? r.Agent.ToString(), r.Step.Role.ToString(), r.Step.Profile?.Model ?? (r.Step.Agent == r.Agent ? r.Step.Model : null),
            r.Result.Ok, (int)(i < times.Count ? times[i].TotalSeconds : 0), r.Result.InputTokens + r.Result.OutputTokens)).ToList();
        string report = result.Records.LastOrDefault(r => r.Step.Role is TeamRole.Implement or TeamRole.Answer && r.Result.Ok)?.Result.Message
            ?? (result.Outcome == TeamOutcome.Done ? "" : result.Summary);
        string verdict = result.Records.LastOrDefault(r => r.Step.Role == TeamRole.Review && r.Result.Ok) is { } review
            ? TeamRun.ParseVerdict(review.Result.Message).ToString() : "";
        // Los agentes enlazan archivos con su ruta del disco («[a.cs](/C:/Users/…/a.cs)»): en el informe basta el nombre.
        report = System.Text.RegularExpressions.Regex.Replace(report, @"\[([^\]]+)\]\((?:file:///)?/?[A-Za-z]:[^)]*\)", "$1");
        if (verdict == nameof(Verdict.Approved) && result.Records.LastOrDefault(r => r.Step.Role == TeamRole.Review && r.Result.Ok) is { } approver)
            report = report.Length > 0 ? $"{report}\n\n{tr("Revisión: aprobada por %@.", approver.Agent.ToString())}" : report;
        var entry = Entry(job, id, branchId, readsProject ? "" : dir, baseSha, started, result.Outcome.ToString(), steps, report, result.Tokens, run.CostUsd, changes, verdict, state)
            with { Report = Clip(report.Length > 0 ? report : result.Summary, 2000) };
        if (commitError is not null) AppLog.Write($"equipo: no se pudo guardar en la rama: {commitError}");
        Finish(entry, job);
    }

    private TeamHistoryEntry Entry(TeamJob job, string id, string branchId, string dir, string baseSha, DateTime started, string outcome,
        List<TeamStepSummary> steps, string report, long tokens, double cost, List<FileChange> changes, string verdict, string state) => new()
    {
        Id = id, BranchId = branchId, ParentId = job.FollowUpOf?.Id, Root = job.Root, TreeDir = dir, BaseSha = baseSha, Title = job.Title, Task = Clip(job.Task.Trim(), 4000),
        Preset = job.Preset.ToString(), Started = started, Seconds = (int)(DateTime.Now - started - (Current is { } c && c.Id == id ? c.Paused : TimeSpan.Zero)).TotalSeconds, Outcome = outcome, Tokens = tokens,
        CostUsd = cost, Files = changes.Count, Added = changes.Sum(c => c.Added), Removed = changes.Sum(c => c.Removed), Report = Clip(report, 2000),
        ScheduleId = job.ScheduleId, State = state, Steps = steps, Verdict = verdict,
    };

    private void Finish(TeamHistoryEntry entry, TeamJob job)
    {
        string? storeError = Store.SaveHistory(entry) ? null : tr("No se pudo guardar el historial: %@", Store.LastError ?? tr("error desconocido"));
        if (job.ScheduleId is { } sid && Store.LoadSchedules().FirstOrDefault(s => s.Id == sid) is { } schedule)
            if (!Store.UpsertSchedule(schedule.WithOutcome(entry.Outcome)))
                storeError = string.Join(" ", new[] { storeError, tr("No se pudo actualizar la tarea programada: %@", Store.LastError ?? tr("error desconocido")) }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (storeError is not null)
        {
            AppLog.Write($"equipo: {storeError}");
            entry = entry with { StoreError = storeError };
        }
        Current = null;
        Pending = null;
        Finished?.Invoke(entry);
        Changed?.Invoke();
    }

    private static void CleanupAttachments(TeamJob job)
    {
        if (job.AttachmentDir is not { Length: > 0 } attachments) return;
        try { if (Directory.Exists(attachments)) Directory.Delete(attachments, true); }
        catch (Exception e) { AppLog.Write($"equipo: no se pudieron borrar los adjuntos temporales: {e.Message}"); }
    }

    private async Task<string?> Gate(TeamStep step, string text, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate) { decision = tcs; Pending = new PlanApproval(step, text); if (Current is { } waiting) waiting.WaitingSince = DateTime.Now; }
        Changed?.Invoke();
        using var registration = ct.Register(() => tcs.TrySetResult(null));
        var answer = await tcs.Task;
        lock (gate)
        {
            decision = null; Pending = null;
            if (Current is { WaitingSince: { } since } resumed) { resumed.Paused += DateTime.Now - since; resumed.WaitingSince = null; }
        }
        Changed?.Invoke();
        return answer;
    }

    /// Fusiona la rama en la rama actual del repo y borra la copia de trabajo. Devuelve el error, o null si salió bien.
    public async Task<string?> MergeAsync(TeamHistoryEntry entry)
    {
        var error = await TeamWorktree.MergeAsync(entry.Root, entry.BranchId);
        error ??= await TeamWorktree.DiscardAsync(entry.Root, entry.TreeDir, entry.BranchId);
        if (error is null) Store.SetState(entry.BranchId, "Merged");
        Changed?.Invoke();
        return error;
    }

    public async Task<string?> DiscardAsync(TeamHistoryEntry entry)
    {
        var error = await TeamWorktree.DiscardAsync(entry.Root, entry.TreeDir, entry.BranchId);
        if (error is null) Store.SetState(entry.BranchId, "Discarded");
        Changed?.Invoke();
        return error;
    }

    /// Un seguimiento: «cambia esto» sobre la misma rama, con lo que ya se hizo como contexto. Implementa quien implementó y revisa el otro.
    public static TeamJob FollowUp(TeamHistoryEntry entry, string text, IReadOnlyList<CodexModel> models)
    {
        var last = entry.Steps.LastOrDefault(s => s.Role == nameof(TeamRole.Implement));
        var agent = last is not null && Enum.TryParse<TeamAgent>(last.Agent, out var a) ? a : TeamAgent.Sonnet;
        var steps = TeamRouter.FollowUpSteps(agent).Select(s => CodexModels.Apply(s, models)).ToList();
        var preset = Enum.TryParse<TeamPreset>(entry.Preset, out var p) ? p : TeamPreset.Auto;
        return new TeamJob(text.Trim(), entry.Root, preset, steps, TeamRouter.Profile(text)) { FollowUpOf = entry };
    }

    /// Una pregunta de seguimiento sobre una respuesta ya cerrada («¿y en inglés?»): sigue siendo una pregunta sencilla aunque la frase suelta no lo
    /// parezca, la responde el mismo agente y modelo que respondió y lleva la conversación anterior como contexto.
    public static TeamJob AnswerFollowUp(TeamHistoryEntry entry, string text, IReadOnlyList<CodexModel> models, IReadOnlyList<TeamHistoryEntry> history)
    {
        var profile = TeamRouter.Profile(text) with { Kind = TaskKind.Question };
        var preset = Enum.TryParse<TeamPreset>(entry.Preset, out var p) ? p : TeamPreset.Auto;
        var step = TeamRouter.Route(profile, preset).Steps[0];
        string? slug = null;
        if (entry.Steps.FirstOrDefault(s => s.Role == nameof(TeamRole.Answer)) is { } answered && Enum.TryParse<TeamAgent>(answered.Agent, out var agent))
        {
            step = step with { Agent = agent, Model = answered.Model };
            slug = answered.Model;
        }
        step = CodexModels.Apply(step, models, slug);
        return new TeamJob(text.Trim(), entry.Root, preset, [step], profile) { FollowUpOf = entry, ThreadContext = Thread(entry, history) };
    }

    /// Las preguntas y respuestas anteriores de una conversación, de la más vieja a la más nueva (hasta `turns` turnos y `maxChars` caracteres;
    /// si no cabe, se conserva lo más reciente).
    public static string Thread(TeamHistoryEntry entry, IReadOnlyList<TeamHistoryEntry> history, int turns = 3, int maxChars = 6000)
    {
        var chain = new List<TeamHistoryEntry>();
        for (var current = entry; current is not null && chain.Count < turns; current = current.ParentId is null ? null : history.FirstOrDefault(h => h.Id == current.ParentId))
            chain.Add(current);
        chain.Reverse();
        var text = string.Join("\n\n", chain.Select(e => $"Pregunta: {e.Task.Trim()}\nRespuesta: {e.Report.Trim()}"));
        return text.Length <= maxChars ? text : "…" + text[^maxChars..];
    }

    /// Un nuevo intento de una tarea que falló: mismo texto, proyecto y reparto, enrutada de nuevo con los modelos de ahora.
    public static TeamJob RetryJob(TeamHistoryEntry entry, string task, IReadOnlyList<CodexModel> models, bool reviewPlan = false)
    {
        var profile = TeamRouter.Profile(task);
        var preset = Enum.TryParse<TeamPreset>(entry.Preset, out var p) ? p : TeamPreset.Auto;
        var steps = TeamRouter.Route(profile, preset).Steps.Select(s => CodexModels.Apply(s, models)).ToList();
        return new TeamJob(task.Trim(), entry.Root, preset, steps, profile) { ReviewPlan = reviewPlan };
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max] + "…";
}
