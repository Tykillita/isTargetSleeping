using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IsTargetSleeping;
using IsTargetSleeping.UI;

/// La tarjeta de permiso de un agente dentro del notch real: se abre sola al llegar la petición y se cierra al responder.
/// Con ITS_CAPTURE_UI=carpeta además guarda capturas (ES/EN, normal y alto contraste, 100/125/150 %). Solo con ITS_ONLY_APPROVAL=1.
internal static class ApprovalNotchTests
{
    private static HookRequest Request(string command) => AgentHooks.Parse(new JsonObject
    {
        ["agent"] = "claude",
        ["payload"] = new JsonObject
        {
            ["hook_event_name"] = AgentHooks.PermissionEvent, ["session_id"] = "demo", ["cwd"] = @"C:\dev\korus", ["tool_name"] = "Bash",
            ["tool_input"] = new JsonObject { ["command"] = command },
        },
    }.ToJsonString())!;

    public static void Run(Action<string, bool> check)
    {
        if (SkipWithoutSecondary(check, out var screen)) return;
        var captureDir = Environment.GetEnvironmentVariable("ITS_CAPTURE_UI");
        if (captureDir is { Length: > 0 }) Directory.CreateDirectory(captureDir);
        var originalContrast = Theme.ForceHighContrast;
        var originalLanguage = L10n.Effective;
        foreach (var (language, locale) in new[] { (Language.Es, "es"), (Language.En, "en") })
            foreach (bool highContrast in new[] { false, true })
            {
                L10n.Init(language);
                Theme.ForceHighContrast = highContrast;
                Theme.Apply();
                var controller = new OllamaController();
                var prefs = new Prefs();
                controller.LoadDemo(); prefs.LoadDemo();
                prefs.SetList(PrefKeys.NotchActivities, ["notice"]);
                var supervisor = new Supervisor(controller, prefs);
                supervisor.LoadDemo();
                var model = new PanelViewModel(supervisor);
                var lazyWidgets = (Dictionary<string, Lazy<WidgetModel>>)typeof(WidgetHub).GetField("all", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(model.Hub)!;
                var eagerOptional = new[] { "team", "weather", "lmstudio", "launcher", "teleprompter" }.Where(id => lazyWidgets[id].IsValueCreated).ToArray();
                check($"[{locale}] los widgets opcionales se difieren hasta necesitarlos{(eagerOptional.Length == 0 ? "" : $": {string.Join(", ", eagerOptional)}")}", eagerOptional.Length == 0);
                model.RaiseNotice("Aviso de prueba", 30);
                ContentView content = null!;
                int contentCreations = 0;
                var notch = new NotchWindow(model, () => { contentCreations++; return content = new ContentView(model); }, screen);
                var tag = $"{locale}-{(highContrast ? "hc" : "normal")}";
                try
                {
                    notch.Start();
                    model.Show(PanelView.Main);
                    var state = (NotchState)typeof(NotchWindow).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notch)!;
                    var mediaStarted = typeof(MediaWidget).GetField("started", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var holder = (System.Windows.Controls.Panel)typeof(NotchWindow).GetField("holder", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notch)!;
                    var peekField = typeof(NotchWindow).GetField("peek", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var shell = (FrameworkElement)typeof(NotchWindow).GetField("shell", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notch)!;
                    var pill = (FrameworkElement)typeof(NotchWindow).GetField("pill", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notch)!;
                    var gate = (VisibilityGate)typeof(NotchWindow).GetField("gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notch)!;
                    var apply = typeof(NotchWindow).GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    gate.Update(HideReason.None, 0);
                    gate.Update(HideReason.None, 1);
                    state.Update(inside: false, suppressed: false, keepOpen: false, now: 0);
                    apply.Invoke(notch, null);
                    check($"[{tag}] el notch empieza cerrado", state.Mode == NotchMode.Collapsed);
                    check($"[{tag}] el arranque colapsado descarga ambas vistas", gate.Shown && !state.Hidden
                        && contentCreations == 0 && peekField.GetValue(notch) is null);
                    check($"[{tag}] el árbol visual de la vista corta se difiere hasta el hover", peekField.GetValue(notch) is null);
                    check($"[{tag}] Música no inicializa servicios mientras el notch sigue cerrado", !(bool)mediaStarted.GetValue(model.Hub.Media)!);
                    check($"[{tag}] la pastilla nueva es neutral y no enseña el modelo de Ollama", model.PillIds.SequenceEqual(["clock"]));
                    Pump(TimeSpan.FromMilliseconds(300));
                    notch.UpdateLayout();
                    check($"[{tag}] el indicador cerrado sigue visible (pastilla {pill.Opacity:0.00}, notch {shell.Opacity:0.00})", model.LiveActivities.Any(a => a.Id == "notice")
                        && pill.IsVisible && pill.Opacity > 0 && pill.ActualWidth > 0 && shell.Opacity > 0);
                    check($"[{tag}] las instalaciones nuevas activan Música, Tareas, Pomodoro, Estante y Sistema",
                        model.PeekIds.SequenceEqual(WidgetCatalog.DefaultPeek) && model.PeekDots.Count == 5);
                    if (captureDir is { Length: > 0 })
                    {
                        notch.UpdateLayout();
                        var frame = shell;
                        foreach (double dpi in new[] { 96d, 120d, 144d })
                            Save(frame, dpi, Path.Combine(captureDir, $"notch-cerrado-{tag}-{dpi:0}.png"));
                    }
                    state.Expand();
                    apply.Invoke(notch, null);
                    bool previewReady = PumpUntil(() => state.Mode == NotchMode.Expanded && peekField.GetValue(notch) is NotchPeek active
                        && active.IsVisible && active.ActualWidth > 0 && active.ActualHeight > 0,
                        TimeSpan.FromSeconds(3));
                    var peekView = peekField.GetValue(notch) as NotchPeek;
                    if (previewReady) Pump(TimeSpan.FromMilliseconds(300));
                    else Console.WriteLine($"[{tag}] peek diagnóstico: modo={state.Mode}, gate={gate.Shown}, notch={notch.IsVisible}/{notch.ActualWidth:0}x{notch.ActualHeight:0}, creado={peekView is not null}, vis={peekView?.Visibility}/{peekView?.IsVisible}, actual={peekView?.ActualWidth:0}x{peekView?.ActualHeight:0}, deseado={peekView?.DesiredSize.Width:0}x{peekView?.DesiredSize.Height:0}");
                    if (peekView is null) { check($"[{tag}] la vista corta se crea al abrir", false); continue; }
                    bool peekConnected = PresentationSource.FromVisual(peekView) is not null;
                    check($"[{tag}] la vista corta queda conectada a la ventana", peekConnected);
                    if (!peekConnected) continue;
                    FrameworkElement peek = peekView;
                    var footer = (FrameworkElement)typeof(NotchPeek).GetField("peekFooter", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(peekView)!;
                    var pager = (FrameworkElement)typeof(NotchPeek).GetField("pager", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(peekView)!;
                    var dockRow = (FrameworkElement)typeof(NotchPeek).GetField("dockRow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(peekView)!;
                    notch.UpdateLayout();
                    check($"[{tag}] la vista corta abre sin reactivar la vista completa", state.Mode == NotchMode.Expanded
                        && previewReady && contentCreations == 0 && holder.Children.Contains(peek));
                    check($"[{tag}] Música se inicializa al mostrarse su página", (bool)mediaStarted.GetValue(model.Hub.Media)!);
                    var media = model.Hub.Media;
                    bool musicHasContent = PumpUntil(() => media.HasMediaCard || media.HasCachedTrack, TimeSpan.FromSeconds(3));
                    check($"[{tag}] Música muestra su canción guardada o la pista de prueba, nunca una página vacía",
                        musicHasContent && media.MediaTitle.Length > 0);
                    check($"[{tag}] las flechas y los puntos del pie se pintan con varias páginas",
                        model.ShowPageArrows && footer.IsVisible && pager.IsVisible && pager.ActualWidth > 0 && pager.ActualHeight > 0);
                    if (captureDir is { Length: > 0 })
                    {
                        var frame = (FrameworkElement)notch.Content;
                        foreach (double dpi in new[] { 96d, 120d, 144d })
                            Save(frame, dpi, Path.Combine(captureDir, $"notch-vista-corta-{tag}-{dpi:0}.png"));
                    }
                    model.StepPeekPage(1, wrap: true);
                    Pump(TimeSpan.FromMilliseconds(500));
                    notch.UpdateLayout();
                    check($"[{tag}] la flecha avanza a Tareas", model.PeekDots.Count == 5 && model.PeekDots[1].IsSelected);
                    var cursor = footer.PointToScreen(new Point(footer.ActualWidth / 2, footer.ActualHeight / 2));
                    peekView.TrackCursor((int)cursor.X, (int)cursor.Y);
                    bool DockFits()
                    {
                        notch.UpdateLayout();
                        if (!dockRow.IsVisible || dockRow.ActualWidth <= 0 || dockRow.ActualHeight <= 0) return false;
                        var row = dockRow.TranslatePoint(new Point(0, 0), peek);
                        return row.Y >= 0 && row.Y + dockRow.ActualHeight <= peek.ActualHeight + 1;
                    }
                    bool dockReady = PumpUntil(DockFits,
                        TimeSpan.FromSeconds(2));
                    if (dockReady) Pump(TimeSpan.FromMilliseconds(220));
                    notch.UpdateLayout();
                    if (!dockReady) Console.WriteLine($"[{tag}] dock no cabe: fila={dockRow.ActualWidth:0}x{dockRow.ActualHeight:0} y={dockRow.TranslatePoint(new Point(0, 0), peek).Y:0}, pie={footer.ActualHeight:0}, peek={peek.ActualWidth:0}x{peek.ActualHeight:0} deseado={peek.DesiredSize.Width:0}x{peek.DesiredSize.Height:0}, ventana={notch.ActualWidth:0}x{notch.ActualHeight:0}");
                    check($"[{tag}] al pasar por los puntos aparece el control para abrir el notch completo",
                        dockReady && model.OpenFullFromPeek is not null && footer.ActualHeight > 14 && dockRow.Opacity > 0.9);
                    if (captureDir is { Length: > 0 })
                    {
                        var frame = (FrameworkElement)notch.Content;
                        foreach (double dpi in new[] { 96d, 120d, 144d })
                            Save(frame, dpi, Path.Combine(captureDir, $"notch-vista-corta-dock-{tag}-{dpi:0}.png"));
                    }
                    var placedField = typeof(NotchWindow).GetField("placed", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var windowBeforePin = (Win32.RECT)placedField.GetValue(notch)!;
                    var centerFrames = new List<double>();
                    EventHandler recordCenter = (_, _) =>
                    {
                        if (state.Mode == NotchMode.Pinned && shell.ActualWidth > 0)
                            centerFrames.Add(shell.PointToScreen(new Point(shell.ActualWidth / 2, shell.ActualHeight / 2)).X);
                    };
                    CompositionTarget.Rendering += recordCenter;
                    model.OpenFullFromPeek!.Execute(null);
                    var windowAfterPin = (Win32.RECT)placedField.GetValue(notch)!;
                    Console.WriteLine($"[{tag}] apertura desde el mini dock: ventana X {windowBeforePin.Left}..{windowBeforePin.Right} -> {windowAfterPin.Left}..{windowAfterPin.Right}");
                    check($"[{tag}] fijar desde el mini dock conserva la huella horizontal",
                        windowBeforePin.Left == windowAfterPin.Left && windowBeforePin.Right == windowAfterPin.Right);
                    bool fullFromPeekOpened = PumpUntil(() => state.Mode == NotchMode.Pinned && holder.Children.Contains(content)
                        && !holder.Children.Contains(peek) && peekField.GetValue(notch) is null, TimeSpan.FromSeconds(3));
                    check($"[{tag}] el control del dock crea la vista completa al abrirla", fullFromPeekOpened && contentCreations == 1 && ReferenceEquals(content.DataContext, model));
                    if (captureDir is { Length: > 0 } && tag == "es-normal") ApprovalNotchTests.Save((FrameworkElement)notch.Content, 120, Path.Combine(captureDir, "notch-expand-from-peek-start-es-normal-120.png"));
                    Pump(TimeSpan.FromMilliseconds(55));
                    if (captureDir is { Length: > 0 } && tag == "es-normal") ApprovalNotchTests.Save((FrameworkElement)notch.Content, 120, Path.Combine(captureDir, "notch-expand-from-peek-55ms-es-normal-120.png"));
                    Pump(TimeSpan.FromMilliseconds(545));
                    CompositionTarget.Rendering -= recordCenter;
                    var centerDrift = centerFrames.Count == 0 ? double.PositiveInfinity : centerFrames.Max() - centerFrames.Min();
                    check($"[{tag}] el centro del notch no deriva durante la apertura ({centerFrames.Count} frames, {centerDrift:0.0}px)", centerDrift <= 3);
                    if (captureDir is { Length: > 0 } && tag == "es-normal") ApprovalNotchTests.Save((FrameworkElement)notch.Content, 120, Path.Combine(captureDir, "notch-expand-from-peek-final-es-normal-120.png"));
                    notch.Collapse();
                    bool fullFromPeekClosed = PumpUntil(() => state.Mode == NotchMode.Collapsed && !holder.Children.Contains(content)
                        && peekField.GetValue(notch) is null, TimeSpan.FromSeconds(3));
                    check($"[{tag}] cerrar la vista ampliada la descarga", fullFromPeekClosed && content.DataContext is null);
                    state.Expand();
                    apply.Invoke(notch, null);
                    bool previewReopened = PumpUntil(() => state.Mode == NotchMode.Expanded && peekField.GetValue(notch) is NotchPeek reopened
                        && reopened.IsVisible && reopened.ActualWidth > 0 && reopened.ActualHeight > 0,
                        TimeSpan.FromSeconds(3));
                    var reopenedPeek = peekField.GetValue(notch) as NotchPeek;
                    check($"[{tag}] la vista corta vuelve a abrirse y se recrea al salir del estado cerrado",
                        previewReopened && reopenedPeek is not null && !ReferenceEquals(reopenedPeek, peekView));
                    notch.Collapse();
                    var closeCleanup = typeof(NotchWindow).GetField("closeCleanupPending", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    bool previewClosed = PumpUntil(() => state.Mode == NotchMode.Collapsed && !holder.Children.Contains(content) && peekField.GetValue(notch) is null
                        && !(bool)closeCleanup.GetValue(notch)!, TimeSpan.FromSeconds(3));
                    check($"[{tag}] cerrar la vista corta descarga ambas vistas y sus enlaces", previewClosed && content.DataContext is null);
                    var first = Request("npm test -- --coverage && git commit -am \"Aplica la TVA al 20 %\" && git push origin feature/invoice-vat");
                    var second = Request("rm -rf node_modules");
                    model.ShowApproval(first);
                    model.ShowApproval(second);
                    Pump(TimeSpan.FromMilliseconds(900));
                    notch.UpdateLayout();
                    check($"[{tag}] un permiso abre y fija el notch solo", state.Mode == NotchMode.Pinned && model.HasApproval);
                    check($"[{tag}] al fijarlo adjunta la vista completa y descarga la corta", holder.Children.Contains(content) && peekField.GetValue(notch) is null
                        && ReferenceEquals(content.DataContext, model) && content.IsVisible);
                    var overlay = (FrameworkElement)content.FindName("ApprovalOverlay")!;
                    check($"[{tag}] la tarjeta se ve y no desborda el notch", overlay.IsVisible && overlay.ActualWidth > 0 && overlay.ActualHeight > 0);
                    if (captureDir is { Length: > 0 })
                    {
                        var frame = (FrameworkElement)notch.Content;
                        foreach (double dpi in new[] { 96d, 120d, 144d })
                            Save(frame, dpi, Path.Combine(captureDir, $"notch-permiso-{tag}-{dpi:0}.png"));
                    }
                    first.Decide(HookVerdict.Allow);
                    Pump(TimeSpan.FromMilliseconds(300));
                    check($"[{tag}] al responder pasa a la siguiente petición en cola", model.Approval?.Request == second && state.Mode == NotchMode.Pinned);
                    second.Decide(HookVerdict.Deny);
                    Pump(TimeSpan.FromMilliseconds(900));
                    check($"[{tag}] al vaciarse la cola el notch se vuelve a cerrar", !model.HasApproval && state.Mode == NotchMode.Collapsed);
                    bool detached = PumpUntil(() => !holder.Children.Contains(content) && peekField.GetValue(notch) is null, TimeSpan.FromSeconds(3));
                    check($"[{tag}] al cerrarse vuelve a descargar ambas vistas y sus enlaces", detached && content.DataContext is null);
                    check($"[{tag}] las respuestas llegaron al agente", first.Answer.Result == HookVerdict.Allow && second.Answer.Result == HookVerdict.Deny);
                }
                finally { notch.Stop(); notch.Close(); model.Hub.Dispose(); }
            }
        Theme.ForceHighContrast = originalContrast;
        Theme.Apply();
        L10n.Init(originalLanguage == "en" ? Language.En : Language.Es);
    }

    /// La página Chat del notch real con una charla de ejemplo (ES/EN). Mismas capturas y variables que la tarjeta de permiso.
    public static void RunChat(Action<string, bool> check)
    {
        if (SkipWithoutSecondary(check, out var screen)) return;
        var captureDir = Environment.GetEnvironmentVariable("ITS_CAPTURE_UI");
        if (captureDir is { Length: > 0 }) Directory.CreateDirectory(captureDir);
        var originalLanguage = L10n.Effective;
        foreach (var (language, locale) in new[] { (Language.Es, "es"), (Language.En, "en") })
        {
            L10n.Init(language);
            Theme.Apply();
            var controller = new OllamaController();
            var prefs = new Prefs();
            controller.LoadDemo(); prefs.LoadDemo();
            prefs.SetSwitch(PrefKeys.ChatDock, true);
            var supervisor = new Supervisor(controller, prefs);
            supervisor.LoadDemo();
            var model = new PanelViewModel(supervisor);
            var notch = new NotchWindow(model, new ContentView(model), screen);
            try
            {
                notch.Start();
                model.Show(PanelView.Chat);
                model.ChatLines.Add(new(language == Language.Es ? "Hola, ¿qué tal el día?" : "Hi, how is your day going?", true));
                model.ChatLines.Add(new(language == Language.Es ? "¡Hola! Tranquilo por aquí: tu modelo sigue dormidito y la RAM está sobrada." : "Hi! Calm over here: your model is still napping and RAM is plentiful.", false));
                model.ChatLines.Add(new(language == Language.Es ? "Genial. Avísame cuando termine Claude." : "Great. Tell me when Claude finishes.", true));
                notch.Toggle();
                Pump(TimeSpan.FromMilliseconds(900));
                notch.UpdateLayout();
                check($"[chat {locale}] la página Chat abre en el notch", model.ShowingChat && model.ChatDockOn && !model.ChatEmpty);
                if (captureDir is { Length: > 0 })
                {
                    var frame = (FrameworkElement)notch.Content;
                    foreach (double dpi in new[] { 96d, 120d })
                        Save(frame, dpi, Path.Combine(captureDir, $"notch-chat-{locale}-{dpi:0}.png"));
                }
                model.CurrentSettingsTab = PanelViewModel.SettingsTab.General;
                model.Show(PanelView.Settings);
                Pump(TimeSpan.FromMilliseconds(700));
                notch.UpdateLayout();
                check($"[chat {locale}] los ajustes de agentes, chat y charla ambiente se ven", model.ChatterSegments.Count == 3 && model.ChatDockOn);
                if (captureDir is { Length: > 0 })
                    Save((FrameworkElement)notch.Content, 120, Path.Combine(captureDir, $"notch-ajustes-general-{locale}-120.png"));
            }
            finally { notch.Stop(); notch.Close(); model.Hub.Dispose(); }
        }
        L10n.Init(originalLanguage == "en" ? Language.En : Language.Es);
    }

    /// El centro de agentes en el notch real: la tarjeta de un comando delicado y las pestañas Ahora, Historial y Reglas. Usa ITS_DATA_DIR para no tocar datos reales.
    public static void RunAgents(Action<string, bool> check)
    {
        if (SkipWithoutSecondary(check, out var screen)) return;
        var captureDir = Environment.GetEnvironmentVariable("ITS_CAPTURE_UI");
        if (captureDir is { Length: > 0 }) Directory.CreateDirectory(captureDir);
        bool isolated = Environment.GetEnvironmentVariable("ITS_DATA_DIR") is { Length: > 0 };
        check("las pruebas del centro de agentes usan una carpeta de datos propia", isolated);
        if (!isolated) return;
        var originalLanguage = L10n.Effective;
        foreach (var (language, locale) in new[] { (Language.Es, "es"), (Language.En, "en") })
        {
            L10n.Init(language);
            Theme.Apply();
            AgentCenter.Log.Clear();
            foreach (var key in AgentCenter.Rules.Rules.Select(r => r.Key).ToList()) AgentCenter.Rules.Remove(key);
            var now = DateTime.Now;
            AgentCenter.Log.Add(new(now.AddMinutes(-42), "claude", "korus", "Bash", "npm test", "auto", null));
            AgentCenter.Log.Add(new(now.AddMinutes(-31), "claude", "korus", "Edit", @"C:\dev\korus\src\invoice.ts", "allow", null));
            AgentCenter.Log.Add(new(now.AddMinutes(-12), "claude", "web", "Bash", "git push --force origin main", "deny", "Fuerza un push y reescribe el historial remoto"));
            AgentCenter.Log.Add(new(now.AddMinutes(-3), "claude", "korus", "Bash", "npm run build", "always", null));
            AgentCenter.Rules.Add(Request("npm run build"));
            AgentCenter.Rules.Add(Request("dotnet test"));
            var controller = new OllamaController();
            var prefs = new Prefs();
            controller.LoadDemo(); prefs.LoadDemo();
            prefs.SetSwitch(PrefKeys.AgentApprovals, true);
            var supervisor = new Supervisor(controller, prefs);
            supervisor.LoadDemo();
            var model = new PanelViewModel(supervisor);
            var content = new ContentView(model);
            var notch = new NotchWindow(model, content, screen);
            try
            {
                notch.Start();
                model.Hub.Agents.LoadDemo();
                model.Show(PanelView.Agents);
                var risky = Request("git push --force origin main");
                var normal = Request("npm run lint");
                model.ShowApproval(risky);
                model.ShowApproval(normal);
                Pump(TimeSpan.FromMilliseconds(900));
                notch.UpdateLayout();
                check($"[agentes {locale}] lo delicado se marca y no se puede guardar como «Siempre»", model.Approval is { IsRisky: true, CanAlways: false });
                model.Approval!.AllowKey.Execute(null);
                check($"[agentes {locale}] la tecla Y no permite un comando delicado", !risky.Answer.IsCompleted);
                check($"[agentes {locale}] hay dos peticiones y el dock enseña Agentes", model.PendingApprovals.Count == 2 && model.AgentsDockOn);
                if (captureDir is { Length: > 0 }) Save((FrameworkElement)notch.Content, 120, Path.Combine(captureDir, $"notch-agentes-riesgo-{locale}-120.png"));
                var overlay = (FrameworkElement)content.FindName("ApprovalOverlay")!;
                overlay.Visibility = Visibility.Collapsed;   // para ver la lista de la página sin la tarjeta encima
                Pump(TimeSpan.FromMilliseconds(300));
                if (captureDir is { Length: > 0 }) Save((FrameworkElement)notch.Content, 120, Path.Combine(captureDir, $"notch-agentes-ahora-{locale}-120.png"));
                model.AgentsTabs[1].Pick.Execute(null);
                Pump(TimeSpan.FromMilliseconds(400));
                check($"[agentes {locale}] el historial lista las decisiones, la última primero", model.DecisionRows.Count == 4 && model.DecisionRows[0].Detail.Contains("npm run build"));
                if (captureDir is { Length: > 0 }) Save((FrameworkElement)notch.Content, 120, Path.Combine(captureDir, $"notch-agentes-historial-{locale}-120.png"));
                model.AgentsTabs[2].Pick.Execute(null);
                Pump(TimeSpan.FromMilliseconds(400));
                check($"[agentes {locale}] las reglas se listan y se pueden revocar", model.RuleRows.Count == 2);
                if (captureDir is { Length: > 0 }) Save((FrameworkElement)notch.Content, 120, Path.Combine(captureDir, $"notch-agentes-reglas-{locale}-120.png"));
                model.RuleRows[0].Revoke.Execute(null);
                check($"[agentes {locale}] revocar quita la regla", model.RuleRows.Count == 1);
                risky.Decide(HookVerdict.Deny); normal.Decide(HookVerdict.Allow);
                Pump(TimeSpan.FromMilliseconds(500));
            }
            finally { notch.Stop(); notch.Close(); model.Hub.Dispose(); }
        }
        L10n.Init(originalLanguage == "en" ? Language.En : Language.Es);
    }

    /// El monitor que NO es el principal, donde van las pruebas visuales (el principal puede tener un juego a pantalla completa). Null si no hay.
    internal static string? SecondaryScreen() => IsTargetSleeping.UI.Screens.List().FirstOrDefault(sc => !sc.Primary)?.Id;

    /// Se usa al empezar cada prueba con ventana: sin pantalla secundaria se omite en vez de abrirla en la principal.
    internal static bool SkipWithoutSecondary(Action<string, bool> check, out string screen)
    {
        screen = SecondaryScreen() ?? "";
        if (screen.Length > 0) { NotchWindow.NoForeground = true; return false; }
        check("(sin pantalla secundaria: se omiten las pruebas visuales para no abrir nada en la principal)", true);
        return true;
    }

    internal static void Save(FrameworkElement frame, double dpi, string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(frame.ActualWidth * dpi / 96), (int)Math.Ceiling(frame.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(frame);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    internal static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static bool PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
            Pump(TimeSpan.FromMilliseconds(50));
        return condition();
    }
}
