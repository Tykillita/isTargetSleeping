using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace IsTargetSleeping.Pet;

/// Los agentes propios de la app (`team\agents.json`, junto a la carpeta de datos del complemento). Las mismas reglas que valida la app;
/// el complemento solo lista, crea, reemplaza y borra perfiles; ejecutarlos es cosa de Team.
public sealed class AgentProfilesFile(string path)
{
    public static readonly string[] Tools = ["Read", "Grep", "Glob", "Edit", "Write", "WebSearch", "WebFetch", "git diff", "git status"];
    public const int MaxBytes = 16_384, MaxProfiles = 20;
    static readonly Regex IdRx = new("^[a-z0-9][a-z0-9-]{0,29}$"), ModelRx = new(@"^[A-Za-z0-9][A-Za-z0-9._:/-]{0,59}$");
    readonly object gate = new();

    public JsonArray List()
    {
        lock (gate)
            try { return File.Exists(path) && new FileInfo(path).Length < 200_000 && JsonNode.Parse(File.ReadAllText(path)) is JsonArray a ? a : []; }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    /// Null = válido; si no, el motivo. Solo mira el perfil, no los demás.
    public static string? Validate(JsonObject p)
    {
        string S(string k) => p[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
        long N(string k) => p[k] is JsonValue v && v.TryGetValue<long>(out var n) ? n : 0;
        bool B(string k) => p[k] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
        var tools = p["tools"] is JsonArray ta ? ta.Select(t => t is JsonValue v && v.TryGetValue<string>(out var s) ? s : "").ToList() : [];
        string engine = S("engine"), model = S("model");
        if (!IdRx.IsMatch(S("id"))) return "id inválido (minúsculas, números y guiones)";
        if (S("name").Trim().Length is 0 or > 40) return "el nombre debe tener de 1 a 40 caracteres";
        if (S("description").Length > 200) return "la descripción admite hasta 200 caracteres";
        if (S("instructions").Length > 4000) return "las instrucciones admiten hasta 4000 caracteres";
        if (engine is not ("claude" or "codex" or "ollama")) return "motor desconocido";
        if (engine == "claude" ? model is not ("haiku" or "sonnet" or "opus") : !ModelRx.IsMatch(model)) return "modelo inválido para ese motor";
        if (tools.Any(t => !Tools.Contains(t))) return "herramienta fuera de la lista permitida";
        if (engine == "ollama" && B("canWrite")) return "un modelo local no puede escribir archivos";
        if (!B("canWrite") && tools.Any(t => t is "Edit" or "Write")) return "tiene herramientas de escritura pero no puede escribir";
        if (N("maxTokens") is < 1_000 or > 2_000_000) return "el tope de tokens es obligatorio (1.000 a 2.000.000)";
        if (N("timeoutMin") is < 1 or > 60) return "el tiempo máximo va de 1 a 60 minutos";
        if (p["effort"] is JsonValue ev && ev.TryGetValue<string>(out var ef) && ef is not ("" or "low" or "medium" or "high")) return "esfuerzo: low, medium o high";
        return null;
    }

    /// Reemplaza (o crea) el perfil con ese id. Devuelve el motivo si no se pudo.
    public string? Put(string id, JsonObject p)
    {
        var clean = new JsonObject
        {
            ["id"] = id, ["name"] = p["name"]?.DeepClone(), ["description"] = p["description"]?.DeepClone() ?? "", ["engine"] = p["engine"]?.DeepClone(), ["model"] = p["model"]?.DeepClone(),
            ["instructions"] = p["instructions"]?.DeepClone() ?? "", ["canWrite"] = p["canWrite"]?.DeepClone() ?? false, ["tools"] = p["tools"]?.DeepClone() ?? new JsonArray(),
            ["effort"] = p["effort"]?.DeepClone(), ["maxTokens"] = p["maxTokens"]?.DeepClone(), ["timeoutMin"] = p["timeoutMin"]?.DeepClone(), ["enabled"] = p["enabled"]?.DeepClone() ?? true,
        };
        if (Validate(clean) is { } why) return why;
        lock (gate)
        {
            var all = List();
            int i = all.ToList().FindIndex(x => x?["id"]?.GetValue<string>() == id);
            if (i < 0 && all.Count >= MaxProfiles) return "demasiados agentes";
            if (i < 0) all.Add(clean); else all[i] = clean;
            Write(all);
        }
        return null;
    }

    public bool Delete(string id)
    {
        lock (gate)
        {
            var all = List();
            int i = all.ToList().FindIndex(x => x?["id"]?.GetValue<string>() == id);
            if (i < 0) return false;
            all.RemoveAt(i);
            Write(all);
            return true;
        }
    }

    void Write(JsonArray all)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, all.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        File.Move(tmp, path, overwrite: true);
    }
}
