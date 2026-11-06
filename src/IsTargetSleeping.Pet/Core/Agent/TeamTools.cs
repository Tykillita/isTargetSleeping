using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace IsTargetSleeping.Pet;

/// Pone en la cola de Team (en la app) un trabajo para uno de tus agentes propios. Siempre pregunta: es ejecutar código/modelos en la PC y gastar tokens.
/// La app vuelve a validar todo (agente activo, proyecto conocido, cola y presupuesto) y nunca fusiona nada sola.
public sealed class TeamEnqueueTool(IAppBridge bridge, AgentProfilesFile profiles) : BridgeTool(bridge, "team.enqueue", "Lanzar un agente", ToolRisk.Pc,
    Obj(("task", "string"), ("profile", "string"), ("root", "string")))
{
    static readonly Regex IdRx = new("^[a-z0-9][a-z0-9-]{0,29}$");

    public override string? Validate(JsonElement a)
    {
        if (Str(a, "task", 300) is null) return "falta la tarea (máx. 300 caracteres)";
        if (Str(a, "profile", 30) is not { } p || !IdRx.IsMatch(p)) return "falta el agente (su id)";
        if (a.TryGetProperty("root", out var r) && r.ValueKind == JsonValueKind.String && (r.GetString()!.Length > 60 || r.GetString()!.Any(char.IsControl))) return "proyecto inválido";
        return null;
    }

    public override string Describe(JsonElement a)
    {
        var prof = profiles.List().OfType<JsonObject>().FirstOrDefault(x => x["id"]?.GetValue<string>() == Str(a, "profile", 30));
        string who = prof is null ? "«" + Str(a, "profile", 30) + "» (no existe)" : $"«{prof["name"]}» ({prof["engine"]} {prof["model"]}, tope {prof["maxTokens"]} tokens)";
        string where = Str(a, "root", 60) is { } r ? " en «" + r + "»" : "";
        string mode = prof?["canWrite"]?.GetValue<bool>() == true ? "Trabajará en una rama aparte y te pedirá aprobar el plan; nada se fusiona solo." : "Solo lee: no cambia archivos.";
        return $"Pedirle a {who}{where}: «{Str(a, "task", 300)}». {mode}";
    }
}

/// Lanza una tarea con el reparto automático de Team (Claude y Codex). Siempre pregunta; la app elige agentes y pasos, enseña el plan antes de escribir y trabaja en una rama.
public sealed class TeamRunTool(IAppBridge bridge, string? uploads = null) : BridgeTool(bridge, "team.run", "Lanzar con el equipo", ToolRisk.Pc, Obj(("task", "string"), ("root", "string"), ("preset", "string"), ("effort", "string")))
{
    public static readonly Dictionary<string, string> Presets = new() { ["Auto"] = "Auto", ["Saving"] = "Ahorro", ["Quality"] = "Calidad", ["Design"] = "Diseño", ["Speed"] = "Velocidad", ["ClaudeOnly"] = "Solo Claude", ["CodexOnly"] = "Solo Codex" };
    public static readonly Dictionary<string, string> Efforts = new() { ["Auto"] = "Auto", ["Low"] = "Bajo", ["Medium"] = "Medio", ["High"] = "Alto", ["Max"] = "Máximo" };

    public override string? Validate(JsonElement a)
    {
        if (Str(a, "task", 300) is null) return "falta la tarea (máx. 300 caracteres)";
        if (a.TryGetProperty("root", out var r) && r.ValueKind == JsonValueKind.String && (r.GetString()!.Length > 60 || r.GetString()!.Any(char.IsControl))) return "proyecto inválido";
        if (a.TryGetProperty("preset", out var p) && !(p.ValueKind == JsonValueKind.String && Presets.ContainsKey(p.GetString()!))) return "modo desconocido";
        if (a.TryGetProperty("effort", out var e) && !(e.ValueKind == JsonValueKind.String && Efforts.ContainsKey(e.GetString()!))) return "esfuerzo desconocido";
        if (a.TryGetProperty("review_plan", out var rp) && rp.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return "review_plan debe ser true o false";
        if (a.TryGetProperty("attachments", out var at) && !(at.ValueKind == JsonValueKind.Array && at.GetArrayLength() <= 5 && at.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String && x.GetString()!.Length == 32 && x.GetString()!.All(Uri.IsHexDigit)))) return "adjuntos inválidos (máximo 5)";
        return null;
    }

