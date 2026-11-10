using static IsTargetSleeping.L10n;

namespace IsTargetSleeping;

/// Un tipo de actividad en vivo: algo que pasa ahora y que la pastilla cerrada enseña junto a sus datos.
/// `Left`: solo el indicador de cámara sale a la izquierda del notch cerrado; todo lo demás, a la derecha.
public sealed record ActivityInfo(string Id, string Key, string Glyph, int Priority, bool DefaultOn = true, bool Left = false)
{
    public string Title => tr(Key);
}

/// Una actividad que está ocurriendo (texto ya listo para enseñar).
public sealed record Activity(string Id, string Glyph, string Text, int Priority, bool Left = false, double Progress = -1);

public static class ActivityCatalog
{
    /// Cuántas caben a la vez junto a la pastilla.
    public const int Max = 2;
    /// A la izquierda del notch, solo la cámara; a la derecha, una actividad cada vez (el hueco es fijo y el notch no se ensancha).
    public const int MaxCamera = 1, MaxRight = 1;
    /// Las rápidas (un aviso puntual o una acción corta) pasan por delante de las que duran: mientras ocurren, solas; al acabar, vuelven las demás.
    public static readonly IReadOnlySet<string> Quick = new HashSet<string> { "notice", "volume", "brightness", "clean", "power", "pressure", "locks", "bluetooth" };

    /// De más a menos urgente: lo que el usuario espera ver primero (limpieza y arranque) pasa por delante de lo que solo acompaña.
    public static readonly IReadOnlyList<ActivityInfo> All =
    [
        new("notice", "Aviso nuevo", "", 97),
        new("volume", "Volumen", "", 95),
        new("clean", "Liberando RAM", "", 90),
        new("power", "Encendiendo o apagando Ollama", "", 80),
        new("pressure", "Memoria crítica", "", 70),
        new("pomodoro", "Pomodoro en marcha", "", 50),
        new("media", "Música sonando", "", 40),
        new("brightness", "Brillo cambiado", "", 96),
        new("locks", "Bloq. Mayús o Bloq. Núm", "", 34),
        new("bluetooth", "Auricular Bluetooth", "", 33),
        new("camera", "Indicador de cámara", "\uE722", 30, DefaultOn: false, Left: true),
        // Solo enseña algo mientras el equipo trabaja o acaba de dejar una respuesta o tarea lista.
        new("team", "Equipo trabajando", "", 35),
        new("agents", "Agente trabajando", "", 32),
        new("meeting", "Evento en curso", "", 31),
        new("awake", "Mantener despierto", "", 10),
    ];

    public static readonly IReadOnlyList<WidgetFolder> Folders =
    [
        new("ollama", "Ollama y RAM", ["clean", "power", "pressure"]),
        new("devices", "Dispositivos", ["volume", "brightness", "locks", "bluetooth", "camera"]),
        new("agenda", "Agenda", ["pomodoro", "meeting", "awake", "notice"]),
        new("work", "Música y agentes", ["media", "team", "agents"]),
    ];

    public static ActivityInfo? Find(string id) => All.FirstOrDefault(a => a.Id == id);

    /// Las permitidas (null = nunca se tocó: las que vienen activadas por defecto).
    public static List<string> Normalize(IEnumerable<string>? stored) =>
        stored is null ? All.Where(a => a.DefaultOn).Select(a => a.Id).ToList() : stored.Where(id => Find(id) is not null).Distinct().ToList();

    public static List<string> Toggle(IReadOnlyList<string> enabled, string id) =>
        enabled.Contains(id) ? enabled.Where(i => i != id).ToList() : [.. enabled, id];
}

public static class ActivityQueue
{
    /// Lo que se enseña a cada lado del notch: la cámara (izquierda) y, a la derecha, una sola actividad. Si hay rápidas, sale la más urgente de
    /// ellas; si no, las que duran se turnan (`rotation` elige el turno, p. ej. un contador que sube cada pocos segundos).
    public static (List<Activity> Left, List<Activity> Right) PickSides(IEnumerable<Activity> happening, IReadOnlyCollection<string> enabled,
        int rotation = 0, bool cameraInUse = false)
    {
        var picked = happening.Where(a => a.Id == "camera" ? cameraInUse || enabled.Contains(a.Id) : enabled.Contains(a.Id))
            .OrderByDescending(a => a.Priority).ThenBy(a => a.Id, StringComparer.Ordinal).ToList();
        var left = picked.Where(a => a.Id == "camera").Take(ActivityCatalog.MaxCamera).ToList();
        var rest = picked.Where(a => a.Id != "camera").ToList();
        var quick = rest.Where(a => ActivityCatalog.Quick.Contains(a.Id)).ToList();
        var one = quick.Count > 0 ? quick[0] : rest.Count > 0 ? rest[(rotation % rest.Count + rest.Count) % rest.Count] : null;
        return (left, one is null ? [] : [one]);
    }

    /// Las que se enseñan: solo las permitidas, de mayor a menor prioridad y como mucho `max`.
    public static List<Activity> Pick(IEnumerable<Activity> happening, IReadOnlyCollection<string> enabled, int max = ActivityCatalog.Max) =>
        happening.Where(a => enabled.Contains(a.Id)).OrderByDescending(a => a.Priority).ThenBy(a => a.Id, StringComparer.Ordinal).Take(max).ToList();
}
