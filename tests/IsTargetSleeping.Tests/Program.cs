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
Check("ida y vuelta", parsed is { Areas: CleanAreas.Default, CloseMemReduct: false } && parsed.Value.Keep.SequenceEqual([1234, 5678]));
Check("cerrar Mem Reduct sin zonas", CleanSpec.Parse(new CleanSpec(CleanAreas.None, [], true).Format()) is { Areas: CleanAreas.None, CloseMemReduct: true, Keep.Count: 0 });
Check("rechaza zonas desconocidas, PIDs raros, claves repetidas o inventadas",
    CleanSpec.Parse("a=1ff") is null && CleanSpec.Parse("a=e7;k=12a") is null && CleanSpec.Parse("a=e7;k=-1") is null
    && CleanSpec.Parse("a=e7;a=01") is null && CleanSpec.Parse("a=e7;cmd=calc") is null && CleanSpec.Parse("a=e7;x=calc") is null
    && CleanSpec.Parse("k=1") is null && CleanSpec.Parse("") is null && CleanSpec.Parse("a=e7;k=1 2") is null);
Check("rechaza una orden demasiado larga", CleanSpec.Parse("a=e7;k=" + string.Join(',', Enumerable.Repeat("123456789", 120))) is null);
Check("códigos de salida del agente", AgentExit.IsDone(AgentExit.Done | 0x08) && AgentExit.Failed(AgentExit.Done | 0x08) == CleanAreas.Standby
    && !AgentExit.IsDone(AgentExit.NotElevated) && !AgentExit.IsDone(0));

Section("Liberar RAM: reglas");
var rules = new CleanRuleTracker(t0) { ThresholdPercent = 60 };
Check("por debajo del umbral, nada", rules.Sample(55, false, At(300)) is null);
Check("al pasar del 60 % limpia", rules.Sample(62, false, At(310)) == CleanReason.Threshold);
rules.Cleaned(At(310));
Check("sigue alto (modelo cargado): no limpia en bucle", rules.Sample(70, false, At(310 + 600)) is null);
rules.Sample(56, false, At(1000));
Check("bajar 4 puntos no rearma", rules.Sample(61, false, At(1010)) is null);
rules.Sample(54, false, At(1020));
Check("bajar 5 puntos rearma", rules.Sample(61, false, At(1030)) == CleanReason.Threshold);
rules.Cleaned(At(1030));
rules.Sample(40, false, At(1040));
Check("mínimo 3 min entre limpiezas automáticas", rules.Sample(65, false, At(1030 + 170)) is null && rules.Sample(65, false, At(1030 + 180)) == CleanReason.Threshold);

var every = new CleanRuleTracker(t0) { IntervalMinutes = 6 };
Check("intervalo: a los 5 min no", every.Sample(30, false, At(300)) is null);
Check("intervalo: a los 6 min sí", every.Sample(30, false, At(360)) == CleanReason.Interval);
every.Cleaned(At(360));
every.Cleaned(At(500));   // una limpieza manual reinicia el intervalo
Check("una limpieza manual reinicia el intervalo", every.Sample(30, false, At(720)) is null && every.Sample(30, false, At(860)) == CleanReason.Interval);

var crit = new CleanRuleTracker(t0) { OnCritical = true };
Check("presión crítica: una vez por episodio", crit.Sample(97, true, At(200)) == CleanReason.Critical);
crit.Cleaned(At(200));
Check("sigue crítica: no repite", crit.Sample(97, true, At(600)) is null);
crit.Sample(80, false, At(700));
Check("tras volver a normal, vuelve a actuar", crit.Sample(97, true, At(800)) == CleanReason.Critical);
Check("todo apagado: nunca", new CleanRuleTracker(t0).Sample(99, true, At(9999)) is null);

