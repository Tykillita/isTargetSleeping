using System.Globalization;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IsTargetSleeping;

/// Una barra de límite de un proveedor: cuánto va usado (0–100) y cuándo se reinicia.
public sealed record UsageLimit(string Label, double Percent, DateTimeOffset? ResetsAt);

/// Lo que se sabe del uso de un proveedor en un momento. `Extra` es la línea secundaria (otro cupo, reinicios gratis…).
public sealed record ProviderUsage(string Id, string Name, string Plan, IReadOnlyList<UsageLimit> Limits, string? Extra, DateTime Updated);

/// Una sesión OAuth ya iniciada en este equipo. El token solo vive en memoria el tiempo de una consulta: no se guarda, copia ni registra.
public sealed record UsageSession(string Provider, string Token, string? AccountId, string? Plan, bool Expired);

/// Uso de IA desde sesiones locales, APIs configuradas y el servidor local de Ollama. Cada adaptador enseña solo las métricas que su fuente entrega.
public static class AiUsage
{
    public static string HomeDir => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// Las sesiones iniciadas bajo `home` (la carpeta personal). Una credencial ilegible o sin token no cuenta.
    public static List<UsageSession> Detect(string home) => Detect(home, Environment.GetEnvironmentVariable("XAI_MANAGEMENT_API_KEY"), Environment.GetEnvironmentVariable("XAI_TEAM_ID"), TeamProcess.Find("muse") is not null);

    public static List<UsageSession> Detect(string home, string? xaiManagementKey, string? xaiTeamId, bool museAvailable)
    {
        var found = new List<UsageSession>();
        if (ReadJson(Path.Combine(home, ".claude", ".credentials.json")) is { } claude && claude.TryGetProperty("claudeAiOauth", out var oauth)
            && Str(oauth, "accessToken") is { Length: > 0 } claudeToken)
            found.Add(new("claude", claudeToken, null, Str(oauth, "subscriptionType"), ExpiredMs(oauth, "expiresAt")));
        if (ReadJson(Path.Combine(home, ".codex", "auth.json")) is { } codex && codex.TryGetProperty("tokens", out var tokens)
            && Str(tokens, "access_token") is { Length: > 0 } codexToken)
            found.Add(new("codex", codexToken, Str(tokens, "account_id"), null, JwtExpired(codexToken)));
        if (ReadJson(Path.Combine(home, ".gemini", "oauth_creds.json")) is { } gemini && Str(gemini, "access_token") is { Length: > 0 } geminiToken)
            found.Add(new("gemini", geminiToken, null, null, ExpiredMs(gemini, "expiry_date")));
        if (museAvailable) found.Add(new("muse", "", null, null, false));
        if (!string.IsNullOrWhiteSpace(xaiManagementKey) && !string.IsNullOrWhiteSpace(xaiTeamId))
            found.Add(new("grok-api", xaiManagementKey.Trim(), xaiTeamId.Trim(), "API", false));
        return found;
    }

