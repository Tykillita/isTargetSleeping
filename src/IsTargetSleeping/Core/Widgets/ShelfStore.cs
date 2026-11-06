using System.IO;
using System.Text.Json.Nodes;

namespace IsTargetSleeping;

/// La lógica del Estante, sin interfaz: una lista de rutas (nunca copias) que se guarda en shelf.json.
public static class ShelfStore
{
    public const int Max = 100;

    /// Añade al final lo que exista (archivo o carpeta), sin repetir (sin mirar mayúsculas) y hasta `Max`.
    public static List<string> Add(IReadOnlyList<string> shelf, IEnumerable<string> paths)
    {
        var result = shelf.ToList();
        foreach (var raw in paths)
        {
            if (result.Count >= Max) break;
            string path;
            try { path = Path.GetFullPath(raw); } catch { continue; }
            if (!File.Exists(path) && !Directory.Exists(path)) continue;
            if (result.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase))) continue;
            result.Add(path);
        }
        return result;
    }

    public static List<string> Remove(IReadOnlyList<string> shelf, IEnumerable<string> paths)
    {
        var drop = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        return shelf.Where(p => !drop.Contains(p)).ToList();
    }

    /// Quita lo que ya no existe (el archivo se movió o se borró).
    public static List<string> Prune(IReadOnlyList<string> shelf) => shelf.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();

    public static string Serialize(IEnumerable<string> shelf) =>
        new JsonObject { ["files"] = new JsonArray([.. shelf.Select(p => (JsonNode)p)]) }.ToJsonString();

    /// Un archivo roto o vacío no da nada, sin fallar.
    public static List<string> Parse(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject { } root || root["files"] is not JsonArray items) return [];
            return items.Select(n => n?.GetValue<string>()).OfType<string>().Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(Max).ToList();
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException or FormatException) { return []; }
    }

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1_048_576 => $"{bytes / 1024.0:0.#} KB",
        < 1_073_741_824 => $"{bytes / 1_048_576.0:0.#} MB",
        _ => $"{bytes / 1_073_741_824.0:0.##} GB",
    };

    /// El nombre como lo corta Nusa: unos 14 caracteres y «…».
    public static string ShortName(string name, int max = 14) => name.Length <= max ? name : name[..max] + "…";
}

/// El estado del Estante sin interfaz: las rutas, el modo «Elegir» y lo marcado. Cada método dice si la lista cambió (y hay que guardar).
public sealed class ShelfList
{
    private readonly HashSet<string> marked = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Files { get; private set; } = [];
    public bool Selecting { get; private set; }
    public bool IsMarked(string path) => marked.Contains(path);

    public void Load(IEnumerable<string> paths) => Files = ShelfStore.Prune([.. paths]);

    public void Add(IEnumerable<string> paths) => Files = ShelfStore.Add(Files, paths);

    public void ToggleSelect() { Selecting = !Selecting; marked.Clear(); }

    /// Pulsar un mosaico: en «Elegir» lo marca o desmarca .
    public void Mark(string path) { if (!marked.Remove(path)) marked.Add(path); }

    public void Remove(string path) { Files = ShelfStore.Remove(Files, [path]); marked.Remove(path); Settle(); }

    public void RemoveMarked() { Files = ShelfStore.Remove(Files, marked); marked.Clear(); Settle(); }

    public void Clear() { Files = []; marked.Clear(); Settle(); }

    /// Quita lo que ya no existe; true si quitó algo.
    public bool Prune()
    {
        var alive = ShelfStore.Prune(Files);
        if (alive.Count == Files.Count) return false;
        Files = alive;
        marked.IntersectWith(alive);
        Settle();
        return true;
    }

    private void Settle() { if (Files.Count == 0) Selecting = false; }
}
