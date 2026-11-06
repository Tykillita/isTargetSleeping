using System.Text.Json.Nodes;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// Lo que la mascota (celular/placa) puede hacer con las peticiones de los agentes: verlas y decidirlas. Mismo camino que el notch:
/// `HookRequest.Decide/Reply/Reject`; gana la primera respuesta (si el notch ya la resolvió, la mascota recibe «ya no está pendiente»).
public sealed partial class PanelViewModel
{
    private DateTime lastPetApprovals = DateTime.MinValue;
    private IReadOnlyList<AgentApproval> AllApprovals => (Approval is null ? [] : new[] { Approval }).Concat(approvalQueue).ToList();

    /// Publica la cola actual (y la renueva cada 20 s mientras haya algo: así la mascota sabe si la app dejó de correr).
    public void PublishPetApprovals(bool heartbeat = false)
    {
        var all = AllApprovals;
        if (heartbeat && (all.Count == 0 || DateTime.UtcNow - lastPetApprovals < TimeSpan.FromSeconds(20))) return;
        lastPetApprovals = DateTime.UtcNow;
        var items = all.Select(a =>
        {
            var r = a.Request;
            var asks = a.IsQuestion ? AgentQuestions.Parse(r.ToolInput).Select(q => new PetAskInfo(q.Text, q.Header, q.Multi, q.Options.Select(o => o.Label).ToList())).ToList() : null;
            string? plan = a.IsPlan ? AgentQuestions.PlanText(r.ToolInput) : null;
            return new PetApprovalInfo(a.PetId, r.Agent, a.Kind.ToString().ToLowerInvariant(), r.Tool ?? "", r.Cwd, r.Detail, r.Risk is null ? null : tr(r.Risk),
                new DateTimeOffset(r.At.ToUniversalTime()).Add(AgentHooks.Wait).ToUnixTimeSeconds(), asks, plan is { Length: > 1200 } ? plan[..1200] + "…" : plan) { Diff = PetDiff(r) };
        }).ToList();
        PetBridge.PublishApprovals(items, force: heartbeat);
    }

    /// El cambio que pide una edición, en líneas con «-» y «+», acotado (24 líneas de 140 caracteres). Null si no es una edición o el archivo es sensible.
    private static string? PetDiff(HookRequest r)
    {
        if (r.ToolInput is not JsonObject o || r.Tool is not ("Edit" or "MultiEdit" or "Write")) return null;
        string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        if (AgentRisk.IsSensitivePath(Str(o["file_path"]))) return null;
        var lines = new List<string>();
        void Chunk(string? old, string? @new, int max)
        {
            foreach (var l in (old ?? "").Replace("\r", "").Split('\n').Take(max)) lines.Add("-" + l);
            foreach (var l in (@new ?? "").Replace("\r", "").Split('\n').Take(max)) lines.Add("+" + l);
        }
        if (r.Tool == "Edit") Chunk(Str(o["old_string"]), Str(o["new_string"]), 10);
        else if (r.Tool == "Write") Chunk(null, Str(o["content"]), 16);
        else if (o["edits"] is JsonArray ed) foreach (var e in ed.Take(3)) if (e is JsonObject eo) Chunk(Str(eo["old_string"]), Str(eo["new_string"]), 6);
        if (lines.Count == 0) return null;
        var text = string.Join("\n", lines.Take(24).Select(l => new string(l.Where(c => !char.IsControl(c) || c == '\t').ToArray())).Select(l => l.Length > 140 ? l[..140] + "…" : l));
        return text.Length > 1500 ? text[..1500] + "…" : text;
    }

