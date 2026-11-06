using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace IsTargetSleeping.Pet;

/// Herramienta que la app isTargetSleeping ejecuta (por el puente). El complemento solo valida y describe.
public abstract class BridgeTool(IAppBridge bridge, string name, string title, ToolRisk risk, JsonObject schema) : ITool
{
    public ToolSpec Spec { get; } = new(name, title, risk, schema);
    public abstract string? Validate(JsonElement args);
    public abstract string Describe(JsonElement args);
    protected virtual JsonObject Args(JsonElement args) => (JsonObject)JsonNode.Parse(args.GetRawText())!;
    public async Task<ToolResult> RunAsync(JsonElement args, CancellationToken ct)
    {
        var (ok, message) = await bridge.CallAsync(Spec.Name, Args(args), ct);
        return new ToolResult(ok, message);
    }

    protected static string? Str(JsonElement a, string k, int max) =>
        a.ValueKind == JsonValueKind.Object && a.TryGetProperty(k, out var p) && p.ValueKind == JsonValueKind.String && p.GetString() is { } s && s.Trim().Length > 0 && s.Length <= max && !s.Any(char.IsControl) ? s.Trim() : null;

    protected static JsonObject Obj(params (string, string)[] props)
    {
        var p = new JsonObject();
        foreach (var (k, t) in props) p[k] = new JsonObject { ["type"] = t };
        return new JsonObject { ["type"] = "object", ["properties"] = p };
    }
}

public sealed class PomodoroTool(IAppBridge bridge) : BridgeTool(bridge, "pomodoro.control", "Pomodoro", ToolRisk.Reversible, Obj(("action", "string")))
{
    public static readonly string[] Actions = ["start", "pause", "reset", "skip"];
    public override string? Validate(JsonElement a) => Str(a, "action", 10) is { } s && Array.IndexOf(Actions, s) >= 0 ? null : "action debe ser start, pause, reset o skip";
    public override string Describe(JsonElement a) => Str(a, "action", 10) switch { "start" => "Iniciar el pomodoro", "pause" => "Pausar el pomodoro", "reset" => "Reiniciar el pomodoro", _ => "Saltar a la siguiente fase del pomodoro" };
}

public sealed class TodoAddTool(IAppBridge bridge) : BridgeTool(bridge, "todo.add", "Tareas", ToolRisk.Reversible, Obj(("title", "string"), ("time", "string")))
{
    public override string? Validate(JsonElement a) => Str(a, "title", 120) is null ? "falta el título (máx. 120)" : a.TryGetProperty("time", out var t) && t.ValueKind == JsonValueKind.String && !Regex.IsMatch(t.GetString() ?? "", @"^([01]\d|2[0-3]):[0-5]\d$") ? "time debe ser HH:mm" : null;
    public override string Describe(JsonElement a) => "Añadir la tarea «" + Str(a, "title", 120) + "»";
}

public sealed class NoteAddTool(IAppBridge bridge) : BridgeTool(bridge, "note.add", "Notas", ToolRisk.Reversible, Obj(("text", "string")))
{
    public override string? Validate(JsonElement a) => Str(a, "text", 600) is null ? "falta el texto (máx. 600)" : null;
    public override string Describe(JsonElement a) => "Crear una nota: «" + Cut(Str(a, "text", 600)) + "»";
    private static string Cut(string? s) => s is null ? "" : s.Length > 60 ? s[..60] + "…" : s;
}

public sealed class NotifyTool(IAppBridge bridge) : BridgeTool(bridge, "notify.show", "Aviso en el notch", ToolRisk.Reversible, Obj(("title", "string"), ("text", "string")))
{
    public override string? Validate(JsonElement a) => Str(a, "text", 160) is null ? "falta el texto (máx. 160)" : null;
    public override string Describe(JsonElement a) => "Mostrar un aviso en el notch: «" + Str(a, "text", 160) + "»";
}

