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

// MARK: mantenimiento (limpieza de RAM)

Section("Mantenimiento");
var gate = new MaintenanceGate();
Check("sin limpieza no hay mantenimiento", !gate.Active(false, At(0)));
Check("durante la limpieza sí", gate.Active(true, At(10)));
Check("a los 19 s de acabar sigue (Ollama aún se recupera)", gate.Active(false, At(29)));
Check("a los 20 s termina y el vigilante vuelve a actuar", !gate.Active(false, At(30)));
Check("una limpieza larga se renueva en cada consulta", gate.Active(true, At(100)) && gate.Active(true, At(160)) && gate.Active(false, At(170)));

// MARK: dock tipo notch
Section("Dock tipo notch");
Win32.RECT NR(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };
var nm = NR(0, 0, 1920, 1080);
var nw = NR(0, 0, 1920, 1040);   // barra de tareas abajo
var nr = NotchLayout.Place(nm, nw, 1.0, 340, 600);
Check("notch: la pastilla queda centrada y pegada al borde de arriba",
    nr.Pill.Top == 0 && nr.Pill.Width == 230 && nr.Pill.Height == 37 && nr.Pill.Left == 960 - 115);
Check("notch: expandida mantiene el centro y mide lo que el contenido", nr.Expanded.Width == 340 && nr.Expanded.Height == 600
    && nr.Expanded.Left + nr.Expanded.Width / 2 == 960);
Check("notch: la zona activa llega al borde de la pantalla y es más ancha que la pastilla",
    nr.Hot.Top == 0 && nr.Hot.Left < nr.Pill.Left && nr.Hot.Right > nr.Pill.Right && NotchLayout.Contains(nr.Hot, 960, 0));
Check("notch: el cursor lejos de la pastilla no la despierta", !NotchLayout.Contains(nr.Hot, 200, 5) && !NotchLayout.Contains(nr.Hot, 960, 200));
var nTop = NotchLayout.Place(nm, NR(0, 48, 1920, 1080), 1.0, 340, 600);
Check("notch: con la barra de tareas arriba queda justo debajo", nTop.Pill.Top == 48 && nTop.Hot.Top == 0);
var tall = NotchLayout.Place(nm, nw, 1.0, 340, 5000);
Check("notch: la vista expandida no se sale del área de trabajo", tall.Expanded.Bottom <= nw.Bottom);
var hi = NotchLayout.Place(NR(0, 0, 3840, 2160), NR(0, 0, 3840, 2120), 2.0, 340, 600);
Check("notch: a 200 % de escala todo se duplica en píxeles", hi.Pill.Width == 460 && hi.Pill.Height == 74 && hi.Expanded.Width == 680);
var nLeft = NotchLayout.Place(NR(-1920, 0, 0, 1080), NR(-1920, 0, 0, 1040), 1.5, 340, 600);
Check("notch: un monitor a la izquierda (origen negativo) centra en su propio ancho",
    nLeft.Pill.Left + nLeft.Pill.Width / 2 == -960 && nLeft.Pill.Left >= -1920 && nLeft.Pill.Right <= 0);
Check("notch: nunca es más ancha que el monitor", NotchLayout.Place(NR(0, 0, 300, 600), NR(0, 0, 300, 600), 1.0, 340, 200).Expanded.Width <= 300);

var nDock = NotchLayout.DockBelow(nr.Expanded, 1.0);
Check("notch: los botones flotantes cuelgan centrados bajo la isla, con su hueco",
    nDock.Top == nr.Expanded.Bottom + 8 && nDock.Height == 34 && nDock.Width == 276
    && nDock.Left + nDock.Width / 2 == nr.Expanded.Left + nr.Expanded.Width / 2);
var nAll = NotchLayout.Union(nr.Expanded, nDock);
Check("notch: la zona de la isla con sus botones los incluye a los dos",
    nAll.Top == nr.Expanded.Top && nAll.Bottom == nDock.Bottom && NotchLayout.Contains(nAll, 960, nDock.Top + 5) && NotchLayout.Contains(nAll, 960, 10));
Check("notch: a 150 % el dock crece con la escala", NotchLayout.DockBelow(NotchLayout.Place(nm, nw, 1.5, 340, 200).Expanded, 1.5).Height == 51);

var ns = new NotchState();
Check("notch: empieza colapsada", ns.Mode == NotchMode.Collapsed && !ns.Hidden);
Check("notch: un roce no la abre", !ns.Update(true, false, false, 0) && !ns.Update(false, false, false, 0.1) && ns.Mode == NotchMode.Collapsed);
ns.Update(true, false, false, 1);
Check("notch: tarda 0,15 s en abrirse con el cursor encima", ns.Mode == NotchMode.Collapsed
    && ns.Update(true, false, false, 1.16) && ns.Mode == NotchMode.Expanded);
Check("notch: un instante fuera no la cierra", !ns.Update(false, false, false, 2) && !ns.Update(true, false, false, 2.2) && ns.Mode == NotchMode.Expanded);
ns.Update(false, false, false, 3);
Check("notch: tarda 0,4 s en cerrarse", ns.Mode == NotchMode.Expanded && ns.Update(false, false, false, 3.41) && ns.Mode == NotchMode.Collapsed);
ns.Update(true, false, false, 4); ns.Update(true, false, false, 4.2);
Check("notch: con un menú abierto no se cierra aunque el cursor salga",
    ns.Mode == NotchMode.Expanded && !ns.Update(false, false, true, 5) && !ns.Update(false, false, true, 6) && ns.Mode == NotchMode.Expanded);
ns.Pin();
Check("notch: fijada, no se cierra sola ni se oculta por pantalla completa",
    ns.Mode == NotchMode.Pinned && !ns.Update(false, true, false, 10) && ns.Mode == NotchMode.Pinned && !ns.Hidden);
ns.Close();
Check("notch: Esc o perder el foco la devuelven a la pastilla", ns.Mode == NotchMode.Collapsed);
Check("notch: a pantalla completa se oculta, y no se abre con el cursor",
    ns.Update(true, true, false, 20) && ns.Hidden && ns.Mode == NotchMode.Collapsed && !ns.Update(true, true, false, 21) && ns.Mode == NotchMode.Collapsed);
Check("notch: al salir de pantalla completa reaparece", ns.Update(false, false, false, 22) && !ns.Hidden);
ns.Pin(); ns.Pin();
Check("notch: fijar dos veces es lo mismo", ns.Mode == NotchMode.Pinned);

// MARK: widgets del notch

Section("Widgets del notch");
Check("widgets: sin nada guardado salen los de siempre y caben justos",
    WidgetLayout.Normalize(null).SequenceEqual(new[] { "ollama", "system", "clock" }) && WidgetLayout.Cells(WidgetLayout.Normalize(null)) == 4);
Check("widgets: lo desconocido y lo repetido se descarta, y lo que no cabe en cuatro celdas también",
    WidgetLayout.Normalize(new[] { "ollama", "bogus", "ollama", "notes", "system" }).SequenceEqual(new[] { "ollama", "notes" }));
Check("widgets: una lista vacía guardada sigue vacía (no vuelven los de siempre)", WidgetLayout.Normalize(Array.Empty<string>()).Count == 0);
var full = new List<string> { "ollama", "system", "clock" };
Check("widgets: con el Home lleno no se puede añadir otro y añadir no cambia nada",
    !WidgetLayout.CanAdd(full, "pomodoro") && WidgetLayout.Add(full, "pomodoro").SequenceEqual(full));
Check("widgets: quitar uno hace sitio para otro", WidgetLayout.CanAdd(WidgetLayout.Remove(full, "clock"), "pomodoro")
    && WidgetLayout.Add(WidgetLayout.Remove(full, "clock"), "pomodoro").SequenceEqual(new[] { "ollama", "system", "pomodoro" }));
Check("widgets: uno de dos celdas no cabe donde solo queda una", !WidgetLayout.CanAdd(new List<string> { "ollama", "system" }, "notes") && WidgetLayout.CanAdd(new List<string> { "ollama" }, "notes"));
Check("widgets: subir y bajar intercambia con el vecino",
    WidgetLayout.Move(full, "clock", -1).SequenceEqual(new[] { "ollama", "clock", "system" })
    && WidgetLayout.Move(full, "ollama", +1).SequenceEqual(new[] { "system", "ollama", "clock" }));
Check("widgets: en el borde se queda donde está", WidgetLayout.Move(full, "ollama", -1).SequenceEqual(full) && WidgetLayout.Move(full, "clock", +1).SequenceEqual(full));
Check("widgets: un id desconocido no mueve nada", WidgetLayout.Move(full, "bogus", -1).SequenceEqual(full));
Check("widgets: el editor guarda ancho sin romper ids anteriores y no ensancha los fijos",
    WidgetLayout.ToggleWidth(new[] { "system" }, "system").SequenceEqual(new[] { "system@2" })
    && WidgetLayout.Normalize(new[] { "system@2", "ollama" }).SequenceEqual(new[] { "system@2", "ollama" })
    && WidgetLayout.Cells(new[] { "system@2", "clock" }) == 3
    && !WidgetLayout.CanResize("ollama")
    && WidgetLayout.ToggleWidth(new[] { "system", "ollama", "clock" }, "system").SequenceEqual(new[] { "system", "ollama", "clock" })
    && !WidgetLayout.DragResizeTriggered(false, 35) && WidgetLayout.DragResizeTriggered(false, 36)
    && !WidgetLayout.DragResizeTriggered(true, -35) && WidgetLayout.DragResizeTriggered(true, -36));
