using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IsTargetSleeping.Pet;

public sealed record OpenAiChatOptions(Uri BaseUri, string Model, string ApiKey, bool JsonSchema = false);

/// API chat-completions compatible para Ollama Cloud y xAI. La clave solo vive en memoria.
public sealed class OpenAiChatBackend(HttpClient http, OpenAiChatOptions options, Func<string> persona) : IChatBackend
{
    public async Task<ChatReply> GenerateAsync(IReadOnlyList<ChatTurn> history, CancellationToken ct)
    {
        var messages = ProviderChat.Messages(persona(), history, gemini: false);
        var body = new JsonObject
        {
            ["model"] = options.Model,
            ["messages"] = messages,
            ["stream"] = false,
            ["temperature"] = 0.6,
            ["response_format"] = options.JsonSchema
                ? new JsonObject { ["type"] = "json_schema", ["json_schema"] = new JsonObject { ["name"] = "nimbo_reply", ["strict"] = true, ["schema"] = ProviderChat.StrictSchema() } }
                : new JsonObject { ["type"] = "json_object" },
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(options.BaseUri, "chat/completions"));
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.ApiKey);
        req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(90));
        HttpResponseMessage res;
        try { res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token); }
        catch (HttpRequestException) { throw new ChatBackendException("model_unavailable", "El proveedor de Nimbo no responde."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ChatBackendException("model_unavailable", "El proveedor de Nimbo tardó demasiado."); }

        using (res)
        {
            if (!res.IsSuccessStatusCode)
                throw new ChatBackendException("model_unavailable", ProviderChat.HttpError(res.StatusCode, "el proveedor de Nimbo"));
            try
            {
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(cts.Token));
                var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content");
                if (content.ValueKind == JsonValueKind.String) return OllamaBackend.ParseContent(content.GetString() ?? "");
                if (content.ValueKind == JsonValueKind.Array)
                    return OllamaBackend.ParseContent(string.Concat(content.EnumerateArray().Where(x => x.TryGetProperty("text", out _)).Select(x => x.GetProperty("text").GetString())));
                return OllamaBackend.Fallback;
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
            {
                return OllamaBackend.Fallback;
            }
        }
    }
}

/// Gemini GenerateContent: admite API key de AI Studio o credenciales ADC de Google Cloud.
public sealed class GeminiChatBackend(HttpClient http, string model, string? apiKey, string? project, Func<string> persona) : IChatBackend
{
    public async Task<ChatReply> GenerateAsync(IReadOnlyList<ChatTurn> history, CancellationToken ct)
    {
        string systemPrompt = ProviderChat.SystemPrompt(persona());
        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = systemPrompt }) },
            ["contents"] = ProviderChat.Messages("", history, gemini: true),
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["responseSchema"] = ProviderChat.GeminiSchema(),
                ["temperature"] = 0.6,
            },
        };
        using var req = new HttpRequestMessage(HttpMethod.Post,
            new Uri("https://generativelanguage.googleapis.com/v1beta/models/" + Uri.EscapeDataString(model) + ":generateContent"));
        if (!string.IsNullOrWhiteSpace(apiKey)) req.Headers.Add("x-goog-api-key", apiKey);
        else
        {
            if (string.IsNullOrWhiteSpace(project)) throw new ChatBackendException("model_unavailable", "Para Gemini OAuth configura GOOGLE_CLOUD_PROJECT y las credenciales ADC de gcloud.");
            string token = await ProviderProcess.GoogleAccessToken(ct);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("x-goog-user-project", project);
        }
        req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(90));
        HttpResponseMessage res;
        try { res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token); }
        catch (HttpRequestException) { throw new ChatBackendException("model_unavailable", "Gemini no responde."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ChatBackendException("model_unavailable", "Gemini tardó demasiado."); }

        using (res)
        {
            if (!res.IsSuccessStatusCode) throw new ChatBackendException("model_unavailable", ProviderChat.HttpError(res.StatusCode, "Gemini"));
            try
            {
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(cts.Token));
                string content = string.Concat(doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")
                    .EnumerateArray().Where(x => x.TryGetProperty("text", out _)).Select(x => x.GetProperty("text").GetString()));
                return OllamaBackend.ParseContent(content);
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
            {
                return OllamaBackend.Fallback;
            }
        }
    }
}