    public override string Describe(JsonElement a)
    {
        string mode = a.TryGetProperty("preset", out var p) && Presets.TryGetValue(p.GetString() ?? "", out var pn) && pn != "Auto" ? $", modo {pn}" : ", reparto automático";
        string eff = a.TryGetProperty("effort", out var e) && Efforts.TryGetValue(e.GetString() ?? "", out var en) && en != "Auto" ? $", esfuerzo {en.ToLowerInvariant()}" : "";
        bool review = !(a.TryGetProperty("review_plan", out var rv) && rv.ValueKind == JsonValueKind.False);
        string att = "";
        if (a.TryGetProperty("attachments", out var at) && at.ValueKind == JsonValueKind.Array && at.GetArrayLength() > 0)
        {
            var names = at.EnumerateArray().Select(x => x.GetString() ?? "").Select(id => uploads is not null && Directory.Exists(Path.Combine(uploads, id)) ? Directory.GetFiles(Path.Combine(uploads, id)).Select(Path.GetFileName).FirstOrDefault() : null).Select(n => n ?? "(archivo)").ToList();
            att = $" Con {names.Count} adjunto{(names.Count == 1 ? "" : "s")}: {string.Join(", ", names.Take(5))}.";
        }
        return $"Lanzar con el equipo (Claude y Codex{mode}{eff}){(Str(a, "root", 60) is { } r ? " en «" + r + "»" : "")}: «{Str(a, "task", 300)}».{att} " +
               "Elegirá los agentes según la tarea, " + (review ? "te enseñará el plan antes de escribir" : "SIN revisar el plan: escribirá en cuanto empiece") + " y trabajará en una rama aparte; nada se fusiona solo.";
    }
}

/// Órdenes claras en español para lanzar un agente propio: «pídele a Revisor que …», «lanza el agente Revisor en isTargetSleeping para …».
public static class TeamIntents
{
    const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    static readonly Regex Ask = new(@"^\s*(?:por favor[, ]*)?(?:p[ií]de(?:le|selo)|dile|enc[aá]rga(?:le|selo))\s+a\s+(?:mi\s+agente\s+|el\s+agente\s+)?(?<p>.+?)\s+que\s+(?<t>.+)$", O);
    static readonly Regex Launch = new(@"^\s*(?:por favor[, ]*)?(?:lanza|ejecuta|usa|pon a trabajar|arranca)\w*\s+(?:a\s+)?(?:mi\s+|el\s+|al\s+)?(?:agente\s+)?(?<p>.+?)(?:\s+en\s+(?:el\s+proyecto\s+)?(?<r>[\w.-]{1,60}))?\s+(?:para|con|:)\s+(?<t>.+)$", O);