Check("widgets: el editor mueve arrastrando delante del destino",
    WidgetLayout.MoveBefore(new[] { "system", "clock", "pomodoro" }, "pomodoro", "system").SequenceEqual(new[] { "pomodoro", "system", "clock" })
    && WidgetLayout.MoveBefore(new[] { "system", "clock" }, "audio", "system").SequenceEqual(new[] { "audio", "system", "clock" }));
Check("widgets: el catálogo no repite ids y todos son de una o dos celdas",
    WidgetCatalog.All.Select(w => w.Id).Distinct().Count() == WidgetCatalog.All.Count && WidgetCatalog.All.All(w => w.Cells is 1 or 2));
Check("widgets: un widget de una celda solo en su fila se ensancha; con compañero se queda en una celda",
    WidgetLayout.Arrange(new[] { "ollama", "system", "clock" }).SequenceEqual(new[] { ("ollama", 2), ("system", 1), ("clock", 1) })
    && WidgetLayout.Arrange(new[] { "system" }).SequenceEqual(new[] { ("system", 2) })
    && WidgetLayout.Arrange(new[] { "clock", "pomodoro", "awake" }).SequenceEqual(new[] { ("clock", 1), ("pomodoro", 1), ("awake", 2) })
    && WidgetLayout.Arrange(new[] { "system", "ollama", "clock" }).SequenceEqual(new[] { ("system", 2), ("ollama", 2), ("clock", 2) })
    && WidgetLayout.Arrange(Array.Empty<string>()).Count == 0);
Check("widgets: la vista corta tiene sus propios valores por defecto y su propia lista vacía",
    WidgetLayout.Normalize(null, WidgetCatalog.DefaultPeek).SequenceEqual(new[] { "ollama" }) && WidgetLayout.Normalize(Array.Empty<string>(), WidgetCatalog.DefaultPeek).Count == 0);
Check("pastilla: sin nada guardado salen estado, modelo y RAM, y caben tres",
    PillCatalog.Normalize(null).SequenceEqual(new[] { "state", "model", "ram" })
    && PillCatalog.Normalize(new[] { "clock", "x", "cpu", "clock", "ram", "state" }).SequenceEqual(new[] { "clock", "cpu", "ram" })
    && PillCatalog.Toggle(new List<string> { "state", "model", "ram" }, "cpu").Count == 3
    && PillCatalog.Toggle(new List<string> { "state", "model" }, "cpu").SequenceEqual(new[] { "state", "model", "cpu" }));
Check("pastilla: la CPU, la hora y la batería piden su widget; el resto no",
    PillCatalog.WidgetsNeeded(new[] { "cpu", "clock", "battery", "ram" }).SequenceEqual(new[] { "system", "clock", "battery" })
    && !PillCatalog.WidgetsNeeded(new[] { "state", "model", "ram", "vram", "pomodoro" }).Any());
var happening = new List<Activity> { new("awake", "g", "", 10), new("pomodoro", "g", "24:00", 50), new("clean", "g", "Liberando…", 90) };
Check("actividades: salen las más urgentes primero y caben dos",
    ActivityQueue.Pick(happening, ActivityCatalog.Normalize(null)).Select(a => a.Id).SequenceEqual(new[] { "clean", "pomodoro" }));
Check("actividades: las que el usuario apagó no salen y la siguiente ocupa su sitio",
    ActivityQueue.Pick(happening, ActivityCatalog.Toggle(ActivityCatalog.Normalize(null), "clean")).Select(a => a.Id).SequenceEqual(new[] { "pomodoro", "awake" })
    && !ActivityQueue.Pick(happening, Array.Empty<string>()).Any());
var both = new List<Activity> { new("awake", "g", "", 10), new("pomodoro", "g", "24:00", 50), new("media", "g", "canción", 40), new("clean", "g", "Liberando…", 90, true), new("power", "g", "Encendiendo…", 80, true) };
var sides = ActivityQueue.PickSides(both, ActivityCatalog.All.Select(a => a.Id).ToList());
Check("actividades: lo de Ollama sale a la izquierda (una) y lo demás a la derecha (dos), cada lado por urgencia",
    sides.Left.Select(a => a.Id).SequenceEqual(new[] { "clean" }) && sides.Right.Select(a => a.Id).SequenceEqual(new[] { "pomodoro", "media" }));
Check("actividades: limpieza, encendido y memoria crítica van a la izquierda; el resto, a la derecha",
    ActivityCatalog.All.Where(a => a.Left).Select(a => a.Id).OrderBy(i => i).SequenceEqual(new[] { "clean", "power", "pressure" }));
Check("actividades: sin nada de Ollama, la izquierda queda vacía", ActivityQueue.PickSides(both.Where(a => !a.Left), ActivityCatalog.All.Select(a => a.Id).ToList()).Left.Count == 0);
Check("actividades: sin nada guardado están las de siempre (la música no, hasta que se activa); lo desconocido se descarta",
    ActivityCatalog.Normalize(null).SequenceEqual(ActivityCatalog.All.Where(a => a.DefaultOn).Select(a => a.Id)) && ActivityCatalog.Normalize(new[] { "x", "awake", "awake" }).SequenceEqual(new[] { "awake" })
    && ActivityCatalog.All.Select(a => a.Id).Distinct().Count() == ActivityCatalog.All.Count);
Check("actividades: la pastilla más ancha mantiene su centro y su zona activa",
    NotchLayout.Place(nm, nw, 1.0, 340, 200, 330).Pill.Width == 330 && NotchLayout.Place(nm, nw, 1.0, 340, 200, 330).Hot.Right > NotchLayout.Place(nm, nw, 1.0, 340, 200).Hot.Right
    && NotchLayout.Place(nm, nw, 1.0, 340, 200, 100).Pill.Width == 230);

Section("Audio, series y datos locales");
var audioLevels = AudioMixer.Remember(null, @"C:\Apps\Player.exe", 1.3);
Check("audio: niveles recordados por exe, limitados a 0–1 y sin ids inválidos",
    AudioMixer.NormalizeExecutable(@"C:\Apps\PLAYER.EXE") == "player.exe"
    && AudioMixer.Remembered(audioLevels, "PLAYER.exe") == 1
    && AudioMixer.Remember(new[] { "broken", "old.exe|0.4" }, "OLD.exe", .7).Count == 1);
var series = new SeriesBuffer(3); series.Add(.2); series.Add(double.NaN); series.Add(1.5); series.Add(.8);
Check("series: mantiene las últimas muestras y limita valores", series.Snapshot().SequenceEqual(new[] { 0d, 1d, .8d }));
var meeting = MeetingLink.Extract("https://teams.microsoft.com/l/meetup-join/123", "Sala 4", "Notas https://example.com/info.");
Check("reunión: prioriza URL, limpia puntuación y solo acepta HTTP(S)", meeting?.Host == "teams.microsoft.com"
    && MeetingLink.Extract("https://zoom.us/j/1", "Sala · https://teams.microsoft.com/l/meetup-join/2", null)?.Host == "teams.microsoft.com"
    && MeetingLink.Extract(null, "Sala 4", "https://example.com/meet.")?.AbsolutePath == "/meet"
    && MeetingLink.Extract("file:///C:/meet", null, null) is null);
var agentFixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
var claudeAgent = AgentLogs.Parse("claude-1", "project", "Claude Code", File.ReadAllLines(Path.Combine(agentFixtures, "claude-active.jsonl")), DateTime.UtcNow);
var repeatedCommands = Enumerable.Range(1, 8).SelectMany(i => new[]
{
    System.Text.Json.JsonSerializer.Serialize(new { type = "response_item", payload = new { type = "custom_tool_call", call_id = $"repeat-{i}", name = "exec", status = "completed" } }),
    System.Text.Json.JsonSerializer.Serialize(new { type = "response_item", payload = new { type = "custom_tool_call_output", call_id = $"repeat-{i}", output = "done" } }),
});
var currentCommand = System.Text.Json.JsonSerializer.Serialize(new { type = "response_item", payload = new { type = "custom_tool_call", call_id = "command-current", name = "exec" } });
var codexAgent = AgentLogs.Parse("rollout-2026-01-01T00-00-00-01234567-89ab-cdef-0123-456789abcdef", "sample-codex-project", "Codex",
    File.ReadAllLines(Path.Combine(agentFixtures, "codex-active.jsonl")).Concat(repeatedCommands).Append(currentCommand), DateTime.UtcNow);
var heartbeatAgent = AgentLogs.Parse("heartbeat-1", "daily-check", "Codex", File.ReadAllLines(Path.Combine(agentFixtures, "codex-heartbeat.jsonl")), DateTime.UtcNow);
Check("agentes: extrae el último mensaje del agente, proyecto y herramienta de Claude sin exponer prompts",
    claudeAgent.State == AgentState.Working && claudeAgent.Provider == "Claude Code" && claudeAgent.ProjectName == "sample-project"
    && claudeAgent.Name == "Update sample project"
    && claudeAgent.Tool == "Read" && claudeAgent.ToolAction == "Leyendo archivo" && claudeAgent.File == "C:/projects/sample-project/Widget.cs"
    && claudeAgent.Message == "I am checking the sample file and its references."
    && claudeAgent.RecentActivity[0].Tool == "Read" && claudeAgent.RecentActivity.Any(a => a.Message == claudeAgent.Message)
    && !claudeAgent.ToString().Contains("PRIVATE_USER_PROMPT_MARKER", StringComparison.Ordinal)
    && claudeAgent.RecentActivity.All(a => !a.Summary.Contains("PRIVATE_USER_PROMPT_MARKER", StringComparison.Ordinal)));
