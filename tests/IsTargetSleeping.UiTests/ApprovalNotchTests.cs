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
                var supervisor = new Supervisor(controller, prefs);
                supervisor.LoadDemo();
                var model = new PanelViewModel(supervisor);
                var content = new ContentView(model);
                var notch = new NotchWindow(model, content, screen);
                var tag = $"{locale}-{(highContrast ? "hc" : "normal")}";
                try
                {
                    notch.Start();
                    model.Show(PanelView.Main);
                    var state = (NotchState)typeof(NotchWindow).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notch)!;
                    check($"[{tag}] el notch empieza cerrado", state.Mode == NotchMode.Collapsed);
                    var first = Request("npm test -- --coverage && git commit -am \"Aplica la TVA al 20 %\" && git push origin feature/invoice-vat");
                    var second = Request("rm -rf node_modules");
                    model.ShowApproval(first);
                    model.ShowApproval(second);
                    Pump(TimeSpan.FromMilliseconds(900));
                    notch.UpdateLayout();
                    check($"[{tag}] un permiso abre y fija el notch solo", state.Mode == NotchMode.Pinned && model.HasApproval);
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
}
