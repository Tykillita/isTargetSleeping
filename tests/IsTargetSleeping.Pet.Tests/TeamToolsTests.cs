using System.Text.Json;
using System.Text.Json.Nodes;
using IsTargetSleeping.Pet;

/// Nimbo orquestador: intenciones en español, la herramienta (siempre pregunta), el vigilante de Team y el diff de los permisos.
static class TeamToolsTests
{
    public static void Run(Action<bool, string> check)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "pet-test-team-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = new AgentProfilesFile(Path.Combine(dir, "team", "agents.json"));
        file.Put("revisor", JsonNode.Parse("{\"name\":\"Revisor\",\"engine\":\"claude\",\"model\":\"haiku\",\"instructions\":\"x\",\"canWrite\":false,\"tools\":[\"Read\"],\"maxTokens\":20000,\"timeoutMin\":5}")!.AsObject());
        var agents = new List<(string, string)> { ("revisor", "Revisor") };

        var i1 = TeamIntents.Detect("Pídele a Revisor que mire el diff de hoy.", agents);
        check(i1 is { Tool: "team.enqueue" } && i1.Args.GetProperty("profile").GetString() == "revisor" && i1.Args.GetProperty("task").GetString() == "Mire el diff de hoy", "equipo: «pídele a Revisor que…» lanza su agente");
        var i2 = TeamIntents.Detect("lanza el agente revisor en isTargetSleeping para revisar los cambios", agents);
        check(i2 is not null && i2.Args.GetProperty("root").GetString() == "isTargetSleeping" && i2.Args.GetProperty("profile").GetString() == "revisor", "equipo: «lanza el agente X en <proyecto> para…» toma el proyecto");
        check(TeamIntents.Detect("pídele a Fulano que haga algo", agents) is null, "equipo: un agente que no existe no se inventa");
        check(TeamIntents.Detect("pídele a Revisor que mire", []) is null, "equipo: sin agentes propios no hay intención");
        check(TeamIntents.Detect("hola, ¿qué tal?", agents) is null, "equipo: una charla normal no lanza nada");

        foreach (var phrase in new[] { "Lanza una tarea usando en automático el widget claude codex team: arregla el login", "lanza con el equipo en isTargetSleeping para arreglar el login", "pídele al equipo que arregle el login", "usa el equipo de Claude y Codex: arregla el login" })
        {
            var ia = TeamIntents.Detect(phrase, []);
            check(ia is { Tool: "team.run" } && ia.Args.GetProperty("task").GetString()!.Contains("rregl"), "equipo auto: «" + phrase[..Math.Min(40, phrase.Length)] + "…» lanza el reparto automático");
        }
        check(TeamIntents.Detect("lanza con el equipo en isTargetSleeping para arreglar el login", [])!.Args.GetProperty("root").GetString() == "isTargetSleeping", "equipo auto: toma el proyecto de la frase");
        check(TeamIntents.Detect("el equipo de fútbol ganó ayer", []) is null && TeamIntents.Detect("lanza una moneda", []) is null, "equipo auto: charla normal no lo dispara");
        foreach (var code in new[] { "Arregla el error al guardar los ajustes en el proyecto isTargetSleeping", "Implementa una función que ordene la lista de tareas por hora", "Refactoriza la clase que maneja el pomodoro para que tenga pruebas" })
            check(TeamIntents.Detect(code, []) is { Tool: "team.run" }, "router: «" + code[..28] + "…» se propone al equipo");
        check(TeamIntents.Detect("Arregla el error al guardar los ajustes en el proyecto isTargetSleeping", [])!.Args.GetProperty("root").GetString() == "isTargetSleeping", "router: toma el proyecto si se nombra");
        foreach (var chat in new[] { "¿Cómo arreglo un error de importación en Python?", "Cuéntame algo curioso sobre el código fuente de Linux", "arregla el botón", "Explícame qué es una función recursiva", "Dame ánimo para terminar el proyecto de hoy" })
            check(TeamIntents.Detect(chat, []) is null, "router: «" + chat[..Math.Min(26, chat.Length)] + "…» sigue siendo charla");
        var runTool = new TeamRunTool(new FakeBridge());
        var runArgs = JsonDocument.Parse("{\"task\":\"Arregla el login\",\"root\":\"isTargetSleeping\"}").RootElement;
        check(runTool.Validate(runArgs) is null && runTool.Describe(runArgs).Contains("Claude y Codex") && runTool.Describe(runArgs).Contains("plan antes de escribir") && runTool.Spec.Risk == ToolRisk.Pc, "equipo auto: la confirmación explica el reparto y siempre pregunta");
        var full = JsonDocument.Parse("{\"task\":\"Arregla el login\",\"preset\":\"Quality\",\"effort\":\"High\",\"review_plan\":false}").RootElement;
        check(runTool.Validate(full) is null && runTool.Describe(full).Contains("modo Calidad") && runTool.Describe(full).Contains("esfuerzo alto") && runTool.Describe(full).Contains("SIN revisar el plan"), "equipo auto: la confirmación nombra el modo, el esfuerzo y si no se revisa el plan");
        string upDir = Path.Combine(dir, "uploads", new string('a', 32)); Directory.CreateDirectory(upDir); File.WriteAllText(Path.Combine(upDir, "captura.png"), "x");
        var withAtt = JsonDocument.Parse("{\"task\":\"Mira esto\",\"attachments\":[\"" + new string('a', 32) + "\"]}").RootElement;
        var attTool = new TeamRunTool(new FakeBridge(), Path.Combine(dir, "uploads"));
        check(attTool.Validate(withAtt) is null && attTool.Describe(withAtt).Contains("1 adjunto: captura.png"), "adjuntos: la confirmación nombra los archivos");
        check(attTool.Validate(JsonDocument.Parse("{\"task\":\"x\",\"attachments\":[\"../x\"]}").RootElement) is not null && attTool.Validate(JsonDocument.Parse("{\"task\":\"x\",\"attachments\":[\"" + new string('a', 32) + "\",\"" + new string('b', 32) + "\",\"" + new string('c', 32) + "\",\"" + new string('d', 32) + "\",\"" + new string('e', 32) + "\",\"" + new string('f', 32) + "\"]}").RootElement) is not null, "adjuntos: ids inválidos o más de 5 se rechazan");
        check(runTool.Validate(JsonDocument.Parse("{\"task\":\"x\",\"preset\":\"Turbo\"}").RootElement) is not null && runTool.Validate(JsonDocument.Parse("{\"task\":\"x\",\"effort\":\"Max2\"}").RootElement) is not null, "equipo auto: modo o esfuerzo desconocidos se rechazan");

