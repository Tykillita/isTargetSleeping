using System.Text;
using System.Text.Json;
using IsTargetSleeping;
using IsTargetSleeping.Pet;

// Banco de pruebas del complemento Mascota conectada: protocolo, estado autoritativo y SQLite.
// Salida: "N comprobaciones, M fallos"; codigo de salida 1 si hay fallos.
int checks = 0, failed = 0;
void Check(bool ok, string what)
{
    checks++;
    if (!ok) { failed++; Console.WriteLine("FALLO: " + what); }
}

static byte[] B(string s) => Encoding.UTF8.GetBytes(s);
const string Id1 = "5b2f0c9a41d34e7f8a3c6d1e90ab12cd";
static string NewId(int n) => n.ToString("x32");

// ------------------------------------------------------------------ protocolo: entrada
{
    var r = PetProtocol.Parse(B($$"""{"v":1,"type":"hello","id":"{{Id1}}","since_rev":7}"""));
    Check(r.Message is HelloMsg { SinceRev: 7 }, "hello valido");

    r = PetProtocol.Parse(B($$"""{"v":1,"type":"action","id":"{{Id1}}","kind":"tap","age_s":0}"""));
    Check(r.Message is ActionMsg { Kind: "tap", AgeS: 0 }, "accion tap valida");

    r = PetProtocol.Parse(B($$"""{"v":1,"type":"action","id":"{{Id1}}","kind":"rename","age_s":2,"name":"Luna"}"""));
    Check(r.Message is ActionMsg { Name: "Luna" }, "rename valido");

    r = PetProtocol.Parse(B($$"""{"v":1,"type":"action","id":"{{Id1}}","kind":"rename","age_s":2,"name":"Nombre demasiado largo"}"""));
    Check(r.Error?.Code == "bad_json", "rename con nombre largo se rechaza");

    r = PetProtocol.Parse(B($$"""{"v":1,"type":"ping","id":"{{Id1}}"}"""));
    Check(r.Message is PingMsg, "ping");

    r = PetProtocol.Parse(B($$"""{"v":1,"type":"cancel","id":"{{Id1}}","chat_id":"{{Id1}}"}"""));
    Check(r.Message is CancelMsg { ChatId: Id1 }, "cancel valido");
    r = PetProtocol.Parse(B($$"""{"v":1,"type":"cancel","id":"{{Id1}}","chat_id":"xx"}"""));
    Check(r.Error?.Code == "bad_json", "cancel con chat_id invalido");

    r = PetProtocol.Parse(B("{no es json"));
    Check(r.Error?.Code == "bad_json", "JSON invalido");

    r = PetProtocol.Parse(B("[1,2,3]"));
    Check(r.Error?.Code == "bad_json", "no es un objeto");

    r = PetProtocol.Parse(B($$"""{"v":2,"type":"ping","id":"{{Id1}}"}"""));
    Check(r.Error?.Code == "bad_version", "version distinta");

    r = PetProtocol.Parse(B("""{"v":1,"type":"ping","id":"corto"}"""));
    Check(r.Error?.Code == "bad_json", "id invalido");

    r = PetProtocol.Parse(B($$"""{"v":1,"type":"action","id":"{{Id1}}","kind":"vuela","age_s":0}"""));
    Check(r.Error?.Code == "bad_json", "accion desconocida");

    r = PetProtocol.Parse(B($$"""{"v":1,"type":"action","id":"{{Id1}}","kind":"tap","age_s":-1}"""));
    Check(r.Error?.Code == "bad_json", "age_s negativo");

    r = PetProtocol.Parse(B($$"""{"v":1,"type":"raro","id":"{{Id1}}"}"""));
    Check(r.Error?.Code == "bad_json", "tipo desconocido");

    // limites de tamano
    var big = new string('a', 4100);
    r = PetProtocol.Parse(B($$"""{"v":1,"type":"chat","id":"{{Id1}}","text":"{{big}}"}"""));
    Check(r.Error?.Code == "too_large", "mas de 4 KB");

    string t400 = new('a', 400), t401 = new('a', 401);
    r = PetProtocol.Parse(B($$"""{"v":1,"type":"chat","id":"{{Id1}}","text":"{{t400}}"}"""));
    Check(r.Message is ChatMsg, "chat de 400 caracteres");
    r = PetProtocol.Parse(B($$"""{"v":1,"type":"chat","id":"{{Id1}}","text":"{{t401}}"}"""));
    Check(r.Error?.Code == "text_too_long", "chat de 401 caracteres");

    // los caracteres se cuentan como puntos de codigo: 400 emojis caben (1600 bytes), 401 no
    string e400 = string.Concat(Enumerable.Repeat("😀", 400)), e401 = string.Concat(Enumerable.Repeat("😀", 401));
    r = PetProtocol.Parse(B($$"""{"v":1,"type":"chat","id":"{{Id1}}","text":"{{e400}}"}"""));
    Check(r.Message is ChatMsg, "400 emojis (puntos de codigo)");
    r = PetProtocol.Parse(B($$"""{"v":1,"type":"chat","id":"{{Id1}}","text":"{{e401}}"}"""));
    Check(r.Error?.Code == "text_too_long", "401 emojis");

    r = PetProtocol.Parse(B($$"""{"v":1,"type":"chat","id":"{{Id1}}","text":"canción, años y ¿qué tal?"}"""));
    Check(r.Message is ChatMsg { Text: "canción, años y ¿qué tal?" }, "tildes y signos en espanol intactos");

    r = PetProtocol.Parse(B($$"""{"v":1,"type":"chat","id":"{{Id1}}","text":""}"""));
    Check(r.Error is not null, "chat vacio");
}

// ------------------------------------------------------------------ protocolo: salida
{
    var snap = new PetSnapshot(42, "Nimbo", 74, 61, "happy", false);
    var s = PetProtocol.State(snap);
    using var doc = JsonDocument.Parse(s);
    var root = doc.RootElement;
    Check(root.GetProperty("type").GetString() == "state" && root.GetProperty("rev").GetInt64() == 42, "state serializa rev");
    Check(root.GetProperty("pet").GetProperty("name").GetString() == "Nimbo", "state serializa nombre");
    Check(PetProtocol.IsId(root.GetProperty("id").GetString()), "state lleva id de 128 bits");
    Check(s.Length <= PetProtocol.MaxMessageBytes, "state <= 4 KB");

    var say = PetProtocol.Say(Id1, "¡Hola! Me alegra verte, qué día tan bonito.", "happy", true);
    Check(Encoding.UTF8.GetString(say).Contains("¡Hola!"), "say conserva tildes sin escapar a \\u");
    bool threw = false;
    try { PetProtocol.Say(Id1, "hola", "enojada", true); } catch (ArgumentException) { threw = true; }
    Check(threw, "say rechaza expresiones fuera de la lista cerrada");
    threw = false;
    try { PetProtocol.Say(Id1, new string('x', 401), "open", true); } catch (ArgumentException) { threw = true; }
    Check(threw, "say rechaza texto largo");
    string max = string.Concat(Enumerable.Repeat("😀", 400));
    Check(PetProtocol.Say(Id1, max, "happy", true).Length <= PetProtocol.MaxMessageBytes, "say de 400 emojis cabe en 4 KB");

    Check(PetProtocol.HttpStatus("unauthorized") == 401 && PetProtocol.HttpStatus("busy") == 409 &&
          PetProtocol.HttpStatus("model_missing") == 503 && PetProtocol.HttpStatus("bad_json") == 400 &&
          PetProtocol.HttpStatus("text_too_long") == 400 && PetProtocol.HttpStatus("too_large") == 400, "codigos HTTP");
    Check(PetProtocol.IsValidName("Nimbo") && !PetProtocol.IsValidName(" Nimbo") && !PetProtocol.IsValidName("") &&
          !PetProtocol.IsValidName("Nombre muy largo!") && !PetProtocol.IsValidName("a\nb"), "validacion de nombre");
}

