using System.Text.Json.Nodes;

namespace IsTargetSleeping;

public enum HookKind { Permission, Question, Plan }
public sealed record AskOption(string Label, string Description);
public sealed record AskQuestion(string Text, string Header, bool Multi, IReadOnlyList<AskOption> Options);

/// Lo que se puede responder desde el notch además de un permiso: las preguntas de Claude Code (AskUserQuestion) y la revisión de un plan (ExitPlanMode).
public static class AgentQuestions
{
    public const string QuestionTool = "AskUserQuestion";
    public const string PlanTool = "ExitPlanMode";
    public const int MaxQuestions = 4;
    public const int MaxOptions = 6;
    public const int PlanMax = 8000;

    public static HookKind KindOf(string? tool) => tool == QuestionTool ? HookKind.Question : tool == PlanTool ? HookKind.Plan : HookKind.Permission;

    /// Las preguntas de un `tool_input` (hasta 4, con hasta 6 opciones cada una). Lo que no se entiende se descarta: nunca rompe la tarjeta.
    public static IReadOnlyList<AskQuestion> Parse(JsonNode? input)
    {
        var list = new List<AskQuestion>();
        if (input?["questions"] is not JsonArray questions) return list;
        foreach (var node in questions.Take(MaxQuestions))
        {
            var text = node?["question"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(text)) continue;
            var options = new List<AskOption>();
            if (node?["options"] is JsonArray array)
                foreach (var o in array.Take(MaxOptions))
                    if (o?["label"]?.GetValue<string>() is { Length: > 0 } label) options.Add(new(label, o["description"]?.GetValue<string>() ?? ""));
            if (options.Count == 0) continue;   // sin opciones no hay nada que elegir aquí: que conteste la terminal
            list.Add(new(text, node?["header"]?.GetValue<string>() ?? "", node?["multiSelect"]?.GetValue<bool>() ?? false, options));
        }
        return list;
    }

    /// El texto del plan de ExitPlanMode (recortado).
    public static string? PlanText(JsonNode? input) =>
        input?["plan"]?.GetValue<string>() is { Length: > 0 } plan ? (plan.Length <= PlanMax ? plan : plan[..PlanMax] + "…") : null;

    /// Las respuestas: una por pregunta (el texto de la pregunta → la opción elegida; con varias, separadas por coma y espacio).
    public static Dictionary<string, string> Answers(IEnumerable<(string Question, IEnumerable<string> Chosen)> picks) =>
        picks.Where(p => p.Chosen.Any()).ToDictionary(p => p.Question, p => string.Join(", ", p.Chosen));
}
