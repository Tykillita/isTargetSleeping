using IsTargetSleeping;

internal static class TeamTests
{
    static TeamPlan Route(string prompt, TeamPreset preset = TeamPreset.Auto, params string[] files) =>
        TeamRouter.Route(TeamRouter.Profile(prompt, files), preset);
    static string Shape(TeamPlan p) => string.Join(" ", p.Steps.Select(s => $"{s.Agent}:{s.Role}"));

    public static void Run(Action<string, bool> check)
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");

        // Router: los ejemplos del plan.
        var polish = Route("pule el botón de guardar: alinea el padding", files: "src/UI/Save.xaml");
        check("equipo: pulir un botón → Sonnet solo, sin Codex", Shape(polish) == "Sonnet:Implement");

        var redesign = Route("rediseña la interfaz del panel de ajustes", files: ["src/UI/Panel.xaml", "src/UI/Panel.cs"]);
        check("equipo: rediseñar → Opus planifica y diseña, Sonnet XAML, Codex lógica, revisión cruzada",
            Shape(redesign) == "Opus:Plan Opus:Design Sonnet:Implement Codex:Implement Codex:Review Sonnet:Review");

        var bug = Route("arregla el crash al cerrar: debe guardar los ajustes en Prefs.cs y Settings.cs y Shell.cs", files: ["Prefs.cs", "Settings.cs", "Shell.cs"]);
        check("equipo: bug claro de varios archivos → diagnóstico, Codex corrige, Claude revisa",
            Shape(bug) == "Sonnet:Plan Codex:Implement Sonnet:Review");

        var small = Route("arregla el crash en Prefs.cs: debe guardar", files: "Prefs.cs");
        check("equipo: bug de un archivo → Sonnet solo (delegar costaría más)", Shape(small) == "Sonnet:Implement");

        var ambiguous = Route("mejora la app");
        check("equipo: sin archivos ni criterio → primero se aclara", ambiguous.Steps[0].Role == TeamRole.Clarify);
        check("equipo: aclarar lo hace el modelo más barato: Haiku, y en Ahorro el Codex ligero",
            ambiguous.Steps[0].Agent == TeamAgent.Haiku && Route("mejora la app", TeamPreset.Saving).Steps[0] is { Agent: TeamAgent.Codex, Role: TeamRole.Clarify, Tier: CodexTier.Light });
        var quickPlan = TeamRouter.Route(TeamRouter.Profile("¿qué es una lambda?")).Steps;
        check("equipo: si Haiku no está, responde el Codex ligero; si no está Codex, Haiku; si están los dos, no cambia nada",
            TeamRouter.AnswerWith(quickPlan, false, true)[0].Agent == TeamAgent.Codex && TeamRouter.AnswerWith(quickPlan, true, true)[0].Agent == TeamAgent.Haiku
            && TeamRouter.AnswerWith(TeamRouter.Route(TeamRouter.Profile("¿qué es una lambda?"), TeamPreset.CodexOnly).Steps, true, false)[0].Agent == TeamAgent.Haiku
            && TeamRouter.AnswerWith(quickPlan, false, false)[0].Agent == TeamAgent.Haiku && TeamRouter.AnswerWith(Route("arregla el bug", files: "A.cs").Steps, false, true).Count > 1);

        var risky = Route("cambia el instalador: debe firmar el paquete", files: ["packaging/installer.iss", "build.ps1", "package.ps1"]);
        check("equipo: riesgo → planifica Opus y hay revisión cruzada",
            risky.Steps[0].Agent == TeamAgent.Opus && risky.Steps.Any(s => s.Role == TeamRole.Review));

        check("equipo: el revisor nunca es quien escribió",
            new[] { redesign, bug, risky, polish }.All(p => p.Steps.Where(s => s.Role == TeamRole.Review)
                .All(r => !p.Steps.Where(s => s.Role == TeamRole.Implement).Select(s => s.Agent).Contains(r.Agent) || p.Steps.Count(s => s.Role == TeamRole.Implement) > 1)));

