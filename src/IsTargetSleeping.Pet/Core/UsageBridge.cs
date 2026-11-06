using System.Text;
using System.Text.Json;

namespace IsTargetSleeping.Pet;

/// Lee `usage.json` (lo escribe la app solo si el usuario activo el widget de Uso) y lo resume en una frase para el modelo.
/// Devuelve null si no hay archivo, esta roto o es demasiado viejo: la mascota entonces dice que no lo sabe en vez de inventarlo.
public static class UsageBridge
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(45);  // la app lo renueva cada 5-10 min mientras corre

    public static string? Summary(string path, DateTimeOffset now)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var root = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
            var ts = DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("ts").GetInt64());
            if (now - ts > StaleAfter || ts - now > TimeSpan.FromMinutes(5)) return null;
            var sb = new StringBuilder();
            foreach (var p in root.GetProperty("data").GetProperty("providers").EnumerateArray())
            {
                string name = Clean(p.GetProperty("name").GetString()), plan = Clean(p.GetProperty("plan").GetString());
                var parts = new List<string>();
                foreach (var l in p.GetProperty("limits").EnumerateArray())
                {
                    double used = Math.Clamp(l.GetProperty("used_pct").GetDouble(), 0, 100);
                    string label = Clean(l.GetProperty("label").GetString());
                    label = label switch { "5h" or "5 h" => "límite de 5 horas", "week" or "Semanal" => "límite semanal", _ => label };
                    string s = $"{label}: usado {used:0.#} %, queda {100 - used:0.#} %";
                    if (l.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.Number)
                        s += ", " + Reset(DateTimeOffset.FromUnixTimeSeconds(r.GetInt64()), now);
                    parts.Add(s);
                }
                if (parts.Count == 0)
                {
                    // Un proveedor sin cifras con motivo (sesion caducada, sin conexion): la mascota puede explicarlo en vez de callar.
                    if (p.TryGetProperty("problem", out var pr) && pr.ValueKind == JsonValueKind.String && Clean(pr.GetString()) is { Length: > 0 } why)
                    {
                        if (sb.Length > 0) sb.Append("; ");
                        sb.Append(name).Append(" → sin datos: ").Append(why);
                    }
                    continue;
                }
                if (sb.Length > 0) sb.Append("; ");
                sb.Append(name).Append(plan.Length > 0 ? $" (plan {plan})" : "").Append(" → ").Append(string.Join(", ", parts));
                if (sb.Length > 700) break;
            }
            return sb.Length == 0 ? null : sb.ToString();
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { return null; }
    }

    private static string Clean(string? s)
    {
        string t = new((s ?? "").Where(c => !char.IsControl(c)).ToArray());
        return t.Length > 40 ? t[..40] : t;  // es texto de otra app: se acota antes de ir al prompt
    }

    private static string Reset(DateTimeOffset at, DateTimeOffset now)
    {
        var d = at - now;
        if (d <= TimeSpan.Zero) return "ya se reinició";
        return d.TotalHours < 1 ? $"reinicia en {Math.Ceiling(d.TotalMinutes)} min"
             : d.TotalHours < 48 ? $"reinicia en {Math.Round(d.TotalHours)} h"
             : $"reinicia en {Math.Round(d.TotalDays)} días";
    }
}