/// Claude Messages API. Claude subscription OAuth is intentionally not routed through Nimbo.
public sealed class AnthropicChatBackend(HttpClient http, string model, string apiKey, Func<string> persona) : IChatBackend
{
    public async Task<ChatReply> GenerateAsync(IReadOnlyList<ChatTurn> history, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = 512,
            ["system"] = ProviderChat.SystemPrompt(persona()),
            ["messages"] = ProviderChat.AnthropicMessages(history),
            ["output_config"] = new JsonObject
            {
                ["effort"] = "low",
                ["format"] = new JsonObject { ["type"] = "json_schema", ["schema"] = ProviderChat.StrictSchema() },
            },
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(90));
        HttpResponseMessage res;
        try { res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token); }
        catch (HttpRequestException) { throw new ChatBackendException("model_unavailable", "Claude API no responde."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ChatBackendException("model_unavailable", "Claude API tardó demasiado."); }
        using (res)
        {
            if (!res.IsSuccessStatusCode) throw new ChatBackendException("model_unavailable", ProviderChat.HttpError(res.StatusCode, "Claude API"));
            try
            {
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(cts.Token));
                string content = string.Concat(doc.RootElement.GetProperty("content").EnumerateArray()
                    .Where(x => x.TryGetProperty("text", out _)).Select(x => x.GetProperty("text").GetString()));
                return OllamaBackend.ParseContent(content);
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                return OllamaBackend.Fallback;
            }
        }
    }
}

