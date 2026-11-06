using static IsTargetSleeping.L10n;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IsTargetSleeping;

/// Un agente propio: instrucciones + motor + modelo + permisos acotados. Vive en `team\agents.json` (editable a mano).
public sealed record AgentProfile(string Id, string Name, string Description, string Engine, string Model, string Instructions,
    bool CanWrite, IReadOnlyList<string> Tools, string? Effort, long MaxTokens, int TimeoutMin, bool Enabled = true)
{
    public const string Claude = "claude", Codex = "codex", Ollama = "ollama";
}

/// Carga, valida y guarda los perfiles. Lista cerrada de herramientas: nada de Bash libre.
public static class AgentProfiles
{
    public static readonly string[] AllowedTools = ["Read", "Grep", "Glob", "Edit", "Write", "WebSearch", "WebFetch", "git diff", "git status"];
    static readonly string[] WriteTools = ["Edit", "Write"];
    static readonly string[] ClaudeModels = ["haiku", "sonnet", "opus"];
    static readonly Regex IdRx = new("^[a-z0-9][a-z0-9-]{0,29}$");
    static readonly Regex ModelRx = new(@"^[A-Za-z0-9][A-Za-z0-9._:/-]{0,59}$");
    public const int MaxProfiles = 20;

    public static string DefaultPath => Path.Combine(Paths.DataDir, "team", "agents.json");

    /// Perfiles válidos del archivo (los inválidos se descartan); vacío si no existe o está roto.
    public static IReadOnlyList<AgentProfile> Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 200_000) return [];
            return Parse(File.ReadAllText(path), out _);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    /// Lee el JSON (una lista); cada perfil que no cumple las reglas se salta y se explica en <paramref name="errors"/>.
    public static IReadOnlyList<AgentProfile> Parse(string json, out List<string> errors)
    {
        errors = [];
        var list = new List<AgentProfile>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) { errors.Add(tr("el archivo debe ser una lista")); return list; }
            foreach (var e in doc.RootElement.EnumerateArray().Take(MaxProfiles + 1))
            {
                var p = FromJson(e);
                if (Validate(p, list) is { } err) errors.Add($"{p.Id}: {err}"); else list.Add(p);
            }
        }
        catch (JsonException) { errors.Add(tr("JSON inválido")); }
        return list;
    }

    static AgentProfile FromJson(JsonElement e)
    {
        string S(string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        long N(string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.TryGetInt64(out var n) ? n : 0;
        bool B(string k, bool d) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : d;
        var tools = new List<string>();
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("tools", out var t) && t.ValueKind == JsonValueKind.Array)
            foreach (var x in t.EnumerateArray()) tools.Add(x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : "");
        return new(S("id"), S("name"), S("description"), S("engine").ToLowerInvariant(), S("model"), S("instructions"), B("canWrite", false), tools,
            S("effort") is { Length: > 0 } ef ? ef : null, N("maxTokens"), (int)Math.Clamp(N("timeoutMin"), 0, 1000), B("enabled", true));
    }

    /// Null = válido. Rechaza lo desconocido: motor, herramientas fuera de la lista, sin tope de tokens, ids repetidos.
    public static string? Validate(AgentProfile p, IReadOnlyList<AgentProfile> others)
    {
        if (!IdRx.IsMatch(p.Id)) return tr("id inválido (minúsculas, números y guiones, hasta 30)");
        if (others.Any(o => o.Id == p.Id)) return tr("id repetido");
        if (others.Count >= MaxProfiles) return tr("demasiados agentes (máx. %d)", MaxProfiles);
        if (p.Name.Trim().Length is 0 or > 40) return tr("nombre de 1 a 40 caracteres");
        if (p.Description.Length > 200) return tr("descripción de hasta 200 caracteres");
        if (p.Instructions.Length > 4000) return tr("instrucciones de hasta 4000 caracteres");
        if (p.Engine is not (AgentProfile.Claude or AgentProfile.Codex or AgentProfile.Ollama)) return tr("motor desconocido");
        if (p.Engine == AgentProfile.Claude && !ClaudeModels.Contains(p.Model)) return tr("modelo de Claude: haiku, sonnet u opus");
        if (p.Engine != AgentProfile.Claude && !ModelRx.IsMatch(p.Model)) return tr("modelo inválido");
        if (p.Tools.Any(t => !AllowedTools.Contains(t))) return tr("herramienta fuera de la lista permitida");
        if (p.Engine == AgentProfile.Ollama && p.CanWrite) return tr("un modelo local no puede escribir archivos");
        if (!p.CanWrite && p.Tools.Any(t => WriteTools.Contains(t))) return tr("tiene herramientas de escritura pero no puede escribir");
        if (p.MaxTokens is < 1_000 or > 2_000_000) return tr("tope de tokens obligatorio (1.000 a 2.000.000)");
        if (p.TimeoutMin is < 1 or > 60) return tr("tiempo máximo de 1 a 60 minutos");
        if (p.Effort is { } ef && ef is not ("low" or "medium" or "high")) return tr("esfuerzo: low, medium o high");
        return null;
    }

    /// Guarda la lista completa (reemplazo atómico). Quien llama ya validó cada perfil.
    public static void Save(IEnumerable<AgentProfile> profiles, string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, ToJson(profiles));
        File.Move(tmp, path, overwrite: true);
    }

    public static string ToJson(IEnumerable<AgentProfile> profiles) => JsonSerializer.Serialize(profiles.Select(p => new
    {
        id = p.Id, name = p.Name, description = p.Description, engine = p.Engine, model = p.Model, instructions = p.Instructions,
        canWrite = p.CanWrite, tools = p.Tools, effort = p.Effort, maxTokens = p.MaxTokens, timeoutMin = p.TimeoutMin, enabled = p.Enabled,
    }), new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    /// Las herramientas de Claude Code de un perfil, en la sintaxis de `--allowedTools`.
    public static string ClaudeTools(AgentProfile p) => string.Join(",", p.Tools.Select(t => t switch
    {
        "git diff" => "Bash(git diff:*)", "git status" => "Bash(git status:*)", _ => t,
    }));

    /// Un trabajo de Team con un solo paso que ejecuta este perfil. Si no escribe, responde (sin rama) leyendo la carpeta del proyecto;
    /// si escribe, trabaja en una rama aparte y muestra el plan antes (ReviewPlan).
    public static TeamJob Job(AgentProfile p, string task, string root)
    {
        var agent = p.Engine == AgentProfile.Codex ? TeamAgent.Codex : p.Model == "haiku" ? TeamAgent.Haiku : p.Model == "opus" ? TeamAgent.Opus : TeamAgent.Sonnet;
        var role = p.CanWrite ? TeamRole.Implement : TeamRole.Answer;
        var step = new TeamStep(agent, role, p.CanWrite, p.TimeoutMin, p.Name, Model: p.Engine == AgentProfile.Claude ? null : p.Model, Effort: p.Effort) { Profile = p, ReadsProject = !p.CanWrite && root.Length > 0 };
        var kind = p.CanWrite ? TaskKind.Feature : TaskKind.Question;
        return new TeamJob(task.Trim(), root, TeamPreset.Auto, [step], new TaskProfile(kind, false, 1, true, false)) { MaxTokens = p.MaxTokens, ReviewPlan = false, ReadsRoot = !p.CanWrite };
    }
}