// ----------------------------------------------------------------- estado autoritativo
{
    var now = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    var svc = new PetService(new MemStore(), () => now);
    var start = svc.Snapshot;
    Check(start.Asleep && start.Name == "Nimbo" && start.Rev == 0, "estado inicial dormida, rev 0");

    var r = svc.Apply(new ActionMsg(NewId(1), "tap", 0, null));
    Check(r.Outcome == ApplyOutcome.Applied && r.Rev == 1, "tap aplica y sube rev");
    Check(!svc.Snapshot.Asleep && svc.Snapshot.Affection == 62, "tap despierta (+2 carino)");

    var dup = svc.Apply(new ActionMsg(NewId(1), "tap", 0, null));
    Check(dup.Outcome == ApplyOutcome.Duplicate && dup.Rev == 1 && svc.Snapshot.Rev == 1, "reenviar el mismo id no cambia nada");

    svc.Apply(new ActionMsg(NewId(2), "tap", 0, null));
    Check(svc.Snapshot.Affection == 68 && svc.Snapshot.Rev == 2 && svc.Snapshot.Pose == "happy", "tap acaricia (+6) y pone happy");
    now = now.AddSeconds(3);
    Check(svc.Snapshot.Pose == "open", "la pose transitoria expira");

    svc.Apply(new ActionMsg(NewId(3), "play", 0, null));
    Check(svc.Snapshot.Energy == 77 && svc.Snapshot.Affection == 72, "jugar gasta energia y sube carino");

    svc.Apply(new ActionMsg(NewId(4), "rest", 0, null));
    Check(svc.Snapshot.Asleep && svc.Snapshot.Energy == 89 && svc.Snapshot.Pose == "sleep", "descansar duerme y recupera energia");

    svc.Apply(new ActionMsg(NewId(5), "rename", 0, "Luna"));
    Check(svc.Snapshot.Name == "Luna", "renombrar");
    var bad = svc.Apply(new ActionMsg(NewId(6), "rename", 0, "x\ny"));
    Check(bad.Outcome == ApplyOutcome.Ignored, "renombrar con nombre invalido se ignora");

    // accion vieja (placa sin conexion > 8 h): se acusa pero no toca las necesidades
    int aff = svc.Snapshot.Affection;
    var old = svc.Apply(new ActionMsg(NewId(7), "tap", 9 * 3600, null));
    Check(old.Outcome == ApplyOutcome.Applied && svc.Snapshot.Affection == aff, "accion de hace 9 h no cambia las necesidades");

    // necesidades: sin castigo y con tope
    var s2 = new PetService(new MemStore(), () => now);
    s2.Apply(new ActionMsg(NewId(100), "tap", 0, null));
    s2.Elapse(100000);
    Check(s2.Snapshot.Energy >= PetService.NeedFloor && s2.Snapshot.Affection >= PetService.NeedFloor, "las necesidades no bajan de 10");
    Check(s2.Snapshot.Energy >= 80 - PetService.MaxAbsenceMinutes / 15, "tope de ausencia de 8 h");

    // deduplicacion acotada: el id mas antiguo sale tras 256 acciones distintas
    var s3 = new PetService(new MemStore(), () => now);
    for (int i = 0; i < PetService.MaxSeen + 5; i++) s3.Apply(new ActionMsg(NewId(1000 + i), "dizzy", 0, null));
    long revNow = s3.Snapshot.Rev;
    var recent = s3.Apply(new ActionMsg(NewId(1000 + PetService.MaxSeen + 4), "dizzy", 0, null));
    Check(recent.Outcome == ApplyOutcome.Duplicate && s3.Snapshot.Rev == revNow, "los ultimos 256 ids siguen deduplicando");
    var forgotten = s3.Apply(new ActionMsg(NewId(1000), "dizzy", 0, null));
    Check(forgotten.Outcome == ApplyOutcome.Applied, "un id mas antiguo que 256 acciones ya se olvido");

    // evento
    int fired = 0;
    var s4 = new PetService(new MemStore(), () => now);
    s4.Changed += _ => fired++;
    s4.Apply(new ActionMsg(NewId(200), "tap", 0, null));
    s4.Apply(new ActionMsg(NewId(200), "tap", 0, null));
    Check(fired == 1, "Changed se emite una vez por cambio real");
}

// ------------------------------------------------------------------------- SQLite
{
    string dir = Path.Combine(AppContext.BaseDirectory, "pet-test-db");
    Directory.CreateDirectory(dir);
    string path = Path.Combine(dir, $"pet-{Guid.NewGuid():N}.db");
    var now = DateTimeOffset.UtcNow;
    {
        using var store = new SqlitePetStore(path);
        var svc = new PetService(store, () => now);
        svc.Apply(new ActionMsg(NewId(1), "tap", 0, null));
        svc.Apply(new ActionMsg(NewId(2), "rename", 0, "Nube"));
        Check(svc.Snapshot.Rev == 2, "SQLite: dos cambios");

        store.AddMessage(Id1, "user", "Hola, ¿cómo estás?", null, 1);
        store.AddMessage(Id1, "pet", "¡Muy bien! Gracias por preguntar.", "happy", 2);
        var msgs = store.RecentMessages(10);
        Check(msgs.Count == 2 && msgs[0].Text == "Hola, ¿cómo estás?" && msgs[1].Expression == "happy", "mensajes con tildes ida y vuelta");

        long m1 = store.AddMemory("Me gusta el café", 3);
        long m2 = store.AddMemory("Mi cumpleaños es en mayo", 4);
        store.EditMemory(m1, "Me gusta el café con leche");
        store.DeleteMemory(m2);
        var mem = store.Memories();
        Check(mem.Count == 1 && mem[0].Text == "Me gusta el café con leche", "recuerdos: crear, editar, borrar");
    }
    SqliteConnectionPoolClear();
    {
        using var store = new SqlitePetStore(path);
        var svc = new PetService(store, () => now);
        Check(svc.Snapshot.Rev == 2 && svc.Snapshot.Name == "Nube" && !svc.Snapshot.Asleep, "SQLite: el estado sobrevive al reinicio");
        var dup = svc.Apply(new ActionMsg(NewId(1), "tap", 0, null));
        Check(dup.Outcome == ApplyOutcome.Duplicate, "SQLite: los ids vistos sobreviven al reinicio (sin duplicar al reconectar)");
        Check(store.RecentMessages(10).Count == 2, "SQLite: el historial sobrevive");
    }
    static void SqliteConnectionPoolClear() => Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    try { Directory.Delete(dir, true); } catch { }
}

