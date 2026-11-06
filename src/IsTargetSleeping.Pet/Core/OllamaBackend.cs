using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IsTargetSleeping.Pet;

/// Reserva de actividad: mientras la mascota conversa deja un archivo `lease.json` que isTargetSleeping (la app
/// principal) consulta antes de descargar el modelo por inactividad. Se renueva sola y caduca si el proceso muere.
public sealed class ActivityLease : IDisposable
{
    private readonly string path;
    private readonly Timer renew;
    private readonly Func<DateTimeOffset> clock;
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private ActivityLease(string path, Func<DateTimeOffset> clock)
    {
        this.path = path;
        this.clock = clock;
        Write();
        renew = new Timer(_ => Write(), null, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20));
    }

    public static ActivityLease Acquire(string dir, Func<DateTimeOffset>? clock = null)
    {
        Directory.CreateDirectory(dir);
        return new ActivityLease(Path.Combine(dir, "lease.json"), clock ?? (() => DateTimeOffset.UtcNow));
    }

    private void Write()
    {
        try
        {
            var json = JsonSerializer.Serialize(new { pid = Environment.ProcessId, until = clock().Add(Ttl).ToUnixTimeSeconds() });
            File.WriteAllText(path, json);
        }
        catch (IOException) { }
    }

    /// true si hay una reserva vigente (la app principal la usa para no descargar el modelo).
    public static bool IsActive(string dir, DateTimeOffset now)
    {
        try
        {
            var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "lease.json")));
            long until = doc.RootElement.GetProperty("until").GetInt64();
            int pid = doc.RootElement.GetProperty("pid").GetInt32();
            if (now.ToUnixTimeSeconds() >= until) return false;
            try { System.Diagnostics.Process.GetProcessById(pid); return true; }
            catch (ArgumentException) { return false; }  // el proceso ya no existe: reserva huerfana
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { return false; }
    }

    public void Dispose()
    {
        renew.Dispose();
        try { File.Delete(path); } catch (IOException) { }
    }
}

/// Ollama local por /api/chat con salida estructurada {text, expression}. No descarga modelos: si falta, informa.
public sealed class OllamaBackend(HttpClient http, OllamaOptions options, Func<string> persona, string leaseDir, bool reserveLocalModel = true) : IChatBackend
{
    public async Task<ChatReply> GenerateAsync(IReadOnlyList<ChatTurn> history, CancellationToken ct)
    {
        using var lease = reserveLocalModel ? ActivityLease.Acquire(leaseDir) : null;  // OAuth cloud usa Ollama solo como proxy y no reserva pesos locales.
        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = persona() } };
        foreach (var (user, json) in Persona.FewShot)
        {
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = user });
            messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = json });
        }
        for (int i = 0; i < history.Count; i++)
        {
            var t = history[i];
            var node = new JsonObject { ["role"] = t.Role, ["content"] = t.Text };
            if (i == history.Count - 1 && t.Images is { Count: > 0 } pics) node["images"] = new JsonArray(pics.Select(x => (JsonNode)x).ToArray());
            messages.Add(node);
        }

        var body = new JsonObject
        {
            ["model"] = options.Model,
            ["messages"] = messages,
            ["stream"] = false,
            ["think"] = false,
            ["keep_alive"] = options.KeepAlive,
            ["options"] = new JsonObject { ["num_ctx"] = options.NumCtx, ["num_predict"] = options.NumPredict, ["temperature"] = options.Temperature },
            ["format"] = Schema(),
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(options.BaseUri, "api/chat"))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(options.Timeout);
        HttpResponseMessage res;
        try { res = await http.SendAsync(req, cts.Token); }
        catch (HttpRequestException) { throw new ChatBackendException("model_unavailable", "Ollama no responde"); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ChatBackendException("model_unavailable", "Ollama tardo demasiado"); }

        using (res)
        {
            string text = await res.Content.ReadAsStringAsync(ct);
            if (res.StatusCode == HttpStatusCode.NotFound) throw new ChatBackendException("model_missing", "el modelo no esta instalado en Ollama");
            if (!res.IsSuccessStatusCode) throw new ChatBackendException("model_unavailable", "Ollama respondio " + (int)res.StatusCode);
            return Parse(text);
        }
    }

    /// Esquema de la salida: texto corto y una expresion de la lista cerrada.
    public static JsonObject Schema()
    {
        var enumArr = new JsonArray();
        foreach (var e in PetProtocol.Expressions) enumArr.Add(e);
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["text"] = new JsonObject { ["type"] = "string" },
                ["expression"] = new JsonObject { ["type"] = "string", ["enum"] = enumArr },
                ["action"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. AppActionNames.All.Select(a => (JsonNode?)JsonValue.Create(a))]) },
            },
            ["required"] = new JsonArray("text", "expression", "action"),
        };
    }

    /// Respuesta de /api/chat -> ChatReply. Si el modelo no devuelve JSON valido: `smile` y un mensaje corto.
    public static ChatReply Parse(string apiResponse)
    {
        try
        {
            using var outer = JsonDocument.Parse(apiResponse);
            string content = outer.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "";
            return ParseContent(content);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Fallback;
        }
    }

    public static ChatReply ParseContent(string content)
    {
        try
        {
            using var inner = JsonDocument.Parse(content);
            string? t = inner.RootElement.GetProperty("text").GetString();
            string? e = inner.RootElement.TryGetProperty("expression", out var ee) ? ee.GetString() : null;
            if (string.IsNullOrWhiteSpace(t)) return Fallback;
            string? a = inner.RootElement.TryGetProperty("action", out var aa) && aa.ValueKind == JsonValueKind.String ? aa.GetString() : null;
            return new ChatReply(t, PetProtocol.IsExpression(e) ? e! : "smile", AppActionNames.Normalize(a));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Fallback;
        }
    }

    public static readonly ChatReply Fallback = new("No te entendí bien, ¿me lo repites?", "smile");
}

public sealed class OllamaOptions
{
    public Uri BaseUri { get; init; } = new("http://127.0.0.1:11434/");
    public string Model { get; init; } = "gemma4:latest";   // configurable (--model); no se descarga nada solo
    public int NumCtx { get; init; } = 4096;
    public int NumPredict { get; init; } = 220;  // margen: un JSON cortado a mitad se descartaria como respuesta invalida
    public double Temperature { get; init; } = 0.6;
    public string KeepAlive { get; init; } = "10m";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(90);
}
