using System.Text;
using System.Text.Json.Nodes;
using IsTargetSleeping;

internal static class AgentHookTests
{
    private static string Payload(string evt, string tool = "Bash", string input = "{\"command\":\"npm test\"}") =>
        $"{{\"hook_event_name\":\"{evt}\",\"session_id\":\"s1\",\"cwd\":\"C:\\\\proj\",\"tool_name\":\"{tool}\",\"tool_input\":{input}}}";

    private static string Line(string agent, string payload) => new JsonObject { ["agent"] = agent, ["payload"] = JsonNode.Parse(payload) }.ToJsonString();

    /// Corre el cliente contra el servidor y devuelve lo que habría impreso.
    private static string Client(string pipe, string payload, TimeSpan? wait = null)
    {
        var output = new MemoryStream();
        AgentHooks.RunClient("claude", new MemoryStream(Encoding.UTF8.GetBytes(payload)), output, pipe, wait);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    public static void Run(Action<string, bool> check)
    {
        var dir = Path.Combine(Path.GetTempPath(), "its-hook-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // — Parse
            var ask = AgentHooks.Parse(Line("claude", Payload("PermissionRequest")));
            check("un permiso de Bash se entiende con su comando", ask is { NeedsAnswer: true, Tool: "Bash", Detail: "npm test", Session: "s1" });
            check("un agente con nombre raro se rechaza", AgentHooks.Parse(Line("Bad Agent!", Payload("Stop"))) is null);
            check("un evento desconocido se rechaza", AgentHooks.Parse(Line("claude", Payload("PreCompact"))) is null);
            check("basura no se entiende ni rompe", AgentHooks.Parse("{no json") is null);
            var big = AgentHooks.Parse(Line("claude", Payload("PermissionRequest", "Write", $"{{\"file_path\":\"{new string('a', 5000)}\"}}")));
            check("el detalle largo se recorta", big?.Detail is { Length: <= AgentHooks.DetailMax + 1 });

            // — Reglas «Siempre»
            var rulesFile = Path.Combine(dir, "rules.json");
            var rules = new HookRules(rulesFile);
            rules.Add(ask!);
            check("«Siempre» coincide con el mismo comando", rules.Matches(ask!));
            check("«Siempre» no coincide con otro comando", !rules.Matches(AgentHooks.Parse(Line("claude", Payload("PermissionRequest", "Bash", "{\"command\":\"npm publish\"}")))!));
            check("«Siempre» se guarda y se recupera", new HookRules(rulesFile).Matches(ask!));
            var other = AgentHooks.Parse(Line("codex", Payload("PermissionRequest")))!;
            rules.Add(other);
            check("«Siempre» solo vale para Claude Code", !rules.Matches(other));

            // — Sonidos
            foreach (var sound in Enum.GetValues<AgentSound>())
            {
                var wav = AgentSounds.Wave(sound);
                check($"el sonido {sound} es un WAV válido y no está en silencio",
                    wav.Length > 44 && Encoding.ASCII.GetString(wav, 0, 4) == "RIFF" && BitConverter.ToInt32(wav, 40) == wav.Length - 44
                    && Enumerable.Range(0, (wav.Length - 44) / 2).Any(i => Math.Abs(BitConverter.ToInt16(wav, 44 + i * 2)) > 1000));
            }

            // — Pipe de punta a punta
            var pipe = "its-test-" + Guid.NewGuid().ToString("N");
            check("sin la app abierta el cliente no imprime nada", Client(pipe, Payload("PermissionRequest")) == "");
            using var server = new HookServer(new HookRules(Path.Combine(dir, "r2.json")), pipe, TimeSpan.FromSeconds(2));
            var seen = new List<HookRequest>();
            HookVerdict? next = HookVerdict.Allow;
            server.Requested += r => { lock (seen) seen.Add(r); if (r.NeedsAnswer && next is { } v) r.Decide(v); };
            server.Start();
            var allow = Client(pipe, Payload("PermissionRequest"));
            check("permitir imprime la decisión allow", JsonNode.Parse(allow)?["hookSpecificOutput"]?["decision"]?["behavior"]?.GetValue<string>() == "allow");
            next = HookVerdict.Deny;
            var deny = Client(pipe, Payload("PermissionRequest"));
            check("denegar imprime deny con motivo", JsonNode.Parse(deny)?["hookSpecificOutput"]?["decision"]?["behavior"]?.GetValue<string>() == "deny");
            next = null;   // nadie responde: vence y se pliega a la terminal
            check("sin respuesta a tiempo no imprime nada (decide la terminal)", Client(pipe, Payload("PermissionRequest"), TimeSpan.FromSeconds(10)) == "");
            Client(pipe, Payload("Stop"));
            Thread.Sleep(200);
            check("un evento sin respuesta llega y no bloquea", seen.Any(r => r.Event == "Stop"));

            // — settings.json
            var settings = Path.Combine(dir, "settings.json");
            const string exe = @"C:\Users\x\AppData\Local\Programs\isTargetSleeping\isTargetSleeping.exe";
            File.WriteAllText(settings, """
                { "model": "opus", "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "echo mio" } ] } ] } }
                """);
            check("antes de instalar no figura", !AgentHooksInstaller.IsInstalled(settings));
            AgentHooksInstaller.Install(settings, exe);
            AgentHooksInstaller.Install(settings, exe);
            var after = JsonNode.Parse(File.ReadAllText(settings))!;
            check("instalar es idempotente: un hook nuestro por evento", AgentHooks.Events.All(e => after["hooks"]![e]!.AsArray().Count(g => g!["hooks"]![0]!["args"]?[0]?.GetValue<string>() == "--hook") == 1));
            check("instalar conserva los hooks y ajustes ajenos", after["model"]!.GetValue<string>() == "opus" && after["hooks"]!["Stop"]!.AsArray().Count == 2);
            var entry = after["hooks"]!["Stop"]![1]!["hooks"]![0]!;
            check("el hook se registra en forma exec: ejecutable con barras normales y args", entry["command"]!.GetValue<string>() == "C:/Users/x/AppData/Local/Programs/isTargetSleeping/isTargetSleeping.exe"
                && entry["args"]!.AsArray().Select(a => a!.GetValue<string>()).SequenceEqual(["--hook", "claude"]) && entry["timeout"]!.GetValue<int>() == 300);
            File.WriteAllText(settings, """{ "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "\"C:/x/isTargetSleeping.exe\" --hook claude" } ] } ] } }""");
            bool legacySeen = AgentHooksInstaller.IsInstalled(settings);
            AgentHooksInstaller.Install(settings, exe);
            check("se reconoce y se reemplaza la forma con shell de una versión anterior", legacySeen && JsonNode.Parse(File.ReadAllText(settings))!["hooks"]!["Stop"]!.AsArray().Count == 1);
            AgentHooksInstaller.Uninstall(settings);
            File.WriteAllText(settings, """
                { "model": "opus", "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "echo mio" } ] } ] } }
                """);
            AgentHooksInstaller.Install(settings, exe);
            check("queda una copia de seguridad", Directory.GetFiles(dir, "settings.json.its-backup-*").Length >= 1);
            check("después de instalar figura", AgentHooksInstaller.IsInstalled(settings));
            AgentHooksInstaller.Uninstall(settings);
            var restored = JsonNode.Parse(File.ReadAllText(settings))!;
            check("desinstalar deja solo lo ajeno", !AgentHooksInstaller.IsInstalled(settings) && restored["hooks"]!.AsObject().Count == 1 && restored["hooks"]!["Stop"]!.AsArray().Count == 1 && restored["model"] is not null);
            File.WriteAllText(settings, "[1,2]");
            bool refused = false;
            try { AgentHooksInstaller.Install(settings, exe); } catch (InvalidDataException) { refused = true; }
            check("un settings.json que no es un objeto no se reescribe", refused && File.ReadAllText(settings) == "[1,2]");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
