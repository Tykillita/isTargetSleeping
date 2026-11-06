using System.Text.Json;
using System.Text.Json.Nodes;

namespace IsTargetSleeping.Pet;

/// Que tan delicada es una herramienta. Fija el modo por defecto (ver PolicyStore) y que se le muestra al usuario.
public enum ToolRisk { Read, Reversible, Sensitive, Pc }

/// Lo que la politica decide para una herramienta: no ejecutar, pedir confirmacion o ejecutar sola.
public enum ToolMode { Off, Ask, Auto }

public sealed record ToolSpec(string Name, string Title, ToolRisk Risk, JsonObject Schema);

public sealed record ToolResult(bool Ok, string Message);

/// Una capacidad concreta y cerrada de la mascota. No hay herramienta de shell libre: cada una valida sus argumentos.
public interface ITool
{
    ToolSpec Spec { get; }
    /// null si los argumentos son validos; si no, el motivo (en espanol).
    string? Validate(JsonElement args);
    /// Lo que va a pasar, en una frase exacta para mostrar en la confirmacion.
    string Describe(JsonElement args);
    Task<ToolResult> RunAsync(JsonElement args, CancellationToken ct);
}

/// Registro cerrado de herramientas por nombre.
public sealed class ToolRegistry
{
    private readonly Dictionary<string, ITool> tools = new(StringComparer.Ordinal);

    public ToolRegistry Add(ITool tool)
    {
        if (!tools.TryAdd(tool.Spec.Name, tool)) throw new ArgumentException("herramienta duplicada: " + tool.Spec.Name);
        return this;
    }

    public ITool? Find(string name) => tools.GetValueOrDefault(name);
    public IReadOnlyCollection<ITool> All => tools.Values;
}

/// Llamada a la app isTargetSleeping (otro proceso). La app solo atiende su lista cerrada de herramientas.
public interface IAppBridge
{
    Task<(bool Ok, string Message)> CallAsync(string tool, JsonObject args, CancellationToken ct);
}

/// Canal por archivos: `commands\<id>.json` (la app lo consulta cada 2 s) y `results\<id>.json` con la respuesta.
public sealed class FileAppBridge(string dir, TimeSpan? timeout = null) : IAppBridge
{
    private readonly TimeSpan wait = timeout ?? TimeSpan.FromSeconds(8);

    public async Task<(bool Ok, string Message)> CallAsync(string tool, JsonObject args, CancellationToken ct)
    {
        string id = PetProtocol.NewId();
        string commands = Path.Combine(dir, "commands"), results = Path.Combine(dir, "results");
        Directory.CreateDirectory(commands);
        Directory.CreateDirectory(results);
        string tmp = Path.Combine(commands, id + ".tmp");
        File.WriteAllText(tmp, new JsonObject { ["id"] = id, ["tool"] = tool, ["args"] = args.DeepClone(), ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() }.ToJsonString());
        File.Move(tmp, Path.Combine(commands, id + ".json"));
        string result = Path.Combine(results, id + ".json");
        var until = DateTime.UtcNow + wait;
        while (DateTime.UtcNow < until)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var root = JsonDocument.Parse(File.ReadAllText(result)).RootElement;
                try { File.Delete(result); } catch (IOException) { }
                return (root.GetProperty("ok").GetBoolean(), root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "");
            }
            catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { }
            await Task.Delay(200, ct);
        }
        try { File.Delete(Path.Combine(commands, id + ".json")); } catch (IOException) { }
        return (false, "la app no respondió a tiempo");
    }
}

/// Enciende o apaga el widget de Uso de IA de la app. Reversible.
public sealed class UsageSetTool(IAppBridge bridge) : ITool
{
    public ToolSpec Spec { get; } = new("usage.set", "Widget de uso de IA", ToolRisk.Reversible, new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject { ["on"] = new JsonObject { ["type"] = "boolean" } },
        ["required"] = new JsonArray("on"),
    });

    public string? Validate(JsonElement args) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty("on", out var on) && on.ValueKind is JsonValueKind.True or JsonValueKind.False ? null : "falta «on» (true o false)";

    public string Describe(JsonElement args) => args.GetProperty("on").GetBoolean() ? "Encender el widget de uso de IA" : "Apagar el widget de uso de IA";

    public async Task<ToolResult> RunAsync(JsonElement args, CancellationToken ct)
    {
        var (ok, message) = await bridge.CallAsync(Spec.Name, new JsonObject { ["on"] = args.GetProperty("on").GetBoolean() }, ct);
        return new ToolResult(ok, message);
    }
}