Section("Mem Reduct");
var ini = """
[memreduct]
CheckUpdatesLast=1790734368
AutoreductEnable=true
AutoreductIntervalEnable=true
HotkeyCleanEnable=true
AutoreductValue=60
AutoreductIntervalValue=6
BalloonCleanResults=false
[memreduct\window]
Position=3010,243
""";
var mr = MemReduct.ParseIni(ini);
Check("importa 60 % y cada 6 min", mr is { AutoEnabled: true, AutoPercent: 60, IntervalEnabled: true, IntervalMinutes: 6, NotifyResults: false });
Check("sin ReductMask2: sus zonas por defecto", mr.Areas == CleanAreas.Default && (int)CleanAreas.Default == 0xE7);
var masked = MemReduct.ParseIni("[memreduct]\r\nReductMask2=13\r\n");
Check("ReductMask2 = 13: memoria de trabajo, prioridad baja y lista en espera",
    masked.Areas == (CleanAreas.WorkingSets | CleanAreas.StandbyLowPriority | CleanAreas.Standby) && !masked.AutoEnabled);
Check("la sección de la ventana no se confunde", MemReduct.ParseIni("[memreduct\\window]\nAutoreductEnable=true\n").AutoEnabled == false);
var edited = MemReduct.SetIniValues(ini.Replace("\n", "\r\n"), new Dictionary<string, string> { ["AutoreductEnable"] = "false", ["AutoreductIntervalEnable"] = "false", ["Nueva"] = "1" });
var reparsed = MemReduct.ParseIni(edited);
Check("apagar su limpieza automática sin tocar lo demás",
    !reparsed.AutoEnabled && !reparsed.IntervalEnabled && reparsed.AutoPercent == 60
    && edited.Contains("Position=3010,243") && edited.IndexOf("Nueva=1") < edited.IndexOf("[memreduct\\window]"));
Check("sin sección [memreduct], la crea", MemReduct.ParseIni(MemReduct.SetIniValues("", new Dictionary<string, string> { ["AutoreductEnable"] = "true" })).AutoEnabled);

var cleanStats = new StatsStore(null);
cleanStats.Add(new StatEvent(now.AddDays(-1), StatKind.Nap, "qwen", 5_000_000_000, "auto"));
cleanStats.Add(new StatEvent(now.AddHours(-2), StatKind.Clean, null, 1_500_000_000, "threshold"));
cleanStats.Add(new StatEvent(now.AddHours(-1), StatKind.Clean, null, 500_000_000, "manual"));
var cw = cleanStats.Week(now);
Check("la semana suma siestas y limpiezas", cw.Recovered == 7_000_000_000 && cw.Cleaned == 2_000_000_000 && cw.Cleans == 2 && cw.Naps == 1);

// MARK: actualizaciones

Section("Actualizaciones");
Check("1.1.0 > 1.0.9", Updater.CompareVersions("1.1.0", "1.0.9") > 0);
Check("v1.10.0 > 1.9.9", Updater.CompareVersions("v1.10.0", "1.9.9") > 0);
Check("1.1 == 1.1.0", Updater.CompareVersions("1.1", "1.1.0") == 0);
Check("2.0.0-beta.1 < 2.0.0", Updater.CompareVersions("2.0.0-beta.1", "2.0.0") < 0);
Check("1.0.0 no es más nueva que 1.0.0", Updater.CompareVersions("1.0.0", "1.0.0") == 0);

