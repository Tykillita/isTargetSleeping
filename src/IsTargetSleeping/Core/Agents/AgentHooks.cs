using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IsTargetSleeping;

public enum HookVerdict { Fold, Allow, Deny, Always }

/// Un evento de un agente (Claude Code) recibido por el hook. Solo los PermissionRequest esperan respuesta.
public sealed class HookRequest
{
    private readonly TaskCompletionSource<HookVerdict> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public required string Agent { get; init; }
    public required string Session { get; init; }
    public required string Event { get; init; }
    public string? Tool { get; init; }
    public string? Cwd { get; init; }
    /// Lo que se va a hacer, en una línea corta: el comando de Bash o la ruta del archivo. Nunca el contenido completo.
    public string? Detail { get; init; }
    /// Por qué esta petición es delicada (ver `AgentRisk`), o null. Una petición delicada no se permite con la tecla Y ni con «Siempre».
    public string? Risk { get; init; }
    /// El `tool_input` tal cual lo mandó el agente (para preguntas y planes).
    public JsonNode? ToolInput { get; init; }
    public HookKind Kind => AgentQuestions.KindOf(Tool);
    /// Las respuestas elegidas a una pregunta del agente (pregunta → opción), si se contestó desde aquí.
    public IReadOnlyDictionary<string, string>? Answers { get; private set; }
    /// Lo que se le dice al agente al rechazar (por ejemplo, que se quieren cambios en el plan).
    public string? DenyMessage { get; private set; }
    public DateTime At { get; } = DateTime.Now;
    public bool NeedsAnswer => Event is AgentHooks.PermissionEvent or AgentHooks.PreToolEvent;
    public Task<HookVerdict> Answer => answer.Task;
    public bool Decide(HookVerdict verdict) => answer.TrySetResult(verdict);
    /// Contesta una pregunta del agente con las opciones elegidas.
    public bool Reply(IReadOnlyDictionary<string, string> answers) { Answers = answers; return Decide(HookVerdict.Allow); }
    /// Rechaza con un motivo que el agente lee (en un plan: «quiero cambios»).
    public bool Reject(string message) { DenyMessage = message; return Decide(HookVerdict.Deny); }
}

/// Hook de Claude Code → esta app por un named pipe solo del usuario actual (sin red). Es opcional: si la app no corre,
/// el cliente sale al instante sin salida y Claude Code pregunta en su terminal como siempre.
public static class AgentHooks
{
    public const string PermissionEvent = "PermissionRequest";
    /// Las preguntas y los planes solo se pueden contestar por PreToolUse (allow + updatedInput); el instalador lo registra solo para esas dos herramientas.
    public const string PreToolEvent = "PreToolUse";
    public const string PreToolMatcher = "AskUserQuestion|ExitPlanMode";
    public const int MaxLine = 1 << 20;
    public const int DetailMax = 400;
    /// Margen para decidir: menos que el timeout (300 s) que se escribe en settings.json, para que el hook salga limpio antes que Claude Code.
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(280);
    /// El plazo que se puede elegir para responder a una petición: entre 10 s y el máximo (Claude Code corta el hook a los 300 s).
    public static TimeSpan DecisionWait(string? seconds) =>
        int.TryParse(seconds, out var s) ? TimeSpan.FromSeconds(Math.Clamp(s, 10, (int)Wait.TotalSeconds)) : Wait;
    public static readonly string[] Events = ["SessionStart", "UserPromptSubmit", PermissionEvent, PreToolEvent, "Stop", "Notification"];

    public static string PipeName => "isTargetSleeping.hooks." + WindowsIdentity.GetCurrent().User?.Value;

