using System.Text.Json;

namespace IsTargetSleeping.Pet;

/// Avisos proactivos: lo que la mascota cuenta sin que se lo pregunten (agentes que terminan o piden atención, uso de IA alto).
/// Respeta el horario de silencio de la política (`quiet`, por ejemplo «22:00-07:00»).
public static class Notices
{
    public static string? ForAgentState(string state, string? previous) => (state, previous) switch
    {
        ("done", "working") => "Tus agentes terminaron lo que estaban haciendo.",
        ("attention", _) when previous != "attention" => "Un agente necesita tu atención.",
        ("alert", _) when previous != "alert" => "Un agente lanzó una alerta.",
        _ => null,
    };

    /// true si `local` cae dentro de «HH:mm-HH:mm» (puede cruzar la medianoche). Un valor vacío o mal escrito = nunca en silencio.
    public static bool InQuiet(string? quiet, TimeSpan local)
    {
        if (string.IsNullOrWhiteSpace(quiet)) return false;
        var parts = quiet.Split('-');
        if (parts.Length != 2 || !TimeSpan.TryParse(parts[0], out var a) || !TimeSpan.TryParse(parts[1], out var b)) return false;
        return a <= b ? local >= a && local < b : local >= a || local < b;
    }
}

/// Vigila `usage.json` y avisa una vez cuando un límite pasa del umbral (por defecto 80 %); vuelve a avisar solo tras reiniciarse el límite.
public sealed class UsageWatcher(string path, double threshold = 80)
{
    private readonly HashSet<string> notified = [];

    public IReadOnlyList<string> Check(DateTimeOffset now)
    {
        var msgs = new List<string>();
        try
        {
            if (!File.Exists(path)) return msgs;
            var root = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
            if (now - DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("ts").GetInt64()) > UsageBridge.StaleAfter) return msgs;
            foreach (var p in root.GetProperty("data").GetProperty("providers").EnumerateArray())
            {
                string name = p.GetProperty("name").GetString() ?? "IA";
                foreach (var l in p.GetProperty("limits").EnumerateArray())
                {
                    double used = l.GetProperty("used_pct").GetDouble();
                    long reset = l.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt64() : 0;
                    string key = name + "|" + l.GetProperty("label").GetString() + "|" + reset;
                    if (used >= threshold && notified.Add(key))
                    {
                        string label = l.GetProperty("label").GetString() is "5h" ? "de 5 horas" : "semanal";
                        msgs.Add($"{name} lleva el {used:0} % de su límite {label}.");
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { }
        return msgs;
    }
}
