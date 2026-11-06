using System.Text.Json;

namespace IsTargetSleeping;

/// Avisa al complemento «Mascota conectada» (programa aparte) de lo que hacen tus agentes de IA, para que la mascota de la
/// placa reaccione. Escribe `<datos>\pet\agent-state.json` con el mismo estado que ilumina el notch. Si el complemento no
/// está instalado (no existe la carpeta `pet`) no escribe nada. Mientras haya actividad renueva el archivo cada 30 s.
internal sealed record PetActivityInfo(string? Tool, string? File, string Summary, bool Done);

internal sealed record PetSessionInfo(string Id, string Provider, string Project, string State, string? Tool, string? File, string? Message, long Last)
{
    public string? Title { get; init; }
    public string? Branch { get; init; }
    public long ContextTokens { get; init; }
    public int Added { get; init; }
    public int Removed { get; init; }
    public int Repeat { get; init; }
    public string? RepeatSubject { get; init; }
    public string? Completed { get; init; }
    public bool Stalled { get; init; }
    public bool CanResume { get; init; }
    public IReadOnlyList<PetActivityInfo> Activity { get; init; } = [];
}

internal sealed record PetAskInfo(string Question, string Header, bool Multi, IReadOnlyList<string> Options);

internal sealed record PetApprovalInfo(string Id, string Agent, string Kind, string Tool, string? Cwd, string? Detail, string? Risk, long Expires, IReadOnlyList<PetAskInfo>? Questions, string? Plan)
{
    /// Líneas «-», «+» o « » de lo que el agente quiere cambiar (solo Edit, MultiEdit y Write, y nunca en archivos sensibles).
    public string? Diff { get; init; }
}

internal static class PetBridge
{
    private static AgentGlowState? last;
    private static DateTime lastWrite;

    public static void Publish(AgentGlowState state) => Publish(state, DateTime.UtcNow);

    internal static void Publish(AgentGlowState state, DateTime now)
    {
        if (state == last && (state == AgentGlowState.Off || (now - lastWrite).TotalSeconds < 30)) return;
        string dir = Path.Combine(Paths.DataDir, "pet");
        if (!Directory.Exists(dir)) return;
        last = state;
        lastWrite = now;
        try
        {
            string name = state switch
            {
                AgentGlowState.Working => "working",
                AgentGlowState.Attention => "attention",
                AgentGlowState.Alert => "alert",
                AgentGlowState.Done => "done",
                _ => "idle",
            };
            string tmp = Path.Combine(dir, "agent-state.json.tmp");
            File.WriteAllText(tmp, JsonSerializer.Serialize(new { state = name, ts = new DateTimeOffset(now).ToUnixTimeSeconds() }));
            File.Move(tmp, Path.Combine(dir, "agent-state.json"), overwrite: true);
        }
        catch (IOException) { /* el complemento lo leerá en el siguiente aviso */ }
        catch (UnauthorizedAccessException) { }
    }

    private static string? lastUsage;