    private static JsonElement? ReadJson(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonDocument.Parse(stream).RootElement.Clone();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static string? Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static double? Num(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static bool ExpiredMs(JsonElement e, string name) => Num(e, name) is { } ms && DateTimeOffset.FromUnixTimeMilliseconds((long)ms) <= DateTimeOffset.UtcNow;

    /// ¿Caducó un JWT? Solo se lee su `exp` (la parte pública); sin él se da por vigente y manda la respuesta del servidor.
    private static bool JwtExpired(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return false;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            return Num(doc.RootElement, "exp") is { } exp && DateTimeOffset.FromUnixTimeSeconds((long)exp) <= DateTimeOffset.UtcNow;
        }
        catch (Exception e) when (e is FormatException or JsonException) { return false; }
    }

    public static string DisplayName(string provider) => provider switch
    {
        "claude" => "Claude", "codex" => "Codex", "gemini" => "Gemini", "muse" => "Muse Code",
        "ollama" => "Ollama local", "grok-api" => "Grok API", _ => provider,
    };

    // MARK: respuestas

    private static DateTimeOffset? Date(string? s) => DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;
    private static string Title(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// `GET api.anthropic.com/api/oauth/usage`: `five_hour` y `seven_day` con `utilization` (0–100) y `resets_at`; el resto de ventanas
    /// semanales (`seven_day_opus`, `seven_day_sonnet`…) van a la línea secundaria.
    public static ProviderUsage? ParseClaude(string json, string? subscription, DateTime now)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var limits = new List<UsageLimit>();
            UsageLimit? Window(string key, string label) =>
                root.TryGetProperty(key, out var w) && w.ValueKind == JsonValueKind.Object && Num(w, "utilization") is { } u ? new(label, Math.Clamp(u, 0, 100), Date(Str(w, "resets_at"))) : null;
            if (Window("five_hour", "5h") is { } five) limits.Add(five);
            if (Window("seven_day", "week") is { } week) limits.Add(week);
            if (limits.Count == 0) return null;
            var extras = new List<string>();
            foreach (var p in root.EnumerateObject())
                if (p.Name.StartsWith("seven_day_", StringComparison.Ordinal) && p.Value.ValueKind == JsonValueKind.Object && Num(p.Value, "utilization") is { } u)
                    extras.Add($"{Title(p.Name["seven_day_".Length..])} {Math.Round(u):0}%");
            return new("claude", "Claude", Title(subscription ?? ""), limits, extras.Count > 0 ? string.Join(" · ", extras) : null, now);
        }
        catch (JsonException) { return null; }
    }

