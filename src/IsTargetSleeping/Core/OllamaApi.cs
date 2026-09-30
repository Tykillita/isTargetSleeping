using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping;

public sealed record LoadedModel(string Name, long Bytes, long Vram, int? Context)
{
    /// Parte del modelo que vive en la RAM del sistema: con GPU dedicada, lo que
    /// va a la VRAM no ocupa RAM.
    public long RamBytes => Math.Max(0, Bytes - Vram);
}

public sealed record InstalledModel(string Name, long Bytes, string? Quantization, bool Vision, bool Tools);

/// Progreso de `ollama pull`: el estado que da Ollama («pulling manifest»,
/// «pulling 6a0746a1ec1a», «verifying sha256 digest», «success») y la fracción
/// descargada de todas las capas vistas hasta ahora.
public readonly record struct PullProgress(string Status, double? Fraction, long Completed, long Total);

/// Lee el NDJSON de /api/pull línea a línea. Lógica pura, para probarla.
public sealed class PullParser
{
    private readonly Dictionary<string, (long Total, long Completed)> layers = [];
    public string? Error { get; private set; }
    public bool Done { get; private set; }

    public PullProgress? Feed(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        JsonObject? o;
        try { o = JsonNode.Parse(line) as JsonObject; } catch (JsonException) { return null; }
        if (o is null) return null;
        if (o["error"]?.GetValue<string>() is { } error) { Error = error; return null; }
        var status = o["status"]?.GetValue<string>() ?? "";
        if (status == "success") Done = true;
        if (o["digest"]?.GetValue<string>() is { } digest && o["total"] is { } t)
        {
            long total = LongOf(t), completed = o["completed"] is { } c ? LongOf(c) : 0;
            if (total > 0) layers[digest] = (total, Math.Min(completed, total));
        }
        long sum = layers.Values.Sum(l => l.Total), done = layers.Values.Sum(l => l.Completed);
        double? fraction = Done ? 1 : sum > 0 ? (double)done / sum : null;
        return new PullProgress(status, fraction, done, sum);
    }

    private static long LongOf(JsonNode n) { try { return n.GetValue<long>(); } catch { return 0; } }
}

public static class OllamaApi
{
    /// Respeta OLLAMA_HOST (del proceso o de las variables del usuario); si no, el puerto de siempre.
    public static readonly Uri Base = ResolveBase();

    private static Uri ResolveBase()
    {
        var raw = Environment.GetEnvironmentVariable("OLLAMA_HOST");
        if (string.IsNullOrWhiteSpace(raw))
        {
            try { raw = Registry.CurrentUser.OpenSubKey("Environment")?.GetValue("OLLAMA_HOST") as string; } catch { }
        }
        if (!string.IsNullOrWhiteSpace(raw))
        {
            var withScheme = raw.Contains("://") ? raw : $"http://{raw}";
            if (Uri.TryCreate(withScheme.Replace("0.0.0.0", "127.0.0.1"), UriKind.Absolute, out var url))
            {
                // «127.0.0.1» a secas es el puerto de Ollama, no el 80.
                if (url.IsDefaultPort && !raw.Contains(":80")) url = new UriBuilder(url) { Port = 11434 }.Uri;
                return url;
            }
        }
        return new Uri("http://127.0.0.1:11434");
    }

    public static string HostLabel => $"{Base.Host}:{Base.Port}";

    /// Descargar un modelo puede llevar horas: sin límite de tiempo, se corta con el token.
    private static readonly HttpClient stream = new(new SocketsHttpHandler { UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };

    /// `ollama pull` por la API, con progreso. Devuelve null si terminó bien o el error.
    /// Si se cancela, lanza OperationCanceledException.
    public static async Task<string?> Pull(string model, IProgress<PullProgress>? progress, CancellationToken ct)
    {
        var parser = new PullParser();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Base, "api/pull"))
        {
            Content = JsonContent.Create(new Dictionary<string, object> { ["model"] = model, ["stream"] = true }),
        };
        try
        {
            using var resp = await stream.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync(ct));
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (parser.Feed(line) is { } p) progress?.Report(p);
                if (parser.Error is { } e) return e;
            }
            if (!resp.IsSuccessStatusCode) return tr("Ollama respondió %d", (int)resp.StatusCode);
            return parser.Done ? null : tr("La descarga se cortó antes de terminar.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) { return e.Message; }
    }

    /// Borra un modelo del disco (`ollama rm`).
    public static async Task<string?> Delete(string model)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(Base, "api/delete"))
            {
                Content = JsonContent.Create(new Dictionary<string, object> { ["model"] = model }),
            };
            using var resp = await slow.SendAsync(request);
            if (resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadAsStringAsync();
            try { if (JsonNode.Parse(body)?["error"]?.GetValue<string>() is { } e) return e; } catch (JsonException) { }
            return tr("Ollama respondió %d", (int)resp.StatusCode);
        }
        catch (Exception e) { return e.Message; }
    }

    private static readonly HttpClient quick = new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(1.5) };

    /// Cargar un modelo grande en frío tarda; no cortamos antes de tiempo.
    private static readonly HttpClient slow = new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(900) };

    private static async Task<JsonNode?> Json(string path)
    {
        try
        {
            using var resp = await quick.GetAsync(new Uri(Base, path));
            if (!resp.IsSuccessStatusCode) return null;
            return JsonNode.Parse(await resp.Content.ReadAsStringAsync());
        }
        catch { return null; }
    }

    private static long Long(JsonNode? n) { try { return n?.GetValue<long>() ?? 0; } catch { return 0; } }

    public static async Task<string?> Version() => (await Json("api/version"))?["version"]?.GetValue<string>();

    public static async Task<List<LoadedModel>> Loaded()
    {
        var list = (await Json("api/ps"))?["models"] as JsonArray ?? [];
        return list.OfType<JsonObject>()
            .Where(m => m["name"] is not null)
            .Select(m => new LoadedModel(
                m["name"]!.GetValue<string>(),
                Long(m["size"]),
                Long(m["size_vram"]),
                m["context_length"] is { } c ? (int)Long(c) : null))
            .ToList();
    }

    public static async Task<List<InstalledModel>> Installed()
    {
        var list = (await Json("api/tags"))?["models"] as JsonArray ?? [];
        return list.OfType<JsonObject>()
            .Where(m => m["name"] is not null)
            .Select(m =>
            {
                var caps = (m["capabilities"] as JsonArray)?.Select(c => c?.GetValue<string>()).ToList() ?? [];
                var quant = m["details"]?["quantization_level"]?.GetValue<string>();
                return new InstalledModel(
                    m["name"]!.GetValue<string>(),
                    Long(m["size"]),
                    string.IsNullOrEmpty(quant) ? null : quant,
                    caps.Contains("vision"),
                    caps.Contains("tools"));
            })
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// Sin prompt, `/api/generate` solo carga o descarga: keep_alive -1 lo deja
    /// en memoria indefinidamente, 0 lo libera ya.
    public static async Task<string?> SetKeepAlive(string model, int keepAlive)
    {
        try
        {
            using var resp = await slow.PostAsJsonAsync(new Uri(Base, "api/generate"),
                new Dictionary<string, object> { ["model"] = model, ["keep_alive"] = keepAlive });
            if (resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadAsStringAsync();
            try { if (JsonNode.Parse(body)?["error"]?.GetValue<string>() is { } e) return e; } catch (JsonException) { }
            return tr("Ollama respondió %d", (int)resp.StatusCode);
        }
        catch (Exception e) { return e.Message; }
    }
}