// ------------------------------------------------------------------ agentes de la PC (AgentBridge)
{
    string dir = Path.Combine(AppContext.BaseDirectory, "pet-test-agents-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    string file = Path.Combine(dir, "agent-state.json");
    var clock = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    var bridge = new AgentBridge(file, () => clock);
    var seen = new List<string>();
    bridge.Changed += s => seen.Add(s);
    void Write(string state, int ageS = 0) => File.WriteAllText(file, $$"""{"state":"{{state}}","ts":{{clock.AddSeconds(-ageS).ToUnixTimeSeconds()}}}""");

    bridge.Poll();
    Check(bridge.Current == "idle" && seen.Count == 0, "agentes: sin archivo -> idle y sin aviso");
    Write("working"); bridge.Poll(); bridge.Poll();
    Check(bridge.Current == "working" && seen.SequenceEqual(["working"]), "agentes: working se avisa una sola vez");
    Write("attention"); bridge.Poll();
    Write("alert"); bridge.Poll();
    Check(seen.SequenceEqual(["working", "attention", "alert"]), "agentes: attention y alert");
    Write("working", ageS: 120); bridge.Poll();
    Check(bridge.Current == "idle", "agentes: un estado no renovado en 90 s caduca");
    Write("done", ageS: 5); bridge.Poll();
    Check(bridge.Current == "done", "agentes: done reciente");
    clock = clock.AddSeconds(40); bridge.Poll();
    Check(bridge.Current == "idle", "agentes: done dura 30 s");
    File.WriteAllText(file, "{roto"); bridge.Poll();
    Check(bridge.Current == "idle", "agentes: archivo roto -> idle");
    Write("explotando"); bridge.Poll();
    Check(bridge.Current == "idle", "agentes: estado desconocido -> idle");
    Check(PetProtocol.Agents("working").Length > 0 && Encoding.UTF8.GetString(PetProtocol.Agents("loquesea")).Contains("\"idle\""), "agentes: el mensaje solo lleva estados conocidos");
    try { Directory.Delete(dir, true); } catch { }
}

// ----------------------------------------------- enlace de la app principal (PetBridge -> agent-state.json)
{
    string dir = Path.Combine(AppContext.BaseDirectory, "pet-test-app-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    Environment.SetEnvironmentVariable("ITS_DATA_DIR", dir);
    string file = Path.Combine(dir, "pet", "agent-state.json");
    var t0 = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    PetBridge.ResetForTest();
    PetBridge.Publish(AgentGlowState.Working, t0);
    Check(!File.Exists(file), "enlace: sin el complemento (no existe la carpeta pet) no escribe nada");

    Directory.CreateDirectory(Path.Combine(dir, "pet"));
    PetBridge.ResetForTest();
    PetBridge.Publish(AgentGlowState.Working, t0);
    string Read() => File.ReadAllText(file);
    Check(Read().Contains("\"working\"") && Read().Contains(new DateTimeOffset(t0).ToUnixTimeSeconds().ToString()), "enlace: escribe working con la hora");
    File.Delete(file);
    PetBridge.Publish(AgentGlowState.Working, t0.AddSeconds(10));
    Check(!File.Exists(file), "enlace: el mismo estado no reescribe antes de 30 s");
    PetBridge.Publish(AgentGlowState.Working, t0.AddSeconds(31));
    Check(File.Exists(file), "enlace: latido a los 30 s");
    foreach (var (state, name) in new[] { (AgentGlowState.Attention, "attention"), (AgentGlowState.Alert, "alert"), (AgentGlowState.Done, "done"), (AgentGlowState.Off, "idle") })
    {
        PetBridge.Publish(state, t0.AddSeconds(40));
        Check(Read().Contains("\"" + name + "\""), "enlace: " + state + " -> " + name);
        t0 = t0.AddSeconds(1);
    }
    var consumer = new AgentBridge(file, () => new DateTimeOffset(t0.AddSeconds(41)));
    PetBridge.Publish(AgentGlowState.Alert, t0.AddSeconds(42));
    consumer.Poll();
    Check(consumer.Current == "alert", "enlace: el complemento lee lo que escribe la app");
    Environment.SetEnvironmentVariable("ITS_DATA_DIR", null);
    try { Directory.Delete(dir, true); } catch { }
}

// ------------------------------- reserva de actividad: la app no descarga el modelo mientras la mascota conversa
{
    string dir = Path.Combine(AppContext.BaseDirectory, "pet-test-lease2-" + Guid.NewGuid().ToString("N"));
    string pet = Path.Combine(dir, "pet");
    Directory.CreateDirectory(pet);
    Environment.SetEnvironmentVariable("ITS_DATA_DIR", dir);
    Check(!PetLease.Active, "reserva: sin archivo no hay reserva (la app descarga como siempre)");
    using (var lease = ActivityLease.Acquire(pet))
    {
        Check(PetLease.Active, "reserva: el complemento conversando -> la app lo ve activa");
        Check(PetLease.IsActive(pet, DateTimeOffset.UtcNow) == ActivityLease.IsActive(pet, DateTimeOffset.UtcNow), "reserva: app y complemento coinciden");
        Check(!PetLease.IsActive(pet, DateTimeOffset.UtcNow.AddMinutes(5)), "reserva: caduca sola si no se renueva");
    }
    Check(!PetLease.Active, "reserva: al terminar la conversacion se libera");
    PetBridge.ResetForTest();
    PetBridge.PublishUsage([("claude", "Claude", "Max", new List<(string, double, DateTimeOffset?)> { ("Semanal", 10, null) }, null, null)]);
    Check(File.Exists(Path.Combine(pet, "usage.json")), "uso: publicado");
    PetBridge.ClearUsage();
    Check(!File.Exists(Path.Combine(pet, "usage.json")), "uso: al apagar el widget se borran los datos publicados");
    File.WriteAllText(Path.Combine(pet, "lease.json"), $$"""{"pid":2147483000,"until":{{DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeSeconds()}}}""");
    Check(!PetLease.Active, "reserva: de un proceso que ya no existe no cuenta");
    File.WriteAllText(Path.Combine(pet, "lease.json"), "{roto");
    Check(!PetLease.Active, "reserva: ilegible no bloquea la descarga");
    Environment.SetEnvironmentVariable("ITS_DATA_DIR", null);
    try { Directory.Delete(dir, true); } catch { }
}

// ------------------------------- uso de IA: la app lo publica, el complemento se lo da a la mascota
{
    string dir = Path.Combine(AppContext.BaseDirectory, "pet-test-usage-" + Guid.NewGuid().ToString("N"));
    string pet = Path.Combine(dir, "pet");
    Environment.SetEnvironmentVariable("ITS_DATA_DIR", dir);
    var limits = (IReadOnlyList<(string, double, DateTimeOffset?)>)new List<(string, double, DateTimeOffset?)> { ("Semanal", 32.04, DateTimeOffset.UtcNow.AddHours(50)), ("5 h", 6, DateTimeOffset.UtcNow.AddMinutes(90)) };
    var prov = new[] { ("claude", "Claude", "Max", limits, (string?)null, (string?)null), ("codex", "Codex", "", (IReadOnlyList<(string, double, DateTimeOffset?)>)[], (string?)null, "La sesión caducó: abre Codex para renovarla.") };
    PetBridge.ResetForTest();
    PetBridge.PublishUsage(prov);
    Check(!File.Exists(Path.Combine(pet, "usage.json")), "uso: sin la carpeta del complemento no se escribe nada");
    Directory.CreateDirectory(pet);
    PetBridge.PublishUsage(prov);
    string file = Path.Combine(pet, "usage.json");
    Check(File.Exists(file), "uso: con el complemento instalado se publica");
    Check(!File.ReadAllText(file).Contains("token", StringComparison.OrdinalIgnoreCase), "uso: el archivo solo lleva porcentajes y horas");
    string? sum = UsageBridge.Summary(file, DateTimeOffset.UtcNow);
    Check(sum is not null && sum.Contains("Claude (plan Max)") && sum.Contains("usado 32 %") && sum.Contains("queda 68 %") && sum.Contains("reinicia en 2 días") && sum.Contains("reinicia en 1 h"),
          "uso: resumen con usado, queda y reinicio ya calculados");
    Check(sum is not null && sum.Contains("Codex → sin datos: La sesión caducó"), "uso: un proveedor con problema se explica en vez de callarlo");
    Check(UsageBridge.Summary(file, DateTimeOffset.UtcNow.AddHours(2)) is null, "uso: dato viejo (>45 min) no se usa");
    File.WriteAllText(file, "{roto");
    Check(UsageBridge.Summary(file, DateTimeOffset.UtcNow) is null, "uso: archivo roto -> sin dato, no revienta");
    Check(UsageBridge.Summary(Path.Combine(pet, "nada.json"), DateTimeOffset.UtcNow) is null, "uso: sin archivo -> sin dato");
    var snap2 = new PetSnapshot(1, "Nimbo", 70, 50, "open", false);
    Check(Persona.Build(snap2, DateTime.Now, [], "Claude → semanal: queda 68 %").Contains("queda 68 %"), "persona: incluye los datos de uso cuando existen");
    Check(Persona.Build(snap2, DateTime.Now, [], null).Contains("no inventes cifras"), "persona: sin datos le dice que no invente");
    Environment.SetEnvironmentVariable("ITS_DATA_DIR", null);
    try { Directory.Delete(dir, true); } catch { }
}

// ------------------------------- ordenes de la mascota a la app: lista cerrada, sin repetidos ni viejos
{
    string dir = Path.Combine(AppContext.BaseDirectory, "pet-test-cmd-" + Guid.NewGuid().ToString("N"));
    string pet = Path.Combine(dir, "pet");
    Directory.CreateDirectory(pet);
    Environment.SetEnvironmentVariable("ITS_DATA_DIR", dir);
    PetCommands.ResetForTest();
    var calls = new List<string>();
    (bool, string) Handler(string tool, JsonElement args) { calls.Add(tool + ":" + args.GetRawText()); return tool == "usage.set" ? (true, "ok") : (false, "herramienta desconocida"); }
    long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    string Put(string tool, string args, long ts, string? id = null)
    {
        id ??= PetProtocol.NewId();
        Directory.CreateDirectory(Path.Combine(pet, "commands"));
        File.WriteAllText(Path.Combine(pet, "commands", id + ".json"), "{\"id\":\"" + id + "\",\"tool\":\"" + tool + "\",\"args\":" + args + ",\"ts\":" + ts + "}");
        return id;
    }
    JsonElement? Result(string id)
    {
        string f = Path.Combine(pet, "results", id + ".json");
        return File.Exists(f) ? JsonDocument.Parse(File.ReadAllText(f)).RootElement.Clone() : null;
    }
    string a1 = Put("usage.set", "{\"on\":true}", Now());
    PetCommands.Poll(Handler);
    Check(calls.Count == 1 && Result(a1)?.GetProperty("ok").GetBoolean() == true && Directory.GetFiles(Path.Combine(pet, "commands")).Length == 0, "orden: la herramienta se ejecuta, se responde y la orden se consume");
    Put("usage.set", "{\"on\":false}", Now(), a1);
    PetCommands.Poll(Handler);
    Check(calls.Count == 1, "orden: el mismo id no se ejecuta dos veces");
    string a3 = Put("format.disk", "{}", Now());
    PetCommands.Poll(Handler);
    Check(Result(a3)?.GetProperty("ok").GetBoolean() == false, "orden: herramienta desconocida rechazada con resultado");
    string a4 = Put("usage.set", "{\"on\":true}", Now() - 120);
    PetCommands.Poll(Handler);
    Check(calls.Count == 2 /* solo la desconocida llego al manejador */ && Result(a4)?.GetProperty("ok").GetBoolean() == false && Result(a4)?.GetProperty("message").GetString() == "orden vieja", "orden: una orden vieja (>30 s) se rechaza sin ejecutar");
    File.WriteAllText(Path.Combine(pet, "commands", "roto.json"), "{roto");
    string badId = Put("usage.set", "{}", Now(), "no-es-un-id");
    PetCommands.Poll(Handler);
    Check(Directory.GetFiles(Path.Combine(pet, "commands")).Length == 0, "orden: archivos rotos o con id invalido se descartan sin romper nada");

    // canal completo: el complemento escribe, la "app" (hilo con Poll) responde
    PetCommands.ResetForTest();
    calls.Clear();
    using var stop = new CancellationTokenSource();
    var app = Task.Run(async () => { while (!stop.IsCancellationRequested) { PetCommands.Poll(Handler); await Task.Delay(50); } });
    var bridge = new FileAppBridge(pet, TimeSpan.FromSeconds(5));
    var r1 = await bridge.CallAsync("usage.set", new System.Text.Json.Nodes.JsonObject { ["on"] = true }, CancellationToken.None);
    Check(r1.Ok && calls.Count == 1, "canal: el complemento pide y la app confirma");
    var r2 = await bridge.CallAsync("format.disk", new System.Text.Json.Nodes.JsonObject(), CancellationToken.None);
    Check(!r2.Ok, "canal: una herramienta que la app no conoce vuelve como error");
    stop.Cancel();
    await app;
    Check(!(await new FileAppBridge(pet, TimeSpan.FromMilliseconds(500)).CallAsync("usage.set", new System.Text.Json.Nodes.JsonObject { ["on"] = true }, CancellationToken.None)).Ok, "canal: sin la app respondiendo -> falla por tiempo");
    Environment.SetEnvironmentVariable("ITS_DATA_DIR", null);
    try { Directory.Delete(dir, true); } catch { }
}

// ------------------------------- agente: politica de permisos y confirmaciones (primera respuesta gana)
{
    string dir = Path.Combine(AppContext.BaseDirectory, "pet-test-policy-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    string pf = Path.Combine(dir, "policy.json");
    var policy = new PolicyStore(pf);
    var usage = new UsageSetTool(new FakeBridge());
    Check(policy.Mode(usage.Spec) == ToolMode.Auto, "politica: una herramienta reversible es automatica por defecto");
    Check(policy.Mode(new ToolSpec("x", "x", ToolRisk.Sensitive, new())) == ToolMode.Ask && policy.Mode(new ToolSpec("x", "x", ToolRisk.Pc, new())) == ToolMode.Ask, "politica: lo sensible o de la PC pregunta por defecto");
    Check(policy.Mode(usage.Spec, untrusted: true) == ToolMode.Ask, "politica: lo que nace de contenido ajeno nunca es automatico");
    Check(policy.Mode(new ToolSpec("r", "r", ToolRisk.Read, new()), untrusted: true) == ToolMode.Auto, "politica: leer sigue siendo automatico");
    policy.Set("usage.set", ToolMode.Off);
    Check(policy.Mode(usage.Spec) == ToolMode.Off && new PolicyStore(pf).Mode(usage.Spec) == ToolMode.Off, "politica: un permiso apagado persiste y manda");
    policy.Set("usage.set", null);
    policy.SetStrict(true);
    Check(policy.Mode(usage.Spec) == ToolMode.Ask, "politica: el modo estricto convierte lo automatico en preguntar");
    policy.SetStrict(false);
    policy.SetPaused(true);
    Check(policy.Mode(usage.Spec) == ToolMode.Off && policy.Paused, "politica: pausar apaga todo");
    policy.SetPaused(false);
    File.WriteAllText(pf, "{roto");
    Check(policy.Paused && policy.Mode(usage.Spec) == ToolMode.Off, "politica: un archivo danado deja la mascota pausada (falla cerrado)");
    File.Delete(pf);
    Check(!policy.Paused && policy.Mode(usage.Spec) == ToolMode.Auto, "politica: sin archivo vuelven los valores por defecto");

    JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();
    var tools = new ToolRegistry().Add(usage);
    var sink = new ListSink();
    var log = new List<ActivityEntry>();
    var bridge = new FakeBridge();
    tools = new ToolRegistry().Add(new UsageSetTool(bridge));
    var broker = new ActionBroker(tools, policy, sink, log.Add, ttl: TimeSpan.FromSeconds(3));
    var auto = await broker.ExecuteAsync("usage.set", J("{\"on\":true}"), "chat", false, CancellationToken.None);
    Check(auto.Status == BrokerStatus.Done && bridge.Calls.Count == 1 && log.Count == 1 && log[0].Tool == "usage.set", "broker: automatica -> se ejecuta y queda en el registro");
    Check((await broker.ExecuteAsync("nope", J("{}"), "chat", false, CancellationToken.None)).Status == BrokerStatus.Unknown, "broker: herramienta desconocida");
    Check((await broker.ExecuteAsync("usage.set", J("{\"on\":\"si\"}"), "chat", false, CancellationToken.None)).Status == BrokerStatus.Invalid, "broker: argumentos invalidos se rechazan");
    policy.Set("usage.set", ToolMode.Off);
    Check((await broker.ExecuteAsync("usage.set", J("{\"on\":true}"), "chat", false, CancellationToken.None)).Status == BrokerStatus.Off && bridge.Calls.Count == 1, "broker: apagada -> no ejecuta");

    // preguntar: la primera respuesta gana, la segunda se ignora
    policy.Set("usage.set", ToolMode.Ask);
    var pendingTask = broker.ExecuteAsync("usage.set", J("{\"on\":true}"), "chat", false, CancellationToken.None);
    for (int i = 0; i < 100 && broker.Pending.Count == 0; i++) await Task.Delay(10);
    Check(broker.Pending.Count == 1 && sink.Types().Contains("confirm") && bridge.Calls.Count == 1, "broker: preguntar -> queda pendiente y se publica la confirmacion, sin ejecutar");
    string pid = broker.Pending[0].Id;
    Check(!broker.Resolve(PetProtocol.NewId(), "allow", "phone"), "broker: una respuesta con un id que no esta pendiente se ignora");
    Check(broker.Resolve(pid, "allow", "board"), "broker: la primera respuesta (placa) gana");
    Check(!broker.Resolve(pid, "deny", "phone"), "broker: la segunda respuesta (celular) llega tarde y se ignora");
    var asked = await pendingTask;
    Check(asked.Status == BrokerStatus.Done && bridge.Calls.Count == 2 && broker.Pending.Count == 0, "broker: confirmada -> se ejecuta una sola vez");
    Check(sink.Types().Contains("resolved") && log.Any(e => e.Channel == "chat+board"), "broker: se avisa a todos que ya se resolvio y el registro dice desde donde");
    var denied = broker.ExecuteAsync("usage.set", J("{\"on\":false}"), "chat", false, CancellationToken.None);
    for (int i = 0; i < 100 && broker.Pending.Count == 0; i++) await Task.Delay(10);
    broker.Resolve(broker.Pending[0].Id, "deny", "pc");
    Check((await denied).Status == BrokerStatus.Denied && bridge.Calls.Count == 2, "broker: denegada -> no se ejecuta");
    var expiring = new ActionBroker(tools, policy, new ListSink(), log.Add, ttl: TimeSpan.FromMilliseconds(300));
    Check((await expiring.ExecuteAsync("usage.set", J("{\"on\":true}"), "chat", false, CancellationToken.None)).Status == BrokerStatus.Expired && bridge.Calls.Count == 2 && expiring.Pending.Count == 0, "broker: sin respuesta a tiempo caduca y no ejecuta");
    using var cts = new CancellationTokenSource();
    var cancelled = broker.ExecuteAsync("usage.set", J("{\"on\":true}"), "chat", false, cts.Token);
    for (int i = 0; i < 100 && broker.Pending.Count == 0; i++) await Task.Delay(10);
    cts.Cancel();
    bool threw = false;
    try { await cancelled; } catch (OperationCanceledException) { threw = true; }
    Check(threw && broker.Pending.Count == 0, "broker: cancelar el chat retira la confirmacion pendiente");
    policy.Set("usage.set", ToolMode.Auto);
    for (int i = 0; i < ActionBroker.MaxPerMinute; i++) await broker.ExecuteAsync("usage.set", J("{\"on\":true}"), "chat", false, CancellationToken.None);
    Check((await broker.ExecuteAsync("usage.set", J("{\"on\":true}"), "chat", false, CancellationToken.None)).Status == BrokerStatus.RateLimited, "broker: limite de ritmo por herramienta");

    // protocolo v2
    var confirm = JsonDocument.Parse(PetProtocol.Confirm(new PendingAction(PetProtocol.NewId(), "usage.set", "Widget de uso", "Encender el widget", ToolRisk.Reversible, DateTimeOffset.UtcNow.AddSeconds(60), "chat"))).RootElement;
    Check(confirm.GetProperty("type").GetString() == "confirm" && confirm.GetProperty("risk").GetString() == "reversible", "protocolo: mensaje confirm");
    var dec = PetProtocol.Parse(B("{\"v\":1,\"type\":\"decision\",\"id\":\"" + PetProtocol.NewId() + "\",\"ref\":\"" + pid + "\",\"verdict\":\"allow\"}"));
    Check(dec.Message is DecisionMsg { Verdict: "allow" }, "protocolo: decision valida");
    Check(PetProtocol.Parse(B("{\"v\":1,\"type\":\"decision\",\"id\":\"" + PetProtocol.NewId() + "\",\"ref\":\"" + pid + "\",\"verdict\":\"always\"}")).Error is not null, "protocolo: 'always' no existe (lista cerrada de veredictos)");
    Check(PetProtocol.Parse(B("{\"v\":1,\"type\":\"decision\",\"id\":\"" + PetProtocol.NewId() + "\",\"ref\":\"x\",\"verdict\":\"allow\"}")).Error is not null, "protocolo: decision con ref invalido");

    // registro de actividad en SQLite
    using var store = new SqlitePetStore(Path.Combine(dir, "act.db"));
    foreach (var e in log.Take(3)) store.AddActivity(e);
    Check(store.RecentActivity(10).Count == 3 && store.RecentActivity(10)[0].Tool == "usage.set", "actividad: se guarda y se lee (la mas reciente primero)");
    store.ClearActivity();
    Check(store.RecentActivity(10).Count == 0, "actividad: se puede borrar");
    try { Directory.Delete(dir, true); } catch { }
}

// ------------------------------- agentes: ver y decidir las peticiones de Claude Code desde el celular o la placa
{
    string dir = Path.Combine(AppContext.BaseDirectory, "pet-test-appr-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    string file = Path.Combine(dir, "approvals.json");
    var clockNow = DateTimeOffset.UtcNow;
    var feed = new ApprovalFeed(file, () => clockNow);
    var added = new List<string>();
    var removed = new List<string>();
    feed.Changed += (a, r) => { added.AddRange(a.Select(x => x.Id)); removed.AddRange(r); };
    string idA = PetProtocol.NewId(), idB = PetProtocol.NewId(), idQ = PetProtocol.NewId();
    string Items(long ts, params string[] items) => "{\"ts\":" + ts + ",\"items\":[" + string.Join(",", items) + "]}";
    long Ts() => clockNow.ToUnixTimeSeconds();
    string Perm(string id, string detail, string? risk = null) => "{\"id\":\"" + id + "\",\"agent\":\"claude\",\"kind\":\"permission\",\"tool\":\"Bash\",\"cwd\":\"D:\\\\Proyectos\\\\TargetOS\\\\src\",\"detail\":\"" + detail + "\",\"risk\":" + (risk is null ? "null" : "\"" + risk + "\"") + ",\"expires\":" + (Ts() + 200) + "}";
    File.WriteAllText(file, Items(Ts(), Perm(idA, "npm test"), Perm(idB, "rm -rf build", "Borra archivos de forma recursiva")));
    feed.Poll();
    Check(added.SequenceEqual([idA, idB]) && feed.Current.Count == 2, "feed: llegan las peticiones nuevas");
    Check(feed.Find(idA)!.Cwd == "TargetOS/src" && feed.Find(idB)!.Risk == "Borra archivos de forma recursiva", "feed: la carpeta se acorta y el riesgo se conserva");
    feed.Poll();
    Check(added.Count == 2 && removed.Count == 0, "feed: sin cambios no avisa otra vez");
    File.WriteAllText(file, Items(Ts(), Perm(idB, "rm -rf build", "Borra archivos de forma recursiva")));
    feed.Poll();
    Check(removed.SequenceEqual([idA]), "feed: lo que se resolvio en la PC desaparece");
    clockNow = clockNow.AddSeconds(90);
    feed.Poll();
    Check(feed.Current.Count == 0 && removed.Contains(idB), "feed: si la app dejo de renovar el archivo (>60 s), no se ofrece nada");
    File.WriteAllText(file, Items(Ts(), Perm("no-es-id", "x"), "{\"id\":\"" + idA + "\",\"agent\":\"claude\",\"tool\":\"Bash\",\"detail\":\"" + new string('x', 900) + "\",\"expires\":0}"));
    feed.Poll();
    Check(feed.Current.Count == 1 && feed.Current[0].Detail!.Length <= 241, "feed: ids invalidos se descartan y el detalle se acota");
    File.WriteAllText(file, "{roto");
    feed.Poll();
    Check(feed.Current.Count == 0, "feed: archivo roto = lista vacia");

    // decidir
    var policy = new PolicyStore(Path.Combine(dir, "policy.json"));
    var bridge = new FakeBridge();
    var sink = new ListSink();
    var log = new List<ActivityEntry>();
    var svc = new ApprovalService(feed, policy, bridge, sink, log.Add);
    clockNow = DateTimeOffset.UtcNow;
    string q1 = "{\"question\":\"¿Qué base de datos?\",\"header\":\"BD\",\"multi\":false,\"options\":[\"SQLite\",\"Postgres\"]}";
    string qItem = "{\"id\":\"" + idQ + "\",\"agent\":\"claude\",\"kind\":\"question\",\"tool\":\"AskUserQuestion\",\"questions\":[" + q1 + "],\"expires\":" + (Ts() + 200) + "}";
    File.WriteAllText(file, Items(Ts(), Perm(idA, "npm test"), Perm(idB, "rm -rf build", "Borra archivos de forma recursiva"), qItem));
    feed.Poll();
    var ok = await svc.DecideAsync(idA, "allow", null, null, "phone", CancellationToken.None);
    Check(ok.Ok && bridge.Calls.SequenceEqual(["agent.decide"]) && feed.Find(idA) is null && sink.Types().Contains("approval_done"), "decidir: permitir una peticion normal la resuelve y avisa a todos");
    var again = await svc.DecideAsync(idA, "deny", null, null, "board", CancellationToken.None);
    Check(!again.Ok && again.Code == "not_found" && bridge.Calls.Count == 1, "decidir: la segunda respuesta (otro canal) llega tarde y se ignora");
    var risky = await svc.DecideAsync(idB, "allow", null, null, "phone", CancellationToken.None);
    Check(!risky.Ok && risky.Code == "forbidden" && bridge.Calls.Count == 1 && feed.Find(idB) is not null, "decidir: lo delicado NO se permite desde el celular por defecto");
    var denyRisky = await svc.DecideAsync(idB, "deny", "no", null, "phone", CancellationToken.None);
    Check(denyRisky.Ok && bridge.Calls.Count == 2, "decidir: pero SI se puede denegar");
    policy.SetRiskyFromPhone(true);
    File.WriteAllText(file, Items(Ts(), Perm(idB, "rm -rf build", "Borra archivos de forma recursiva"), qItem));
    feed.Poll();
    Check((await svc.DecideAsync(idB, "allow", null, null, "phone", CancellationToken.None)).Ok, "decidir: con el permiso explicito del usuario, lo delicado se puede permitir");
    var qNoAns = await svc.DecideAsync(idQ, "allow", null, null, "phone", CancellationToken.None);
    Check(!qNoAns.Ok && qNoAns.Code == "bad_json", "decidir: una pregunta sin respuestas no se envia");
    File.WriteAllText(file, Items(Ts(), Perm(idA, "npm test")));
    feed.Poll();
    policy.SetPaused(true);
    Check((await svc.DecideAsync(idA, "allow", null, null, "phone", CancellationToken.None)).Code == "forbidden", "decidir: con la mascota en pausa no se decide nada");
    policy.SetPaused(false);
    policy.Set("agent.decide", ToolMode.Off);
    Check((await svc.DecideAsync(idA, "allow", null, null, "phone", CancellationToken.None)).Code == "forbidden", "decidir: `agent.decide: off` cierra las aprobaciones remotas");
    policy.Set("agent.decide", null);
    Check(log.Count >= 3 && log.All(e => e.Tool == "agent.decide"), "decidir: todo queda en el registro de actividad");
    var failing = new ApprovalService(feed, policy, new FakeBridge { Ok = false }, new ListSink());
    Check(!(await failing.DecideAsync(idA, "allow", null, null, "phone", CancellationToken.None)).Ok, "decidir: si la app dice que ya no esta pendiente, se informa");

    // protocolo
    var dm = PetProtocol.Parse(B("{\"v\":1,\"type\":\"decision\",\"id\":\"" + PetProtocol.NewId() + "\",\"ref\":\"" + idQ + "\",\"verdict\":\"allow\",\"answers\":{\"¿Qué base de datos?\":\"SQLite\"},\"message\":\"ok\"}"));
    Check(dm.Message is DecisionMsg { Answers.Count: 1, Note: "ok" }, "protocolo: decision con respuestas y mensaje");
    Check(PetProtocol.Parse(B("{\"v\":1,\"type\":\"decision\",\"id\":\"" + PetProtocol.NewId() + "\",\"ref\":\"" + idQ + "\",\"verdict\":\"allow\",\"answers\":{\"a\":1}}")).Error is not null, "protocolo: answers con valores no texto se rechaza");
    var big = new ApprovalItem(idQ, "claude", "plan", "ExitPlanMode", "a/b", new string('d', 240), "riesgo", 0, [new AskItem(new string('q', 200), "h", false, ["a", "b", "c", "d", "e", "f"])], new string('p', 800));
    Check(PetProtocol.Approval(big).Length <= PetProtocol.MaxMessageBytes, "protocolo: una peticion enorme cabe en 4 KB");
    try { Directory.Delete(dir, true); } catch { }
}

// ------------------------------- recordatorios, intenciones, memoria y avisos proactivos
{
    var utc = TimeZoneInfo.Utc;
    var now = new DateTimeOffset(2026, 11, 4, 20, 30, 0, TimeSpan.Zero);
    ParsedReminder? P(string t) => ReminderParser.Parse(t, now, utc);
    var r1 = P("recuérdame en 10 minutos revisar el horno");
    Check(r1 is not null && r1.Due == now.AddMinutes(10) && r1.Text == "Revisar el horno" && r1.Repeat == "none", "recordatorio: «en 10 minutos»");
    var r2 = P("Recuérdame a las 5 pm llamar a mamá");
    Check(r2 is not null && r2.Due == new DateTimeOffset(2026, 11, 5, 17, 0, 0, TimeSpan.Zero) && r2.Text == "Llamar a mamá", "recordatorio: 5 pm ya pasó hoy -> mañana");
    var r3 = P("recuérdame mañana a las 8 tomar la pastilla");
    Check(r3 is not null && r3.Due == new DateTimeOffset(2026, 11, 5, 8, 0, 0, TimeSpan.Zero) && r3.Text == "Tomar la pastilla", "recordatorio: «mañana a las 8» sin am/pm -> la próxima ocurrencia");
    var r4 = P("recuérdame todos los días a las 7 estirarme");
    Check(r4 is not null && r4.Repeat == "daily" && r4.Due == new DateTimeOffset(2026, 11, 5, 7, 0, 0, TimeSpan.Zero), "recordatorio: diario");
    var r5 = P("recuérdame cada 2 horas beber agua");
    Check(r5 is not null && r5.Repeat == "every" && r5.EveryMin == 120 && r5.Due == now.AddHours(2) && r5.Text == "Beber agua", "recordatorio: cada 2 horas");
    Check(P("recuérdame comprar pan") is null && P("hola, ¿cómo estás?") is null && P("recuérdame mañana comprar pan") is null, "recordatorio: sin hora clara no inventa nada");
    Check(P("recuérdame cada 1 minuto algo") is null && P("recuérdame en 0 minutos algo") is null, "recordatorio: nunca cada pocos segundos");
    Check(P("avísame en media hora")?.Due == now.AddMinutes(30), "recordatorio: «media hora»");
    Check(ReminderParser.Describe(now.AddMinutes(10), now, utc) == "hoy a las 20:40" && ReminderParser.Describe(now.AddDays(1), now, utc) == "mañana a las 20:30", "recordatorio: se confirma con la hora entendida");
    Check(ReminderParser.NextDue("daily", 0, now.AddDays(-3), now) > now && ReminderParser.NextDue("daily", 0, now.AddDays(-3), now) <= now.AddDays(1), "recordatorio: si la PC estuvo apagada, salta al siguiente futuro (no 50 avisos)");

    // intenciones en español
    var nowL = DateTimeOffset.UtcNow;
    Check(Intents.Detect("inicia un pomodoro", nowL, false)?.Tool == "pomodoro.control" && Intents.Detect("pausa el pomodoro", nowL, false)?.Args.GetProperty("action").GetString() == "pause" && Intents.Detect("reinicia el pomodoro", nowL, false)?.Args.GetProperty("action").GetString() == "reset", "intención: pomodoro (iniciar, pausar, reiniciar)");
    var todo = Intents.Detect("añade una tarea comprar leche a las 18:30", nowL, false);
    Check(todo?.Tool == "todo.add" && todo.Args.GetProperty("title").GetString() == "Comprar leche" && todo.Args.GetProperty("time").GetString() == "18:30", "intención: tarea con hora");
    Check(Intents.Detect("crea una nota: ideas para el fin de semana", nowL, false)?.Tool == "note.add", "intención: nota");
    Check(Intents.Detect("recuerda que me gusta el té", nowL, false)?.Args.GetProperty("text").GetString() == "Me gusta el té", "intención: recordar un dato");
    Check(Intents.Detect("olvida lo del té", nowL, false)?.Tool == "memory.forget", "intención: olvidar un dato");
    Check(Intents.Detect("recuérdame en 5 minutos probar", nowL, false)?.Tool == "reminder.add", "intención: recordatorio");
    Check(Intents.Detect("hola, ¿qué tal?", nowL, false) is null && Intents.Detect("me gusta el pomodoro de mi mamá", nowL, false) is null && Intents.Detect("recuérdame comprar pan", nowL, false) is null, "intención: charla normal no dispara nada");

    // almacen y programador
    string dir = Path.Combine(AppContext.BaseDirectory, "pet-test-rem-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    using var store = new SqlitePetStore(Path.Combine(dir, "pet.db"));
    var clock = DateTimeOffset.UtcNow;
    long ts = clock.ToUnixTimeSeconds();
    long a = store.AddReminder("Una vez", ts - 30, "none", 0), b = store.AddReminder("Diario", ts - 30, "daily", 0), c = store.AddReminder("Futuro", ts + 3600, "none", 0), old = store.AddReminder("Viejo", ts - 3 * 86400, "none", 0);
    var sink = new ListSink();
    var bridge = new FakeBridge();
    var sched = new ReminderScheduler(store, sink, bridge, () => clock);
    var firedList = new List<string>();
    sched.Fired += (r, late) => firedList.Add(r.Text + (late ? "*" : ""));
    await sched.TickAsync();
    Check(firedList.SequenceEqual(["Una vez", "Diario"]) && sink.Types().Count(x => x == "reminder") == 2, "programador: dispara los vencidos y no el futuro ni el viejo (>24 h se descarta en silencio)");
    Check(bridge.Calls.Count == 2, "programador: también avisa en el notch de la app");
    await sched.TickAsync();
    Check(firedList.Count == 2, "programador: no repite lo que ya sonó");
    var list = store.ListReminders();
    Check(list.Any(r => r.Text == "Diario" && !r.Fired && r.Due > ts) && list.Any(r => r.Text == "Una vez" && r.Fired) && list.Any(r => r.Text == "Futuro" && !r.Fired) && list.All(r => r.Text != "Viejo" || r.Fired), "agenda: el diario queda pendiente para mañana y el de una vez como «sonado»");
    store.DeleteReminder(c);
    Check(store.ListReminders().All(r => r.Text != "Futuro"), "agenda: se puede borrar");

    // herramientas locales por el broker (memoria y recordatorio)
    var policy = new PolicyStore(Path.Combine(dir, "policy.json"));
    var tools = new ToolRegistry().Add(new ReminderAddTool(store)).Add(new MemoryRememberTool(store)).Add(new MemoryForgetTool(store));
    var broker = new ActionBroker(tools, policy, sink, store.AddActivity);
    JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();
    var rem = await broker.ExecuteAsync("memory.remember", J("{\"text\":\"Le gusta el té verde\"}"), "chat", false, CancellationToken.None);
    Check(rem.Status == BrokerStatus.Done && store.Memories().Any(x => x.Text == "Le gusta el té verde"), "memoria: recordar (automático, reversible)");
    Check((await broker.ExecuteAsync("memory.remember", J("{\"text\":\"" + new string('x', 300) + "\"}"), "chat", false, CancellationToken.None)).Status == BrokerStatus.Invalid, "memoria: un recuerdo enorme se rechaza");
    var forget = broker.ExecuteAsync("memory.forget", J("{\"text\":\"té verde\"}"), "chat", false, CancellationToken.None);
    for (int i = 0; i < 100 && broker.Pending.Count == 0; i++) await Task.Delay(10);
    Check(broker.Pending.Count == 1 && broker.Pending[0].Summary.Contains("Le gusta el té verde") && store.Memories().Count == 1, "memoria: olvidar es sensible: pregunta y muestra EXACTAMENTE qué se borraría");
    broker.Resolve(broker.Pending[0].Id, "allow", "phone");
    Check((await forget).Status == BrokerStatus.Done && store.Memories().Count == 0, "memoria: confirmado -> se olvida");
    var add = await broker.ExecuteAsync("reminder.add", J("{\"text\":\"Probar\",\"due\":" + (ts + 600) + ",\"repeat\":\"none\",\"every\":0}"), "chat", false, CancellationToken.None);
    Check(add.Status == BrokerStatus.Done && store.ListReminders().Any(r => r.Text == "Probar"), "recordatorio: la herramienta lo crea");

    // bitacora de avisos: quien estaba desconectado los recibe al volver
    long seq1 = store.LogFired(new Reminder(1, "Uno", ts, "none", 0, true, 0), ts - 4000), seq2 = store.LogFired(new Reminder(2, "Dos", ts, "none", 0, true, 0), ts - 30), seq3 = store.LogFired(new Reminder(3, "Tres", ts, "none", 0, true, 0), ts - 10);
    var missed = store.FiredSince(seq1, 600, ts);
    Check(missed.Select(m => m.R.Text).SequenceEqual(["Dos", "Tres"]) && store.FiredSince(seq3, 600, ts).Count == 0 && store.FiredSince(seq1 - 1, 600, ts).Count == 2 && store.FiredSince(seq1 - 1, 86400, ts).Count == 3, "bitacora: repite lo que no vio (ventana de tiempo) y nunca lo ya visto");
    Check(PetProtocol.Parse(B("{\"v\":1,\"type\":\"hello\",\"id\":\"" + PetProtocol.NewId() + "\",\"since_rev\":0,\"since_reminder\":7}")).Message is HelloMsg { SinceReminder: 7 } && PetProtocol.Parse(B("{\"v\":1,\"type\":\"hello\",\"id\":\"" + PetProtocol.NewId() + "\",\"since_rev\":0}")).Message is HelloMsg { SinceReminder: -1 }, "protocolo: hello con o sin el ultimo aviso visto");
    Check(JsonDocument.Parse(PetProtocol.Reminder(new Reminder(5, "Hola", ts, "none", 0, false, 0), true, 9)).RootElement.GetProperty("seq").GetInt64() == 9, "protocolo: el aviso lleva su numero de secuencia");
    Check(PushService.IsAllowedEndpoint("https://web.push.apple.com/abc") && PushService.IsAllowedEndpoint("https://fcm.googleapis.com/fcm/send/x") && !PushService.IsAllowedEndpoint("https://evil.example/x") && !PushService.IsAllowedEndpoint("http://web.push.apple.com/x") && !PushService.IsAllowedEndpoint("https://web.push.apple.com.evil.example/x") && !PushService.IsAllowedEndpoint("https://127.0.0.1/x"), "push: solo se aceptan servicios de push conocidos (el servidor hace POST a esa direccion)");
    var pushSvc = new PushService(Path.Combine(dir, "push.json"));
    Check(pushSvc.PublicKey.Length > 40 && pushSvc.Add("https://web.push.apple.com/z1", "BPublicKeyBase64UrlAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "authSecret123456") && pushSvc.Count == 1 && !pushSvc.Add("https://evil.example/x", "k", "a"), "push: claves VAPID generadas y suscripcion valida");
    Check(new PushService(Path.Combine(dir, "push.json")).PublicKey == pushSvc.PublicKey && new PushService(Path.Combine(dir, "push.json")).Count == 1, "push: las claves y suscripciones persisten");
    pushSvc.Remove("https://web.push.apple.com/z1");
    Check(pushSvc.Count == 0, "push: se puede quitar");

    // avisos proactivos
    Check(Notices.ForAgentState("done", "working") is not null && Notices.ForAgentState("done", "idle") is null && Notices.ForAgentState("attention", "working") is not null && Notices.ForAgentState("attention", "attention") is null, "avisos: terminar tras trabajar, atención y alerta una sola vez");
    Check(Notices.InQuiet("22:00-07:00", new TimeSpan(23, 0, 0)) && Notices.InQuiet("22:00-07:00", new TimeSpan(3, 0, 0)) && !Notices.InQuiet("22:00-07:00", new TimeSpan(12, 0, 0)) && !Notices.InQuiet("", TimeSpan.Zero) && !Notices.InQuiet("basura", TimeSpan.Zero), "avisos: horario de silencio (cruza la medianoche; un valor roto no silencia)");
    policy.SetQuiet("22:00-07:00");
    Check(new PolicyStore(Path.Combine(dir, "policy.json")).Quiet == "22:00-07:00", "avisos: el horario de silencio persiste");
    policy.SetQuiet("mal formato");
    Check(policy.Quiet == "", "avisos: un horario mal escrito se descarta");
    string uf = Path.Combine(dir, "usage.json");
    File.WriteAllText(uf, "{\"ts\":" + ts + ",\"data\":{\"providers\":[{\"id\":\"claude\",\"name\":\"Claude\",\"plan\":\"Max\",\"limits\":[{\"label\":\"week\",\"used_pct\":85,\"resets_at\":" + (ts + 9999) + "},{\"label\":\"5h\",\"used_pct\":10}]}]}}");
    var watch = new UsageWatcher(uf);
    var first = watch.Check(clock);
    Check(first.Count == 1 && first[0].Contains("85 %") && watch.Check(clock).Count == 0, "avisos: uso alto se avisa una sola vez");

    // estado de la app (solo lectura)
    string sf = Path.Combine(dir, "app-state.json");
    File.WriteAllText(sf, "{\"ts\":" + ts + ",\"pomodoro\":{\"running\":true,\"focus\":true,\"remaining\":\"12:30\"},\"todo\":{\"done\":2,\"total\":5,\"items\":[{\"title\":\"Comprar leche\",\"done\":false}]},\"weather\":{\"current\":\"27° · Nublado\"}}");
    string? st = AppStateBridge.Summary(sf, clock);
    Check(st is not null && st.Contains("Pomodoro en marcha") && st.Contains("2 de 5") && st.Contains("27° · Nublado"), "estado: resumen de pomodoro, tareas y clima");
    Check(AppStateBridge.Summary(sf, clock.AddMinutes(30)) is null, "estado: dato viejo no se usa");
    Check(Persona.Build(new PetSnapshot(1, "Nimbo", 70, 50, "open", false), DateTime.Now, [], null, st).Contains("Clima actual"), "persona: incluye el estado de la app");
    try { Directory.Delete(dir, true); } catch { }
}

// ---------------------------------------------------------------------------- personalidad
{
    var snap = new PetSnapshot(3, "Luna", 20, 80, "open", false);
    string p = Persona.Build(snap, new DateTime(2026, 11, 4, 22, 30, 0), ["Me gusta el café con leche"]);
    Check(p.Contains("Eres Luna") && p.Contains("22:30") && p.Contains("por la noche"), "persona: nombre, hora y franja");
    Check(p.Contains("muy poca energía") && p.Contains("muy a gusto"), "persona: estado de animo segun energia y carino");
    Check(p.Contains("Me gusta el café con leche"), "persona: incluye los recuerdos guardados");
    Check(p.Contains("No inventes") && p.Contains("200 caracteres") && p.Contains("happy"), "persona: reglas de estilo y expresiones");
    Check(!Persona.Build(snap, DateTime.Now, []).Contains("recordar"), "persona: sin recuerdos no menciona la seccion");
    Check(p.Contains("nunca hables de tu humano como si durmiera") && p.Contains("amiguito"), "persona: no confunde los papeles ni usa diminutivos");
    Check(Persona.FewShot.Length >= 3 && Persona.FewShot.All(e => OllamaBackend.Parse("{\"message\":{\"content\":" + System.Text.Json.JsonSerializer.Serialize(e.Json) + "}}").Text.Length > 0 && OllamaBackend.Parse("{\"message\":{\"content\":" + System.Text.Json.JsonSerializer.Serialize(e.Json) + "}}") != OllamaBackend.Fallback), "persona: los ejemplos son respuestas validas del protocolo");
    Check(p.Length < 2200, "persona: corta (cabe en el contexto de 4096)");
}

// ---------------------------------------------------------------------- coordinador de chat
{
    static string Type(byte[] m) => JsonDocument.Parse(m).RootElement.GetProperty("type").GetString()!;
    static string Phase(byte[] m) => JsonDocument.Parse(m).RootElement.GetProperty("phase").GetString()!;
    static async Task<bool> Until(Func<bool> cond, int ms = 3000)
    {
        for (int i = 0; i < ms / 10 && !cond(); i++) await Task.Delay(10);
        return cond();
    }

    // respuesta normal: pensando -> hablando -> say -> idle
    {
        var sink = new ListSink();
        var backend = new FakeBackend { Reply = new ChatReply("¡Hola! Qué gusto verte.", "happy") };
        using var chat = new ChatCoordinator(backend, sink);
        var (id, err) = chat.Enqueue("hola");
        Check(id is not null && err is null, "chat aceptado");
        Check(await Until(() => sink.Types().Contains("say") && sink.Last() == "status"), "respuesta completa");
        var seq = string.Join(",", sink.All().Select(m => Type(m) == "status" ? "status:" + Phase(m) : Type(m)));
        Check(seq == "status:thinking,status:speaking,say,status:idle", "secuencia pensando-hablando-say-idle: " + seq);
        var say = JsonDocument.Parse(sink.All().First(m => Type(m) == "say")).RootElement;
        Check(say.GetProperty("text").GetString() == "¡Hola! Qué gusto verte." && say.GetProperty("chat_id").GetString() == id, "say con tildes y chat_id");
    }

    // la mascota pide activar el widget de uso: dice que lo hace, la app confirma, llegan los datos y responde con ellos
    {
        var sink = new ListSink();
        var backend = new FakeBackend { Reply = new ChatReply("Lo activo ahora.", "think", "usage_widget_on") };
        var fake = new FakeBridge { Ok = true };
        string? data = null;
        using var chat = new ChatCoordinator(backend, sink, broker: BrokerHelper.Make(fake, sink), usageProbe: () => data);
        _ = Task.Run(async () => { await Task.Delay(400); data = "Claude: queda 68 %"; });
        chat.Enqueue("prende lo del uso, porfa");
        Check(await Until(() => sink.Types().Count(t => t == "say") >= 2 && sink.Last() == "status", 8000), "accion: dice lo que hace y luego responde con los datos");
        var says = sink.All().Where(m => Type(m) == "say").Select(m => JsonDocument.Parse(m).RootElement).ToList();
        Check(says.Count == 2 && !says[0].GetProperty("final").GetBoolean() && says[1].GetProperty("final").GetBoolean(), "accion: primer say no final, el segundo cierra");
        Check(fake.Calls.SequenceEqual(["usage.set"]) && backend.Started == 2, "accion: una accion y una sola vuelta (sin cadenas)");
    }
    {
        var sink = new ListSink();
        var backend = new FakeBackend { Reply = new ChatReply("Lo apago.", "smile", "usage_widget_off") };
        using var chat = new ChatCoordinator(backend, sink, broker: BrokerHelper.Make(new FakeBridge { Ok = false }, sink), usageProbe: () => null);
        chat.Enqueue("apaga el uso");
        Check(await Until(() => sink.Types().Contains("say") && sink.Last() == "status" && sink.All().Count(m => Type(m) == "say") >= 2), "accion rechazada: responde");
        var last = JsonDocument.Parse(sink.All().Last(m => Type(m) == "say")).RootElement;
        Check(last.GetProperty("text").GetString()!.StartsWith("No pude hacerlo") && last.GetProperty("final").GetBoolean(), "accion rechazada: avisa que no pudo");
    }
    {
        var sink = new ListSink();
        var backend = new FakeBackend { Reply = new ChatReply("Hola", "happy", "format_disk") };
        var fake = new FakeBridge { Ok = true };
        using var chat = new ChatCoordinator(backend, sink, broker: BrokerHelper.Make(fake, sink));
        chat.Enqueue("hola");
        Check(await Until(() => sink.Types().Contains("say") && sink.Last() == "status"), "accion inventada: responde normal");
        Check(fake.Calls.Count == 0, "accion inventada por el modelo: se ignora (lista cerrada)");
    }
    Check(OllamaBackend.Parse("""{"message":{"content":"{\"text\":\"ok\",\"expression\":\"smile\",\"action\":\"usage_widget_on\"}"}}""").Action == "usage_widget_on", "parse: accion valida");
    Check(OllamaBackend.Parse("""{"message":{"content":"{\"text\":\"ok\",\"expression\":\"smile\",\"action\":\"rm -rf\"}"}}""").Action == "none", "parse: accion fuera de la lista -> none");

    // intencion clara: pregunta por su uso sin datos -> el complemento activa el widget aunque el modelo no lo pida
    {
        Check(AppIntents.Detect("¿Cuánto uso me queda en Claude?", false)?.Action == "usage_widget_on", "intencion: pregunta de uso sin datos -> activar");
        Check(AppIntents.Detect("¿Cuánto uso me queda en Claude?", true) is null, "intencion: con datos ya disponibles responde el modelo");
        Check(AppIntents.Detect("hola, ¿qué tal?", false) is null && AppIntents.Detect("me gusta el uso de colores", false) is null, "intencion: charla normal no dispara nada");
        Check(AppIntents.Detect("apaga el widget de uso", true)?.Action == "usage_widget_off", "intencion: apagar el widget");
        var sink = new ListSink();
        var backend = new FakeBackend { Reply = new ChatReply("Te queda el 68 %.", "happy") };
        var fake = new FakeBridge { Ok = true };
        string? data = null;
        using var chat = new ChatCoordinator(backend, sink, broker: BrokerHelper.Make(fake, sink), usageProbe: () => data);
        _ = Task.Run(async () => { await Task.Delay(400); data = "Claude: queda 68 %"; });
        chat.Enqueue("¿Cuánto uso me queda en Claude?");
        Check(await Until(() => sink.All().Count(m => Type(m) == "say") >= 2 && sink.Last() == "status", 8000), "intencion: activa, espera los datos y responde");
        var lastSay = JsonDocument.Parse(sink.All().Last(m => Type(m) == "say")).RootElement;
        Check(fake.Calls.SequenceEqual(["usage.set"]) && lastSay.GetProperty("text").GetString() == "Te queda el 68 %." && backend.Started == 1, "intencion: una accion y el modelo solo redacta con los datos");
    }

    // JSON/expresion invalida del modelo: expresion `smile` y texto recortado a 400
    {
        var r = ChatCoordinator.Sanitize(new ChatReply(new string('a', 900), "furiosa"));
        Check(r.Expression == "smile" && PetProtocol.CountChars(r.Text) == 400 && r.Text.EndsWith('…'), "sanitiza expresion y largo");
        Check(ChatCoordinator.Sanitize(new ChatReply("   ", "open")).Text == "…", "texto vacio");
        Check(ChatCoordinator.Sanitize(new ChatReply("action=usage_widget_on. Ahora lo miro.", "open")).Text == "Ahora lo miro.", "el nombre de la accion no se cuela en el texto");
    }

    // cancelar mientras genera: vuelve a idle y la respuesta tardia se descarta
    {
        var sink = new ListSink();
        var backend = new FakeBackend { Manual = true, IgnoreCancel = true };  // un modelo que no obedece la cancelacion
        using var chat = new ChatCoordinator(backend, sink);
        var (id, _) = chat.Enqueue("cuéntame algo");
        Check(await Until(() => backend.Started == 1), "la generacion arranca");
        Check(chat.Cancel(id!), "cancelar devuelve true");
        Check(sink.All().Any(m => Type(m) == "status" && Phase(m) == "idle"), "cancelar publica idle");
        backend.Complete(new ChatReply("llegue tarde", "happy"));  // respuesta tardia
        await Task.Delay(150);
        Check(!sink.Types().Contains("say"), "la respuesta tardia no se publica");
        Check(!chat.Cancel(id!) || true, "cancelar dos veces no revienta");
    }

    // cola: 1 en curso + 3 en cola; el quinto es busy; cancelar uno en cola
    {
        var sink = new ListSink();
        var backend = new FakeBackend { Manual = true };
        using var chat = new ChatCoordinator(backend, sink);
        var ids = new List<string>();
        for (int i = 0; i < 4; i++) { var (id, e) = chat.Enqueue("m" + i); Check(id is not null, "mensaje " + i + " aceptado"); ids.Add(id!); }
        var (none, code) = chat.Enqueue("m4");
        Check(none is null && code == "busy", "quinto mensaje: busy");
        Check(await Until(() => backend.Started == 1), "una sola generacion a la vez");
        Check(chat.Cancel(ids[2]), "cancelar uno que esta en cola");
        backend.Complete(new ChatReply("uno", "open"));
        Check(await Until(() => backend.Started == 2), "pasa al siguiente");
        backend.Complete(new ChatReply("dos", "open"));
        Check(await Until(() => backend.Started == 3), "salta el cancelado y sigue con el ultimo");
        backend.Complete(new ChatReply("tres", "open"));
        Check(await Until(() => sink.Types().Count(t => t == "say") == 3), "se responden 3 (uno cancelado)");
        Check(backend.Started == 3, "el cancelado nunca llego al modelo");
    }

    // errores del modelo: se informan con su codigo y nunca se queda pensando
    {
        foreach (var code in new[] { "model_missing", "model_unavailable" })
        {
            var sink = new ListSink();
            using var chat = new ChatCoordinator(new FakeBackend { Fail = code }, sink);
            chat.Enqueue("hola");
            Check(await Until(() => sink.Types().Contains("error") && sink.Types().Count(t => t == "status") >= 2), "fin con " + code);
            var err = sink.All().Select(m => JsonDocument.Parse(m).RootElement).FirstOrDefault(r => r.GetProperty("type").GetString() == "error");
            Check(err.ValueKind == JsonValueKind.Object && err.GetProperty("code").GetString() == code, "error " + code);
            Check(Phase(sink.All().Last()) == "idle", "vuelve a idle tras " + code);
        }
    }
}

// ----------------------------------------------------------------------- Ollama (HTTP simulado)
{
    string leaseDir = Path.Combine(AppContext.BaseDirectory, "pet-test-lease-" + Guid.NewGuid().ToString("N"));
    var history = new List<ChatTurn> { new("user", "Hola, ¿cómo te llamas?") };
    static string Content(string text, string expr) =>
        JsonSerializer.Serialize(new { message = new { role = "assistant", content = JsonSerializer.Serialize(new { text, expression = expr }) } });

    // cuerpo de la peticion y reserva de actividad
    string? sent = null;
    bool leaseDuring = false;
    var ok = new StubHandler(async (req, ct) =>
    {
        sent = await req.Content!.ReadAsStringAsync(ct);
        leaseDuring = ActivityLease.IsActive(leaseDir, DateTimeOffset.UtcNow);
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(Content("Me llamo Mira, ¡mucho gusto!", "happy")) };
    });
    var backend = new OllamaBackend(new HttpClient(ok), new OllamaOptions(), () => "Eres Mira, una mascota cariñosa.", leaseDir);
    var reply = await backend.GenerateAsync(history, CancellationToken.None);
    Check(reply.Text == "Me llamo Mira, ¡mucho gusto!" && reply.Expression == "happy", "Ollama: respuesta estructurada con tildes");
    var req0 = JsonDocument.Parse(sent!).RootElement;
    Check(req0.GetProperty("model").GetString() == "gemma4:latest" && !req0.GetProperty("stream").GetBoolean() && !req0.GetProperty("think").GetBoolean(), "Ollama: modelo, stream=false, think=false");
    Check(req0.GetProperty("options").GetProperty("num_ctx").GetInt32() == 4096, "Ollama: num_ctx 4096");
    Check(req0.GetProperty("format").GetProperty("properties").GetProperty("expression").GetProperty("enum").GetArrayLength() == 10, "Ollama: expresiones como lista cerrada en el esquema");
    var msgs = req0.GetProperty("messages");
    Check(msgs[0].GetProperty("role").GetString() == "system" && msgs[msgs.GetArrayLength() - 1].GetProperty("content").GetString() == "Hola, ¿cómo te llamas?", "Ollama: persona + ejemplos + historial");
    Check(msgs.GetArrayLength() == 1 + Persona.FewShot.Length * 2 + 1 && msgs[2].GetProperty("role").GetString() == "assistant", "Ollama: los ejemplos van como turnos de usuario y asistente");
    Check(leaseDuring, "reserva de actividad vigente durante la peticion");
    Check(!ActivityLease.IsActive(leaseDir, DateTimeOffset.UtcNow), "la reserva se libera al terminar");

    // respuestas invalidas
    Check(OllamaBackend.Parse("{\"message\":{\"content\":\"esto no es json\"}}") == OllamaBackend.Fallback, "JSON invalido -> fallback smile");
    Check(OllamaBackend.Parse("basura") == OllamaBackend.Fallback, "respuesta ilegible -> fallback");
    Check(OllamaBackend.Parse(Content("hola", "enojada")).Expression == "smile", "expresion desconocida -> smile");
    Check(OllamaBackend.Fallback.Expression == "smile", "el fallback es smile");

    // errores
    async Task<string?> CodeOf(HttpMessageHandler h, CancellationToken ct = default, TimeSpan? timeout = null)
    {
        var b = new OllamaBackend(new HttpClient(h), new OllamaOptions { Timeout = timeout ?? TimeSpan.FromSeconds(5) }, () => "x", leaseDir);
        try { await b.GenerateAsync(history, ct); return null; }
        catch (ChatBackendException e) { return e.Code; }
    }
    Check(await CodeOf(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)))) == "model_missing", "404 -> model_missing");
    Check(await CodeOf(new StubHandler((_, _) => throw new HttpRequestException("rechazada"))) == "model_unavailable", "Ollama parado -> model_unavailable");
    Check(await CodeOf(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)))) == "model_unavailable", "500 -> model_unavailable");
    Check(await CodeOf(new StubHandler(async (_, ct) => { await Task.Delay(5000, ct); return new HttpResponseMessage(); }), default, TimeSpan.FromMilliseconds(150)) == "model_unavailable", "tiempo agotado -> model_unavailable");

    // cancelar: se propaga como cancelacion (no como error de modelo) y libera la reserva
    using var cts = new CancellationTokenSource();
    var slow = new StubHandler(async (_, ct) => { await Task.Delay(5000, ct); return new HttpResponseMessage(); });
    var b2 = new OllamaBackend(new HttpClient(slow), new OllamaOptions(), () => "x", leaseDir);
    var task = b2.GenerateAsync(history, cts.Token);
    await Task.Delay(100);
    cts.Cancel();
    bool cancelled = false;
    try { await task; } catch (OperationCanceledException) { cancelled = true; }
    Check(cancelled, "cancelar se propaga como cancelacion");
    Check(!ActivityLease.IsActive(leaseDir, DateTimeOffset.UtcNow), "la reserva se libera tambien al cancelar");

    // una reserva caducada o de un proceso que ya no existe no cuenta
    Directory.CreateDirectory(leaseDir);
    File.WriteAllText(Path.Combine(leaseDir, "lease.json"), $$"""{"pid":{{Environment.ProcessId}},"until":{{DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeSeconds()}}}""");
    Check(!ActivityLease.IsActive(leaseDir, DateTimeOffset.UtcNow), "reserva caducada");
    File.WriteAllText(Path.Combine(leaseDir, "lease.json"), $$"""{"pid":2147483000,"until":{{DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeSeconds()}}}""");
    Check(!ActivityLease.IsActive(leaseDir, DateTimeOffset.UtcNow), "reserva de un proceso inexistente");
    File.WriteAllText(Path.Combine(leaseDir, "lease.json"), "{roto");
    Check(!ActivityLease.IsActive(leaseDir, DateTimeOffset.UtcNow), "reserva ilegible");
    try { Directory.Delete(leaseDir, true); } catch { }
}

