using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using IsTargetSleeping.Pet;

// isTargetSleeping-pet: complemento "Mascota conectada" (programa aparte, no va dentro del .exe de la app).
//   serve      arranca el servidor local HTTPS para la mascota de la placa ESP32
//   provision  genera la CA, el certificado del servidor y un token de dispositivo (archivo provision.json para grabar por USB)
//   revoke     revoca los tokens de una etiqueta
return await Cli.Run(args);

static class Cli
{
    const string Usage = """
        isTargetSleeping-pet <comando> [opciones]
          provision --host <ip|nombre> [--host ...] [--port 8443] [--label esp32] [--data <carpeta>]
          serve [--port 8443] [--web-port 8780] [--provider ollama|ollama-cloud|codex|anthropic|grok|gemini] [--model <modelo>] [--ollama http://127.0.0.1:11434/] [--data <carpeta>]
          pair [--url https://mi-pc.tailnet.ts.net] [--data <carpeta>]   (QR de acceso para el telefono)
          revoke --label <etiqueta> [--data <carpeta>]
        Los datos viven en %LOCALAPPDATA%\isTargetSleeping\pet (o --data).
        """;

    public static async Task<int> Run(string[] args)
    {
        if (args.Length == 0) { Console.WriteLine(Usage); return 2; }
        var opts = Parse(args[1..]);
        string data = opts.TryGetValue("data", out var d) ? d[^1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "isTargetSleeping", "pet");
        Directory.CreateDirectory(data);
        try
        {
            return args[0] switch
            {
                "provision" => Provision(data, opts),
                "serve" => await Serve(data, opts),
                "pair" => Pair(data, opts),
                "revoke" => Revoke(data, opts),
                "push-test" => await PushTest(data),
                _ => Fail(Usage),
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or CryptographicException)
        {
            return Fail("Error: " + e.Message);
        }
    }

    static int Fail(string msg) { Console.Error.WriteLine(msg); return 2; }

    static Dictionary<string, List<string>> Parse(string[] a)
    {
        var d = new Dictionary<string, List<string>>();
        for (int i = 0; i + 1 < a.Length; i += 2)
            if (a[i].StartsWith("--")) { var k = a[i][2..]; if (!d.TryGetValue(k, out var l)) d[k] = l = []; l.Add(a[i + 1]); }
        return d;
    }

    static int Provision(string data, Dictionary<string, List<string>> o)
    {
        if (!o.TryGetValue("host", out var hosts)) return Fail("Falta --host <ip|nombre> (la direccion con la que la placa llegara a esta PC).");
        int port = o.TryGetValue("port", out var p) ? int.Parse(p[^1]) : 8443;
        string label = o.TryGetValue("label", out var l) ? l[^1] : "esp32";
        var now = DateTimeOffset.UtcNow;
        using var ca = Pki.EnsureCa(data, now);
        using var server = Pki.IssueServer(ca, [.. hosts, "127.0.0.1", "localhost"], now);
        File.WriteAllBytes(Path.Combine(data, "server.pfx"), server.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx));
        string token = new TokenStore(Path.Combine(data, "tokens.json")).Issue(label);
        string file = Path.Combine(data, "provision.json");
        File.WriteAllText(file, JsonSerializer.Serialize(new { host = hosts[0], port, token, ca_pem = Pki.ToPem(ca) }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Listo. Archivo para grabar en la placa por USB: {file}");
        Console.WriteLine("Contiene el token del dispositivo: tratalo como una contrasena y borralo despues de grabarlo.");
        Console.WriteLine($"Servidor: https://{hosts[0]}:{port}  (arranca con: isTargetSleeping-pet serve --port {port})");
        return 0;
    }

    /// Codigo de un solo uso (5 min) + QR. El QR lleva la direccion y el codigo en el fragmento (#c=), que el navegador nunca envia al servidor.
    /// Sin --url averigua la direccion de Tailscale, arranca el servidor si no corre y publica la web en el tailnet: asi la app solo llama a `pair`.
    static int Pair(string data, Dictionary<string, List<string>> o)
    {
        int webPort = o.TryGetValue("web-port", out var wp) ? int.Parse(wp[^1]) : 8780;
        string file = Path.Combine(data, "pair.html");
        string? url = o.TryGetValue("url", out var u) ? u[^1].TrimEnd('/') : null;
        string? openUrl = null;
        try
        {
            if (url is null)
            {
                url = Tailscale.Url() ?? throw new InvalidOperationException("Tailscale no esta conectado en esta PC. Abre Tailscale, inicia sesion y vuelve a intentarlo.");
                if (!Tailscale.Serve(webPort)) throw new InvalidOperationException("No pude publicar la web en tu red de Tailscale (tailscale serve).");
            }
            EnsureServer(data, webPort);
            var (code, nonce) = new PairingStore(Path.Combine(data, "pair.json")).NewPairing();
            string link = $"{url}/#c={code}";
            using var gen = new QRCoder.QRCodeGenerator();
            using var qr = gen.CreateQrCode(link, QRCoder.QRCodeGenerator.ECCLevel.M);
            File.WriteAllText(file, PairPage.Html(url, code, link, DateTimeOffset.UtcNow.AddMinutes(5), $"/pairing/{nonce}/status"));
            openUrl = $"http://127.0.0.1:{webPort}/pairing/{nonce}";
            Console.WriteLine($"Codigo de emparejamiento: {code}  (vale 5 minutos y un solo uso)");
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine(new QRCoder.AsciiQRCode(qr).GetGraphic(1, "██", "  ", false));
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            File.WriteAllText(file, PairPage.Error(e.Message));
            Console.Error.WriteLine(e.Message);
            Open(file);
            return 1;
        }
        Open(openUrl ?? file);
        return 0;
    }

    static void Page(string file, string body) =>
        File.WriteAllText(file, "<!doctype html><meta charset=utf-8><meta name=color-scheme content='dark light'><title>Conectar celular</title><body style=\"font:18px system-ui;text-align:center;margin:40px\">" + body);

    static void Open(string file)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { Console.WriteLine($"Abre {file}"); }
    }

    /// Si nadie escucha la web, arranca `serve` desacoplado (sigue vivo al cerrar quien lo lanzo).
    static void EnsureServer(string data, int webPort)
    {
        if (Listening(webPort)) return;
        var psi = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { CreateNoWindow = true, UseShellExecute = false };
        foreach (var a in new[] { "serve", "--data", data, "--web-port", webPort.ToString() }) psi.ArgumentList.Add(a);
        System.Diagnostics.Process.Start(psi);
        for (int i = 0; i < 50 && !Listening(webPort); i++) Thread.Sleep(200);
        if (!Listening(webPort)) throw new InvalidOperationException("El servidor de la mascota no arranco.");
    }

    static bool Listening(int port)
    {
        try { using var c = new System.Net.Sockets.TcpClient(); return c.ConnectAsync(IPAddress.Loopback, port).Wait(300); } catch (Exception) { return false; }
    }

    static async Task<int> PushTest(string data)
    {
        var push = new PushService(Path.Combine(data, "push.json"));
        Console.WriteLine($"Suscripciones: {push.Count}");
        foreach (var l in await push.TestAsync()) Console.WriteLine(l);
        return 0;
    }

    static int Revoke(string data, Dictionary<string, List<string>> o)
    {
        if (!o.TryGetValue("label", out var l)) return Fail("Falta --label.");
        int n = new TokenStore(Path.Combine(data, "tokens.json")).Revoke(l[^1]);
        Console.WriteLine($"{n} token(s) revocado(s).");
        return n > 0 ? 0 : 1;
    }

    static async Task<int> Serve(string data, Dictionary<string, List<string>> o)
    {
        // Sin certificado/token de la placa solo se abre la web movil (el telefono no necesita ESP32).
        string pfx = Path.Combine(data, "server.pfx");
        var tokens = new TokenStore(Path.Combine(data, "tokens.json"));
        using var cert = File.Exists(pfx) && tokens.Count > 0 ? System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(pfx, null) : null;

        using var store = new SqlitePetStore(Path.Combine(data, "pet.db"));
        var service = new PetService(store);
        string provider = (o.TryGetValue("provider", out var pv) ? pv[^1] : Environment.GetEnvironmentVariable("NIMBO_PROVIDER") ?? "ollama").Trim().ToLowerInvariant();
        string? model = o.TryGetValue("model", out var m) ? m[^1] : Environment.GetEnvironmentVariable("NIMBO_MODEL");
        string? key = provider switch
        {
            "ollama-cloud" => Environment.GetEnvironmentVariable("OLLAMA_API_KEY"),
            "grok" => Environment.GetEnvironmentVariable("XAI_API_KEY"),
            "gemini" => Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY"),
            "anthropic" => Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"),
            _ => null,
        };
        string auth = provider switch { "ollama" => "local", "codex" => "oauth", "gemini" when string.IsNullOrWhiteSpace(key) => "oauth", _ => "api_key" };
        var fallback = NimboProviderManager.Validate(new NimboProviderSettings(provider, model, auth, key, Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT")));
        var providerStore = new NimboProviderSettingsStore(data);
        var ollamaUri = o.TryGetValue("ollama", out var u) ? new Uri(u[^1]) : new Uri("http://127.0.0.1:11434/");
        string PersonaText() => Persona.Build(service.Snapshot, DateTime.Now, store.Memories().Select(x => x.Text).ToList(), UsageBridge.Summary(Path.Combine(data, "usage.json"), DateTimeOffset.UtcNow), string.Join(". ", new[] { AppStateBridge.Summary(Path.Combine(data, "app-state.json"), DateTimeOffset.UtcNow), TeamStateBridge.Summary(Path.Combine(data, "team-state.json"), DateTimeOffset.UtcNow) }.Where(x => x is not null)) is { Length: > 0 } extra ? extra : null);
        using var http = new HttpClient();
        IChatBackend BuildBackend(NimboProviderSettings s) => s.Provider switch
        {
            "ollama" => new OllamaBackend(http, new OllamaOptions { Model = s.Model ?? "gemma4:latest", BaseUri = ollamaUri }, PersonaText, data),
            "ollama-cloud" when s.Auth == "ollama_oauth" => new OllamaBackend(http, new OllamaOptions { Model = s.Model!, BaseUri = ollamaUri }, PersonaText, data, reserveLocalModel: false),
            "ollama-cloud" when !string.IsNullOrWhiteSpace(s.ApiKey) => new OpenAiChatBackend(http, new OpenAiChatOptions(new Uri("https://ollama.com/v1/"), s.Model!, s.ApiKey), PersonaText),
            "grok" when !string.IsNullOrWhiteSpace(s.ApiKey) => new OpenAiChatBackend(http, new OpenAiChatOptions(new Uri("https://api.x.ai/v1/"), s.Model!, s.ApiKey, JsonSchema: true), PersonaText),
            "gemini" when s.Auth == "api_key" && !string.IsNullOrWhiteSpace(s.ApiKey) => new GeminiChatBackend(http, s.Model!, s.ApiKey, null, PersonaText),
            "gemini" when s.Auth == "oauth" && !string.IsNullOrWhiteSpace(s.Project) => new GeminiChatBackend(http, s.Model!, null, s.Project, PersonaText),
            "codex" => new CliChatBackend(s.Model, PersonaText),
            "anthropic" when !string.IsNullOrWhiteSpace(s.ApiKey) => new AnthropicChatBackend(http, s.Model!, s.ApiKey, PersonaText),
            "ollama-cloud" when s.Auth == "api_key" => new UnconfiguredChatBackend("Configura la clave API de Ollama Cloud en Ajustes de Nimbo."),
            "ollama-cloud" => new UnconfiguredChatBackend("Inicia sesión en Ollama en esta PC y asegúrate de que su servidor local esté activo."),
            "grok" => new UnconfiguredChatBackend("Configura la clave API de xAI en Ajustes de Nimbo."),
            "gemini" when s.Auth == "api_key" => new UnconfiguredChatBackend("Configura la clave API de Gemini en Ajustes de Nimbo."),
            "gemini" => new UnconfiguredChatBackend("Configura el proyecto y el inicio de sesión OAuth de Google en Ajustes de Nimbo."),
            "anthropic" => new UnconfiguredChatBackend("Configura la clave API de Claude en Ajustes de Nimbo."),
            _ => new UnconfiguredChatBackend("El proveedor de Nimbo no es válido."),
        };
        var providers = new NimboProviderManager(providerStore, fallback, BuildBackend);
        int port = o.TryGetValue("port", out var p) ? int.Parse(p[^1]) : 8443;
        var agents = new AgentBridge(Path.Combine(data, "agent-state.json"));
        var policy = new PolicyStore(Path.Combine(data, "policy.json"));
        var push = new PushService(Path.Combine(data, "push.json"));
        var approvalFeed = new ApprovalFeed(Path.Combine(data, "approvals.json"));
        var app = PetHost.Build(new PetHostOptions { Service = service, Tokens = tokens, Backend = providers.Backend, Providers = providers, Store = store, Agents = agents, ServerCert = cert, Port = port, WebPort = o.TryGetValue("web-port", out var wp) ? int.Parse(wp[^1]) : 8780, Pairing = new PairingStore(Path.Combine(data, "pair.json")), Bridge = new FileAppBridge(data), Approvals = approvalFeed, Push = push, SessionsPath = Path.Combine(data, "sessions.json"), TeamPath = Path.Combine(data, "team-state.json"), PairPagePath = Path.Combine(data, "pair.html"), UploadsPath = Path.Combine(data, "uploads"), Profiles = new AgentProfilesFile(Path.Combine(Path.GetDirectoryName(data.TrimEnd((char)92, (char)47))!, "team", "agents.json")), Policy = policy, UsageProbe = () => UsageBridge.Summary(Path.Combine(data, "usage.json"), DateTimeOffset.UtcNow) }, out var hub, out _, out _);
        using var agentPoll = new Timer(_ => { agents.Poll(); approvalFeed.Poll(); }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        var bridge = new FileAppBridge(data);
        var scheduler = new ReminderScheduler(store, hub, bridge, push: push);
        var usageWatch = new UsageWatcher(Path.Combine(data, "usage.json"));
        var teamWatch = new TeamWatcher(Path.Combine(data, "team-state.json"));
        int busy = 0;
        using var every15 = new Timer(async _ =>
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) return;   // un tick a la vez
            try
            {
                await scheduler.TickAsync();
                if (!Notices.InQuiet(policy.Quiet, DateTime.Now.TimeOfDay))
                    foreach (var msg in usageWatch.Check(DateTimeOffset.UtcNow)) { hub.Publish(PetProtocol.Notice(msg, "attention")); _ = push.SendAsync("Nimbo", msg, "/#agents", "usage"); }
                foreach (var msg in teamWatch.Check().Take(3))
                    if (!Notices.InQuiet(policy.Quiet, DateTime.Now.TimeOfDay)) { hub.Publish(PetProtocol.Notice(msg, "attention")); _ = push.SendAsync("Nimbo", msg, "/#agents", "team"); }
            }
            catch (Exception e) when (e is IOException or InvalidOperationException) { }
            finally { Interlocked.Exchange(ref busy, 0); }
        }, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(15));
        using var tick = new Timer(_ => service.Tick(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        var initial = providers.Status.Settings;
        Console.WriteLine($"Mascota conectada ({(cert is null ? "solo web movil" : $"placa en https://0.0.0.0:{port}")}, proveedor {initial.Provider}, modelo {initial.Model ?? "predeterminado"}). Web movil en http://127.0.0.1:{(o.TryGetValue("web-port", out var w2) ? w2[^1] : "8780")} (publicala con: tailscale serve --bg 8780). Ctrl+C para salir.");
        await app.RunAsync();
        return 0;
    }
}
