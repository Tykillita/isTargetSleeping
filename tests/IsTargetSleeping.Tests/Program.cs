using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using IsTargetSleeping;

// Banco de pruebas de la lógica pura: .\build.ps1 -Test
int failures = 0;
void Check(string name, bool ok)
{
    Console.WriteLine(ok ? $"✓ {name}" : $"✗ {name}");
    if (!ok) failures++;
}
void Section(string name) => Console.WriteLine($"\n— {name}");

var t0 = new DateTime(2026, 9, 14, 12, 0, 0);
DateTime At(double s) => t0.AddSeconds(s);

// MARK: IdleTracker

Section("IdleTracker");
Dictionary<int, double> Cpu(int pid, double seconds) => new() { [pid] = seconds };
HashSet<string> Models(string name) => [name];

var idle = new IdleTracker(t0);
idle.Sample(Cpu(100, 6.590), Models("gemma4"), At(0));
Check("un modelo nuevo cuenta como uso", idle.IdleSeconds(At(0)) == 0);

// Reposo medido: +0,003 s y +0,058 s en tandas de 15 s.
idle.Sample(Cpu(100, 6.593), Models("gemma4"), At(15));
idle.Sample(Cpu(100, 6.651), Models("gemma4"), At(30));
Check("el ruido de reposo no reinicia el contador", idle.IdleSeconds(At(30)) == 30);

// Generar 104 tokens: +0,988 s.
idle.Sample(Cpu(100, 7.639), Models("gemma4"), At(40));
Check("generar reinicia el contador", idle.IdleSeconds(At(40)) == 0);

idle.Sample(Cpu(100, 7.645), Models("gemma4"), At(100));
Check("vuelve a contar tras generar", idle.IdleSeconds(At(100)) == 60);

idle.Sample(Cpu(200, 0.5), Models("gemma4"), At(110));
Check("un runner distinto (recarga) cuenta como uso", idle.IdleSeconds(At(110)) == 0);

idle.Sample(Cpu(200, 0.5), Models("qwen"), At(120));
Check("cambiar de modelo cuenta como uso", idle.IdleSeconds(At(120)) == 0);

idle.Sample(Cpu(200, 0.5), Models("qwen"), At(200));
idle.Touch(At(210));
Check("una acción manual cuenta como uso", idle.IdleSeconds(At(215)) == 5);

idle.Sample(Cpu(200, 0.3), Models("qwen"), At(220));
Check("un contador de CPU que baja no rompe nada", idle.IdleSeconds(At(220)) == 10);

idle.Sample(Cpu(200, 0.3 + IdleTracker.ActivityThreshold), Models("qwen"), At(230));
Check("justo en el umbral cuenta como uso", idle.IdleSeconds(At(230)) == 0);

// Medido en Windows (Ollama 0.34, llama-server en una RTX 4060): 0,008 s cada 2,5 s en reposo.
var win = new IdleTracker(t0);
win.Sample(Cpu(300, 64.200), Models("gemma-4-e2b"), At(0));
for (int i = 1; i <= 24; i++) win.Sample(Cpu(300, 64.200 + 0.008 * i), Models("gemma-4-e2b"), At(2.5 * i));
Check("Windows: el reposo del llama-server no cuenta como uso", win.IdleSeconds(At(60)) == 60);

// Varios modelos a la vez: basta con que uno genere.
var multi = new IdleTracker(t0);
multi.Sample(new() { [1] = 1.0, [2] = 5.0 }, ["a", "b"], At(0));
multi.Sample(new() { [1] = 1.0, [2] = 5.0 }, ["a", "b"], At(30));
multi.Sample(new() { [1] = 1.0, [2] = 6.2 }, ["a", "b"], At(40));
Check("con dos modelos, uno generando es uso", multi.IdleSeconds(At(40)) == 0);

// MARK: Watchdog

Section("Vigilante");
var wd = new Watchdog();
Check("encendido y respondiendo: nada", wd.Sample(true, true, true, At(0)) == WatchdogAction.None);
Check("caída: la primera muestra espera", wd.Sample(true, false, false, At(2.5)) == WatchdogAction.None);
Check("caída: la segunda reinicia", wd.Sample(true, false, false, At(5)) == WatchdogAction.Restart);

var hang = new Watchdog();
Check("cuelgue: 1.ª muestra sin API con servidor vivo, nada", hang.Sample(true, false, true, At(0)) == WatchdogAction.None);
Check("cuelgue: 2.ª, nada", hang.Sample(true, false, true, At(2.5)) == WatchdogAction.None);
Check("cuelgue: 3.ª, matar y reiniciar", hang.Sample(true, false, true, At(5)) == WatchdogAction.KillAndRestart);
Check("una respuesta a tiempo reinicia la cuenta del cuelgue",
    hang.Sample(true, false, true, At(7.5)) == WatchdogAction.None && hang.Sample(true, true, true, At(10)) == WatchdogAction.None
    && hang.Sample(true, false, true, At(12.5)) == WatchdogAction.None && hang.Sample(true, false, true, At(15)) == WatchdogAction.None);

var off = new Watchdog();
off.Sample(false, false, false, At(0));
Check("apagado por el usuario (sin armar): no actúa", off.Sample(false, false, false, At(2.5)) == WatchdogAction.None);

var limit = new Watchdog();
var actions = new List<WatchdogAction>();
for (int i = 0; i < 10; i++) actions.Add(limit.Sample(true, false, false, At(i * 30)));
Check("máximo 3 reinicios en 10 min y luego se rinde una vez",
    actions.Count(a => a == WatchdogAction.Restart) == 3 && actions.Count(a => a == WatchdogAction.GiveUp) == 1 && limit.GaveUp);
Check("rendido: no insiste", limit.Sample(true, false, false, At(400)) == WatchdogAction.None);
limit.Sample(true, true, true, At(1000));
Check("si Ollama vuelve a responder, se rearma", !limit.GaveUp
    && limit.Sample(true, false, false, At(1300)) == WatchdogAction.None && limit.Sample(true, false, false, At(1302.5)) == WatchdogAction.Restart);

// MARK: modo juego

Section("Modo juego");
var game = new GameModeTracker();
Check("un juego recién abierto no activa nada", game.Sample("Hollow Knight", At(0)) == GameTransition.None);
Check("a los 4 s todavía no", game.Sample("Hollow Knight", At(4)) == GameTransition.None && !game.Active);
Check("a los 5 s entra", game.Sample("Hollow Knight", At(5)) == GameTransition.Enter && game.Active && game.Game == "Hollow Knight");
game.Stopped(["ollama", "llamacpp"]);
Check("cerrar el juego no sale enseguida", game.Sample(null, At(10)) == GameTransition.None && game.Active);
Check("reabrirlo antes de 30 s sigue en modo juego", game.Sample("Hollow Knight", At(30)) == GameTransition.None && game.Active);
game.Sample(null, At(31));
Check("a los 29 s de cerrarlo sigue", game.Sample(null, At(59)) == GameTransition.None);
Check("a los 30 s sale", game.Sample(null, At(60)) == GameTransition.Exit && !game.Active);
var restore = game.ToRestore(_ => false);
Check("restaura lo que estaba encendido", restore.Count == 2 && restore.Contains("ollama") && restore.Contains("llamacpp"));

