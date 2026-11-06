using System.Text.Json;
using System.Text.Json.Nodes;

namespace IsTargetSleeping.Pet;

public sealed record AskItem(string Question, string Header, bool Multi, IReadOnlyList<string> Options);

/// Una peticion de un agente (Claude Code) que espera respuesta en la PC: permiso, pregunta o plan. La publica la app en `approvals.json`.
public sealed record ApprovalItem(string Id, string Agent, string Kind, string Tool, string? Cwd, string? Detail, string? Risk,
                                  long Expires, IReadOnlyList<AskItem> Questions, string? Plan)
{
    public string? Diff { get; init; }
}

/// Lee `approvals.json` (la app lo renueva al cambiar la cola y cada 20 s mientras haya algo). Si no se renueva en 60 s, la app
/// dejo de correr y la lista queda vacia: nunca se ofrece decidir algo que ya no se puede contestar.
public sealed class ApprovalFeed
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(60);
    public const int MaxItems = 8;

    private readonly string path;
    private readonly Func<DateTimeOffset> clock;
    private readonly object gate = new();
    private List<ApprovalItem> current = [];

    /// (nuevas, ids que ya no estan)
    public event Action<IReadOnlyList<ApprovalItem>, IReadOnlyList<string>>? Changed;

    public ApprovalFeed(string path, Func<DateTimeOffset>? clock = null)
    {
        this.path = path;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public IReadOnlyList<ApprovalItem> Current { get { lock (gate) return [.. current]; } }
    public ApprovalItem? Find(string id) { lock (gate) return current.FirstOrDefault(a => a.Id == id); }

    /// La decision ya se tomo: se retira sin esperar a la siguiente lectura.
    public void Remove(string id) { lock (gate) current.RemoveAll(a => a.Id == id); }

    public void Poll()
    {
        var next = Read();
        IReadOnlyList<ApprovalItem> added;
        IReadOnlyList<string> removed;
        lock (gate)
        {
            added = next.Where(n => current.All(c => c.Id != n.Id)).ToList();
            removed = current.Where(c => next.All(n => n.Id != c.Id)).Select(c => c.Id).ToList();
            if (added.Count == 0 && removed.Count == 0) return;
            current = next;
        }
        Changed?.Invoke(added, removed);
    }

    private List<ApprovalItem> Read()
    {
        try
        {
            if (!File.Exists(path)) return [];
            var root = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
            var ts = DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("ts").GetInt64());
            var now = clock();
            if (now - ts > StaleAfter || ts - now > TimeSpan.FromMinutes(5)) return [];
            var list = new List<ApprovalItem>();
            foreach (var e in root.GetProperty("items").EnumerateArray())
            {
                string? id = S(e, "id");
                if (!PetProtocol.IsId(id)) continue;
                long expires = e.TryGetProperty("expires", out var ex) && ex.TryGetInt64(out var xv) ? xv : 0;
                if (expires != 0 && expires < now.ToUnixTimeSeconds()) continue;
                string kind = S(e, "kind") is "question" or "plan" ? S(e, "kind")! : "permission";
                var asks = new List<AskItem>();
                if (e.TryGetProperty("questions", out var qs) && qs.ValueKind == JsonValueKind.Array)
                    foreach (var q in qs.EnumerateArray().Take(4))
                    {
                        var opts = q.TryGetProperty("options", out var os) && os.ValueKind == JsonValueKind.Array
                            ? os.EnumerateArray().Where(o => o.ValueKind == JsonValueKind.String).Select(o => Cut(o.GetString(), 40)).Where(o => o.Length > 0).Take(6).ToList() : [];
                        if (opts.Count > 0 && S(q, "question") is { Length: > 0 } text)
                            asks.Add(new AskItem(Cut(text, 200), Cut(S(q, "header"), 30), q.TryGetProperty("multi", out var m) && m.ValueKind == JsonValueKind.True, opts));
                    }
                list.Add(new ApprovalItem(id!, Cut(S(e, "agent"), 20), kind, Cut(S(e, "tool"), 40), Short(S(e, "cwd")), Cut(S(e, "detail"), 240),
                    S(e, "risk") is { Length: > 0 } rk ? Cut(rk, 120) : null, expires, asks, S(e, "plan") is { Length: > 0 } pl ? Cut(pl, 800) : null) { Diff = S(e, "diff") is { Length: > 0 } df ? Cut(df, 1600) : null });
                if (list.Count == MaxItems) break;
            }
            return list;
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { return []; }
    }

    private static string? S(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static string Cut(string? s, int max)
    {
        string t = new((s ?? "").Where(c => !char.IsControl(c) || c is '\n' or '\t').ToArray());
        return t.Length > max ? t[..max] + "…" : t;
    }

    /// Solo las dos ultimas carpetas: lo justo para saber el proyecto, sin exponer la ruta completa del usuario.
    private static string? Short(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd)) return null;
        var parts = cwd.Replace('\\', '/').TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return Cut(string.Join('/', parts.TakeLast(2)), 80);
    }
}

/// Resuelve una peticion de un agente desde el celular o la placa. La decision pasa por la app (mismo camino que el notch).
public sealed class ApprovalService(ApprovalFeed feed, PolicyStore policy, IAppBridge bridge, IEventSink sink, Action<ActivityEntry>? log = null)
{
    /// (ok, codigo de error del protocolo o null, mensaje)
    public async Task<(bool Ok, string? Code, string Message)> DecideAsync(string id, string verdict, string? message,
        IReadOnlyDictionary<string, string>? answers, string channel, CancellationToken ct)
    {
        var item = feed.Find(id);
        if (item is null) return (false, "not_found", "ya no está pendiente");
        if (!policy.ApprovalsEnabled) return (false, "forbidden", "las aprobaciones desde aquí están desactivadas en los permisos");
        if (verdict == "allow" && item.Risk is not null && !policy.RiskyFromPhone)
            return (false, "forbidden", "es delicado: solo se puede permitir desde la PC");
        if (item.Kind == "question" && verdict == "allow" && (answers is null || answers.Count == 0)) return (false, "bad_json", "faltan respuestas");

        var args = new JsonObject { ["id"] = id, ["verdict"] = verdict };
        if (!string.IsNullOrWhiteSpace(message)) args["message"] = message.Length > 300 ? message[..300] : message;
        if (answers is { Count: > 0 })
        {
            var a = new JsonObject();
            foreach (var (k, v) in answers.Take(4)) a[k] = v;
            args["answers"] = a;
        }
        feed.Remove(id);   // gana la primera respuesta: nadie mas puede decidir esta
        var (ok, msg) = await bridge.CallAsync("agent.decide", args, ct);
        sink.Publish(PetProtocol.ApprovalDone(id, verdict, ok ? channel : "pc"));
        log?.Invoke(new ActivityEntry(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), channel, "agent.decide",
            $"{item.Agent}: {item.Tool} {item.Detail}".Trim(), (ok ? verdict : "ya resuelto") + (msg.Length > 0 ? ": " + msg : "")));
        return ok ? (true, null, msg) : (false, "not_found", msg);
    }
}
