using static IsTargetSleeping.L10n;

namespace IsTargetSleeping;

/// Un tipo de actividad en vivo: algo que pasa ahora y que la pastilla cerrada enseña junto a sus datos.
public sealed record ActivityInfo(string Id, string Key, string Glyph, int Priority, bool DefaultOn = true)
{
    public string Title => tr(Key);
}

/// Una actividad que está ocurriendo (texto ya listo para enseñar).
public sealed record Activity(string Id, string Glyph, string Text, int Priority);

public static class ActivityCatalog
{
    /// Cuántas caben a la vez junto a la pastilla.
    public const int Max = 2;

    /// De más a menos urgente: lo que el usuario espera ver primero (limpieza y arranque) pasa por delante de lo que solo acompaña.
    public static readonly IReadOnlyList<ActivityInfo> All =
    [
        new("volume", "Volumen", "", 95),
        new("clean", "Liberando RAM", "", 90),
        new("power", "Encendiendo o apagando Ollama", "", 80),
        new("pressure", "Memoria crítica", "", 70),
        new("pomodoro", "Pomodoro en marcha", "", 50),
        // Apagada por defecto: leer qué suena es lo único que mira lo que hace el usuario fuera de la app.
        new("media", "Música sonando", "", 40, DefaultOn: false),
        new("awake", "Mantener despierto", "", 10),
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
    /// Las que se enseñan: solo las permitidas, de mayor a menor prioridad y como mucho `max`.
    public static List<Activity> Pick(IEnumerable<Activity> happening, IReadOnlyCollection<string> enabled, int max = ActivityCatalog.Max) =>
        happening.Where(a => enabled.Contains(a.Id)).OrderByDescending(a => a.Priority).ThenBy(a => a.Id, StringComparer.Ordinal).Take(max).ToList();
}