/// Adaptador sin copia de credenciales: invoca Codex CLI y deja sus tokens en manos del CLI.
public sealed class CliChatBackend(string? model, Func<string> persona) : IChatBackend
{
    public async Task<ChatReply> GenerateAsync(IReadOnlyList<ChatTurn> history, CancellationToken ct)
    {
        const string name = "codex";
        string? exe = ProviderProcess.Find(name);
        if (exe is null) throw new ChatBackendException("model_unavailable", $"No encuentro {name} CLI en PATH.");

        string root = Path.Combine(Path.GetTempPath(), "its-nimbo-" + Guid.NewGuid().ToString("N"));
        string work = Path.Combine(root, "work"), images = Path.Combine(root, "images"), output = Path.Combine(root, "reply.json"), schema = Path.Combine(root, "schema.json");
        Directory.CreateDirectory(work);
        try
        {
            var imagePaths = await ProviderChat.WriteImages(history.LastOrDefault()?.Images, images, ct);
            string prompt = ProviderChat.Prompt(persona(), history);
            IReadOnlyList<string> args;
            await File.WriteAllTextAsync(schema, ProviderChat.StrictSchema().ToJsonString(), ct);
            var a = new List<string> { "exec", "--json", "--ephemeral", "--ignore-user-config", "--ignore-rules", "--skip-git-repo-check", "--sandbox", "read-only", "--disable", "shell_tool", "--disable", "code_mode_host", "--disable", "apps", "--disable", "browser_use", "--disable", "browser_use_external", "-C", work, "-o", output, "--output-schema", schema };
            if (!string.IsNullOrWhiteSpace(model)) a.AddRange(["--model", model]);
            foreach (var image in imagePaths) a.AddRange(["--image", image]);
            a.Add("-");
            args = a;

            ProviderProcess.Result result;
            try { result = await ProviderProcess.RunAsync(exe, args, prompt, work, TimeSpan.FromSeconds(90), ct); }
            catch (TimeoutException) { throw new ChatBackendException("model_unavailable", $"{name} tardó demasiado."); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
            { throw new ChatBackendException("model_unavailable", $"No pude iniciar {name} CLI."); }

            if (result.Exit != 0)
            {
                string all = result.Out + "\n" + result.Err;
                if (all.Contains("login", StringComparison.OrdinalIgnoreCase) || all.Contains("auth", StringComparison.OrdinalIgnoreCase))
                    throw new ChatBackendException("model_unavailable", "Inicia sesión con `codex login` y vuelve a probar.");
                throw new ChatBackendException("model_unavailable", $"{name} CLI no pudo generar la respuesta.");
            }
            string raw = File.Exists(output) ? await File.ReadAllTextAsync(output, ct) : "";
            return OllamaBackend.ParseContent(raw);
        }
        finally { try { Directory.Delete(root, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
    }
}

internal static class ProviderChat
{
    public static string SystemPrompt(string persona) => persona + " Devuelve exclusivamente un objeto JSON con text (string), expression y action. Usa expression de la lista permitida y action de la lista permitida; action debe ser none salvo que el usuario solicite una acción disponible.";

    public static JsonArray Messages(string persona, IReadOnlyList<ChatTurn> history, bool gemini)
    {
        var messages = new JsonArray();
        if (!gemini) messages.Add(new JsonObject { ["role"] = "system", ["content"] = SystemPrompt(persona) });
        foreach (var (user, json) in Persona.FewShot)
        {
            messages.Add(gemini
                ? new JsonObject { ["role"] = "user", ["parts"] = new JsonArray(new JsonObject { ["text"] = user }) }
                : new JsonObject { ["role"] = "user", ["content"] = user });
            messages.Add(gemini
                ? new JsonObject { ["role"] = "model", ["parts"] = new JsonArray(new JsonObject { ["text"] = json }) }
                : new JsonObject { ["role"] = "assistant", ["content"] = json });
        }
        for (int i = 0; i < history.Count; i++)
        {
            var turn = history[i];
            string role = gemini && turn.Role == "assistant" ? "model" : turn.Role;
            var parts = new JsonArray { new JsonObject { ["text"] = turn.Text } };
            if (i == history.Count - 1 && turn.Images is { Count: > 0 } pictures)
                foreach (string image in pictures)
                {
                    if (gemini) parts.Add(new JsonObject { ["inlineData"] = new JsonObject { ["mimeType"] = ChatImage.Mime(image), ["data"] = image } });
                    else parts.Add(new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = $"data:{ChatImage.Mime(image)};base64,{image}" } });
                }
            messages.Add(gemini
                ? new JsonObject { ["role"] = role, ["parts"] = parts }
                : new JsonObject { ["role"] = role, ["content"] = turn.Images is { Count: > 0 } && i == history.Count - 1 ? parts : JsonValue.Create(turn.Text) });
        }
        return messages;
    }

    public static JsonArray AnthropicMessages(IReadOnlyList<ChatTurn> history)
    {
        var messages = new JsonArray();
        foreach (var (user, json) in Persona.FewShot)
        {
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = user });
            messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = json });
        }
        for (int i = 0; i < history.Count; i++)
        {
            var turn = history[i];
            string role = turn.Role == "assistant" ? "assistant" : "user";
            if (i == history.Count - 1 && turn.Images is { Count: > 0 } pictures)
            {
                var content = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = turn.Text } };
                foreach (var image in pictures)
                    content.Add(new JsonObject { ["type"] = "image", ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = ChatImage.Mime(image), ["data"] = image } });
                messages.Add(new JsonObject { ["role"] = role, ["content"] = content });
            }
            else messages.Add(new JsonObject { ["role"] = role, ["content"] = turn.Text });
        }
        return messages;
    }

    public static string Prompt(string persona, IReadOnlyList<ChatTurn> history)
    {
        var turns = history.Select(t => new { role = t.Role, text = t.Text }).ToArray();
        return SystemPrompt(persona) + "\n\nEjemplos:\n" + string.Join("\n", Persona.FewShot.Select(x => $"Usuario: {x.User}\nNimbo: {x.Json}")) +
            "\n\nHistorial JSON (responde al último mensaje):\n" + JsonSerializer.Serialize(turns) +
            (history.LastOrDefault()?.Images is { Count: > 0 } ? "\nLas imágenes del último mensaje están adjuntas; considérelas al responder." : "");
    }

    public static JsonObject StrictSchema()
    {
        var schema = (JsonObject)OllamaBackend.Schema().DeepClone();
        schema["additionalProperties"] = false;
        return schema;
    }

    public static JsonObject GeminiSchema()
    {
        var expressions = new JsonArray();
        foreach (var value in PetProtocol.Expressions) expressions.Add(value);
        var actions = new JsonArray();
        foreach (var value in AppActionNames.All) actions.Add(value);
        return new JsonObject
        {
            ["type"] = "OBJECT",
            ["properties"] = new JsonObject
            {
                ["text"] = new JsonObject { ["type"] = "STRING" },
                ["expression"] = new JsonObject { ["type"] = "STRING", ["enum"] = expressions },
                ["action"] = new JsonObject { ["type"] = "STRING", ["enum"] = actions },
            },
            ["required"] = new JsonArray("text", "expression", "action"),
        };
    }

    public static async Task<IReadOnlyList<string>> WriteImages(IReadOnlyList<string>? base64, string dir, CancellationToken ct)
    {
        if (base64 is not { Count: > 0 }) return [];
        Directory.CreateDirectory(dir);
        var result = new List<string>();
        foreach (string image in base64.Take(2))
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String(image); }
            catch (FormatException) { continue; }
            var info = ChatImage.Type(bytes);
            string path = Path.Combine(dir, $"image-{result.Count + 1}{info.Extension}");
            await File.WriteAllBytesAsync(path, bytes, ct);
            result.Add(path);
        }
        return result;
    }

    public static string HttpError(HttpStatusCode status, string provider) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => $"{provider} rechazó la autenticación; revisa sus credenciales.",
        HttpStatusCode.NotFound => $"No se encontró el modelo configurado en {provider}.",
        HttpStatusCode.TooManyRequests => $"{provider} alcanzó su límite de uso.",
        _ => $"{provider} respondió con HTTP {(int)status}.",
    };
}

