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
        new("audio", "Audio por app", "", 1),
        new("agents", "Agentes de IA", "", 1),
        new("weather", "Tiempo", "", 1),
        new("teleprompter", "Teleprompter", "", 1),
    ];

    public static readonly IReadOnlyList<string> DefaultWidgets = ["ollama", "system", "clock"];
    /// Lo que enseña la versión corta (al pasar el ratón) si nunca se eligió: Ollama, como siempre.
    public static readonly IReadOnlyList<string> DefaultPeek = ["ollama"];

    public static WidgetInfo? Find(string id) => All.FirstOrDefault(w => w.Id == id);
}

/// Los botones del dock: Inicio, Widgets y Ajustes siempre están; estas páginas son opcionales (apagadas por defecto).
public static class DockCatalog
{
    public static readonly IReadOnlyList<FavoriteInfo> Optional =
    [
        new("ollama", "Ollama", ""),
        new("activity", "Actividad", ""),
    ];

    public static FavoriteInfo? Find(string id) => Optional.FirstOrDefault(f => f.Id == id);

    /// Lo guardado (null = nunca se tocó: ninguna) sin ids desconocidos ni repetidos.
    public static List<string> Normalize(IEnumerable<string>? stored) =>
        stored is null ? [] : stored.Where(id => Find(id) is not null).Distinct().ToList();

    public static List<string> Toggle(IReadOnlyList<string> ids, string id) =>
        ids.Contains(id) ? ids.Where(i => i != id).ToList() : [.. ids, id];
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
    public static string BaseId(string id) => id.Split('@', 2)[0];
    public static int Span(string id)
    {
        var baseId = BaseId(id);
        var info = WidgetCatalog.Find(baseId);
        if (info is null) return 0;
        var canResize = info.Cells == 1 && baseId is "system" or "clock" or "pomodoro" or "awake" or "battery" or "media" or "audio" or "agents" or "weather" or "teleprompter";
        var suffix = id.IndexOf('@');
        return canResize && suffix >= 0 && int.TryParse(id[(suffix + 1)..], out int span) && span is 1 or 2 ? span : info.Cells;
    }
    public static int Cells(IEnumerable<string> ids) => ids.Sum(Span);

    /// Lo guardado (null = nunca se tocó) sin ids desconocidos ni repetidos; lo que no cabe en cuatro celdas se descarta.
    public static List<string> Normalize(IEnumerable<string>? stored, IEnumerable<string>? defaults = null)
    {
        var result = new List<string>();
        foreach (var id in stored ?? defaults ?? WidgetCatalog.DefaultWidgets)
        {
            var baseId = BaseId(id);
            if (WidgetCatalog.Find(baseId) is not { } info || result.Any(x => BaseId(x) == baseId)) continue;
            var span = Span(id);
            var canResize = span != info.Cells;
            var normalized = canResize ? $"{baseId}@{span}" : baseId;
            if (Cells(result) + span <= WidgetCatalog.MaxCells) result.Add(normalized);
        }
        return result;
    }

    public static bool CanAdd(IReadOnlyList<string> ids, string id) =>
        WidgetCatalog.Find(BaseId(id)) is { } info && ids.All(x => BaseId(x) != BaseId(id)) && Cells(ids) + info.Cells <= WidgetCatalog.MaxCells;

    public static List<string> Add(IReadOnlyList<string> ids, string id) => CanAdd(ids, id) ? [.. ids, id] : [.. ids];

    public static List<string> Remove(IReadOnlyList<string> ids, string id) => ids.Where(i => BaseId(i) != BaseId(id)).ToList();

    public static bool CanResize(string id) => Span(id) > 0 && WidgetCatalog.Find(BaseId(id))?.Cells == 1
        && BaseId(id) is "system" or "clock" or "pomodoro" or "awake" or "battery" or "media" or "audio" or "agents" or "weather" or "teleprompter";

    public static List<string> ToggleWidth(IReadOnlyList<string> ids, string id)
    {
        var index = ids.ToList().FindIndex(x => BaseId(x) == BaseId(id));
        if (index < 0 || !CanResize(id)) return [.. ids];
        var result = ids.ToList();
        int target = Span(result[index]) == 1 ? 2 : 1;
        var token = target == WidgetCatalog.Find(BaseId(id))!.Cells ? BaseId(id) : $"{BaseId(id)}@{target}";
        if (Cells(result) - Span(result[index]) + target > WidgetCatalog.MaxCells) return result;
        result[index] = token;
        return result;
    }

    public static bool DragResizeTriggered(bool wide, double delta) => wide ? delta <= -36 : delta >= 36;

    /// Cuántas celdas ocupa de verdad cada widget: una fila mide dos. Los de dos celdas llenan una fila; los de una se emparejan
    /// y el que se queda solo en su fila (sin compañero) se ensancha a las dos y cambia a su vista ancha.
    public static List<(string Id, int Span)> Arrange(IReadOnlyList<string> ids)
    {
        var rows = new List<List<(string Id, int Cells)>>();
        foreach (var id in ids)
        {
            var baseId = BaseId(id);
            var cells = Span(id);
            if (cells == 0) continue;
            if (rows.Count == 0 || rows[^1].Sum(w => w.Cells) + cells > 2) rows.Add([]);
            rows[^1].Add((baseId, cells));
        }
        return rows.SelectMany(row => row.Select(w => (w.Id, row.Count == 1 ? 2 : w.Cells))).ToList();
    }

    /// Sube (-1) o baja (+1) un widget una posición; en el borde se queda donde está.
    public static List<string> Move(IReadOnlyList<string> ids, string id, int delta)
    {
        var list = ids.ToList();
        var from = list.FindIndex(x => BaseId(x) == BaseId(id));
        var to = from + delta;
        if (from < 0 || to < 0 || to >= list.Count) return list;
        (list[from], list[to]) = (list[to], list[from]);
        return list;
    }

    public static List<string> MoveBefore(IReadOnlyList<string> ids, string sourceId, string targetId)
    {
        var list = ids.ToList();
        int from = list.FindIndex(x => BaseId(x) == BaseId(sourceId));
        int to = list.FindIndex(x => BaseId(x) == BaseId(targetId));
        if (to < 0 || from == to) return list;
        if (from < 0)
        {
            if (CanAdd(list, sourceId)) list.Insert(to, sourceId);
            return list;
        }
        var item = list[from]; list.RemoveAt(from);
        if (from < to) to--;
        list.Insert(to, item);
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