    static string Norm(string s) => new string(s.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant().Trim();

    const string TeamNoun = @"(?:el\s+)?(?:widget\s+)?(?:de\s+)?(?:claude\s*(?:\+|y|,)?\s*codex\s+)?(?:team|equipo)(?:\s+(?:de\s+)?claude\s*(?:\+|y|,)?\s*codex)?";
    static readonly Regex Auto = new(@"^\s*(?:por favor[, ]*)?(?:lanz|ejecut|arranc|inici|us|haz|cre)\w*(?:\s+una\s+tarea)?(?:\s+en\s+autom[aá]tico|\s+autom[aá]ticamente)?(?:\s+(?:con|usando|mediante|en))?\s+(?:en\s+autom[aá]tico\s+)?" + TeamNoun + @"(?:\s+en\s+(?:el\s+proyecto\s+)?(?<r>[\w.-]{1,60}))?\s*(?::|,|\bpara\b|\bque\b)\s*(?<t>.{4,})$", O);
    static readonly Regex AutoAsk = new(@"^\s*(?:por favor[, ]*)?(?:p[ií]de(?:le|selo)|dile|enc[aá]rga(?:le|selo))\s+al\s+" + TeamNoun + @"(?:\s+en\s+(?:el\s+proyecto\s+)?(?<r>[\w.-]{1,60}))?\s+que\s+(?<t>.{4,})$", O);

    /// Primero lo que se pide de forma explícita (equipo o agente propio); si no, si suena a trabajo de código, se propone mandarlo al equipo (siempre con confirmación).
    public static ToolIntent? Detect(string text, IReadOnlyList<(string Id, string Name)> agents) => DetectExplicit(text, agents) ?? Suggest(text);

    // El enrutador del chat: una orden en imperativo sobre código. Las preguntas («¿cómo arreglo…?») y la charla no cuentan.
    static readonly Regex CodeVerb = new(@"^\s*(?:por favor[, ]*)?(?:arregl|corrig|implement|refactoriz|a[nñ]ad|agreg|cre[ae]|program|migr|optimiz|escrib|solucion|repar|elimin|renombr|actualiz|integr|dise[nñ]|mejor)\w*\b", O);
    static readonly Regex CodeNoun = new(@"\b(bug|error(?:es)?|fallo|funci[oó]n|m[eé]todo|clase|archivos?|c[oó]digo|tests?|pruebas|pantalla|bot[oó]n|interfaz|api|endpoint|componente|m[oó]dulo|repo(?:sitorio)?|proyecto|base de datos|script|widget|p[aá]gina|login|servicio|consulta|ventana|men[uú])\b", O);
    static readonly Regex Question = new(@"[?¿]|^\s*(?:c[oó]mo|qu[eé]|por qu[eé]|cu[aá]l|cu[aá]ndo|d[oó]nde|me explicas|expl[ií]came)\b", O);
    static readonly Regex InProject = new(@"\b(?:en|del?)\s+(?:el\s+)?proyecto\s+(?<r>[\w.-]{1,60})", O);

    /// null si no parece una tarea de código. Pide al menos 5 palabras: «arregla el botón» solo no basta para armar un plan.
    public static ToolIntent? Suggest(string text)
    {
        string t = Regex.Replace(text.Trim(), @"\s+", " ");
        if (t.Length is < 20 or > 300 || Question.IsMatch(t) || !CodeVerb.IsMatch(t) || !CodeNoun.IsMatch(t) || t.Split(' ').Length < 5) return null;
        var args = new JsonObject { ["task"] = char.ToUpperInvariant(t[0]) + t[1..].TrimEnd('.') };
        if (InProject.Match(t) is { Success: true } pm) args["root"] = pm.Groups["r"].Value;
        return new ToolIntent("team.run", JsonDocument.Parse(args.ToJsonString()).RootElement.Clone(), "Eso suena a trabajo de código: lo preparo para el equipo.", "think",
            "Listo: la tarea está en la cola de Equipo. Te aviso cuando el plan espere tu aprobación o cuando termine.");
    }

    static ToolIntent? DetectExplicit(string text, IReadOnlyList<(string Id, string Name)> agents)
    {
        foreach (var rx in new[] { Auto, AutoAsk })
            if (rx.Match(text.Trim()) is { Success: true } am)
            {
                string at = Regex.Replace(am.Groups["t"].Value.Trim(), @"\s+", " ").Trim(' ', '.', ',', ';', '«', '»', '"');
                if (at.Length is < 4 or > 300) return null;
                var aargs = new JsonObject { ["task"] = char.ToUpperInvariant(at[0]) + at[1..] };
                if (am.Groups["r"].Success) aargs["root"] = am.Groups["r"].Value;
                return new ToolIntent("team.run", JsonDocument.Parse(aargs.ToJsonString()).RootElement.Clone(), "Déjame prepararlo.", "think",
                    "Listo: la tarea está en la cola de Equipo. Te aviso cuando el plan espere tu aprobación o cuando termine.");
            }
        if (agents.Count == 0) return null;
        foreach (var rx in new[] { Ask, Launch })
        {
            var m = rx.Match(text.Trim());
            if (!m.Success) continue;
            string want = Norm(m.Groups["p"].Value);
            var hit = agents.Where(x => Norm(x.Name) == want || x.Id == want || Norm(x.Name).Replace(' ', '-') == want.Replace(' ', '-')).ToList();
            if (hit.Count != 1) continue;
            string task = Regex.Replace(m.Groups["t"].Value.Trim(), @"\s+", " ").Trim(' ', '.', ',', ';', '«', '»', '"');
            if (task.Length is 0 or > 300) return null;
            string? root = m.Groups["r"].Success ? m.Groups["r"].Value : null;
            var args = new JsonObject { ["task"] = char.ToUpperInvariant(task[0]) + task[1..], ["profile"] = hit[0].Id };
            if (root is not null) args["root"] = root;
            return new ToolIntent("team.enqueue", JsonDocument.Parse(args.ToJsonString()).RootElement.Clone(), "Déjame prepararlo.", "think",
                $"Listo: «{hit[0].Name}» está en la cola de Equipo. Te aviso cuando haya algo que decidir o termine.");
        }
        return null;
    }
}

/// Lo que ha pasado en Team desde la última vez: un plan que espera, un trabajo que termina o que se queda sin presupuesto. Lee `team-state.json`.
public sealed class TeamWatcher(string path)
{
    string? plan; string? lastDone; bool primed;

