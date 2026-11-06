using System.Text.Json;

namespace IsTargetSleeping.Pet;

/// Lee `agent-state.json`, que la app isTargetSleeping escribe cuando cambia lo que hacen tus agentes de IA
/// (el mismo estado que ilumina el notch). Si el archivo no se renueva (la app se cerro), el estado caduca solo.
public sealed class AgentBridge
{
    public static readonly string[] States = ["idle", "working", "attention", "alert", "done"];
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(90);  // la app renueva cada 30 s mientras haya actividad
    public static readonly TimeSpan DoneFor = TimeSpan.FromSeconds(30);

    private readonly string path;
    private readonly Func<DateTimeOffset> clock;
    private readonly object gate = new();
    private string current = "idle";

    public event Action<string>? Changed;

    public AgentBridge(string path, Func<DateTimeOffset>? clock = null)
    {
        this.path = path;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public string Current { get { lock (gate) return current; } }

    /// Lee el archivo y, si el estado cambio, avisa. Cualquier archivo ausente, roto o caducado cuenta como `idle`.
    public void Poll()
    {
        string next = Read();
        bool changed;
        lock (gate) { changed = next != current; current = next; }
        if (changed) Changed?.Invoke(next);
    }

    private string Read()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            string? state = doc.RootElement.GetProperty("state").GetString();
            long ts = doc.RootElement.GetProperty("ts").GetInt64();
            if (state is null || Array.IndexOf(States, state) < 0) return "idle";
            var age = clock() - DateTimeOffset.FromUnixTimeSeconds(ts);
            if (age > StaleAfter) return "idle";
            if (state == "done" && age > DoneFor) return "idle";
            return state;
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return "idle";
        }
    }
}