    private static string SessionId(AgentSnapshot s) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s.Session)))[..12].ToLowerInvariant();

    /// Como PetCut pero conserva los saltos de línea (informes y tareas de varias líneas).
    private static string? PetText(string? s, int n)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = new string(s.Replace("\r", "").Where(c => !char.IsControl(c) || c == '\n' || c == '\t').ToArray()).Trim();
        return t.Length > n ? t[..n] + "…" : t;
    }

    private readonly Dictionary<string, List<object>> petChanges = [];

    /// Los archivos que cambió una ejecución (ruta, +/−), de su rama mientras exista. Se calculan una vez en segundo plano; sin contenido.
    private List<object>? PetChanges(TeamHistoryEntry e)
    {
        if (e.Files == 0 || e.State != "Open" || !Directory.Exists(e.TreeDir) || e.BaseSha.Length == 0) return null;
        if (petChanges.TryGetValue(e.Id, out var done)) return done;
        petChanges[e.Id] = [];   // marcador: ya se está calculando
        _ = Task.Run(async () =>
        {
            try
            {
                var list = (await TeamGit.ChangesAsync(e.TreeDir, e.BaseSha)).Where(c => !AgentRisk.IsSensitivePath(c.Path)).Take(20)
                    .Select(c => (object)new { path = PetCut(c.Path.Replace('\\', '/'), 100), added = c.Added, removed = c.Removed }).ToList();
                petChanges[e.Id] = list;
            }
            catch (Exception) { }
        });
        return null;
    }

    private static string? PetCut(string? s, int n)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = new string(s.Where(c => !char.IsControl(c)).ToArray());
        return t.Length > n ? t[..n] + "…" : t;
    }

    /// Los agentes que trabajan ahora, con su detalle, para la pestaña Agentes del celular. Sin cuerpo de archivos ni rutas completas:
    /// proyecto, estado, herramienta, nombres de archivo (los sensibles se ocultan), rama y las últimas actividades.
    public void PublishPetSessions()
    {
        var now = DateTime.Now;
        var items = AgentSessions.Select(s =>
        {
            bool hide = AgentRisk.IsSensitivePath(s.File);
            var activity = s.RecentActivity.Take(6).Select(a =>
            {
                bool secret = AgentRisk.IsSensitivePath(a.File);
                return new PetActivityInfo(PetCut(a.Tool, 30), secret || a.File is null ? null : Path.GetFileName(a.File), secret ? $"{a.Tool} · (archivo oculto)" : PetCut(a.Summary, 90) ?? "", a.Completed);
            }).ToList();
            var age = now - s.LastActivity;
            return new PetSessionInfo(SessionId(s), s.Provider, s.ProjectName, s.State.ToString().ToLowerInvariant(), PetCut(s.Tool, 30),
                hide || s.File is null ? null : Path.GetFileName(s.File), PetCut(s.Message, 100), new DateTimeOffset(s.LastActivity.ToUniversalTime()).ToUnixTimeSeconds())
            {
                Title = PetCut(s.Title, 80), Branch = PetCut(s.GitBranch, 60), ContextTokens = s.ContextTokens, Added = s.Added ?? 0, Removed = s.Removed ?? 0,
                Repeat = s.RepeatCount, RepeatSubject = AgentRisk.IsSensitivePath(s.RepeatSubject) ? null : PetCut(s.RepeatSubject, 60), Completed = PetCut(s.WorkSummary, 60),
                Stalled = s.State == AgentState.Working && age >= TimeSpan.FromMinutes(2) && age < TimeSpan.FromMinutes(5),
                CanResume = AgentTerminal.CanOpen(s), Activity = activity,
            };
        }).ToList();
        PetBridge.PublishSessions(items);
    }

    /// Abre una sesión terminada en una terminal de la PC (`claude --resume`). Solo si la app lo permite (no hay otro escritor).
    public (bool Ok, string Message) PetResume(string id)
    {
        var s = AgentSessions.FirstOrDefault(x => SessionId(x) == id);
        if (s is null) return (false, "la sesión ya no está");
        if (!AgentTerminal.CanOpen(s)) return (false, "esa sesión sigue activa o no se puede retomar");
        return AgentTerminal.Open(s) ? (true, "terminal abierta en la PC") : (false, "no se pudo abrir la terminal");
    }

    private string? lastTeamState;

    /// Trabajo en curso, plan pendiente, cola e historial de Team. Sin el texto completo de las tareas (títulos de 80 caracteres) ni rutas.
    private DateTime lastTeamPublish, lastHistoryRead;
    private List<object> teamHistory = [];
    private string? lastRunId;

    public void PublishPetTeam()
    {
        if (DateTime.UtcNow - lastTeamPublish < TimeSpan.FromSeconds(3)) return;
        lastTeamPublish = DateTime.UtcNow;
        var svc = Hub.Team.Service;
        var run = svc.Current;
        var pending = svc.Pending;
        if (DateTime.UtcNow - lastHistoryRead > TimeSpan.FromSeconds(15) || svc.Current?.Id != lastRunId)   // al empezar o terminar un trabajo el historial se relee al momento
        {
            lastHistoryRead = DateTime.UtcNow;
            lastRunId = svc.Current?.Id;
            teamHistory = svc.Store.LoadHistory().Take(8).Select(e => (object)new
        {
            id = e.Id, title = PetCut(e.Title, 80), outcome = e.Outcome, verdict = e.Verdict, state = e.State, branch = "team/" + e.BranchId,
            started = new DateTimeOffset(e.Started.ToUniversalTime()).ToUnixTimeSeconds(), seconds = e.Seconds, tokens = e.Tokens, costUsd = e.CostUsd,
            files = e.Files, added = e.Added, removed = e.Removed, report = PetText(e.Report, 2000),
            // el detalle que se abre al tocar una entrada: la tarea, los pasos y los archivos que cambió (sin su contenido)
            task = PetText(e.Task, 600), preset = e.Preset, root = Path.GetFileName(e.Root.TrimEnd('\\', '/')),
            steps = e.Steps.Select(s => new { agent = s.Agent, role = s.Role, model = PetCut(s.Model, 40), ok = s.Ok, seconds = s.Seconds, tokens = s.Tokens }).ToList(),
            changes = PetChanges(e),
        }).ToList();
        }
        var history = teamHistory;
        object? current = run is null ? null : new
        {
            id = run.Id, title = PetCut(run.Job.Title, 80), preset = run.Job.Preset.ToString(), tokens = run.Tokens, costUsd = run.CostUsd,
            started = new DateTimeOffset(run.Started.ToUniversalTime()).ToUnixTimeSeconds(), maxTokens = run.Job.MaxTokens,
            steps = run.Job.Steps.Select((st, i) => new { agent = run.Agents[i].ToString(), role = st.Role.ToString(), write = st.CanWrite, state = run.States[i].ToString(), tokens = run.StepTokens[i], seconds = (int)run.StepSeconds[i] }).ToList(),
            log = run.Log.TakeLast(10).Select(l => PetCut(l.Text, 100)).ToList(),
        };
        object? plan = pending is null ? null : new { role = pending.Step.Role.ToString(), agent = pending.Step.Agent.ToString(), text = PetCut(pending.Text, 1500) };
        var (rootNames, defaultRoot) = PetTeamRootNames();
        var state = new { current, pending = plan, queued = svc.Queued.Select(j => PetCut(j.Title, 80)).ToList(), history, roots = rootNames, defaultRoot };
        PetBridge.PublishTeam(state);
    }

    /// Decide una petición desde la mascota. «Siempre» no existe aquí; lo delicado (AgentRisk) solo se permite si el usuario lo habilitó.
    public (bool Ok, string Message) PetDecide(string id, string verdict, string? message, IReadOnlyDictionary<string, string>? answers)
    {
        var item = AllApprovals.FirstOrDefault(a => a.PetId == id);
        if (item is null) return (false, "ya no está pendiente");
        var r = item.Request;
        if (verdict == "allow" && r.Risk is not null && !Prefs.Switch(PrefKeys.PetApproveRisky, false))
            return (false, "es delicado: solo se puede permitir desde la PC");
        string? note = string.IsNullOrWhiteSpace(message) ? null : (message.Length > 300 ? message[..300] : message);
        bool done;
        if (item.IsQuestion)
        {
            if (verdict != "allow") done = r.Decide(HookVerdict.Deny);
            else
            {
                var asks = AgentQuestions.Parse(r.ToolInput);
                if (answers is null || asks.Any(q => !answers.TryGetValue(q.Text, out var pick) || !pick.Split(", ").All(p => q.Options.Any(o => o.Label == p))))
                    return (false, "faltan respuestas válidas");
                done = r.Reply(asks.ToDictionary(q => q.Text, q => answers[q.Text]));
            }
        }
        else if (verdict == "allow") done = r.Decide(HookVerdict.Allow);
        else done = note is not null || item.IsPlan ? r.Reject(note ?? "Quiero cambios en el plan") : r.Decide(HookVerdict.Deny);
        return done ? (true, verdict == "allow" ? "permitido" : "denegado") : (false, "ya no está pendiente");
    }
}