/// Crea un recordatorio en la base local (no toca la app: la entrega al notch la hace el programador).
public sealed class ReminderAddTool(SqlitePetStore store, Func<DateTimeOffset>? clock = null) : ITool
{
    public ToolSpec Spec { get; } = new("reminder.add", "Recordatorios", ToolRisk.Reversible, new JsonObject { ["type"] = "object" });
    public string? Validate(JsonElement a) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String && t.GetString()!.Length is > 0 and <= 140
        && a.TryGetProperty("due", out var d) && d.ValueKind == JsonValueKind.Number ? null : "faltan text y due";
    public string Describe(JsonElement a) => $"Recordar «{a.GetProperty("text").GetString()}» " + ReminderParser.Describe(DateTimeOffset.FromUnixTimeSeconds(a.GetProperty("due").GetInt64()), (clock ?? (() => DateTimeOffset.UtcNow))());
    public Task<ToolResult> RunAsync(JsonElement a, CancellationToken ct)
    {
        string repeat = a.TryGetProperty("repeat", out var r) && r.GetString() is "daily" or "weekly" or "every" ? r.GetString()! : "none";
        int every = a.TryGetProperty("every", out var e) && e.TryGetInt32(out var ev) ? ev : 0;
        long id = store.AddReminder(a.GetProperty("text").GetString()!, a.GetProperty("due").GetInt64(), repeat, every);
        return Task.FromResult(new ToolResult(true, "recordatorio " + id));
    }
}

public sealed class MemoryRememberTool(SqlitePetStore store, Func<DateTimeOffset>? clock = null) : ITool
{
    public const int MaxMemories = 60, MaxChars = 200;
    public ToolSpec Spec { get; } = new("memory.remember", "Memoria", ToolRisk.Reversible, new JsonObject { ["type"] = "object" });
    public string? Validate(JsonElement a) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String && t.GetString()!.Trim().Length is > 0 and <= MaxChars && !t.GetString()!.Any(char.IsControl) ? null : $"falta text (máx. {MaxChars})";
    public string Describe(JsonElement a) => "Recordar: «" + a.GetProperty("text").GetString()!.Trim() + "»";
    public Task<ToolResult> RunAsync(JsonElement a, CancellationToken ct)
    {
        if (store.Memories().Count >= MaxMemories) return Task.FromResult(new ToolResult(false, "ya tengo demasiados recuerdos; borra alguno"));
        long id = store.AddMemory(a.GetProperty("text").GetString()!.Trim(), (clock ?? (() => DateTimeOffset.UtcNow))().ToUnixTimeMilliseconds());
        return Task.FromResult(new ToolResult(true, "recuerdo " + id));
    }
}

