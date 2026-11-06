using static IsTargetSleeping.L10n;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace IsTargetSleeping;

public sealed record AgentResult(bool Ok, string Message, long InputTokens = 0, long OutputTokens = 0, string? SessionId = null, double? CostUsd = null, string? Error = null)
{
    /// Un fallo que repetir no arregla (el CLI no está instalado, o venció el tiempo): no se reintenta ni se escala a otro modelo.
    public bool Permanent { get; init; }
}

/// Construye y lee las salidas de `codex exec --json` y `claude -p --output-format json`.
/// El prompt viaja por stdin (sin problemas de comillas) y la salida se parsea sin leer nunca el razonamiento.
public static class CodexRunner
{
    /// <paramref name="model"/> y <paramref name="effort"/> en null dejan que Codex use los suyos (los de su config).
    public static IReadOnlyList<string> Args(string dir, bool canWrite, string lastMessageFile, string? model = null, string? effort = null, IReadOnlyList<string>? images = null, bool skipGitCheck = false)
    {
        var args = new List<string> { "exec", "--json" };
        // Una pregunta se responde en una carpeta vacía (no es un repositorio): Codex lo exige expresamente.
        if (skipGitCheck) args.Add("--skip-git-repo-check");
        // `-i` acepta varios archivos: va antes de otra opción para que no se lleve el resto.
        foreach (var image in images ?? []) if (TeamEffort.ImageExtensions.Contains(Path.GetExtension(image))) args.AddRange(["-i", image]);   // `-i` solo acepta imágenes
        args.AddRange(["-s", canWrite ? "workspace-write" : "read-only", "-C", dir, "-o", lastMessageFile]);
        if (model is { Length: > 0 }) args.AddRange(["-m", model]);
        if (effort is { Length: > 0 }) args.AddRange(["-c", $"model_reasoning_effort=\"{effort}\""]);
        args.Add("-");
        return args;
    }

    public static AgentResult Parse(IEnumerable<string> jsonLines, string? lastMessage = null)
    {
        string? thread = null, message = null, error = null;
        long input = 0, output = 0;
        bool completed = false;
        foreach (var line in jsonLines)
        {
            if (!line.StartsWith('{')) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                switch (root.TryGetProperty("type", out var t) ? t.GetString() : null)
                {
                    case "thread.started": thread = Str(root, "thread_id"); break;
                    case "item.completed" when root.TryGetProperty("item", out var item) && Str(item, "type") == "agent_message":
                        message = Str(item, "text"); break;
                    case "turn.completed":
                        completed = true;
                        // Se cuenta lo que no vino de la caché: la entrada cacheada (el prompt del sistema se repite en cada vuelta) inflaría el total.
                        if (root.TryGetProperty("usage", out var u)) { input += Math.Max(0, Num(u, "input_tokens") - Num(u, "cached_input_tokens")); output += Num(u, "output_tokens"); }
                        break;
                    case "turn.failed" or "error":
                        error = root.TryGetProperty("error", out var e) ? Str(e, "message") ?? e.ToString() : Str(root, "message") ?? tr("error de Codex");
                        break;
                }
            }
            catch (JsonException) { }
        }
        var text = lastMessage?.Trim() is { Length: > 0 } l ? l : message ?? "";
        return new(completed && error is null, text, input, output, thread, null, error ?? (completed ? null : tr("Codex no terminó el turno")));
    }

    internal static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    internal static long Num(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : 0;
}

public static class ClaudeRunner
{
    public static string Model(TeamAgent a) => a switch { TeamAgent.Haiku => "haiku", TeamAgent.Opus => "opus", _ => "sonnet" };

