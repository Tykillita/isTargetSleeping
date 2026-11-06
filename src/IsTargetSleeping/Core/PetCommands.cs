using System.Text.Json;
using System.Text.RegularExpressions;

namespace IsTargetSleeping;

/// Ordenes que la mascota (complemento «Mascota conectada») le da a la app: `pet\commands\<id>.json` → `pet\results\<id>.json`.
/// La app solo atiende las herramientas que enumera el manejador (lista cerrada); se rechaza lo repetido, lo viejo y lo mal formado,
/// y siempre se escribe un resultado para que quien pidió no se quede esperando.
internal static class PetCommands
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);
    public const int MaxPerPoll = 8, MaxSeen = 64;
    private static readonly Regex IdPattern = new("^[0-9a-f]{32}$", RegexOptions.Compiled);
    private static readonly Queue<string> seen = new();

    /// `handler(tool, args)` corre en el hilo que llama (el de la interfaz) y devuelve (hecho, mensaje corto).
    public static void Poll(Func<string, JsonElement, (bool Ok, string Message)> handler, DateTimeOffset? now = null)
    {
        string dir = Path.Combine(Paths.DataDir, "pet"), commands = Path.Combine(dir, "commands"), results = Path.Combine(dir, "results");
        if (!Directory.Exists(commands)) return;
        var when = now ?? DateTimeOffset.UtcNow;
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(commands, "*.json").OrderBy(File.GetCreationTimeUtc).Take(MaxPerPoll).ToList(); }
        catch (IOException) { return; }
        foreach (var file in files)
        {
            string? id = null, tool = null;
            JsonElement args = default;
            bool fresh = false;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                id = root.GetProperty("id").GetString();
                tool = root.GetProperty("tool").GetString();
                args = root.TryGetProperty("args", out var a) ? a.Clone() : JsonDocument.Parse("{}").RootElement;
                var age = when - DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("ts").GetInt64());
                fresh = age >= TimeSpan.FromSeconds(-5) && age <= MaxAge;
            }
            catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException or UnauthorizedAccessException) { }
            try { File.Delete(file); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            if (id is null || tool is null || !IdPattern.IsMatch(id) || seen.Contains(id)) continue;
            seen.Enqueue(id);
            while (seen.Count > MaxSeen) seen.Dequeue();
            (bool Ok, string Message) result;
            if (!fresh) result = (false, "orden vieja");
            else
            {
                try { result = handler(tool, args); }
                catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException) { result = (false, "falló: " + e.Message); }
            }
            try
            {
                Directory.CreateDirectory(results);
                string tmp = Path.Combine(results, id + ".tmp");
                File.WriteAllText(tmp, JsonSerializer.Serialize(new { id, ok = result.Ok, message = result.Message }));
                File.Move(tmp, Path.Combine(results, id + ".json"), overwrite: true);
            }
            catch (IOException) { }
        }
        try
        {
            if (Directory.Exists(results))
                foreach (var old in Directory.EnumerateFiles(results).Where(f => when - File.GetLastWriteTimeUtc(f) > TimeSpan.FromMinutes(5))) File.Delete(old);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    internal static void ResetForTest() => seen.Clear();
}
