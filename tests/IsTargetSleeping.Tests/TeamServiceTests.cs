using IsTargetSleeping;

internal static class TeamServiceTests
{
    static readonly DateTime Mon = new(2026, 10, 19, 8, 0, 0); // lunes

    static string TempDir(string name) => Path.Combine(Path.GetTempPath(), $"its-{name}-{Guid.NewGuid():N}"[..28]);

    static string Git(string dir, params string[] args) =>
        TeamProcess.RunAsync(TeamProcess.Find("git")!, args, "", dir, TimeSpan.FromMinutes(1)).Result.Out;

    static string MakeRepo()
    {
        var repo = TempDir("repo");
        Directory.CreateDirectory(repo);
        Git(repo, "init", "-q");
        File.WriteAllText(Path.Combine(repo, "a.txt"), "a");
        Git(repo, "add", ".");
        Git(repo, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-qm", "i");
        return repo;
    }

    static TeamJob Job(string repo, string task = "haz algo", params TeamStep[] steps) =>
        new(task, repo, TeamPreset.Auto, steps.Length > 0 ? steps : [new TeamStep(TeamAgent.Codex, TeamRole.Implement, true, 5, "")], TeamRouter.Profile(task));

    static void Delete(params string[] dirs) { foreach (var d in dirs) try { Directory.Delete(d, true); } catch { } }

    /// Arreglos de las pruebas con usuarios simulados: enrutado de preguntas, tareas vacías, nombres con acentos y carpetas sin git.
    static void Fixes(Action<string, bool> check)
    {
        check("equipo (router): «códigos hex» es una pregunta", TeamRouter.IsQuestion("Dame 3 ideas de paleta de colores para una web de una panadería, con códigos hex", []));
        check("equipo (router): preguntas sobre una función o un script siguen siendo preguntas", TeamRouter.IsQuestion("¿Qué hace esta función?", []) && TeamRouter.IsQuestion("Explícame qué es un script", []));
        check("equipo (router): las tareas con verbo de cambio siguen siendo de proyecto", !TeamRouter.IsQuestion("Arregla el código del contador", []) && !TeamRouter.IsQuestion("Añade una función que sume", []));
        check("equipo (router): se reconoce cuándo una pregunta habla del proyecto", TeamRouter.RefersToProject("¿Qué hace este programa?") && TeamRouter.RefersToProject("What does this code do?") && !TeamRouter.RefersToProject("¿Qué es una rama?"));

        var repo = MakeRepo();
        var seen = new List<(string Dir, bool Reads)>();
        var service = new TeamService(new TeamStore(TempDir("st")), (step, prompt, dir, ct) => { seen.Add((dir, step.ReadsProject)); return Task.FromResult(new AgentResult(true, "hecho")); }, r => TempDir("bd"));
        service.Enqueue(Job(repo, "   ", new TeamStep(TeamAgent.Haiku, TeamRole.Answer, false, 1, "")));
        service.WhenIdle().Wait();
        check("equipo: una tarea vacía o de solo espacios no se encola ni gasta un agente", seen.Count == 0 && service.Store.LoadHistory().Count == 0);

        var question = "¿Qué hace este programa?";
        service.Enqueue(new TeamJob(question, repo, TeamPreset.Auto, [new TeamStep(TeamAgent.Haiku, TeamRole.Answer, false, 1, "")], TeamRouter.Profile(question)));
        service.WhenIdle().Wait();
        var asked = service.Store.LoadHistory().FirstOrDefault();
        check("equipo (leer proyecto): el agente corre en la carpeta del proyecto con ReadsProject", seen.Count == 1 && seen[0].Dir == repo && seen[0].Reads);
        check("equipo (leer proyecto): el proyecto sigue intacto y la entrada no guarda rama", Directory.Exists(repo) && File.Exists(Path.Combine(repo, "a.txt")) && asked is { State: "Answered", TreeDir: "" });
        service.Enqueue(new TeamJob("¿Qué es una rama?", repo, TeamPreset.Auto, [new TeamStep(TeamAgent.Haiku, TeamRole.Answer, false, 1, "")], TeamRouter.Profile("¿Qué es una rama?")));
        service.WhenIdle().Wait();
        check("equipo: una pregunta que no habla del proyecto se responde en una carpeta vacía, sin ver el proyecto", seen.Count == 2 && seen[1].Dir != repo && !seen[1].Reads);

        File.WriteAllText(Path.Combine(repo, "ñandú.txt"), "x");
        Git(repo, "add", ".");
        Git(repo, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-qm", "acentos");
        var baseSha = TeamGit.HeadAsync(repo, default).Result;
        File.WriteAllText(Path.Combine(repo, "áéí.txt"), "y");
        Git(repo, "add", ".");
        Git(repo, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-qm", "más acentos");
        var changes = TeamGit.ChangesAsync(repo, baseSha).Result;
        check("equipo: los archivos con ñ o acentos salen con su nombre real (no entre comillas y en octal) y su diff no sale vacío",
            changes.Count == 1 && changes[0].Path == "áéí.txt" && TeamGit.DiffAsync(repo, baseSha, "áéí.txt").Result.Contains("+y"));

        var plain = TempDir("nogit"); Directory.CreateDirectory(plain);
        var baseOfPlain = TempDir("base");
        var (made, error) = TeamWorktree.CreateAsync(plain, "x1", baseOfPlain).Result;
        check("equipo: una carpeta sin git da un mensaje claro (no el «fatal:» de git) y no deja carpetas vacías", made is null && error is { Length: > 0 } && !error.Contains("fatal") && !Directory.Exists(baseOfPlain));
        Delete(repo, plain);

        // Fallos permanentes: un CLI que falta o un tiempo agotado no se reintenta 4 veces con escalado, y el error llega al informe.
        int calls = 0;
        var missing = new TeamRun((s, p, ct) => { calls++; return Task.FromResult(new AgentResult(false, "", Error: "Claude Code no está instalado.") { Permanent = true }); });
        var failed = missing.RunAsync(new TeamPlan(TeamRouter.Profile("¿qué es x?"), TeamPreset.Auto, [new TeamStep(TeamAgent.Haiku, TeamRole.Answer, false, 1, "")]), "¿qué es x?").Result;
        check("equipo: un fallo permanente (CLI ausente, tiempo agotado) se intenta una sola vez y el informe dice por qué", calls == 1 && failed.Outcome == TeamOutcome.Failed && failed.Summary.Contains("no está instalado"));
        calls = 0;
        var flaky = new TeamRun((s, p, ct) => { calls++; return Task.FromResult(new AgentResult(false, "", Error: "boom")); });
        flaky.RunAsync(new TeamPlan(TeamRouter.Profile("¿qué es x?"), TeamPreset.Auto, [new TeamStep(TeamAgent.Haiku, TeamRole.Answer, false, 1, "")]), "¿qué es x?").Wait();
        check("equipo: un fallo corriente se sigue reintentando y escalando como antes (4 intentos, tope en Sonnet y Opus)", calls == 4);

        // Cola: se puede quitar una tarea que espera, y la que corre sigue su curso.
        var gateOpen = new TaskCompletionSource();
        var ran = new List<string>();
        var q = new TeamService(new TeamStore(TempDir("st")), async (step, prompt, dir, ct) => { lock (ran) ran.Add(prompt); await gateOpen.Task; return new AgentResult(true, "ok"); }, r => TempDir("bd"));
        var first = new TeamJob("primera", "", TeamPreset.Auto, [new TeamStep(TeamAgent.Haiku, TeamRole.Answer, false, 1, "")], TeamRouter.Profile("¿primera?"));
        var second = first with { Task = "segunda" };
        q.Enqueue(first); q.Enqueue(second);
        SpinWait.SpinUntil(() => q.Current is not null, 5000);
        bool removed = q.Remove(second), removedAgain = q.Remove(second), busyWhileRunning = q.Busy;
        gateOpen.SetResult();
        q.WhenIdle().Wait();
        check("equipo: se quita de la cola una tarea que espera (una sola vez), la que corre termina y Busy no parpadea", removed && !removedAgain && busyWhileRunning && ran.Count == 1 && !q.Busy);

        // Aprobar sin texto (o solo espacios) aprueba el plan tal cual: no manda un plan vacío al implementador.
        string? approved = "sin respuesta";
        var gated = new TeamService(new TeamStore(TempDir("st")), (step, prompt, dir, ct) => Task.FromResult(new AgentResult(true, step.Role == TeamRole.Plan ? "el plan" : "hecho")), r => TempDir("bd"));
        var gatedRepo = MakeRepo();
        gated.Changed += () => { if (gated.Pending is not null) gated.Approve("   "); };
        gated.Enqueue(new TeamJob("añade algo en a.txt", gatedRepo, TeamPreset.Auto, [new TeamStep(TeamAgent.Sonnet, TeamRole.Plan, false, 1, ""), new TeamStep(TeamAgent.Codex, TeamRole.Implement, true, 1, "")], TeamRouter.Profile("añade algo en a.txt")) { ReviewPlan = true });
        gated.WhenIdle().Wait();
        approved = gated.Store.LoadHistory().FirstOrDefault()?.Outcome;
        check("equipo: aprobar con el cuadro vacío aprueba el plan original en vez de cancelar o mandar vacío", approved == "Done");
        Delete(gatedRepo);

        check("equipo: el registro enseña el comando sin la ruta del intérprete ni sus opciones",
            TeamActivity.FriendlyCommand("\"C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\" -Command \"git status\"") == "git status"
            && TeamActivity.FriendlyCommand("git diff --stat") == "git diff --stat" && TeamActivity.FriendlyCommand("bash -lc 'ls src'") == "ls src");
        // Home: las cuatro primeras celdas se ven al abrirlo y el Home admite más, que aparecen al desplazarte; la vista corta sigue en cuatro.
        var six = new[] { "system", "clock", "pomodoro", "awake", "battery", "media" };
        check("Home: admite más de cuatro celdas (hasta doce) y conserva su orden, mientras que la vista corta se queda en cuatro",
            WidgetLayout.Normalize(six, max: WidgetCatalog.MaxHomeCells).SequenceEqual(six) && WidgetLayout.Normalize(six).Count == 4 && WidgetLayout.Normalize(six).SequenceEqual(six.Take(4))
            && WidgetCatalog.MaxHomeCells > WidgetCatalog.MaxCells && WidgetLayout.Cells(WidgetLayout.Normalize(WidgetCatalog.All.Select(w => w.Id), max: WidgetCatalog.MaxHomeCells)) <= WidgetCatalog.MaxHomeCells);
        var full4 = new List<string> { "system", "clock", "pomodoro", "awake" };
        check("Home: con las cuatro celdas de la primera pantalla llenas aún se puede añadir (hasta doce), y a la vista corta no",
            WidgetLayout.CanAdd(full4, "battery", WidgetCatalog.MaxHomeCells) && !WidgetLayout.CanAdd(full4, "battery") && WidgetLayout.Add(full4, "battery", WidgetCatalog.MaxHomeCells).Count == 5
            && WidgetLayout.ToggleWidth(full4, "clock", WidgetCatalog.MaxHomeCells).Contains("clock@2") && !WidgetLayout.ToggleWidth(full4, "clock").Contains("clock@2"));

        // Home como cuadrícula: tamaños «id@CxF», área en celdas y ciclo 1×1 → 2×1 → 2×2 → 1×2.
        check("Home en cuadrícula: lee tamaños columnas×filas, pesa su área y cicla los tamaños que caben",
            WidgetLayout.Span("clock@2x2") == 2 && WidgetLayout.Rows("clock@2x2") == 2 && WidgetLayout.Rows("system") == 1 && WidgetLayout.Rows("clock") == 2 && WidgetLayout.Rows("clock@2x2", home: false) == 1
            && WidgetLayout.Area(new[] { "clock", "system", "notes" }) == 2 + 1 + 2
            && WidgetLayout.ToggleWidth(new[] { "system" }, "system", 12, home: true)[0] == "system@2"
            && WidgetLayout.ToggleWidth(new[] { "system@2" }, "system", 12, home: true)[0] == "system@2x2"
            && WidgetLayout.ToggleWidth(new[] { "system@2x2" }, "system", 12, home: true)[0] == "system@1x2"
            && WidgetLayout.ToggleWidth(new[] { "system@1x2" }, "system", 12, home: true)[0] == "system"
            && WidgetLayout.Normalize(new[] { "clock@1x2", "team@2x2", "notes" }, max: 12, home: true).SequenceEqual(new[] { "clock", "team@2", "notes" })
            && WidgetLayout.ArrangeHome(new[] { "system" })[0] is { Cols: 2 });

        // Baterías de los aparatos: tipo por clase y por nombre, nombres sin los sufijos de Bluetooth, sin repetidos y en orden.
        check("baterías: clasifica por clase de Windows y por nombre, y limpia los nombres de los perfiles",
            DeviceKinds.Classify("Keychron K3", classes: ["Keyboard"]) == DeviceKind.Keyboard && DeviceKinds.Classify("Acer NGR300", classes: ["Mouse"]) == DeviceKind.Mouse
            && DeviceKinds.Classify("Cubitt Power Buds") == DeviceKind.Headphones && DeviceKinds.Classify("Cubitt Altavoz") == DeviceKind.Speaker
            && DeviceKinds.Classify("Xbox Wireless Controller") == DeviceKind.Gamepad && DeviceKinds.Classify("Cosa rara") == DeviceKind.Other && DeviceKinds.Classify("Cosa rara", handsFree: true) == DeviceKind.Headphones
            && DeviceKinds.CleanName("Cubitt Headphones Hands-Free AG") == "Cubitt Headphones" && DeviceKinds.CleanName("Keychron K3") == "Keychron K3");
        var devices = DeviceKinds.Merge([
            new DeviceBattery("1", "Teclado", DeviceKind.Keyboard, 98), new DeviceBattery("2", "Este equipo", DeviceKind.Laptop, 72), new DeviceBattery("3", "Auriculares", DeviceKind.Headphones, 90),
            new DeviceBattery("4", "auriculares", DeviceKind.Headphones, 88), new DeviceBattery("5", "Roto", DeviceKind.Other, 140), new DeviceBattery("6", "Ratón", DeviceKind.Mouse, 64)]);
        check("baterías: sin repetidos ni valores imposibles, el equipo primero y luego por tipo",
            devices.Select(d => d.Name).SequenceEqual(["Este equipo", "Auriculares", "Ratón", "Teclado"]) && devices[1].Percent == 90);

        // Luces LED: tramas de nueve bytes, nombres de tira reconocibles, colores HSV y alarmas.
        var ledFrames = new[] { LedProtocol.Power(true), LedProtocol.Power(false), LedProtocol.Color(1, 2, 3), LedProtocol.Brightness(500), LedProtocol.Effect(0x88), LedProtocol.Speed(-4),
            LedProtocol.Power(true, LedFamily.ElkAlt), LedProtocol.Color(1, 2, 3, LedFamily.ElkAlt), LedProtocol.Brightness(50, LedFamily.ElkAlt) };
        check("luces: toda orden son nueve bytes, de 0x7E a 0xEF, y el brillo y la velocidad no se salen de 0–100",
            ledFrames.All(f => f.Length == 9 && f[0] == 0x7E && f[8] == 0xEF) && LedProtocol.Brightness(500)[3] == 100 && LedProtocol.Speed(-4)[3] == 0
            && LedProtocol.Color(255, 0, 128).SequenceEqual(new byte[] { 0x7E, 0x00, 0x05, 0x03, 255, 0, 128, 0x00, 0xEF }) && LedProtocol.Power(false)[5] == 0x00 && LedProtocol.Power(true)[3] == 0xF0);
        check("luces: reconoce las tiras ELK y MELK por su nombre y no toca otros dispositivos",
            LedProtocol.IsLedName("ELK-BLEDOM") && LedProtocol.IsLedName("MELK-OA10   04") && !LedProtocol.IsLedName("Keychron K3") && !LedProtocol.IsLedName(null)
            && LedProtocol.FamilyFor("MELK-OA10") == LedFamily.ElkAlt && LedProtocol.FamilyFor("ELK-BLEDOM") == LedFamily.Elk);
        check("luces: interpreta la dirección que Windows anuncia para un dispositivo BLE",
            LedDevice.TryParseBluetoothAddress("be:69:8d:01:d2:05", out var ledAddress) && ledAddress == 0xBE698D01D205
            && !LedDevice.TryParseBluetoothAddress("nombre de tira", out _));
        var (hr, hg, hb) = LedProtocol.FromHsv(120, 1, 1); var back = LedProtocol.ToHsv(255, 128, 0);
        check("luces: pasa de HSV a RGB y vuelta", LedProtocol.FromHsv(0, 1, 1) is (255, 0, 0) && (hr, hg, hb) == (0, 255, 0) && LedProtocol.FromHsv(240, 0, 1) is (255, 255, 255)
            && Math.Abs(back.Hue - 30) < 0.5 && back.Saturation == 1 && back.Value == 1 && LedProtocol.FromHsv(-120, 1, 1) is (0, 0, 255));
        check("luces: los efectos y las escenas tienen códigos únicos y nombres", LedEffects.All.Select(e => e.Code).Distinct().Count() == LedEffects.All.Count && LedEffects.Find(0x88)?.Key == "Salto de 7 colores"
            && LedEffects.Scenes.All(s => s.Key.Length > 0 && s.Brightness is > 0 and <= 100) && LedEffects.Swatches.Count == 10);
        check("luces: cada efecto tiene su clase de movimiento y los colores que se ven en él",
            LedEffects.KindOf(LedEffects.Find(0x88)!) == LedEffectKind.Jump && LedEffects.KindOf(LedEffects.Find(0x8B)!) == LedEffectKind.Fade && LedEffects.KindOf(LedEffects.Find(0x96)!) == LedEffectKind.Blink
            && LedEffects.PaletteOf(LedEffects.Find(0x96)!).Count == 1 && LedEffects.PaletteOf(LedEffects.Find(0x93)!).Count == 2 && LedEffects.PaletteOf(LedEffects.Find(0x89)!).Count == LedEffects.Rainbow.Count);
        var ledDevice = new LedDevice(0xBE2340030C23, "ELK-BLEDOM", LedFamily.Elk) { On = false, R = 0x8A, G = 0x4D, B = 0xFF, Brightness = 70, Effect = 0x88, Speed = 33 };
        var ledBack = LedDevice.Parse(ledDevice.Serialize());
        check("luces: una tira se guarda y se recupera tal cual", ledBack is { Address: 0xBE2340030C23, Name: "ELK-BLEDOM", On: false, R: 0x8A, G: 0x4D, B: 0xFF, Brightness: 70, Effect: 0x88, Speed: 33 } && LedDevice.Parse("basura") is null);
        var ledTimer = new LedTimer { TurnOn = true, Minute = 19 * 60 + 30, Days = 1 };
        check("luces: una alarma suena a su hora, el día que toca, y se guarda y se recupera",
            ledTimer.Matches(new DateTime(2026, 10, 22, 19, 30, 5)) && !ledTimer.Matches(new DateTime(2026, 10, 24, 19, 30, 5)) && !ledTimer.Matches(new DateTime(2026, 10, 22, 19, 31, 0))
            && !new LedTimer { Minute = 0, Enabled = false }.Matches(new DateTime(2026, 10, 22, 0, 0, 0)) && LedTimer.Parse(ledTimer.Serialize()) is { TurnOn: true, Minute: 1170, Days: 1, Enabled: true } && LedTimer.Parse("x") is null);

        check("clima: la lista de ciudades no repite ids, cada país tiene ciudades y cada ciudad su país, con coordenadas posibles",
            WeatherPlaces.All.Select(p => p.Id).Distinct().Count() == WeatherPlaces.All.Count
            && WeatherPlaces.Countries.All(c => WeatherPlaces.InCountry(c.Code).Any()) && WeatherPlaces.All.All(p => WeatherPlaces.Countries.Any(c => c.Code == p.Country))
            && WeatherPlaces.All.All(p => p.Lat is >= -90 and <= 90 && p.Lon is >= -180 and <= 180 && p.City("es").Length > 0));
        // Luna nueva el 11/1/2024 11:57 UTC, cuarto creciente el 18/1 03:52 y llena el 25/1 17:54 (efemérides): el ciclo medio acierta con menos de un día.
        double Near(double a, double b) => Math.Min(Math.Abs(a - b), 1 - Math.Abs(a - b));
        check("luna: la fase sale del ciclo medio (nueva, cuarto y llena de enero de 2024) y se nombra",
            Near(MoonPhase.Fraction(new DateTime(2024, 1, 11, 11, 57, 0, DateTimeKind.Utc)), 0) < 0.03 && Near(MoonPhase.Fraction(new DateTime(2024, 1, 25, 17, 54, 0, DateTimeKind.Utc)), 0.5) < 0.03
            && Near(MoonPhase.Fraction(new DateTime(2024, 1, 18, 3, 52, 0, DateTimeKind.Utc)), 0.25) < 0.04 && MoonPhase.Name(0.5) == "Luna llena" && MoonPhase.Name(0.0) == "Luna nueva" && MoonPhase.Name(0.26) == "Cuarto creciente");
        check("clima: lo guardado cuando se escribía a mano («Santiago», «madrid, españa») se reconoce y lo desconocido no",
            WeatherPlaces.Resolve("Santiago")?.Id == "santiago-cl" && WeatherPlaces.Resolve("madrid, españa")?.Id == "madrid-es" && WeatherPlaces.Resolve("lima-pe")?.Country == "pe"
            && WeatherPlaces.Resolve("Narnia") is null && WeatherPlaces.Resolve("") is null);
        check("equipo: el registro cuenta lo que hace un comando o un patrón, no el comando ni el patrón crudos",
            TeamActivity.Describe("ls -la") == "Listando archivos" && TeamActivity.Describe("rg -n -i foo src") == "Buscando en el código"
            && TeamActivity.Describe("dotnet test tests/x") == "Corriendo las pruebas" && TeamActivity.Describe("miscript --x") == "Ejecutando miscript --x");
        check("equipo: de un plan se quita lo dicho antes de su primera sección (charla del modelo consigo mismo)",
            TeamRun.CleanPlan("Confirmado: los espacios de nombres ya están. Tengo lo necesario.\n\n**Objetivo**: añadir X.\n\n**Pasos**: 1. hacer") == "**Objetivo**: añadir X.\n\n**Pasos**: 1. hacer"
            && TeamRun.CleanPlan("**Objetivo**: ya empieza bien.") == "**Objetivo**: ya empieza bien.");
        check("equipo: sin carpeta de proyecto el error lo dice sin un nombre vacío",
            TeamWorktree.CreateAsync("", "x2", TempDir("b0")).Result is (null, { Length: > 0 } msg) && !msg.StartsWith(' '));
        check("equipo: el registro no enseña rutas de disco ni scripts enteros de Codex",
            TeamActivity.FriendlyCommand(@"Get-Content 'C:\Users\x\.codex\skills\a\SKILL.md'") == @"Get-Content 'SKILL.md'"
            && TeamActivity.FriendlyCommand("@'\nconst fs = require(1);\nconsole.log(1)\n'@ | node -") == ""
            && TeamActivity.FriendlyCommand("rg -n -i foo") == "rg -n -i foo");
        var linked = new TeamService(new TeamStore(TempDir("st")), (step, prompt, dir, ct) => Task.FromResult(new AgentResult(true, "Mira [index.html](/C:/Users/x/AppData/index.html) y [la guía](https://a.b/c).")), r => TempDir("bd"));
        linked.Enqueue(new TeamJob("¿qué es x?", "", TeamPreset.Auto, [new TeamStep(TeamAgent.Haiku, TeamRole.Answer, false, 1, "")], TeamRouter.Profile("¿qué es x?")));
        linked.WhenIdle().Wait();
        check("equipo: el informe deja el nombre de los archivos enlazados con rutas del disco, y no toca los enlaces web",
            linked.Store.LoadHistory().FirstOrDefault()?.Report == "Mira index.html y [la guía](https://a.b/c).");
    }

    public static void Run(Action<string, bool> check)
    {
        Fixes(check);
        // Programación: cuándo toca cada tarea.
        var daily = new TeamSchedule { Prompt = "x", Root = "r", Time = "09:00", Created = Mon };
        check("programadas: diaria — toca a las 09:00 y la siguiente es mañana",
            daily.Next == new DateTime(2026, 10, 19, 9, 0, 0) && daily.Check(Mon.AddMinutes(30)) == ScheduleCheck.NotYet && daily.Check(Mon.AddHours(1).AddMinutes(1)) == ScheduleCheck.Due
            && (daily with { LastRun = Mon.AddHours(1) }).Next == new DateTime(2026, 10, 20, 9, 0, 0));
        var weekdays = daily with { Kind = ScheduleKind.Weekdays, LastRun = new DateTime(2026, 10, 23, 9, 0, 0) };   // viernes
        check("programadas: laborables — el viernes salta al lunes", weekdays.Next == new DateTime(2026, 10, 26, 9, 0, 0));
        var weekly = daily with { Kind = ScheduleKind.Weekly, DaysMask = (1 << 2) | (1 << 5), LastRun = Mon.AddHours(2) };   // miércoles y sábado
        check("programadas: semanal — el miércoles y luego el sábado",
            weekly.Next == new DateTime(2026, 10, 21, 9, 0, 0) && (weekly with { LastRun = new DateTime(2026, 10, 21, 9, 0, 0) }).Next == new DateTime(2026, 10, 24, 9, 0, 0));
        var every = daily with { Kind = ScheduleKind.EveryHours, EveryHours = 6, Created = Mon.AddHours(2) };
        check("programadas: cada N horas — desde la creación o la última vez", every.Next == Mon.AddHours(8) && every.Check(Mon.AddHours(7)) == ScheduleCheck.NotYet
            && every.Check(Mon.AddHours(8).AddMinutes(30)) == ScheduleCheck.Due);
        var once = daily with { Kind = ScheduleKind.Once, At = new DateTime(2026, 10, 20, 18, 0, 0) };
        check("programadas: una sola vez — después de hacerse no hay siguiente", once.Check(new DateTime(2026, 10, 20, 18, 5, 0)) == ScheduleCheck.Due
            && (once with { LastRun = new DateTime(2026, 10, 20, 18, 5, 0) }).Next is null);
        var late = Mon.AddHours(14);
        check("programadas: con la app cerrada a su hora no se ejecuta tarde, salvo que se pida", daily.Check(late) == ScheduleCheck.Missed
            && (daily with { CatchUp = true }).Check(late) == ScheduleCheck.Due);
        check("programadas: apagadas, sin tarea o sin proyecto no se lanzan", (daily with { Enabled = false }).Check(late) == ScheduleCheck.NotYet
            && (daily with { Prompt = " " }).Check(late) == ScheduleCheck.NotYet && (daily with { Root = "" }).Check(late) == ScheduleCheck.NotYet);
        check("programadas: una hora ilegible vale las 09:00", (daily with { Time = "tarde" }).TimeOfDay == TimeSpan.FromHours(9));

        // Almacén: historial y programadas en disco.
        var dir = TempDir("store");
        var store = new TeamStore(dir);
        for (int i = 0; i < TeamStore.MaxHistory + 5; i++)
            store.SaveHistory(new TeamHistoryEntry { Id = $"e{i}", BranchId = $"e{i}", Started = Mon.AddMinutes(i), Title = $"t{i}" });
        var history = store.LoadHistory();
        check("historial: lo más reciente primero y con tope", history.Count == TeamStore.MaxHistory && history[0].Id == $"e{TeamStore.MaxHistory + 4}");
        store.SaveHistory(history[0] with { Title = "otro" });
        check("historial: guardar el mismo id lo reemplaza", store.LoadHistory().Count(e => e.Id == history[0].Id) == 1 && store.LoadHistory()[0].Title == "otro");
        store.SaveHistory(new TeamHistoryEntry { Id = "p", BranchId = "p", Started = Mon.AddDays(1) });
        store.SaveHistory(new TeamHistoryEntry { Id = "q", BranchId = "p", ParentId = "p", Started = Mon.AddDays(2) });
        store.SetState("p", "Merged");
        check("historial: fusionar o descartar una rama marca también sus seguimientos", store.LoadHistory().Where(e => e.BranchId == "p").All(e => e.State == "Merged"));
        store.ClearHistory();
        check("historial: se puede borrar entero", store.LoadHistory().Count == 0);
        store.UpsertSchedule(daily);
        store.UpsertSchedule(daily with { Title = "nueva" });
        check("programadas: se guardan sin duplicar y se borran", store.LoadSchedules() is [{ Title: "nueva" }]);
        store.DeleteSchedule(daily.Id);
        check("programadas: borrar la última deja la lista vacía", store.LoadSchedules().Count == 0);
        File.WriteAllText(Path.Combine(dir, "history.json"), "{no es json");
        check("historial: un archivo roto no tumba nada", store.LoadHistory().Count == 0);
        Delete(dir);

        var corruptStoreDir = TempDir("store-corrupt"); Directory.CreateDirectory(corruptStoreDir);
        var corruptHistory = Path.Combine(corruptStoreDir, "history.json"); File.WriteAllText(corruptHistory, "{historial roto");
        var corruptStore = new TeamStore(corruptStoreDir);
        var historyBefore = File.ReadAllText(corruptHistory);
        bool historyKept = corruptStore.LoadHistory().Count == 0 && !string.IsNullOrWhiteSpace(corruptStore.LastError)
            && !corruptStore.SaveHistory(new TeamHistoryEntry { Id = "new" }) && File.ReadAllText(corruptHistory) == historyBefore;
        var corruptSchedules = Path.Combine(corruptStoreDir, "schedules.json"); File.WriteAllText(corruptSchedules, "{programación rota");
        var schedulesBefore = File.ReadAllText(corruptSchedules);
        bool schedulesKept = corruptStore.LoadSchedules().Count == 0 && !string.IsNullOrWhiteSpace(corruptStore.LastError)
            && !corruptStore.UpsertSchedule(daily) && File.ReadAllText(corruptSchedules) == schedulesBefore;
        check("equipo: conserva historial y tareas programadas corruptas, muestra el error y bloquea sobrescribirlas", historyKept && schedulesKept);
        Delete(corruptStoreDir);

        var blockedStorePath = TempDir("store-blocked");
        File.WriteAllText(blockedStorePath, "file where folder should be");
        var blockedStore = new TeamStore(blockedStorePath);
        check("historial: un fallo de escritura se devuelve y expone en vez de ocultarse",
            !blockedStore.SaveHistory(new TeamHistoryEntry { Id = "failure" }) && !string.IsNullOrWhiteSpace(blockedStore.LastError));
        File.Delete(blockedStorePath);

        var leaseDir = TempDir("schedule-lease");
        var leaseStoreA = new TeamStore(leaseDir);
        var leaseStoreB = new TeamStore(leaseDir);
        var leaseSchedule = daily with { Id = "lease", LastRun = null, Created = Mon.AddDays(-1) };
        leaseStoreA.UpsertSchedule(leaseSchedule);
        var claimA = leaseStoreA.ClaimSchedule(leaseSchedule.Id, Mon.AddDays(1), runNow: true, out _, out var leaseA);
        var claimB = leaseStoreB.ClaimSchedule(leaseSchedule.Id, Mon.AddDays(1), runNow: true, out _, out var leaseB);
        leaseA?.Dispose();
        var claimAfterRelease = leaseStoreB.ClaimSchedule(leaseSchedule.Id, Mon.AddDays(1), runNow: true, out _, out leaseB);
        leaseB?.Dispose();
        check("programadas: el bloqueo compartido impide dos ejecuciones simultáneas desde instancias separadas y se libera al terminar",
            claimA == ScheduleClaimResult.Claimed && claimB == ScheduleClaimResult.Busy && claimAfterRelease == ScheduleClaimResult.Claimed);

        var signal = Path.Combine(leaseDir, "child-claimed");
        var release = Path.Combine(leaseDir, "child-release");
        var processPath = Environment.ProcessPath!;
        var process = new System.Diagnostics.ProcessStartInfo(processPath) { UseShellExecute = false, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            process.ArgumentList.Add(typeof(TeamServiceTests).Assembly.Location);
        process.ArgumentList.Add("--claim-team-schedule");
        process.ArgumentList.Add(leaseDir);
        process.ArgumentList.Add(leaseSchedule.Id);
        process.ArgumentList.Add(signal);
        process.ArgumentList.Add(release);
        using (var child = System.Diagnostics.Process.Start(process)!)
        {
            var signaled = SpinWait.SpinUntil(() => File.Exists(signal) || child.HasExited, TimeSpan.FromSeconds(10));
            var childClaimed = File.Exists(signal) && File.ReadAllText(signal) == "claimed";
            var parentClaim = leaseStoreA.ClaimSchedule(leaseSchedule.Id, Mon.AddDays(1), runNow: true, out _, out var parentLease);
            parentLease?.Dispose();
            File.WriteAllText(release, "release");
            child.WaitForExit(10_000);
            check("programadas: dos procesos no reclaman la misma ejecución a la vez",
                signaled && childClaimed && parentClaim == ScheduleClaimResult.Busy && child.ExitCode == 0);
        }
        Delete(leaseDir);

        var cmdDir = TempDir("cmd launch");
        Directory.CreateDirectory(cmdDir);
        var cmdScript = Path.Combine(cmdDir, "cli shim.cmd");
        File.WriteAllText(cmdScript, "@echo off\r\necho [%~1]\r\n");
        var cmdResult = TeamProcess.RunAsync(cmdScript, ["argument with spaces"], "", cmdDir, TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        check("equipo: el CLI .cmd se ejecuta a través de cmd.exe y conserva rutas y argumentos con espacios",
            cmdResult.Exit == 0 && cmdResult.Out.Contains("[argument with spaces]"));
        Delete(cmdDir);

        // Git: archivos cambiados.
        check("git: lee numstat, con binarios y rutas con tabulador",
            TeamGit.ParseNumstat("3\t1\ta.txt\n-\t-\timg.png\n0\t5\tdir/b.cs\n") is [{ Path: "a.txt", Added: 3, Removed: 1 }, { Path: "img.png", Added: 0, Removed: 0 }, { Path: "dir/b.cs", Added: 0, Removed: 5 }]);

        // TeamRun: aprobar o editar el plan, tope de tokens y contexto.
        AgentResult Ok(string text, long tokens = 10) => new(true, text, tokens, 0);
        var seen = new List<string>();
        var planSteps = new[] { new TeamStep(TeamAgent.Sonnet, TeamRole.Plan, false, 5, ""), new TeamStep(TeamAgent.Codex, TeamRole.Implement, true, 5, "") };
        var plan = new TeamPlan(TeamRouter.Profile("x"), TeamPreset.Auto, planSteps);
        var edited = new TeamRun((s, p, _) => { seen.Add(p); return Task.FromResult(Ok(s.Role == TeamRole.Plan ? "PLAN ORIGINAL" : "hecho")); },
            gate: (_, _, _) => Task.FromResult<string?>("PLAN EDITADO")).RunAsync(plan, "tarea").Result;
        check("equipo: el plan editado por el usuario es el que recibe quien implementa", edited.Outcome == TeamOutcome.Done && seen[^1].Contains("PLAN EDITADO") && !seen[^1].Contains("PLAN ORIGINAL"));
        var rejected = new TeamRun((_, _, _) => Task.FromResult(Ok("plan")), gate: (_, _, _) => Task.FromResult<string?>(null)).RunAsync(plan, "tarea").Result;
        check("equipo: rechazar el plan cancela sin implementar", rejected.Outcome == TeamOutcome.Cancelled && rejected.Records.Count == 1);
        var budget = new TeamRun((_, _, _) => Task.FromResult(Ok("hecho", 600)), maxTokens: 1000).RunAsync(plan, "tarea").Result;
        check("equipo: al pasar el tope de tokens se para y pide decisión", budget.Outcome == TeamOutcome.NeedsUser && budget.Summary.Contains("presupuesto") && budget.Records.Count == 2);
        seen.Clear();
        new TeamRun((_, p, _) => { seen.Add(p); return Task.FromResult(Ok("hecho")); }, context: "YA SE HIZO ESTO").RunAsync(plan, "cambia el color").Wait();
        check("equipo: un seguimiento lleva lo ya hecho como contexto", seen.All(p => p.Contains("YA SE HIZO ESTO") && p.Contains("cambia el color")));

        // Servicio: cola, rama, historial, fusionar, descartar y seguimiento (con agentes simulados y un repo temporal).
        var repo = MakeRepo();
        var trees = TempDir("trees");
        var data = TempDir("data");
        var service = new TeamService(new TeamStore(data), (step, prompt, work, _) =>
        {
            if (step.Role == TeamRole.Implement && prompt.Contains("escribe")) File.WriteAllText(Path.Combine(work, "nuevo.txt"), "contenido\nde\nprueba\n");
            return Task.FromResult(step.Role == TeamRole.Review ? Ok("{\"verdict\":\"APPROVED\"}") : Ok("informe de " + step.Agent));
        }, _ => trees);
        var finished = new List<TeamHistoryEntry>();
        service.Finished += finished.Add;
        service.Enqueue(Job(repo, "escribe un archivo", new TeamStep(TeamAgent.Codex, TeamRole.Implement, true, 5, ""), new TeamStep(TeamAgent.Sonnet, TeamRole.Review, false, 5, "")));
        service.Enqueue(Job(repo, "no toques nada"));
        service.WhenIdle().Wait();
        check("servicio: dos tareas en cola corren de una en una y las dos terminan", finished.Count == 2 && finished[0].Title.StartsWith("escribe") && !service.Busy);
        var first = finished[0];
        check("servicio: guarda en la rama lo que hizo (archivos, líneas, veredicto, informe)",
            first.Outcome == "Done" && first is { Files: 1, Added: 3, Removed: 0, Verdict: "Approved" } && first.Report.Contains("informe de Codex") && first.Steps.Count == 2
            && Git(repo, "branch", "--list", $"team/{first.BranchId}").Contains(first.BranchId) && !File.Exists(Path.Combine(repo, "nuevo.txt")));
        check("servicio: sin cambios la copia de trabajo se borra sola", finished[1] is { Files: 0, State: "Discarded" } && !Directory.Exists(finished[1].TreeDir));
        check("servicio: el historial queda en disco con las dos", service.Store.LoadHistory().Count == 2);
        var diff = TeamGit.DiffAsync(first.TreeDir, first.BaseSha).Result;
        check("servicio: se puede ver el diff de la rama sin abrir otra app", diff.Contains("+contenido") && diff.Contains("nuevo.txt")
            && TeamGit.ChangesAsync(first.TreeDir, first.BaseSha).Result is [{ Path: "nuevo.txt", Added: 3 }]);

        var follow = TeamService.FollowUp(first, "escribe también otra cosa", []);
        service.Enqueue(follow);
        service.WhenIdle().Wait();
        var second = finished[^1];
        check("servicio: un seguimiento sigue en la misma rama, con el informe anterior de contexto",
            second.BranchId == first.BranchId && second.ParentId == first.Id && second.TreeDir == first.TreeDir && second.Id != first.Id && second.Outcome == "Done");
        var merge = service.MergeAsync(second).Result;
        check("servicio: fusionar trae el cambio al repo, borra la copia y marca todo como fusionado",
            merge is null && File.Exists(Path.Combine(repo, "nuevo.txt")) && !Directory.Exists(first.TreeDir) && service.Store.LoadHistory().Where(e => e.BranchId == first.BranchId).All(e => e.State == "Merged"));

        var attachDir = TempDir("adjuntos");
        Directory.CreateDirectory(attachDir);
        File.WriteAllBytes(Path.Combine(attachDir, "captura.png"), [1, 2, 3]);
        service.Enqueue(Job(repo, "con imagen") with { AttachmentDir = attachDir });
        service.WhenIdle().Wait();
        check("servicio: al terminar la tarea se borra la carpeta de sus imágenes adjuntas", !Directory.Exists(attachDir));

        var throwData = TempDir("throw-data");
        var throwAttachments = TempDir("throw-attachments");
        Directory.CreateDirectory(throwAttachments);
        File.WriteAllText(Path.Combine(throwAttachments, "image.png"), "temporary");
        var throwingService = new TeamService(new TeamStore(throwData), (_, _, _, _) => Task.FromException<AgentResult>(new IOException("executor exploded")));
        var questionProfile = TeamRouter.Profile("¿qué es X?");
        throwingService.Enqueue(new TeamJob("¿qué es X?", "", TeamPreset.CodexOnly,
            [new TeamStep(TeamAgent.Codex, TeamRole.Answer, false, 1, "")], questionProfile) { AttachmentDir = throwAttachments });
        throwingService.WhenIdle().GetAwaiter().GetResult();
        var failedExecution = throwingService.Store.LoadHistory().SingleOrDefault();
        check("servicio: una excepción del ejecutor deja un fallo en el historial y borra los adjuntos temporales",
            failedExecution is { Outcome: "Failed" } && failedExecution.Report.Contains("executor exploded") && !Directory.Exists(throwAttachments));
        Delete(throwData);

        var answerService = new TeamService(new TeamStore(TempDir("dataq")), (step, _, work, _) => Task.FromResult(Ok("Es el médico del oído, la nariz y la garganta.")), _ => trees);
        TeamHistoryEntry? answered = null;
        answerService.Finished += e => answered = e;
        var answerProfile = TeamRouter.Profile("¿Qué significa otorrinolaringólogo?");
        answerService.Enqueue(new TeamJob("¿Qué significa otorrinolaringólogo?", "", TeamPreset.Auto, TeamRouter.Route(answerProfile).Steps, answerProfile));
        answerService.WhenIdle().Wait();
        check("servicio: una pregunta sencilla se responde sin proyecto: sin rama, sin cambios y con la respuesta entera en el historial",
            answered is { State: "Answered", Outcome: "Done", Files: 0, BranchId: "" } && answered.Report.StartsWith("Es el médico")
            && (!Directory.Exists(trees) || !Directory.EnumerateDirectories(trees).Any(d => d.Contains("questions"))));   // la carpeta del proyecto se borra cuando queda vacía

        check("servicio: el historial guarda el texto completo de la tarea y un reintento la lanza igual", answered is { Task: "¿Qué significa otorrinolaringólogo?" }
            && TeamService.RetryJob(answered!, answered!.Task, [], reviewPlan: true) is { Task: "¿Qué significa otorrinolaringólogo?", ReviewPlan: true, Profile.Kind: TaskKind.Question } retry
            && retry.Steps.Count == 1 && retry.Steps[0].Role == TeamRole.Answer);

        // Seguimiento de una respuesta ya cerrada: «¿y en inglés?» sigue siendo una pregunta y lleva la conversación anterior.
        var followPrompts = new List<string>();
        var followService = new TeamService(new TeamStore(TempDir("dataf")), (step, prompt, work, _) => { lock (followPrompts) followPrompts.Add(prompt); return Task.FromResult(Ok("It is the doctor of the ear, nose and throat.")); }, _ => trees);
        TeamHistoryEntry? followEntry = null;
        followService.Finished += e => followEntry = e;
        var followJob = TeamService.AnswerFollowUp(answered!, "¿y en inglés?", [], [answered!]);
        followService.Enqueue(followJob);
        followService.WhenIdle().Wait();
        check("servicio: una pregunta de seguimiento sigue siendo pregunta (aunque la frase suelta no lo parezca), la responde el mismo agente y lleva la conversación anterior",
            followJob.Profile.Kind == TaskKind.Question && followJob.Steps is [{ Role: TeamRole.Answer } onlyStep] && onlyStep.Agent.ToString() == answered!.Steps.First(s => s.Role == nameof(TeamRole.Answer)).Agent
            && followJob.ThreadContext!.Contains("Pregunta: ¿Qué significa otorrinolaringólogo?") && followJob.ThreadContext.Contains("Respuesta: Es el médico")
            && followPrompts.Count == 1 && followPrompts[0].Contains("Conversación anterior:") && followPrompts[0].Contains("Nueva pregunta: ¿y en inglés?")
            && followEntry is { State: "Answered", Outcome: "Done" } && followEntry.ParentId == answered.Id);

        var turns = Enumerable.Range(1, 5).Select(i => answered! with { Id = $"t{i}", ParentId = i == 1 ? null : $"t{i - 1}", Task = $"p{i}", Report = $"r{i}" }).ToList();
        var thread = TeamService.Thread(turns[4], turns);
        var shortThread = TeamService.Thread(turns[4], turns, maxChars: 30);
        check("servicio: la conversación de un seguimiento lleva hasta tres turnos, del más viejo al más nuevo, y si no cabe se queda con lo más reciente",
            thread.StartsWith("Pregunta: p3") && thread.EndsWith("Respuesta: r5") && !thread.Contains("p2") && shortThread.Length <= 31 && shortThread.EndsWith("Respuesta: r5"));

        check("servicio: las carpetas de las tareas del equipo (rama aparte y preguntas) no cuentan como sesiones de otras apps",
            TeamService.OwnsPath(Path.Combine(Paths.DataDir, "team", "repo-1a2b3c4d", "261020-120000")) && TeamService.OwnsPath(Path.Combine(Path.GetTempPath(), "its-team-quick", "261020-120000"))
            && !TeamService.OwnsPath(@"C:\proyectos\web") && !TeamService.OwnsPath("") && !TeamService.OwnsPath(null));

        // Aprobación del plan desde el servicio (la interfaz llama a Approve con el texto editado).
        var prompts = new List<string>();
        var gated = new TeamService(new TeamStore(TempDir("data2")), (step, prompt, work, _) => { lock (prompts) prompts.Add(prompt); return Task.FromResult(Ok(step.Role == TeamRole.Plan ? "PLAN DEL AGENTE" : "hecho")); }, _ => trees);
        gated.Enqueue(Job(repo, "tarea con plan", new TeamStep(TeamAgent.Sonnet, TeamRole.Plan, false, 5, ""), new TeamStep(TeamAgent.Codex, TeamRole.Implement, true, 5, "")) with { ReviewPlan = true });
        for (int i = 0; i < 100 && gated.Pending is null; i++) Thread.Sleep(50);
        check("servicio: con «revisar el plan» se para y enseña el plan del agente", gated.Pending is { Text: "PLAN DEL AGENTE" } && gated.Current is not null);
        gated.Approve("PLAN QUE EDITÉ YO");
        gated.WhenIdle().Wait();
        check("servicio: tras aprobar sigue con el plan editado", prompts[^1].Contains("PLAN QUE EDITÉ YO") && gated.Pending is null);

        var agentStarted = new ManualResetEventSlim();
        var cancelled = new TeamService(new TeamStore(TempDir("data3")), async (_, _, _, ct) => { agentStarted.Set(); await Task.Delay(Timeout.Infinite, ct); return Ok("nunca"); }, _ => trees);
        TeamHistoryEntry? cancelledEntry = null;
        cancelled.Finished += e => cancelledEntry = e;
        cancelled.Enqueue(Job(repo, "tarea larga"));
        // Cancelar solo cuando el agente ya está trabajando (una espera fija falla con el PC cargado).
        agentStarted.Wait(TimeSpan.FromSeconds(30));
        cancelled.Cancel();
        cancelled.WhenIdle().Wait();
        check("servicio: cancelar para el paso en curso y lo deja como cancelado", cancelledEntry is { Outcome: "Cancelled" });

        // Programador: lanza lo que toca, sin apilar, sin ejecutar tarde y sin fusionar.
        var schedService = new TeamService(new TeamStore(TempDir("data4")), (step, _, work, _) =>
        {
            if (step.Role == TeamRole.Implement) File.WriteAllText(Path.Combine(work, "programada.txt"), "x");
            return Task.FromResult(step.Role == TeamRole.Review ? Ok("{\"verdict\":\"APPROVED\"}") : Ok("hecho"));
        }, _ => trees);
        var scheduler = new TeamScheduler(schedService, () => CodexModels.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "codex-models")));
        var sched = new TeamSchedule { Title = "diaria", Prompt = "crea el informe diario en informe.md: debe resumir", Root = repo, Time = "09:00", Created = Mon, MaxTokens = 5000 };
        schedService.Store.UpsertSchedule(sched);
        check("programador: antes de su hora no lanza nada", scheduler.Tick(Mon.AddMinutes(30)).Count == 0);
        var launched = scheduler.Tick(Mon.AddHours(1).AddMinutes(1));
        check("programador: a su hora la lanza una vez", launched is [{ Result: TeamScheduler.Launch.Started }]);
        schedService.WhenIdle().Wait();
        var ran = schedService.Store.LoadHistory().Single();
        check("programador: deja la rama sin fusionar y apunta el resultado en la programada",
            ran is { ScheduleId: var sid, State: "Open", Outcome: "Done" } && sid == sched.Id && !File.Exists(Path.Combine(repo, "programada.txt"))
            && schedService.Store.LoadSchedules().Single() is { LastOutcome: "Done", LastRun: not null });
        check("programador: el historial lleva el nombre que el usuario dio a la tarea", ran.Title.StartsWith("diaria: crea el informe diario"));
        check("programador: no repite la misma hasta su siguiente hora", scheduler.Tick(Mon.AddHours(2)).Count == 0);
        var missed = scheduler.Tick(Mon.AddDays(1).AddHours(14));
        check("programador: con la app cerrada a su hora la salta y cuenta desde ahora", missed is [{ Result: TeamScheduler.Launch.SkippedMissed }]
            && schedService.Store.LoadSchedules().Single().LastOutcome == "Missed");
        var onceSched = sched with { Id = "una", Kind = ScheduleKind.Once, At = Mon.AddDays(5), LastRun = null, Created = Mon };
        schedService.Store.UpsertSchedule(onceSched);
        scheduler.Tick(Mon.AddDays(5).AddMinutes(1));
        schedService.WhenIdle().Wait();
        check("programador: una tarea de una sola vez se apaga después de lanzarse", schedService.Store.LoadSchedules().Single(s => s.Id == "una") is { Enabled: false });
        schedService.Store.UpsertSchedule(sched with { Id = "mala", Root = Path.Combine(repo, "no-existe"), LastRun = null, Created = Mon.AddDays(-9) });
        check("programador: un proyecto que ya no existe se anota como fallo, sin lanzar nada",
            scheduler.RunNow("mala", Mon.AddDays(6)) == TeamScheduler.Launch.SkippedInvalid && schedService.Store.LoadSchedules().Single(s => s.Id == "mala").LastOutcome == "Failed");
        var job = scheduler.BuildJob(sched with { Preset = nameof(TeamPreset.CodexOnly) });
        check("programador: la tarea sale enrutada, con el modelo real de Codex y sin pedir aprobación",
            job.Steps[0] is { Agent: TeamAgent.Codex, Model: not null, Effort: not null } && !job.ReviewPlan && job.MaxTokens == 5000);

        Delete(repo, trees, data);
    }
}
