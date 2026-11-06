using System.Text.Json;

namespace IsTargetSleeping;

/// Cuánta potencia pide un paso de Codex: Ligero (tareas fáciles, barato), Estándar (el de diario) o Frontera (lo más exigente).
public enum CodexTier { Light, Standard, Frontier }

public sealed record CodexModel(string Slug, string Name, string Description, int Priority, string DefaultEffort, IReadOnlyList<string> Efforts);

/// Los modelos que Codex ofrece de verdad en este PC: se leen de su caché (`~/.codex/models_cache.json`), solo lectura,
/// en vez de suponer nombres. Sin caché no se fuerza ninguno y Codex usa el suyo.
public static class CodexModels
{
    // Orden de menos a más razonamiento; el que no esté aquí cuenta como el más alto.
    static readonly string[] EffortOrder = ["minimal", "low", "medium", "high", "xhigh", "max", "ultra"];

    public static string Home => Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } h ? h
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

    /// Los modelos visibles para el usuario, del primero (más prioritario) al último. Lista vacía si no hay caché legible.
    public static IReadOnlyList<CodexModel> Load(string? home = null)
    {
        try
        {
            var file = Path.Combine(home ?? Home, "models_cache.json");
            if (!File.Exists(file)) return [];
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (!doc.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array) return [];
            var result = new List<CodexModel>();
            foreach (var m in models.EnumerateArray())
            {
                var slug = CodexRunner.Str(m, "slug");
                if (slug is null || CodexRunner.Str(m, "visibility") != "list") continue;
                var efforts = m.TryGetProperty("supported_reasoning_levels", out var levels) && levels.ValueKind == JsonValueKind.Array
                    ? levels.EnumerateArray().Select(l => CodexRunner.Str(l, "effort")).OfType<string>().ToList() : [];
                result.Add(new(slug, CodexRunner.Str(m, "display_name") ?? slug, CodexRunner.Str(m, "description") ?? "",
                    (int)CodexRunner.Num(m, "priority"), CodexRunner.Str(m, "default_reasoning_level") ?? "medium", efforts));
            }
            return [.. result.OrderBy(m => m.Priority)];
        }
        catch { return []; }
    }

    static bool Older(CodexModel m) => m.Description.Contains("older", StringComparison.OrdinalIgnoreCase) || m.Description.Contains("previous", StringComparison.OrdinalIgnoreCase);
    static bool Says(CodexModel m, params string[] words) => words.Any(w => m.Description.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// El modelo para cada nivel, según lo que Codex dice de él en su propia descripción; si no hay uno claro, el estándar.
    public static CodexModel? Pick(IReadOnlyList<CodexModel> models, CodexTier tier)
    {
        var standard = models.FirstOrDefault(m => !Older(m) && Says(m, "workhorse")) ?? models.FirstOrDefault();
        return tier switch
        {
            CodexTier.Frontier => models.FirstOrDefault(m => Says(m, "frontier", "most demanding")) ?? standard,
            CodexTier.Light => models.FirstOrDefault(m => !Older(m) && Says(m, "affordable", "easier", "efficient")) ?? standard,
            _ => standard,
        };
    }

    /// A un paso de Codex le pone el modelo y el razonamiento que le tocan (o el que se eligió a mano); los demás pasos no cambian.
    public static TeamStep Apply(TeamStep step, IReadOnlyList<CodexModel> models, string? slug = null)
    {
        if (step.Agent != TeamAgent.Codex || models.Count == 0) return step;
        var model = models.FirstOrDefault(m => m.Slug == slug) ?? Pick(models, step.Tier);
        return model is null ? step : step with { Model = model.Slug, Effort = Effort(model, step.Role, step.Tier) };
    }

    /// El razonamiento que pide cada rol (revisar y pensar cuestan tokens: lo justo), ajustado a lo que el modelo admite.
    public static string Effort(CodexModel model, TeamRole role, CodexTier tier)
    {
        var wanted = role switch
        {
            TeamRole.Clarify => "low",
            TeamRole.Plan or TeamRole.Design => "medium",
            _ => tier == CodexTier.Frontier ? "high" : "medium",
        };
        if (model.Efforts.Count == 0 || model.Efforts.Contains(wanted)) return model.Efforts.Count == 0 ? model.DefaultEffort : wanted;
        int Rank(string e) { var i = Array.IndexOf(EffortOrder, e); return i < 0 ? EffortOrder.Length : i; }
        // El más cercano por debajo; si no hay, el más bajo que ofrezca.
        return model.Efforts.Where(e => Rank(e) <= Rank(wanted)).OrderByDescending(Rank).FirstOrDefault() ?? model.Efforts.OrderBy(Rank).First();
    }
}