// Un servidor HTTP mínimo en 127.0.0.1 que hace de GitHub: nada sale del PC.
var fakeExe = Encoding.UTF8.GetBytes("MZ versión 9.9.9 de prueba");
byte[] zipBytes;
using (var ms = new MemoryStream())
{
    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
    {
        using (var entry = zip.CreateEntry("isTargetSleeping.exe").Open()) entry.Write(fakeExe);
        using (var license = zip.CreateEntry("LICENSE").Open()) license.Write("MIT"u8);
    }
    zipBytes = ms.ToArray();
}
var zipName = $"isTargetSleeping-9.9.9-win-{Updater.Arch}.zip";
var goodSha = $"{Convert.ToHexString(SHA256.HashData(zipBytes)).ToLowerInvariant()}  {zipName}";
string sha = goodSha;
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;
var api = $"http://127.0.0.1:{port}";
var release = $$"""
{"tag_name":"v9.9.9","html_url":"{{api}}/release",
 "assets":[{"name":"{{zipName}}","browser_download_url":"{{api}}/dl/{{zipName}}"},
           {"name":"{{zipName}}.sha256","browser_download_url":"{{api}}/dl/{{zipName}}.sha256"},
           {"name":"isTargetSleeping-9.9.9-setup-x64.exe","browser_download_url":"{{api}}/dl/setup.exe"}]}
""";
var server = Task.Run(async () =>
{
    while (true)
    {
        TcpClient client;
        try { client = await listener.AcceptTcpClientAsync(); } catch { return; }
        using (client)
        {
            var stream = client.GetStream();
            var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync() ?? "";
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
            var path = requestLine.Split(' ').ElementAtOrDefault(1) ?? "/";
            byte[] body; string type = "application/octet-stream"; int status = 200;
            if (path == "/repos/demo/its/releases/latest") { body = Encoding.UTF8.GetBytes(release); type = "application/json"; }
            else if (path == $"/dl/{zipName}") body = zipBytes;
            else if (path == $"/dl/{zipName}.sha256") { body = Encoding.UTF8.GetBytes(sha); type = "text/plain"; }
            else { body = []; status = 404; }
            var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} X\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(head);
            await stream.WriteAsync(body);
        }
    }
});

var info = Updater.ParseRelease(release, Updater.Arch);
Check("la release trae el zip y su .sha256 de esta arquitectura", info is { Version: "9.9.9" } && info.ZipUrl.EndsWith(zipName));
Check("sin el .sha256 no se ofrece", Updater.ParseRelease(release.Replace(".sha256\"", ".txt\""), Updater.Arch) is null);
var found = await Updater.Check(api, "demo/its", "1.1.0");
Check("detecta la 9.9.9 en el servidor local", found?.Version == "9.9.9");
Check("con la misma versión no hay nada", await Updater.Check(api, "demo/its", "9.9.9") is null);
Check("un repositorio que no existe: null", await Updater.Check(api, "otro/repo", "1.0.0") is null);
double lastProgress = 0;
var downloaded = await Updater.Download(found!, new Progress<double>(p => lastProgress = p));
var exe = Path.Combine(Path.GetTempPath(), $"its-new-{Guid.NewGuid():N}.exe");
File.Copy(downloaded, exe);
Check("descarga, comprueba el SHA-256 y extrae el .exe", File.ReadAllBytes(exe).SequenceEqual(fakeExe));
sha = new string('0', 64) + "  " + zipName;
bool rejected = false;
try { await Updater.Download(found!); } catch (InvalidDataException) { rejected = true; }
Check("un hash que no coincide se rechaza", rejected);
listener.Stop();

var dir = Directory.CreateTempSubdirectory("its-swap").FullName;
try
{
    var current = Path.Combine(dir, "isTargetSleeping.exe");
    File.WriteAllText(current, "vieja");
    Updater.Swap(current, exe);
    Check("sustituye el .exe y deja el viejo en .old",
        File.ReadAllBytes(current).SequenceEqual(fakeExe) && File.ReadAllText(current + ".old") == "vieja");
    Updater.Swap(current, exe);
    Check("una segunda vez pisa el .old anterior", File.Exists(current + ".old") && File.ReadAllBytes(current + ".old").SequenceEqual(fakeExe));
}
finally { Directory.Delete(dir, recursive: true); File.Delete(exe); }

Console.WriteLine();
Console.WriteLine(failures == 0 ? "todo bien" : $"{failures} fallos");
return failures == 0 ? 0 : 1;
