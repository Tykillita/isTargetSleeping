using System.Text;
using System.Text.Json.Nodes;
using IsTargetSleeping;

internal static class AgentQuestionTests
{
    private static JsonObject Questions() => new()
    {
        ["questions"] = new JsonArray(
            new JsonObject
            {
                ["question"] = "Which framework?", ["header"] = "Framework", ["multiSelect"] = false,
                ["options"] = new JsonArray(new JsonObject { ["label"] = "React", ["description"] = "Component library" }, new JsonObject { ["label"] = "Vue", ["description"] = "Progressive" }),
            },
            new JsonObject
            {
                ["question"] = "Which sections?", ["header"] = "Sections", ["multiSelect"] = true,
                ["options"] = new JsonArray(new JsonObject { ["label"] = "Intro" }, new JsonObject { ["label"] = "Conclusion" }),
            }),
    };

    private static string Payload(string evt, string tool, JsonObject input) => new JsonObject
    {
        ["hook_event_name"] = evt, ["session_id"] = "s1", ["cwd"] = @"C:\proj\app", ["tool_name"] = tool, ["tool_input"] = input,
    }.ToJsonString();

    private static string Line(string payload) => new JsonObject { ["agent"] = "claude", ["payload"] = JsonNode.Parse(payload) }.ToJsonString();

