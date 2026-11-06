namespace IsTargetSleeping.Pet;

/// Motor que genera la respuesta de la mascota (Ollama en produccion; falso en pruebas).
public interface IChatBackend
{
    /// Devuelve texto y expresion. Lanza ChatBackendException con un codigo del protocolo si falla.
    Task<ChatReply> GenerateAsync(IReadOnlyList<ChatTurn> history, CancellationToken ct);
}

public sealed record ChatTurn(string Role, string Text)
{
    /// Imágenes (base64) que acompañan este turno del usuario; solo el último turno las lleva al modelo.
    public IReadOnlyList<string>? Images { get; init; }
}
public sealed record ChatReply(string Text, string Expression, string Action = "none");

public sealed class ChatBackendException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// Destino de lo que emite el coordinador (mensajes ya serializados del protocolo).
public interface IEventSink
{
    void Publish(byte[] message);
}

/// Una generacion a la vez, cola maxima de 3, cancelacion que siempre vuelve a `idle`, y respuestas tardias
/// descartadas con un contador de generacion (si una respuesta llega despues de cancelar o de otro mensaje, se ignora).
public sealed class ChatCoordinator : IDisposable
{
    public const int MaxQueued = 3;
    public const int HistoryTurns = 8;

    private sealed class Job(string chatId, string text)
    {
        public string ChatId { get; } = chatId;
        public string Text { get; } = text;
        public IReadOnlyList<string>? Attachments { get; set; }
        public CancellationTokenSource Cts { get; } = new();
        public bool Cancelled { get; set; }
    }

    private readonly object gate = new();
    private readonly IChatBackend backend;
    private readonly IEventSink sink;
    private readonly SqlitePetStore? store;
    private readonly Func<DateTimeOffset> clock;
    private readonly Queue<Job> queue = new();
    private readonly List<ChatTurn> history = [];
    private Job? running;
    private long generation;
    private bool worker;
    private bool disposed;

    private readonly ActionBroker? broker;
    private readonly Func<string?>? usageProbe;
    private readonly Func<IReadOnlyList<(string Id, string Name)>>? agents;
    private readonly Func<IReadOnlyList<string>, (string Text, IReadOnlyList<string> Images)>? attachmentReader;
    public TimeSpan UsageWait { get; set; } = TimeSpan.FromSeconds(30);

