using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IsTargetSleeping;

public sealed record ChatTurn(string Role, string Content);
public enum ChatFailure { None, Offline, NoModel, Error }
public sealed record ChatResult(string Text, ChatFailure Failure, string? Detail = null);

/// Charla con la mascota por el modelo local que ya está cargado en Ollama. Nunca carga un modelo por su cuenta (la app existe para cuidar la RAM)
/// y nada sale del PC: es la API local de Ollama.
public sealed class ChatService(HttpClient? http = null, Uri? baseUri = null)
{
    public const int MaxTurns = 12;
    public const int MaxChars = 4000;
    private static readonly HttpClient Shared = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly HttpClient client = http ?? Shared;
    private readonly Uri baseUri = baseUri ?? OllamaApi.Base;

    public static string SystemPrompt(string language, string? personality = null) =>
        $"You are a small, friendly desktop companion that lives in the user's notch. Answer briefly (one to three sentences) in {language}. " +
        "Be warm and a little playful, never pushy. If you do not know something, say so." +
        (personality is null ? "" : $" Your character: {personality} Stay in character, but never claim abilities you do not have.");

    /// El primer modelo ya cargado; null si no hay ninguno.
    public static string? PickModel(IEnumerable<LoadedModel> loaded) => loaded.FirstOrDefault()?.Name;

    public static JsonObject Body(string model, string language, IEnumerable<ChatTurn> history, string? personality = null)
    {
        var messages = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = SystemPrompt(language, personality) });
        foreach (var turn in history.TakeLast(MaxTurns))
            messages.Add(new JsonObject { ["role"] = turn.Role == "assistant" ? "assistant" : "user", ["content"] = Cap(turn.Content) });
        return new JsonObject { ["model"] = model, ["messages"] = messages, ["stream"] = true };
    }

    private static string Cap(string text) => text.Length <= MaxChars ? text : text[..MaxChars];

    /// Un trozo de la respuesta en streaming (una línea JSON de Ollama). Null si no es una línea que se entienda.
    public static (string Content, bool Done, string? Error)? ParseChunk(string line)
    {
        try
        {
            if (JsonNode.Parse(line) is not JsonObject o) return null;
            return (o["message"]?["content"]?.GetValue<string>() ?? "", o["done"]?.GetValue<bool>() ?? false, o["error"]?.GetValue<string>());
        }
        catch (JsonException) { return null; }
    }

    public async Task<ChatResult> Ask(IReadOnlyList<ChatTurn> history, string? model, string language, Action<string>? onText, CancellationToken ct, string? personality = null)
    {
        if (model is null) return new("", ChatFailure.NoModel);
        var text = new StringBuilder();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "api/chat")) { Content = JsonContent.Create(Body(model, language, history, personality)) };
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) return new("", ChatFailure.Error, $"HTTP {(int)response.StatusCode}");
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token), Encoding.UTF8);
            while (await reader.ReadLineAsync(timeout.Token) is { } line)
            {
                if (ParseChunk(line) is not { } chunk) continue;
                if (chunk.Error is not null) return new(text.ToString(), ChatFailure.Error, chunk.Error);
                if (chunk.Content.Length > 0) { text.Append(chunk.Content); onText?.Invoke(text.ToString()); }
                if (chunk.Done) break;
            }
            return new(text.ToString().Trim(), ChatFailure.None);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return new(text.ToString().Trim(), ChatFailure.None); }
        catch (HttpRequestException) { return new("", ChatFailure.Offline); }
        catch (OperationCanceledException) { return new(text.ToString().Trim(), ChatFailure.Error, "timeout"); }
    }
}