/// Un modelo local de Ollama como agente: una sola respuesta, sin herramientas ni acceso a archivos.
public static class OllamaRunner
{
    public static async Task<AgentResult> RunAsync(AgentProfile p, string prompt, TimeSpan timeout, CancellationToken ct, string endpoint = "http://127.0.0.1:11434")
    {
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var messages = new List<object>();
        if (p.Instructions.Length > 0) messages.Add(new { role = "system", content = p.Instructions });
        messages.Add(new { role = "user", content = prompt });
        var body = JsonSerializer.Serialize(new { model = p.Model, messages, stream = false, options = new { num_predict = (int)Math.Min(p.MaxTokens, 32_000) } });
        try
        {
            using var res = await http.PostAsync(endpoint + "/api/chat", new StringContent(body, Encoding.UTF8, "application/json"), cts.Token);
            var json = await res.Content.ReadAsStringAsync(cts.Token);
            return Parse(res.IsSuccessStatusCode, json);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(false, "", Error: tr("Ollama superó %d min", p.TimeoutMin)) { Permanent = true }; }
        catch (HttpRequestException) { return new(false, "", Error: tr("Ollama no responde: ¿está abierto?")) { Permanent = true }; }
    }

    public static AgentResult Parse(bool httpOk, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (!httpOk) return new(false, "", Error: CodexRunner.Str(r, "error") ?? tr("Ollama devolvió un error")) { Permanent = true };
            var text = r.TryGetProperty("message", out var m) ? CodexRunner.Str(m, "content") ?? "" : "";
            return new(text.Trim().Length > 0, text.Trim(), CodexRunner.Num(r, "prompt_eval_count"), CodexRunner.Num(r, "eval_count"), Error: text.Trim().Length > 0 ? null : tr("Ollama no devolvió texto"));
        }
        catch (JsonException) { return new(false, "", Error: tr("Ollama no devolvió JSON")) { Permanent = true }; }
    }
}
