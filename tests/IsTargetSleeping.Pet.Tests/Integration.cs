using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using IsTargetSleeping.Pet;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

/// Servidor real en proceso: HTTPS con CA propia, token, limites, WebSocket, chat, cancelar. Puerto efimero en 127.0.0.1.
static class Integration
{
    public static async Task Run(Action<bool, string> check)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "pet-test-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var now = DateTimeOffset.UtcNow;
        using var ca = Pki.EnsureCa(dir, now);
        using var server = Pki.IssueServer(ca, ["127.0.0.1", "localhost"], now);
        check(Pki.ChainsTo(server, ca, now), "PKI: el certificado del servidor encadena con la CA propia");
        using var otherCa = Pki.EnsureCa(Path.Combine(dir, "otra"), now);
        check(!Pki.ChainsTo(server, otherCa, now), "PKI: no encadena con una CA ajena");
        check(Pki.ToPem(ca).StartsWith("-----BEGIN CERTIFICATE-----"), "PKI: la CA se exporta en PEM");
        var sanExt = server.Extensions.OfType<X509SubjectAlternativeNameExtension>().First();
        check(sanExt.EnumerateIPAddresses().Any(a => a.ToString() == "127.0.0.1") && sanExt.EnumerateDnsNames().Contains("127.0.0.1"),
              "PKI: la IP va como SAN de IP y tambien como SAN DNS (mbedTLS 2.x del ESP32 solo mira los DNS)");

        var tokens = new TokenStore(Path.Combine(dir, "tokens.json"));
        string token = tokens.Issue("esp32");
        check(tokens.Validate(token) && !tokens.Validate(token + "x") && !tokens.Validate("") && !tokens.Validate(null), "tokens: valido, alterado, vacio y nulo");
        check(!File.ReadAllText(Path.Combine(dir, "tokens.json")).Contains(token), "tokens: el archivo solo guarda el hash");
        check(new TokenStore(Path.Combine(dir, "tokens.json")).Validate(token), "tokens: sobreviven al reinicio");

        using var store = new SqlitePetStore(Path.Combine(dir, "pet.db"));
        var service = new PetService(store);
        var backend = new FakeBackend { Manual = true };
        string agentFile = Path.Combine(dir, "agent-state.json");
        var agents = new AgentBridge(agentFile);
        var app = PetHost.Build(new PetHostOptions
        {
            Service = service, Tokens = tokens, Backend = backend, Store = store, Agents = agents, ServerCert = server,
            Address = IPAddress.Loopback, Port = 0,
        }, out var hub, out _, out _);
        await app.StartAsync();
        string addr = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First().Replace("[::]", "127.0.0.1").Replace("0.0.0.0", "127.0.0.1");
        var baseUri = new Uri(addr);
        bool Trust(X509Certificate2? c) => c is not null && Pki.ChainsTo(c, ca, DateTimeOffset.UtcNow);