    /// Resumen del uso de IA (el que ya muestra el widget de Uso, solo si el usuario lo activó): porcentajes y horas de reinicio,
    /// nunca sesiones ni tokens. Lo lee el complemento para que la mascota pueda contestar «¿cuánto uso me queda?».
    public static void PublishUsage(IEnumerable<(string Id, string Name, string Plan, IReadOnlyList<(string Label, double Percent, DateTimeOffset? ResetsAt)> Limits, string? Note, string? Problem)> providers)
    {
        string dir = Path.Combine(Paths.DataDir, "pet");
        if (!Directory.Exists(dir)) return;
        var body = JsonSerializer.Serialize(new
        {
            providers = providers.Select(p => new
            {
                id = p.Id, name = p.Name, plan = p.Plan, note = p.Note, problem = p.Problem,
                limits = p.Limits.Select(l => new { label = l.Label, used_pct = Math.Round(l.Percent, 1), resets_at = l.ResetsAt?.ToUnixTimeSeconds() }),
            }),
        });
        if (body == lastUsage && (DateTime.UtcNow - lastUsageWrite).TotalMinutes < 10) return;
        try
        {
            string tmp = Path.Combine(dir, "usage.json.tmp");
            File.WriteAllText(tmp, JsonSerializer.Serialize(new { ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), data = JsonDocument.Parse(body).RootElement }));
            File.Move(tmp, Path.Combine(dir, "usage.json"), overwrite: true);
            lastUsage = body;
            lastUsageWrite = DateTime.UtcNow;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static DateTime lastUsageWrite;
    private static string? lastApprovals;
    private static string? lastTeam;
    private static DateTime lastTeamWrite;

    /// Estado de Team (trabajo en curso, plan pendiente, cola e historial): lo que la mascota enseña en el celular. Se renueva cada 20 s.
    public static void PublishTeam(object state)
    {
        string dir = Path.Combine(Paths.DataDir, "pet");
        if (!Directory.Exists(dir)) return;
        var camel = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var body = JsonSerializer.Serialize(state, camel);
        if (body == lastTeam && (DateTime.UtcNow - lastTeamWrite).TotalSeconds < 20) return;
        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(body)!.AsObject();
            node["ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string tmp = Path.Combine(dir, "team-state.json.tmp");
            File.WriteAllText(tmp, node.ToJsonString());
            File.Move(tmp, Path.Combine(dir, "team-state.json"), overwrite: true);
            lastTeam = body;
            lastTeamWrite = DateTime.UtcNow;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string? lastSessions;
    private static DateTime lastSessionsWrite;

    /// Los agentes de IA que trabajan ahora (Claude Code, Codex…): proyecto, estado, herramienta y archivo actuales. Es lo que ya enseña el
    /// widget de Agentes; la mascota lo muestra en el celular. Se renueva cada 20 s aunque no cambie (así sabe si la app dejó de correr).
    public static void PublishSessions(IReadOnlyList<PetSessionInfo> items)
    {
        string dir = Path.Combine(Paths.DataDir, "pet");
        if (!Directory.Exists(dir)) return;
        var camel = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var body = JsonSerializer.Serialize(items, camel);
        if (body == lastSessions && (DateTime.UtcNow - lastSessionsWrite).TotalSeconds < 20) return;
        try
        {
            string tmp = Path.Combine(dir, "sessions.json.tmp");
            File.WriteAllText(tmp, JsonSerializer.Serialize(new { ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), items }, camel));
            File.Move(tmp, Path.Combine(dir, "sessions.json"), overwrite: true);
            lastSessions = body;
            lastSessionsWrite = DateTime.UtcNow;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// Lo que la mascota puede mencionar de la app (solo lectura): pomodoro, tareas de hoy y clima. Va a `app-state.json`.
    public static void PublishAppState(object state)
    {
        string dir = Path.Combine(Paths.DataDir, "pet");
        if (!Directory.Exists(dir)) return;
        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(state, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }))!.AsObject();
            node["ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string tmp = Path.Combine(dir, "app-state.json.tmp");
            File.WriteAllText(tmp, node.ToJsonString());
            File.Move(tmp, Path.Combine(dir, "app-state.json"), overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// Peticiones de permiso/pregunta/plan de agentes que esperan respuesta, para que la mascota las muestre en el celular y la placa.
    /// Solo lleva lo que ya ve el notch (comando o ruta corta, carpeta, riesgo); nunca el contenido de archivos.
    public static void PublishApprovals(IReadOnlyList<PetApprovalInfo> items, bool force = false)
    {
        string dir = Path.Combine(Paths.DataDir, "pet");
        if (!Directory.Exists(dir)) return;
        var camel = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var body = JsonSerializer.Serialize(items, camel);
        if (!force && body == lastApprovals) return;
        if (lastApprovals is null && items.Count == 0 && !File.Exists(Path.Combine(dir, "approvals.json"))) { lastApprovals = body; return; }
        try
        {
            string tmp = Path.Combine(dir, "approvals.json.tmp");
            File.WriteAllText(tmp, JsonSerializer.Serialize(new { ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), items }, camel));
            File.Move(tmp, Path.Combine(dir, "approvals.json"), overwrite: true);
            lastApprovals = body;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// El usuario apagó el widget de Uso: el complemento deja de ver cifras (no se conservan datos viejos).
    public static void ClearUsage()
    {
        if (lastUsage is null && !File.Exists(Path.Combine(Paths.DataDir, "pet", "usage.json"))) return;
        lastUsage = null;
        try { File.Delete(Path.Combine(Paths.DataDir, "pet", "usage.json")); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    internal static void ResetForTest() { last = null; lastWrite = default; lastUsage = null; lastUsageWrite = default; lastApprovals = null; }
}
