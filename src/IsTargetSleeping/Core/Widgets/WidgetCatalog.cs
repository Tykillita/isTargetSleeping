using static IsTargetSleeping.L10n;

namespace IsTargetSleeping;

/// Un módulo que se puede poner en el Home del notch. `Key` es el texto en español (la clave de traducción).
public sealed record WidgetInfo(string Id, string Key, string Glyph, int Cells)
{
    public string Title => tr(Key);
}

/// Una acción de un toque para la tira de favoritos junto a la navegación.
public sealed record FavoriteInfo(string Id, string Key, string Glyph)
{
    public string Title => tr(Key);
}

public static class WidgetCatalog
{
    /// El Home cabe en cuatro celdas: un widget ocupa una o dos.
    public const int MaxCells = 4;

    public static readonly IReadOnlyList<WidgetInfo> All =
    [
        new("ollama", "Ollama", "", 2),
        new("system", "Sistema", "", 1),
        new("clock", "Reloj y calendario", "", 1),
        new("pomodoro", "Pomodoro", "", 1),
        new("awake", "Mantener despierto", "", 1),
        new("notes", "Notas", "", 2),
        new("battery", "Batería", "", 1),
        new("code", "Código", "", 2),
        new("media", "Música", "", 1),
    ];

    public static readonly IReadOnlyList<string> DefaultWidgets = ["ollama", "system", "clock"];
    /// Lo que enseña la versión corta (al pasar el ratón) si nunca se eligió: Ollama, como siempre.
    public static readonly IReadOnlyList<string> DefaultPeek = ["ollama"];

    public static WidgetInfo? Find(string id) => All.FirstOrDefault(w => w.Id == id);
}

public static class FavoriteCatalog
{
    public const int Max = 4;

    public static readonly IReadOnlyList<FavoriteInfo> All =
    [
        new("power", "Encender o apagar Ollama", ""),
        new("clean", "Liberar RAM", ""),
        new("free", "Liberar el modelo", ""),
        new("processes", "Procesos", ""),
        new("awake", "Mantener despierto", ""),
        new("pomodoro", "Pomodoro", ""),
    ];

    public static readonly IReadOnlyList<string> Defaults = ["power", "clean", "processes"];

    /// Un favorito puede ser una app o un archivo del usuario: su id es «app:» y la ruta.
    public const string AppPrefix = "app:";
    public static string AppId(string path) => AppPrefix + path;
    public static bool IsApp(string id) => id.StartsWith(AppPrefix, StringComparison.Ordinal) && id.Length > AppPrefix.Length;
    public static string AppPath(string id) => id[AppPrefix.Length..];

    public static FavoriteInfo? Find(string id) =>
        IsApp(id) ? new FavoriteInfo(id, Path.GetFileNameWithoutExtension(AppPath(id)), "") : All.FirstOrDefault(f => f.Id == id);

    /// Lo guardado (null = nunca se tocó) sin ids desconocidos ni repetidos y con tope.
    public static List<string> Normalize(IEnumerable<string>? stored) =>
        stored is null
            ? [.. Defaults]
            : stored.Where(id => Find(id) is not null).Distinct().Take(Max).ToList();

    public static List<string> Toggle(IReadOnlyList<string> ids, string id)
    {
        if (ids.Contains(id)) return ids.Where(i => i != id).ToList();
        return ids.Count >= Max ? [.. ids] : [.. ids, id];
    }
}

/// El orden y la elección de widgets del Home. Todo son funciones puras: la interfaz solo guarda el resultado.
public static class WidgetLayout
{
    public static int Cells(IEnumerable<string> ids) => ids.Sum(id => WidgetCatalog.Find(id)?.Cells ?? 0);

    /// Lo guardado (null = nunca se tocó) sin ids desconocidos ni repetidos; lo que no cabe en cuatro celdas se descarta.
    public static List<string> Normalize(IEnumerable<string>? stored, IEnumerable<string>? defaults = null)
    {
        var result = new List<string>();
        foreach (var id in stored ?? defaults ?? WidgetCatalog.DefaultWidgets)
        {
            if (WidgetCatalog.Find(id) is not { } info || result.Contains(id)) continue;
            if (Cells(result) + info.Cells <= WidgetCatalog.MaxCells) result.Add(id);
        }
        return result;
    }

