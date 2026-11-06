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

/// Una carpeta de la página Widgets: agrupa widgets parecidos. Cada widget está en una sola.
public sealed record WidgetFolder(string Id, string Key, IReadOnlyList<string> Widgets)
{
    public string Title => tr(Key);
}

public static class WidgetCatalog
{
    /// La primera pantalla del Home (y la vista al pasar el ratón) cabe en cuatro celdas: un widget ocupa una o dos.
    public const int MaxCells = 4;
    /// El Home muestra cuatro celdas; cada widget restante tiene su propia página. El tope total sigue siendo doce celdas.
    public const int MaxHomeCells = 12;

    public static readonly IReadOnlyList<WidgetInfo> All =
    [
        new("ollama", "Ollama", "", 2),
        new("system", "Sistema", "", 1),
        new("clock", "Reloj y calendario", "", 1),
        new("pomodoro", "Pomodoro", "", 1),
        new("awake", "Mantener despierto", "", 1),
        new("notes", "Notas", "", 2),
        new("todo", "Tareas", "", 2),
        new("shelf", "Estante", "", 2),
        new("battery", "Batería", "", 1),
        new("code", "Código", "", 2),
        new("media", "Música", "", 1),
        new("audio", "Audio por app", "", 1),
        new("agents", "Agentes de IA", "", 1),
        new("usage", "Uso de IA", "", 1),
        new("lmstudio", "LM Studio", "", 2),
        new("launcher", "Modelos locales", "", 1),
        new("weather", "Clima", "", 1),
        new("teleprompter", "Teleprompter", "", 1),
        new("team", "Equipo Claude + Codex", "", 1),
        new("led", "Luces LED", "\uEA80", 1),
    ];

    public static readonly IReadOnlyList<string> DefaultWidgets = ["ollama", "system", "clock"];
    /// Lo que enseña la versión corta (al pasar el ratón) si nunca se eligió: Ollama, como siempre.
    public static readonly IReadOnlyList<string> DefaultPeek = ["ollama"];

    public static readonly IReadOnlyList<WidgetFolder> Folders =
    [
        new("ai", "IA", ["ollama", "lmstudio", "launcher", "agents", "usage", "team"]),
        new("system", "Sistema", ["system", "battery", "audio", "awake", "led"]),
        new("time", "Hora y clima", ["clock", "pomodoro", "weather"]),
        new("create", "Creación", ["notes", "todo", "shelf", "teleprompter", "code", "media"]),
    ];

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

