using System.Net;
using System.Net.WebSockets;
using System.Text;
using IsTargetSleeping.Pet;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

/// Web movil: emparejado por codigo/QR, sesion por cookie, CSRF y Origin. Servidor real en 127.0.0.1, puertos efimeros.
static class WebTests
{
    public static async Task Run(Action<bool, string> check)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "pet-test-web-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var now = DateTimeOffset.UtcNow;
        using var ca = Pki.EnsureCa(dir, now);
        using var server = Pki.IssueServer(ca, ["127.0.0.1"], now);
        var tokens = new TokenStore(Path.Combine(dir, "tokens.json"));
        tokens.Issue("esp32");
        using var store = new SqlitePetStore(Path.Combine(dir, "pet.db"));
        var pairing = new PairingStore(Path.Combine(dir, "pair.json"));
        var app = PetHost.Build(new PetHostOptions
        {
            Service = new PetService(store), Tokens = tokens, Backend = new FakeBackend { Manual = true }, Store = store,
            ServerCert = server, Address = IPAddress.Loopback, Port = 0, WebPort = 0, Pairing = pairing,
            PairPagePath = Path.Combine(dir, "pair.html"), UploadsPath = Path.Combine(dir, "uploads"), Bridge = new FakeBridge(), Policy = new PolicyStore(Path.Combine(dir, "policy.json")), Profiles = new AgentProfilesFile(Path.Combine(dir, "team", "agents.json")),
        }, out _, out _, out _);
        await app.StartAsync();
        var addrs = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses
            .Select(a => new Uri(a.Replace("[::]", "127.0.0.1").Replace("0.0.0.0", "127.0.0.1"))).ToList();
        var web = addrs.First(u => u.Scheme == "http");
        var devUri = addrs.First(u => u.Scheme == "https");
        var jar = new CookieContainer();
        using var http = new HttpClient(new HttpClientHandler { CookieContainer = jar }) { BaseAddress = web };
        using var devHttp = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }) { BaseAddress = devUri };
        string own = web.GetLeftPart(UriPartial.Authority);

        check((await http.GetAsync("/")).Content.Headers.ContentType?.MediaType == "text/html", "web: la pagina se sirve sin sesion");
        check((await http.GetAsync("/v1/state")).StatusCode == HttpStatusCode.Unauthorized, "web: /v1 sin sesion -> 401");
        check((await http.GetAsync("/web/session")).StatusCode == HttpStatusCode.Unauthorized, "web: /web/session sin sesion -> 401");
        check((await devHttp.GetAsync("/")).StatusCode == HttpStatusCode.NotFound, "dispositivo: la pagina web NO se sirve en el puerto del dispositivo");
        var devPair = await devHttp.PostAsync("/web/pair", new StringContent("""{"code":"AAAA-AAAA"}""", Encoding.UTF8, "application/json"));
        check(devPair.StatusCode == HttpStatusCode.NotFound, "dispositivo: /web/pair no existe en el puerto del dispositivo");

        Task<HttpResponseMessage> Pair(string code, string? origin = null)
        {
            var r = new HttpRequestMessage(HttpMethod.Post, "/web/pair") { Content = new StringContent("{\"code\":\"" + code + "\"}", Encoding.UTF8, "application/json") };
            if (origin is not null) r.Headers.Add("Origin", origin);
            return http.SendAsync(r);
        }

        check((await Pair("AAAA-AAAA")).StatusCode == HttpStatusCode.Unauthorized, "emparejar: sin codigo emitido -> 401");
        string code = pairing.NewCode();
        check((await Pair(code, "http://evil.example")).StatusCode == HttpStatusCode.Forbidden, "emparejar: Origin de otro sitio -> 403");
        var ok = await Pair(code.ToLowerInvariant());
        check(ok.IsSuccessStatusCode, "emparejar: codigo valido (minusculas y con guion) -> 200");
        string csrf = System.Text.Json.JsonDocument.Parse(await ok.Content.ReadAsStringAsync()).RootElement.GetProperty("csrf").GetString()!;
        check(jar.GetCookies(web)["its_pet"] is { HttpOnly: true }, "emparejar: cookie de sesion HttpOnly");
        check((await Pair(code)).StatusCode == HttpStatusCode.Unauthorized, "emparejar: el codigo es de un solo uso");

        check((await http.GetAsync("/v1/state")).IsSuccessStatusCode, "sesion: GET /v1/state con cookie -> 200");
        check((await http.GetAsync("/web/session")).IsSuccessStatusCode, "sesion: /web/session devuelve el csrf");
        HttpRequestMessage Chat(string? c, string? origin = null)
        {
            var r = new HttpRequestMessage(HttpMethod.Post, "/v1/chat") { Content = new StringContent("{\"v\":1,\"type\":\"chat\",\"id\":\"" + PetProtocol.NewId() + "\",\"text\":\"hola\"}", Encoding.UTF8, "application/json") };
            if (c is not null) r.Headers.Add("X-CSRF", c);
            if (origin is not null) r.Headers.Add("Origin", origin);
            return r;
        }
        check((await http.SendAsync(Chat(null))).StatusCode == HttpStatusCode.Forbidden, "csrf: POST sin X-CSRF -> 403");
        check((await http.SendAsync(Chat("otro"))).StatusCode == HttpStatusCode.Forbidden, "csrf: POST con X-CSRF equivocado -> 403");
        check((await http.SendAsync(Chat(csrf, "http://evil.example"))).StatusCode == HttpStatusCode.Forbidden, "csrf: POST con Origin ajeno -> 403");
        check((await http.SendAsync(Chat(csrf, own))).StatusCode == HttpStatusCode.Accepted, "csrf: POST valido -> 202");

        async Task<bool> WsOpens(string origin)
        {
            using var ws = new ClientWebSocket();
            ws.Options.Cookies = jar;
            ws.Options.SetRequestHeader("Origin", origin);
            try { await ws.ConnectAsync(new Uri("ws://" + web.Authority + "/v1/events"), CancellationToken.None); return true; }
            catch (WebSocketException) { return false; }
        }
        check(await WsOpens(own), "ws: se abre con cookie y Origin propio");
        check(!await WsOpens("http://evil.example"), "ws: Origin ajeno rechazado");
        check((await devHttp.GetAsync("/v1/state")).StatusCode == HttpStatusCode.Unauthorized, "dispositivo: sin token no entra (la web no abre esta puerta)");

        string c2 = pairing.NewCode();
        for (int i = 0; i < PairingStore.MaxFails; i++) await Pair("ZZZZ-ZZZZ");
        check((await Pair(c2)).StatusCode == HttpStatusCode.Unauthorized, "emparejar: tras 5 fallos el codigo queda anulado");

        var t = now;
        var timed = new PairingStore(Path.Combine(dir, "p2.json"), () => t);
        string c3 = timed.NewCode();
        t = t.AddMinutes(6);
        check(!timed.Redeem(c3), "emparejar: el codigo caduca a los 5 minutos");

        var sNow = now;
        var ss = new SessionStore(() => sNow);
        var (sid, _) = ss.Create();
        check(ss.Csrf(sid) is not null, "sesiones: valida al crear");
        sNow = sNow.AddDays(8);
        check(ss.Csrf(sid) is null, "sesiones: caduca a los 7 dias");

        var lo = new HttpRequestMessage(HttpMethod.Post, "/web/logout");
        lo.Headers.Add("X-CSRF", csrf);
        check((await http.SendAsync(lo)).IsSuccessStatusCode && (await http.GetAsync("/v1/state")).StatusCode == HttpStatusCode.Unauthorized, "sesiones: salir cierra la sesion");

        check((await http.GetAsync("/sw.js")).Content.Headers.ContentType?.MediaType == "text/javascript" && (await http.GetAsync("/manifest.webmanifest")).IsSuccessStatusCode && (await http.GetAsync("/logo-180.png")).Content.Headers.ContentType?.MediaType == "image/png", "pwa: service worker, manifest e icono se sirven sin sesion");
        check((await http.GetAsync("/jsqr.js")).Content.Headers.ContentType?.MediaType == "text/javascript" && (await http.GetStringAsync("/jsqr.js")).Contains("jsQR"), "pwa: el lector de QR se sirve sin sesion");
        // agenda y memoria por REST (con sesion y CSRF; la sesion ya se cerro arriba, se empareja de nuevo)
        string code2 = pairing.NewCode();
        var ok2 = await Pair(code2);
        string csrf2 = System.Text.Json.JsonDocument.Parse(await ok2.Content.ReadAsStringAsync()).RootElement.GetProperty("csrf").GetString()!;
        HttpRequestMessage Rq(HttpMethod m, string path, string? body = null, bool withCsrf = true)
        {
            var r = new HttpRequestMessage(m, path);
            if (withCsrf) r.Headers.Add("X-CSRF", csrf2);
            if (body is not null) r.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return r;
        }
        check((await http.SendAsync(Rq(HttpMethod.Post, "/v1/reminders", "{\"say\":\"en 10 minutos probar\"}", withCsrf: false))).StatusCode == HttpStatusCode.Forbidden, "rest: crear un recordatorio exige CSRF");
        var created = await http.SendAsync(Rq(HttpMethod.Post, "/v1/reminders", "{\"say\":\"en 10 minutos revisar el horno\"}"));
        check(created.IsSuccessStatusCode, "rest: crear un recordatorio con texto natural");
        long remId = System.Text.Json.JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt64();
        check((await http.GetStringAsync("/v1/reminders")).Contains("Revisar el horno"), "rest: la agenda lo lista");
        check((int)(await http.SendAsync(Rq(HttpMethod.Post, "/v1/reminders", "{\"say\":\"comprar pan\"}"))).StatusCode == 422, "rest: sin hora clara responde 422 con el motivo");
        check((await http.SendAsync(Rq(HttpMethod.Delete, "/v1/reminders/" + remId))).IsSuccessStatusCode && !(await http.GetStringAsync("/v1/reminders")).Contains("Revisar el horno"), "rest: se puede borrar");
        var mem = await http.SendAsync(Rq(HttpMethod.Post, "/v1/memory", "{\"text\":\"Prefiere el café sin azúcar\"}"));
        long memId = System.Text.Json.JsonDocument.Parse(await mem.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt64();
        check((await http.GetStringAsync("/v1/memory")).Contains("café sin azúcar"), "rest: memoria — crear y listar (con tildes)");
        check((await http.SendAsync(Rq(HttpMethod.Put, "/v1/memory/" + memId, "{\"text\":\"Prefiere el té\"}"))).IsSuccessStatusCode && (await http.GetStringAsync("/v1/memory")).Contains("Prefiere el té"), "rest: memoria — editar");
        check((int)(await http.SendAsync(Rq(HttpMethod.Post, "/v1/memory", "{\"text\":\"" + new string('x', 400) + "\"}"))).StatusCode == 422, "rest: memoria — un texto enorme se rechaza");
        check((await http.SendAsync(Rq(HttpMethod.Delete, "/v1/memory/" + memId))).IsSuccessStatusCode && !(await http.GetStringAsync("/v1/memory")).Contains("Prefiere el té"), "rest: memoria — borrar");

        // agentes propios por REST
        const string prof = "{\"name\":\"Revisor\",\"description\":\"revisa\",\"engine\":\"claude\",\"model\":\"haiku\",\"instructions\":\"Eres un revisor.\",\"canWrite\":false,\"tools\":[\"Read\",\"Grep\"],\"maxTokens\":20000,\"timeoutMin\":5}";
        check((await http.SendAsync(Rq(HttpMethod.Put, "/v1/agent-profiles/revisor", prof, withCsrf: false))).StatusCode == HttpStatusCode.Forbidden, "agentes propios: guardar exige CSRF");
        check((await http.SendAsync(Rq(HttpMethod.Put, "/v1/agent-profiles/revisor", prof))).IsSuccessStatusCode && (await http.GetStringAsync("/v1/agent-profiles")).Contains("Eres un revisor."), "agentes propios: crear y listar");
        check((int)(await http.SendAsync(Rq(HttpMethod.Put, "/v1/agent-profiles/malo", prof.Replace("\"Read\"", "\"Bash\"")))).StatusCode == 422, "agentes propios: una herramienta fuera de la lista se rechaza");
        check((int)(await http.SendAsync(Rq(HttpMethod.Put, "/v1/agent-profiles/malo", prof.Replace("\"maxTokens\":20000", "\"maxTokens\":0")))).StatusCode == 422, "agentes propios: sin tope de tokens se rechaza");
        check((int)(await http.SendAsync(Rq(HttpMethod.Put, "/v1/agent-profiles/Mal Id", prof))).StatusCode is 422 or 404, "agentes propios: un id con espacios o mayusculas se rechaza");
        check(!(await http.GetStringAsync("/v1/agent-profiles")).Contains("\"malo\""), "agentes propios: lo rechazado no se guarda");
        check((await http.SendAsync(Rq(HttpMethod.Delete, "/v1/agent-profiles/revisor"))).IsSuccessStatusCode && !(await http.GetStringAsync("/v1/agent-profiles")).Contains("Eres un revisor."), "agentes propios: borrar");

        // lanzar desde la caja de chat (modo Equipo)
        check((await http.SendAsync(Rq(HttpMethod.Post, "/v1/team/launch", "{\"task\":\"Arregla el login\"}", withCsrf: false))).StatusCode == HttpStatusCode.Forbidden, "launch: exige CSRF");
        check((int)(await http.SendAsync(Rq(HttpMethod.Post, "/v1/team/launch", "{\"task\":\"\"}"))).StatusCode == 422, "launch: sin tarea -> 422");
        check((int)(await http.SendAsync(Rq(HttpMethod.Post, "/v1/team/launch", "{\"task\":\"Arregla el login\",\"preset\":\"Turbo\"}"))).StatusCode == 422, "launch: un modo desconocido -> 422");
        check((int)(await http.SendAsync(Rq(HttpMethod.Post, "/v1/team/launch", "{\"task\":\"Arregla el login\",\"effort\":\"Extremo\"}"))).StatusCode == 422, "launch: un esfuerzo desconocido -> 422");
        check((int)(await http.SendAsync(Rq(HttpMethod.Post, "/v1/team/launch", "{\"task\":\"Arregla el login en la app\",\"root\":\"isTargetSleeping\",\"preset\":\"Quality\",\"effort\":\"High\",\"review_plan\":true}"))).StatusCode == 202, "launch: una tarea valida queda pendiente de confirmar (202)");

        // adjuntos del celular
        HttpRequestMessage Up(string name, byte[] bytes, bool csrf = true) { var r = Rq(HttpMethod.Post, "/v1/team/attach?name=" + Uri.EscapeDataString(name), null, csrf); r.Content = new ByteArrayContent(bytes); return r; }
        check((await http.SendAsync(Up("foto.png", [1, 2, 3], csrf: false))).StatusCode == HttpStatusCode.Forbidden, "adjuntos: exige CSRF");
        var up = await http.SendAsync(Up("foto.png", [137, 80, 78, 71, 1, 2, 3]));
        check(up.IsSuccessStatusCode, "adjuntos: una imagen se sube");
        string upId = System.Text.Json.JsonDocument.Parse(await up.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
        check(File.Exists(Path.Combine(dir, "uploads", upId, "foto.png")), "adjuntos: queda en uploads/<id>/<nombre>");
        check((int)(await http.SendAsync(Up("virus.exe", [1, 2, 3]))).StatusCode == 415, "adjuntos: un .exe se rechaza");
        check((int)(await http.SendAsync(Up("../../evil.txt", [65]))).StatusCode == 200 && !File.Exists(Path.Combine(dir, "evil.txt")), "adjuntos: el nombre no puede salirse de su carpeta");
        check((int)(await http.SendAsync(Up("grande.pdf", new byte[10 * 1024 * 1024 + 10]))).StatusCode == 413, "adjuntos: mas de 10 MB -> 413");
        check((int)(await http.SendAsync(Rq(HttpMethod.Post, "/v1/team/launch", "{\"task\":\"Arregla el login\",\"attachments\":[\"" + upId + "\"]}"))).StatusCode == 202, "launch: con un adjunto valido queda pendiente (202)");
        check((int)(await http.SendAsync(Rq(HttpMethod.Post, "/v1/team/launch", "{\"task\":\"Arregla el login\",\"attachments\":[\"../../x\"]}"))).StatusCode == 422, "launch: un id de adjunto invalido -> 422");

        // pagina de emparejamiento local: solo directa, con su nonce, y avisa cuando el telefono se conecta
        File.WriteAllText(Path.Combine(dir, "pair.html"), "<html>hola</html>");
        var (pcode, nonce) = pairing.NewPairing();
        check((await http.GetStringAsync("/pairing/" + nonce)).Contains("hola"), "pairing: la pagina local se sirve con su nonce");
        check(System.Text.Json.JsonDocument.Parse(await http.GetStringAsync("/pairing/" + nonce + "/status")).RootElement.GetProperty("state").GetString() == "waiting", "pairing: esperando al telefono");
        check((await http.GetAsync("/pairing/" + new string('a', 32))).StatusCode == HttpStatusCode.NotFound, "pairing: un nonce equivocado -> 404");
        var viaProxy = new HttpRequestMessage(HttpMethod.Get, "/pairing/" + nonce + "/status"); viaProxy.Headers.Add("X-Forwarded-For", "100.64.0.9");
        check((await http.SendAsync(viaProxy)).StatusCode == HttpStatusCode.NotFound, "pairing: por un proxy o Tailscale no se contesta");
        var viaTs = new HttpRequestMessage(HttpMethod.Get, "/pairing/" + nonce); viaTs.Headers.Add("Tailscale-User-Login", "x@y.z");
        check((await http.SendAsync(viaTs)).StatusCode == HttpStatusCode.NotFound, "pairing: con cabeceras de Tailscale tampoco");
        check((await Pair(pcode)).IsSuccessStatusCode, "pairing: el telefono canjea el codigo");
        check(System.Text.Json.JsonDocument.Parse(await http.GetStringAsync("/pairing/" + nonce + "/status")).RootElement.GetProperty("state").GetString() == "used", "pairing: la pagina se entera de que ya se uso");
        check((await Pair(pcode)).StatusCode == HttpStatusCode.Unauthorized, "pairing: un codigo usado no vale otra vez");

        await app.StopAsync();
        try { Directory.Delete(dir, true); } catch { }
    }
}