        using var http = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, c, _, _) => Trust(c) }) { BaseAddress = baseUri };
        HttpRequestMessage Req(HttpMethod m, string path, string? tok = null, string? body = null)
        {
            var r = new HttpRequestMessage(m, path);
            if (tok is not null) r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tok);
            if (body is not null) r.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return r;
        }
        static string Code(string json) => JsonDocument.Parse(json).RootElement.GetProperty("code").GetString()!;

        // --- autenticacion
        var r1 = await http.SendAsync(Req(HttpMethod.Get, "/v1/state"));
        check(r1.StatusCode == HttpStatusCode.Unauthorized && Code(await r1.Content.ReadAsStringAsync()) == "unauthorized", "GET /v1/state sin token -> 401");
        var r2 = await http.SendAsync(Req(HttpMethod.Get, "/v1/state", "token-falso"));
        check(r2.StatusCode == HttpStatusCode.Unauthorized, "token invalido -> 401");
        var r3 = await http.SendAsync(Req(HttpMethod.Get, "/v1/state", token));
        var st = JsonDocument.Parse(await r3.Content.ReadAsStringAsync()).RootElement;
        check(r3.StatusCode == HttpStatusCode.OK && st.GetProperty("type").GetString() == "state" && st.GetProperty("rev").GetInt64() == 0, "GET /v1/state con token -> snapshot rev 0");
        check(r3.Headers.CacheControl?.NoStore == true, "respuestas sin cache");
        var r4 = await http.SendAsync(Req(HttpMethod.Get, "/otra-ruta", token));
        check(r4.StatusCode == HttpStatusCode.NotFound, "rutas fuera de /v1 -> 404");

        // --- certificado no confiable: una placa sin la CA debe rechazar el servidor
        using var strict = new HttpClient { BaseAddress = baseUri };
        bool rejected = false;
        try { await strict.SendAsync(Req(HttpMethod.Get, "/v1/state", token)); } catch (HttpRequestException) { rejected = true; }
        check(rejected, "certificado no confiable: el cliente sin la CA rechaza la conexion");

        // --- limites del cuerpo
        var j1 = await http.SendAsync(Req(HttpMethod.Post, "/v1/chat", token, "{roto"));
        check(j1.StatusCode == HttpStatusCode.BadRequest && Code(await j1.Content.ReadAsStringAsync()) == "bad_json", "POST /v1/chat con JSON invalido -> 400 bad_json");
        string id = PetProtocol.NewId();
        var j2 = await http.SendAsync(Req(HttpMethod.Post, "/v1/chat", token, $$"""{"v":1,"type":"chat","id":"{{id}}","text":"{{new string('a', 401)}}"}"""));
        check(j2.StatusCode == HttpStatusCode.BadRequest && Code(await j2.Content.ReadAsStringAsync()) == "text_too_long", "chat de 401 caracteres -> 400 text_too_long");
        var j3 = await http.SendAsync(Req(HttpMethod.Post, "/v1/chat", token, new string('x', 6000)));
        check((int)j3.StatusCode >= 400, "cuerpo de 6 KB rechazado");

        // --- WebSocket: sin token falla; con token hello/accion/dedupe/ping
        using (var bad = new ClientWebSocket())
        {
            bad.Options.RemoteCertificateValidationCallback = (_, c, _, _) => Trust(c as X509Certificate2);
            bool failed = false;
            try { await bad.ConnectAsync(new Uri("wss://" + baseUri.Authority + "/v1/events"), CancellationToken.None); } catch (WebSocketException) { failed = true; }
            check(failed, "WS /v1/events sin token no conecta");
        }

        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Authorization", "Bearer " + token);
        ws.Options.RemoteCertificateValidationCallback = (_, c, _, _) => Trust(c as X509Certificate2);
        await ws.ConnectAsync(new Uri("wss://" + baseUri.Authority + "/v1/events"), CancellationToken.None);

        async Task Send(string json) => await ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);
        // Un lector en segundo plano: cancelar un ReceiveAsync aborta el ClientWebSocket, asi que nunca se cancela.
        var inbox = new System.Collections.Concurrent.ConcurrentQueue<JsonElement>();
        _ = Task.Run(async () =>
        {
            var buf = new byte[8192];
            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    var res = await ws.ReceiveAsync(buf, CancellationToken.None);
                    if (res.MessageType == WebSocketMessageType.Close) break;
                    inbox.Enqueue(JsonDocument.Parse(buf.AsMemory(0, res.Count)).RootElement.Clone());
                }
            }
            catch (Exception e) when (e is WebSocketException or ObjectDisposedException) { }
        });
        async Task<List<JsonElement>> RecvUntil(Func<List<JsonElement>, bool> done, int ms = 4000)
        {
            var list = new List<JsonElement>();
            var end = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < end)
            {
                while (inbox.TryDequeue(out var m)) list.Add(m);
                if (done(list)) break;
                await Task.Delay(10);
            }
            return list;
        }
        static string T(JsonElement e) => e.GetProperty("type").GetString()!;

        await Send($$"""{"v":1,"type":"hello","id":"{{PetProtocol.NewId()}}","since_rev":0}""");
        var hello = await RecvUntil(l => l.Any(m => T(m) == "state"));
        check(hello.Any(m => T(m) == "state" && m.GetProperty("rev").GetInt64() == 0), "hello -> state completo");

        // los agentes de la PC llegan a la placa
        File.WriteAllText(agentFile, $$"""{"state":"working","ts":{{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}""");
        agents.Poll();
        var ag = await RecvUntil(l => l.Any(m => T(m) == "agents"));
        check(ag.Any(m => T(m) == "agents" && m.GetProperty("state").GetString() == "working"), "WS: agents working llega a la placa");
        File.WriteAllText(agentFile, $$"""{"state":"done","ts":{{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}""");
        agents.Poll();
        check((await RecvUntil(l => l.Any(m => T(m) == "agents"))).Any(m => m.GetProperty("state").GetString() == "done"), "WS: agents done");

        string a1 = PetProtocol.NewId();
        await Send($$"""{"v":1,"type":"action","id":"{{a1}}","kind":"tap","age_s":0}""");
        var got = await RecvUntil(l => l.Any(m => T(m) == "ack") && l.Any(m => T(m) == "state"));
        var ack = got.FirstOrDefault(m => T(m) == "ack");
        check(ack.ValueKind == JsonValueKind.Object && ack.GetProperty("ref").GetString() == a1 && ack.GetProperty("rev").GetInt64() == 1, "accion -> ack con rev 1");
        check(got.Any(m => T(m) == "state" && m.GetProperty("rev").GetInt64() == 1 && !m.GetProperty("pet").GetProperty("asleep").GetBoolean()), "accion -> state nuevo difundido");

        await Send($$"""{"v":1,"type":"action","id":"{{a1}}","kind":"tap","age_s":0}""");  // reenvio del mismo id
        var dup = await RecvUntil(l => l.Any(m => T(m) == "ack"));
        check(dup.Any(m => T(m) == "ack" && m.GetProperty("rev").GetInt64() == 1) && !dup.Any(m => T(m) == "state"), "reenviar el id: ack con el mismo rev y sin cambio");
        check(service.Snapshot.Rev == 1, "el servicio no volvio a aplicar la accion");

        await Send($$"""{"v":1,"type":"ping","id":"{{PetProtocol.NewId()}}"}""");
        check((await RecvUntil(l => l.Any(m => T(m) == "pong"))).Any(m => T(m) == "pong"), "ping -> pong");

        await Send("{esto no es json");
        check((await RecvUntil(l => l.Any(m => T(m) == "error"))).Any(m => T(m) == "error" && m.GetProperty("code").GetString() == "bad_json"), "JSON invalido por WS -> error bad_json (la conexion sigue)");
        await Send($$"""{"v":1,"type":"ping","id":"{{PetProtocol.NewId()}}"}""");
        check((await RecvUntil(l => l.Any(m => T(m) == "pong"))).Any(m => T(m) == "pong"), "la conexion sigue viva tras un error");

        // --- chat completo por HTTP, respuesta por WS
        var c1 = await http.SendAsync(Req(HttpMethod.Post, "/v1/chat", token, $$"""{"v":1,"type":"chat","id":"{{PetProtocol.NewId()}}","text":"Hola, ¿cómo estás?"}"""));
        var acc = JsonDocument.Parse(await c1.Content.ReadAsStringAsync()).RootElement;
        check(c1.StatusCode == HttpStatusCode.Accepted && acc.GetProperty("type").GetString() == "chat_accepted", "POST /v1/chat -> 202 chat_accepted");
        string chatId = acc.GetProperty("chat_id").GetString()!;
        var think = await RecvUntil(l => l.Any(m => T(m) == "status"));
        check(think.Any(m => T(m) == "status" && m.GetProperty("phase").GetString() == "thinking"), "WS: status thinking");
        backend.Complete(new ChatReply("¡Muy bien, gracias por preguntar!", "happy"));
        var said = await RecvUntil(l => l.Any(m => T(m) == "say"));
        var say = said.FirstOrDefault(m => T(m) == "say");
        check(say.ValueKind == JsonValueKind.Object && say.GetProperty("text").GetString() == "¡Muy bien, gracias por preguntar!" && say.GetProperty("chat_id").GetString() == chatId, "WS: say con tildes y chat_id");

        // --- chat y cancelar por el WebSocket (lo que usa la placa)
        await Send($$"""{"v":1,"type":"chat","id":"{{PetProtocol.NewId()}}","text":"¿Qué haces hoy?"}""");
        var wsAcc = await RecvUntil(l => l.Any(m => T(m) == "chat_accepted"));
        var wsAccepted = wsAcc.FirstOrDefault(m => T(m) == "chat_accepted");
        check(wsAccepted.ValueKind == JsonValueKind.Object, "WS: chat -> chat_accepted");
        string wsChat = wsAccepted.GetProperty("chat_id").GetString()!;
        await Send($$"""{"v":1,"type":"cancel","id":"{{PetProtocol.NewId()}}","chat_id":"{{wsChat}}"}""");
        var wsCx = await RecvUntil(l => l.Any(m => T(m) == "chat_cancelled"));
        check(wsCx.Any(m => T(m) == "chat_cancelled"), "WS: cancel -> chat_cancelled");
        await Send($$"""{"v":1,"type":"cancel","id":"{{PetProtocol.NewId()}}","chat_id":"{{PetProtocol.NewId()}}"}""");
        var wsNf = await RecvUntil(l => l.Any(m => T(m) == "error"));
        check(wsNf.Any(m => T(m) == "error" && m.GetProperty("code").GetString() == "not_found"), "WS: cancel de un chat inexistente -> not_found");
        await Task.Delay(150);
        while (inbox.TryDequeue(out _)) { }

        // --- cancelar y busy
        var c2 = await http.SendAsync(Req(HttpMethod.Post, "/v1/chat", token, $$"""{"v":1,"type":"chat","id":"{{PetProtocol.NewId()}}","text":"cuéntame un cuento"}"""));
        string chat2 = JsonDocument.Parse(await c2.Content.ReadAsStringAsync()).RootElement.GetProperty("chat_id").GetString()!;
        var cx = await http.SendAsync(Req(HttpMethod.Post, $"/v1/chat/{chat2}/cancel", token));
        check(cx.StatusCode == HttpStatusCode.OK, "cancelar -> 200");
        var cx2 = await http.SendAsync(Req(HttpMethod.Post, $"/v1/chat/{PetProtocol.NewId()}/cancel", token));
        check(cx2.StatusCode == HttpStatusCode.NotFound, "cancelar un chat inexistente -> 404");
        var cx3 = await http.SendAsync(Req(HttpMethod.Post, "/v1/chat/no-es-un-id/cancel", token));
        check(cx3.StatusCode == HttpStatusCode.NotFound, "cancelar con id mal formado -> 404");

        HttpStatusCode last = HttpStatusCode.OK;
        for (int i = 0; i < 6; i++)
        {
            var rr = await http.SendAsync(Req(HttpMethod.Post, "/v1/chat", token, $$"""{"v":1,"type":"chat","id":"{{PetProtocol.NewId()}}","text":"m{{i}}"}"""));
            last = rr.StatusCode;
            if (last == HttpStatusCode.Conflict) { check(Code(await rr.Content.ReadAsStringAsync()) == "busy", "cola llena -> 409 busy"); break; }
        }
        check(last == HttpStatusCode.Conflict, "la cola llena responde 409");

        await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "fin", CancellationToken.None);  // el lector de fondo recibe el cierre del servidor

        // --- mensaje demasiado grande por WS: el servidor avisa y cierra
        using var ws2 = new ClientWebSocket();
        ws2.Options.SetRequestHeader("Authorization", "Bearer " + token);
        ws2.Options.RemoteCertificateValidationCallback = (_, c, _, _) => Trust(c as X509Certificate2);
        await ws2.ConnectAsync(new Uri("wss://" + baseUri.Authority + "/v1/events"), CancellationToken.None);
        await ws2.SendAsync(Encoding.UTF8.GetBytes(new string('z', 5000)), WebSocketMessageType.Text, true, CancellationToken.None);
        var buf2 = new byte[8192];
        bool sawTooBig = false;
        using (var cts = new CancellationTokenSource(4000))
        {
            try
            {
                while (ws2.State == WebSocketState.Open || ws2.State == WebSocketState.CloseSent)
                {
                    var res = await ws2.ReceiveAsync(buf2, cts.Token);
                    if (res.MessageType == WebSocketMessageType.Close) { sawTooBig |= ws2.CloseStatus == WebSocketCloseStatus.MessageTooBig; break; }
                    sawTooBig |= Encoding.UTF8.GetString(buf2, 0, res.Count).Contains("too_large");
                }
            }
            catch (Exception e) when (e is OperationCanceledException or WebSocketException) { }
        }
        check(sawTooBig, "WS: mensaje de 5 KB -> too_large y cierre");

        await app.StopAsync();
        await app.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(dir, true); } catch { }
    }
}