    /// Herramientas acotadas: leer y mirar el diff siempre; editar solo si el paso escribe.
    public static IReadOnlyList<string> Args(TeamAgent agent, bool canWrite, string? effort = null, IReadOnlyList<string>? imageDirs = null, bool answer = false, bool stream = false, bool readProject = false)
    {
        var args = new List<string>
    {
        // Sin los servidores MCP, las skills ni los ajustes de usuario: en este PC cada paso arrancaba con ~113.000 tokens de contexto
        // (medido); con estas opciones, ~7.000 y, con la caché, menos de 1.000 nuevos. Solo cuenta el CLAUDE.md del proyecto.
        // stream-json (con --verbose) da un evento por línea mientras trabaja: es lo que alimenta el registro en vivo; el resultado es su último evento.
        "-p", "--model", Model(agent), "--output-format", stream ? "stream-json" : "json", "--no-session-persistence",
        "--strict-mcp-config", "--disable-slash-commands", "--setting-sources", "project",
        // Nunca el modo plan de Claude Code: pensado para uso interactivo, termina llamando a ExitPlanMode (que aquí no existe), escribe el plan en un archivo
        // de ~/.claude/plans y devuelve solo un resumen disculpándose. Un paso de solo lectura va en modo normal: lo que no está en --allowedTools se deniega.
        "--permission-mode", canWrite && !answer ? "acceptEdits" : "default",
        "--allowedTools", answer ? (readProject ? "Read,Grep,Glob,WebSearch,WebFetch" : "Read,WebSearch,WebFetch") : canWrite ? "Read,Grep,Glob,Edit,Write,Bash(git diff:*),Bash(git status:*)" : "Read,Grep,Glob,Bash(git diff:*),Bash(git status:*)",
    };
        if (stream) args.Add("--verbose");
        if (effort is { Length: > 0 }) args.AddRange(["--effort", effort]);
        // Las imágenes viven fuera del repositorio: Claude solo puede leerlas si se le abre su carpeta.
        foreach (var dir in imageDirs ?? []) args.AddRange(["--add-dir", dir]);
        return args;
    }

    /// Aplica un agente propio a los argumentos: sus herramientas (lista cerrada) sustituyen a las del rol y sus instrucciones se añaden al sistema.
    public static IReadOnlyList<string> WithProfile(IReadOnlyList<string> args, AgentProfile p)
    {
        var list = args.ToList();
        int i = list.IndexOf("--allowedTools");
        if (i >= 0 && i + 1 < list.Count) list[i + 1] = AgentProfiles.ClaudeTools(p);
        if (p.Instructions.Length > 0) list.AddRange(["--append-system-prompt", p.Instructions]);
        return list;
    }

    /// Acepta la salida de `--output-format json` (un objeto) y la de `stream-json` (un evento por línea: el resultado es el evento «result»).
    public static AgentResult Parse(string output)
    {
        var text = output.Trim();
        // El evento «result» no siempre empieza por "type": se mira cada línea.
        if (text.Contains('\n'))
            foreach (var line in text.Split('\n').Reverse())
            {
                var candidate = line.Trim();
                if (!candidate.StartsWith('{') || !candidate.Contains("\"result\"", StringComparison.Ordinal)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(candidate);
                    if (CodexRunner.Str(doc.RootElement, "type") == "result") return ParseOne(candidate);
                }
                catch (JsonException) { }
            }
        return ParseOne(text);
    }

    private static AgentResult ParseOne(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            long input = 0, output = 0;
            if (r.TryGetProperty("usage", out var u))
            {
                // Entrada = nueva + escrita en caché (sin la leída de caché), igual que en Codex: los tokens que de verdad se gastan.
                input = CodexRunner.Num(u, "input_tokens") + CodexRunner.Num(u, "cache_creation_input_tokens");
                output = CodexRunner.Num(u, "output_tokens");
            }
            bool isError = r.TryGetProperty("is_error", out var ie) && ie.ValueKind == JsonValueKind.True;
            var text = CodexRunner.Str(r, "result") ?? "";
            double? cost = r.TryGetProperty("total_cost_usd", out var c) && c.TryGetDouble(out var d) ? d : null;
            return new(!isError, isError ? "" : text, input, output, CodexRunner.Str(r, "session_id"), cost, isError ? text : null);
        }
        catch (JsonException) { return new(false, "", Error: tr("Claude no devolvió JSON")); }
    }
}