        // adjuntos del chat normal
        string cu = Path.Combine(dir, "chatup"); Directory.CreateDirectory(cu);
        string Mk(string name, byte[] data) { string id = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(); Directory.CreateDirectory(Path.Combine(cu, id)); File.WriteAllBytes(Path.Combine(cu, id, name), data); return id; }
        string idTxt = Mk("nota.txt", System.Text.Encoding.UTF8.GetBytes("El codigo es PINA-42")), idPng = Mk("foto.png", [137, 80, 78, 71, 1]), idPdf = Mk("doc.pdf", [37, 80, 68, 70]), idBin = Mk("x.log", [65, 0, 66]);
        var (ctext, cimgs) = ChatAttachments.Read(cu, [idTxt, idPng, idPdf]);
        var (btext, _) = ChatAttachments.Read(cu, [idBin, "../../x"]);
        check(ctext.Contains("[Archivo adjunto: nota.txt]") && ctext.Contains("PINA-42"), "chat adjuntos: el texto del archivo se pega en el mensaje");
        check(cimgs.Count == 1 && ctext.Contains("[Imagen adjunta: foto.png]"), "chat adjuntos: la imagen viaja aparte para el modelo");
        check(ctext.Contains("PDF adjunto") && btext.Contains("binario"), "chat adjuntos: un PDF o un binario se explican, no se leen");
        check(!Directory.Exists(Path.Combine(cu, idTxt)), "chat adjuntos: lo leído se borra");
        var pm = PetProtocol.Parse(System.Text.Encoding.UTF8.GetBytes("{\"v\":1,\"type\":\"chat\",\"id\":\"" + new string('a', 32) + "\",\"text\":\"hola\",\"attachments\":[\"" + new string('b', 32) + "\"]}"));
        check(pm.Message is ChatMsg { Attachments.Count: 1 }, "chat adjuntos: el mensaje del celular lleva los ids");
        check(PetProtocol.Parse(System.Text.Encoding.UTF8.GetBytes("{\"v\":1,\"type\":\"chat\",\"id\":\"" + new string('a', 32) + "\",\"text\":\"hola\",\"attachments\":[\"../x\"]}")).Error is not null, "chat adjuntos: un id raro se rechaza");