var manual = new GameModeTracker();
manual.Sample("Terraria", At(0));
manual.Sample("Terraria", At(6));
manual.Stopped(["ollama", "llamacpp"]);
manual.Touch("ollama");
manual.Sample(null, At(10));
manual.Sample(null, At(40));
var restore2 = manual.ToRestore(id => id == "ollama");
Check("lo que tocaste a mano durante el juego se respeta", restore2.SequenceEqual(["llamacpp"]));
var none = new GameModeTracker();
none.Sample("X", At(0)); none.Sample("X", At(5)); none.Stopped([]);
Check("sin nada encendido al entrar, nada que restaurar", none.ForceExit() == GameTransition.Exit && none.ToRestore(_ => false).Count == 0);
var already = new GameModeTracker();
already.Sample("X", At(0)); already.Sample("X", At(5)); already.Stopped(["ollama"]); already.ForceExit();
Check("si ya está encendido al salir, no se toca", already.ToRestore(_ => true).Count == 0);

// MARK: bibliotecas de juegos

Section("Bibliotecas de juegos");
var vdf = """
"libraryfolders"
{
	"0"
	{
		"path"		"C:\\Program Files (x86)\\Steam"
		"label"		""
		"apps"
		{
			"228980"		"412709521"
		}
	}
	"1"
	{
		"path"		"D:\\SteamLibrary"
		"label"		""
	}
}
""";
var libs = GameLibraries.ParseSteamLibraries(vdf);
Check("libraryfolders.vdf: las dos bibliotecas", libs.SequenceEqual([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"]));

var epicGame = GameLibraries.ParseEpicManifest("""{"DisplayName":"Sid Meier's Civilization VI","InstallLocation":"D:\\Epic Games\\SidMeiersCivilizationVI","AppCategories":["public","games","applications"]}""");
var epicDlc = GameLibraries.ParseEpicManifest("""{"DisplayName":"Civilization VI : Rise and Fall","InstallLocation":"D:\\Epic Games\\SidMeiersCivilizationVI","AppCategories":["addons"]}""");
Check("manifiesto de Epic: carpeta y nombre", epicGame is { Path: @"D:\Epic Games\SidMeiersCivilizationVI", Name: "Sid Meier's Civilization VI" });
Check("manifiesto de Epic: un DLC no da nombre", epicDlc is { Name: null });
Check("manifiesto roto: null", GameLibraries.ParseEpicManifest("{no es json") is null);

GameRoot[] roots = [new(@"D:\SteamLibrary\steamapps\common"), epicGame!, new(@"C:\Riot Games")];
Check("Steam: el juego es la subcarpeta", GameLibraries.Match(@"D:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight.exe", roots) == "Hollow Knight");
Check("Epic: el nombre del manifiesto", GameLibraries.Match(@"D:\Epic Games\SidMeiersCivilizationVI\Base\Binaries\Win64EOS\CivilizationVI.exe", roots) == "Sid Meier's Civilization VI");
Check("Wallpaper Engine no es un juego", GameLibraries.Match(@"D:\SteamLibrary\steamapps\common\wallpaper_engine\wallpaper64.exe", roots) is null);
Check("fuera de las bibliotecas: null", GameLibraries.Match(@"C:\Program Files\Obsidian\Obsidian.exe", roots) is null);
Check("no confunde carpetas con el mismo prefijo", GameLibraries.Match(@"D:\SteamLibrary\steamapps\commonX\a\b.exe", roots) is null);
Check("lanzadores y ayudantes no cuentan",
    !GameLibraries.LooksLikeGame(@"C:\Riot Games\Riot Client\RiotClientServices.exe")
    && !GameLibraries.LooksLikeGame(@"D:\Epic Games\Fortnite\FortniteGame\Binaries\Win64\FortniteBootstrapper.exe")
    && !GameLibraries.LooksLikeGame(@"D:\SteamLibrary\steamapps\common\ARK\UnityCrashHandler64.exe")
    && GameLibraries.LooksLikeGame(@"D:\SteamLibrary\steamapps\common\Terraria\Terraria.exe"));

// MARK: quién despierta al modelo

Section("Quién lo despierta");
var clients = new ClientTracker();
var obsidian = new ClientApp("Obsidian", @"C:\Obsidian.exe");
var cursor = new ClientApp("Cursor", @"C:\Cursor.exe");
clients.Seen([obsidian], At(0));
clients.Seen([cursor], At(5));
var recent = clients.Recent(At(8));
Check("atribuye a las apps vistas en los últimos 10 s, la más reciente primero", recent.Select(a => a.Name).SequenceEqual(["Cursor", "Obsidian"]));
Check("lo visto hace más de 10 s no cuenta", clients.Recent(At(12)).Select(a => a.Name).SequenceEqual(["Cursor"]));
Check("sin nadie conectado, nadie", clients.Recent(At(60)).Count == 0);

// MARK: estadísticas

Section("Estadísticas");
var now = new DateTime(2026, 9, 29, 18, 0, 0);
var stats = new StatsStore(null);
stats.Add(new StatEvent(now.AddDays(-1), StatKind.Nap, "qwen", 5_000_000_000, "auto"));
stats.Add(new StatEvent(now.AddDays(-2), StatKind.Nap, "gemma", 3_000_000_000, "manual"));
stats.Add(new StatEvent(now.AddDays(-9), StatKind.Nap, "viejo", 9_000_000_000, "auto"));
stats.Add(new StatEvent(now.AddHours(-3), StatKind.Restart, Detail: "crash"));
stats.Add(new StatEvent(now.AddHours(-5), StatKind.Wake, "Obsidian", 1, @"C:\o.exe", "qwen"));
stats.Add(new StatEvent(now.AddHours(-2), StatKind.Wake, "Obsidian", 1, @"C:\o.exe", "qwen"));
stats.Add(new StatEvent(now.AddHours(-1), StatKind.Wake, "Cursor", 1, @"C:\c.exe", "qwen"));
stats.Add(new StatEvent(now.AddHours(-1), StatKind.Wake, null, 1));
stats.AddLoaded(3600, now);
stats.AddLoaded(1800, now.AddDays(-1));
stats.AddLoaded(7200, now.AddDays(-8));
var week = stats.Week(now);
Check("semana: memoria recuperada de las siestas de 7 días", week.Recovered == 8_000_000_000);
Check("semana: siestas", week.Naps == 2);
Check("semana: horas con modelo (sin lo de hace 8 días)", Math.Abs(week.LoadedHours - 1.5) < 1e-9);
Check("semana: reinicios", week.Restarts == 1);
var who = stats.Clients();
Check("quién despierta: Obsidian 2 veces primero, sin los desconocidos",
    who.Count == 2 && who[0] is { App: "Obsidian", Wakes: 2 } && who[0].Last == now.AddHours(-2) && who[1].App == "Cursor");
Check("recientes: el más nuevo primero", stats.Recent(3)[0].At == now.AddHours(-1));

for (int i = 0; i <= 40 * 60; i += 10) stats.Sample(new MemorySample(now.AddSeconds(i), 16, 8, 4));
var samples = stats.Samples();
Check("la gráfica guarda solo 30 min", (samples[^1].At - samples[0].At) <= StatsStore.SampleWindow && samples.Count <= 181);

var file = Path.Combine(Path.GetTempPath(), $"its-stats-{Guid.NewGuid():N}.json");
try
{
    var persisted = new StatsStore(file);
    persisted.Add(new StatEvent(DateTime.Now.AddHours(-1), StatKind.Download, "smollm2:135m", 270_000_000));
    persisted.Add(new StatEvent(DateTime.Now.AddDays(-100), StatKind.Nap, "antiguo", 1));
    var reloaded = new StatsStore(file);
    Check("se guarda y se lee de disco (sin lo de más de 90 días)",
        reloaded.Recent(10) is [{ Kind: StatKind.Download, Subject: "smollm2:135m", Bytes: 270_000_000 }]);
}
finally { File.Delete(file); }

// MARK: descargas

Section("Descargas (NDJSON de /api/pull)");
var pull = new PullParser();
var p1 = pull.Feed("""{"status":"pulling manifest"}""");
Check("el manifiesto aún no tiene tamaño", p1 is { Status: "pulling manifest", Fraction: null });
pull.Feed("""{"status":"pulling aaa","digest":"sha256:aaa","total":1000,"completed":250}""");
var p2 = pull.Feed("""{"status":"pulling bbb","digest":"sha256:bbb","total":3000,"completed":0}""");
Check("la fracción suma todas las capas", p2 is { Fraction: 0.0625 } && p2.Value.Total == 4000);
pull.Feed("""{"status":"pulling aaa","digest":"sha256:aaa","total":1000,"completed":1000}""");
var p3 = pull.Feed("""{"status":"pulling bbb","digest":"sha256:bbb","total":3000,"completed":3000}""");
Check("todas las capas: 100 %", p3 is { Fraction: 1.0 });
pull.Feed("""{"status":"verifying sha256 digest"}""");
pull.Feed("");
pull.Feed("""{"status":"success"}""");
Check("termina con success", pull.Done && pull.Error is null);
var bad = new PullParser();
bad.Feed("""{"error":"pull model manifest: file does not exist"}""");
Check("un error de Ollama se recoge", bad.Error == "pull model manifest: file does not exist" && !bad.Done);

// MARK: enlaces

Section("Enlaces istargetsleeping://");
Check("on", Links.Parse("istargetsleeping://on") == new Link(LinkAction.On));
Check("mayúsculas y barra final", Links.Parse("IsTargetSleeping://Sleep/") == new Link(LinkAction.Sleep));
Check("load con etiqueta", Links.Parse("istargetsleeping://load/llama3.2:latest") == new Link(LinkAction.Load, "llama3.2:latest"));
Check("load con barras y escapes", Links.Parse("istargetsleeping://load/hf.co%2Fuser%2Fmodel:Q4") == new Link(LinkAction.Load, "hf.co/user/model:Q4")
    && Links.Parse("istargetsleeping://load/hf.co/user/model") == new Link(LinkAction.Load, "hf.co/user/model"));
Check("entre comillas (como lo pasa Windows)", Links.Parse("\"istargetsleeping://activity\"") == new Link(LinkAction.Activity));
Check("load sin modelo, acción desconocida u otro esquema: null",
    Links.Parse("istargetsleeping://load") is null && Links.Parse("istargetsleeping://format-c") is null && Links.Parse("https://on") is null);

Check("clean", Links.Parse("istargetsleeping://clean") == new Link(LinkAction.Clean));

// MARK: liberar RAM

Section("Liberar RAM: orden para el agente");
var spec = new CleanSpec(CleanAreas.Default, [1234, 5678]);
Check("formato compacto", spec.Format() == "a=e7;k=1234,5678");
var parsed = CleanSpec.Parse(spec.Format());
Check("ida y vuelta", parsed is { Areas: CleanAreas.Default } && parsed.Value.Keep.SequenceEqual([1234, 5678]));
Check("rechaza zonas desconocidas, PIDs raros, claves repetidas o inventadas",
    CleanSpec.Parse("a=1ff") is null && CleanSpec.Parse("a=e7;k=12a") is null && CleanSpec.Parse("a=e7;k=-1") is null
    && CleanSpec.Parse("a=e7;a=01") is null && CleanSpec.Parse("a=e7;cmd=calc") is null && CleanSpec.Parse("a=e7;x=calc") is null
    && CleanSpec.Parse("k=1") is null && CleanSpec.Parse("") is null && CleanSpec.Parse("a=e7;k=1 2") is null);
Check("rechaza una orden demasiado larga", CleanSpec.Parse("a=e7;k=" + new string('1', CleanSpec.MaxLength)) is null);
Check("códigos de salida del agente", AgentExit.IsDone(AgentExit.Done | 0x08) && AgentExit.Failed(AgentExit.Done | 0x08) == CleanAreas.Standby
    && !AgentExit.IsDone(AgentExit.NotElevated) && !AgentExit.IsDone(0));

Section("Liberar RAM: reglas");
var rules = new CleanRuleTracker(t0) { ThresholdPercent = 70, ThresholdCooldownMinutes = 5, IntervalMinutes = 30 };
Check("por debajo del umbral, nada", rules.Sample(65, false, At(60)) is null);
Check("al pasar del 70 % limpia aunque no toque el intervalo", rules.Sample(72, false, At(70)) == CleanReason.Threshold);
rules.Cleaned(At(70));
Check("sigue por encima: espera la pausa mínima", rules.Sample(75, false, At(70 + 299)) is null);
Check("sigue por encima: repite tras la pausa mínima", rules.Sample(75, false, At(70 + 300)) == CleanReason.Threshold);
rules.Cleaned(At(370));
rules.ThresholdCooldownMinutes = 2;
Check("la pausa mínima se configura", rules.Sample(75, false, At(370 + 119)) is null && rules.Sample(75, false, At(370 + 120)) == CleanReason.Threshold);
rules.Cleaned(At(490));
rules.ThresholdPercent = 0;
Check("sin umbral, solo el intervalo", rules.Sample(99, false, At(490 + 600)) is null && rules.Sample(99, false, At(490 + 1800)) == CleanReason.Interval);

var every = new CleanRuleTracker(t0) { IntervalMinutes = 6 };
Check("intervalo: a los 5 min no", every.Sample(30, false, At(300)) is null);
Check("intervalo: a los 6 min sí, aunque haya poca RAM en uso", every.Sample(30, false, At(360)) == CleanReason.Interval);
every.Cleaned(At(360));
every.Cleaned(At(500));   // una limpieza manual reinicia el intervalo
Check("una limpieza manual reinicia el intervalo", every.Sample(30, false, At(720)) is null && every.Sample(30, false, At(860)) == CleanReason.Interval);

var crit = new CleanRuleTracker(t0) { OnCritical = true, ThresholdCooldownMinutes = 5 };
Check("presión crítica: actúa al empezar", crit.Sample(97, true, At(200)) == CleanReason.Critical);
crit.Cleaned(At(200));
Check("sigue crítica: espera la pausa mínima", crit.Sample(97, true, At(260)) is null);
Check("sigue crítica: repite tras la pausa mínima", crit.Sample(97, true, At(500)) == CleanReason.Critical);
crit.Cleaned(At(500));
crit.Sample(80, false, At(570));
Check("una crisis nueva actúa sin esperar la pausa", crit.Sample(97, true, At(580)) == CleanReason.Critical);
Check("todo apagado: nunca", new CleanRuleTracker(t0).Sample(99, true, At(9999)) is null);

Section("Coordinación de limpieza y compatibilidad");
CleanCoordinationTests.Run(Check);
Section("Procesos y finalización");
ProcessTests.Run(Check);
Section("Informes y protocolo del agente");
MemoryAgentTests.Run(Check);
Section("Memoria ahora");
// Dos archivos de paginación (12 GB cada uno, 1 y 2 GB en uso) en el formato de SYSTEM_PAGEFILE_INFORMATION.
var pageBuffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(64);
try
{
    void Entry(int at, int next, uint total, uint inUse)
    {
        System.Runtime.InteropServices.Marshal.WriteInt32(pageBuffer, at, next);
        System.Runtime.InteropServices.Marshal.WriteInt32(pageBuffer, at + 4, (int)total);
        System.Runtime.InteropServices.Marshal.WriteInt32(pageBuffer, at + 8, (int)inUse);
        System.Runtime.InteropServices.Marshal.WriteInt32(pageBuffer, at + 12, 0);
    }
    const long gb = 1L << 30, page = 4096;
    Entry(0, 32, (uint)(12 * gb / page), (uint)(gb / page));
    Entry(32, 0, (uint)(12 * gb / page), (uint)(2 * gb / page));
    Check("suma todos los archivos de paginación", MemoryBreakdown.ParsePageFiles(pageBuffer, 64, page) == (3 * gb, 24 * gb));
    Check("no lee más allá de lo devuelto", MemoryBreakdown.ParsePageFiles(pageBuffer, 40, page) == (gb, 12 * gb)
        && MemoryBreakdown.ParsePageFiles(pageBuffer, 0, page) == (0, 0));
}
finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(pageBuffer); }
var memNow = MemoryBreakdown.Current();
Check("lee la memoria de este PC", memNow.PhysicalTotal > 0 && memNow.PhysicalAvailable <= memNow.PhysicalTotal
    && memNow.CommitLimit >= memNow.PhysicalTotal && memNow.PageFileUsed <= memNow.PageFileTotal
    && memNow.CacheCurrent >= 0 && memNow.CommitFraction is >= 0 and <= 1);
Check("sin datos, fracciones en cero", new MemoryBreakdown().PhysicalFraction == 0 && new MemoryBreakdown().CacheFraction == 0);

Section("Liberar RAM: zonas y estadísticas");
// Los bits de las zonas son los de `ReductMask2` de Mem Reduct; su predeterminado es 0xE7.
Check("zonas por defecto como las de Mem Reduct", (int)CleanAreas.Default == 0xE7
    && (int)(CleanAreas.WorkingSets | CleanAreas.StandbyLowPriority | CleanAreas.Standby) == 13);
var cleanStats = new StatsStore(null);
cleanStats.Add(new StatEvent(now.AddDays(-1), StatKind.Nap, "qwen", 5_000_000_000, "auto"));
cleanStats.Add(new StatEvent(now.AddHours(-2), StatKind.Clean, null, 1_500_000_000, "threshold"));
cleanStats.Add(new StatEvent(now.AddHours(-1), StatKind.Clean, null, 500_000_000, "manual"));
var cw = cleanStats.Week(now);
Check("la semana suma siestas y limpiezas", cw.Recovered == 7_000_000_000 && cw.Cleaned == 2_000_000_000 && cw.Cleans == 2 && cw.Naps == 1);

// MARK: fijar el ícono

Section("Fijar el ícono en la barra de tareas");
TrayPinTests.Run(Check);

// MARK: ícono animado

Section("Ícono animado");
var motion = new TrayMotion(TrayPhase.Off, false, animate: true);
Check("apagado → arrancando: buscando", motion.Set(TrayPhase.Starting, false, 1) == TrayAnimation.Searching);
Check("la búsqueda arranca de la pose de apagado", motion.PoseAt(1).Near(TrayPose.Off));
Check("la búsqueda es un bucle que no acaba", motion.IsAnimating(60));
Check("el bucle se repite cada vuelta", motion.PoseAt(3).Near(motion.PoseAt(3 + TrayMotion.Turn)));
Check("en el bucle hay fotograma para reutilizar", motion.LoopFrame(3) is >= 0 and < TrayMotion.LoopFrames && motion.LoopFrame(1.1) is null);
Check("el fotograma guardado es la pose", motion.PoseAt(3).Near(TrayMotion.Loop(true, motion.LoopFrame(3)!.Value)));
Check("el radar gira en el sentido del reloj", TrayMotion.Searching(0.25).ArcStart is > 89 and < 91);
Check("arrancando → encendido: fijado", motion.Set(TrayPhase.On, false, 3) == TrayAnimation.LockOn);
Check("el fijado arranca de la pose del radar (sin saltos)", motion.PoseAt(3).Near(TrayMotion.Loop(true, TrayMotion.Frame(2 / TrayMotion.Turn))));
double peak = Enumerable.Range(0, 60).Max(i => motion.PoseAt(3 + i * 0.01).Dot);
Check("la insignia salta por encima de 1", peak > 1.15);
Check("las marcas salen hacia fuera", Enumerable.Range(0, 60).Min(i => motion.PoseAt(3 + i * 0.01).TickShift) <= -0.99);
Check("el fijado acaba en la pose de encendido", motion.PoseAt(3.6).Near(TrayPose.On(false)) && !motion.IsAnimating(3.6));
Check("un modelo carga: el ojo se abre", motion.Set(TrayPhase.On, true, 5) == TrayAnimation.EyeOpen);
Check("el ojo abierto acaba despierto", motion.PoseAt(5.35).Near(TrayPose.On(true)));
Check("el modelo duerme: el ojo se cierra", motion.Set(TrayPhase.On, false, 6) == TrayAnimation.EyeClose && motion.PoseAt(6.45).Near(TrayPose.On(false)));
Check("sin cambios no hay animación", motion.Set(TrayPhase.On, false, 7) == TrayAnimation.None);
motion.Set(TrayPhase.On, true, 7);
Check("encendido → apagándose: soltando", motion.Set(TrayPhase.Stopping, false, 8) == TrayAnimation.LettingGo);
Check("soltando cierra el ojo y la insignia pasa a ámbar", motion.PoseAt(8.4) is { Eye: 0, DotTint: 1, Dot: 1 });
Check("el radar al soltar gira al revés", TrayMotion.LettingGo(0.25).ArcStart is > 269 and < 271);
Check("apagándose → apagado: se apaga", motion.Set(TrayPhase.Off, false, 9) == TrayAnimation.Off);
Check("al apagarse el anillo se deshace", motion.PoseAt(9.24).Ring < 0.1 && motion.PoseAt(9.24).Dot < 0.1);
Check("y acaba en la pose de apagado", motion.PoseAt(9.5).Near(TrayPose.Off) && !motion.IsAnimating(9.5));

var failed = new TrayMotion(TrayPhase.Off, false, animate: true);
failed.Set(TrayPhase.Starting, false, 0);
Check("arrancar y fallar: la misma animación de apagado", failed.Set(TrayPhase.Off, false, 2) == TrayAnimation.Off);

// Interrupción a media animación: la nueva empieza donde iba la otra.
var cut = new TrayMotion(TrayPhase.Off, false, animate: true);
cut.Set(TrayPhase.Starting, false, 0);
cut.Set(TrayPhase.On, false, 2);
var mid = cut.PoseAt(2.25);
cut.Set(TrayPhase.Stopping, false, 2.25);
Check("una interrupción arranca de la pose actual", cut.PoseAt(2.25).Near(mid));
var midOff = cut.PoseAt(2.4);
cut.Set(TrayPhase.Off, false, 2.4);
Check("también al apagarse a media entrada", cut.PoseAt(2.4).Near(midOff));

var still = new TrayMotion(TrayPhase.Off, false, animate: false);
Check("sin animaciones: nada que animar", still.Set(TrayPhase.Starting, false, 0) == TrayAnimation.None && !still.IsAnimating(0));
Check("sin animaciones: insignia ámbar fija", still.PoseAt(0).Near(TrayPose.Busy) && still.PoseAt(0.7).Near(TrayPose.Busy));
still.Set(TrayPhase.On, true, 1);
Check("sin animaciones: salta a la pose final", still.PoseAt(1).Near(TrayPose.On(true)));
still.SetAnimate(true, 2);
Check("volver a animar no mueve una pose fija", !still.IsAnimating(2) && still.PoseAt(2).Near(TrayPose.On(true)));

// MARK: mascota

Section("Mascota: estados");
Check("apagado: duerme", PetBrain.Choose(new(), false) == PetActivity.DeepSleep);
Check("arrancando: se despierta", PetBrain.Choose(new(Starting: true), false) == PetActivity.WakingUp);
Check("apagándose: bosteza", PetBrain.Choose(new(Up: true, Stopping: true), false) == PetActivity.Yawning);
Check("encendido sin modelo: somnolienta", PetBrain.Choose(new(Up: true), false) == PetActivity.Drowsy);
Check("cargando un modelo: come", PetBrain.Choose(new(Up: true, Loading: true), false) == PetActivity.Eating);
Check("modelo en memoria: alerta", PetBrain.Choose(new(Up: true, ModelLoaded: true), false) == PetActivity.Alert);
Check("generando: trabaja", PetBrain.Choose(new(Up: true, ModelLoaded: true), true) == PetActivity.Working);
Check("descargar va antes que trabajar", PetBrain.Choose(new(Up: true, ModelLoaded: true, Downloading: true), true) == PetActivity.Downloading);
Check("liberar RAM va antes que todo, incluso apagado", PetBrain.Choose(new(Cleaning: true), false) == PetActivity.Sweeping);
Check("jugando: oculta", PetBrain.Choose(new(Up: true, ModelLoaded: true, Cleaning: true, Game: true), true) == PetActivity.Hidden);

var brain = new PetBrain();
brain.Update(new(Up: true, ModelLoaded: true, Generating: true), 0);
Check("la señal de generar la pone a trabajar", brain.Activity == PetActivity.Working);
brain.Update(new(Up: true, ModelLoaded: true), 2.5);
Check("sigue trabajando un rato sin señal", brain.Activity == PetActivity.Working);
brain.Update(new(Up: true, ModelLoaded: true), 3.1);
Check("y luego vuelve a estar alerta", brain.Activity == PetActivity.Alert && brain.ActivitySince == 3.1);

brain.React(PetReaction.Jump, 10);
brain.React(PetReaction.Dizzy, 10.1);
Check("una reacción más importante interrumpe", brain.Reaction == PetReaction.Dizzy);
brain.React(PetReaction.Hearts, 10.2);
brain.React(PetReaction.Sparkle, 10.2);
brain.React(PetReaction.Sparkle, 10.3);
Check("las menos importantes esperan su turno", brain.Reaction == PetReaction.Dizzy);
brain.Expire(10.1 + PetBrain.Duration(PetReaction.Dizzy) + 0.01);
Check("la cola sigue por prioridad", brain.Reaction == PetReaction.Sparkle);
brain.Expire(20);
Check("al acabar la cola vuelve a su actividad", brain.Reaction == PetReaction.None && brain.Activity == PetActivity.Alert);
brain.React(PetReaction.Hearts, 21);
brain.Update(new(Up: true, Game: true), 21.1);
Check("al ocultarse se olvidan las reacciones", brain.Activity == PetActivity.Hidden && brain.Reaction == PetReaction.None);
brain.React(PetReaction.Jump, 21.2);
Check("oculta no reacciona", brain.Reaction == PetReaction.None);

Section("Mascota: fotogramas clave");
var keyed = new PetClip([new(0, new PetPose(Eye: 0)), new(1, new PetPose(Eye: 1, Prop: PetProp.Laptop)), new(2, new PetPose(Eye: 0.5))], loop: true, length: 3);
Check("en cada clave la pose es exacta", keyed.At(0).Eye == 0 && keyed.At(1).Eye == 1 && keyed.At(2).Eye == 0.5);
Check("entre claves se interpola con su curva", keyed.At(0.5).Eye is > 0.3 and < 0.7
    && Enumerable.Range(0, 100).Select(i => keyed.At(i / 100.0).Eye).Zip(Enumerable.Range(1, 100).Select(i => keyed.At(i / 100.0).Eye)).All(p => p.Second >= p.First - 1e-9));
Check("el bucle vuelve a la primera clave sin salto", Math.Abs(keyed.At(2.999).Eye - 0) < 0.01 && keyed.At(3.0).Eye == keyed.At(0).Eye);
Check("un accesorio que aparece entra con PropIn (no salta)",
    PetPose.Lerp(new PetPose(), new PetPose(Prop: PetProp.Laptop), 0.3) is { Prop: PetProp.Laptop, PropIn: > 0.2 and < 0.4 });
var once = new PetClip([new(0, new PetPose(Eye: 0)), new(1, new PetPose(Eye: 1))], loop: false);
Check("sin bucle se queda en la última", once.At(5).Eye == 1);
Check("la pose de reposo: ojo abierto y bracitos en cruz", new PetPose() is { Eye: 1, ArmL: PetArm.Out, ArmR: PetArm.Out, Sit: 0 });

Section("Mascota: animación continua");
var miraProfile = PetProfiles.Mira;
// Lo más que puede cambiar una pose entre fotogramas a 60 fps sin que se vea un salto
// (los parpadeos y los golpes rápidos llegan a 0,35; el resto va muy por debajo).
double Jump(PetClip clip, double seconds) =>
    Enumerable.Range(0, (int)(seconds * 60)).Max(i => PetPose.Distance(clip.At(i / 60.0), clip.At((i + 1) / 60.0)));
var clips = Enum.GetValues<PetActivity>().Select(a => (a.ToString(), miraProfile.For(a)))
    .Concat(Enum.GetValues<PetReaction>().Select(r => (r.ToString(), miraProfile.For(r, PetActivity.Alert))))
    .Concat(Enum.GetValues<PetGesture>().Select(g => (g.ToString(), miraProfile.Gesture(g))))
    .Append(("Peek", miraProfile.Peek)).ToList();
var rough = clips.Where(c => Jump(c.Item2, c.Item2.Length * 2 + 0.1) > 0.35).Select(c => c.Item1).ToList();
Check($"ninguna animación salta entre fotogramas a 60 fps{(rough.Count > 0 ? ": " + string.Join(", ", rough) : "")}", rough.Count == 0);
Check("cada reacción cabe en su duración", Enum.GetValues<PetReaction>().Where(r => r != PetReaction.None)
    .All(r => miraProfile.For(r, PetActivity.Alert).Length <= PetBrain.Duration(r) + 1e-9));
Check("dormida: en su cama, con el ojo cerrado (también sin movimiento)",
    Enumerable.Range(0, 34).Select(i => miraProfile.For(PetActivity.DeepSleep).At(i / 10.0)).All(p => p.Eye == 0 && p.BedIn == 1)
    && miraProfile.Still(PetActivity.DeepSleep) is { Eye: 0, BedIn: 1 });
Check("el bostezo acaba en la cama", miraProfile.For(PetActivity.Yawning).At(60) == miraProfile.Still(PetActivity.DeepSleep));
var alertClip = miraProfile.For(PetActivity.Alert);
var alertEyes = Enumerable.Range(0, (int)(alertClip.Length * 60)).Select(i => alertClip.At(i / 60.0).Eye).ToList();
int longestBlink = 0, run = 0;
foreach (var e in alertEyes) { run = e < 0.5 ? run + 1 : 0; longestBlink = Math.Max(longestBlink, run); }
Check("alerta: casi siempre con el ojo abierto y parpadeos rápidos (< 0,2 s cerrado)",
    alertEyes.Count(e => e > 0.95) >= alertEyes.Count * 0.85 && longestBlink > 0 && longestBlink / 60.0 < 0.2);
Check("esperando un modelo: tras el logo, asomándose de 0 a 3 píxeles y sonrojándose",
    Enumerable.Range(0, 160).Select(i => miraProfile.Peek.At(i / 20.0)).All(p => p.Behind && p.Peek is >= -0.01 and <= 3.01)
    && Enumerable.Range(0, 160).Max(i => miraProfile.Peek.At(i / 20.0).Blush) > 0.9 && miraProfile.PeekStill.Behind);
var arc = Enumerable.Range(0, 31).Select(i => miraProfile.Motion(PetReaction.Jump, i / 60.0, PetActivity.Alert).Dy).ToList();
Check("el salto es una parábola que vuelve al suelo", arc.Min() < -4.5 && arc[0] == 0 && arc[^1] == 0);
var walkPoses = Enumerable.Range(0, 120).Select(i => miraProfile.Walk(i * 0.1, 1, 0, false)).ToList();
Check("al caminar el paso va con la distancia (dos pasos cada 6 píxeles)",
    walkPoses.Count(p => p.Feet == PetFeet.StepLeft) > 10 && walkPoses.Count(p => p.Feet == PetFeet.StepRight) > 10
    && miraProfile.Walk(0, 1, 0, false).Feet == miraProfile.Walk(6, 1, 0, false).Feet);

Section("Mascota: transiciones y mezclas");
var wake = miraProfile.Transition(PetActivity.DeepSleep, PetActivity.WakingUp, false, false)!;
Check("al despertar sale de la cama", wake.At(0) == miraProfile.Still(PetActivity.DeepSleep) && wake.At(wake.Length).BedIn < 0.05 && wake.At(wake.Length).Sit < 0.05);
var laptop = miraProfile.Transition(PetActivity.Alert, PetActivity.Working, false, false)!;
Check("al ponerse a trabajar saca el portátil", laptop.At(0) is { Prop: PetProp.Laptop, PropIn: 0 } && laptop.At(laptop.Length).PropIn == 1);
var close = miraProfile.Transition(PetActivity.Working, PetActivity.Alert, false, false)!;
Check("y al acabar lo guarda", close.At(close.Length) is { Prop: PetProp.None } || close.At(close.Length) is { PropIn: < 0.01 });
Check("salir de detrás del logo y meterse detrás tienen su animación",
    miraProfile.Transition(PetActivity.Drowsy, PetActivity.Eating, true, false) is { } emerge && emerge.At(0).Behind && !emerge.At(emerge.Length).Behind
    && miraProfile.Transition(PetActivity.WakingUp, PetActivity.Drowsy, false, true) is { } duck && duck.At(duck.Length).Behind);
Check("sin transición propia, nada (se mezclan las poses)", miraProfile.Transition(PetActivity.Alert, PetActivity.Drowsy, false, false) is null);

var anchors = new PetAnchors(13.5, 6, 13.5, 15.5, 13.5, 14.5, 23.5);
PetFrame[] Run(PetDirector d, Func<double, PetSituation> at, double from, double to)
{
    var frames = new List<PetFrame>();
    for (double t = from; t < to; t += 1 / 60.0) frames.Add(d.Step(t, 1 / 60.0, at(t)));
    return [.. frames];
}
var director = new PetDirector(anchors, seed: 2);
var calm = Run(director, _ => new PetSituation(PetActivity.Alert), 0, 3);
var switched = Run(director, _ => new PetSituation(PetActivity.Drowsy), 3, 4);
Check("al cambiar de actividad la pose se mezcla sin salto",
    PetPose.Distance(calm[^1].Pose, switched[0].Pose) < 0.2 && switched.Zip(switched.Skip(1)).All(p => PetPose.Distance(p.First.Pose, p.Second.Pose) < 0.35));
var reacting = Run(director, t => new PetSituation(PetActivity.Drowsy, PetReaction.Hearts, 4), 4, 5.4);
Check("las reacciones entran y salen mezclándose, con corazones",
    PetPose.Distance(switched[^1].Pose, reacting[0].Pose) < 0.35 && director.Particles.Live.Any(p => p.Kind == ParticleKind.Heart));
var calmPose = new PetDirector(anchors).Step(0, 1 / 60.0, new PetSituation(PetActivity.Working, Still: true));
Check("con movimiento reducido: pose fija, sin partículas ni reloj",
    calmPose.Pose == miraProfile.Still(PetActivity.Working) && calmPose.Fps == 0);
var dozing = new PetDirector(anchors);
var sleepy = Run(dozing, _ => new PetSituation(PetActivity.DeepSleep), 0, 4);
Check("dormida: suben las Z, a menos fotogramas", dozing.Particles.Live.Any(p => p.Kind == ParticleKind.Z) && sleepy[^1].Fps == 20);
var looking = new PetDirector(anchors);
var gaze = Run(looking, _ => new PetSituation(PetActivity.Alert, Hovered: true, HoverX: 1, HoverY: 0), 0, 1);
Check("el ojo sigue al ratón con suavidad (sin saltos)", gaze[^1].Pose.LookX > 0.9
    && gaze.Zip(gaze.Skip(1)).All(p => Math.Abs(p.Second.Pose.LookX - p.First.Pose.LookX) < 0.2));

Section("Mascota: gestos al estar quieta");
var gesturer = new PetIdle(seed: 4);
var gestures = new List<(PetGesture Gesture, double At)>();
for (double t = 0; t < 300; t += 0.05)
{
    var before = gesturer.Current;
    gesturer.Update(t, allowed: true, drowsy: false);
    if (gesturer.Current != PetGesture.None && gesturer.Current != before) gestures.Add((gesturer.Current, t));
}
Check("hace gestos de vez en cuando (entre 6 y 15 s de espera)", gestures.Count is > 12 and < 50
    && gestures.Zip(gestures.Skip(1)).All(p => p.Second.At - p.First.At >= PetIdle.MinWait - 0.1));
Check("nunca repite el mismo dos veces seguidas", gestures.Zip(gestures.Skip(1)).All(p => p.First.Gesture != p.Second.Gesture));
Check("y cabecear es solo de somnolienta", gestures.All(g => g.Gesture != PetGesture.Nod));
var blocked = new PetIdle(seed: 4);
for (double t = 0; t < 100; t += 0.05) blocked.Update(t, allowed: false, drowsy: false);
Check("sin permiso (reacción, paseo, ratón…) no hace ninguno", blocked.Current == PetGesture.None);
var twinIdle = new PetIdle(seed: 4);
var twinGestures = new List<PetGesture>();
for (double t = 0; t < 300; t += 0.05)
{
    var before = twinIdle.Current;
    twinIdle.Update(t, allowed: true, drowsy: false);
    if (twinIdle.Current != PetGesture.None && twinIdle.Current != before) twinGestures.Add(twinIdle.Current);
}
Check("con la misma semilla, los mismos gestos", twinGestures.SequenceEqual(gestures.Select(g => g.Gesture)));

Section("Mascota: partículas");
var fx = new PetParticles(anchors, seed: 9);
fx.Emit(ParticleKind.Z);
fx.Emit(ParticleKind.Dust);
double zStart = fx.Live[0].Y, dustVy = fx.Live[1].Vy;
for (int i = 0; i < 30; i++) fx.Update(1 / 60.0);
Check("las Z suben y el polvo cae", fx.Live[0].Y < zStart && fx.Live[1].Vy > dustVy);
Check("aparecen y se desvanecen al final de su vida",
    new Particle(ParticleKind.Z, 0, 0, 0, 0, 0.01, 2, 0).Alpha < 0.2 && new Particle(ParticleKind.Z, 0, 0, 0, 0, 1.0, 2, 0).Alpha == 1
    && new Particle(ParticleKind.Z, 0, 0, 0, 0, 1.95, 2, 0).Alpha < 0.15);
for (int i = 0; i < 300; i++) fx.Update(1 / 60.0);
Check("y luego desaparecen", fx.Live.Count == 0);
var drip = new PetParticles(anchors);
for (double t = 0; t < 5; t += 1 / 60.0) { drip.Drip(ParticleKind.Z, 1.1, t); drip.Update(1 / 60.0); }
Check("el goteo suelta una cada tanto", drip.Live.Count is >= 2 and <= 3);

Section("Mascota: el logo de Windows");
Check("Ollama apagado: el logo como siempre (ni se oscurece)", PetLights.For(PetActivity.DeepSleep, 3, false).Dark);
Check("Ollama encendido: brilla un poco, menos que al liberar RAM",
    PetLights.For(PetActivity.Drowsy, 3, false) == PetGlow.All(PetLights.Lit)
    && PetLights.For(PetReaction.Sparkle, 0.1, false)!.Value.A0 > PetLights.Lit);
double GlowJump(Func<double, PetGlow> glow, double seconds) => Enumerable.Range(0, (int)(seconds * 60))
    .Max(i => Enumerable.Range(0, 4).Max(p => Math.Abs(glow((i + 1) / 60.0)[p] - glow(i / 60.0)[p])));
Check("al arrancar el brillo llega panel a panel con fundidos (sin saltos)",
    GlowJump(t => PetLights.For(PetActivity.WakingUp, t, false), 6) < 0.05
    && PetLights.For(PetActivity.WakingUp, 0.35, false) is { A0: > 0.15, A3: < 0.01 });
Check("al apagarse el brillo se va panel a panel y queda el logo de siempre",
    GlowJump(t => PetLights.For(PetActivity.Yawning, t, false), 3) < 0.05 && PetLights.For(PetActivity.Yawning, 3, false).Dark);
Check("trabajando: destellos que se apagan enseguida", PetLights.For(PetActivity.Working, 0.01, false).A1 > 0.4
    && PetLights.For(PetActivity.Working, 0.15, false).A1 < 0.3);
var glowDirector = new PetDirector(anchors);
var glowA = Run(glowDirector, _ => new PetSituation(PetActivity.DeepSleep), 0, 1);
var glowB = Run(glowDirector, _ => new PetSituation(PetActivity.Alert), 1, 2);
Check("al cambiar de estado, las luces se funden", glowB.Zip(glowB.Skip(1)).All(p => Math.Abs(p.Second.Glow.A0 - p.First.Glow.A0) < 0.05)
    && Math.Abs(glowB[0].Glow.A0 - glowA[^1].Glow.A0) < 0.05);

Section("Mascota: paseo");
var walker = new PetWalker(seed: 3);
var seen = new List<double>();
for (int i = 0; i < 4000; i++)
{
    walker.Update(i * 0.05, 0.05, 100, 900, 500, PetWalker.Mode.Roam, 60);
    seen.Add(walker.X);
}
Check("el paseo no sale del recorrido", seen.All(x => x >= 100 && x <= 900));
Check("y de verdad pasea", seen.Max() - seen.Min() > 300);
var smooth = new PetWalker(seed: 3);
var path = new List<double>();
for (int i = 0; i < 6000; i++) { smooth.Update(i / 60.0, 1 / 60.0, 100, 900, 500, PetWalker.Mode.Roam, 60); path.Add(smooth.X); }
var speeds = path.Zip(path.Skip(1)).Select(p => Math.Abs(p.Second - p.First) * 60).ToList();
Check("acelera y frena: a 60 fps la velocidad nunca cambia de golpe",
    speeds.Zip(speeds.Skip(1)).All(v => Math.Abs(v.Second - v.First) <= 2 * 60 / 0.35 / 60 + 1e-6) && speeds.Max() > 50);
var twin = new PetWalker(seed: 3);
for (int i = 0; i < 4000; i++) twin.Update(i * 0.05, 0.05, 100, 900, 500, PetWalker.Mode.Roam, 60);
Check("con la misma semilla, el mismo camino", twin.X == walker.X);
for (int i = 0; i < 600; i++) walker.Update(200 + i * 0.05, 0.05, 100, 900, 500, PetWalker.Mode.GoHome, 60);
Check("al dormirse vuelve a casa y se para", walker.X == 500 && !walker.Walking);
walker.Update(300, 0.05, 100, 900, 500, PetWalker.Mode.Stay, 60);
Check("quieta no se mueve", walker.X == 500 && !walker.Walking);
var turner = new PetWalker(seed: 1);
turner.Update(0, 0.05, 0, 1000, 500, PetWalker.Mode.GoHome, 60);
bool turned = false;
for (int i = 0; i < 200; i++)
{
    turner.Update(i * 0.05, 0.05, 0, 1000, 100, PetWalker.Mode.GoHome, 60);
    turned |= turner.Turning;
}
Check("para ir hacia el otro lado se da la vuelta", turned && turner.Facing == -1 && turner.X == 100);

Section("Mascota: posición");
Win32.RECT R(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };
var screen = R(0, 0, 1920, 1080);
var bottomBar = R(0, 1032, 1920, 1080);
var startBtn = R(700, 1032, 748, 1080);       // barra centrada de Windows 11
var trayArea = R(1613, 1032, 1920, 1080);
var mira = new PetSize(27, 24, 18, Reach: 23);
var above = TaskbarPetLayout.Place(PetPlacement.Above, bottomBar, startBtn, trayArea, screen, 96, mira);
Check("sobre Inicio: centrada y de pie justo encima del logo (estimado)",
    above.Scale == 2 && above.Width == 54 && above.X == 724 - 27 && above.Y + above.Height == 1056 - 11 - 1 && !above.Fallback);
var onLogo = TaskbarPetLayout.Place(PetPlacement.Above, bottomBar, startBtn, trayArea, screen, 96,
    new PetSize(32, 30, 22, Reach: 29, CenterX: 14.5), logo: R(713, 1045, 736, 1068));
Check("sobre el logo reconocido: el centro del cuerpo sobre el del logo y las patas encima",
    onLogo.X + 29 == (713 + 736) / 2 && onLogo.Y + onLogo.Height == 1045 - 1);
var left = TaskbarPetLayout.Place(PetPlacement.Left, bottomBar, startBtn, trayArea, screen, 96, mira);
Check("a la izquierda: el cuerpo dentro de la barra y la mano tocando el logo de Windows",
    left.Scale == 2 && left.X + 23 * 2 == 724 - 11 && left.Y + 6 * 2 >= 1032 && left.Y + left.Height <= 1080 && !left.Fallback);
var snug = TaskbarPetLayout.Place(PetPlacement.Left, bottomBar, startBtn, trayArea, screen, 96, mira, logo: R(713, 1045, 736, 1068));
Check("con el logo reconocido: la mano en su borde real y el cuerpo a su altura",
    snug.X + 23 * 2 == 713 && snug.Y + 6 * 2 + 18 * 2 / 2 == (1045 + 1068) / 2);
var leftAligned = TaskbarPetLayout.Place(PetPlacement.Left, bottomBar, R(0, 1032, 48, 1080), trayArea, screen, 96, mira);
Check("sin sitio a la izquierda: sobre Inicio y avisa", leftAligned.Fallback && leftAligned.Y + leftAligned.Height == 1044);
var roam = TaskbarPetLayout.Place(PetPlacement.Walk, bottomBar, startBtn, trayArea, screen, 96, mira);
Check("de paseo: del borde izquierdo hasta la bandeja",
    roam.MinX >= 0 && roam.MaxX + roam.Width <= 1613 && roam.MinX < roam.X && roam.X < roam.MaxX);
Check("escala entera según el DPI",
    TaskbarPetLayout.PixelScale(96) == 2 && TaskbarPetLayout.PixelScale(120) == 3 && TaskbarPetLayout.PixelScale(144) == 3
    && TaskbarPetLayout.PixelScale(192) == 4);
var topBar = TaskbarPetLayout.Place(PetPlacement.Above, R(0, 0, 1920, 48), R(700, 0, 748, 48), null, screen, 96, mira);
Check("barra arriba: cuelga por debajo", topBar.Y == 48 - 4);
var sideBar = TaskbarPetLayout.Place(PetPlacement.Walk, R(0, 0, 48, 1080), R(0, 0, 48, 48), null, screen, 96, mira);
Check("barra vertical: al lado, sin paseo", sideBar.X == 48 - 4 && sideBar.MinX == sideBar.MaxX);
var big = TaskbarPetLayout.Place(PetPlacement.Left, R(0, 1008, 1920, 1080), R(700, 1008, 772, 1080), null, screen, 144, mira);
Check("a la izquierda a 150 %: el cuerpo cabe en el alto de la barra", big.Scale == 3 && big.Y + 6 * 3 >= 1008 && big.Y + big.Height <= 1080);

// Pantalla completa: solo una app de verdad que tapa el monitor oculta la mascota.
ForegroundWindow Fg(string process, string cls, Win32.RECT rect, long ex = 0, bool visible = true, bool cloaked = false, bool minimized = false) =>
    new(process, cls, rect, ex, visible, cloaked, minimized);
Check("pantalla completa: un juego o vídeo sin bordes la oculta",
    TaskbarPetLayout.FullscreenApp(Fg("game", "UnityWndClass", screen), screen)
    && TaskbarPetLayout.FullscreenApp(Fg("chrome", "Chrome_WidgetWin_1", R(-1, -1, 1921, 1081)), screen));
Check("pantalla completa: una ventana maximizada no (la barra sigue a la vista)",
    !TaskbarPetLayout.FullscreenApp(Fg("claude", "Chrome_WidgetWin_1", R(-8, -8, 1928, 1040)), screen));
Check("pantalla completa: vistas previas de la barra, Alt+Tab y Recortes no la ocultan",
    !TaskbarPetLayout.FullscreenApp(Fg("explorer", "XamlExplorerHostIslandWindow", screen), screen)
    && !TaskbarPetLayout.FullscreenApp(Fg("SnippingTool", "XamlWindow", screen), screen)
    && !TaskbarPetLayout.FullscreenApp(Fg("explorer", "WorkerW", screen), screen));
Check("pantalla completa: ventanas ocultas, minimizadas o capas transparentes no cuentan",
    !TaskbarPetLayout.FullscreenApp(Fg("app", "X", screen, visible: false), screen)
    && !TaskbarPetLayout.FullscreenApp(Fg("app", "X", screen, cloaked: true), screen)
    && !TaskbarPetLayout.FullscreenApp(Fg("app", "X", screen, minimized: true), screen)
    && !TaskbarPetLayout.FullscreenApp(Fg("overlay", "X", screen, Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT), screen)
    && !TaskbarPetLayout.FullscreenApp(Fg("overlay", "X", screen, Win32.WS_EX_NOACTIVATE), screen));

Section("Colección y personalidades de mascotas");
PetTests.Run(Check);

// MARK: actualizaciones
Section("Actualizaciones");
await UpdateTests.Run(Check);

Console.WriteLine();
Console.WriteLine(failures == 0 ? "todo bien" : $"{failures} fallos");
return failures == 0 ? 0 : 1;