    /// Cliente: `isTargetSleeping --hook claude`. Lee el JSON del hook, se lo pasa a la app y, si pedía permiso, imprime la decisión.
    public static int RunClient(string agent, Stream stdin, Stream stdout, string? pipeName = null, TimeSpan? wait = null)
    {
        try
        {
            using var reader = new StreamReader(stdin, Encoding.UTF8);
            var payload = reader.ReadToEnd();
            if (payload.Length is 0 or > MaxLine) return 0;
            using var pipe = new NamedPipeClientStream(".", pipeName ?? PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect(150);   // la app no corre: no estorbar
            var line = new JsonObject { ["agent"] = agent, ["payload"] = JsonNode.Parse(payload) }.ToJsonString();
            var send = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            send.WriteLine(line);
            using var input = JsonDocument.Parse(payload);
            var evt = input.RootElement.TryGetProperty("hook_event_name", out var en) ? en.GetString() : null;
            if (evt is not (PermissionEvent or PreToolEvent)) return 0;
            var reply = ReadLine(pipe, wait ?? Wait + TimeSpan.FromSeconds(5));
            if (reply is null) return 0;
            using var doc = JsonDocument.Parse(reply);
            var verdict = doc.RootElement.TryGetProperty("verdict", out var v) ? v.GetString() : null;
            var behavior = verdict is "allow" or "always" ? "allow" : verdict == "deny" ? "deny" : null;
            if (behavior is null) return 0;   // plegado: que decida la terminal
            var message = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
            JsonObject answers = doc.RootElement.TryGetProperty("answers", out var an) && JsonNode.Parse(an.GetRawText()) is JsonObject a ? a : [];
            string output;
            if (evt == PreToolEvent)
            {
                // Preguntas y planes: «allow» solo vale con `updatedInput` (el input entero, con las respuestas si las hay).
                var hook = new JsonObject { ["hookEventName"] = PreToolEvent, ["permissionDecision"] = behavior };
                if (behavior == "allow")
                {
                    var updated = input.RootElement.TryGetProperty("tool_input", out var ti) && JsonNode.Parse(ti.GetRawText()) is JsonObject o ? o : [];
                    if (answers.Count > 0) updated["answers"] = answers;
                    hook["updatedInput"] = updated;
                }
                else hook["permissionDecisionReason"] = message ?? "Denied from isTargetSleeping";
                output = new JsonObject { ["hookSpecificOutput"] = hook }.ToJsonString();
            }
            else
            {
                var decision = new JsonObject { ["behavior"] = behavior };
                if (behavior == "deny") decision["message"] = message ?? "Denied from isTargetSleeping";
                output = new JsonObject
                {
                    ["hookSpecificOutput"] = new JsonObject { ["hookEventName"] = PermissionEvent, ["decision"] = decision },
                }.ToJsonString();
            }
            stdout.Write(new UTF8Encoding(false).GetBytes(output));
            stdout.Flush();
            return 0;
        }
        catch { return 0; }   // un hook nunca debe romper ni bloquear al agente
    }

    private static string? ReadLine(Stream pipe, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try { return new StreamReader(pipe, Encoding.UTF8).ReadLineAsync(cts.Token).AsTask().GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { return null; }
    }

    /// Convierte la línea del cliente en un evento. Null si no es de un agente conocido o no se entiende.
    public static HookRequest? Parse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var agent = root.GetProperty("agent").GetString();
            var payload = root.GetProperty("payload");
            if (agent is not { Length: > 0 and <= 24 } || !agent.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')) return null;
            string? Text(string name) => payload.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            var evt = Text("hook_event_name");
            if (evt is null || !Events.Contains(evt)) return null;
            var tool = Text("tool_name");
            if (evt == PreToolEvent && AgentQuestions.KindOf(tool) == HookKind.Permission) return null;   // un PreToolUse de otra herramienta no es cosa nuestra
            // El riesgo se mira sobre el texto completo, no sobre el recorte que se enseña.
            var full = payload.TryGetProperty("tool_input", out var toolInput) ? FullText(tool, toolInput) : null;
            var inputNode = payload.TryGetProperty("tool_input", out var rawInput) ? JsonNode.Parse(rawInput.GetRawText()) : null;
            return new HookRequest
            {
                Agent = agent,
                Session = Text("session_id") ?? "",
                Event = evt,
                Tool = tool,
                Cwd = Text("cwd"),
                Detail = full is not null ? Trim(full) : Text("message") ?? Text("prompt"),
                Risk = AgentRisk.Check(tool, full),
                ToolInput = inputNode,
            };
        }
        catch { return null; }
    }

    private static string? FullText(string? tool, JsonElement input)
    {
        string? Field(string name) => input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        if (tool == AgentQuestions.QuestionTool) return AgentQuestions.Parse(JsonNode.Parse(input.GetRawText())).FirstOrDefault()?.Text;
        if (tool == AgentQuestions.PlanTool) return Field("plan");
        return tool is "Bash" or "PowerShell" ? Field("command") : Field("file_path") ?? Field("path") ?? Field("pattern") ?? Field("url") ?? input.ToString();
    }

    private static string Trim(string text) => text.Length <= DetailMax ? text : text[..DetailMax] + "…";
}

/// Reglas «Siempre»: herramienta + proyecto (+ el comando exacto en Bash). Se guardan en un archivo propio, no en settings.json de Claude Code.
public sealed class HookRules
{
    private readonly string path;
    private readonly List<string> keys;
    public HookRules(string path)
    {
        this.path = path;
        try { keys = File.Exists(path) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? [] : []; }
        catch { keys = []; }
    }
    public IReadOnlyList<string> Keys => keys;