    public ChatCoordinator(IChatBackend backend, IEventSink sink, SqlitePetStore? store = null, Func<DateTimeOffset>? clock = null,
                           ActionBroker? broker = null, Func<string?>? usageProbe = null, Func<IReadOnlyList<(string Id, string Name)>>? agents = null,
                           Func<IReadOnlyList<string>, (string Text, IReadOnlyList<string> Images)>? attachments = null)
    {
        this.attachmentReader = attachments;
        this.agents = agents;
        this.broker = broker;
        this.usageProbe = usageProbe;
        this.backend = backend;
        this.sink = sink;
        this.store = store;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// Acepta un mensaje. `busy` si ya hay una generacion y la cola esta llena. Devuelve el chat_id.
    public (string? ChatId, string? ErrorCode) Enqueue(string text, IReadOnlyList<string>? attachmentIds = null)
    {
        Job job;
        lock (gate)
        {
            if (disposed) return (null, "internal");
            if (queue.Count + (running is null ? 0 : 1) > MaxQueued) return (null, "busy");  // 1 en curso + 3 en cola como mucho
            job = new Job(PetProtocol.NewId(), text) { Attachments = attachmentIds };
            queue.Enqueue(job);
            if (!worker) { worker = true; _ = Task.Run(Pump); }
        }
        return (job.ChatId, null);
    }

    /// Cancela un chat (en cola o en curso). Siempre deja a la mascota en `idle`. false si no existe.
    public bool Cancel(string chatId)
    {
        Job? target = null;
        lock (gate)
        {
            if (running?.ChatId == chatId) target = running;
            else
            {
                var rest = queue.ToList();
                target = rest.FirstOrDefault(j => j.ChatId == chatId);
                if (target is not null)
                {
                    queue.Clear();
                    foreach (var j in rest.Where(j => j != target)) queue.Enqueue(j);
                }
            }
            if (target is null) return false;
            target.Cancelled = true;
            if (target == running) generation++;  // su respuesta llegara "tarde" y se descartara (un trabajo en cola no afecta al que corre)
        }
        target.Cts.Cancel();
        sink.Publish(PetProtocol.Status(chatId, "idle"));
        return true;
    }

    private async Task Pump()
    {
        while (true)
        {
            Job job;
            long myGen;
            lock (gate)
            {
                if (disposed || queue.Count == 0) { worker = false; running = null; return; }
                job = queue.Dequeue();
                if (job.Cancelled) continue;
                running = job;
                myGen = ++generation;
            }
            await Run(job, myGen);
        }
    }

    private async Task Run(Job job, long myGen)
    {
        sink.Publish(PetProtocol.Status(job.ChatId, "thinking"));
        List<ChatTurn> turns;
        lock (gate)
        {
            string turnText = job.Text;
            IReadOnlyList<string>? turnImages = null;
            if (job.Attachments is { Count: > 0 } ids && attachmentReader is not null)
            {
                var (extra, imgs) = attachmentReader(ids);
                turnText += extra;
                if (imgs.Count > 0) turnImages = imgs;
            }
            history.Add(new ChatTurn("user", turnText) { Images = turnImages });
            Trim();
            turns = [.. history];
        }
        store?.AddMessage(job.ChatId, "user", job.Text, null, clock().ToUnixTimeMilliseconds());

        bool haveUsage = usageProbe?.Invoke() is not null;
        ToolIntent? preset = null;
        if (broker is not null)
        {
            preset = Intents.Detect(job.Text, clock(), haveUsage);
            if (preset is null) preset = TeamIntents.Detect(job.Text, agents?.Invoke() ?? []);
            if (preset is null && AppIntents.Detect(job.Text, haveUsage) is { } legacy) preset = FromModel(legacy);
        }
        if (preset is not null)
        {
            await RunWithAction(job, myGen, turns, preset);
            sink.Publish(PetProtocol.Status(job.ChatId, "idle"));
            lock (gate) { if (running == job) running = null; }
            return;
        }

        ChatReply? reply = null;
        string? errorCode = null, errorText = null;
        try { reply = await backend.GenerateAsync(turns, job.Cts.Token); }
        catch (OperationCanceledException) { errorCode = "cancelled"; }
        catch (ChatBackendException e) { errorCode = e.Code; errorText = e.Message; }
        catch (Exception) { errorCode = "internal"; errorText = "fallo inesperado al generar la respuesta"; }

        bool stale;
        lock (gate) stale = job.Cancelled || generation != myGen;
        if (stale)
        {
            // Respuesta tardia: se descarta. La cancelacion ya publico `idle`.
            lock (gate) { if (running == job) running = null; }
            return;
        }

        if (reply is not null && broker is not null && Sanitize(reply) is { Action: not "none" } withAction)
        {
            await RunWithAction(job, myGen, turns, FromModel(withAction));
            sink.Publish(PetProtocol.Status(job.ChatId, "idle"));
            lock (gate) { if (running == job) running = null; }
            return;
        }

        if (reply is not null)
        {
            var clean = Sanitize(reply);
            lock (gate) { history.Add(new ChatTurn("assistant", clean.Text)); Trim(); }
            store?.AddMessage(job.ChatId, "pet", clean.Text, clean.Expression, clock().ToUnixTimeMilliseconds());
            sink.Publish(PetProtocol.Status(job.ChatId, "speaking"));
            sink.Publish(PetProtocol.Say(job.ChatId, clean.Text, clean.Expression, true));
        }
        else
        {
            sink.Publish(PetProtocol.Error(errorCode ?? "internal", errorText ?? "no se pudo generar la respuesta"));
        }
        sink.Publish(PetProtocol.Status(job.ChatId, "idle"));  // nunca se queda "pensando"
        lock (gate) { if (running == job) running = null; }
    }

    /// La mascota va a operar la app (o su propia agenda/memoria): dice lo que hace, el broker aplica los permisos (puede pedir confirmacion
    /// en cualquier canal) y, si era activar el widget de uso, espera los datos y responde de nuevo con ellos. Una sola vuelta: sin cadenas.
    private async Task RunWithAction(Job job, long myGen, List<ChatTurn> turns, ToolIntent intent)
    {
        bool Stale() { lock (gate) return job.Cancelled || generation != myGen; }
        void Final(ChatReply r)
        {
            lock (gate) { history.Add(new ChatTurn("assistant", r.Text)); Trim(); }
            store?.AddMessage(job.ChatId, "pet", r.Text, r.Expression, clock().ToUnixTimeMilliseconds());
            sink.Publish(PetProtocol.Status(job.ChatId, "speaking"));
            sink.Publish(PetProtocol.Say(job.ChatId, r.Text, r.Expression, true));
        }
        sink.Publish(PetProtocol.Status(job.ChatId, "speaking"));
        sink.Publish(PetProtocol.Say(job.ChatId, intent.Say, intent.Expression, false));
        BrokerResult res;
        try { res = await broker!.ExecuteAsync(intent.Tool, intent.Args, "chat", untrusted: false, job.Cts.Token); }
        catch (OperationCanceledException) { return; }
        if (Stale()) return;
        if (res.Status != BrokerStatus.Done)
        {
            Final(new ChatReply(res.Status switch
            {
                BrokerStatus.Denied => "Entendido, no lo hago.",
                BrokerStatus.Expired => "No me respondiste a tiempo, lo dejé sin hacer.",
                BrokerStatus.Off => "No puedo hacerlo: " + res.Message + ".",
                BrokerStatus.RateLimited => "Voy muy rápido; espera un momento y me lo vuelves a pedir.",
                _ => "No pude hacerlo: " + (res.Message.Length > 0 ? res.Message : "la app de isTargetSleeping no me respondió") + ".",
            }, "squint"));
            return;
        }
        bool usageOn = intent.Tool == "usage.set" && intent.Args.TryGetProperty("on", out var on) && on.ValueKind == System.Text.Json.JsonValueKind.True;
        if (!usageOn || usageProbe is null) { Final(new ChatReply(intent.DoneSay ?? intent.Say, intent.Expression == "think" ? "happy" : intent.Expression)); return; }

        sink.Publish(PetProtocol.Status(job.ChatId, "thinking"));
        var until = DateTime.UtcNow + UsageWait;
        try { while (usageProbe() is null && DateTime.UtcNow < until) await Task.Delay(300, job.Cts.Token); }
        catch (OperationCanceledException) { return; }
        if (Stale()) return;
        if (usageProbe() is null) { Final(new ChatReply("Lo activé, pero todavía no hay datos. Pregúntame otra vez en un momento.", "think")); return; }

        var follow = new List<ChatTurn>(turns) { new("assistant", intent.Say), new("user", "(aviso del sistema) Ya activé el widget de uso y ahora tienes sus datos. Responde a mi pregunta de antes usando esos números.") };
        ChatReply? second = null;
        try { second = await backend.GenerateAsync(follow, job.Cts.Token); }
        catch (OperationCanceledException) { return; }
        catch (ChatBackendException) { }
        if (Stale()) return;
        Final(second is null ? new ChatReply("Ya lo activé; pregúntame otra vez y te lo digo.", "smile") : Sanitize(second with { Action = "none" }));
    }

    private static ToolIntent FromModel(ChatReply r)
    {
        var (tool, args) = AppActionNames.ToTool(r.Action);
        return new ToolIntent(tool, args, r.Text, r.Expression, r.Text);
    }

    private void Trim()
    {
        while (history.Count > HistoryTurns) history.RemoveAt(0);
    }

    /// Texto <= 400 caracteres y expresion de la lista cerrada (si no, `smile`).
    public static ChatReply Sanitize(ChatReply r)
    {
        string text = System.Text.RegularExpressions.Regex.Replace(r.Text ?? "", @"\baction\s*[=:]\s*\w+\.?\s*", "").Trim();
        if (text.Length == 0) text = "…";
        if (PetProtocol.CountChars(text) > PetProtocol.MaxTextChars)
        {
            var runes = text.EnumerateRunes().Take(PetProtocol.MaxTextChars - 1);
            text = string.Concat(runes.Select(x => x.ToString())) + "…";
        }
        string expr = PetProtocol.IsExpression(r.Expression) ? r.Expression : "smile";
        return new ChatReply(text, expr, AppActionNames.Normalize(r.Action));
    }

    public void Dispose()
    {
        lock (gate) { disposed = true; running?.Cts.Cancel(); foreach (var j in queue) j.Cts.Cancel(); }
    }
}