internal static class ChatImage
{
    public static (string Extension, string Mime) Type(ReadOnlySpan<byte> b)
    {
        if (b.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return (".png", "image/png");
        if (b.StartsWith(new byte[] { 255, 216, 255 })) return (".jpg", "image/jpeg");
        if (b.StartsWith("GIF8"u8)) return (".gif", "image/gif");
        if (b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8)) return (".webp", "image/webp");
        if (b.StartsWith("BM"u8)) return (".bmp", "image/bmp");
        return (".jpg", "image/jpeg");
    }
    public static string Mime(string base64)
    {
        try { return Type(Convert.FromBase64String(base64)).Mime; }
        catch (FormatException) { return "image/jpeg"; }
    }
}

internal static class ProviderProcess
{
    public sealed record Result(int Exit, string Out, string Err);

    public static string? Find(string name)
    {
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (string ext in new[] { ".exe", ".cmd", ".bat" })
                try { string file = Path.Combine(dir.Trim().Trim('"'), name + ext); if (File.Exists(file)) return file; } catch { }
        return null;
    }

    public static async Task<Result> RunAsync(string exe, IEnumerable<string> args, string stdin, string workDir, TimeSpan timeout, CancellationToken ct)
    {
        var arguments = args.ToArray();
        bool batch = OperatingSystem.IsWindows() && Path.GetExtension(exe) is var ext && (ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase));
        var psi = new ProcessStartInfo(batch ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe" : exe)
        {
            WorkingDirectory = workDir, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string secret in new[] { "OPENAI_API_KEY", "OLLAMA_API_KEY", "XAI_API_KEY", "ANTHROPIC_API_KEY", "GEMINI_API_KEY", "GOOGLE_API_KEY" }) psi.Environment.Remove(secret);
        if (batch)
        {
            string prefix = "ITS_NIMBO_" + Guid.NewGuid().ToString("N");
            psi.Environment[prefix + "_EXE"] = exe;
            var command = $"\"%{prefix}_EXE%\"";
            for (int i = 0; i < arguments.Length; i++) command += " " + BatchToken(arguments[i], psi, prefix + "_ARG_" + i);
            psi.Arguments = $"/d /v:off /s /c \"{command}\"";
        }
        else foreach (string arg in arguments) psi.ArgumentList.Add(arg);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("El proceso no arrancó.");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        try { await p.StandardInput.WriteAsync(stdin); } catch (IOException) { }
        try { p.StandardInput.Close(); } catch (IOException) { }
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            await p.WaitForExitAsync(CancellationToken.None);
            if (ct.IsCancellationRequested) throw;
            throw new TimeoutException();
        }
        return new(p.ExitCode, await stdout, await stderr);
    }

    public static async Task<string> GoogleAccessToken(CancellationToken ct)
    {
        // shortcut: ask gcloud for a fresh ADC token on each chat until token-refresh overhead needs profiling.
        string? exe = Find("gcloud");
        if (exe is null) throw new ChatBackendException("model_unavailable", "Para Gemini OAuth instala gcloud y configura `gcloud auth application-default login`.");
        try
        {
            var result = await RunAsync(exe, ["auth", "application-default", "print-access-token"], "", Path.GetTempPath(), TimeSpan.FromSeconds(15), ct);
            if (result.Exit == 0 && !string.IsNullOrWhiteSpace(result.Out)) return result.Out.Trim();
        }
        catch (TimeoutException) { }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException) { }
        throw new ChatBackendException("model_unavailable", "No pude obtener el token ADC de Gemini; ejecuta `gcloud auth application-default login`.");
    }

    public static void StartLogin(string provider)
    {
        string command = provider switch { "codex" => "codex", "gemini" => "gcloud", "ollama-cloud" => "ollama", _ => throw new ArgumentException("Proveedor OAuth no permitido.") };
        string exe = Find(command) ?? throw new InvalidOperationException($"No encuentro {command} en PATH de esta PC.");
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal,
            Arguments = provider switch
            {
                "codex" => "login",
                "gemini" => "auth application-default login --scopes=https://www.googleapis.com/auth/cloud-platform,https://www.googleapis.com/auth/generative-language.retriever",
                _ => "signin",
            },
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"No pude iniciar {command}.");
    }

    private static string BatchToken(string arg, ProcessStartInfo psi, string key)
    {
        if (arg.Length > 0 && !arg.Any(c => char.IsWhiteSpace(c) || "&|<>^()%!\"".Contains(c))) return arg;
        psi.Environment[key] = arg;
        return $"\"%{key}%\"";
    }
}