    // shortcut: Bash solo coincide con el comando idéntico; un patrón por prefijo dejaría pasar `npm publish` tras permitir `npm test`.
    public static string? KeyFor(HookRequest r) => r.Tool is null || r.Cwd is null || r.Kind != HookKind.Permission ? null
        : $"{r.Agent}|{r.Cwd.TrimEnd('\\', '/').ToLowerInvariant()}|{r.Tool}|{(r.Tool == "Bash" ? r.Detail : "")}";

    public bool Matches(HookRequest r) => r.Risk is null && r.Agent == "claude" && KeyFor(r) is { } k && keys.Contains(k);

    /// Una regla guardada, en claro, para enseñarla y poder revocarla.
    public sealed record Rule(string Key, string Agent, string Project, string Tool, string Command);

    public IReadOnlyList<Rule> Rules => keys.Select(k =>
    {
        var parts = k.Split('|', 4);
        return parts.Length < 4 ? null : new Rule(k, parts[0], Path.GetFileName(parts[1].TrimEnd('\\', '/')), parts[2], parts[3]);
    }).OfType<Rule>().ToList();

    public void Add(HookRequest r)
    {
        if (r.Risk is not null || r.Agent != "claude" || KeyFor(r) is not { } k || keys.Contains(k)) return;   // «Siempre» solo para Claude Code y nunca para lo delicado
        keys.Add(k);
        Save();
    }

    public void Remove(string key) { if (keys.Remove(key)) Save(); }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(keys));
        File.Move(temp, path, overwrite: true);
    }
}

/// Servidor del pipe. Cada conexión es un evento; los de permiso esperan a `Decide`. Si nadie responde a tiempo, se pliega y decide la terminal.
public sealed class HookServer : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly string pipeName;
    private readonly HookRules rules;
    /// Cuánto espera una respuesta antes de dejar la petición a la terminal. Se puede cambiar con el servidor en marcha.
    public TimeSpan Wait { get; set; }
    private readonly DecisionLog? log;
    public event Action<HookRequest>? Requested;
    /// Se respondió (o se dejó a la terminal): para refrescar lo que se enseña.
    public event Action? Decided;

    public HookServer(HookRules rules, string? pipeName = null, TimeSpan? wait = null, DecisionLog? log = null)
    {
        this.rules = rules;
        this.log = log;
        this.pipeName = pipeName ?? AgentHooks.PipeName;
        Wait = wait ?? AgentHooks.Wait;
    }

    public void Start() => Task.Run(Loop);

    private async Task Loop()
    {
        while (!stop.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop.Token);
                _ = Handle(pipe);
            }
            catch (OperationCanceledException) { pipe?.Dispose(); return; }
            catch { pipe?.Dispose(); await Task.Delay(500); }
        }
    }

    private async Task Handle(NamedPipeServerStream pipe)
    {
        using (pipe)
        {
            try
            {
                using var read = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                read.CancelAfter(TimeSpan.FromSeconds(5));
                var line = await new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true).ReadLineAsync(read.Token);
                if (line is null or { Length: > AgentHooks.MaxLine } || AgentHooks.Parse(line) is not { } request) return;
                bool auto = request.NeedsAnswer && rules.Matches(request);
                if (auto) { request.Decide(HookVerdict.Allow); Record(request, "auto"); }
                else Requested?.Invoke(request);
                if (!request.NeedsAnswer) return;
                var verdict = await Task.WhenAny(request.Answer, Task.Delay(Wait, stop.Token)) == request.Answer ? request.Answer.Result : HookVerdict.Fold;
                request.Decide(verdict);   // cierra la tarjeta si venció
                if (verdict == HookVerdict.Always && request.Risk is not null) verdict = HookVerdict.Allow;   // lo delicado se permite una vez, nunca «siempre»
                if (verdict == HookVerdict.Always) rules.Add(request);
                if (!auto) Record(request, verdict.ToString().ToLowerInvariant());
                var replyNode = new JsonObject { ["verdict"] = verdict.ToString().ToLowerInvariant() };
                if (request.Answers is { Count: > 0 } picked) replyNode["answers"] = new JsonObject(picked.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value)));
                if (request.DenyMessage is { } why) replyNode["message"] = why;
                var reply = replyNode.ToJsonString();
                await pipe.WriteAsync(Encoding.UTF8.GetBytes(reply + "\n"), stop.Token);
                await pipe.FlushAsync(stop.Token);
            }
            catch { /* el cliente se fue: el agente sigue por su terminal */ }
        }
    }

    private void Record(HookRequest r, string verdict)
    {
        log?.Add(new DecisionEntry(r.At, r.Agent, Path.GetFileName((r.Cwd ?? "").TrimEnd((char)92, '/')), r.Tool ?? "", r.Detail ?? "", verdict, r.Risk));
        Decided?.Invoke();
    }

    public void Dispose() => stop.Cancel();
}

