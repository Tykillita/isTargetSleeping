using System.Text.Json;

namespace IsTargetSleeping.Pet;

public enum BrokerStatus { Done, Failed, Denied, Expired, Off, Unknown, Invalid, RateLimited }

public sealed record BrokerResult(BrokerStatus Status, string Message);

public sealed record PendingAction(string Id, string Tool, string Title, string Summary, ToolRisk Risk, DateTimeOffset Expires, string Source);

public sealed record ActivityEntry(long Ts, string Channel, string Tool, string Summary, string Outcome);

/// Decide si una accion de la mascota se ejecuta sola, pide confirmacion o se niega, segun la politica. Una confirmacion
/// pendiente se publica a todos los canales (celular, placa, PC) y gana la primera respuesta; una respuesta con un id que ya
/// no esta pendiente se ignora. Todo queda en el registro de actividad.
public sealed class ActionBroker
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(120);
    public const int MaxPending = 8, MaxPerMinute = 30, MaxArgsLogged = 200;

    private sealed class Entry(PendingAction pending, TaskCompletionSource<(bool Allow, string By)> tcs)
    {
        public PendingAction Pending { get; } = pending;
        public TaskCompletionSource<(bool Allow, string By)> Tcs { get; } = tcs;
    }

    private readonly object gate = new();
    private readonly ToolRegistry tools;
    private readonly PolicyStore policy;
    private readonly IEventSink sink;
    private readonly Action<ActivityEntry>? log;
    private readonly Func<DateTimeOffset> clock;
    private readonly TimeSpan ttl;
    private readonly Dictionary<string, Entry> pending = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> recent = new();

    public ActionBroker(ToolRegistry tools, PolicyStore policy, IEventSink sink, Action<ActivityEntry>? log = null,
                        Func<DateTimeOffset>? clock = null, TimeSpan? ttl = null)
    {
        this.tools = tools;
        this.policy = policy;
        this.sink = sink;
        this.log = log;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.ttl = ttl ?? DefaultTtl;
    }

    public ToolRegistry Tools => tools;
    public IReadOnlyList<PendingAction> Pending { get { lock (gate) return [.. pending.Values.Select(e => e.Pending)]; } }

    /// Ejecuta (o pide confirmar y espera) una herramienta. `source` = quien la origino ("chat", "recordatorio"…);
    /// `untrusted` = nace de contenido ajeno: nunca se ejecuta sola.
    public async Task<BrokerResult> ExecuteAsync(string toolName, JsonElement args, string source, bool untrusted, CancellationToken ct)
    {
        var tool = tools.Find(toolName);
        if (tool is null) return Finish(source, toolName, toolName, BrokerStatus.Unknown, "herramienta desconocida");
        if (tool.Validate(args) is { } why) return Finish(source, toolName, tool.Spec.Title, BrokerStatus.Invalid, why);
        string summary = tool.Describe(args);
        var mode = policy.Mode(tool.Spec, untrusted);
        if (mode == ToolMode.Off) return Finish(source, toolName, summary, BrokerStatus.Off, policy.Paused ? "la mascota está en pausa" : "esa acción está desactivada en los permisos");
        if (!AllowRate(toolName)) return Finish(source, toolName, summary, BrokerStatus.RateLimited, "demasiadas acciones seguidas");

        if (mode == ToolMode.Ask)
        {
            var (allow, by) = await AskAsync(tool, summary, source, ct);
            if (by == "expired") return Finish(source, toolName, summary, BrokerStatus.Expired, "nadie respondió a tiempo");
            if (!allow) return Finish(source, toolName, summary, BrokerStatus.Denied, "denegado desde " + by);
            source = source + "+" + by;
        }
        ToolResult result;
        try { result = await tool.RunAsync(args, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException) { result = new ToolResult(false, e.Message); }
        return Finish(source, toolName, summary, result.Ok ? BrokerStatus.Done : BrokerStatus.Failed, result.Message);
    }

    /// Resuelve una confirmacion pendiente. false si el id no esta pendiente (ya resuelta, caducada o inventada): se ignora.
    public bool Resolve(string id, string verdict, string channel)
    {
        Entry? e;
        lock (gate)
        {
            if (!pending.Remove(id, out e)) return false;
        }
        bool allow = verdict == "allow";
        e.Tcs.TrySetResult((allow, channel));
        sink.Publish(PetProtocol.Resolved(id, allow ? "allow" : "deny", channel));
        return true;
    }

    private async Task<(bool Allow, string By)> AskAsync(ITool tool, string summary, string source, CancellationToken ct)
    {
        Entry entry;
        lock (gate)
        {
            if (pending.Count >= MaxPending) return (false, "limite");
            var p = new PendingAction(PetProtocol.NewId(), tool.Spec.Name, tool.Spec.Title, summary, tool.Spec.Risk, clock() + ttl, source);
            entry = new Entry(p, new TaskCompletionSource<(bool, string)>(TaskCreationOptions.RunContinuationsAsynchronously));
            pending[p.Id] = entry;
        }
        sink.Publish(PetProtocol.Confirm(entry.Pending));
        using var reg = ct.Register(() => entry.Tcs.TrySetCanceled());
        var delay = Task.Delay(ttl, CancellationToken.None);
        try
        {
            var done = await Task.WhenAny(entry.Tcs.Task, delay);
            if (done == entry.Tcs.Task) return await entry.Tcs.Task;
        }
        catch (OperationCanceledException)
        {
            lock (gate) pending.Remove(entry.Pending.Id);
            sink.Publish(PetProtocol.Resolved(entry.Pending.Id, "deny", "cancelled"));
            throw;
        }
        lock (gate) pending.Remove(entry.Pending.Id);
        sink.Publish(PetProtocol.Resolved(entry.Pending.Id, "deny", "expired"));
        return (false, "expired");
    }

    private bool AllowRate(string tool)
    {
        var now = clock();
        lock (gate)
        {
            if (!recent.TryGetValue(tool, out var q)) recent[tool] = q = new Queue<DateTimeOffset>();
            while (q.Count > 0 && now - q.Peek() > TimeSpan.FromMinutes(1)) q.Dequeue();
            if (q.Count >= MaxPerMinute) return false;
            q.Enqueue(now);
            return true;
        }
    }

    private BrokerResult Finish(string source, string tool, string summary, BrokerStatus status, string message)
    {
        string text = summary.Length > MaxArgsLogged ? summary[..MaxArgsLogged] : summary;
        log?.Invoke(new ActivityEntry(clock().ToUnixTimeSeconds(), source, tool, text, status + (message.Length > 0 ? ": " + (message.Length > 160 ? message[..160] : message) : "")));
        return new BrokerResult(status, message);
    }
}