Check("agentes: lee actividad y cambios estructurados de Codex",
    codexAgent.State == AgentState.Working && codexAgent.ProjectName == "sample-codex-project" && codexAgent.Provider == "Codex"
    && codexAgent.Name == "sample-codex-project · 012345"
    && codexAgent.Tool == "exec" && codexAgent.ToolAction == "Ejecutando comando"
    && codexAgent.Message == "Voy a corregir el comportamiento y dejar la prueba actualizada."
    && codexAgent.RecentActivity[0].Tool == "exec" && codexAgent.RecentActivity.Any(a => a.Message == codexAgent.Message)
    && codexAgent.WorkSummary == "9 comandos ejecutados · 1 archivo editado"
    && codexAgent.RecentActivity.Count(a => a.Tool == "exec") == 2
    && codexAgent.RecentActivity.Any(a => a.Tool == "exec" && a.Completed)
    && codexAgent.Added == 3 && codexAgent.Removed == 1 && codexAgent.File == "src/Main.cs");
Check("agentes: convierte el XML de automatización en un resultado legible sin repetir el mensaje",
    heartbeatAgent.State == AgentState.Finished && heartbeatAgent.IsAutomation
    && heartbeatAgent.AutomationId == "scheduled-check-example"
    && heartbeatAgent.Name == "Scheduled check example"
    && heartbeatAgent.Message == "No new result was found; no notification was sent."
    && !heartbeatAgent.Message.Contains("<heartbeat", StringComparison.Ordinal)
    && heartbeatAgent.RecentActivity.Count == 1
    && heartbeatAgent.RecentActivity[0].Summary == "Comprobación automática · sin aviso");
var agentNow = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
var justFinished = new AgentSnapshot("finished-1", "C:/src/App", "Codex", AgentState.Finished, null, null, null, null, "Terminé.", [], agentNow.AddMinutes(-1));
var staleWorking = new AgentSnapshot("idle-1", "C:/src/Idle", "Claude Code", AgentState.Working, null, null, null, null, null, [], agentNow.AddMinutes(-7));
var staleAttention = new AgentSnapshot("attention-1", "C:/src/Review", "Codex", AgentState.NeedsAttention, null, null, null, null, null, [], agentNow.AddMinutes(-6));
var activeWorking = new AgentSnapshot("active-1", "C:/src/Active", "Codex", AgentState.Working, null, null, null, null, null, [], agentNow.AddMinutes(-4));
var atRecentBoundary = new AgentSnapshot("window-1", "C:/src/Window", "Codex", AgentState.Finished, null, null, null, null, null, [], agentNow.Add(-AgentLogs.RecentWindow));
var expired = new AgentSnapshot("expired-1", "C:/src/Old", "Codex", AgentState.Finished, null, null, null, null, null, [], agentNow.AddHours(-12).AddSeconds(-1));
var recentAgents = AgentLogs.RecentSessions([], [justFinished, staleWorking, staleAttention, activeWorking, atRecentBoundary, expired], agentNow);
Check("agentes: incluye terminadas recientes e inactivas hasta 12 horas sin duplicar las activas",
    recentAgents.Count == 4 && recentAgents.Single(s => s.Session == "idle-1").State == AgentState.Inactive
    && recentAgents.Single(s => s.Session == "attention-1").State == AgentState.NeedsAttention
    && recentAgents.Any(s => s.Session == "finished-1") && recentAgents.Any(s => s.Session == "window-1")
    && recentAgents.All(s => s.Session != "active-1" && s.Session != "expired-1")
    && AgentLogs.RecentSessions(recentAgents, [staleWorking with { LastActivity = agentNow }], agentNow).All(s => s.Session != "idle-1"));
var exactlyFiveMinutes = activeWorking with { LastActivity = agentNow - AgentLogs.InactiveAfter };
var justPastFiveMinutes = exactlyFiveMinutes with { Session = "idle-boundary", LastActivity = exactlyFiveMinutes.LastActivity.AddTicks(-1) };
Check("agentes: cinco minutos exactos siguen activos; al superarlos pasan a recientes",
    AgentLogs.IsActive(exactlyFiveMinutes, agentNow)
    && AgentLogs.RecentSessions([], [exactlyFiveMinutes, justPastFiveMinutes], agentNow).Single().State == AgentState.Inactive);
Check("agentes: conserva el aviso y el tiempo sin actividad para sesiones que necesitan atención",
    recentAgents.Single(s => s.Session == "attention-1").StateLabel == "Necesita tu atención"
    && recentAgents.Single(s => s.Session == "attention-1").InactivityLabelAt(agentNow) == "Sin actividad hace 6 min"
    && recentAgents.Single(s => s.Session == "idle-1").StateLabel == "Sin actividad"
    && recentAgents.Single(s => s.Session == "idle-1").InactivityLabelAt(agentNow) == "Sin actividad hace 7 min"
    && AgentLogs.AgeLabel(agentNow.AddMinutes(-62), agentNow) == "1 h 2 min");
var repeatedEdit = new[]
{
    "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"name\":\"Edit\",\"id\":\"edit-1\",\"input\":{\"file_path\":\"src/Main.cs\"}}]}}",
    "{\"type\":\"user\",\"message\":{\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"edit-1\"}]}}",
    "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"name\":\"Edit\",\"id\":\"edit-2\",\"input\":{\"file_path\":\"src/Main.cs\"}}]}}",
    "{\"type\":\"user\",\"message\":{\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"edit-2\"}]}}",
    "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"name\":\"Edit\",\"id\":\"edit-3\",\"input\":{}}]}}",
    "{\"type\":\"user\",\"message\":{\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"edit-3\"}]}}",
};
var editSummary = AgentLogs.Parse("edits", "C:/src", "Claude Code", repeatedEdit, agentNow);
Check("agentes: cuenta rutas únicas editadas y no inventa archivos si el log omite la ruta",
    editSummary.WorkSummary == "1 archivo editado");
var codexExplicitPath = System.Text.Json.JsonSerializer.Serialize(new
{
    type = "response_item",
    payload = new { type = "custom_tool_call", call_id = "edit-file", name = "Edit", arguments = "{\"file_path\":\"src/Chosen.cs\"}", status = "completed" },
});
var codexPathAgent = AgentLogs.Parse("codex-edit", "C:/src", "Codex", [codexExplicitPath], agentNow);
Check("agentes: acepta rutas explícitas de herramientas de archivo de Codex sin conservar sus argumentos",
    codexPathAgent.File == "src/Chosen.cs" && codexPathAgent.WorkSummary == "1 archivo editado"
    && codexPathAgent.RecentActivity.All(a => a.Summary != "{\"file_path\":\"src/Chosen.cs\"}"));
var forecast = WeatherForecast.Parse("Panamá", "{\"current\":{\"time\":\"2026-10-17T12:00\",\"temperature_2m\":29,\"weather_code\":1},\"hourly\":{\"time\":[\"2026-10-17T12:00\",\"2026-10-17T13:00\"],\"temperature_2m\":[29,30],\"weather_code\":[1,2]}}");
Check("tiempo: decodifica condiciones y horas", forecast is { Temperature: 29, Code: 1 } && forecast.Hours.Count == 2 && forecast.Hours[1].Temperature == 30);
Check("favoritos: sin nada guardado salen encender, liberar RAM y procesos",
    FavoriteCatalog.Normalize(null).SequenceEqual(new[] { "power", "clean", "processes" }));
var appFav = FavoriteCatalog.AppId(@"C:\Program Files\Demo\demo-app.exe");
Check("favoritos: una app del usuario se guarda con su ruta y se titula con su nombre",
    FavoriteCatalog.IsApp(appFav) && FavoriteCatalog.AppPath(appFav) == @"C:\Program Files\Demo\demo-app.exe"
    && FavoriteCatalog.Find(appFav)?.Title == "demo-app" && !FavoriteCatalog.IsApp("app:") && !FavoriteCatalog.IsApp("power"));
Check("favoritos: las apps cuentan en el tope de cuatro y sobreviven a Normalize",
    FavoriteCatalog.Normalize(new[] { "power", appFav, appFav }).SequenceEqual(new[] { "power", appFav })
    && FavoriteCatalog.Toggle(FavoriteCatalog.Toggle(new[] { "power", "clean", "free" }, appFav), FavoriteCatalog.AppId("x.exe")).Count == 4);
Check("favoritos: lo desconocido y lo repetido se descarta", FavoriteCatalog.Normalize(new[] { "x", "power", "power" }).SequenceEqual(new[] { "power" }));
var favs = new List<string> { "power", "clean", "processes" };
var fourFavs = FavoriteCatalog.Toggle(favs, "free");
Check("favoritos: caben cuatro y el quinto no entra",
    fourFavs.Count == 4 && FavoriteCatalog.Toggle(fourFavs, "awake").SequenceEqual(fourFavs) && FavoriteCatalog.Toggle(fourFavs, "free").Count == 3);

var pomo = new Pomodoro();
var pomoStart = new DateTime(2026, 1, 1, 9, 0, 0);
Check("pomodoro: empieza en 25:00, en pausa", pomo.Left == TimeSpan.FromMinutes(25) && !pomo.Running && pomo.Phase == PomodoroPhase.Focus && !pomo.Tick(pomoStart));
pomo.Start(pomoStart);
Check("pomodoro: cuenta el tiempo que pasa", !pomo.Tick(pomoStart.AddMinutes(10)) && pomo.Left == TimeSpan.FromMinutes(15));
pomo.Pause(pomoStart.AddMinutes(11));
Check("pomodoro: al pausar guarda lo que quedaba y el tiempo parado no cuenta",
    pomo.Left == TimeSpan.FromMinutes(14) && !pomo.Running && !pomo.Tick(pomoStart.AddHours(5)) && pomo.Left == TimeSpan.FromMinutes(14));