await Integration.Run((ok, what) => Check(ok, what));
await WebTests.Run((ok, what) => Check(ok, what));
TeamToolsTests.Run((ok, what) => Check(ok, what));

Console.WriteLine($"{checks} comprobaciones, {failed} fallos");
return failed == 0 ? 0 : 1;

sealed class MemStore : IPetStore
{
    private PetSnapshot? state;
    private readonly List<string> ids = [];
    public PetSnapshot? LoadState() => state;
    public void SaveState(PetSnapshot s, long poseUntilMs) => state = s;
    public List<string> LoadSeenIds(int max) => [.. ids.TakeLast(max)];
    public void AddSeenId(string id, long tsMs) => ids.Add(id);
}

sealed class FakeBridge : IAppBridge
{
    public bool Ok { get; set; } = true;
    public List<string> Calls { get; } = [];
    public Task<(bool Ok, string Message)> CallAsync(string tool, System.Text.Json.Nodes.JsonObject args, CancellationToken ct) { Calls.Add(tool); return Task.FromResult((Ok, Ok ? "hecho" : "la app lo rechazo")); }
}

static class BrokerHelper
{
    public static ActionBroker Make(IAppBridge bridge, IEventSink sink)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "pet-test-broker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new ActionBroker(new ToolRegistry().Add(new UsageSetTool(bridge)), new PolicyStore(Path.Combine(dir, "policy.json")), sink);
    }
}