        check("equipo: ningún plan automático es revisión propia (un rediseño se revisa entre Claude y Codex)",
            new[] { redesign, bug, risky }.All(p => !TeamRouter.SelfReviewed(p.Steps)));
        check("equipo: el mismo agente escribiendo y revisando sí es revisión propia",
            TeamRouter.SelfReviewed([new(TeamAgent.Codex, TeamRole.Implement, true, 30, ""), new(TeamAgent.Codex, TeamRole.Review, false, 10, "")]));
        check("equipo: Solo Claude y Solo Codex son un paso", Shape(Route("añade X", TeamPreset.ClaudeOnly)) == "Sonnet:Implement"
            && Shape(Route("añade X", TeamPreset.CodexOnly)) == "Codex:Implement");
        check("equipo: Velocidad no revisa", Route("arregla el bug en A.cs", TeamPreset.Speed, "A.cs").Steps.All(s => s.Role != TeamRole.Review));
        check("equipo: Calidad empieza por Opus y revisa",
            Route("implementa X en A.cs: debe sumar", TeamPreset.Quality, "A.cs") is { } q && q.Steps[0].Agent == TeamAgent.Opus && q.Steps.Any(s => s.Role == TeamRole.Review));
        check("equipo: el write de cada rol es coherente", redesign.Steps.All(s => s.CanWrite == (s.Role == TeamRole.Implement)));

        check("equipo: política inexistente usa los valores por defecto", TeamPolicy.Load(Path.Combine(fixtures, "no-existe.json")) == TeamPolicy.Default);

        // Runners: salida real de `codex exec --json` y forma de `claude -p --output-format json`.
        var codex = CodexRunner.Parse(File.ReadAllLines(Path.Combine(fixtures, "codex-exec.jsonl")));
        check("equipo: Codex → mensaje, hilo y tokens", codex is { Ok: true, Message: "listo", InputTokens: 24118, OutputTokens: 45, SessionId: "01a156ce-2ac1-7d31-b6d3-9348e25872e2" });
        check("equipo: Codex no cuenta la entrada cacheada como gasto",
            CodexRunner.Parse(["{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":24116,\"cached_input_tokens\":12800,\"output_tokens\":5}}"]) is { InputTokens: 11316, OutputTokens: 5 });
        check("equipo: Codex sin turn.completed no es éxito", !CodexRunner.Parse(["{\"type\":\"thread.started\",\"thread_id\":\"x\"}"]).Ok);
        check("equipo: Codex con error lo reporta",
            CodexRunner.Parse(["{\"type\":\"turn.failed\",\"error\":{\"message\":\"sin cuota\"}}"]) is { Ok: false, Error: "sin cuota" });
        check("equipo: Codex prefiere el último mensaje del archivo -o", CodexRunner.Parse(File.ReadAllLines(Path.Combine(fixtures, "codex-exec.jsonl")), " final ").Message == "final");

        var claude = ClaudeRunner.Parse(File.ReadAllText(Path.Combine(fixtures, "claude-result.json")));
        check("equipo: Claude → texto, sesión, coste y tokens (sin la caché leída)", claude is { Ok: true, Message: "listo", InputTokens: 3120, OutputTokens: 45, CostUsd: 0.0042 });
        check("equipo: Claude con is_error no es éxito", ClaudeRunner.Parse("{\"is_error\":true,\"result\":\"sesión caducada\"}") is { Ok: false, Error: "sesión caducada" });
        check("equipo: Claude sin JSON no es éxito", !ClaudeRunner.Parse("basura").Ok);
        check("equipo: Codex nunca usa danger-full-access", !CodexRunner.Args(".", true, "x").Contains("danger-full-access")
            && CodexRunner.Args(".", false, "x").Contains("read-only"));
        check("equipo: Claude arranca sin MCP, skills ni ajustes de usuario (ahorra ~100.000 tokens por paso)",
            ClaudeRunner.Args(TeamAgent.Haiku, true) is var a && a.Contains("--strict-mcp-config") && a.Contains("--disable-slash-commands")
            && a.Zip(a.Skip(1)).Any(p => p is ("--setting-sources", "project")));
        check("equipo: Claude de solo lectura no puede editar", !string.Join(",", ClaudeRunner.Args(TeamAgent.Sonnet, false)).Contains("Edit"));