    public static void Run(Action<string, bool> check)
    {
        // — Las preguntas y el plan se entienden
        var parsed = AgentQuestions.Parse(Questions());
        check("se leen las preguntas con sus opciones", parsed.Count == 2 && parsed[0].Options.Count == 2 && parsed[1].Multi && parsed[0].Options[0].Description == "Component library");
        var tooMany = new JsonObject { ["questions"] = new JsonArray(Enumerable.Range(0, 9).Select(i => (JsonNode)new JsonObject { ["question"] = $"q{i}", ["options"] = new JsonArray(new JsonObject { ["label"] = "a" }) }).ToArray()) };
        check("como mucho cuatro preguntas", AgentQuestions.Parse(tooMany).Count == AgentQuestions.MaxQuestions);
        check("una pregunta sin opciones se descarta", AgentQuestions.Parse(new JsonObject { ["questions"] = new JsonArray(new JsonObject { ["question"] = "x" }) }).Count == 0 && AgentQuestions.Parse(null).Count == 0);
        check("el plan se lee y se recorta", AgentQuestions.PlanText(new JsonObject { ["plan"] = "1. Hacer" }) == "1. Hacer" && AgentQuestions.PlanText(new JsonObject { ["plan"] = new string('x', 20000) })!.Length <= AgentQuestions.PlanMax + 1);
        check("las respuestas se unen con coma y espacio", AgentQuestions.Answers([("p", new[] { "A", "B" }), ("vacía", Array.Empty<string>())]) is { Count: 1 } a && a["p"] == "A, B");

        // — Qué llega por PreToolUse y qué no
        check("una pregunta por PreToolUse necesita respuesta", AgentHooks.Parse(Line(Payload("PreToolUse", "AskUserQuestion", Questions()))) is { NeedsAnswer: true, Kind: HookKind.Question });
        check("un plan por PreToolUse necesita respuesta", AgentHooks.Parse(Line(Payload("PreToolUse", "ExitPlanMode", new JsonObject { ["plan"] = "# Plan" }))) is { NeedsAnswer: true, Kind: HookKind.Plan });
        check("un PreToolUse de otra herramienta se ignora", AgentHooks.Parse(Line(Payload("PreToolUse", "Bash", new JsonObject { ["command"] = "ls" }))) is null);
        var rules = new HookRules(Path.Combine(Path.GetTempPath(), "its-q-" + Guid.NewGuid().ToString("N") + ".json"));
        check("las preguntas y los planes nunca son reglas «Siempre»", HookRules.KeyFor(AgentHooks.Parse(Line(Payload("PreToolUse", "AskUserQuestion", Questions())))!) is null);

        // — De punta a punta por el pipe
        var pipe = "its-q-" + Guid.NewGuid().ToString("N");
        using var server = new HookServer(rules, pipe, TimeSpan.FromSeconds(2));
        Action<HookRequest> handler = _ => { };
        server.Requested += r => handler(r);
        server.Start();
        string Client(string payload, int waitSeconds = 10)
        {
            var o = new MemoryStream();
            AgentHooks.RunClient("claude", new MemoryStream(Encoding.UTF8.GetBytes(payload)), o, pipe, TimeSpan.FromSeconds(waitSeconds));
            return Encoding.UTF8.GetString(o.ToArray());
        }

        handler = r => r.Reply(new Dictionary<string, string> { ["Which framework?"] = "React", ["Which sections?"] = "Intro, Conclusion" });
        var answered = JsonNode.Parse(Client(Payload("PreToolUse", "AskUserQuestion", Questions())))!["hookSpecificOutput"]!;
        check("contestar una pregunta devuelve allow con updatedInput", answered["hookEventName"]!.GetValue<string>() == "PreToolUse" && answered["permissionDecision"]!.GetValue<string>() == "allow");
        check("updatedInput repite las preguntas y añade las respuestas", answered["updatedInput"]!["questions"]!.AsArray().Count == 2
            && answered["updatedInput"]!["answers"]!["Which framework?"]!.GetValue<string>() == "React" && answered["updatedInput"]!["answers"]!["Which sections?"]!.GetValue<string>() == "Intro, Conclusion");

        handler = r => r.Decide(HookVerdict.Allow);
        var approved = JsonNode.Parse(Client(Payload("PreToolUse", "ExitPlanMode", new JsonObject { ["plan"] = "# Plan\n1. Hacer" })))!["hookSpecificOutput"]!;
        check("aprobar un plan devuelve allow con el plan en updatedInput", approved["permissionDecision"]!.GetValue<string>() == "allow" && approved["updatedInput"]!["plan"]!.GetValue<string>().StartsWith("# Plan"));

        handler = r => r.Reject("Quiero otro plan");
        var changes = JsonNode.Parse(Client(Payload("PreToolUse", "ExitPlanMode", new JsonObject { ["plan"] = "x" })))!["hookSpecificOutput"]!;
        check("pedir cambios devuelve deny con el motivo para el agente", changes["permissionDecision"]!.GetValue<string>() == "deny" && changes["permissionDecisionReason"]!.GetValue<string>() == "Quiero otro plan");

        handler = r => r.Decide(HookVerdict.Fold);
        check("dejarlo a la terminal no imprime nada", Client(Payload("PreToolUse", "AskUserQuestion", Questions())) == "");

        handler = r => r.Decide(HookVerdict.Deny);
        var perm = JsonNode.Parse(Client(Payload("PermissionRequest", "Bash", new JsonObject { ["command"] = "npm test" })))!["hookSpecificOutput"]!;
        check("los permisos siguen en su forma de siempre", perm["hookEventName"]!.GetValue<string>() == "PermissionRequest" && perm["decision"]!["behavior"]!.GetValue<string>() == "deny");

        // — Instalador: PreToolUse solo para preguntas y planes
        var settings = Path.Combine(Path.GetTempPath(), "its-q-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            AgentHooksInstaller.Install(settings, @"C:\x\isTargetSleeping.exe");
            var group = JsonNode.Parse(File.ReadAllText(settings))!["hooks"]!["PreToolUse"]![0]!;
            check("PreToolUse se instala solo para preguntas y planes", group["matcher"]!.GetValue<string>() == "AskUserQuestion|ExitPlanMode");
            check("los demás eventos no llevan filtro", JsonNode.Parse(File.ReadAllText(settings))!["hooks"]!["Stop"]![0]!["matcher"] is null);
        }
        finally { try { File.Delete(settings); } catch { } foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(settings) + ".its-backup-*")) try { File.Delete(f); } catch { } }
    }
}