pomo.Start(pomoStart.AddHours(6));
Check("pomodoro: al acabar el enfoque avisa una vez y deja el descanso listo, en pausa",
    pomo.Tick(pomoStart.AddHours(6).AddMinutes(14)) && pomo.Phase == PomodoroPhase.Break && pomo.Left == Pomodoro.BreakLength && !pomo.Running
    && !pomo.Tick(pomoStart.AddHours(7)));
pomo.Toggle(pomoStart.AddHours(8));
Check("pomodoro: el descanso dura 5 min y al acabar vuelve al enfoque",
    pomo.Tick(pomoStart.AddHours(8).AddMinutes(5)) && pomo.Phase == PomodoroPhase.Focus && pomo.Left == Pomodoro.FocusLength);
pomo.Start(pomoStart); pomo.Reset();
Check("pomodoro: reiniciar lo deja en 25:00 parado", !pomo.Running && pomo.Left == Pomodoro.FocusLength && pomo.Phase == PomodoroPhase.Focus && pomo.Progress == 0);
Check("pomodoro: el formato es mm:ss y redondea hacia arriba",
    Pomodoro.Format(TimeSpan.FromSeconds(65)) == "01:05" && Pomodoro.Format(TimeSpan.Zero) == "00:00" && Pomodoro.Format(TimeSpan.FromSeconds(0.2)) == "00:01"
    && Pomodoro.Format(TimeSpan.FromSeconds(-3)) == "00:00");
Check("cpu: la mitad del tiempo ocupado es el 50 %", Math.Abs(CpuMath.Percent(0, 0, 0, 50, 100, 0) - 50) < 0.001);
Check("cpu: sin tiempo transcurrido es 0 y nunca pasa de 100",
    CpuMath.Percent(5, 5, 5, 5, 5, 5) == 0 && CpuMath.Percent(0, 0, 0, 0, 100, 0) == 100);

// MARK: agente de memoria

Section("Agente de memoria");
{
    // El agente corre como administrador y se instala una vez: solo se cambia su versión (AGENT_VERSION) cuando cambia su código.
    // Este hash de sus fuentes obliga a subirla (y a actualizar el hash) en el mismo commit que lo toque.
    string? repo = AppContext.BaseDirectory;
    while (repo is not null && !File.Exists(Path.Combine(repo, "AGENT_VERSION"))) repo = Path.GetDirectoryName(repo);
    Check("agente: se encuentra AGENT_VERSION en el repositorio", repo is not null);
    if (repo is not null)
    {
        var agentFiles = Directory.GetFiles(Path.Combine(repo, "src", "IsTargetSleeping.Agent"), "*.cs")
            .Concat(new[] { "CleanSpec.cs", "MemoryContracts.cs", "ProcessContracts.cs", "ProcessMonitoring.cs", "ProcessActions.cs" }
                .Select(n => Path.Combine(repo, "src", "IsTargetSleeping", "Core", n)))
            .Select(p => Path.GetRelativePath(repo, p).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal).ToList();
        var sha = System.Security.Cryptography.SHA256.Create();
        var text = new System.Text.StringBuilder();
        foreach (var f in agentFiles) text.Append(f).Append('\n').Append(File.ReadAllText(Path.Combine(repo, f)).Replace("\r\n", "\n")).Append('\n');
        var hash = Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
        Check("agente: si cambia su código hay que subir AGENT_VERSION y actualizar este hash (" + hash + ")", hash == "0abcfa4ad267fd1b505f10d9500246176fe006133d5b70a942ed28b214a78c5b");
        var agentVersion = File.ReadAllText(Path.Combine(repo, "AGENT_VERSION")).Trim();
        var appVersion = File.ReadAllText(Path.Combine(repo, "VERSION")).Trim();
        Check("agente: AGENT_VERSION es una versión y no supera a la de la app",
            Version.TryParse(agentVersion, out var av) && Version.TryParse(appVersion, out var pv) && av <= pv);
    }
}

// MARK: bandeja de archivos y portapapeles

Section("Bandeja de archivos");
{
    var dir = Path.Combine(Path.GetTempPath(), "its-tray-" + Guid.NewGuid().ToString("N"));
    var sub = Path.Combine(dir, "carpeta"); Directory.CreateDirectory(Path.Combine(sub, "dentro"));
    var otra = Path.Combine(dir, "otra"); Directory.CreateDirectory(otra);
    var a = Path.Combine(dir, "a.txt"); File.WriteAllText(a, "hola");
    var b = Path.Combine(otra, "a.txt"); File.WriteAllText(b, "otro a");
    File.WriteAllText(Path.Combine(sub, "x.txt"), "x"); File.WriteAllText(Path.Combine(sub, "dentro", "y.txt"), "y");
    try
    {
        var tray = new FileTray();
        Check("bandeja: entra lo que existe, sin repetir, y lo que no existe se ignora",
            tray.Add(new[] { a, a, Path.Combine(dir, "no-existe.txt"), sub }) == 2 && tray.Count == 2
            && tray.Entries[0] is { IsDirectory: false, Name: "a.txt", Size: 4 } && tray.Entries[1] is { IsDirectory: true, Name: "carpeta" });
        Check("bandeja: la misma ruta con otra grafía de mayúsculas no se repite", tray.Add(new[] { a.ToUpperInvariant() }) == 0);
        Check("bandeja: el total suma el tamaño de los archivos", tray.TotalBytes == 4);
        tray.Add(new[] { b });
        var zipPath = tray.ZipTo(dir, "prueba");
        using (var zip = System.IO.Compression.ZipFile.OpenRead(zipPath))
        {
            var names = zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Check("bandeja: el zip lleva archivos, carpetas con su contenido y nombres repetidos renumerados",
                names.SequenceEqual(new[] { "a (2).txt", "a.txt", "carpeta/dentro/y.txt", "carpeta/x.txt" }));
        }
        Check("bandeja: otro zip con el mismo nombre no sobrescribe", tray.ZipTo(dir, "prueba").EndsWith("prueba (2).zip") && File.Exists(zipPath));
        File.Delete(a);
        Check("bandeja: lo que se borró del disco se quita solo", tray.Prune() == 1 && tray.Entries.All(e => e.Name != "a.txt" || e.Path == b));
        tray.Remove(b);
        tray.Clear();
        Check("bandeja: vaciar quita todo", tray.Count == 0 && tray.TotalBytes == 0);
        var folderBox = Path.Combine(dir, "medir"); Directory.CreateDirectory(Path.Combine(folderBox, "a", "b"));
        File.WriteAllText(Path.Combine(folderBox, "uno.txt"), new string('x', 1000)); File.WriteAllText(Path.Combine(folderBox, "a", "dos.txt"), "yy"); File.WriteAllText(Path.Combine(folderBox, "a", "b", "tres.txt"), "zzz");
        var measured = FileTray.Measure(folderBox);
        Check("bandeja: mide una carpeta (archivos, subcarpetas y bytes) recorriendo todo su contenido", measured == new FolderStats(3, 2, 1005, false));
        Check("bandeja: una carpeta con más elementos que el tope se corta y lo dice", FileTray.Measure(folderBox, cap: 3) is { Truncated: true } && FileTray.Measure(Path.Combine(dir, "no-existe")) == new FolderStats(0, 0, 0, false));
        var many = new FileTray();
        var files = Enumerable.Range(0, FileTray.Max + 10).Select(i => { var f = Path.Combine(dir, $"m{i}.txt"); File.WriteAllText(f, "m"); return f; }).ToList();
        Check("bandeja: tiene tope", many.Add(files) == FileTray.Max && many.Count == FileTray.Max);
    }
    finally { try { Directory.Delete(dir, true); } catch { } }
}

Section("Historial del portapapeles");
{
    var history = new ClipboardHistory();
    Check("portapapeles: vacío o solo espacios no entra", !history.Add(null) && !history.Add("") && !history.Add("  \n\t") && history.Count == 0);
    Check("portapapeles: lo último copiado va primero", history.Add("uno") && history.Add("dos") && history.Items.SequenceEqual(new[] { "dos", "uno" }));
    Check("portapapeles: copiar lo mismo dos veces seguidas no repite", !history.Add("dos") && history.Count == 2);
    Check("portapapeles: un texto que ya estaba sube al primer puesto sin duplicarse", history.Add("uno") && history.Items.SequenceEqual(new[] { "uno", "dos" }));
    for (int i = 0; i < ClipboardHistory.Max + 5; i++) history.Add("texto " + i);
    Check("portapapeles: tiene tope y conserva lo más reciente", history.Count == ClipboardHistory.Max && history.Items[0] == "texto " + (ClipboardHistory.Max + 4));
    history.RemoveAt(0); history.RemoveAt(999);
    Check("portapapeles: se puede quitar una entrada y un índice fuera de rango no hace nada", history.Count == ClipboardHistory.Max - 1);
    Check("portapapeles: un texto enorme se recorta", new ClipboardHistory().Add(new string('x', ClipboardHistory.MaxLength + 50)));
    var huge = new ClipboardHistory(); huge.Add(new string('x', ClipboardHistory.MaxLength + 50));
    Check("portapapeles: el recorte deja MaxLength caracteres", huge.Items[0].Length == ClipboardHistory.MaxLength);
    Check("portapapeles: la vista previa es la primera línea con contenido y se corta con «…»",
        ClipboardHistory.Preview("\n  hola mundo \nsegunda") == "hola mundo" && ClipboardHistory.Preview(new string('a', 200), 20) == new string('a', 19) + "…");
    Check("portapapeles: lo marcado por un gestor de contraseñas no se registra",
        ClipboardHistory.ShouldRecord(false, null) && ClipboardHistory.ShouldRecord(false, 1) && !ClipboardHistory.ShouldRecord(true, null) && !ClipboardHistory.ShouldRecord(false, 0));
    history.Clear();
    Check("portapapeles: vaciar quita todo", history.Count == 0);
}

