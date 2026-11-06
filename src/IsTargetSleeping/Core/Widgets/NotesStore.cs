using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping;

/// Una nota: su texto entero (la primera línea es el título, como en las Notas de iOS) y cuándo se tocó por última vez.
public sealed record NoteData(string Id, string Text, DateTime Updated);
public enum NotesLoadIssue { None, CorruptFileBackedUp, CorruptFilePreserved, ReadFailed }
public sealed record NotesLoadResult(List<NoteData> Notes, string? Current, bool SaveAllowed, NotesLoadIssue Issue, string? BackupPath = null);

/// La lógica pura del widget Notas: título y vista previa de una nota, la fecha corta, el archivo (JSON) y la migración del bloc único antiguo.
public static class NotesStore
{
    public const int MaxNotes = 50;

    /// El título: la primera línea con algo escrito, recortada.
    public static string Title(string text)
    {
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            if (line.Trim() is { Length: > 0 } t) return t.Length > 80 ? t[..80] + "…" : t;
        return "";
    }

    /// Lo que sigue al título, en una sola línea: el trozo que enseña la lista bajo el título.
    public static string Preview(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Skip(1);
        var joined = string.Join(" ", lines);
        return joined.Length > 160 ? joined[..160] + "…" : joined;
    }

    /// «14:05» si es de hoy, «Ayer» si es de ayer, el día («lun») dentro de la semana y «3 oct» si es más viejo.
    public static string DateLabel(DateTime updated, DateTime now)
    {
        var culture = L10n.Effective == "es" ? new CultureInfo("es") : new CultureInfo("en");
        if (updated.Date == now.Date) return updated.ToString("HH:mm", culture);
        if (updated.Date == now.Date.AddDays(-1)) return tr("Ayer");
        if (updated.Date > now.Date.AddDays(-7)) return updated.ToString("ddd", culture);
        return updated.ToString("d MMM", culture);
    }

    public static string Serialize(IEnumerable<NoteData> notes, string? current) => new JsonObject
    {
        ["current"] = current,
        ["notes"] = new JsonArray([.. notes.Select(n => (JsonNode)new JsonObject { ["id"] = n.Id, ["text"] = n.Text, ["updated"] = n.Updated.ToString("o", CultureInfo.InvariantCulture) })]),
    }.ToJsonString();

    /// Lee las notas sin convertir un archivo corrupto en una lista vacía que luego se pueda guardar encima.
    public static NotesLoadResult Load(string file, string oldFile, DateTime now)
    {
        try
        {
            if (File.Exists(file))
            {
                var json = File.ReadAllText(file);
                if (TryParse(json, out var notes, out var current)) return new(notes, current, true, NotesLoadIssue.None);
                var backup = file + ".corrupt-" + now.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture);
                try { File.Copy(file, backup, overwrite: false); return new([], null, true, NotesLoadIssue.CorruptFileBackedUp, backup); }
                catch { return new([], null, false, NotesLoadIssue.CorruptFilePreserved); }
            }
            return new(File.Exists(oldFile) ? Migrate(File.ReadAllText(oldFile), File.GetLastWriteTime(oldFile)) : [], null, true, NotesLoadIssue.None);
        }
        catch { return new([], null, false, NotesLoadIssue.ReadFailed); }
    }

    /// Lo guardado. Una forma inválida no se interpreta como un archivo vacío.
    public static (List<NoteData> Notes, string? Current) Parse(string json) =>
        TryParse(json, out var notes, out var current) ? (notes, current) : ([], null);

    public static bool TryParse(string json, out List<NoteData> notes, out string? current)
    {
        notes = [];
        current = null;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["notes"] is not JsonArray items) return false;
            current = root["current"] is JsonValue value && value.TryGetValue<string>(out var selected) ? selected : null;
            foreach (var item in items.OfType<JsonObject>())
            {
                if (item["id"] is not JsonValue idValue || !idValue.TryGetValue<string>(out var id) || string.IsNullOrWhiteSpace(id) || notes.Any(x => x.Id == id)) continue;
                var text = item["text"] is JsonValue textValue && textValue.TryGetValue<string>(out var body) ? body : "";
                var updated = item["updated"] is JsonValue updatedValue && updatedValue.TryGetValue<string>(out var date)
                    && DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : DateTime.Now;
                notes.Add(new(id, text, updated));
                if (notes.Count >= MaxNotes) break;
            }
            return true;
        }
        catch (JsonException) { return false; }
    }

    /// El temporal comparte volumen con el destino y se vacía antes del reemplazo, para que un cierre no trunque las notas anteriores.
    public static void SaveAtomic(string file, string json)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(file))!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, Path.GetFileName(file) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                var bytes = new UTF8Encoding(false).GetBytes(json);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, file, overwrite: true);
        }
        finally { try { File.Delete(temp); } catch { } }
    }

    /// El bloc único de antes (notes.txt) pasa a ser la primera nota; vacío no crea ninguna.
    public static List<NoteData> Migrate(string? oldText, DateTime now) =>
        string.IsNullOrWhiteSpace(oldText) ? [] : [new(Guid.NewGuid().ToString("N"), oldText, now)];
}
