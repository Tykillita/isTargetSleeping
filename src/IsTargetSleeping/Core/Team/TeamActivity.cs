using System.Text.Json;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping;

/// Convierte lo que va escribiendo el CLI de un agente (una línea JSON por evento) en una frase corta de lo que está haciendo ahora, para el registro en
/// vivo del equipo: «Leyendo App.xaml», «Buscando “WeatherWidget”», «Pensando…». Devuelve null si el evento no dice nada útil. Nunca enseña el razonamiento
/// ni el texto de los mensajes: solo qué herramienta usa y sobre qué.
public static class TeamActivity
{
    /// El nombre corto de lo que hace un paso, para el registro.
    public static string RoleName(TeamRole role) => role switch
    {
        TeamRole.Clarify => tr("Aclarar"), TeamRole.Plan => tr("Planificar"), TeamRole.Design => tr("Diseñar"),
        TeamRole.Implement => tr("Implementar"), TeamRole.Review => tr("Revisar"), _ => tr("Responder"),
    };

    public static string? Summarize(TeamAgent agent, string line)
    {
        line = line.Trim();
        if (!line.StartsWith('{')) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var type = CodexRunner.Str(root, "type");
            return agent == TeamAgent.Codex ? FromCodex(root, type) : FromClaude(root, type);
        }
        catch (JsonException) { return null; }
    }

    private static string? FromClaude(JsonElement root, string? type)
    {
        if (type != "assistant" || !root.TryGetProperty("message", out var message) || !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return null;
        string? last = null;
        foreach (var block in content.EnumerateArray())
            last = CodexRunner.Str(block, "type") switch
            {
                "tool_use" => Tool(CodexRunner.Str(block, "name"), block.TryGetProperty("input", out var input) ? input : default),
                "thinking" => tr("Pensando…"),
                "text" => tr("Redactando…"),
                _ => last,
            } ?? last;
        return last;
    }

    private static string? FromCodex(JsonElement root, string? type)
    {
        if (!root.TryGetProperty("item", out var item)) return type == "turn.started" ? tr("Empezando…") : null;
        var kind = CodexRunner.Str(item, "type");
        // Lo que empieza es lo que se está haciendo; lo que termina solo importa si no se vio empezar (razonar y escribir no tienen «inicio» útil).
        return (type, kind) switch
        {
            ("item.started", "command_execution") => Describe(CodexRunner.Str(item, "command") ?? ""),
            ("item.started" or "item.completed", "web_search") => tr("Buscando en la web"),
            ("item.started", "file_change") or ("item.completed", "file_change") => tr("Editando archivos"),
            ("item.completed", "reasoning") => tr("Pensando…"),
            ("item.completed", "agent_message") => tr("Redactando…"),
            _ => null,
        };
    }

    /// «"C:\Windows\...\powershell.exe" -Command "git status"» → «git status»: ni la ruta del intérprete ni sus opciones dicen qué se hace.
    /// Lo que hace un comando, en una frase («ls -la» → «Listando archivos»); los que no se reconocen se muestran recortados.
    public static string Describe(string command)
    {
        var c = FriendlyCommand(command);
        if (c.Length == 0) return tr("Ejecutando un comando");
        var first = c.Split(' ', 2)[0].ToLowerInvariant();
        bool Has(string word) => c.Contains(" " + word, StringComparison.OrdinalIgnoreCase);
        string? plain = first switch
        {
            "ls" or "dir" or "get-childitem" or "gci" or "tree" or "find" or "fd" => tr("Listando archivos"),
            "cat" or "type" or "get-content" or "gc" or "head" or "tail" or "sed" => tr("Leyendo archivos"),
            "rg" or "grep" or "select-string" or "findstr" or "ag" => tr("Buscando en el código"),
            "dotnet" or "npm" or "pnpm" or "yarn" when Has("test") => tr("Corriendo las pruebas"),
            "dotnet" when Has("build") => tr("Compilando"),
            "git" when Has("diff") || Has("status") => tr("Revisando los cambios"),
            "git" when Has("log") => tr("Mirando el historial"),
            _ => null,
        };
        return plain ?? tr("Ejecutando %@", Shorten(c, 46));
    }

    public static string FriendlyCommand(string command)
    {
        var text = command.Trim();
        // Un script entero («@'…'@», varias líneas) no cabe ni dice nada: se nombra y ya.
        if (text.StartsWith("@'") || text.StartsWith("@\"") || text.Contains('\n') && text.Length > 80) return "";
        // Las rutas de unidad («C:\Users\x\skills\SKILL.md») quedan en su último tramo.
        text = System.Text.RegularExpressions.Regex.Replace(text, @"[A-Za-z]:[\\/][^\s'""]*[\\/]([^\s'""\\/]+)", "$1");
        var m = System.Text.RegularExpressions.Regex.Match(text, @"^""?(?<exe>[^""]*?)""?(\s+(?<rest>.*))?$", System.Text.RegularExpressions.RegexOptions.Singleline);
        if (!m.Success) return text;
        var exe = Path.GetFileNameWithoutExtension(m.Groups["exe"].Value.Replace('/', '\\'));
        var rest = m.Groups["rest"].Value.Trim();
        if (exe.ToLowerInvariant() is "powershell" or "pwsh" or "cmd" or "bash" or "sh")
        {
            var inner = System.Text.RegularExpressions.Regex.Match(rest, @"(?:-Command|-[a-z]*c|/c)\s+(?<c>.*)$", System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return (inner.Success ? inner.Groups["c"].Value : rest).Trim().Trim('"', '\'');
        }
        return rest.Length > 0 ? $"{exe} {rest}" : exe;
    }

    private static string? Tool(string? name, JsonElement input)
    {
        string? Field(string key) => input.ValueKind == JsonValueKind.Object ? CodexRunner.Str(input, key) : null;
        string Name(string? path) => string.IsNullOrWhiteSpace(path) ? "" : Path.GetFileName(path.Replace('/', Path.DirectorySeparatorChar));
        // Un evento a medias (la herramienta sin su argumento todavía) no dice nada útil: se espera al siguiente.
        if (name is "Read" or "Edit" or "Write" or "MultiEdit" && Field("file_path") is null or "") return null;
        if (name is "Grep" or "Glob" && Field("pattern") is null or "") return null;
        if (name is "WebSearch" && Field("query") is null or "") return null;
        return name switch
        {
            "ToolSearch" => tr("Preparando herramientas"),
            "Read" => tr("Leyendo %@", Name(Field("file_path"))),
            // Un patrón de expresión regular o de globo con llaves no le dice nada a quien mira: se cuenta lo que se hace, no el patrón.
            "Grep" => System.Text.RegularExpressions.Regex.IsMatch(Field("pattern") ?? "", @"[|\()\[\]{}*+?]") ? tr("Buscando en el código") : tr("Buscando «%@»", Shorten(Field("pattern") ?? "", 34)),
            "Glob" => (Field("pattern") ?? "").Contains("**") || (Field("pattern") ?? "").Contains((char)123) ? tr("Buscando archivos del proyecto") : tr("Buscando archivos %@", Shorten(Field("pattern") ?? "", 34)),
            "Edit" or "Write" or "MultiEdit" or "NotebookEdit" => tr("Editando %@", Name(Field("file_path"))),
            "WebSearch" => tr("Buscando en la web «%@»", Shorten(Field("query") ?? "", 34)),
            "WebFetch" => tr("Leyendo una página web"),
            "Bash" => Describe(Field("command") ?? ""),
            null or "" => null,
            _ => tr("Usando %@", name),
        };
    }

    private static string Shorten(string text, int max)
    {
        text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= max ? text : text[..max] + "…";
    }
}
