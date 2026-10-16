namespace IsTargetSleeping;

/// El historial del portapapeles: solo texto, en memoria y con tope. Nada se guarda en disco ni sale del PC.
public sealed class ClipboardHistory
{
    public const int Max = 25, MaxLength = 20000;
    private readonly List<string> items = [];

    /// De la copia más reciente a la más antigua.
    public IReadOnlyList<string> Items => items;
    public int Count => items.Count;

    /// Añade un texto: vacío o solo espacios no entra, y si ya estaba sube al primer puesto en vez de repetirse.
    /// Devuelve true si el historial cambió.
    public bool Add(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Length > MaxLength) text = text[..MaxLength];
        if (items.Count > 0 && items[0] == text) return false;
        items.Remove(text);
        items.Insert(0, text);
        if (items.Count > Max) items.RemoveRange(Max, items.Count - Max);
        return true;
    }

    public void RemoveAt(int index)
    {
        if (index >= 0 && index < items.Count) items.RemoveAt(index);
    }

    public void Clear() => items.Clear();

    /// Una línea para la lista: la primera con contenido, sin sangrías y cortada con «…».
    public static string Preview(string text, int length = 90)
    {
        var line = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        return line.Length <= length ? line : line[..(length - 1)] + "…";
    }

    /// Los gestores de contraseñas marcan lo que copian para que no se registre: se respeta esa marca.
    /// `excludeFlag` es ExcludeClipboardContentFromMonitorProcessing presente; `canInclude` es el valor de CanIncludeInClipboardHistory (null = no hay).
    public static bool ShouldRecord(bool excludeFlag, int? canInclude) => !excludeFlag && canInclude != 0;
}