sealed class ListSink : IEventSink
{
    private readonly List<byte[]> items = [];
    public void Publish(byte[] message) { lock (items) items.Add(message); }
    public List<byte[]> All() { lock (items) return [.. items]; }
    public List<string> Types() => All().Select(m => JsonDocument.Parse(m).RootElement.GetProperty("type").GetString()!).ToList();
    public string Last() { var t = Types(); return t.Count == 0 ? "" : t[^1]; }
}

sealed class FakeBackend : IChatBackend
{
    public ChatReply Reply { get; set; } = new("ok", "open");
    public bool Manual { get; set; }
    public bool IgnoreCancel { get; set; }
    public string? Fail { get; set; }
    public int Started;
    private TaskCompletionSource<ChatReply>? pending;
    private ChatReply? early;
    // Si la respuesta se da antes de que la generacion arranque, se guarda para cuando llegue (evita carreras en las pruebas).
    public void Complete(ChatReply r)
    {
        var p = pending;
        if (p is not null && !p.Task.IsCompleted) p.TrySetResult(r); else early = r;
    }

    public async Task<ChatReply> GenerateAsync(IReadOnlyList<ChatTurn> history, CancellationToken ct)
    {
        Interlocked.Increment(ref Started);
        if (Fail is not null) throw new ChatBackendException(Fail, "fallo simulado");
        if (!Manual) return Reply;
        if (early is { } e) { early = null; return e; }
        var tcs = new TaskCompletionSource<ChatReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = tcs;
        using var reg = IgnoreCancel ? default : ct.Register(() => tcs.TrySetCanceled());
        return await tcs.Task;
    }
}

sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> fn) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => fn(request, ct);
}