        // Modelos reales de Codex, leídos de su caché (fixture recortado de una caché real).
        var models = CodexModels.Load(Path.Combine(fixtures, "codex-models"));
        check("equipo: lee los modelos visibles de Codex, por prioridad y sin los ocultos",
            models.Select(m => m.Slug).SequenceEqual(["gpt-6.1-sol", "gpt-6-astra", "gpt-6-luna", "gpt-5.6-sol"]) && models[0].Name == "GPT-6.1-Sol");
        check("equipo: sin caché no hay modelos (Codex usa el suyo)", CodexModels.Load(Path.Combine(fixtures, "no-hay")).Count == 0);
        check("equipo: Estándar = el caballo de batalla actual, no el antiguo", CodexModels.Pick(models, CodexTier.Standard)?.Slug == "gpt-6.1-sol");
        check("equipo: Frontera = el más exigente; Ligero = el barato", CodexModels.Pick(models, CodexTier.Frontier)?.Slug == "gpt-6-astra"
            && CodexModels.Pick(models, CodexTier.Light)?.Slug == "gpt-6-luna");
        check("equipo: sin modelos no se elige ninguno", CodexModels.Pick([], CodexTier.Frontier) is null);
        var luna = models.Single(m => m.Slug == "gpt-6-luna");
        check("equipo: razonamiento por rol y nivel", CodexModels.Effort(luna, TeamRole.Clarify, CodexTier.Light) == "low"
            && CodexModels.Effort(luna, TeamRole.Implement, CodexTier.Standard) == "medium" && CodexModels.Effort(luna, TeamRole.Implement, CodexTier.Frontier) == "high");
        var limited = luna with { Efforts = ["low", "xhigh"] };
        check("equipo: si el modelo no admite el razonamiento pedido, usa el más cercano por debajo", CodexModels.Effort(limited, TeamRole.Implement, CodexTier.Standard) == "low");
        var codexArgs = CodexRunner.Args(".", true, "x", "gpt-6.1-sol", "high");
        check("equipo: Codex recibe -m y el razonamiento; sin ellos no los pasa", codexArgs.SequenceEqual(["exec", "--json", "-s", "workspace-write", "-C", ".", "-o", "x", "-m", "gpt-6.1-sol", "-c", "model_reasoning_effort=\"high\"", "-"])
            && !CodexRunner.Args(".", false, "x").Contains("-m"));
        // Preguntas sencillas: sin proyecto, un solo agente.
        var question = TeamRouter.Profile("¿Qué significa otorrinolaringólogo?");
        check("equipo: una pregunta sencilla se detecta y la responde un solo agente de solo lectura, sin proyecto",
            question.Kind == TaskKind.Question && Shape(TeamRouter.Route(question)) == "Haiku:Answer" && !TeamRouter.Route(question).Steps[0].CanWrite);
        check("equipo: Calidad responde con Sonnet y Solo Codex con Codex",
            Shape(TeamRouter.Route(question, TeamPreset.Quality)) == "Sonnet:Answer" && Shape(TeamRouter.Route(question, TeamPreset.CodexOnly)) == "Codex:Answer");
        check("equipo: lo que nombra archivos o pide cambios no es una pregunta sencilla",
            new[] { "¿cómo arreglo el bug de Prefs.cs?", "pule el botón de guardar", "resume los commits de ayer", "arregla el cierre", "mejora la app" }.All(t => TeamRouter.Profile(t).Kind != TaskKind.Question));
        check("equipo: preguntas de ejemplo en español e inglés se detectan",
            new[] { "qué es una lambda", "how do I say hello in French?", "explica la diferencia entre TCP y UDP", "¿cuántos días tiene febrero?", "what does idempotent mean" }.All(t => TeamRouter.Profile(t).Kind == TaskKind.Question));
        check("equipo: Codex responde sin pedir un repositorio de git; Claude, sin modo plan y con búsqueda web",
            CodexRunner.Args(".", false, "x", null, null, null, true).Contains("--skip-git-repo-check") && !CodexRunner.Args(".", false, "x").Contains("--skip-git-repo-check")
            && ClaudeRunner.Args(TeamAgent.Haiku, false, null, null, true) is var answerArgs && answerArgs.SkipWhile(a => a != "--permission-mode").Skip(1).First() == "default"
            && string.Join(",", answerArgs).Contains("WebSearch") && !string.Join(",", ClaudeRunner.Args(TeamAgent.Haiku, false)).Contains("WebSearch"));
        // Los pasos de solo lectura no usan el modo plan de Claude Code (intenta ExitPlanMode, que aquí no existe) y el plan se pide completo y sin disculpas.
        var readOnlyArgs = ClaudeRunner.Args(TeamAgent.Sonnet, false);
        var planPrompt = typeof(TeamRun).GetMethod("Prompt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [new TeamStep(TeamAgent.Sonnet, TeamRole.Plan, false, 15, ""), "tarea", "", ""]) as string ?? "";
        check("equipo: un paso de solo lectura va en modo normal (no plan) y sin herramientas que escriban; el plan se pide por secciones, completo y sin disculpas",
            readOnlyArgs.SkipWhile(a => a != "--permission-mode").Skip(1).First() == "default" && !readOnlyArgs.Contains("plan") && !string.Join(",", readOnlyArgs).Contains("Edit")
            && ClaudeRunner.Args(TeamAgent.Sonnet, true).SkipWhile(a => a != "--permission-mode").Skip(1).First() == "acceptEdits"
            && planPrompt.Contains("**Archivos a tocar**") && planPrompt.Contains("**Pasos**") && planPrompt.Contains("**Criterio de hecho**") && planPrompt.Contains("no en un archivo"));
        var apology = "No pude invocar la herramienta `ExitPlanMode` (no está disponible), así que dejo el plan escrito en C:\\plans\\x.md.\n\n**Archivos a tocar:**\n1. a.cs\n\n¿Apruebas este plan para empezar a implementarlo?";
        check("equipo: de un plan se quitan las disculpas por ExitPlanMode y la pregunta final de aprobación, y se conserva lo demás",
            TeamRun.CleanPlan(apology) == "**Archivos a tocar:**\n1. a.cs" && TeamRun.CleanPlan("Solo un párrafo.") == "Solo un párrafo.");
        // Registro en vivo: lo que hace el agente, en frases cortas, a partir de la salida de cada CLI.
        const string claudeRead = """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Read","input":{"file_path":"C:\\src\\App.xaml"}}]}}""";
        const string claudeGrep = """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Grep","input":{"pattern":"WeatherWidget"}}]}}""";
        const string claudeThink = """{"type":"assistant","message":{"content":[{"type":"thinking","thinking":"secreto"}]}}""";
        const string codexCommand = """{"type":"item.started","item":{"type":"command_execution","command":"git status"}}""";
        check("equipo: el registro en vivo resume cada evento («Leyendo App.xaml», «Buscando…», «Pensando…», «Ejecutando…») sin enseñar el razonamiento",
            TeamActivity.Summarize(TeamAgent.Sonnet, claudeRead) == "Leyendo App.xaml" && TeamActivity.Summarize(TeamAgent.Sonnet, claudeGrep) == "Buscando «WeatherWidget»"
            && TeamActivity.Summarize(TeamAgent.Sonnet, claudeThink) == "Pensando…" && !TeamActivity.Summarize(TeamAgent.Sonnet, claudeThink)!.Contains("secreto")
            && TeamActivity.Summarize(TeamAgent.Codex, codexCommand) == "Revisando los cambios" && TeamActivity.Summarize(TeamAgent.Sonnet, "no es json") is null);
        var streamOutput = string.Join("\n", """{"type":"system","subtype":"init"}""", claudeRead,
            """{"type":"result","subtype":"success","is_error":false,"result":"hecho","total_cost_usd":0.01,"session_id":"abc","usage":{"input_tokens":5,"output_tokens":7}}""",
            """{"type":"system","subtype":"task_summary"}""");
        check("equipo: Claude en modo stream-json (con --verbose) da su resultado desde el evento «result» y el formato de siempre se sigue entendiendo",
            ClaudeRunner.Parse(streamOutput) is { Ok: true, Message: "hecho", OutputTokens: 7 }
            && ClaudeRunner.Args(TeamAgent.Haiku, false, null, null, false, true) is var streamArgs && streamArgs.Contains("stream-json") && streamArgs.Contains("--verbose")
            && !ClaudeRunner.Args(TeamAgent.Haiku, false).Contains("--verbose")
            && ClaudeRunner.Parse("""{"result":"ok","is_error":false}""") is { Ok: true, Message: "ok" }
            && ClaudeRunner.Parse(string.Join("\n", claudeRead, """{"duration_api_ms":5,"stop_reason":"end_turn","type":"result","is_error":false,"result":"otro orden","usage":{"input_tokens":1,"output_tokens":2}}""")) is { Ok: true, Message: "otro orden" });
        // Esfuerzo manual y adjuntos.
        var fullEfforts = luna with { Efforts = ["low", "medium", "high", "xhigh"] };
        check("equipo: el esfuerzo Auto no toca nada; Bajo a Máximo fijan el de Codex y el de Claude",
            TeamEffort.ForCodex(TeamEffortLevel.Auto, luna) is null && TeamEffort.ForCodex(TeamEffortLevel.Low, fullEfforts) == "low"
            && TeamEffort.ForCodex(TeamEffortLevel.Max, fullEfforts) == "xhigh" && TeamEffort.ForClaude(TeamEffortLevel.High, TeamAgent.Sonnet) == "high"
            && TeamEffort.ForClaude(TeamEffortLevel.Max, TeamAgent.Opus) == "max");
        check("equipo: si el modelo no admite el esfuerzo pedido usa el más alto que ofrece por debajo",
            TeamEffort.ForCodex(TeamEffortLevel.Max, luna with { Efforts = ["low", "medium"] }) == "medium");
        check("equipo: Haiku, Codex y Auto no reciben --effort de Claude; Sonnet sí lo lleva cuando se pide",
            TeamEffort.ForClaude(TeamEffortLevel.High, TeamAgent.Haiku) is null && TeamEffort.ForClaude(TeamEffortLevel.High, TeamAgent.Codex) is null
            && TeamEffort.ForClaude(TeamEffortLevel.Auto, TeamAgent.Sonnet) is null && !ClaudeRunner.Args(TeamAgent.Sonnet, true).Contains("--effort")
            && ClaudeRunner.Args(TeamAgent.Sonnet, true, "low") is var withEffort && withEffort.SkipWhile(a => a != "--effort").Skip(1).FirstOrDefault() == "low");
        var sonnetStep = new TeamStep(TeamAgent.Sonnet, TeamRole.Implement, true, 5, "");
        var codexStep = new TeamStep(TeamAgent.Codex, TeamRole.Implement, true, 5, "", Model: fullEfforts.Slug, Effort: "medium");
        // La rotación al pulsar el icono de un paso pasa por todo y vuelve a lo del router.
        var rotation = new List<string>();
        (TeamAgent Agent, string? Slug)? stop = null;
        bool byHand = false; var agentNow = TeamAgent.Sonnet; string? slugNow = null;
        for (int i = 0; i < 40; i++)
        {
            var next = TeamRouter.NextChoice(byHand, agentNow, slugNow, CodexModels.Load(Path.Combine(fixtures, "codex-models")));
            if (next is null) { stop = null; rotation.Add("router"); break; }
            (agentNow, slugNow) = next.Value; byHand = true;
            rotation.Add(agentNow == TeamAgent.Codex ? slugNow ?? "codex" : agentNow.ToString());
        }
        var fixtureModels = CodexModels.Load(Path.Combine(fixtures, "codex-models"));
        check("equipo: pulsar el icono de un paso rota por Haiku, Sonnet, Opus y TODOS los modelos de Codex, y vuelve a lo del router",
            fixtureModels.Count > 0 && rotation.Count == 3 + fixtureModels.Count + 1 && rotation[0] == "Haiku" && rotation.Contains("Opus") && rotation[^1] == "router"
            && fixtureModels.All(m => rotation.Contains(m.Slug)) && rotation.Distinct().Count() == rotation.Count);
        check("equipo: la rotación empieza en el primero desde lo del router y sigue desde donde estaba un paso ya cambiado",
            TeamRouter.NextChoice(false, TeamAgent.Codex, null, fixtureModels) is { Agent: TeamAgent.Haiku } && TeamRouter.NextChoice(true, TeamAgent.Sonnet, null, fixtureModels) is { Agent: TeamAgent.Opus }
            && TeamRouter.NextChoice(true, TeamAgent.Codex, fixtureModels[^1].Slug, fixtureModels) is null && TeamRouter.NextChoice(true, TeamAgent.Codex, fixtureModels[0].Slug, fixtureModels) is { Slug: var second } && second == fixtureModels[1].Slug);
        check("equipo: aplicar el esfuerzo pone el nivel en cada paso y en Auto lo deja igual",
            TeamEffort.Apply(sonnetStep, TeamEffortLevel.Max, [fullEfforts]).Effort == "max" && TeamEffort.Apply(codexStep, TeamEffortLevel.High, [fullEfforts]).Effort == "high"
            && TeamEffort.Apply(codexStep, TeamEffortLevel.Auto, [fullEfforts]) == codexStep && TeamEffort.Apply(sonnetStep, TeamEffortLevel.Auto, []) == sonnetStep);
        check("equipo: el esfuerzo guardado se lee y lo desconocido es Auto",
            TeamEffort.Parse("High") == TeamEffortLevel.High && TeamEffort.Parse("zzz") == TeamEffortLevel.Auto && TeamEffort.Parse(null) == TeamEffortLevel.Auto && TeamEffort.Parse("99") == TeamEffortLevel.Auto);
        var imageArgs = CodexRunner.Args(".", false, "x", null, null, ["a.png", "b.jpg"]);
        check("equipo: las imágenes van a Codex con -i antes de las demás opciones, para que no se lleven el resto",
            imageArgs.Take(6).SequenceEqual(["exec", "--json", "-i", "a.png", "-i", "b.jpg"]) && imageArgs.Contains("-s") && imageArgs[^1] == "-");
        var claudeImageArgs = ClaudeRunner.Args(TeamAgent.Sonnet, false, null, ["C:\\adjuntos\\1"]);
        check("equipo: a Claude se le abre la carpeta de las imágenes para que pueda leerlas",
            claudeImageArgs.SkipWhile(a => a != "--add-dir").Skip(1).FirstOrDefault() == "C:\\adjuntos\\1");
        var fakePng = Path.Combine(Path.GetTempPath(), $"its-adj-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(fakePng, [1, 2, 3]);
        check("equipo: solo se adjuntan imágenes de formatos conocidos y que existen; el peso no las prohíbe, solo avisa",
            TeamEffort.Reject(fakePng) is null && TeamEffort.Reject("notas.txt") == "formato" && TeamEffort.Reject(fakePng + ".nada.png") == "no existe" && !TeamEffort.IsHeavy(fakePng));
        var heavyPng = Path.Combine(Path.GetTempPath(), $"its-pesada-{Guid.NewGuid():N}.png");
        using (var heavyFile = File.Create(heavyPng)) heavyFile.SetLength(TeamEffort.HeavyImageBytes + 1);
        check("equipo: una imagen de más de 10 MB se puede adjuntar (no se rechaza) pero cuenta como pesada para avisar",
            TeamEffort.Reject(heavyPng) is null && TeamEffort.IsHeavy(heavyPng));
        File.Delete(heavyPng);
        File.Delete(fakePng);
        check("equipo: riesgo → Codex en Frontera; tarea pequeña de pruebas → Ligero",
            risky.Steps.All(s => s.Tier is CodexTier.Frontier or CodexTier.Standard) && risky.Steps.First(s => s.Role == TeamRole.Implement).Tier == CodexTier.Frontier
            && Route("añade pruebas en A.cs: debe cubrir", TeamPreset.CodexOnly, "A.cs").Steps[0].Tier == CodexTier.Light);
        check("equipo: quien revisa no baja de Estándar", Route("añade pruebas en A.cs: debe cubrir", TeamPreset.Saving, "A.cs").Steps.Where(s => s.Role == TeamRole.Review).All(s => s.Tier != CodexTier.Light));

        check("equipo: sesión de Claude según `claude auth status`", TeamProcess.ParseLogin("claude", 0, "{\"loggedIn\": false, \"authMethod\": \"none\"}") == false
            && TeamProcess.ParseLogin("claude", 0, "{\"loggedIn\": true}") == true && TeamProcess.ParseLogin("claude", 1, "basura") is null);
        check("equipo: sesión de Codex según `codex login status`", TeamProcess.ParseLogin("codex", 0, "Logged in using ChatGPT") == true
            && TeamProcess.ParseLogin("codex", 1, "Not logged in") == false && TeamProcess.ParseLogin("codex", 0, "") is null);

        // Veredictos.
        check("equipo: veredicto APPROVED", TeamRun.ParseVerdict("{\"verdict\":\"APPROVED\",\"findings\":[]}") == Verdict.Approved);
        check("equipo: veredicto BLOCKED", TeamRun.ParseVerdict("```json\n{\"verdict\":\"BLOCKED\"}\n```") == Verdict.Blocked);
        check("equipo: veredicto ilegible = WARNING, no aprobado", TeamRun.ParseVerdict("me parece bien") == Verdict.Warning);

        // Orquestación con ejecutor simulado.
        AgentResult Ok(string text) => new(true, text, 10, 5);
        var codexPlan = Route("implementa X en A.cs: debe sumar", TeamPreset.Saving, "A.cs");
        var calls = new List<string>();
        var happy = new TeamRun((s, _, _) => { calls.Add($"{s.Agent}:{s.Role}"); return Task.FromResult(Ok(s.Role == TeamRole.Review ? "{\"verdict\":\"APPROVED\"}" : "hecho")); })
            .RunAsync(codexPlan, "X").Result;
        check("equipo: plan feliz termina en Done con tokens contados", happy.Outcome == TeamOutcome.Done && happy.Tokens == 15 * calls.Count && calls[^1] == "Sonnet:Review");

        int reviews = 0;
        var stubborn = new TeamRun((s, _, _) => Task.FromResult(Ok(s.Role == TeamRole.Review ? (++reviews, "{\"verdict\":\"WARNING\"}").Item2 : "hecho")))
            .RunAsync(codexPlan, "X").Result;
        check("equipo: máx. 3 rondas de revisión y decide el usuario", stubborn.Outcome == TeamOutcome.NeedsUser && reviews == 3);

        var seen = new List<TeamAgent>();
        var escalate = new TeamRun((s, _, _) =>
        {
            seen.Add(s.Agent);
            return Task.FromResult(s.Agent == TeamAgent.Codex ? new AgentResult(false, "", Error: "falló") : Ok(s.Role == TeamRole.Review ? "{\"verdict\":\"APPROVED\"}" : "hecho"));
        }).RunAsync(Route("implementa X en A.cs: debe sumar", TeamPreset.CodexOnly), "X").Result;
        check("equipo: Codex falla 2 veces → escala a Sonnet", escalate.Outcome == TeamOutcome.Done && seen.SequenceEqual([TeamAgent.Codex, TeamAgent.Codex, TeamAgent.Sonnet]));

        var allFail = new TeamRun((_, _, _) => Task.FromResult(new AgentResult(false, "", Error: "x"))).RunAsync(Route("añade X", TeamPreset.ClaudeOnly), "X").Result;
        check("equipo: si hasta Opus falla, el resultado es Failed", allFail.Outcome == TeamOutcome.Failed);
        var thrown = new TeamRun((_, _, _) => throw new IOException("simulated executor crash"))
            .RunAsync(new TeamPlan(TeamRouter.Profile("x"), TeamPreset.CodexOnly, [new TeamStep(TeamAgent.Codex, TeamRole.Implement, true, 1, "")]), "x").Result;
        check("equipo: una excepción del ejecutor se convierte en fallo registrado, no escapa del plan",
            thrown.Outcome == TeamOutcome.Failed && thrown.Records.Count > 0 && thrown.Records.All(r => r.Result.Error == "simulated executor crash"));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        check("equipo: cancelar devuelve Cancelled", new TeamRun((_, _, _) => Task.FromResult(Ok("x"))).RunAsync(codexPlan, "X", cts.Token).Result.Outcome == TeamOutcome.Cancelled);

        var events = new List<(int, StepState)>();
        var prog = new TeamRun((s, _, _) => Task.FromResult(Ok(s.Role == TeamRole.Review ? "{\"verdict\":\"APPROVED\"}" : "hecho")));
        prog.Progress += e => events.Add((e.Index, e.State));
        prog.RunAsync(codexPlan, "X").Wait();
        check("equipo: avisa de cada paso (Running y Done) con su posición",
            events.Count == codexPlan.Steps.Count * 2 && events.Where(e => e.Item2 == StepState.Done).Select(e => e.Item1).OrderBy(i => i).SequenceEqual(Enumerable.Range(0, codexPlan.Steps.Count)));

        // Worktree real en un repo temporal.
        var repo = Path.Combine(Path.GetTempPath(), "its-team-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(repo);
            var git = TeamProcess.Find("git");
            if (git is null) { check("equipo: worktree (git no instalado, omitida)", true); return; }
            string G(params string[] a) => TeamProcess.RunAsync(git, a, "", repo, TimeSpan.FromMinutes(1)).Result.Out;
            G("init", "-q"); File.WriteAllText(Path.Combine(repo, "a.txt"), "a");
            G("add", "."); G("-c", "user.name=t", "-c", "user.email=t@t", "commit", "-qm", "i");
            var trees = Path.Combine(Path.GetTempPath(), "its-trees-" + Guid.NewGuid().ToString("N"));
            check("equipo: reconoce un repo de git y rechaza una carpeta cualquiera", TeamWorktree.IsRepo(repo) && !TeamWorktree.IsRepo(Path.GetTempPath()));
            var (dir, err) = TeamWorktree.CreateAsync(repo, "r1", trees).Result;
            check("equipo: crea worktree y rama propios, fuera del repo", dir is not null && File.Exists(Path.Combine(dir, "a.txt"))
                && G("branch", "--list", "team/r1").Contains("team/r1") && !dir.StartsWith(repo));
            check("equipo: sin cambios no hay commit", TeamWorktree.CommitAsync(dir!, "nada").Result is (false, null));
            File.WriteAllText(Path.Combine(dir!, "b.txt"), "b");
            var committed = TeamWorktree.CommitAsync(dir!, "Equipo: prueba").Result;
            check("equipo: guarda el trabajo en la rama", committed.Committed && committed.Error is null && !File.Exists(Path.Combine(repo, "b.txt")));
            check("equipo: fusionar trae el cambio al repo", TeamWorktree.MergeAsync(repo, "r1").Result is null && File.Exists(Path.Combine(repo, "b.txt")));
            check("equipo: descarta worktree y rama", TeamWorktree.DiscardAsync(repo, dir!, "r1").Result is null && G("branch", "--list", "team/r1").Trim() == "" && !Directory.Exists(dir!));

            // Conflicto: la fusión se deshace y el repo queda como estaba.
            var (dir2, _) = TeamWorktree.CreateAsync(repo, "r2", trees).Result;
            File.WriteAllText(Path.Combine(dir2!, "a.txt"), "rama");
            TeamWorktree.CommitAsync(dir2!, "cambio en la rama").Wait();
            File.WriteAllText(Path.Combine(repo, "a.txt"), "main"); G("add", "."); G("-c", "user.name=t", "-c", "user.email=t@t", "commit", "-qm", "m");
            var conflict = TeamWorktree.MergeAsync(repo, "r2").Result;
            check("equipo: un conflicto de fusión se deshace y se cuenta", conflict is not null && File.ReadAllText(Path.Combine(repo, "a.txt")) == "main"
                && G("status", "--porcelain").Trim() == "");
            TeamWorktree.DiscardAsync(repo, dir2!, "r2").Wait();
            try { Directory.Delete(trees, true); } catch { }
        }
        finally { try { Directory.Delete(repo, true); } catch { } }
    }
}