    public static readonly IReadOnlyList<WidgetFolder> Folders = [new("pages", "Páginas", ["ollama", "activity"])];

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
        new("teamcancel", "Cancelar la tarea del equipo", ""),
    ];

    public static readonly IReadOnlyList<string> Defaults = ["power", "clean", "processes"];

    /// Las apps y archivos del usuario van aparte, en una carpeta «Mis apps» que solo existe si hay alguna.
    public static readonly IReadOnlyList<WidgetFolder> Folders =
    [
        new("ollama", "Ollama", ["power", "free"]),
        new("system", "Sistema", ["clean", "processes", "awake"]),
        new("daily", "Día a día", ["pomodoro", "teamcancel"]),
    ];

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
        var canResize = (info.Cells == 1 && baseId is ("system" or "clock" or "pomodoro" or "awake" or "battery" or "media" or "audio" or "agents" or "usage" or "launcher" or "weather" or "teleprompter" or "team"))
            || baseId is ("notes" or "todo" or "shelf" or "ollama" or "code" or "lmstudio");
        var suffix = id.IndexOf('@');
        return canResize && suffix >= 0 && int.TryParse(Digits(id[(suffix + 1)..], 0), out int span) && span is 1 or 2 ? span : info.Cells;
    }

    /// El trozo `index` de «2x2» (columnas x filas) del final de un id, o vacío.
    private static string Digits(string size, int index) => size.Split('x') is var parts && index < parts.Length ? parts[index] : "";

    /// Cuántas filas de la cuadrícula ocupa por defecto: los que enseñan un calendario, una carátula, una lista o un panel necesitan dos; el resto, una.
    public static int DefaultRows(string baseId) => baseId is "clock" or "media" or "agents" or "usage" or "launcher" or "team" or "code" or "teleprompter" or "led" ? 2 : 1;

    /// Las filas de un widget del Home (la cuadrícula): lo que diga su id («clock@1x2», «team@2x3») o, si no dice, lo de siempre. En la vista corta todo es de una fila.
    public static int Rows(string id, bool home = true)
    {
        if (!home) return 1;
        var suffix = id.IndexOf('@');
        return suffix >= 0 && int.TryParse(Digits(id[(suffix + 1)..], 1), out int rows) && rows is >= 1 and <= 3 ? rows : DefaultRows(BaseId(id));
    }

    /// Lo que pesa en el Home un widget: columnas × filas (una «celda» es una columna de una fila).
    public static int Area(IEnumerable<string> ids) => ids.Sum(id => Span(id) * Rows(id));

    /// El id con su tamaño, sin repetir lo que ya es el de por defecto.
    public static string Token(string baseId, int cols, int rows, bool home)
    {
        var info = WidgetCatalog.Find(baseId);
        bool colsDiffer = info is not null && cols != info.Cells, rowsDiffer = home && rows != DefaultRows(baseId);
        return !colsDiffer && !rowsDiffer ? baseId : $"{baseId}@{cols}" + (rowsDiffer ? $"x{rows}" : "");
    }

    /// Los tamaños que admite un widget, de uno en uno: 1×1, 2×1, 2×2 y 1×2.
    public static IReadOnlyList<(int Cols, int Rows)> Sizes(string baseId) =>
        baseId is "notes" or "todo" or "shelf" ? [(1, 1), (2, 1), (2, 2), (1, 2)]   // las notas ocupan dos celdas por defecto, pero también caben en una
        : baseId is "ollama" or "code" or "lmstudio" ? [(1, 1), (2, 1), (2, 2)]
        : WidgetCatalog.Find(baseId) is { Cells: 2 } ? [(2, 1), (2, 2)]
        : baseId == "awake" ? [(1, 1), (2, 1)]   // un interruptor no gana nada con más alto: no todos los widgets tienen todos los tamaños
        : [(1, 1), (2, 1), (2, 2), (1, 2)];
    public static int Cells(IEnumerable<string> ids) => ids.Sum(Span);

    /// Agranda los widgets de una página del Home hasta llenar sus `cells` celdas con los tamaños que admiten: uno solo ocupa 2×2 en vez de dejar huecos.
    /// Solo cambia lo que se ve; lo guardado sigue con el tamaño que eligió el usuario.
    public static List<string> FillPage(IReadOnlyList<string> page, int cells)
    {
        var list = page.ToList();
        for (bool grew = true; grew;)
        {
            grew = false;
            for (int i = 0; i < list.Count; i++)
            {
                var baseId = BaseId(list[i]);
                int current = Span(list[i]) * Rows(list[i]);
                var next = Sizes(baseId).Where(s => s.Cols * s.Rows > current && Area(list) - current + s.Cols * s.Rows <= cells)
                    .OrderBy(s => s.Cols * s.Rows).ThenByDescending(s => s.Cols).Select(s => ((int Cols, int Rows)?)s).FirstOrDefault();
                if (next is null) continue;
                list[i] = Token(baseId, next.Value.Cols, next.Value.Rows, true);
                grew = true;
            }
        }
        return list;
    }

    /// Lo guardado (null = nunca se tocó) sin ids desconocidos ni repetidos; lo que no cabe en cuatro celdas se descarta.
    public static List<string> Normalize(IEnumerable<string>? stored, IEnumerable<string>? defaults = null, int max = WidgetCatalog.MaxCells, bool home = false)
    {
        var result = new List<string>();
        foreach (var id in stored ?? defaults ?? WidgetCatalog.DefaultWidgets)
        {
            var baseId = BaseId(id);
            if (WidgetCatalog.Find(baseId) is not { } info || result.Any(x => BaseId(x) == baseId)) continue;
            var span = Span(id);
            int rows = Rows(id, home);
            if (home && !Sizes(baseId).Contains((span, rows))) (span, rows) = (info.Cells, DefaultRows(baseId));   // un tamaño que ese widget no tiene vuelve al suyo
            var normalized = Token(baseId, span, rows, home);
            if ((home ? Area(result) : Cells(result)) + span * rows <= max) result.Add(normalized);
        }
        return result;
    }

    public static bool CanAdd(IReadOnlyList<string> ids, string id, int max = WidgetCatalog.MaxCells, bool home = false) =>
        WidgetCatalog.Find(BaseId(id)) is { } info && ids.All(x => BaseId(x) != BaseId(id))
        && (home ? Area(ids) + Span(id) * Rows(id) : Cells(ids) + Span(id)) <= max;

    public static List<string> Add(IReadOnlyList<string> ids, string id, int max = WidgetCatalog.MaxCells, bool home = false) => CanAdd(ids, id, max, home) ? [.. ids, id] : [.. ids];

    /// Cómo añadirlo: tal cual si cabe y, si no, en una celda cuando ese widget la admite y queda una libre (null: no cabe de ninguna forma).
    public static string? Fit(IReadOnlyList<string> ids, string id, int max = WidgetCatalog.MaxCells, bool home = false)
    {
        if (CanAdd(ids, id, max, home)) return id;
        var one = Token(BaseId(id), 1, 1, home);
        return Sizes(BaseId(id)).Contains((1, 1)) && CanAdd(ids, one, max, home) ? one : null;
    }

    public static List<string> Remove(IReadOnlyList<string> ids, string id) => ids.Where(i => BaseId(i) != BaseId(id)).ToList();

    public static bool CanResize(string id, bool home = false) => Span(id) > 0 && (home || BaseId(id) is ("notes" or "todo" or "shelf" or "ollama" or "code" or "lmstudio")
        || WidgetCatalog.Find(BaseId(id))?.Cells == 1
        && BaseId(id) is ("system" or "clock" or "pomodoro" or "awake" or "battery" or "media" or "audio" or "agents" or "usage" or "launcher" or "weather" or "teleprompter" or "team"));

    public static List<string> ToggleWidth(IReadOnlyList<string> ids, string id, int max = WidgetCatalog.MaxCells, bool home = false)
    {
        var index = ids.ToList().FindIndex(x => BaseId(x) == BaseId(id));
        if (index < 0 || !CanResize(id, home)) return [.. ids];
        if (home)
        {
            // En el Home (la cuadrícula) cada pulsación pasa al siguiente tamaño que cabe: 1×1 → 2×1 → 2×2 → 1×2 → 1×1.
            var sizes = Sizes(BaseId(id)); var current = (Span(ids[index]), Rows(ids[index]));
            int at = Math.Max(0, ((List<(int Cols, int Rows)>)[.. sizes]).FindIndex(s => s == current));
            var list = ids.ToList();
            for (int step = 1; step < sizes.Count; step++)
            {
                var next = sizes[(at + step) % sizes.Count];
                if (Area(ids) - current.Item1 * current.Item2 + next.Cols * next.Rows > max) continue;
                list[index] = Token(BaseId(id), next.Cols, next.Rows, true);
                return list;
            }
            return list;
        }
        var result = ids.ToList();
        int target = Span(result[index]) == 1 ? 2 : 1;
        var token = target == WidgetCatalog.Find(BaseId(id))!.Cells ? BaseId(id) : $"{BaseId(id)}@{target}";
        if (Cells(result) - Span(result[index]) + target > max) return result;
        result[index] = token;
        return result;
    }

    public static List<string> SetWidth(IReadOnlyList<string> ids, string id, int width, int max = WidgetCatalog.MaxCells, bool home = false)
    {
        var list = ids.ToList();
        int index = list.FindIndex(x => BaseId(x) == BaseId(id));
        if (index < 0 || !CanResize(id, home) || Span(list[index]) == width) return list;
        var current = (Span(list[index]), Rows(list[index]));
        int currentArea = home ? current.Item1 * current.Item2 : current.Item1;
        foreach (var next in Sizes(BaseId(id)).Where(s => s.Cols == width)
                     .OrderBy(s => s.Rows == current.Item2 ? 0 : 1).ThenBy(s => s.Rows))
        {
            int nextArea = home ? next.Cols * next.Rows : next.Cols;
            if ((home ? Area(ids) : Cells(ids)) - currentArea + nextArea > max) continue;
            list[index] = Token(BaseId(id), next.Cols, next.Rows, home);
            return list;
        }
        return list;
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

    /// El Home como cuadrícula: cada widget con las columnas y filas que dice su id; si solo hay uno, ocupa las dos columnas.
    public static List<(string Id, int Cols, int Rows)> ArrangeHome(IReadOnlyList<string> ids) =>
        ids.Where(id => Span(id) > 0).Select(id => (BaseId(id), ids.Count == 1 ? 2 : Span(id), Rows(id))).ToList();

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

    public static List<string> MoveBefore(IReadOnlyList<string> ids, string sourceId, string targetId, int max = WidgetCatalog.MaxCells, bool home = false)
    {
        var list = ids.ToList();
        int from = list.FindIndex(x => BaseId(x) == BaseId(sourceId));
        int to = list.FindIndex(x => BaseId(x) == BaseId(targetId));
        if (to < 0 || from == to) return list;
        if (from < 0)
        {
            if (CanAdd(list, sourceId, max, home)) list.Insert(to, sourceId);
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
        new("team", "Equipo", "El avance de la tarea del equipo de agentes", ""),
        new("usage", "Uso de IA", "El % semanal de cada suscripción de IA", ""),
    ];

    public static readonly IReadOnlyList<string> Defaults = ["state", "model", "ram"];

    public static readonly IReadOnlyList<WidgetFolder> Folders =
    [
        new("ollama", "Ollama", ["state", "model"]),
        new("hardware", "Hardware", ["ram", "vram", "cpu", "battery"]),
        new("daily", "Día a día", ["clock", "pomodoro", "media", "team", "usage"]),
    ];

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
        "team" => "team",
        "usage" => "usage",
        _ => "",
    }).Where(w => w.Length > 0);
}