/// Olvidar un recuerdo (por texto aproximado). Es sensible: por defecto pregunta.
public sealed class MemoryForgetTool(SqlitePetStore store) : ITool
{
    public ToolSpec Spec { get; } = new("memory.forget", "Olvidar un recuerdo", ToolRisk.Sensitive, new JsonObject { ["type"] = "object" });
    public string? Validate(JsonElement a) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String && t.GetString()!.Trim().Length is >= 2 and <= 100 ? null : "falta text (2-100)";
    private (long Id, string Text)? Find(JsonElement a)
    {
        string q = a.GetProperty("text").GetString()!.Trim();
        var hits = store.Memories().Where(m => m.Text.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        return hits.Count == 1 ? (hits[0].Id, hits[0].Text) : null;
    }
    public string Describe(JsonElement a) => Find(a) is { } m ? "Olvidar: «" + m.Text + "»" : "Olvidar un recuerdo (no hay una coincidencia única)";
    public Task<ToolResult> RunAsync(JsonElement a, CancellationToken ct)
    {
        if (Find(a) is not { } m) return Task.FromResult(new ToolResult(false, "no encontré un recuerdo único con eso"));
        store.DeleteMemory(m.Id);
        return Task.FromResult(new ToolResult(true, "olvidado"));
    }
}

/// Una orden clara del usuario en español que no depende de que el modelo elija bien la herramienta.
public sealed record ToolIntent(string Tool, JsonElement Args, string Say, string Expression, string? DoneSay = null);

public static class Intents
{
    private const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly Regex Pomodoro = new(@"\bpomodoro\b", O);
    private static readonly Regex PomStart = new(@"\b(inicia|empieza|arranca|comienza|activa|pon|lanza|prende)\w*\b", O);
    private static readonly Regex PomPause = new(@"\b(pausa|det[eé]n|detiene|para|frena|apaga)\w*\b", O);
    private static readonly Regex PomReset = new(@"\breinicia\w*|\bresetea\w*|\bvuelve a empezar\b", O);
    private static readonly Regex PomSkip = new(@"\b(salta|siguiente fase|pasa a la siguiente)\w*", O);
    private static readonly Regex Todo = new(@"^\s*(?:por favor[, ]*)?(?:a[nñ]ade|agrega|apunta|anota|crea|pon|a[nñ]ademe|agr[eé]game)\w*\s+(?:una\s+|la\s+)?tarea\s*(?:de\s+|para\s+|que\s+|:|llamada\s+)?\s*(?<t>.+)$", O);
    private static readonly Regex Note = new(@"^\s*(?:por favor[, ]*)?(?:crea|a[nñ]ade|anota|guarda|apunta|escribe|haz)\w*\s+(?:una\s+)?nota\s*(?:que dice|de|sobre|:)?\s*(?<t>.+)$", O);
    private static readonly Regex Remember = new(@"^\s*(?:por favor[, ]*)?(?:recuerda|acu[eé]rdate|memoriza|ten en cuenta)\s+que\s+(?<t>.+)$", O);
    private static readonly Regex Forget = new(@"^\s*(?:por favor[, ]*)?(?:olvida|borra de tu memoria|elimina de tu memoria)\w*\s+(?:que\s+|lo de\s+)?(?<t>.+)$", O);

    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();
    private static string Q(string s) => JsonSerializer.Serialize(s, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    public static ToolIntent? Detect(string text, DateTimeOffset now, bool usageAvailable)
    {
        string t = text.Trim();
        if (t.Length == 0 || t.Length > 300) return null;
        if (ReminderParser.LooksLikeReminder(t))
        {
            if (ReminderParser.Parse(t, now) is not { } p) return null;   // sin hora clara: que el modelo pregunte
            string when = ReminderParser.Describe(p.Due, now);
            string rep = p.Repeat switch { "daily" => ", todos los días", "weekly" => ", cada semana", "every" => $", cada {(p.EveryMin % 60 == 0 ? p.EveryMin / 60 + " h" : p.EveryMin + " min")}", _ => "" };
            return new ToolIntent("reminder.add", J($"{{\"text\":{Q(p.Text)},\"due\":{p.Due.ToUnixTimeSeconds()},\"repeat\":\"{p.Repeat}\",\"every\":{p.EveryMin}}}"),
                "Voy a apuntarlo.", "think", $"Listo, te aviso {when}{rep}: {p.Text}.");
        }
        if (Pomodoro.IsMatch(t))
        {
            string? action = PomReset.IsMatch(t) ? "reset" : PomSkip.IsMatch(t) ? "skip" : PomPause.IsMatch(t) ? "pause" : PomStart.IsMatch(t) ? "start" : null;
            if (action is not null)
                return new ToolIntent("pomodoro.control", J($"{{\"action\":\"{action}\"}}"), "Voy.", "think",
                    action switch { "start" => "Pomodoro en marcha. ¡A concentrarse!", "pause" => "Pomodoro en pausa.", "reset" => "Pomodoro reiniciado.", _ => "Listo, siguiente fase." });
        }
        if (Note.Match(t) is { Success: true } nm && Clean(nm.Groups["t"].Value) is { Length: > 0 } nt)
            return new ToolIntent("note.add", J($"{{\"text\":{Q(nt)}}}"), "Lo anoto.", "think", "Nota guardada.");
        if (Todo.Match(t) is { Success: true } tm && Clean(tm.Groups["t"].Value) is { Length: > 0 } tt)
        {
            string? time = null;
            var hm = Regex.Match(tt, @"\b(?:a las\s+)?([01]?\d|2[0-3]):([0-5]\d)\b");
            if (hm.Success) { time = $"{int.Parse(hm.Groups[1].Value):00}:{hm.Groups[2].Value}"; tt = Clean(tt.Remove(hm.Index, hm.Length)); }
            if (tt.Length == 0) return null;
            return new ToolIntent("todo.add", J($"{{\"title\":{Q(tt)}" + (time is null ? "" : $",\"time\":\"{time}\"") + "}"), "Lo añado.", "think", $"Tarea añadida: {tt}.");
        }
        if (Remember.Match(t) is { Success: true } rm && Clean(rm.Groups["t"].Value) is { Length: > 0 } rt)
            return new ToolIntent("memory.remember", J($"{{\"text\":{Q(rt)}}}"), "Me lo apunto.", "happy", $"Lo recordaré: {rt}.");
        if (Forget.Match(t) is { Success: true } fm && Clean(fm.Groups["t"].Value) is { Length: > 0 } ft)
            return new ToolIntent("memory.forget", J($"{{\"text\":{Q(ft)}}}"), "Déjame buscarlo.", "think", "Listo, lo olvidé.");
        return null;
    }

    private static string Clean(string s)
    {
        s = Regex.Replace(s.Trim(), @"\s+", " ").Trim(' ', '.', ',', ':', ';', '«', '»', '"');
        if (s.Length > 0) s = char.ToUpperInvariant(s[0]) + s[1..];
        return s.Length > 120 ? s[..120] : s;
    }
}

/// Estado de la app que la mascota puede mencionar (solo lectura): pomodoro, tareas y clima. Lo publica la app en `app-state.json`.
public static class AppStateBridge
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    public static string? Summary(string path, DateTimeOffset now)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var root = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
            if (now - DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("ts").GetInt64()) > StaleAfter) return null;
            var parts = new List<string>();
            if (root.TryGetProperty("pomodoro", out var p) && p.ValueKind == JsonValueKind.Object)
                parts.Add(p.GetProperty("running").GetBoolean() ? $"Pomodoro en marcha ({(p.GetProperty("focus").GetBoolean() ? "enfoque" : "pausa")}, quedan {Cut(p.GetProperty("remaining").GetString(), 10)})" : "Pomodoro detenido");
            if (root.TryGetProperty("todo", out var t) && t.ValueKind == JsonValueKind.Object)
            {
                var items = t.TryGetProperty("items", out var its) && its.ValueKind == JsonValueKind.Array ? its.EnumerateArray().Take(8).Select(i => (i.GetProperty("done").GetBoolean() ? "[x] " : "[ ] ") + Cut(i.GetProperty("title").GetString(), 60)).ToList() : [];
                parts.Add($"Tareas de hoy: {t.GetProperty("done").GetInt32()} de {t.GetProperty("total").GetInt32()} hechas" + (items.Count > 0 ? " (" + string.Join("; ", items) + ")" : ""));
            }
            if (root.TryGetProperty("weather", out var w) && w.ValueKind == JsonValueKind.Object && w.TryGetProperty("current", out var c) && c.GetString() is { Length: > 0 } cur)
                parts.Add("Clima actual: " + Cut(cur, 60));
            return parts.Count == 0 ? null : string.Join(". ", parts);
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { return null; }
    }

    private static string Cut(string? s, int max)
    {
        string t = new((s ?? "").Where(c => !char.IsControl(c)).ToArray());
        return t.Length > max ? t[..max] + "…" : t;
    }
}