/// Lanza un CLI, le pasa el prompt por stdin y lo mata (con sus hijos) al vencer el tiempo o cancelar.
public static class TeamProcess
{
    /// `codex` es un .cmd de npm y `claude` un .exe: se busca en el PATH sin shell.
    public static string? Find(string name)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var ext in new[] { ".exe", ".cmd" })
            {
                try { var f = Path.Combine(dir.Trim(), name + ext); if (File.Exists(f)) return f; } catch { }
            }
        return null;
    }

    public static async Task<(int Exit, string Out, string Err, bool TimedOut)> RunAsync(
        string exe, IEnumerable<string> args, string stdin, string workDir, TimeSpan timeout, CancellationToken ct = default, Action<string>? onLine = null)
    {
        var arguments = args.ToArray();
        var batch = OperatingSystem.IsWindows() && Path.GetExtension(exe) is var ext && (ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase));
        var psi = new ProcessStartInfo(batch ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe" : exe)
        {
            WorkingDirectory = workDir, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        if (batch)
        {
            var prefix = "ITS_TEAM_" + Guid.NewGuid().ToString("N");
            psi.Environment[prefix + "_EXE"] = exe;
            var command = $"\"%{prefix}_EXE%\"";
            for (int i = 0; i < arguments.Length; i++) command += " " + BatchToken(arguments[i], psi, prefix + "_ARG_" + i);
            psi.Arguments = $"/d /v:off /s /c \"{command}\"";
        }
        else foreach (var a in arguments) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"{exe} no arrancó");
        // La salida se lee línea a línea para poder avisar de lo que va haciendo el agente sin esperar a que termine.
        var stdout = Task.Run(async () =>
        {
            var all = new StringBuilder();
            while (await p.StandardOutput.ReadLineAsync() is { } line)
            {
                all.AppendLine(line);
                try { onLine?.Invoke(line); } catch (Exception) { /* un fallo al pintar el avance no debe romper la tarea */ }
            }
            return all.ToString();
        });
        var stderr = p.StandardError.ReadToEndAsync();
        try { await p.StandardInput.WriteAsync(stdin); } catch (IOException) { }
        try { p.StandardInput.Close(); } catch (IOException) { }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            Procs.KillTree(p.Id);
            await p.WaitForExitAsync(CancellationToken.None);
            if (ct.IsCancellationRequested) throw;
            return (-1, await stdout, await stderr, true);
        }
        return (p.ExitCode, await stdout, await stderr, false);
    }

    private static string BatchToken(string arg, ProcessStartInfo psi, string key)
    {
        if (arg.Length > 0 && !arg.Any(c => char.IsWhiteSpace(c) || "&|<>^()%!".Contains(c))) return arg;
        psi.Environment[key] = arg;
        return $"\"%{key}%\"";
    }

    /// ¿Tiene sesión iniciada? Null = no se pudo saber (no se bloquea nada por eso). `claude auth status` da un JSON con `loggedIn`;
    /// `codex login status` sale con 0 y «Logged in…» si hay sesión.
    public static bool? ParseLogin(string cli, int exitCode, string output)
    {
        if (cli == "claude")
        {
            try { using var doc = JsonDocument.Parse(output); return doc.RootElement.TryGetProperty("loggedIn", out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null; }
            catch (JsonException) { return null; }
        }
        return exitCode == 0 ? output.Contains("logged in", StringComparison.OrdinalIgnoreCase) ? true : null : false;
    }

    public static async Task<bool?> LoggedInAsync(string cli, CancellationToken ct = default)
    {
        if (Find(cli) is not { } exe) return null;
        try
        {
            var r = await RunAsync(exe, cli == "claude" ? ["auth", "status"] : ["login", "status"], "", Path.GetTempPath(), TimeSpan.FromSeconds(10), ct);
            return r.TimedOut ? null : ParseLogin(cli, r.Exit, r.Out + r.Err);
        }
        catch (Exception) { return null; }
    }

    /// Ejecuta un paso en <paramref name="dir"/> con Codex o Claude y devuelve su resultado.
    public static async Task<AgentResult> RunAgentAsync(TeamStep step, string prompt, string dir, CancellationToken ct = default, Action<string>? activity = null)
    {
        Action<string>? onLine = activity is null ? null : line => { if (TeamActivity.Summarize(step.Agent, line) is { } what) activity(what); };
        var timeout = TimeSpan.FromMinutes(step.TimeoutMinutes);
        // Los adjuntos que no son imágenes (documentos, código) se nombran en el prompt para que el agente los lea desde su ruta.
        if ((step.Images ?? []).Where(i => !TeamEffort.ImageExtensions.Contains(Path.GetExtension(i))).ToList() is { Count: > 0 } docs)
            prompt += "\n\nArchivos adjuntos (léelos con la herramienta Read):\n" + string.Join("\n", docs.Select(i => "- " + i));
        if (step.Profile is { } prof)
        {
            // Las instrucciones del agente propio van delante; las de un modelo local viajan como mensaje de sistema.
            if (prof.Engine == AgentProfile.Ollama) return await OllamaRunner.RunAsync(prof, prompt, timeout, ct);
            if (prof.Engine == AgentProfile.Codex) prompt = $"{prof.Instructions}\n\n{prompt}";
        }
        if (step.Agent == TeamAgent.Codex)
        {
            if (Find("codex") is not { } codex) return new(false, "", Error: tr("Codex no está instalado: npm install -g @openai/codex")) { Permanent = true };
            var last = Path.Combine(Path.GetTempPath(), $"its-codex-{Guid.NewGuid():N}.txt");
            try
            {
                var r = await RunAsync(codex, CodexRunner.Args(dir, step.CanWrite, last, step.Model, step.Effort, step.Images, skipGitCheck: step.Role == TeamRole.Answer), prompt, dir, timeout, ct, onLine);
                if (r.TimedOut) return new(false, "", Error: tr("Codex superó %d min", step.TimeoutMinutes)) { Permanent = true };
                var parsed = CodexRunner.Parse(r.Out.Split('\n').Select(l => l.Trim()), File.Exists(last) ? File.ReadAllText(last) : null);
                return parsed.Ok || r.Exit == 0 ? parsed : parsed with { Error = parsed.Error ?? r.Err.Trim() };
            }
            finally { try { File.Delete(last); } catch { } }
        }
        if (Find("claude") is not { } claude) return new(false, "", Error: tr("Claude Code no está instalado.")) { Permanent = true };
        var images = step.Images ?? [];
        if (images.Where(i => TeamEffort.ImageExtensions.Contains(Path.GetExtension(i))).ToList() is { Count: > 0 } pics) prompt += "\n\nImágenes de referencia (míralas con la herramienta Read):\n" + string.Join("\n", pics.Select(i => "- " + i));
        var cargs = ClaudeRunner.Args(step.Agent, step.CanWrite, step.Effort, images.Select(i => Path.GetDirectoryName(i)!).Distinct().ToList(), step.Role == TeamRole.Answer, stream: activity is not null, readProject: step.ReadsProject);
        if (step.Profile is { } cp) cargs = ClaudeRunner.WithProfile(cargs, cp);
        var c = await RunAsync(claude, cargs, prompt, dir, timeout, ct, onLine);
        return c.TimedOut ? new(false, "", Error: tr("Claude superó %d min", step.TimeoutMinutes)) { Permanent = true } : ClaudeRunner.Parse(c.Out);
    }
}