    public IReadOnlyList<string> Check()
    {
        var msgs = new List<string>();
        try
        {
            if (!File.Exists(path)) return msgs;
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (root is null) return msgs;
            string? nowPlan = root["pending"] is JsonObject p ? (p["text"]?.GetValue<string>() ?? "").GetHashCode().ToString() : null;
            string? newest = root["history"] is JsonArray h && h.Count > 0 && h[0] is JsonObject e ? e["id"]?.GetValue<string>() : null;
            if (primed)
            {
                if (nowPlan is not null && nowPlan != plan) msgs.Add("Un plan del equipo espera tu aprobación.");
                if (newest is not null && newest != lastDone && root["history"]![0] is JsonObject d)
                {
                    string title = d["title"]?.GetValue<string>() ?? "una tarea";
                    msgs.Add(d["outcome"]?.GetValue<string>() switch
                    {
                        "Done" => $"El equipo terminó «{title}».",
                        "NeedsUser" => $"«{title}» se detuvo y necesita tu decisión.",
                        "Failed" => $"«{title}» falló.",
                        _ => $"«{title}» terminó.",
                    });
                }
            }
            (plan, lastDone, primed) = (nowPlan, newest, true);
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidOperationException) { }
        return msgs;
    }
}

/// Estado de Team para que Nimbo lo cuente si se lo preguntan («¿qué está haciendo el equipo?»). Solo lectura.
public static class TeamStateBridge
{
    public static string? Summary(string path, DateTimeOffset now)
    {
        try
        {
            if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path)) is not JsonObject r) return null;
            if (now - DateTimeOffset.FromUnixTimeSeconds(r["ts"]?.GetValue<long>() ?? 0) > TimeSpan.FromSeconds(90)) return null;
            string Cut(string? s) { var t = new string((s ?? "").Where(c => !char.IsControl(c)).ToArray()); return t.Length > 80 ? t[..80] + "…" : t; }
            var parts = new List<string>();
            if (r["current"] is JsonObject c) parts.Add($"Equipo trabajando en «{Cut(c["title"]?.GetValue<string>())}»");
            if (r["pending"] is JsonObject) parts.Add("hay un plan esperando aprobación");
            if (r["queued"] is JsonArray q && q.Count > 0) parts.Add($"{q.Count} en cola");
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or FormatException) { return null; }
    }
}
