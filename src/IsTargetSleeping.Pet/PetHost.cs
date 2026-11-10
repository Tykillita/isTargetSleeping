using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IsTargetSleeping.Pet;

public sealed class PetHostOptions
{
    public required PetService Service { get; init; }
    public required TokenStore Tokens { get; init; }
    public required IChatBackend Backend { get; init; }
    public SqlitePetStore? Store { get; init; }
    public AgentBridge? Agents { get; init; }
    /// null = sin puerto de dispositivo (solo web movil).
    public X509Certificate2? ServerCert { get; init; }
    public IPAddress Address { get; init; } = IPAddress.Any;
    public int Port { get; init; } = 8443;
    /// Web movil: HTTP solo en 127.0.0.1 (se publica con `tailscale serve`). null = desactivada, 0 = puerto efimero.
    public int? WebPort { get; init; }
    public PairingStore? Pairing { get; init; }
    /// Herramientas, permisos y puente con la app. null = sin acciones.
    public IAppBridge? Bridge { get; init; }
    public ApprovalFeed? Approvals { get; init; }
    public PushService? Push { get; init; }
    public string? SessionsPath { get; init; }
    public string? TeamPath { get; init; }
    public string? PairPagePath { get; init; }
    public string? UploadsPath { get; init; }
    public AgentProfilesFile? Profiles { get; init; }
    public PolicyStore? Policy { get; init; }
    public NimboProviderManager? Providers { get; init; }
    public Func<string?>? UsageProbe { get; init; }
}

/// Difusion a todos los clientes conectados por WebSocket. Cada cliente tiene una cola acotada: si no lee, pierde lo mas viejo.
public sealed class EventsHub : IEventSink
{
    public sealed class Client(WebSocket ws)
    {
        public WebSocket Socket { get; } = ws;
        public Channel<byte[]> Out { get; } = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });
    }

    private readonly ConcurrentDictionary<Guid, Client> clients = new();
    public int Count => clients.Count;
    public Guid Add(Client c) { var id = Guid.NewGuid(); clients[id] = c; return id; }
    public void Remove(Guid id) { if (clients.TryRemove(id, out var c)) c.Out.Writer.TryComplete(); }
    public void Publish(byte[] message) { foreach (var c in clients.Values) c.Out.Writer.TryWrite(message); }
}