    /// `GET chatgpt.com/backend-api/wham/usage`: `plan_type` y `rate_limit.primary_window` (5 h) / `secondary_window` (semanal) con `used_percent` y
    /// `reset_at` (segundos Unix) o `reset_after_seconds`. Los reinicios completos gratis no los expone: no se enseñan.
    public static ProviderUsage? ParseCodex(string json, DateTime now)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("rate_limit", out var rate)) return null;
            var limits = new List<UsageLimit>();
            void Add(string key, string label)
            {
                if (!rate.TryGetProperty(key, out var w) || w.ValueKind != JsonValueKind.Object || Num(w, "used_percent") is not { } u) return;
                var reset = Num(w, "reset_at") is { } at ? DateTimeOffset.FromUnixTimeSeconds((long)at)
                    : Num(w, "reset_after_seconds") is { } after ? new DateTimeOffset(now.ToUniversalTime()).AddSeconds(after) : (DateTimeOffset?)null;
                limits.Add(new(label, Math.Clamp(u, 0, 100), reset));
            }
            Add("primary_window", "5h");
            Add("secondary_window", "week");
            if (limits.Count == 0) return null;
            return new("codex", "Codex", Title((Str(root, "plan_type") ?? "").Replace('_', ' ')), limits, null, now);
        }
        catch (JsonException) { return null; }
    }

    /// `POST cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota`: `buckets` con `modelId`, `remainingFraction` (0–1) y `resetTime`.
    /// Un cupo por modelo: el de menos fracción restante va primero; los Flash/Pro y demás, a la línea secundaria.
    public static ProviderUsage? ParseGemini(string json, string? tier, DateTime now)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("buckets", out var buckets) || buckets.ValueKind != JsonValueKind.Array) return null;
            var byModel = new Dictionary<string, UsageLimit>();
            foreach (var b in buckets.EnumerateArray())
            {
                if (Num(b, "remainingFraction") is not { } remaining || Str(b, "modelId") is not { Length: > 0 } model) continue;
                var limit = new UsageLimit(model, Math.Clamp((1 - remaining) * 100, 0, 100), Date(Str(b, "resetTime")));
                if (!byModel.TryGetValue(model, out var old) || limit.Percent > old.Percent) byModel[model] = limit;
            }
            if (byModel.Count == 0) return null;
            var ordered = byModel.Values.OrderByDescending(l => l.Percent).ThenBy(l => l.Label, StringComparer.Ordinal).ToList();
            var extras = ordered.Skip(2).Select(l => $"{l.Label} {Math.Round(l.Percent):0}%");
            return new("gemini", "Gemini", tier ?? "", ordered.Take(2).ToList(), ordered.Count > 2 ? string.Join(" · ", extras) : null, now);
        }
        catch (JsonException) { return null; }
    }

    /// `muse usage` es una consulta local sin turno de modelo. Solo se convierten a barras las líneas que identifican ventana y porcentaje.
    public static ProviderUsage? ParseMuse(string output, DateTime now)
    {
        var lines = Regex.Replace(output, @"\x1B\[[0-?]*[ -/]*[@-~]", "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var limits = new List<UsageLimit>();
        for (var i = 0; i < lines.Length; i++)
        {
            var labelSource = Regex.IsMatch(lines[i], @"(?i)5\s*[- ]?\s*hour|five[- ]hour|\b5h\b") ? lines[i]
                : Regex.IsMatch(lines[i], @"(?i)weekly|\bweek(?:ly)?\b|7\s*[- ]?\s*day") ? lines[i]
                : i > 0 ? lines[i - 1] : "";
            var label = Regex.IsMatch(labelSource, @"(?i)5\s*[- ]?\s*hour|five[- ]hour|\b5h\b") ? "5h"
                : Regex.IsMatch(labelSource, @"(?i)weekly|\bweek(?:ly)?\b|7\s*[- ]?\s*day") ? "week" : null;
            var match = Regex.Match(lines[i], @"(?<![\w.])(?<value>\d{1,3}(?:[.,]\d+)?)\s*%");
            if (label is null || !match.Success || !double.TryParse(match.Groups["value"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)) continue;
            limits.Add(new(label, Math.Clamp(percent, 0, 100), null));
        }
        if (limits.Count == 0) return null;
        return new("muse", "Muse Code", "", limits.DistinctBy(l => l.Label).ToList(), null, now);
    }

    /// `GET /api/ps` describe modelos que están cargados; no es una cuota ni un gasto.
    public static ProviderUsage? ParseOllama(string json, DateTime now)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array) return null;
            var names = models.EnumerateArray().Select(m => Str(m, "name") ?? Str(m, "model")).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).ToList();
            var extra = names.Count == 0 ? L10n.tr("Sin modelos cargados")
                : L10n.tr("%d modelos activos: %@", names.Count, string.Join(", ", names.Take(3)) + (names.Count > 3 ? "…" : ""));
            return new("ollama", "Ollama local", "Local", [], extra, now);
        }
        catch (JsonException) { return null; }
    }

    /// El uso de xAI es gasto de API del equipo, no el cupo de una suscripción Grok.
    public static ProviderUsage? ParseGrokApi(string json, DateTime now)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("timeSeries", out var series) || series.ValueKind != JsonValueKind.Array) return null;
            double total = 0;
            foreach (var group in series.EnumerateArray())
                if (group.TryGetProperty("dataPoints", out var points) && points.ValueKind == JsonValueKind.Array)
                    foreach (var point in points.EnumerateArray())
                        if (point.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array)
                            foreach (var value in values.EnumerateArray()) if (value.ValueKind == JsonValueKind.Number) total += value.GetDouble();
            var amount = total.ToString("0.00", CultureInfo.InvariantCulture);
            return new("grok-api", "Grok API", "API", [], L10n.tr("US$%@ este mes", amount), now);
        }
        catch (JsonException) { return null; }
    }

    public static async Task<ProviderUsage?> FetchOllamaAsync(HttpClient http, CancellationToken cancel = default)
    {
        try { return ParseOllama(await http.GetStringAsync("http://127.0.0.1:11434/api/ps", cancel), DateTime.Now); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException) { return null; }
    }

    // MARK: consulta

    /// Consulta el endpoint de uso del propio proveedor con el token en memoria. Null si la sesión caducó, no hay red o la respuesta no se entiende.
    public static async Task<ProviderUsage?> FetchAsync(UsageSession session, HttpClient http, CancellationToken cancel = default)
    {
        if (session.Expired) return null;
        try
        {
            var now = DateTime.Now;
            switch (session.Provider)
            {
                case "claude":
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
                    req.Headers.Add("anthropic-beta", "oauth-2025-04-20");
                    return ParseClaude(await Send(http, req, cancel), session.Plan, now);
                }
                case "codex":
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, "https://chatgpt.com/backend-api/wham/usage");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
                    if (session.AccountId is { Length: > 0 } account) req.Headers.Add("ChatGPT-Account-Id", account);
                    return ParseCodex(await Send(http, req, cancel), now);
                }
                case "gemini":
                {
                    const string api = "https://cloudcode-pa.googleapis.com/v1internal";
                    using var load = new HttpRequestMessage(HttpMethod.Post, api + ":loadCodeAssist")
                    { Content = new StringContent("{\"metadata\":{\"ideType\":\"IDE_UNSPECIFIED\",\"pluginType\":\"GEMINI\"}}", Encoding.UTF8, "application/json") };
                    load.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
                    using var loaded = JsonDocument.Parse(await Send(http, load, cancel));
                    var project = Str(loaded.RootElement, "cloudaicompanionProject");
                    var tier = loaded.RootElement.TryGetProperty("currentTier", out var t) ? Str(t, "name") : null;
                    using var quota = new HttpRequestMessage(HttpMethod.Post, api + ":retrieveUserQuota")
                    { Content = new StringContent(project is null ? "{}" : JsonSerializer.Serialize(new { project }), Encoding.UTF8, "application/json") };
                    quota.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
                    return ParseGemini(await Send(http, quota, cancel), tier, now);
                }
                case "muse":
                {
                    if (TeamProcess.Find("muse") is not { } muse) return null;
                    var run = await TeamProcess.RunAsync(muse, ["usage"], "", HomeDir, TimeSpan.FromSeconds(8), cancel);
                    return run.Exit == 0 && !run.TimedOut ? ParseMuse(run.Out, now) : null;
                }
                case "grok-api":
                {
                    if (session.AccountId is not { Length: > 0 } team) return null;
                    var utc = now.ToUniversalTime();
                    var request = new
                    {
                        analyticsRequest = new
                        {
                            timeRange = new
                            {
                                startTime = new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                                endTime = utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                                timezone = "Etc/GMT",
                            },
                            timeUnit = "TIME_UNIT_DAY",
                            values = new[] { new { name = "usd", aggregation = "AGGREGATION_SUM" } },
                            groupBy = new[] { "description" },
                            filters = Array.Empty<object>(),
                        },
                    };
                    using var req = new HttpRequestMessage(HttpMethod.Post, $"https://management-api.x.ai/v1/billing/teams/{Uri.EscapeDataString(team)}/usage")
                    {
                        Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"),
                    };
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
                    return ParseGrokApi(await Send(http, req, cancel), now);
                }
                default: return null;
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or Win32Exception or InvalidOperationException or IOException) { return null; }
    }

    private static async Task<string> Send(HttpClient http, HttpRequestMessage req, CancellationToken cancel)
    {
        using var response = await http.SendAsync(req, cancel);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancel);
    }

    // MARK: texto

    /// «hace 2 min» como el de la captura de referencia.
    public static string Ago(DateTime updated, DateTime now)
    {
        var m = (int)Math.Max(0, (now - updated).TotalMinutes);
        return m < 1 ? L10n.tr("ahora") : m < 60 ? L10n.tr("hace %d min", m) : L10n.tr("hace %d h", m / 60);
    }

    /// Cuándo se reinicia: la hora si es hoy, el día y la hora si es en la semana («sáb 23:00»), y la fecha si es más lejos.
    public static string Reset(DateTimeOffset? at, DateTime now)
    {
        if (at is not { } when) return "";
        var local = when.LocalDateTime;
        var culture = L10n.Effective == "es" ? new CultureInfo("es") : new CultureInfo("en");
        var time = local.ToString("HH:mm", culture);
        if (local.Date == now.Date) return L10n.tr("reinicia %@", time);
        if (local.Date < now.Date.AddDays(7)) return L10n.tr("reinicia %@", $"{local.ToString("ddd", culture)} {time}");
        return L10n.tr("reinicia %@", local.ToString("d MMM", culture));
    }
}