/// Añade y quita nuestros hooks en el settings.json de Claude Code. Siempre con copia de seguridad y sin tocar los de otros.
public static class AgentHooksInstaller
{
    public static string DefaultSettings => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    /// Forma «exec» de Claude Code (`command` + `args`): el ejecutable se lanza directo, sin bash ni PowerShell, así que no hay comillas ni
    /// barras que acertar. Barras normales por si acaso.
    public static string Command(string exe) => exe.Replace((char)92, '/');

    private static bool Ours(JsonNode? hook)
    {
        if (hook?["command"]?.GetValue<string>() is not { } c || !c.Contains(AppInfo.Name, StringComparison.OrdinalIgnoreCase)) return false;
        return hook["args"] is JsonArray a && a.Any(x => x?.GetValue<string>() == "--hook")
            || c.Contains(" --hook claude", StringComparison.Ordinal);   // la forma con shell de una versión anterior
    }

    public static bool IsInstalled(string settings) => Read(settings)?["hooks"] is JsonObject h && h.Any(e => e.Value is JsonArray a && a.Any(g => g?["hooks"] is JsonArray l && l.Any(Ours)));

    /// Lo que se escribiría, para enseñarlo antes de confirmar.
    public static string Preview(string exe) => string.Join("\n", AgentHooks.Events.Select(e => $"hooks.{e} += {Command(exe)}"));

    public static void Install(string settings, string exe)
    {
        var root = Read(settings) ?? new JsonObject();
        Backup(settings);
        var hooks = root["hooks"] as JsonObject ?? (JsonObject)(root["hooks"] = new JsonObject());
        foreach (var evt in AgentHooks.Events)
        {
            var groups = hooks[evt] as JsonArray ?? (JsonArray)(hooks[evt] = new JsonArray());
            RemoveOurs(groups);
            var group = new JsonObject { ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = Command(exe), ["args"] = new JsonArray("--hook", "claude"), ["timeout"] = 300 }) };
            if (evt == AgentHooks.PreToolEvent) group["matcher"] = AgentHooks.PreToolMatcher;   // solo preguntas y planes: nunca estorba al resto de herramientas
            groups.Add(group);
        }
        Write(settings, root);
    }

    public static void Uninstall(string settings)
    {
        if (Read(settings) is not { } root || root["hooks"] is not JsonObject hooks) return;
        Backup(settings);
        foreach (var evt in hooks.Select(e => e.Key).ToList())
        {
            if (hooks[evt] is not JsonArray groups) continue;
            RemoveOurs(groups);
            if (groups.Count == 0) hooks.Remove(evt);
        }
        if (hooks.Count == 0) root.Remove("hooks");
        Write(settings, root);
    }

    private static void RemoveOurs(JsonArray groups)
    {
        foreach (var group in groups.ToList())
        {
            if (group?["hooks"] is not JsonArray list) continue;
            foreach (var hook in list.ToList()) if (Ours(hook)) list.Remove(hook);
            if (list.Count == 0) groups.Remove(group);
        }
    }

    private static JsonObject? Read(string path)
    {
        if (!File.Exists(path)) return null;
        // Un settings.json que no se entiende no se reescribe: se avisa y no se toca.
        return JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
            ?? throw new InvalidDataException("settings.json no es un objeto JSON");
    }

    private static void Backup(string path)
    {
        if (File.Exists(path)) File.Copy(path, $"{path}.its-backup-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
    }

    private static void Write(string path, JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, overwrite: true);
    }
}