Section("Edición rápida de archivos de texto");
{
    var dir = Path.Combine(Path.GetTempPath(), "its-edit-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        var lf = Path.Combine(dir, "a.md"); File.WriteAllText(lf, "uno\ndos\n");
        var crlf = Path.Combine(dir, "b.txt"); File.WriteAllBytes(crlf, System.Text.Encoding.UTF8.GetBytes("uno\r\ndos\r\ntres"));
        var bom = Path.Combine(dir, "c.cs"); File.WriteAllBytes(bom, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(System.Text.Encoding.UTF8.GetBytes("ñandú\r\n")).ToArray());
        var utf16 = Path.Combine(dir, "d.txt"); File.WriteAllText(utf16, "hola\r\nmundo", new System.Text.UnicodeEncoding(false, true));
        var (docLf, e1, _) = TextFile.Load(lf);
        Check("editor: lee un archivo con saltos «\n» y los conserva al guardar",
            e1 == TextFileError.None && docLf!.Text == "uno\ndos\n" && docLf.Eol == "\n"
            && TextFile.Save(docLf, "uno\r\ndos\r\ncambio\r\n").Result == TextSaveResult.Saved && File.ReadAllText(lf) == "uno\ndos\ncambio\n");
        var (docCrlf, _, _) = TextFile.Load(crlf);
        TextFile.Save(docCrlf!, "uno\ndos\ntres y más");
        Check("editor: un archivo con «\r\n» sigue con «\r\n» y sin salto final si no lo tenía",
            docCrlf!.Eol == "\r\n" && File.ReadAllText(crlf) == "uno\r\ndos\r\ntres y más");
        var (docBom, _, _) = TextFile.Load(bom);
        TextFile.Save(docBom!, "ñandú 2\n");
        var bomBytes = File.ReadAllBytes(bom);
        Check("editor: conserva la marca BOM de UTF-8 y los acentos", docBom!.Text == "ñandú\n" && bomBytes[0] == 0xEF && bomBytes[1] == 0xBB && System.Text.Encoding.UTF8.GetString(bomBytes, 3, bomBytes.Length - 3) == "ñandú 2\r\n");
        var (docU16, e16, _) = TextFile.Load(utf16);
        TextFile.Save(docU16!, "adiós");
        Check("editor: UTF-16 se lee y se vuelve a guardar en UTF-16", e16 == TextFileError.None && docU16!.Text == "hola\nmundo" && File.ReadAllBytes(utf16)[0] == 0xFF && File.ReadAllText(utf16) == "adiós");
        var bin = Path.Combine(dir, "e.bin"); File.WriteAllBytes(bin, new byte[] { 1, 2, 0, 3, 4 });
        var ansi = Path.Combine(dir, "f.txt"); File.WriteAllBytes(ansi, new byte[] { 0x63, 0x61, 0x66, 0xE9 });   // «café» en ANSI: UTF-8 inválido
        var huge = Path.Combine(dir, "g.txt"); File.WriteAllBytes(huge, new byte[TextFile.MaxBytes + 1]);
        Check("editor: rechaza binarios, texto que no es UTF-8 válido y lo demasiado grande, sin tocarlos",
            TextFile.Load(bin).Error == TextFileError.NotText && TextFile.Load(ansi).Error == TextFileError.NotText
            && TextFile.Load(huge).Error == TextFileError.TooBig && TextFile.Load(Path.Combine(dir, "no-existe.txt")).Error == TextFileError.Unreadable);
        var (docConflict, _, _) = TextFile.Load(lf);
        File.WriteAllText(lf, "otro programa cambió esto\n");
        File.SetLastWriteTimeUtc(lf, DateTime.UtcNow.AddMinutes(5));
        var refused = TextFile.Save(docConflict!, "mis cambios\n");
        Check("editor: si el archivo cambió en el disco no lo pisa", refused.Result == TextSaveResult.Conflict && File.ReadAllText(lf) == "otro programa cambió esto\n");
        var forced = TextFile.Save(docConflict!, "mis cambios\n", overwrite: true);
        Check("editor: sobrescribir lo guarda y deja el documento al día", forced.Result == TextSaveResult.Saved && File.ReadAllText(lf) == "mis cambios\n"
            && TextFile.Save(forced.Doc, "otra vez\n").Result == TextSaveResult.Saved);
        Check("editor: no deja archivos temporales", Directory.GetFiles(dir, "*.tmp").Length == 0);
        File.SetAttributes(lf, FileAttributes.ReadOnly);
        var (docRo, _, _) = TextFile.Load(lf);
        var ro = TextFile.Save(docRo!, "no debería");
        File.SetAttributes(lf, FileAttributes.Normal);
        Check("editor: un archivo de solo lectura da error claro, no lo cambia y no deja temporales",
            ro.Result == TextSaveResult.Failed && File.ReadAllText(lf) == "otra vez\n" && Directory.GetFiles(dir, "*.tmp").Length == 0);
    }
    finally { try { Directory.Delete(dir, true); } catch { } }
}

Section("Árbol de archivos e iconos del widget Código");
{
    var dir = Path.Combine(Path.GetTempPath(), "its-code-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(Path.Combine(dir, "src", "sub"));
        foreach (var skipped in new[] { "node_modules", ".git", "bin", "obj" }) Directory.CreateDirectory(Path.Combine(dir, skipped));
        File.WriteAllText(Path.Combine(dir, "zeta.txt"), "z");
        File.WriteAllText(Path.Combine(dir, "Alpha.md"), "a");
        File.WriteAllText(Path.Combine(dir, "src", "a.cs"), "a");
        File.WriteAllText(Path.Combine(dir, "src", "sub", "b.ts"), "b");
        var hiddenFile = Path.Combine(dir, "secreto.txt"); File.WriteAllText(hiddenFile, "s"); File.SetAttributes(hiddenFile, FileAttributes.Hidden);
        var closed = CodeTree.Flatten(dir, new HashSet<string>());
        Check("árbol: carpetas antes que archivos y cada grupo por nombre, sin mayúsculas que descoloquen",
            closed.Select(r => r.Name).SequenceEqual(new[] { "src", "Alpha.md", "zeta.txt" }) && closed.All(r => r.Depth == 0));
        Check("árbol: esconde carpetas generadas (.git, node_modules, bin, obj) y lo oculto del sistema",
            closed.All(r => r.Name is not ("node_modules" or ".git" or "bin" or "obj" or "secreto.txt")));
        var open = CodeTree.Flatten(dir, new HashSet<string> { Path.Combine(dir, "src"), Path.Combine(dir, "src", "sub") });
        Check("árbol: una carpeta abierta enseña su contenido justo debajo y con sangría",
            open.Select(r => $"{r.Depth}:{r.Name}").SequenceEqual(new[] { "0:src", "1:sub", "2:b.ts", "1:a.cs", "0:Alpha.md", "0:zeta.txt" })
            && open[0].Expanded && !open[3].IsDirectory);
        Check("árbol: una carpeta que no existe o sin permiso da una lista vacía", CodeTree.Flatten(Path.Combine(dir, "no-existe"), new HashSet<string>()).Count == 0);
        var many = Path.Combine(dir, "muchos"); Directory.CreateDirectory(many);
        for (int i = 0; i < CodeTree.MaxPerFolder + 20; i++) File.WriteAllText(Path.Combine(many, $"f{i:D4}.txt"), "x");
        Check("árbol: una carpeta enorme se corta en su tope", CodeTree.Flatten(many, new HashSet<string>()).Count == CodeTree.MaxPerFolder);
        Check("árbol: reconoce las imágenes que el visor sabe mostrar", CodeTree.IsImage("a.PNG") && CodeTree.IsImage("b.jpeg") && !CodeTree.IsImage("c.svg") && !CodeTree.IsImage("d.cs"));
    }
    finally { try { Directory.Delete(dir, true); } catch { } }

    var iconFile = "";
    for (var probe = new DirectoryInfo(AppContext.BaseDirectory); probe is not null && iconFile.Length == 0; probe = probe.Parent)
        if (File.Exists(Path.Combine(probe.FullName, "src", "IsTargetSleeping", "Resources", "FileIcons.json.gz"))) iconFile = Path.Combine(probe.FullName, "src", "IsTargetSleeping", "Resources", "FileIcons.json.gz");
    Check("iconos: el archivo de datos está en el repo", iconFile.Length > 0);
    using var iconStream = File.OpenRead(iconFile);
    var icons = FileIconMap.Load(iconStream);
    Check("iconos: trae más de mil iconos con sus formas y su viewBox", icons.Count > 1000 && icons.Keys.All(k => icons.Get(k) is { ViewBox.Length: 4, Shapes.Count: > 0 }));
    Check("iconos: un archivo desconocido y una carpeta cualquiera caen en el genérico",
        icons.Resolve("algo.extensionrara", false, false) == "file" && icons.Resolve("mi-carpeta", true, false) == "folder" && icons.Resolve("mi-carpeta", true, true) == "folder-open");
    Check("iconos: cada lenguaje tiene el suyo, sin distinguir mayúsculas",
        icons.Resolve("main.ts", false, false) != "file" && icons.Resolve("MAIN.TS", false, false) == icons.Resolve("main.ts", false, false)
        && icons.Resolve("Program.cs", false, false) != icons.Resolve("main.ts", false, false) && icons.Resolve("x.py", false, false) != "file");
    Check("iconos: la extensión más larga gana (app.spec.ts no es un .ts cualquiera) y el nombre entero gana a la extensión",
        icons.Resolve("app.spec.ts", false, false) != icons.Resolve("app.ts", false, false)
        && icons.Resolve("package.json", false, false) != icons.Resolve("datos.json", false, false));
    Check("iconos: las carpetas conocidas (src, docs…) tienen icono propio, abierto y cerrado",
        icons.Resolve("src", true, false) != "folder" && icons.Resolve("SRC", true, false) == icons.Resolve("src", true, false)
        && icons.Resolve("src", true, true) != "folder-open" && icons.Get(icons.Resolve("src", true, true)) is not null);
    Check("iconos: un nombre acabado en punto no rompe la búsqueda", icons.Resolve("archivo.", false, false) == "file" && icons.Resolve(".", false, false) == "file");
}

