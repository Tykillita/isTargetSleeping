using System.Text.Json;
using System.Xml.Linq;

namespace IsTargetSleeping;

public enum AgentState { Unknown, Working, NeedsAttention, Finished, Inactive }
public sealed record AgentActivity(string? Tool, string? File, string? Message, bool Completed = false, string? CallId = null)
{
    public string Summary => Tool is not null
        ? $"{(Completed ? AgentSnapshot.DescribeCompletedTool(Tool) : AgentSnapshot.DescribeTool(Tool))} · {Tool}{(File is null ? "" : $" · {Path.GetFileName(File)}")}" : Message ?? "";
}

public sealed record AgentSnapshot(string Session, string Project, string Provider, AgentState State, string? Tool, string? File, int? Added, int? Removed,
    string? Message, IReadOnlyList<AgentActivity> RecentActivity, DateTime LastActivity)
{
    public string ProjectName => Path.GetFileName(Project.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    public string? Title { get; init; }
    public string? AutomationId { get; init; }
    public bool IsAutomation => !string.IsNullOrWhiteSpace(AutomationId);
    public string Name => !string.IsNullOrWhiteSpace(Title) ? Title : $"{(string.IsNullOrWhiteSpace(ProjectName) ? Provider : ProjectName)} · {SessionId}";
    public string SessionId
    {
        get
        {
            var id = Session;
            if (Session.StartsWith("rollout-", StringComparison.Ordinal))
            {
                var parts = Session.Split('-');
                if (parts.Length >= 5 && parts[^5].Length == 8 && parts[^4].Length == 4 && parts[^3].Length == 4 && parts[^2].Length == 4 && parts[^1].Length == 12)
                    id = string.Concat(parts[^5], parts[^4], parts[^3], parts[^2], parts[^1]);
                else id = parts[^1];
            }
            return id[..Math.Min(6, id.Length)];
        }
    }
    public string ToolAction => DescribeTool(Tool);
    public IReadOnlyList<string> CompletedWork { get; init; } = [];
    public string WorkSummary => string.Join(" · ", CompletedWork);
    public static string DescribeTool(string? tool) => tool switch
    {
        "exec" or "exec_command" => L10n.tr("Ejecutando comando"),
        "Read" => L10n.tr("Leyendo archivo"),
        "Edit" or "Write" => L10n.tr("Editando archivo"),
        "Glob" => L10n.tr("Buscando archivos"),
        "Grep" => L10n.tr("Buscando texto"),
        "apply_patch" => L10n.tr("Aplicando cambios"),
        "write_stdin" => L10n.tr("Esperando comando"),
        "Task" => L10n.tr("Delegando tarea"),
        "WebSearch" => L10n.tr("Buscando en web"),
        "WebFetch" => L10n.tr("Consultando web"),
        null or "" => "",
        _ => L10n.tr("Usando herramienta %@", tool),
    };
    public static string DescribeCompletedTool(string tool) => tool switch
    {
        "exec" or "exec_command" => L10n.tr("Ejecutó comando"),
        "Read" => L10n.tr("Leyó archivo"),
        "Edit" or "Write" or "apply_patch" => L10n.tr("Editó archivo"),
        "Glob" or "Grep" => L10n.tr("Buscó archivos"),
        "Task" => L10n.tr("Delegó tarea"),
        "WebSearch" or "WebFetch" => L10n.tr("Consultó la web"),
        _ => L10n.tr("Usó %@", tool),
    };
    internal static string DescribeCompletedCategory(string tool) => tool switch
    {
        "exec" or "exec_command" => L10n.tr("Ejecutó comandos"),
        "Read" => L10n.tr("Leyó archivos"),
        "Edit" or "Write" or "apply_patch" => L10n.tr("Editó archivos"),
        "Glob" or "Grep" => L10n.tr("Buscó archivos"),
        "Task" => L10n.tr("Delegó tareas"),
        "WebSearch" or "WebFetch" => L10n.tr("Consultó la web"),
        _ => L10n.tr("Usó herramientas"),
    };
    public string StateFilterLabel => State switch
    {
        AgentState.Working => L10n.tr("Trabajando"), AgentState.NeedsAttention => L10n.tr("Necesita tu atención"),
        AgentState.Finished => L10n.tr("Terminó"), AgentState.Inactive => L10n.tr("Sin actividad"), _ => L10n.tr("Estado desconocido"),
    };
    public string StateLabel => StateFilterLabel;
    public string InactivityLabel => InactivityLabelAt(DateTime.UtcNow);
    public string InactivityLabelAt(DateTime now) => (State is AgentState.Inactive or AgentState.NeedsAttention) && !AgentLogs.IsActive(this, now)
        ? L10n.tr("Sin actividad hace %@", AgentLogs.AgeLabel(LastActivity, now)) : "";
    public string Changes => Added is null && Removed is null ? "" : $"+{Added ?? 0} / −{Removed ?? 0}";
}

/// <summary>Lee actividad y mensajes del agente; nunca devuelve prompts del usuario ni bloques de pensamiento.</summary>
public static class AgentLogs
{
    public static readonly TimeSpan InactiveAfter = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan RecentWindow = TimeSpan.FromHours(12);

    public static bool IsActive(AgentSnapshot session, DateTime now)
    {
        var age = now.ToUniversalTime() - session.LastActivity.ToUniversalTime();
        return (session.State is AgentState.Working or AgentState.NeedsAttention) && age >= TimeSpan.Zero && age <= InactiveAfter;
    }

    public static string AgeLabel(DateTime lastActivity, DateTime now)
    {
        var age = now.ToUniversalTime() - lastActivity.ToUniversalTime();
        var minutes = Math.Max(1, (int)Math.Max(0, age.TotalMinutes));
        return age.TotalHours < 1 ? L10n.tr("%d min", minutes)
            : L10n.tr("%d h %d min", (int)age.TotalHours, minutes % 60);
    }

    public static List<AgentSnapshot> RecentSessions(IEnumerable<AgentSnapshot> previous, IEnumerable<AgentSnapshot> latest, DateTime now)
    {
        var current = now.ToUniversalTime();
        var snapshots = latest.ToArray();
        var active = snapshots.Where(s => IsActive(s, current))
            .Select(Key).ToHashSet(StringComparer.Ordinal);
        return previous.Concat(snapshots)
            .Where(s => s.State is AgentState.Working or AgentState.NeedsAttention or AgentState.Finished or AgentState.Inactive
                && current - s.LastActivity.ToUniversalTime() is { } age && age >= TimeSpan.Zero && age <= RecentWindow
                && !active.Contains(Key(s)) && (s.State == AgentState.Finished || age >= InactiveAfter))
            .GroupBy(Key, StringComparer.Ordinal).Select(g => g.MaxBy(s => s.LastActivity)!)
            .Select(s => s.State == AgentState.Working ? s with { State = AgentState.Inactive } : s)
            .OrderByDescending(s => s.LastActivity).ToList();
    }

    public static string Key(AgentSnapshot session) => $"{session.Provider}\0{session.Session}";

    public static AgentSnapshot Parse(string session, string project, string provider, IEnumerable<string> lines, DateTime lastActivity)
    {
        var state = AgentState.Unknown;
        string? tool = null, toolCallId = null, file = null, messageText = null, title = null, automationId = null;
        int? added = null, removed = null;
        var activity = new List<AgentActivity>();
        void AddMessage(string? text)
        {
            if (TryHeartbeat(text, out var id, out var decision, out var body))
            {
                automationId = id ?? automationId;
                messageText = Snippet(body) ?? messageText;
                activity.Add(new(null, null, DescribeAutomationDecision(decision)));
            }
            else
            {
                messageText = Snippet(text) ?? messageText;
                if (messageText is not null) activity.Add(new(null, null, messageText));
            }
        }
        foreach (var line in lines)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var type = Str(root, "type") ?? Str(root, "event");
                var payload = root.TryGetProperty("payload", out var p) ? p : default;
                project = Str(root, "cwd") ?? Str(root, "working_directory") ?? Str(payload, "cwd") ?? project;
                if (type is "assistant")
                {
                    var message = root.TryGetProperty("message", out var m) ? m : root;
                    if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                        foreach (var part in content.EnumerateArray())
                            if (Str(part, "type") == "text")
                                AddMessage(Str(part, "text"));
                            else if (Str(part, "type") == "tool_use")
                            {
                                state = AgentState.Working;
                                tool = Str(part, "name");
                                toolCallId = Str(part, "id");
                                var input = part.TryGetProperty("input", out var i) ? i : default;
                                file = Str(input, "file_path") ?? Str(input, "path");
                                if (tool is not null) activity.Add(new(tool, file, null, CallId: toolCallId));
                            }
                    if (Str(message, "stop_reason") == "end_turn") { state = AgentState.Finished; tool = null; }
                }
                else if (type is "result") { state = AgentState.Finished; tool = null; }
                else if (type is "custom-title") title = Str(root, "customTitle");
                else if (type is "user" && root.TryGetProperty("message", out var userMessage)
                    && userMessage.TryGetProperty("content", out var userContent) && userContent.ValueKind == JsonValueKind.Array)
                    foreach (var part in userContent.EnumerateArray().Where(part => Str(part, "type") == "tool_result"))
                    {
                        var callId = Str(part, "tool_use_id");
                        var index = activity.FindLastIndex(a => callId is not null && a.CallId == callId);
                        if (index >= 0) activity[index] = activity[index] with { Completed = true };
                        if (callId is not null && callId == toolCallId) { tool = null; toolCallId = null; }
                    }
                else if (type is "response_item")
                {
                    var kind = Str(payload, "type");
                    if (kind is "session_meta") project = Str(payload, "cwd") ?? project;
                    if (kind is "custom_tool_call")
                    {
                        state = AgentState.Working;
                        var callId = Str(payload, "call_id");
                        var currentTool = Str(payload, "name");
                        var currentFile = ExplicitToolPath(payload, currentTool);
                        var completed = Str(payload, "status") == "completed";
                        if (currentTool is not null)
                        {
                            var index = activity.FindLastIndex(a => callId is not null && a.CallId == callId);
                            if (index >= 0) activity[index] = activity[index] with { Completed = completed, File = currentFile ?? activity[index].File };
                            else activity.Add(new(currentTool, currentFile, null, completed, callId));
                        }
                        if (currentFile is not null) file = currentFile;
                        if (completed)
                        {
                            if (toolCallId == callId) { tool = null; toolCallId = null; }
                        }
                        else { tool = currentTool; toolCallId = callId; }
                    }
                    else if (kind is "custom_tool_call_output")
                    {
                        var callId = Str(payload, "call_id");
                        var index = activity.FindLastIndex(a => callId is not null && a.CallId == callId);
                        if (index >= 0) activity[index] = activity[index] with { Completed = true };
                        if (callId is not null && callId == toolCallId) { tool = null; toolCallId = null; }
                    }
                    else if (kind is "message" && Str(payload, "role") is "assistant")
                    {
                        if (payload.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                            foreach (var part in content.EnumerateArray())
                                if (Str(part, "type") is "output_text" or "text")
                                    AddMessage(Str(part, "text"));
                        var phase = Str(payload, "phase");
                        if (phase is "final") { state = AgentState.Finished; tool = null; }
                        else if (phase is "commentary") { state = AgentState.Working; tool = null; }
                    }
                }
                else if (type is "event_msg")
                {
                    var kind = Str(payload, "type");
                    if (kind is "task_started" or "turn_started") state = AgentState.Working;
                    else if (kind is "task_complete" or "turn_complete") { state = AgentState.Finished; tool = null; }
                    else if (kind is "approval_request" or "user_input_required") state = AgentState.NeedsAttention;
                    if (kind is "tool_call")
                    {
                        state = AgentState.Working; tool = Str(payload, "name"); toolCallId = Str(payload, "call_id");
                        var toolFile = ExplicitToolPath(payload, tool);
                        file = toolFile ?? file;
                        if (tool is not null) activity.Add(new(tool, toolFile, null, CallId: toolCallId));
                    }
                    if (kind is "item_completed" && payload.TryGetProperty("item", out var item))
                    {
                        if (Str(item, "type") is "AgentMessage")
                        {
                            var phase = Str(item, "phase");
                            if (phase is "final") { state = AgentState.Finished; tool = null; toolCallId = null; }
                            else if (phase is "commentary") { state = AgentState.Working; tool = null; toolCallId = null; }
                        }
                        else if (Str(item, "type") is "CommandExecution")
                        {
                            var index = activity.FindLastIndex(a => !a.Completed && (a.Tool is "exec" or "exec_command"));
                            if (index >= 0) activity[index] = activity[index] with { Completed = true };
                            if (tool is "exec" or "exec_command") { tool = null; toolCallId = null; }
                        }
                    }
                    if (kind is "patch_apply_end")
                    {
                        added = Int(payload, "additions"); removed = Int(payload, "deletions");
                        file = Str(payload, "path") ?? file;
                        activity.Add(new("apply_patch", file, null, true));
                        if (tool == "apply_patch") { tool = null; toolCallId = null; }
                    }
                }
            }
            catch (JsonException) { }
        }
        var completedActivity = activity.Where(a => a.Completed && a.Tool is not null).ToArray();
        int Calls(IEnumerable<AgentActivity> calls)
        {
            var items = calls.ToArray();
            return items.Count(a => string.IsNullOrEmpty(a.CallId))
                + items.Where(a => !string.IsNullOrEmpty(a.CallId)).Select(a => (a.Tool, a.CallId)).Distinct().Count();
        }
        static string CountLabel(int count, string one, string many) => count == 1 ? L10n.tr(one) : L10n.tr(many, count);
        var completedWork = new List<string>();
        var commands = Calls(completedActivity.Where(a => a.Tool is "exec" or "exec_command"));
        if (commands > 0) completedWork.Add(CountLabel(commands, "1 comando ejecutado", "%d comandos ejecutados"));
        var editedFiles = completedActivity.Where(a => a.Tool is "Edit" or "Write" or "apply_patch")
            .Select(a => a.File).Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (editedFiles > 0) completedWork.Add(CountLabel(editedFiles, "1 archivo editado", "%d archivos editados"));
        var reads = Calls(completedActivity.Where(a => a.Tool == "Read"));
        if (reads > 0) completedWork.Add(CountLabel(reads, "1 lectura completada", "%d lecturas completadas"));
        var searches = Calls(completedActivity.Where(a => a.Tool is "Glob" or "Grep"));
        if (searches > 0) completedWork.Add(CountLabel(searches, "1 búsqueda realizada", "%d búsquedas realizadas"));
        var tasks = Calls(completedActivity.Where(a => a.Tool == "Task"));
        if (tasks > 0) completedWork.Add(CountLabel(tasks, "1 tarea delegada", "%d tareas delegadas"));
        var webLookups = Calls(completedActivity.Where(a => a.Tool is "WebSearch" or "WebFetch"));
        if (webLookups > 0) completedWork.Add(CountLabel(webLookups, "1 consulta web", "%d consultas web"));
        var otherTools = Calls(completedActivity.Where(a => a.Tool is not ("exec" or "exec_command" or "Edit" or "Write" or "apply_patch" or "Read" or "Glob" or "Grep" or "Task" or "WebSearch" or "WebFetch")));
        if (otherTools > 0) completedWork.Add(CountLabel(otherTools, "1 herramienta usada", "%d herramientas usadas"));
        var recentActivity = activity.TakeLast(32).Reverse().DistinctBy(a => (a.Tool, a.File, a.Message, a.Completed)).Take(6).ToArray();
        return new AgentSnapshot(session, project, provider, state, tool, file, added, removed, messageText, recentActivity, lastActivity)
        { Title = title ?? (automationId is null ? null : AutomationTitle(automationId)),
            AutomationId = automationId, CompletedWork = completedWork };
    }

    private static string AutomationTitle(string id)
    {
        var title = string.Join(' ', id.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries));
        return title.Length == 0 ? id : char.ToUpperInvariant(title[0]) + title[1..];
    }

    private static bool TryHeartbeat(string? text, out string? automationId, out string? decision, out string? message)
    {
        automationId = decision = message = null;
        if (text is null || !text.StartsWith("<heartbeat", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var root = XDocument.Parse(text).Root;
            if (root?.Name.LocalName != "heartbeat") return false;
            automationId = root.Element("automation_id")?.Value;
            decision = root.Element("decision")?.Value;
            message = root.Element("message")?.Value;
            return true;
        }
        catch (System.Xml.XmlException) { return false; }
    }

    private static string DescribeAutomationDecision(string? decision) => decision switch
    {
        "DONT_NOTIFY" => L10n.tr("Comprobación automática · sin aviso"),
        "NOTIFY" => L10n.tr("Comprobación automática · aviso requerido"),
        _ => L10n.tr("Ejecución automatizada"),
    };

    private static string? Snippet(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var clean = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length > 280 ? clean[..277] + "…" : clean;
    }

    private static string? Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int? Int(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : null;
    private static string? ExplicitToolPath(JsonElement payload, string? tool)
    {
        if (payload.ValueKind != JsonValueKind.Object || tool is not ("Edit" or "Write" or "apply_patch" or "Read")) return null;
        var path = Str(payload, "file_path") ?? Str(payload, "path");
        if (path is not null) return path;
        foreach (var field in new[] { "input", "arguments", "parameters" })
        {
            if (!payload.TryGetProperty(field, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Object) path = Str(value, "file_path") ?? Str(value, "path");
            else if (value.ValueKind == JsonValueKind.String && value.GetString() is { } json)
            {
                try
                {
                    using var args = JsonDocument.Parse(json);
                    path = Str(args.RootElement, "file_path") ?? Str(args.RootElement, "path");
                }
                catch (JsonException) { }
            }
            if (!string.IsNullOrWhiteSpace(path)) return path;
        }
        return null;
    }
}