        var fake = new FakeBridge();
        var tool = new TeamEnqueueTool(fake, file);
        var ok = JsonDocument.Parse("{\"task\":\"Mira el diff\",\"profile\":\"revisor\",\"root\":\"isTargetSleeping\"}").RootElement;
        check(tool.Validate(ok) is null && tool.Describe(ok).Contains("Revisor") && tool.Describe(ok).Contains("Solo lee") && tool.Describe(ok).Contains("20000"), "equipo: la confirmación enseña agente, proyecto, tope y que solo lee");
        check(tool.Validate(JsonDocument.Parse("{\"task\":\"x\",\"profile\":\"Mal Id\"}").RootElement) is not null && tool.Validate(JsonDocument.Parse("{\"profile\":\"revisor\"}").RootElement) is not null, "equipo: sin tarea o con un id raro se rechaza");
        check(tool.Spec.Risk == ToolRisk.Pc, "equipo: lanzar un agente es una acción de la PC");

        var policy = new PolicyStore(Path.Combine(dir, "policy.json"));
        policy.Set("team.enqueue", ToolMode.Auto);
        check(policy.Mode(tool.Spec) == ToolMode.Ask, "equipo: aunque se ponga en Auto, lanzar un agente siempre pregunta");
        check(policy.TeamDailyTokens == 300_000 && policy.TeamMaxQueued == 3, "equipo: presupuesto por defecto");
        File.WriteAllText(Path.Combine(dir, "policy.json"), "{\"team_daily_tokens\":50000,\"team_max_queued\":1,\"tools\":{}}");
        check(policy.TeamDailyTokens == 50_000 && policy.TeamMaxQueued == 1, "equipo: el presupuesto se lee de policy.json");
        policy.SetStrict(false);
        check(File.ReadAllText(Path.Combine(dir, "policy.json")).Contains("50000"), "equipo: guardar la política conserva el presupuesto");

        // vigilante: el primer vistazo solo toma nota; después avisa del plan y del trabajo terminado
        string ts = Path.Combine(dir, "team-state.json");
        File.WriteAllText(ts, "{\"ts\":1,\"history\":[{\"id\":\"a\",\"title\":\"Viejo\",\"outcome\":\"Done\"}]}");
        var watch = new TeamWatcher(ts);
        check(watch.Check().Count == 0, "equipo: el primer vistazo no avisa de lo viejo");
        File.WriteAllText(ts, "{\"ts\":2,\"pending\":{\"text\":\"plan\"},\"history\":[{\"id\":\"a\",\"title\":\"Viejo\",\"outcome\":\"Done\"}]}");
        check(watch.Check() is { Count: 1 } m1 && m1[0].Contains("plan") && watch.Check().Count == 0, "equipo: un plan nuevo avisa una sola vez");
        File.WriteAllText(ts, "{\"ts\":3,\"history\":[{\"id\":\"b\",\"title\":\"Revisar\",\"outcome\":\"Done\"},{\"id\":\"a\",\"title\":\"Viejo\",\"outcome\":\"Done\"}]}");
        check(watch.Check() is { Count: 1 } m2 && m2[0].Contains("terminó") && m2[0].Contains("Revisar"), "equipo: avisa cuando termina un trabajo");
        File.WriteAllText(ts, "{\"ts\":4,\"history\":[{\"id\":\"c\",\"title\":\"Otro\",\"outcome\":\"NeedsUser\"}]}");
        check(watch.Check() is { Count: 1 } m3 && m3[0].Contains("decisión"), "equipo: avisa si se detiene y necesita tu decisión");

        File.WriteAllText(ts, "{\"ts\":" + DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ",\"current\":{\"title\":\"Revisar\"},\"queued\":[\"a\",\"b\"]}");
        check(TeamStateBridge.Summary(ts, DateTimeOffset.UtcNow) is { } s && s.Contains("Revisar") && s.Contains("2 en cola"), "equipo: Nimbo puede contar qué hace el equipo");

        // diff en los permisos: viaja al celular y se quita si no cabe en la placa
        var item = new ApprovalItem("a1b2c3d4e5f6", "claude", "permission", "Edit", "p/q", "src/a.cs", null, 0, [], null) { Diff = "-viejo\n+nuevo" };
        check(System.Text.Encoding.UTF8.GetString(PetProtocol.Approval(item)).Contains("\"diff\""), "permisos: el diff llega al celular");
        var big = item with { Diff = new string('x', 1600), Detail = new string('d', 240), Plan = new string('p', 800) };
        var bigBytes = PetProtocol.Approval(big with { Questions = [new AskItem(new string('q', 200), "", false, ["a", "b"]), new AskItem(new string('q', 200), "", false, ["a", "b"]), new AskItem(new string('q', 200), "", false, ["a", "b"])] });
        check(bigBytes.Length <= 4096, "permisos: lo que pasa de 4 KB se manda sin diff para que la placa lo acepte");

        try { Directory.Delete(dir, true); } catch { }
    }
}