/// Servidor local (Kestrel, HTTPS) de la mascota: GET /v1/state, WS /v1/events, POST /v1/chat y /v1/chat/{id}/cancel.
public static class PetHost
{
    public static WebApplication Build(PetHostOptions o, out EventsHub hub, out ChatCoordinator chat, out ActionBroker? brokerOut)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.AddServerHeader = false;
            k.Limits.MaxRequestBodySize = PetProtocol.MaxMessageBytes;
            k.Limits.MaxRequestLineSize = 2048;
            k.Limits.MaxRequestHeadersTotalSize = 8192;
            if (o.ServerCert is { } cert) k.Listen(o.Address, o.Port, l => l.UseHttps(cert));
            if (o.WebPort is { } wp) k.Listen(IPAddress.Loopback, wp);
        });
        var app = builder.Build();
        var eventsHub = hub = new EventsHub();
        ActionBroker? broker = null;
        if (o.Bridge is not null && o.Policy is not null)
        {
            var registry = new ToolRegistry().Add(new UsageSetTool(o.Bridge)).Add(new PomodoroTool(o.Bridge)).Add(new TodoAddTool(o.Bridge))
                .Add(new NoteAddTool(o.Bridge)).Add(new NotifyTool(o.Bridge));
            registry.Add(new TeamRunTool(o.Bridge, o.UploadsPath));
            if (o.Profiles is { } pf) registry.Add(new TeamEnqueueTool(o.Bridge, pf));
            if (o.Store is { } st) registry.Add(new ReminderAddTool(st)).Add(new MemoryRememberTool(st)).Add(new MemoryForgetTool(st));
            broker = new ActionBroker(registry, o.Policy, eventsHub, o.Store is null ? null : o.Store.AddActivity);
        }
        brokerOut = broker;
        ApprovalService? approvals = null;
        if (o.Approvals is { } feed && o.Bridge is not null && o.Policy is not null)
        {
            approvals = new ApprovalService(feed, o.Policy, o.Bridge, eventsHub, o.Store is null ? null : o.Store.AddActivity);
            feed.Changed += (added, removed) =>
            {
                foreach (var a in added) { eventsHub.Publish(PetProtocol.Approval(a, a.Risk is null || o.Policy?.RiskyFromPhone == true)); if (o.Push is not null) _ = o.Push.SendAsync("Un agente espera tu respuesta", (a.Agent == "claude" ? "Claude Code" : a.Agent) + " · " + (a.Kind == "question" ? "te hace una pregunta" : a.Kind == "plan" ? "propone un plan" : "pide permiso"), "/#agents", "ap" + a.Id[..8]); }
                foreach (var id in removed) eventsHub.Publish(PetProtocol.ApprovalDone(id, "deny", "pc"));   // se resolvio en la PC (o caduco)
            };
        }
        var coordinator = chat = new ChatCoordinator(o.Backend, eventsHub, o.Store, broker: broker, usageProbe: o.UsageProbe,
            attachments: o.UploadsPath is null ? null : (ids => ChatAttachments.Read(o.UploadsPath, ids)),
            agents: o.Profiles is null ? null : () => o.Profiles.List().OfType<System.Text.Json.Nodes.JsonObject>().Where(x => x["enabled"]?.GetValue<bool>() != false).Select(x => (x["id"]!.GetValue<string>(), x["name"]!.GetValue<string>())).ToList());
        o.Service.Changed += s => eventsHub.Publish(PetProtocol.State(s));
        string? lastAgent = null;
        if (o.Agents is { } bridge) bridge.Changed += st =>
        {
            eventsHub.Publish(PetProtocol.Agents(st));
            if (Notices.ForAgentState(st, lastAgent) is { } n && !Notices.InQuiet(o.Policy?.Quiet, DateTime.Now.TimeOfDay))
            {
                eventsHub.Publish(PetProtocol.Notice(n, st == "done" ? "info" : "attention"));
                if (o.Push is not null) _ = o.Push.SendAsync("Nimbo", n, "/#agents", "agents");
            }
            lastAgent = st;
        };
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

        var sessions = new SessionStore();
        int? webLocal = null;
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            if (o.WebPort is null) return;
            var a = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses;
            webLocal = a.Select(x => new Uri(x)).First(u => u.Scheme == "http").Port;
        });

        // Autenticacion. Puerto del dispositivo: "Authorization: Bearer <token>". Puerto web (solo loopback): cookie de sesion,
        // Origin igual al Host y, en POST, cabecera X-CSRF; sin sesion solo se sirve la pagina y el canje del codigo.
        app.Use(async (ctx, next) =>
        {
            bool web = o.WebPort is not null && ctx.Connection.LocalPort == webLocal;
            if (web) { await WebGate(ctx, next, o, sessions); return; }
            if (!ctx.Request.Path.StartsWithSegments("/v1")) { ctx.Response.StatusCode = 404; return; }
            if (!o.Tokens.Validate(TokenStore.FromHeader(ctx.Request.Headers.Authorization)))
            {
                await Reply(ctx, 401, PetProtocol.Error("unauthorized", "token invalido"));
                return;
            }
            await next();
        });

        app.MapGet("/v1/state", async ctx => await Reply(ctx, 200, PetProtocol.State(o.Service.Snapshot)));

        // Permisos y actividad: solo desde la web del celular (el token de la placa no cambia la politica).
        bool Phone(HttpContext c) => o.WebPort is not null && c.Connection.LocalPort == webLocal;
        byte[] ProviderJson(NimboProviderStatus status)
        {
            var profiles = new System.Text.Json.Nodes.JsonObject();
            foreach (var (key, profile) in status.Profiles)
                profiles[key] = new System.Text.Json.Nodes.JsonObject
                {
                    ["model"] = profile.Model, ["project"] = profile.Project, ["has_api_key"] = profile.HasApiKey,
                };
            var s = status.Settings;
            return System.Text.Encoding.UTF8.GetBytes(new System.Text.Json.Nodes.JsonObject
            {
                ["provider"] = s.Provider, ["model"] = s.Model, ["auth"] = s.Auth,
                ["has_api_key"] = status.HasApiKey, ["project"] = s.Project, ["configured"] = status.Configured,
                ["profiles"] = profiles,
            }.ToJsonString());
        }
        app.MapGet("/v1/provider", async ctx =>
        {
            if (o.Providers is null || !Phone(ctx)) { await Reply(ctx, 403, PetProtocol.Error("forbidden", "solo desde los ajustes web de Nimbo")); return; }
            await Reply(ctx, 200, ProviderJson(o.Providers.Status));
        });
        app.MapPut("/v1/provider", async ctx =>
        {
            if (o.Providers is null || !Phone(ctx)) { await Reply(ctx, 403, PetProtocol.Error("forbidden", "solo desde los ajustes web de Nimbo")); return; }
            var (body, tooBig) = await ReadBody(ctx.Request.Body, ctx.RequestAborted, 16 * 1024);
            if (tooBig) { await Reply(ctx, 400, PetProtocol.Error("too_large", "maximo 16 KB")); return; }
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind != System.Text.Json.JsonValueKind.Object) throw new ArgumentException("Configuración no válida.");
                string? Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;
                bool clearKey = root.TryGetProperty("clear_api_key", out var clear) && clear.ValueKind == System.Text.Json.JsonValueKind.True;
                var status = o.Providers.Configure(Str("provider") ?? "", Str("model"), Str("auth") ?? "", Str("api_key"), Str("project"), clearKey);
                await Reply(ctx, 200, ProviderJson(status));
            }
            catch (System.Text.Json.JsonException) { await Reply(ctx, 400, PetProtocol.Error("bad_json", "json invalido")); }
            catch (ArgumentException e) { await Reply(ctx, 422, PetProtocol.Error("bad_json", e.Message)); }
            catch (IOException) { await Reply(ctx, 500, PetProtocol.Error("internal", "no pude guardar la configuración del proveedor")); }
            catch (UnauthorizedAccessException) { await Reply(ctx, 500, PetProtocol.Error("internal", "no pude guardar la configuración del proveedor")); }
            catch (System.Security.Cryptography.CryptographicException) { await Reply(ctx, 500, PetProtocol.Error("internal", "no pude cifrar la configuración del proveedor")); }
        });
        app.MapPost("/v1/provider/login/{provider}", async (HttpContext ctx, string provider) =>
        {
            if (o.Providers is null || !Phone(ctx)) { await Reply(ctx, 403, PetProtocol.Error("forbidden", "solo desde los ajustes web de Nimbo")); return; }
            try { o.Providers.StartLogin(provider); await Reply(ctx, 202, "{\"started\":true}"u8.ToArray()); }
            catch (ArgumentException e) { await Reply(ctx, 422, PetProtocol.Error("bad_json", e.Message)); }
            catch (InvalidOperationException e) { await Reply(ctx, 422, PetProtocol.Error("model_unavailable", e.Message)); }
        });
        app.MapGet("/v1/policy", async ctx =>
        {
            if (o.Policy is null) { await Reply(ctx, 404, PetProtocol.Error("not_found", "sin politica")); return; }
            await Reply(ctx, 200, PolicyJson(o.Policy, broker));
        });
        app.MapPut("/v1/policy", async ctx =>
        {
            if (o.Policy is null || !Phone(ctx)) { await Reply(ctx, 403, PetProtocol.Error("forbidden", "solo desde el celular")); return; }
            var (body, tooBig) = await ReadBody(ctx.Request.Body, ctx.RequestAborted);
            if (tooBig) { await Reply(ctx, 400, PetProtocol.Error("too_large", "maximo 4096 bytes")); return; }
            try
            {
                var root = System.Text.Json.JsonDocument.Parse(body).RootElement;
                foreach (var p in root.EnumerateObject())
                    switch (p.Name)
                    {
                        case "paused" when p.Value.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False: o.Policy.SetPaused(p.Value.GetBoolean()); break;
                        case "strict" when p.Value.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False: o.Policy.SetStrict(p.Value.GetBoolean()); break;
                        case "quiet" when p.Value.ValueKind == System.Text.Json.JsonValueKind.String: o.Policy.SetQuiet(p.Value.GetString() ?? ""); break;
                        case "risky_from_phone" when p.Value.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False: o.Policy.SetRiskyFromPhone(p.Value.GetBoolean()); break;
                        case "tools" when p.Value.ValueKind == System.Text.Json.JsonValueKind.Object:
                            foreach (var t in p.Value.EnumerateObject())
                            {
                                bool known = t.Name == "agent.decide" || broker?.Tools.Find(t.Name) is not null;
                                if (!known || t.Value.ValueKind != System.Text.Json.JsonValueKind.String) continue;
                                if (t.Value.GetString() == "default") o.Policy.Set(t.Name, null);
                                else if (Enum.TryParse<ToolMode>(t.Value.GetString(), true, out var mode) && Enum.IsDefined(mode)) o.Policy.Set(t.Name, mode);
                            }
                            break;
                    }
            }
            catch (System.Text.Json.JsonException) { await Reply(ctx, 400, PetProtocol.Error("bad_json", "json invalido")); return; }
            await Reply(ctx, 200, PolicyJson(o.Policy, broker));
        });
        app.MapGet("/v1/activity", async ctx =>
        {
            int n = int.TryParse(ctx.Request.Query["n"], out var q) ? q : 30;
            var rows = o.Store?.RecentActivity(n) ?? [];
            var arr = new System.Text.Json.Nodes.JsonArray();
            foreach (var e in rows) arr.Add(new System.Text.Json.Nodes.JsonObject { ["ts"] = e.Ts, ["channel"] = e.Channel, ["tool"] = e.Tool, ["summary"] = e.Summary, ["outcome"] = e.Outcome });
            await Reply(ctx, 200, System.Text.Encoding.UTF8.GetBytes(new System.Text.Json.Nodes.JsonObject { ["items"] = arr }.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })));
        });
        app.MapDelete("/v1/activity", async ctx =>
        {
            if (!Phone(ctx)) { await Reply(ctx, 403, PetProtocol.Error("forbidden", "solo desde el celular")); return; }
            o.Store?.ClearActivity();
            await Reply(ctx, 200, "{}"u8.ToArray());
        });

        // ---- Agenda (recordatorios) y memoria: el usuario los gestiona desde la pestaña; cada cambio queda en el registro de actividad.
        string Json(System.Text.Json.Nodes.JsonNode n) => n.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        async Task<System.Text.Json.JsonElement?> Body(HttpContext c)
        {
            var (body, tooBig) = await ReadBody(c.Request.Body, c.RequestAborted);
            if (tooBig) { await Reply(c, 400, PetProtocol.Error("too_large", "maximo 4096 bytes")); return null; }
            try { return System.Text.Json.JsonDocument.Parse(body).RootElement.Clone(); }
            catch (System.Text.Json.JsonException) { await Reply(c, 400, PetProtocol.Error("bad_json", "json invalido")); return null; }
        }
        void Log(string tool, string summary, string outcome) => o.Store?.AddActivity(new ActivityEntry(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), "phone", tool, summary, outcome));
        // Agentes trabajando ahora (los publica la app en sessions.json; si no se renueva en 60 s, la app dejo de correr y la lista queda vacia).
        app.MapGet("/v1/sessions", async ctx =>
        {
            var arr = new System.Text.Json.Nodes.JsonArray();
            try
            {
                if (o.SessionsPath is not null && File.Exists(o.SessionsPath))
                {
                    var root = System.Text.Json.JsonDocument.Parse(File.ReadAllText(o.SessionsPath)).RootElement;
                    if (DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("ts").GetInt64()) <= TimeSpan.FromSeconds(60))
                        foreach (var e in root.GetProperty("items").EnumerateArray().Take(8))
                        {
                            string? S(string k, int max) => e.TryGetProperty(k, out var p) && p.ValueKind == System.Text.Json.JsonValueKind.String ? new string(p.GetString()!.Where(c => !char.IsControl(c)).ToArray()) is var t && t.Length > max ? t[..max] : t : null;
                            string? state = S("state", 20);
                            long last = e.TryGetProperty("last", out var l) && l.TryGetInt64(out var lv) ? lv : 0;
                            bool live = state is "working" or "needsattention";
                            if (!live && DateTimeOffset.UtcNow.ToUnixTimeSeconds() - last > 12 * 3600) continue;   // lo terminado hace horas no es noticia
                            long N(string k) => e.TryGetProperty(k, out var nv) && nv.TryGetInt64(out var n) ? Math.Max(0, n) : 0;
                            bool B(string k) => e.TryGetProperty(k, out var bv) && bv.ValueKind == System.Text.Json.JsonValueKind.True;
                            var acts = new System.Text.Json.Nodes.JsonArray();
                            if (e.TryGetProperty("activity", out var av) && av.ValueKind == System.Text.Json.JsonValueKind.Array)
                                foreach (var a in av.EnumerateArray().Take(6))
                                {
                                    string? AS(string k, int max) => a.TryGetProperty(k, out var p) && p.ValueKind == System.Text.Json.JsonValueKind.String ? new string(p.GetString()!.Where(c => !char.IsControl(c)).ToArray()) is var t && t.Length > max ? t[..max] : t : null;
                                    if (!live && AS("tool", 30) is null) continue;   // un mensaje del agente (texto libre) solo se enseña mientras trabaja
                                    acts.Add(new System.Text.Json.Nodes.JsonObject { ["tool"] = AS("tool", 30), ["file"] = AS("file", 80), ["summary"] = AS("summary", 100), ["done"] = a.TryGetProperty("done", out var dv) && dv.ValueKind == System.Text.Json.JsonValueKind.True });
                                }
                            arr.Add(new System.Text.Json.Nodes.JsonObject
                            {
                                ["id"] = S("id", 16), ["provider"] = S("provider", 20), ["project"] = S("project", 60), ["state"] = state,
                                ["tool"] = S("tool", 30), ["file"] = S("file", 80),
                                ["message"] = live ? S("message", 110) : null,   // el último mensaje del agente solo se enseña mientras trabaja
                                ["last"] = last,
                                ["title"] = S("title", 80), ["branch"] = S("branch", 60), ["contextTokens"] = N("contextTokens"), ["added"] = N("added"), ["removed"] = N("removed"),
                                ["repeat"] = N("repeat"), ["repeatSubject"] = S("repeatSubject", 60), ["completed"] = S("completed", 60),
                                ["stalled"] = B("stalled"), ["canResume"] = B("canResume"), ["activity"] = acts,
                            });
                        }
                }
            }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException) { }
            await Reply(ctx, 200, System.Text.Encoding.UTF8.GetBytes(Json(new System.Text.Json.Nodes.JsonObject { ["items"] = arr })));
        });
        // Estado de Team (lo publica la app en team-state.json; si no se renueva en 60 s la app dejo de correr).
        app.MapGet("/v1/team", async ctx =>
        {
            string body = "{}";
            try
            {
                if (o.TeamPath is not null && File.Exists(o.TeamPath) && new FileInfo(o.TeamPath).Length < 120_000)
                {
                    var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(o.TeamPath));
                    if (root is System.Text.Json.Nodes.JsonObject obj && obj["ts"]?.GetValue<long>() is long ts && DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(ts) <= TimeSpan.FromSeconds(60)) body = Json(obj);
                }
            }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException or InvalidOperationException or FormatException) { }
            await Reply(ctx, 200, System.Text.Encoding.UTF8.GetBytes(body));
        });
        // Acciones del usuario sobre Team y sobre una sesion terminada. Solo desde el celular, con CSRF; fusionar o descartar ramas NO existe aqui.
        app.MapPost("/v1/team/action", async ctx =>
        {
            if (o.Bridge is null || o.Policy is null || !Phone(ctx) || await Body(ctx) is not { } b) { if (o.Bridge is null || !Phone(ctx)) await Reply(ctx, 403, PetProtocol.Error("forbidden", "solo desde el celular")); return; }
            string act = b.TryGetProperty("action", out var av) && av.ValueKind == System.Text.Json.JsonValueKind.String ? av.GetString()! : "";
            if (act is not ("cancel" or "approve" or "reject" or "remove")) { await Reply(ctx, 400, PetProtocol.Error("bad_json", "accion desconocida")); return; }
            if (o.Policy.Paused || (act is "approve" or "reject" && !o.Policy.ApprovalsEnabled)) { await Reply(ctx, 403, PetProtocol.Error("forbidden", "Nimbo esta en pausa o las decisiones estan desactivadas")); return; }
            var args = new System.Text.Json.Nodes.JsonObject();
            if (b.TryGetProperty("text", out var tv) && tv.ValueKind == System.Text.Json.JsonValueKind.String && tv.GetString()!.Length <= 1500) args["text"] = tv.GetString();
            if (b.TryGetProperty("index", out var iv) && iv.TryGetInt32(out var idx) && idx is >= 0 and < 10) args["index"] = idx;
            var (ok, msg) = await o.Bridge.CallAsync("team." + act, args, ctx.RequestAborted);
            Log("team." + act, "Equipo: " + act, ok ? "Done: " + msg : "Failed: " + msg);
            await Reply(ctx, ok ? 200 : 409, ok ? "{}"u8.ToArray() : PetProtocol.Error("not_found", msg));
        });
        // Lanzar una tarea desde la caja de chat del celular (modo Equipo): pasa por el mismo broker que el chat, así que pide confirmacion en cualquier canal.
        // Un archivo del celular para una tarea del equipo: se guarda en uploads\<id>\<nombre> hasta que se lance (o 2 horas). Lista cerrada de extensiones y 10 MB.
        app.MapPost("/v1/team/attach", async ctx =>
        {
            if (o.UploadsPath is null || o.Policy is null || !Phone(ctx)) { await Reply(ctx, 403, PetProtocol.Error("forbidden", "solo desde el celular")); return; }
            if (o.Policy.Paused) { await Reply(ctx, 403, PetProtocol.Error("forbidden", "Nimbo esta en pausa")); return; }
            // La subida necesita su propio tope; el resto de peticiones conserva el límite pequeño del protocolo.
            ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize = UploadMax;
            string name = new string((ctx.Request.Query["name"].ToString() ?? "").Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or ' ').ToArray()).Trim(' ', '.');
            if (name.Length > 60) name = name[^60..];
            string ext = Path.GetExtension(name).ToLowerInvariant();
            if (name.Length == 0 || !UploadExt.Contains(ext)) { await Reply(ctx, 415, PetProtocol.Error("bad_json", "tipo de archivo no permitido")); return; }
            var (data, tooBig) = await ReadBody(ctx.Request.Body, ctx.RequestAborted, UploadMax);
            if (tooBig) { await Reply(ctx, 413, PetProtocol.Error("too_large", "maximo 10 MB por archivo")); return; }
            if (data.Length == 0) { await Reply(ctx, 422, PetProtocol.Error("bad_json", "archivo vacio")); return; }
            Directory.CreateDirectory(o.UploadsPath);
            foreach (var old in Directory.GetDirectories(o.UploadsPath)) { try { if (DateTime.UtcNow - Directory.GetCreationTimeUtc(old) > TimeSpan.FromHours(2)) Directory.Delete(old, true); } catch (IOException) { } }
            if (Directory.GetDirectories(o.UploadsPath).Length >= 20) { await Reply(ctx, 429, PetProtocol.Error("busy", "demasiados adjuntos pendientes")); return; }
            string id = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            Directory.CreateDirectory(Path.Combine(o.UploadsPath, id));
            await File.WriteAllBytesAsync(Path.Combine(o.UploadsPath, id, name), data);
            await Reply(ctx, 200, System.Text.Encoding.UTF8.GetBytes(Json(new System.Text.Json.Nodes.JsonObject { ["id"] = id, ["name"] = name, ["size"] = data.Length })));
        });
        app.MapPost("/v1/team/launch", async ctx =>
        {
            if (broker is null || o.Policy is null || !Phone(ctx) || await Body(ctx) is not { } b) { if (broker is null || !Phone(ctx)) await Reply(ctx, 403, PetProtocol.Error("forbidden", "solo desde el celular")); return; }
            if (o.Policy.Paused) { await Reply(ctx, 403, PetProtocol.Error("forbidden", "Nimbo esta en pausa")); return; }
            string? Str(string k) => b.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;
            var args = new System.Text.Json.Nodes.JsonObject();
            if (Str("task")?.Trim() is { Length: > 0 and <= 300 } task && !task.Any(c => char.IsControl(c) && c is not ('\n' or '\t'))) args["task"] = task; else { await Reply(ctx, 422, PetProtocol.Error("bad_json", "la tarea debe tener de 1 a 300 caracteres")); return; }
            if (Str("root") is { Length: > 0 } root) args["root"] = root;
            if (Str("preset") is { Length: > 0 } preset) args["preset"] = preset;
            if (Str("effort") is { Length: > 0 } effort) args["effort"] = effort;
            if (b.TryGetProperty("attachments", out var atv) && atv.ValueKind == System.Text.Json.JsonValueKind.Array)
                args["attachments"] = new System.Text.Json.Nodes.JsonArray(atv.EnumerateArray().Where(x => x.ValueKind == System.Text.Json.JsonValueKind.String).Take(6).Select(x => (System.Text.Json.Nodes.JsonNode)x.GetString()!).ToArray());
            if (b.TryGetProperty("review_plan", out var rpv) && rpv.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False) args["review_plan"] = rpv.GetBoolean();
            var el = System.Text.Json.JsonDocument.Parse(args.ToJsonString()).RootElement.Clone();
            if (broker.Tools.Find("team.run")?.Validate(el) is { } why) { await Reply(ctx, 422, PetProtocol.Error("bad_json", why)); return; }
            // La confirmacion puede tardar hasta 2 minutos: se responde ya y el resultado llega como aviso.
            _ = Task.Run(async () =>
            {
                var res = await broker.ExecuteAsync("team.run", el, "phone", false, CancellationToken.None);
                eventsHub.Publish(PetProtocol.Notice(res.Status switch
                {
                    BrokerStatus.Done => "Equipo: " + res.Message,
                    BrokerStatus.Denied => "No lanzaste la tarea.",
                    BrokerStatus.Expired => "No confirmaste a tiempo: la tarea no se lanzó.",
                    _ => "No pude lanzarla: " + res.Message,
                }, res.Status == BrokerStatus.Done ? "info" : "attention"));
            });
            await Reply(ctx, 202, "{\"pending\":true}"u8.ToArray());
        });
        app.MapPost("/v1/agent/resume", async ctx =>
        {
            if (o.Bridge is null || o.Policy is null || !Phone(ctx) || await Body(ctx) is not { } b) { if (o.Bridge is null || !Phone(ctx)) await Reply(ctx, 403, PetProtocol.Error("forbidden", "solo desde el celular")); return; }
            string id = b.TryGetProperty("id", out var iv) && iv.ValueKind == System.Text.Json.JsonValueKind.String ? iv.GetString()! : "";
            if (id.Length is 0 or > 16 || !id.All(Uri.IsHexDigit) || o.Policy.Paused) { await Reply(ctx, 400, PetProtocol.Error("bad_json", "id invalido o Nimbo en pausa")); return; }
            var (ok, msg) = await o.Bridge.CallAsync("agent.resume", new System.Text.Json.Nodes.JsonObject { ["id"] = id }, ctx.RequestAborted);
            Log("agent.resume", "Retomar una sesion en la terminal de la PC", ok ? "Done: " + msg : "Failed: " + msg);
            await Reply(ctx, ok ? 200 : 409, ok ? "{}"u8.ToArray() : PetProtocol.Error("not_found", msg));
        });
        // Agentes propios (team\agents.json de la app): se editan desde el celular; ejecutarlos lo hace Team, con confirmacion.
        app.MapGet("/v1/agent-profiles", async ctx =>
        {
            var root = new System.Text.Json.Nodes.JsonObject { ["items"] = o.Profiles?.List() ?? [], ["tools"] = new System.Text.Json.Nodes.JsonArray(AgentProfilesFile.Tools.Select(t => (System.Text.Json.Nodes.JsonNode)t).ToArray()) };
            await Reply(ctx, 200, System.Text.Encoding.UTF8.GetBytes(Json(root)));
        });
        app.MapPut("/v1/agent-profiles/{id}", async (HttpContext ctx, string id) =>
        {
            if (o.Profiles is null || o.Policy is null || !Phone(ctx)) { await Reply(ctx, 403, PetProtocol.Error("forbidden", "solo desde el celular")); return; }
            if (o.Policy.Paused) { await Reply(ctx, 403, PetProtocol.Error("forbidden", "Nimbo esta en pausa")); return; }
            var (raw, tooBig) = await ReadBody(ctx.Request.Body, ctx.RequestAborted, AgentProfilesFile.MaxBytes);
            if (tooBig) { await Reply(ctx, 400, PetProtocol.Error("too_large", "maximo 16 KB")); return; }
            System.Text.Json.Nodes.JsonObject? p;
            try { p = System.Text.Json.Nodes.JsonNode.Parse(raw) as System.Text.Json.Nodes.JsonObject; } catch (System.Text.Json.JsonException) { p = null; }
            if (p is null) { await Reply(ctx, 400, PetProtocol.Error("bad_json", "json invalido")); return; }
            var why = o.Profiles.Put(id, p);
            if (why is not null) { await Reply(ctx, 422, PetProtocol.Error("bad_json", why)); return; }
            Log("agent.profile.save", "Agente propio guardado: " + id, "Done desde la pestaña Agentes");
            await Reply(ctx, 200, "{}"u8.ToArray());
        });
        app.MapDelete("/v1/agent-profiles/{id}", async (HttpContext ctx, string id) =>
        {
            if (o.Profiles is null || o.Policy is null || !Phone(ctx)) { await Reply(ctx, 403, PetProtocol.Error("forbidden", "solo desde el celular")); return; }
            if (o.Policy.Paused) { await Reply(ctx, 403, PetProtocol.Error("forbidden", "Nimbo esta en pausa")); return; }
            var ok = o.Profiles.Delete(id);
            if (ok) Log("agent.profile.delete", "Agente propio borrado: " + id, "Done desde la pestaña Agentes");
            await Reply(ctx, ok ? 200 : 404, ok ? "{}"u8.ToArray() : PetProtocol.Error("not_found", "no existe"));
        });
        app.MapGet("/v1/push/key", async ctx =>
            await Reply(ctx, 200, System.Text.Encoding.UTF8.GetBytes(Json(new System.Text.Json.Nodes.JsonObject { ["key"] = o.Push?.PublicKey, ["subscribed"] = o.Push?.Count ?? 0 }))));
        app.MapPost("/v1/push/subscribe", async ctx =>
        {
            if (o.Push is null || !Phone(ctx) || await Body(ctx) is not { } b) { if (o.Push is null || !Phone(ctx)) await Reply(ctx, 403, PetProtocol.Error("forbidden", "solo desde el celular")); return; }
            string ep = b.TryGetProperty("endpoint", out var e) && e.ValueKind == System.Text.Json.JsonValueKind.String ? e.GetString()! : "";
            string p = "", a = "";
            if (b.TryGetProperty("keys", out var k) && k.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                p = k.TryGetProperty("p256dh", out var pv) && pv.ValueKind == System.Text.Json.JsonValueKind.String ? pv.GetString()! : "";
                a = k.TryGetProperty("auth", out var av) && av.ValueKind == System.Text.Json.JsonValueKind.String ? av.GetString()! : "";
            }
            if (!o.Push.Add(ep, p, a)) { await Reply(ctx, 422, PetProtocol.Error("bad_json", "suscripcion no valida")); return; }
            Log("push.subscribe", "Notificaciones push activadas en un teléfono", "Done");
            _ = o.Push.SendAsync("Nimbo", "Listo: te avisaré aquí aunque tengas la web cerrada.", "/", "welcome");
            await Reply(ctx, 200, "{}"u8.ToArray());
        });
        app.MapPost("/v1/push/unsubscribe", async ctx =>
        {
            if (o.Push is null || !Phone(ctx) || await Body(ctx) is not { } b) return;
            if (b.TryGetProperty("endpoint", out var e) && e.ValueKind == System.Text.Json.JsonValueKind.String) o.Push.Remove(e.GetString()!);
            await Reply(ctx, 200, "{}"u8.ToArray());
        });
        app.MapGet("/v1/reminders", async ctx =>
        {
            var arr = new System.Text.Json.Nodes.JsonArray();
            foreach (var r in o.Store?.ListReminders() ?? [])
                arr.Add(new System.Text.Json.Nodes.JsonObject { ["id"] = r.Id, ["text"] = r.Text, ["due"] = r.Due, ["repeat"] = r.Repeat, ["every_min"] = r.EveryMin, ["fired"] = r.Fired, ["fired_ts"] = r.FiredTs });
            await Reply(ctx, 200, System.Text.Encoding.UTF8.GetBytes(Json(new System.Text.Json.Nodes.JsonObject { ["items"] = arr, ["seq"] = o.Store?.MaxFiredSeq() ?? 0 })));
        });
        app.MapPost("/v1/reminders", async ctx =>
        {
            if (o.Store is null || await Body(ctx) is not { } b) return;
            string say = b.TryGetProperty("say", out var sv) && sv.ValueKind == System.Text.Json.JsonValueKind.String ? sv.GetString()! : "";
            var parsed = say.Length is > 0 and <= 300 ? ReminderParser.Parse("recuérdame " + say, DateTimeOffset.UtcNow) : null;
            if (parsed is null) { await Reply(ctx, 422, PetProtocol.Error("bad_json", "no entendí la hora: prueba «a las 5 pm llamar a mamá» o «en 20 minutos revisar el horno»")); return; }
            long id = o.Store.AddReminder(parsed.Text, parsed.Due.ToUnixTimeSeconds(), parsed.Repeat, parsed.EveryMin);
            Log("reminder.add", "Recordar «" + parsed.Text + "» " + ReminderParser.Describe(parsed.Due, DateTimeOffset.UtcNow), "Done desde la pestaña Agenda");
            await Reply(ctx, 200, System.Text.Encoding.UTF8.GetBytes(Json(new System.Text.Json.Nodes.JsonObject { ["id"] = id, ["text"] = parsed.Text, ["when"] = ReminderParser.Describe(parsed.Due, DateTimeOffset.UtcNow) })));
        });
        app.MapDelete("/v1/reminders/{id:long}", async (HttpContext ctx, long id) => { o.Store?.DeleteReminder(id); await Reply(ctx, 200, "{}"u8.ToArray()); });
        app.MapGet("/v1/memory", async ctx =>
        {
            var arr = new System.Text.Json.Nodes.JsonArray();
            foreach (var m in o.Store?.Memories() ?? []) arr.Add(new System.Text.Json.Nodes.JsonObject { ["id"] = m.Id, ["text"] = m.Text, ["ts"] = m.Ts });
            await Reply(ctx, 200, System.Text.Encoding.UTF8.GetBytes(Json(new System.Text.Json.Nodes.JsonObject { ["items"] = arr })));
        });
        app.MapPost("/v1/memory", async ctx =>
        {
            if (o.Store is null || await Body(ctx) is not { } b) return;
            string text = b.TryGetProperty("text", out var tv) && tv.ValueKind == System.Text.Json.JsonValueKind.String ? tv.GetString()!.Trim() : "";
            if (text.Length is 0 or > MemoryRememberTool.MaxChars || text.Any(char.IsControl) || o.Store.Memories().Count >= MemoryRememberTool.MaxMemories) { await Reply(ctx, 422, PetProtocol.Error("bad_json", "texto vacío, demasiado largo o ya hay demasiados recuerdos")); return; }
            long id = o.Store.AddMemory(text, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            Log("memory.remember", "Recordar: «" + text + "»", "Done desde la pestaña Memoria");
            await Reply(ctx, 200, System.Text.Encoding.UTF8.GetBytes(Json(new System.Text.Json.Nodes.JsonObject { ["id"] = id })));
        });
        app.MapPut("/v1/memory/{id:long}", async (HttpContext ctx, long id) =>
        {
            if (o.Store is null || await Body(ctx) is not { } b) return;
            string text = b.TryGetProperty("text", out var tv) && tv.ValueKind == System.Text.Json.JsonValueKind.String ? tv.GetString()!.Trim() : "";
            if (text.Length is 0 or > MemoryRememberTool.MaxChars || text.Any(char.IsControl)) { await Reply(ctx, 422, PetProtocol.Error("bad_json", "texto vacío o demasiado largo")); return; }
            o.Store.EditMemory(id, text);
            await Reply(ctx, 200, "{}"u8.ToArray());
        });
        app.MapDelete("/v1/memory/{id:long}", async (HttpContext ctx, long id) =>
        {
            o.Store?.DeleteMemory(id);
            Log("memory.forget", "Olvidar un recuerdo", "Done desde la pestaña Memoria");
            await Reply(ctx, 200, "{}"u8.ToArray());
        });

        app.MapPost("/v1/chat", async ctx =>
        {
            var (body, tooBig) = await ReadBody(ctx.Request.Body, ctx.RequestAborted);
            if (tooBig) { await Reply(ctx, 400, PetProtocol.Error("too_large", "maximo 4096 bytes")); return; }
            var r = PetProtocol.Parse(body);
            if (r.Error is not null) { await Reply(ctx, PetProtocol.HttpStatus(r.Error.Code), PetProtocol.Error(r.Error.Code, r.Error.Message)); return; }
            if (r.Message is not ChatMsg m) { await Reply(ctx, 400, PetProtocol.Error("bad_json", "se esperaba un mensaje chat")); return; }
            var (id, err) = coordinator.Enqueue(m.Text, m.Attachments);
            if (id is null) await Reply(ctx, PetProtocol.HttpStatus(err!), PetProtocol.Error(err!, err == "busy" ? "ya hay una conversacion en curso" : "no se pudo aceptar"));
            else await Reply(ctx, 202, PetProtocol.ChatAccepted(id));
        });

        app.MapPost("/v1/chat/{id}/cancel", async (HttpContext ctx, string id) =>
        {
            if (!PetProtocol.IsId(id) || !coordinator.Cancel(id)) { await Reply(ctx, 404, PetProtocol.Error("not_found", "chat desconocido")); return; }
            await Reply(ctx, 200, PetProtocol.ChatCancelled());
        });

        app.Map("/v1/events", async ctx =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest) { await Reply(ctx, 400, PetProtocol.Error("bad_json", "se esperaba WebSocket")); return; }
            using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            await Serve(ws, o.Service, eventsHub, coordinator, o.Agents, broker, approvals, o.Approvals, o.Policy, o.Store, o.WebPort is not null && ctx.Connection.LocalPort == webLocal ? "phone" : "board", ctx.RequestAborted);
        });

        app.Lifetime.ApplicationStopping.Register(() => coordinator.Dispose());
        return app;
    }

    private static byte[] PolicyJson(PolicyStore policy, ActionBroker? broker)
    {
        var tools = new System.Text.Json.Nodes.JsonArray();
        void Add(string name, string title, ToolRisk risk) => tools.Add(new System.Text.Json.Nodes.JsonObject
        {
            ["name"] = name, ["title"] = title, ["risk"] = risk.ToString().ToLowerInvariant(),
            ["mode"] = policy.Override(name)?.ToString().ToLowerInvariant() ?? "default",
            ["default"] = PolicyStore.DefaultFor(risk).ToString().ToLowerInvariant(),
        });
        Add("agent.decide", "Aprobar permisos de agentes desde aquí", ToolRisk.Sensitive);
        if (broker is not null) foreach (var t in broker.Tools.All) Add(t.Spec.Name, t.Spec.Title, t.Spec.Risk);
        return System.Text.Encoding.UTF8.GetBytes(new System.Text.Json.Nodes.JsonObject
        {
            ["paused"] = policy.Paused, ["strict"] = policy.Strict, ["risky_from_phone"] = policy.RiskyFromPhone, ["quiet"] = policy.Quiet, ["tools"] = tools,
        }.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    private const string Cookie = "its_pet";

    private static async Task WebGate(HttpContext ctx, Func<Task> next, PetHostOptions o, SessionStore sessions)
    {
        var req = ctx.Request;
        string path = req.Path.Value ?? "/";
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
        ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'unsafe-inline'; script-src 'self' 'unsafe-inline'; connect-src 'self' ws: wss:; img-src 'self' data: blob:; worker-src 'self'; manifest-src 'self'";
        if (HttpMethods.IsGet(req.Method) && path == "/") { await Page(ctx); return; }
        // La página que abre `pair` en esta PC (con su nonce secreto): solo se sirve a quien llega directo, nunca por Tailscale ni por un proxy.
        if (HttpMethods.IsGet(req.Method) && path.StartsWith("/pairing/", StringComparison.Ordinal))
        {
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            bool direct = !req.Headers.Keys.Any(k => k.StartsWith("Tailscale-", StringComparison.OrdinalIgnoreCase) || k.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase) || k.Equals("Forwarded", StringComparison.OrdinalIgnoreCase))
                && System.Net.IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress ?? System.Net.IPAddress.None);
            string? state = direct && parts.Length is 2 or 3 ? o.Pairing?.Status(parts[1]) : null;
            if (state is null) { ctx.Response.StatusCode = 404; return; }
            ctx.Response.Headers.CacheControl = "no-store";
            if (parts.Length == 3 && parts[2] == "status") { await Reply(ctx, 200, System.Text.Encoding.UTF8.GetBytes($"{{\"state\":\"{state}\"}}")); return; }
            if (parts.Length == 2 && o.PairPagePath is { } pp && File.Exists(pp))
            {
                ctx.Response.ContentType = "text/html; charset=utf-8";
                await ctx.Response.Body.WriteAsync(await File.ReadAllBytesAsync(pp));
                return;
            }
            ctx.Response.StatusCode = 404;
            return;
        }
        if (HttpMethods.IsGet(req.Method) && path is "/sw.js" or "/jsqr.js" or "/manifest.webmanifest" or "/logo-180.png" or "/logo-512.png") { await Asset(ctx, path[1..]); return; }
        // Origin (si viene) debe ser este mismo host: bloquea CSRF y WebSockets de otros sitios.
        if (req.Headers.Origin.Count > 0 && !(Uri.TryCreate(req.Headers.Origin[0], UriKind.Absolute, out var origin) && origin.Authority == req.Host.Value))
        { await Reply(ctx, 403, PetProtocol.Error("forbidden", "origen no permitido")); return; }

        if (HttpMethods.IsPost(req.Method) && path == "/web/pair")
        {
            var (body, tooBig) = await ReadBody(req.Body, ctx.RequestAborted);
            string? code = null;
            try { code = tooBig ? null : System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("code").GetString(); } catch (Exception e) when (e is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException) { }
            if (o.Pairing is null || !o.Pairing.Redeem(code)) { await Reply(ctx, 401, PetProtocol.Error("unauthorized", "codigo invalido o caducado")); return; }
            var (id, csrf) = sessions.Create();
            ctx.Response.Cookies.Append(Cookie, id, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, MaxAge = SessionStore.Lifetime, Secure = req.IsHttps || req.Headers["X-Forwarded-Proto"] == "https", Path = "/" });
            await Reply(ctx, 200, System.Text.Encoding.UTF8.GetBytes($"{{\"csrf\":\"{csrf}\"}}"));
            return;
        }

        string? sid = req.Cookies[Cookie];
        string? token = sessions.Csrf(sid);
        if (token is null) { await Reply(ctx, 401, PetProtocol.Error("unauthorized", "sesion no valida")); return; }
        if (HttpMethods.IsGet(req.Method) && path == "/web/session") { await Reply(ctx, 200, System.Text.Encoding.UTF8.GetBytes($"{{\"csrf\":\"{token}\"}}")); return; }
        if (HttpMethods.IsPost(req.Method) && path == "/web/logout")
        {
            if (req.Headers["X-CSRF"] != token) { await Reply(ctx, 403, PetProtocol.Error("forbidden", "csrf")); return; }
            sessions.Remove(sid); ctx.Response.Cookies.Delete(Cookie); await Reply(ctx, 200, "{}"u8.ToArray()); return;
        }
        if (!path.StartsWith("/v1/", StringComparison.Ordinal)) { ctx.Response.StatusCode = 404; return; }
        if (!HttpMethods.IsGet(req.Method) && req.Headers["X-CSRF"] != token) { await Reply(ctx, 403, PetProtocol.Error("forbidden", "csrf")); return; }
        await next();
    }

    private static async Task Asset(HttpContext ctx, string name)
    {
        using var s = typeof(PetHost).Assembly.GetManifestResourceStream(name);
        if (s is null) { ctx.Response.StatusCode = 404; return; }
        ctx.Response.ContentType = name.EndsWith(".js") ? "text/javascript; charset=utf-8" : name.EndsWith(".png") ? "image/png" : "application/manifest+json; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-cache";   // el ícono y el manifiesto se revalidan siempre (iOS los guarda al añadir la web a la pantalla de inicio)
        if (name == "sw.js") ctx.Response.Headers["Service-Worker-Allowed"] = "/";
        await s.CopyToAsync(ctx.Response.Body);
    }

    private static async Task Page(HttpContext ctx)
    {
        using var s = typeof(PetHost).Assembly.GetManifestResourceStream("web.html")!;
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-store";
        await s.CopyToAsync(ctx.Response.Body);
    }

    private const int UploadMax = 10 * 1024 * 1024;
    private static readonly HashSet<string> UploadExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".pdf", ".txt", ".md", ".json", ".csv", ".log", ".xml", ".yaml", ".yml", ".toml", ".ini", ".cs", ".xaml", ".py", ".js", ".ts", ".tsx", ".jsx", ".html", ".css", ".sql", ".sh", ".ps1", ".cpp", ".c", ".h", ".java", ".go", ".rs", ".kt", ".swift",
    };

    private static async Task Reply(HttpContext ctx, int status, byte[] json)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-store";
        await ctx.Response.Body.WriteAsync(json);
    }

    private static async Task<(byte[] Body, bool TooBig)> ReadBody(Stream s, CancellationToken ct, int max = PetProtocol.MaxMessageBytes)
    {
        var buf = new byte[max + 1];
        int n = 0, r;
        while (n < buf.Length && (r = await s.ReadAsync(buf.AsMemory(n), ct)) > 0) n += r;
        return (buf[..Math.Min(n, max + 1)], n > max);
    }

    private static async Task Serve(WebSocket ws, PetService svc, EventsHub hub, ChatCoordinator chat, AgentBridge? agents, ActionBroker? broker, ApprovalService? approvals, ApprovalFeed? feed, PolicyStore? policy, SqlitePetStore? store, string channel, CancellationToken ct)
    {
        var client = new EventsHub.Client(ws);
        Guid id = hub.Add(client);
        var writer = Task.Run(async () =>
        {
            try { await foreach (var msg in client.Out.Reader.ReadAllAsync(ct)) await ws.SendAsync(msg, WebSocketMessageType.Text, true, ct); }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException) { }
        }, ct);
        var buf = new byte[PetProtocol.MaxMessageBytes + 1];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                int n = 0;
                ValueWebSocketReceiveResult res;
                do
                {
                    res = await ws.ReceiveAsync(buf.AsMemory(n), ct);
                    if (res.MessageType == WebSocketMessageType.Close)
                    {
                        try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); } catch (WebSocketException) { }
                        goto done;
                    }
                    n += res.Count;
                    if (n > PetProtocol.MaxMessageBytes)
                    {
                        client.Out.Writer.TryWrite(PetProtocol.Error("too_large", "maximo 4096 bytes"));
                        await Task.Delay(50, CancellationToken.None);
                        await ws.CloseOutputAsync(WebSocketCloseStatus.MessageTooBig, "too_large", CancellationToken.None);
                        goto done;
                    }
                } while (!res.EndOfMessage);

                var parsed = PetProtocol.Parse(buf.AsSpan(0, n));
                if (parsed.Error is not null) { client.Out.Writer.TryWrite(PetProtocol.Error(parsed.Error.Code, parsed.Error.Message)); continue; }
                switch (parsed.Message)
                {
                    case HelloMsg hm:
                        if (hm.SinceReminder >= 0 && store is not null)   // avisos que sonaron mientras este cliente no estaba conectado
                            foreach (var (seq, rr) in store.FiredSince(hm.SinceReminder, channel == "board" ? 600 : 86400, DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
                                client.Out.Writer.TryWrite(PetProtocol.Reminder(rr, true, seq));
                        client.Out.Writer.TryWrite(PetProtocol.State(svc.Snapshot));
                        if (agents is { Current: not "idle" } a2) client.Out.Writer.TryWrite(PetProtocol.Agents(a2.Current));
                        if (feed is not null) foreach (var a in feed.Current) client.Out.Writer.TryWrite(PetProtocol.Approval(a, a.Risk is null || policy?.RiskyFromPhone == true));
                        if (broker is not null) foreach (var p in broker.Pending) client.Out.Writer.TryWrite(PetProtocol.Confirm(p));   // quien llega tarde ve lo pendiente
                        break;
                    case PingMsg: client.Out.Writer.TryWrite(PetProtocol.Pong()); break;
                    case ActionMsg a:
                        var r = svc.Apply(a);  // Changed difunde el `state` nuevo a todos
                        client.Out.Writer.TryWrite(PetProtocol.Ack(a.Id, r.Rev));
                        break;
                    case ChatMsg c:  // mismo efecto que POST /v1/chat; la respuesta llega por este canal
                        var (chatId, err) = chat.Enqueue(c.Text, c.Attachments);
                        client.Out.Writer.TryWrite(chatId is null
                            ? PetProtocol.Error(err!, err == "busy" ? "ya hay una conversacion en curso" : "no se pudo aceptar")
                            : PetProtocol.ChatAccepted(chatId));
                        break;
                    case DecisionMsg d when approvals is not null && feed?.Find(d.Ref) is not null:  // peticion de un agente
                    {
                        var (ok, code, text) = await approvals.DecideAsync(d.Ref, d.Verdict, d.Note, d.Answers, channel, ct);
                        client.Out.Writer.TryWrite(ok ? PetProtocol.Ack(d.Id, svc.Snapshot.Rev) : PetProtocol.Error(code ?? "internal", text));
                        break;
                    }
                    case DecisionMsg d:  // gana la primera respuesta; un id que ya no esta pendiente se ignora
                        client.Out.Writer.TryWrite(broker is not null && broker.Resolve(d.Ref, d.Verdict, channel) ? PetProtocol.Ack(d.Id, svc.Snapshot.Rev) : PetProtocol.Error("not_found", "ya no esta pendiente"));
                        break;
                    case CancelMsg x:
                        client.Out.Writer.TryWrite(chat.Cancel(x.ChatId) ? PetProtocol.ChatCancelled() : PetProtocol.Error("not_found", "chat desconocido"));
                        break;
                    default: client.Out.Writer.TryWrite(PetProtocol.Error("bad_json", "mensaje no permitido en este canal")); break;
                }
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException) { }
    done:
        hub.Remove(id);
        try { await writer.WaitAsync(TimeSpan.FromSeconds(1)); } catch (Exception) { }
    }
}
