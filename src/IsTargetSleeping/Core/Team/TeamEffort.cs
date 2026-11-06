namespace IsTargetSleeping;

/// El esfuerzo que se pide para toda la tarea. Auto deja que el router elija el razonamiento de cada paso según su rol;
/// los demás niveles lo fijan igual en todos los agentes (Claude `--effort`, Codex `model_reasoning_effort`).
public enum TeamEffortLevel { Auto, Low, Medium, High, Max }

public static class TeamEffort
{
    public static readonly IReadOnlyList<TeamEffortLevel> All = Enum.GetValues<TeamEffortLevel>();

    /// Imágenes que se pueden adjuntar a una tarea: pocas, pequeñas y de formatos que los dos agentes leen.
    public const int MaxImages = 30;
    /// Por encima de este peso no se prohíbe nada: se avisa de que puede costar más tokens y el usuario decide.
    public const int HeavyImageMegabytes = 10;
    public const long HeavyImageBytes = HeavyImageMegabytes * 1024L * 1024;
    public static readonly IReadOnlySet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };

    public static TeamEffortLevel Parse(string? text) => Enum.TryParse<TeamEffortLevel>(text, out var level) && Enum.IsDefined(level) ? level : TeamEffortLevel.Auto;

    private static readonly string[] Order = ["minimal", "low", "medium", "high", "xhigh", "max", "ultra"];
    private static int Rank(string e) { var i = Array.IndexOf(Order, e); return i < 0 ? Order.Length : i; }

    /// El razonamiento de Codex para un nivel, ajustado a lo que admite el modelo (null = Auto: lo decide el router).
    public static string? ForCodex(TeamEffortLevel level, CodexModel? model)
    {
        if (level == TeamEffortLevel.Auto) return null;
        var wanted = level switch { TeamEffortLevel.Low => "low", TeamEffortLevel.Medium => "medium", TeamEffortLevel.High => "high", _ => "xhigh" };
        if (model is null || model.Efforts.Count == 0 || model.Efforts.Contains(wanted)) return wanted;
        // El más cercano por debajo; si no hay, el más bajo que ofrezca.
        return model.Efforts.Where(e => Rank(e) <= Rank(wanted)).OrderByDescending(Rank).FirstOrDefault() ?? model.Efforts.OrderBy(Rank).First();
    }

    /// El `--effort` de Claude Code para un nivel (null = Auto). Haiku no admite esfuerzo: se deja como está.
    public static string? ForClaude(TeamEffortLevel level, TeamAgent agent) =>
        level == TeamEffortLevel.Auto || agent == TeamAgent.Haiku || agent == TeamAgent.Codex ? null
        : level switch { TeamEffortLevel.Low => "low", TeamEffortLevel.Medium => "medium", TeamEffortLevel.High => "high", _ => "max" };

    /// Pone el esfuerzo pedido en un paso ya enrutado; en Auto no toca nada.
    public static TeamStep Apply(TeamStep step, TeamEffortLevel level, IReadOnlyList<CodexModel> models)
    {
        if (level == TeamEffortLevel.Auto) return step;
        if (step.Agent == TeamAgent.Codex) return step with { Effort = ForCodex(level, models.FirstOrDefault(m => m.Slug == step.Model)) ?? step.Effort };
        return step with { Effort = ForClaude(level, step.Agent) };
    }

    /// ¿Pesa lo bastante como para avisar antes de adjuntarlo?
    public static bool IsHeavy(string path)
    {
        try { return new FileInfo(path).Length > HeavyImageBytes; }
        catch { return false; }
    }

    /// ¿Se puede adjuntar este archivo? Devuelve el motivo si no (null = sí). El peso no cuenta: decide el usuario.
    public static string? Reject(string path)
    {
        try
        {
            if (!ImageExtensions.Contains(Path.GetExtension(path))) return "formato";
            return new FileInfo(path).Exists ? null : "no existe";
        }
        catch { return "no existe"; }
    }
}
