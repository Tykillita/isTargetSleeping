namespace IsTargetSleeping;

public sealed record ClipboardEntry(string Text, DateTimeOffset CopiedAt, bool IsPinned, int OriginalLength)
{
    public bool WasTruncated => OriginalLength > Text.Length;
}

/// El historial es texto en memoria y tiene un tope. Nada se guarda en disco ni sale del PC.
public sealed class ClipboardHistory
{
    public const int Max = 25, MaxLength = 20000, MaxPinned = 10;
    private readonly List<ClipboardEntry> entries = [];
    public IReadOnlyList<ClipboardEntry> Entries => entries;
    public IReadOnlyList<string> Items => entries.Select(e => e.Text).ToArray();
    public int Count => entries.Count;
    public int PinnedCount => entries.Count(e => e.IsPinned);
    public int Revision { get; private set; }

    /// Añade lo más reciente primero. Si está lleno, solo expulsa una entrada no fijada.
    public bool Add(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        int originalLength = text.Length;
        if (originalLength > MaxLength) text = text[..MaxLength];
        int existing = entries.FindIndex(e => e.Text == text);
        if (existing == 0) return false;
        ClipboardEntry? previous = existing >= 0 ? entries[existing] : null;
        if (existing >= 0) entries.RemoveAt(existing);
        else if (entries.Count == Max)
        {
            int evict = entries.FindLastIndex(e => !e.IsPinned);
            if (evict < 0) return false;
            entries.RemoveAt(evict);
        }
        entries.Insert(0, new ClipboardEntry(text, DateTimeOffset.Now, previous?.IsPinned ?? false, originalLength));
        Revision++;
        return true;
    }

    public bool SetPinned(string text, bool pinned)
    {
        int index = entries.FindIndex(e => e.Text == text);
        if (index < 0 || entries[index].IsPinned == pinned || pinned && PinnedCount >= MaxPinned) return false;
        entries[index] = entries[index] with { IsPinned = pinned };
        Revision++;
        return true;
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= entries.Count) return;
        entries.RemoveAt(index);
        Revision++;
    }

    public void Clear()
    {
        if (entries.Count == 0) return;
        entries.Clear();
        Revision++;
    }

    /// Una línea para la lista: la primera con contenido, sin sangrías y cortada con «…».
    public static string Preview(string text, int length = 90)
    {
        var line = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        return line.Length <= length ? line : line[..(length - 1)] + "…";
    }

    /// Los gestores de contraseñas marcan lo que copian para que no se registre: se respeta esa marca.
    public static bool ShouldRecord(bool excludeFlag, int? canInclude) => !excludeFlag && canInclude != 0;
}
