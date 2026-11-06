using IsTargetSleeping;

internal static class TeamAgentsTests
{
    static AgentProfile P(string id = "revisor", string engine = "claude", string model = "haiku", bool write = false, string[]? tools = null, long max = 20_000, int min = 5) =>
        new(id, "Revisor", "d", engine, model, "Eres un revisor.", write, tools ?? ["Read", "Grep"], null, max, min);

    public static void Run(Action<string, bool> check)
    {
        check("agentes: un perfil correcto es válido", AgentProfiles.Validate(P(), []) is null);
        check("agentes: sin tope de tokens se rechaza", AgentProfiles.Validate(P(max: 0), []) is not null);
        check("agentes: herramienta fuera de la lista (Bash) se rechaza", AgentProfiles.Validate(P(tools: ["Read", "Bash"]), []) is not null);
        check("agentes: id repetido se rechaza", AgentProfiles.Validate(P(), [P()]) is not null);
        check("agentes: motor desconocido se rechaza", AgentProfiles.Validate(P(engine: "gpt"), []) is not null);
        check("agentes: Ollama no puede escribir", AgentProfiles.Validate(P(engine: "ollama", model: "gemma4:latest", write: true, tools: []), []) is not null);
        check("agentes: Edit sin poder escribir se rechaza", AgentProfiles.Validate(P(tools: ["Read", "Edit"]), []) is not null);
        check("agentes: modelo de Claude desconocido se rechaza", AgentProfiles.Validate(P(model: "gpt-9"), []) is not null);
        check("agentes: Ollama con etiqueta válida", AgentProfiles.Validate(P(engine: "ollama", model: "gemma4:latest", tools: []), []) is null);

        var json = AgentProfiles.ToJson([P(), P("local", "ollama", "gemma4:latest", tools: [])]);
        var back = AgentProfiles.Parse(json, out var errs);
        check("agentes: ida y vuelta por JSON", back.Count == 2 && errs.Count == 0 && back[1].Model == "gemma4:latest");
        var mixed = AgentProfiles.Parse("[{\"id\":\"ok\",\"name\":\"a\",\"engine\":\"claude\",\"model\":\"haiku\",\"tools\":[\"Read\"],\"maxTokens\":5000,\"timeoutMin\":3},{\"id\":\"mal\",\"name\":\"b\",\"engine\":\"claude\",\"model\":\"haiku\",\"tools\":[\"Bash\"],\"maxTokens\":5000,\"timeoutMin\":3}]", out var e2);
        check("agentes: el inválido se descarta y el válido queda", mixed.Count == 1 && e2.Count == 1);
        check("agentes: JSON roto no revienta", AgentProfiles.Parse("{no", out var e3).Count == 0 && e3.Count == 1);

        var args = ClaudeRunner.WithProfile(ClaudeRunner.Args(TeamAgent.Haiku, false, null, null, answer: true, readProject: true), P(tools: ["Read", "git diff"]));
        int i = args.ToList().IndexOf("--allowedTools");
        check("agentes: Claude usa solo las herramientas del perfil", args[i + 1] == "Read,Bash(git diff:*)");
        check("agentes: Claude recibe las instrucciones como sistema", args.ToList().IndexOf("--append-system-prompt") is var j && j > 0 && args[j + 1] == "Eres un revisor.");

        var job = AgentProfiles.Job(P(), "revisa esto", @"C:\x");
        check("agentes: un perfil de solo lectura es una respuesta sin rama que lee el proyecto", job.Steps[0].Role == TeamRole.Answer && job.ReadsRoot && job.Steps[0].Profile is not null && job.MaxTokens == 20_000);
        var wjob = AgentProfiles.Job(P("w", write: true, tools: ["Read", "Edit"]), "haz", @"C:\x");
        check("agentes: un perfil que escribe implementa en rama", wjob.Steps[0].Role == TeamRole.Implement && wjob.Steps[0].CanWrite && !wjob.ReadsRoot);

        var ok = OllamaRunner.Parse(true, "{\"message\":{\"content\":\" hola \"},\"prompt_eval_count\":12,\"eval_count\":3}");
        check("agentes: Ollama lee texto y tokens", ok.Ok && ok.Message == "hola" && ok.InputTokens == 12 && ok.OutputTokens == 3);
        check("agentes: error de Ollama es permanente", OllamaRunner.Parse(false, "{\"error\":\"model not found\"}") is { Ok: false, Permanent: true });
    }
}