    public static bool CanAdd(IReadOnlyList<string> ids, string id) =>
        WidgetCatalog.Find(id) is { } info && !ids.Contains(id) && Cells(ids) + info.Cells <= WidgetCatalog.MaxCells;

    public static List<string> Add(IReadOnlyList<string> ids, string id) => CanAdd(ids, id) ? [.. ids, id] : [.. ids];

    public static List<string> Remove(IReadOnlyList<string> ids, string id) => ids.Where(i => i != id).ToList();

    /// Cuántas celdas ocupa de verdad cada widget: una fila mide dos. Los de dos celdas llenan una fila; los de una se emparejan
    /// y el que se queda solo en su fila (sin compañero) se ensancha a las dos y cambia a su vista ancha.
    public static List<(string Id, int Span)> Arrange(IReadOnlyList<string> ids)
    {
        var rows = new List<List<(string Id, int Cells)>>();
        foreach (var id in ids)
        {
            var cells = WidgetCatalog.Find(id)?.Cells ?? 1;
            if (rows.Count == 0 || rows[^1].Sum(w => w.Cells) + cells > 2) rows.Add([]);
            rows[^1].Add((id, cells));
        }
        return rows.SelectMany(row => row.Select(w => (w.Id, row.Count == 1 ? 2 : w.Cells))).ToList();
    }

    /// Sube (-1) o baja (+1) un widget una posición; en el borde se queda donde está.
    public static List<string> Move(IReadOnlyList<string> ids, string id, int delta)
    {
        var list = ids.ToList();
        var from = list.IndexOf(id);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= list.Count) return list;
        (list[from], list[to]) = (list[to], list[from]);
        return list;
    }
}

/// Un dato que puede enseñar la pastilla del notch cuando está cerrada.
public sealed record PillInfo(string Id, string Key, string DetailKey, string Glyph)
{
    public string Title => tr(Key);
    public string Detail => tr(DetailKey);
}

public static class PillCatalog
{
    /// La pastilla mide 230 píxeles: caben tres datos.
    public const int Max = 3;

    public static readonly IReadOnlyList<PillInfo> All =
    [
        new("state", "Estado", "Encendido o apagado", ""),
        new("model", "Modelo", "El modelo cargado", ""),
        new("ram", "RAM", "La RAM en uso", ""),
        new("vram", "VRAM", "La VRAM de la GPU en uso", ""),
        new("cpu", "CPU", "El uso de la CPU", ""),
        new("clock", "Hora", "La hora", ""),
        new("battery", "Batería", "La carga de la batería", ""),
        new("media", "Música", "Lo que suena ahora, con su indicador", ""),
        new("pomodoro", "Pomodoro", "El tiempo del temporizador", ""),
    ];

    public static readonly IReadOnlyList<string> Defaults = ["state", "model", "ram"];

    public static PillInfo? Find(string id) => All.FirstOrDefault(p => p.Id == id);

    /// Lo guardado (null = nunca se tocó) sin ids desconocidos ni repetidos y con tope.
    public static List<string> Normalize(IEnumerable<string>? stored) =>
        stored is null ? [.. Defaults] : stored.Where(id => Find(id) is not null).Distinct().Take(Max).ToList();

    public static List<string> Toggle(IReadOnlyList<string> ids, string id)
    {
        if (ids.Contains(id)) return ids.Where(i => i != id).ToList();
        return ids.Count >= Max ? [.. ids] : [.. ids, id];
    }

    /// Los widgets que tienen que leer datos aunque el notch esté cerrado.
    public static IEnumerable<string> WidgetsNeeded(IEnumerable<string> ids) => ids.Select(id => id switch
    {
        "cpu" => "system",
        "clock" => "clock",
        "battery" => "battery",
        _ => "",
    }).Where(w => w.Length > 0);
}