Section("Resaltado de sintaxis");
{
    string Spans(string text, string file, TokenKind kind) =>
        string.Join("|", SyntaxHighlighter.Tokenize(text, SyntaxHighlighter.LanguageOf(file)).Where(t => t.Kind == kind).Select(t => text.Substring(t.Start, t.Length)));
    Check("resaltado: reconoce el lenguaje por el nombre del archivo",
        SyntaxHighlighter.LanguageOf("a.md") == "markdown" && SyntaxHighlighter.LanguageOf("C:\\x\\Program.CS") == "clike" && SyntaxHighlighter.LanguageOf("package.json") == "json"
        && SyntaxHighlighter.LanguageOf("Dockerfile") == "script" && SyntaxHighlighter.LanguageOf(".gitignore") == "script" && SyntaxHighlighter.LanguageOf("App.xaml") == "xml"
        && SyntaxHighlighter.LanguageOf("LICENSE") is null && SyntaxHighlighter.LanguageOf("foto.png") is null && SyntaxHighlighter.LanguageOf("") is null);
    const string md = "# Título `a`\n\nTexto **negrita** y `codigo` y [enlace](http://x.y).\n\n- uno\n> cita con **negrita**\n\n```\n# no es un título\n```\n| a | b |\n|---|---|\n";
    Check("resaltado: markdown colorea el título (con su código dentro por separado), la negrita, el código y el enlace",
        Spans(md, "x.md", TokenKind.Heading) == "# Título " && Spans(md, "x.md", TokenKind.Code).StartsWith("`a`|`codigo`") && Spans(md, "x.md", TokenKind.Bold).Contains("**negrita**")
        && Spans(md, "x.md", TokenKind.Link) == "[enlace](http://x.y)");
    Check("resaltado: markdown marca las viñetas, las citas y la tabla; un bloque de código no se parte en títulos",
        Spans(md, "x.md", TokenKind.Marker).Contains("-") && Spans(md, "x.md", TokenKind.Quote).StartsWith("> cita con ") && Spans(md, "x.md", TokenKind.Marker).Contains("|---|---|")
        && !Spans(md, "x.md", TokenKind.Heading).Contains("no es un título") && Spans(md, "x.md", TokenKind.Code).Contains("# no es un título"));
    const string cs = "// hola\nvar s = \"a // b\"; /* x\ny */ if (n > 0x1F) return Foo(1.5);\nclass Bar { }";
    Check("resaltado: C# separa comentarios, cadenas (sin confundirlas con comentarios), números, claves, tipos y funciones",
        Spans(cs, "x.cs", TokenKind.Comment) == "// hola|/* x\ny */" && Spans(cs, "x.cs", TokenKind.String) == "\"a // b\"" && Spans(cs, "x.cs", TokenKind.Number) == "0x1F|1.5"
        && Spans(cs, "x.cs", TokenKind.Control) == "if|return" && Spans(cs, "x.cs", TokenKind.Keyword).Contains("var") && Spans(cs, "x.cs", TokenKind.Function) == "Foo" && Spans(cs, "x.cs", TokenKind.Type) == "Bar");
    const string json = "{ \"clave\": \"valor\", \"n\": -12.5e3, \"ok\": true, \"x\": null }";
    Check("resaltado: JSON distingue la clave del valor",
        Spans(json, "x.json", TokenKind.Property) == "\"clave\"|\"n\"|\"ok\"|\"x\"" && Spans(json, "x.json", TokenKind.String) == "\"valor\"" && Spans(json, "x.json", TokenKind.Number) == "-12.5e3"
        && Spans(json, "x.json", TokenKind.Keyword) == "true|null");
    const string xml = "<!-- c --><Grid Width=\"5\" x:Name='a'>texto \"suelto\"</Grid>";
    Check("resaltado: XML colorea etiquetas, atributos y valores, pero no el texto de en medio",
        Spans(xml, "x.xaml", TokenKind.Tag) == "<Grid|</Grid" && Spans(xml, "x.xaml", TokenKind.Attribute) == "Width|x:Name" && Spans(xml, "x.xaml", TokenKind.String) == "\"5\"|'a'"
        && Spans(xml, "x.xaml", TokenKind.Comment) == "<!-- c -->");
    const string py = "# nota\n@dec\ndef f(x):\n    \"\"\"doc\n    # no es comentario\"\"\"\n    return None if x else 3";
    Check("resaltado: Python con comentarios, decoradores y cadenas de triple comilla que cruzan líneas",
        Spans(py, "x.py", TokenKind.Comment) == "# nota" && Spans(py, "x.py", TokenKind.String).StartsWith("\"\"\"doc") && Spans(py, "x.py", TokenKind.Function) == "@dec|f"
        && Spans(py, "x.py", TokenKind.Keyword).Contains("def") && Spans(py, "x.py", TokenKind.Control).Contains("return"));
    Check("resaltado: YAML, CSS, SQL y scripts también colorean lo básico",
        Spans("clave: valor # nota\nlista:\n  - uno\n", "x.yml", TokenKind.Property).Contains("clave") && Spans("a { color: #fff; margin: 4px } /* c */", "x.css", TokenKind.Number) == "#fff|4px"
        && Spans("SELECT n FROM t -- hola", "x.sql", TokenKind.Control) == "SELECT|FROM" && Spans("echo $HOME # nota", "x.sh", TokenKind.Property) == "$HOME" && Spans("echo $HOME # nota", "x.sh", TokenKind.Comment) == "# nota");
    Check("resaltado: lo que no se reconoce o es enorme se enseña sin colores",
        SyntaxHighlighter.Tokenize("texto", null).Count == 0 && SyntaxHighlighter.Tokenize("", "clike").Count == 0 && SyntaxHighlighter.Tokenize(new string('a', SyntaxHighlighter.MaxLength + 1), "clike").Count == 0);
    // En cualquier archivo real del repo los trozos van ordenados, sin solaparse y dentro del texto, y se calculan rápido.
    var root = AppContext.BaseDirectory;
    while (root is not null && !File.Exists(Path.Combine(root, "VERSION"))) root = Path.GetDirectoryName(root);
    int filesChecked = 0, badFiles = 0, tokensSeen = 0;
    var clock = System.Diagnostics.Stopwatch.StartNew();
    foreach (var sourceFile in Directory.EnumerateFiles(root!, "*.*", SearchOption.AllDirectories)
        .Where(f => !f.Contains(@"\obj\") && !f.Contains(@"\bin\") && !f.Contains(@"\node_modules\") && !f.Contains(@"\.git\") && !f.Contains(@"\_site\") && !f.Contains(@"\dist\") && !f.Contains(@"\build\") && !f.Contains(@"\artifacts\") && !f.Contains(@"\media\"))
        .Take(4000))
    {
        if (SyntaxHighlighter.LanguageOf(sourceFile) is not { } language || new FileInfo(sourceFile).Length > SyntaxHighlighter.MaxLength) continue;
        string text;
        try { text = File.ReadAllText(sourceFile); } catch (IOException) { continue; }
        var tokens = SyntaxHighlighter.Tokenize(text, language);
        filesChecked++;
        tokensSeen += tokens.Count;
        int end = 0;
        bool ok = true;
        foreach (var t in tokens) { if (t.Start < end || t.Length <= 0 || t.End > text.Length) { ok = false; break; } end = t.End; }
        if (!ok) { badFiles++; Console.WriteLine("   mal: " + sourceFile); }
    }
    Check($"resaltado: en {filesChecked} archivos reales del repo ({tokensSeen} trozos) todos van ordenados y sin solaparse", filesChecked > 50 && badFiles == 0);
    Check($"resaltado: y se calculan rápido ({clock.ElapsedMilliseconds} ms en total)", clock.ElapsedMilliseconds < Math.Max(20_000, filesChecked * 250L));
}

Section("Lente de cristal del dock (física del muelle)");
{
    var lens = new LensPhysics { X = 18, TargetX = 18 };
    lens.TargetX = 150;
    double highest = lens.X, fastest = 0;
    for (int i = 0; i < 300; i++)
    {
        lens.Step(1.0 / 60);
        highest = Math.Max(highest, lens.X);
        fastest = Math.Max(fastest, lens.Stretch);
    }
    Check("lente: persigue su destino y se asienta", Math.Abs(lens.X - 150) < 0.1 && lens.Settled);
    Check("lente: rebasa un poco su destino al llegar (rebote de muelle), sin pasarse de largo", highest > 150.5 && highest < 150 + 0.2 * 132);
    Check("lente: se estira con la velocidad y vuelve a su forma al pararse", fastest > 0.5 && fastest <= 1 && lens.Stretch < 0.01);
    var press = new LensPhysics();
    press.TargetGrow = 1;
    for (int i = 0; i < 60; i++) press.Step(1.0 / 60);
    Check("lente: al pulsar crece hasta su tamaño y al soltar vuelve al de reposo", Math.Abs(press.Grow - 1) < 0.01);
    press.TargetGrow = 0;
    for (int i = 0; i < 60; i++) press.Step(1.0 / 60);
    Check("lente: …y encoge otra vez", Math.Abs(press.Grow) < 0.01 && press.Settled);
    var slow = new LensPhysics { X = 0, TargetX = 100 };
    slow.Step(10);   // un fotograma larguísimo (la app estuvo ocupada) no debe hacer explotar el muelle
    Check("lente: un fotograma muy largo no la desestabiliza", double.IsFinite(slow.X) && slow.X > 0 && slow.X < 200);
    var centers = new double[] { 18, 54, 90, 126 };
    Check("lente: elige el botón más cercano al puntero", LensPhysics.Nearest(centers, 10) == 0 && LensPhysics.Nearest(centers, 70) == 1 && LensPhysics.Nearest(centers, 73) == 2 && LensPhysics.Nearest(centers, 500) == 3 && LensPhysics.Nearest(Array.Empty<double>(), 5) == -1);
    Check("lente: sigue al puntero entre los botones y se pasa solo un poco de los extremos", LensPhysics.Follow(centers, 60, 14) == 60 && LensPhysics.Follow(centers, -300, 14) == 4 && LensPhysics.Follow(centers, 999, 14) == 140);
    var inner = new Win32.RECT { Left = 100, Top = 200, Right = 400, Bottom = 234 };
    var over = NotchLayout.WithOvershoot(inner, 1.5);
    Check("lente: la ventana del dock deja margen por arriba y por abajo para que la lente sobresalga", over.Left == 100 && over.Right == 400 && over.Top == 200 - 9 && over.Bottom == 234 + 9);
}

Section("Nombre de la app que suena");
{
    Check("fuente: Spotify desde su identificador de la tienda", MediaSource.Name("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify") == "Spotify");
    Check("fuente: un navegador por su ejecutable", MediaSource.Name("chrome.exe") == "Chrome" && MediaSource.Name("MSEdge") == "Edge" && MediaSource.Name("firefox.exe") == "Firefox");
    Check("fuente: una app web de Chrome (YouTube Music instalado) no enseña su identificador", MediaSource.Name("Chrome._crx_h3nctbl7snswhxn4y4yxm5cewi") == "Chrome");
    Check("fuente: YouTube Music, mejor que YouTube", MediaSource.Name("th-ch.YoutubeMusic") == "YouTube Music" && MediaSource.Name("youtube.exe") == "YouTube");
    Check("fuente: un nombre normal se capitaliza", MediaSource.Name("Foo.Bar!baz") == "Baz" && MediaSource.Name("miapp.exe") == "Miapp");
    Check("fuente: un identificador interno desconocido no se enseña", MediaSource.Name("H3NCTBL7SNSWHXN4Y4YXM5CEWI") == "" && MediaSource.Name("_crx_abcdef") == "" && MediaSource.Name(null) == "" && MediaSource.Name("  ") == "");
}

Section("Agenda del calendario");
{
    var when = new DateTime(2026, 10, 17, 21, 30, 0);
    var item = new AgendaItem("a1", "Dentista | con rayos", when, AgendaKind.Reminder, true);
    var back = Agenda.Parse(Agenda.Format(item));
    Check("agenda: un recordatorio se guarda y se lee igual, aunque el título lleve «|»", back == item);
    Check("agenda: una línea rota o incompleta se descarta", Agenda.Parse(null) is null && Agenda.Parse("x") is null && Agenda.Parse("a|20261017T2130|r0|") is null
        && Agenda.Parse("a|ayer|r0|t") is null && Agenda.Parse("a|20261017T2130|z9|t") is null);
    var items = new[]
    {
        new AgendaItem("1", "Tarde", new DateTime(2026, 10, 17, 18, 0, 0), AgendaKind.Reminder),
        new AgendaItem("2", "Ayer", new DateTime(2026, 10, 16, 9, 0, 0), AgendaKind.Reminder),
        new AgendaItem("3", "Mañana", new DateTime(2026, 10, 18, 8, 0, 0), AgendaKind.Event),
        new AgendaItem("4", "Pronto", new DateTime(2026, 10, 17, 8, 0, 0), AgendaKind.Reminder),
    };
    var days = Agenda.Group(items, new DateOnly(2026, 10, 17), 10);
    Check("agenda: agrupa por días desde hoy, en orden, sin lo de ayer", days.Count == 2 && days[0].Items.Select(i => i.Id).SequenceEqual(["4", "1"]) && days[1].Items[0].Id == "3");
    Check("agenda: el máximo de cosas corta la lista", Agenda.Group(items, new DateOnly(2026, 10, 17), 2).Sum(d => d.Items.Count) == 2);
    Check("agenda: semana ISO del 17 de octubre de 2026 es la 42", Agenda.IsoWeek(new DateOnly(2026, 10, 17)) == 42);

    var from = new DateOnly(2026, 10, 1);
    var to = new DateOnly(2026, 12, 31);
    string Ics(params string[] lines) => "BEGIN:VCALENDAR\r\n" + string.Join("\r\n", lines) + "\r\nEND:VCALENDAR\r\n";
    var one = Agenda.ParseIcs(Ics("BEGIN:VEVENT", "UID:u1", "SUMMARY:Reunión\\, con equipo", "DTSTART:20261020T093000", "END:VEVENT"), from, to);
    Check("ics: un evento con su hora y el texto sin escapes", one.Count == 1 && one[0].Title == "Reunión, con equipo" && one[0].Start == new DateTime(2026, 10, 20, 9, 30, 0) && one[0].Kind == AgendaKind.Event && !one[0].AllDay);
    var linked = Agenda.ParseIcs(Ics("BEGIN:VEVENT", "SUMMARY:Reunión", "DTSTART:20261020T093000", "DTEND:20261020T103000", "URL:https://zoom.us/j/1", "LOCATION:Sala https://teams.microsoft.com/l/meetup-join/2", "DESCRIPTION:Notas", "END:VEVENT"), from, to);
    Check("ics: guarda duración y enlace de reunión de URL, ubicación y notas", linked.Count == 1 && linked[0].End == new DateTime(2026, 10, 20, 10, 30, 0)
        && MeetingLink.Extract(linked[0].Url, linked[0].Location, linked[0].Description)?.Host == "teams.microsoft.com");
    var utc = Agenda.ParseIcs(Ics("BEGIN:VEVENT", "SUMMARY:UTC", "DTSTART:20261020T120000Z", "END:VEVENT"), from, to);
    Check("ics: una hora con Z se pasa a la hora local", utc.Count == 1 && utc[0].Start == TimeZoneInfo.ConvertTimeFromUtc(new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Local));
    var allDay = Agenda.ParseIcs(Ics("BEGIN:VEVENT", "SUMMARY:Cumple", "DTSTART;VALUE=DATE:20261105", "END:VEVENT"), from, to);
    Check("ics: un evento de todo el día", allDay.Count == 1 && allDay[0].AllDay && allDay[0].Start == new DateTime(2026, 11, 5));
    var folded = Agenda.ParseIcs("BEGIN:VEVENT\r\nSUMMARY:Título muy\r\n  largo\r\nDTSTART:20261020T090000\r\nEND:VEVENT\r\n", from, to);
    Check("ics: las líneas largas dobladas se unen", folded.Count == 1 && folded[0].Title == "Título muy largo");
    Check("ics: lo que cae fuera de la ventana no sale, y lo roto se salta",
        Agenda.ParseIcs(Ics("BEGIN:VEVENT", "SUMMARY:Lejos", "DTSTART:20300101T090000", "END:VEVENT", "BEGIN:VEVENT", "SUMMARY:Sin fecha", "END:VEVENT", "BEGIN:VEVENT", "DTSTART:20261020T090000", "END:VEVENT"), from, to).Count == 0);
    var weekly = Agenda.ParseIcs(Ics("BEGIN:VEVENT", "UID:w", "SUMMARY:Clase", "DTSTART:20261005T100000", "RRULE:FREQ=WEEKLY;BYDAY=MO,WE;COUNT=5", "END:VEVENT"), from, to);
    Check("ics: repetición semanal con días y COUNT", weekly.Count == 5 && weekly.Select(i => i.Start.Day).SequenceEqual([5, 7, 12, 14, 19]) && weekly.All(i => i.Start.Hour == 10));
    var yearly = Agenda.ParseIcs(Ics("BEGIN:VEVENT", "UID:y", "SUMMARY:Aniversario", "DTSTART;VALUE=DATE:20200315", "RRULE:FREQ=YEARLY", "END:VEVENT"), new DateOnly(2027, 1, 1), new DateOnly(2028, 12, 31));
    Check("ics: repetición anual dentro de la ventana", yearly.Select(i => i.Start).SequenceEqual([new DateTime(2027, 3, 15), new DateTime(2028, 3, 15)]));
    var daily = Agenda.ParseIcs(Ics("BEGIN:VEVENT", "UID:d", "SUMMARY:Pastilla", "DTSTART:20261001T080000", "RRULE:FREQ=DAILY;INTERVAL=2;UNTIL=20261009T000000", "END:VEVENT"), from, to);
    Check("ics: repetición cada dos días con UNTIL", daily.Select(i => i.Start.Day).SequenceEqual([1, 3, 5, 7, 9]));
    Check("ics: un archivo vacío o que no es un calendario da una lista vacía", Agenda.ParseIcs("", from, to).Count == 0 && Agenda.ParseIcs("hola\nmundo", from, to).Count == 0);
}

Section("Color de la portada");
{
    byte[] Pixels(params (byte B, byte G, byte R)[] colors) => colors.SelectMany(c => new byte[] { c.B, c.G, c.R, 255 }).ToArray();
    var (dr, dg, db) = ArtColor.Dominant(Pixels((0, 0, 220), (0, 0, 220), (10, 10, 10), (10, 10, 10)));
    Check("portada: el rojo vivo pesa más que el negro apagado", dr > 150 && dg < 60 && db < 60);
    Check("portada: sin píxeles, gris medio", ArtColor.Dominant([]) == ((byte)128, (byte)128, (byte)128));
    var vivid = ArtColor.Vivid((140, 30, 30));
    Check("portada: el rojo oscuro se aclara y sigue siendo rojo", vivid.R > 200 && vivid.R > vivid.G + 60 && vivid.R > vivid.B + 60);
    Check("portada: un azul pálido gana color sin cambiar de tono", ArtColor.Vivid((150, 170, 200)) is var v && v.B > v.R + 40);
    Check("portada: una portada gris da un blanco suave, no un color inventado", ArtColor.Vivid((120, 120, 120)) == ((byte)0xE6, (byte)0xE6, (byte)0xEB));
}

Section("Vidrio del notch (desenfoque del fondo)");
{
    byte[] Solid(int w, int h, byte b, byte g, byte r) { var px = new byte[w * h * 4]; for (int i = 0; i < w * h; i++) { px[i * 4] = b; px[i * 4 + 1] = g; px[i * 4 + 2] = r; px[i * 4 + 3] = 255; } return px; }
    var flat = BackdropBlur.Reduce(Solid(40, 20, 30, 60, 90), 40, 20, 10, out int fw, out int fh, 1);
    Check("vidrio: una imagen lisa sigue lisa y se reduce por celdas", fw == 4 && fh == 2 && flat.Length == 4 * 2 * 4 && flat[0] == 30 && flat[1] == 60 && flat[2] == 90 && flat[3] == 255);
    var half = Solid(20, 10, 0, 0, 0);
    for (int y = 0; y < 10; y++) for (int x = 10; x < 20; x++) { int i = (y * 20 + x) * 4; half[i] = half[i + 1] = half[i + 2] = 200; }
    var split = BackdropBlur.Reduce(half, 20, 10, 5, out int sw, out _, 1);
    Check("vidrio: cada celda promedia sus píxeles (negro a un lado, claro al otro)", sw == 4 && split[0] == 0 && split[(3) * 4] == 200);
    var odd = BackdropBlur.Reduce(Solid(23, 11, 50, 50, 50), 23, 11, 10, out int ow, out int oh, 1);
    Check("vidrio: un tamaño que no es múltiplo de la celda no deja bordes negros", ow == 3 && oh == 2 && odd[(2 * 4 / 4 * 0) + 0] == 50 && odd[(1 * 3 + 2) * 4] == 50);
    var vivid = BackdropBlur.Reduce(Solid(10, 10, 40, 80, 160), 10, 10, 10, out _, out _, 1.5);
    Check("vidrio: realza el color sin tocar el gris", vivid[2] > 160 && vivid[0] < 40 && BackdropBlur.Reduce(Solid(10, 10, 90, 90, 90), 10, 10, 10, out _, out _, 1.5)[1] == 90);
    Check("vidrio: aclarar acerca el color al blanco sin pasarse", BackdropBlur.Reduce(Solid(10, 10, 100, 100, 100), 10, 10, 10, out _, out _, 1, 0.5)[0] == 178 && BackdropBlur.Reduce(Solid(10, 10, 255, 255, 255), 10, 10, 10, out _, out _, 1, 0.5)[0] == 255);
    Check("vidrio: la claridad va de 0 (negro) a 1 (blanco)", BackdropBlur.Luminance(Solid(4, 4, 0, 0, 0), 4, 4) == 0 && Math.Abs(BackdropBlur.Luminance(Solid(4, 4, 255, 255, 255), 4, 4) - 1) < 1e-9 && BackdropBlur.Luminance([], 0, 0) == 0);
    bool threw = false;
    try { BackdropBlur.Reduce(new byte[10], 4, 4, 2, out _, out _); } catch (ArgumentException) { threw = true; }
    Check("vidrio: una imagen más corta que su tamaño se rechaza", threw);
}

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

// MARK: apagado aislado por backend
Section("Apagado aislado");
// Tarea A (envoltorio 100 → servidor 101), tarea B (200 → 201) y un servidor manual (400, padre 50).
var parents = new Dictionary<int, int> { [100] = 4, [101] = 100, [200] = 4, [201] = 200, [400] = 50, [301] = 999 };
int? ParentOf(int pid) => parents.TryGetValue(pid, out var up) ? up : null;
var servers = new[] { 101, 201, 400 };
var (ownedA, foreignA) = Procs.SplitOwned(servers, new HashSet<int> { 100 }, ParentOf);
Check("apagar A: solo su servidor es suyo; B y el manual siguen",
    ownedA.SequenceEqual([101]) && foreignA.SequenceEqual([201, 400]));
var (ownedNone, foreignNone) = Procs.SplitOwned(servers, new HashSet<int>(), ParentOf);
Check("con A ya detenida no se toca ninguno", ownedNone.Count == 0 && foreignNone.Count == 3);
Check("un servidor huérfano (padre desconocido) no se atribuye a nadie",
    !Procs.Descends(301, new HashSet<int> { 100, 200 }, ParentOf));
parents[1] = 2; parents[2] = 1;
Check("un ciclo de PIDs reutilizados termina sin colgarse", !Procs.Descends(1, new HashSet<int> { 100 }, ParentOf));
Check("el propio servidor como raíz cuenta como suyo (binario arrancado por la app)",
    Procs.Descends(400, new HashSet<int> { 400 }, ParentOf));
var self = Procs.IdentityOf(Environment.ProcessId);
Check("la identidad de un proceso vivo incluye su hora de inicio y no coincide con otro PID reutilizado",
    self is { Started: > 0 } me && Procs.Alive(me) && !Procs.Alive(me with { Started = me.Started + 1 }));

// MARK: VRAM por adaptador
Section("VRAM por adaptador");
const long GB = 1L << 30;
var gpuRtx = new GpuAdapter("NVIDIA GeForce RTX 4060 Laptop GPU", 8 * GB, 0x16062, 0);
var gpuArc = new GpuAdapter("Intel(R) Arc(TM) A770", 16 * GB, 0x2a001, 0);
var gpuCounters = new List<(string Name, long Value)> { (gpuRtx.Luid + "_phys_0", 3 * GB), (gpuArc.Luid + "_phys_0", 5 * GB) };
var gpuProcess = new List<(string Name, long Value)>
{
    ($"pid_10_{gpuRtx.Luid}_phys_0", 2 * GB), ($"pid_11_{gpuRtx.Luid}_phys_0", GB / 2), ($"pid_10_{gpuArc.Luid}_phys_0", GB),
    ($"pid_99_{gpuRtx.Luid}_phys_0", GB / 4),
};
var gpuNone = Gpu.Summarize([], gpuCounters, gpuProcess, new HashSet<int> { 10 });
Check("cero adaptadores: sin filas", gpuNone.Count == 0);
var gpuOne = Gpu.Summarize([gpuRtx], gpuCounters, gpuProcess, new HashSet<int> { 10, 11 });
Check("un adaptador: total, uso y parte de los modelos", gpuOne.Count == 1 && gpuOne[0].Used == 3 * GB && gpuOne[0].Model == 2 * GB + GB / 2);
var two = Gpu.Summarize([gpuRtx, gpuArc], gpuCounters, gpuProcess, new HashSet<int> { 10 });
Check("dos adaptadores: cada uno con su LUID, sin mezclar procesos",
    two[0].Used == 3 * GB && two[0].Model == 2 * GB && two[1].Used == 5 * GB && two[1].Model == GB);
var noCounter = Gpu.Summarize([gpuRtx], null, null, new HashSet<int> { 10 });
Check("contadores ausentes: no disponible, no 0", noCounter[0].Used is null && noCounter[0].Model is null);
var noProcess = Gpu.Summarize([gpuRtx], gpuCounters, null, new HashSet<int> { 10 });
Check("sin contador por proceso: el uso se muestra y el modelo no", noProcess[0].Used == 3 * GB && noProcess[0].Model is null);
var missingOne = Gpu.Summarize([gpuRtx, gpuArc], [(gpuRtx.Luid + "_phys_0", GB)], gpuProcess, new HashSet<int> { 10 });
Check("un adaptador sin instancia en el contador queda no disponible", missingOne[0].Used == GB && missingOne[1].Used is null && missingOne[1].Model is null);
var noRunners = Gpu.Summarize([gpuRtx], gpuCounters, gpuProcess, new HashSet<int>());
Check("sin runners cargados: modelo 0 (medido), no desconocido", noRunners[0].Model == 0);
var capped = Gpu.Summarize([gpuRtx], [(gpuRtx.Luid + "_phys_0", 20 * GB)], gpuProcess, new HashSet<int> { 10 });
Check("un contador mayor que la VRAM se limita al total", capped[0].Used == 8 * GB);
Check("sin controladores, Adapters no falla", Gpu.Adapters is not null);

// MARK: actualizaciones
Section("Actualizaciones");
await UpdateTests.Run(Check);

Console.WriteLine();
Console.WriteLine(failures == 0 ? "todo bien" : $"{failures} fallos");
return failures == 0 ? 0 : 1;
