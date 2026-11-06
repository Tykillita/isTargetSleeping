using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// Estados de ejemplo del widget Equipo para las capturas (arnés fuera de pantalla y material gráfico). No lanza agentes, no toca el
/// disco y solo funciona en modo demostración.
public sealed partial class TeamWidget
{
    /// Escenas: idle, plan, running, pending, done, concern, failed, history, schedules, editor.
    public void LoadDemo(string scene)
    {
        if (!demo) return;
        bool en = Effective == "en";
        string redesign = en ? "Redesign the settings screen in Settings.xaml: it must have a clear hierarchy, hover states and visible focus."
            : "Rediseña la pantalla de ajustes en Settings.xaml: debe tener jerarquía clara, estados al pasar el ratón y foco visible.";
        string title = redesign.Length > 80 ? redesign[..80] + "…" : redesign;
        var demoProfile = TeamRouter.Profile(redesign);
        var steps = TeamRouter.Route(demoProfile, TeamPreset.Auto).Steps.Select(s => CodexModels.Apply(s, Models)).ToList();
        int n = steps.Count;
        demoRun = null; run = null; entry = null; pendingFor = null; editor = null; tab = TeamTab.Task; prompt = ""; live = []; recorded = [];

        TeamStepRow[] Rows(Func<int, (StepState State, string? Message, long Tokens, double Seconds)> state)
        {
            var rows = steps.Select((s, i) => new TeamStepRow(s, i, s.Agent, _ => { }, _ => { }, Resolve) { ModelOverride = s.Model }).ToArray();
            foreach (var r in rows)
            {
                var (st, message, tokens, seconds) = state(r.Index);
                r.Total = n;
                r.Update(st, r.Agent, tokens, seconds, st == StepState.Running ? DateTime.Now.AddSeconds(-seconds) : null, message);
            }
            return rows;
        }

        string AnswerReport() => en
            ? "An **otorhinolaryngologist** is a doctor who specializes in the ear, nose and throat.\n\nThe word combines:\n- **Oto-**: ear\n- **Rhino-**: nose\n- **Laryngo-**: throat\n\nThey treat things like hearing loss, sinusitis and voice problems. Often shortened to *ENT*."
            : "Un **otorrinolaringólogo** es el médico especialista en el oído, la nariz y la garganta.\n\nLa palabra combina:\n- **Oto-**: oído\n- **Rino-**: nariz\n- **Laringo-**: garganta\n\nTrata problemas como la pérdida de audición, la sinusitis y los trastornos de la voz. Se abrevia a veces como *ORL*.";
        TeamHistoryEntry Entry(string id, string outcome, string verdict, int files, int added, int removed, string state, DateTime when, string? schedule = null, string? name = null) => new()
        {
            Id = id, BranchId = id, Root = "C:\\proyectos\\isTargetSleeping", TreeDir = "C:\\datos\\" + id, Title = name ?? title, Preset = "Auto", Verdict = verdict,
            Started = when, Seconds = 412, Outcome = outcome, Tokens = 58_320, CostUsd = 0.08, Files = files, Added = added, Removed = removed, State = state, ScheduleId = schedule,
            Report = en ? "I redesigned the settings screen: sections with a clear hierarchy, hover and focus states, and the same look in light and dark. The tests pass."
                : "Rediseñé la pantalla de ajustes: secciones con jerarquía clara, estados al pasar el ratón y de foco, y el mismo aspecto en claro y oscuro. Las pruebas pasan.",
            Steps = steps.Select(s => new TeamStepSummary(s.Agent.ToString(), s.Role.ToString(), s.Model, true, 40 + (int)s.Role * 25, 6_000 + (int)s.Role * 3_100)).ToList(),
        };

        switch (scene)
        {
            case "plan":
                Prompt = redesign;
                break;
            case "running":
            case "pending":
                int active = scene == "pending" ? 1 : Math.Min(3, n - 1);
                var msgs = new[] { en ? "Settings: 3 groups, hierarchy by weight, focus ring on every control." : "Ajustes: 3 grupos, jerarquía por peso y aro de foco en cada control.",
                    en ? "Spec: 12 px grid, three states per control, tokens from the theme." : "Especificación: rejilla de 12 px, tres estados por control y tokens del tema.",
                    en ? "Writing Settings.xaml: sections and styles…" : "Escribiendo Settings.xaml: secciones y estilos…", null };
                live = Rows(i => i < active ? (StepState.Done, msgs[Math.Min(i, 2)], 5_800 + i * 2_400, 38 + i * 21)
                    : i == active ? (scene == "pending" ? (StepState.Done, msgs[1], 7_200, 44) : (StepState.Running, null, 0, 26)) : (StepState.Pending, null, 0, 0)).ToList();
                demoRun = new RunView(steps, live.Select(r => r.State).ToArray(), live.Select(r => r.Agent).ToArray(), live.Select(r => r.Message).ToArray(),
                    live.Select(r => r.Tokens).ToArray(), live.Select(r => r.Seconds).ToArray(), DateTime.Now.AddSeconds(-148), 0.04, title, true);
                run = demoRun;
                if (scene == "running")
                    demoLog =
                    [
                        new(en ? "Sonnet · Design" : "Sonnet · Diseñar", en ? "52 s ago" : "hace 52 s", false),
                        new(en ? "Reading App.xaml" : "Leyendo App.xaml", en ? "31 s ago" : "hace 31 s", false),
                        new(en ? "Searching “WeatherWidget”" : "Buscando «WeatherWidget»", en ? "14 s ago" : "hace 14 s", false),
                        new(en ? "Reading OptionalWidgets.cs" : "Leyendo OptionalWidgets.cs", en ? "now" : "ahora", true),
                    ];
                if (scene == "pending")
                {
                    pendingFor = en ? "Settings: 3 groups, hierarchy by weight." : "Ajustes: 3 grupos y jerarquía por peso.";
                    planDraft = en
                        ? "**Goal**: redesign the settings screen with a clear hierarchy.\n\n**Files to touch**\n- `Settings.xaml`: three groups (General, Notch, Advanced) with a header each.\n- `Settings.cs`: expose the group state to the view.\n\n**Steps**\n1. Split the list into groups by weight.\n2. Add hover and focus states to every control.\n3. Check light and dark themes.\n\n**Done when**: the tests pass and both themes look the same."
                        : "**Objetivo**: rediseñar la pantalla de ajustes con una jerarquía clara.\n\n**Archivos a tocar**\n- `Settings.xaml`: tres grupos (General, Notch, Avanzado) con su cabecera.\n- `Settings.cs`: exponer el estado de los grupos a la vista.\n\n**Pasos**\n1. Dividir la lista en grupos por peso.\n2. Añadir estados de hover y foco a cada control.\n3. Comprobar los temas claro y oscuro.\n\n**Criterio de hecho**: las pruebas pasan y los dos temas se ven igual.";
                }
                break;
            case "done":
            case "concern":
            case "failed":
                entry = scene == "done" ? Entry("261019-140512", "Done", "Approved", 4, 212, 37, "Open", DateTime.Now.AddMinutes(-6))
                    : scene == "concern" ? Entry("261019-140512", "NeedsUser", "Warning", 4, 212, 37, "Open", DateTime.Now.AddMinutes(-6))
                    : Entry("261019-140512", "Failed", "", 0, 0, 0, "Discarded", DateTime.Now.AddMinutes(-6)) with { Report = en ? "Codex stopped: the sandbox could not start." : "Codex se detuvo: el entorno aislado no arrancó." };
                if (scene == "concern") entry = entry with { Report = en ? "Review blocked after 3 rounds: the hover state still hides the label." : "Revisión sin aprobar tras 3 rondas: el estado al pasar el ratón aún tapa la etiqueta." };
                if (scene == "failed") { status = ""; }
                files = [new("src/Settings.xaml", 164, 29), new("src/Settings.cs", 31, 6), new("tests/SettingsTests.cs", 14, 2), new("Strings.en.json", 3, 0)];
                diffText = "";
                history = [entry];
                recorded = Rows(i => (scene == "failed" && i == 3 ? StepState.Failed : StepState.Done, null, 6_000 + i * 3_100, 40 + i * 25)).ToList();
                break;
            case "history":
                history =
                [
                    Entry("261019-140512", "Done", "Approved", 4, 212, 37, "Open", DateTime.Now.AddMinutes(-6)),
                    Entry("261019-101003", "NeedsUser", "Warning", 2, 40, 12, "Open", DateTime.Now.AddHours(-4), name: en ? "Fix the crash when closing in Prefs.cs" : "Arregla el cierre inesperado en Prefs.cs"),
                    Entry("261018-090001", "Done", "Approved", 1, 18, 4, "Merged", DateTime.Now.AddDays(-1), "ab12cd34", en ? "Daily report: summarize yesterday's commits" : "Informe diario: resume los commits de ayer"),
                    Entry("261017-221544", "Failed", "", 0, 0, 0, "Discarded", DateTime.Now.AddDays(-2), name: en ? "Polish the Save button" : "Pule el botón Guardar"),
                ];
                entry = history[0];
                recorded = Rows(i => (StepState.Done, null, 6_000 + i * 3_100, 40 + i * 25)).ToList();
                tab = TeamTab.History;
                break;
            case "answer":
                // Una pregunta sencilla ya respondida: la respuesta con su formato, en el historial.
                var answered = Entry("261019-090001", "Done", "", 0, 0, 0, "Answered", DateTime.Now.AddMinutes(-3), name: en ? "What does otorhinolaryngologist mean?" : "¿Qué significa otorrinolaringólogo?")
                    with { Report = AnswerReport() };
                history = [answered];
                entry = answered;
                recorded = Rows(i => (StepState.Done, null, 6_000 + i * 3_100, 40 + i * 25)).ToList();
                tab = TeamTab.Task;
                break;
            case "board":
                // Una tarea en marcha, un plan por aprobar y varias ya hechas: las tres columnas con contenido.
                live = Rows(i => i < 2 ? (StepState.Done, null, 6_000, 40) : i == 2 ? (StepState.Running, null, 0, 30) : (StepState.Pending, null, 0, 0)).ToList();
                demoRun = new RunView(steps, live.Select(r => r.State).ToArray(), live.Select(r => r.Agent).ToArray(), live.Select(r => r.Message).ToArray(),
                    live.Select(r => r.Tokens).ToArray(), live.Select(r => r.Seconds).ToArray(), DateTime.Now.AddSeconds(-95), 0.03, title, true);
                run = demoRun;
                pendingFor = en ? "Settings: 3 groups, hierarchy by weight." : "Ajustes: 3 grupos y jerarquía por peso.";
                planDraft = pendingFor + (en ? "\nFiles: Settings.xaml, Settings.cs.\nDone when: it looks the same in light and dark." : "\nArchivos: Settings.xaml y Settings.cs.\nHecho cuando: se vea igual en claro y oscuro.");
                history =
                [
                    Entry("261019-140512", "Done", "Approved", 4, 212, 37, "Open", DateTime.Now.AddMinutes(-6)),
                    Entry("261019-101003", "NeedsUser", "Warning", 2, 40, 12, "Open", DateTime.Now.AddHours(-4), name: en ? "Fix the crash when closing in Prefs.cs" : "Arregla el cierre inesperado en Prefs.cs"),
                    Entry("261019-090001", "Done", "", 0, 0, 0, "Answered", DateTime.Now.AddHours(-5), name: en ? "What does otorhinolaryngologist mean?" : "¿Qué significa otorrinolaringólogo?")
                        with { Report = AnswerReport() },
                    Entry("261018-090001", "Done", "Approved", 1, 18, 4, "Merged", DateTime.Now.AddDays(-1), "ab12cd34", en ? "Daily report: summarize yesterday's commits" : "Informe diario: resume los commits de ayer"),
                    Entry("261017-221544", "Failed", "", 0, 0, 0, "Discarded", DateTime.Now.AddDays(-2), name: en ? "Polish the Save button" : "Pule el botón Guardar"),
                ];
                demoSessions =
                [
                    new AgentSnapshot("claude-demo-1", "C:\\proyectos\\api", "Claude", AgentState.Working, "Edit", "src/auth.ts", 24, 6, en ? "Moving token refresh into its own module." : "Moviendo la renovación de tokens a su propio módulo.",
                        [new AgentActivity("Read", "src/auth.ts", null, true), new AgentActivity("Edit", "src/auth.ts", null)], DateTime.UtcNow.AddSeconds(-20)) { Title = en ? "Refactor the auth module" : "Refactorizar el módulo de autenticación" },
                    new AgentSnapshot("rollout-demo-2", "C:\\proyectos\\web", "Codex", AgentState.NeedsAttention, "exec_command", null, null, null, en ? "Waiting for your OK to run the migration." : "Espera tu visto bueno para ejecutar la migración.",
                        [new AgentActivity("exec_command", null, null, true)], DateTime.UtcNow.AddMinutes(-3)) { Title = en ? "Migrate the users table" : "Migrar la tabla de usuarios" },
                    new AgentSnapshot("claude-demo-3", "C:\\proyectos\\web", "Claude", AgentState.Finished, null, null, 12, 3, en ? "Done: the footer links are fixed." : "Listo: los enlaces del pie están arreglados.",
                        [new AgentActivity(null, null, en ? "Footer links fixed" : "Enlaces del pie arreglados", true)], DateTime.UtcNow.AddHours(-2)) { Title = en ? "Fix the footer links" : "Arreglar los enlaces del pie" },
                ];
                expandedCards.Clear();
                foreach (var key in new[] { "plan", "261019-101003", "261017-221544", "261018-090001" }) expandedCards.Add(key);
                tab = TeamTab.Board;
                break;
            case "schedules":
            case "editor":
                schedules =
                [
                    new TeamSchedule { Id = "a", Title = en ? "Daily report" : "Informe diario", Prompt = "x", Root = "C:\\proyectos\\isTargetSleeping", Kind = ScheduleKind.Weekdays, Time = "08:30",
                        LastRun = DateTime.Now.AddDays(-1).Date.AddHours(8.5), LastOutcome = "Done", Created = DateTime.Now.AddDays(-5) },
                    new TeamSchedule { Id = "b", Title = en ? "Weekly dependency check" : "Revisión semanal de dependencias", Prompt = "x", Root = "C:\\proyectos\\web", Kind = ScheduleKind.Weekly, DaysMask = 0b0000001,
                        Time = "09:00", LastOutcome = "Missed", LastRun = DateTime.Now.AddDays(-7), Created = DateTime.Now.AddDays(-20) },
                    new TeamSchedule { Id = "c", Title = en ? "Tests every 6 hours" : "Pruebas cada 6 horas", Prompt = "x", Root = "C:\\proyectos\\isTargetSleeping", Kind = ScheduleKind.EveryHours, EveryHours = 6, Enabled = false, Created = DateTime.Now.AddDays(-2) },
                ];
                schedulesEnabled = true;
                tab = TeamTab.Schedules;
                if (scene == "editor")
                    editor = new TeamScheduleEditor
                    {
                        Title = en ? "Daily report" : "Informe diario", Root = "C:\\proyectos\\isTargetSleeping", Kind = 2, Time = "08:30",
                        Prompt = en ? "Summarize yesterday's commits in informe.md: it must list what changed and why." : "Resume los commits de ayer en informe.md: debe listar qué cambió y por qué.",
                    };
                break;
        }
        Raise();
    }
}
