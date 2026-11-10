using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ShapePath = System.Windows.Shapes.Path;
using IsTargetSleeping;
using IsTargetSleeping.UI;

/// El beam del borde en el notch cerrado y abierto (pantalla secundaria, sin traer nada al frente). Solo con ITS_ONLY_BEAM=1.
internal static class GlowNotchTests
{
    public static void Run(Action<string, bool> check)
    {
        if (ApprovalNotchTests.SkipWithoutSecondary(check, out var screen)) return;
        var captureDir = Environment.GetEnvironmentVariable("ITS_CAPTURE_UI");
        if (captureDir is { Length: > 0 }) Directory.CreateDirectory(captureDir);
        var originalContrast = Theme.ForceHighContrast;
        var originalLanguage = L10n.Effective;
        L10n.Init(Language.Es);
        Theme.ForceHighContrast = false;
        Theme.Apply();
        var controller = new OllamaController();
        var prefs = new Prefs();
        controller.LoadDemo(); prefs.LoadDemo();
        prefs.SetList(PrefKeys.NotchWidgets, WidgetCatalog.DefaultPeek);
        prefs.SetList(PrefKeys.NotchPeekWidgets, ["shelf"]);
        prefs.SetSwitch(PrefKeys.ReducedMotion, true);   // sin latido: la captura enseña la luz fija
        var supervisor = new Supervisor(controller, prefs);
        supervisor.LoadDemo();
        var model = new PanelViewModel(supervisor);
        model.Show(PanelView.Home);
        var notch = new NotchWindow(model, () => new ContentView(model), screen);
        try
        {
            notch.Start();
            var dock = (Border)typeof(NotchWindow).GetField("dock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notch)!;
            check("el dock de navegación se difiere mientras la pastilla está cerrada", dock.Child is null);
            var gate = (VisibilityGate)typeof(NotchWindow).GetField("gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notch)!;
            var visibilityMonitor = typeof(NotchWindow).GetField("visibility", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notch)!;
            visibilityMonitor.GetType().GetMethod("Dispose")!.Invoke(visibilityMonitor, null);
            var mouseMonitor = typeof(NotchWindow).GetField("mouseMonitor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notch)!;
            mouseMonitor.GetType().GetMethod("Dispose")!.Invoke(mouseMonitor, null);
            gate.Update(HideReason.None, 0);
            gate.Update(HideReason.None, 1);
            var notchState = (NotchState)typeof(NotchWindow).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notch)!;
            notchState.Update(inside: false, suppressed: false, keepOpen: false, now: 0);
            var apply = typeof(NotchWindow).GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic)!;
            apply.Invoke(notch, null);
            ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(300));
            notch.UpdateLayout();
            var glow = (ShapePath)typeof(NotchWindow).GetField("stateGlow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notch)!;
            var shell = (FrameworkElement)typeof(NotchWindow).GetField("shell", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notch)!;
            var trail = typeof(NotchWindow).GetField("beamTrail", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notch)!;
            var trailReady = trail.GetType().GetProperty("Ready", BindingFlags.Instance | BindingFlags.Public)!;
            var trailFront = trail.GetType().GetField("front", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var beamFrames = typeof(NotchWindow).GetField("beamFrameCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var hooked = typeof(NotchWindow).GetField("beamHooked", BindingFlags.Instance | BindingFlags.NonPublic)!;
            ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(700));
            check("sin agentes ni avisos la luz está apagada", glow.Visibility == Visibility.Collapsed);
            prefs.SetSwitch(PrefKeys.ReducedMotion, false);
            model.SetAgentGlowForTest(AgentGlowState.Working);
            ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(250));
            notch.UpdateLayout();
            double before = (double)trailFront.GetValue(trail)!;
            int framesBefore = (int)beamFrames.GetValue(notch)!;
            ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(300));
            double after = (double)trailFront.GetValue(trail)!;
            int renderedFrames = (int)beamFrames.GetValue(notch)! - framesBefore;
            check($"[es-normal] el beam avanza con el notch cerrado (ventana={notch.IsVisible}, frente={before:0.00}->{after:0.00})",
                notch.IsVisible && glow.Visibility == Visibility.Visible && (bool)trailReady.GetValue(trail)! && (bool)hooked.GetValue(notch)! && Math.Abs(after - before) > 0.1);
            check($"el beam limita su repintado a 60 fps ({renderedFrames} fotogramas en 300 ms)", renderedFrames is > 0 and <= 22);
            if (captureDir is { Length: > 0 })
            {
                foreach (var (dpi, scale) in new[] { (96d, "100"), (120d, "125"), (144d, "150") })
                {
                    ApprovalNotchTests.Save(shell, dpi, Path.Combine(captureDir, $"notch-beam-es-normal-{scale}-frame-a.png"));
                    ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(250));
                    ApprovalNotchTests.Save(shell, dpi, Path.Combine(captureDir, $"notch-beam-es-normal-{scale}-frame-b.png"));
                }
                L10n.Init(Language.En);
                Theme.Apply();
                foreach (var (dpi, scale) in new[] { (96d, "100"), (120d, "125"), (144d, "150") })
                {
                    ApprovalNotchTests.Save(shell, dpi, Path.Combine(captureDir, $"notch-beam-en-normal-{scale}-frame-a.png"));
                    ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(250));
                    ApprovalNotchTests.Save(shell, dpi, Path.Combine(captureDir, $"notch-beam-en-normal-{scale}-frame-b.png"));
                }
                L10n.Init(Language.Es);
                Theme.Apply();
            }
            var sync = typeof(NotchWindow).GetMethod("SyncBeamRendering", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var hide = typeof(NotchWindow).GetMethod("HideNotch", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var show = typeof(NotchWindow).GetMethod("ShowNotch", BindingFlags.Instance | BindingFlags.NonPublic)!;
            double hiddenAt = ((System.Diagnostics.Stopwatch)typeof(NotchWindow).GetField("clock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notch)!).Elapsed.TotalSeconds;
            gate.Update(HideReason.Fullscreen, hiddenAt);
            hide.Invoke(notch, null);
            ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(250));
            if (captureDir is { Length: > 0 }) ApprovalNotchTests.Save(shell, 120, Path.Combine(captureDir, "notch-beam-es-normal-paused.png"));
            check("al ocultar el notch desaparece también el guion corto del beam", !(bool)trailReady.GetValue(trail)! && !(bool)hooked.GetValue(notch)!
                && glow.StrokeDashArray is null && glow.Visibility == Visibility.Collapsed);
            gate.Update(HideReason.None, hiddenAt + 0.1);
            gate.Update(HideReason.None, hiddenAt + VisibilityGate.ShowDelay + 0.1);
            show.Invoke(notch, null);
            ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(120));
            double resumedAt = (double)trailFront.GetValue(trail)!;
            ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(300));
            double resumedFront = (double)trailFront.GetValue(trail)!;
            bool resumed = (bool)trailReady.GetValue(trail)! && (bool)hooked.GetValue(notch)!
                && glow.StrokeDashArray is not null && glow.Visibility == Visibility.Visible && Math.Abs(resumedFront - resumedAt) > 0.1;
            check($"al reanudar el beam vuelve a avanzar (gate={gate.Shown}, ventana={notch.IsVisible}, cola={(bool)trailReady.GetValue(trail)!}, evento={(bool)hooked.GetValue(notch)!}, guion={glow.StrokeDashArray is not null}, frente={resumedAt:0.00}->{resumedFront:0.00})", resumed);
            model.SetAgentGlowForTest(null);
            ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(100));
            check("al apagar el beam se limpia la cola dibujada", !(bool)trailReady.GetValue(trail)! && glow.Visibility == Visibility.Collapsed);
            prefs.SetSwitch(PrefKeys.ReducedMotion, true);
            foreach (var state in new[] { AgentGlowState.Working, AgentGlowState.Attention, AgentGlowState.Alert, AgentGlowState.Done })
            {
                model.SetAgentGlowForTest(state);
                ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(500));
                notch.UpdateLayout();
                var (r, g, b) = AgentGlowRules.Color(state);
                check($"[luz {state}] el borde se enciende con su color", glow.Visibility == Visibility.Visible
                    && glow.Stroke is SolidColorBrush { Color: var c } && c.R == r && c.G == g && c.B == b);
                if (captureDir is { Length: > 0 }) ApprovalNotchTests.Save(shell, 120, Path.Combine(captureDir, $"notch-luz-{state}-120.png"));
            }
            model.SetAgentGlowForTest(null);
            ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(500));
            check("al soltar el estado forzado vuelve a apagarse", glow.Visibility == Visibility.Collapsed);

            prefs.SetSwitch(PrefKeys.ReducedMotion, true);
            foreach (var (language, locale) in new[] { (Language.Es, "es"), (Language.En, "en") })
                foreach (bool highContrast in new[] { false, true })
                {
                    double now = ((System.Diagnostics.Stopwatch)typeof(NotchWindow).GetField("clock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notch)!).Elapsed.TotalSeconds;
                    gate.Update(HideReason.None, now);
                    gate.Update(HideReason.None, now + 1);
                    notchState.Update(inside: false, suppressed: false, keepOpen: false, now: 0);
                    apply.Invoke(notch, null);
                    L10n.Init(language);
                    Theme.ForceHighContrast = highContrast;
                    Theme.Apply();
                    var tag = $"{locale}-{(highContrast ? "hc" : "normal")}";
                    model.SetAgentGlowForTest(AgentGlowState.Working);
                    ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(150));
                    notch.UpdateLayout();
                    bool ready = (bool)trailReady.GetValue(trail)!;
                    check($"[{tag}] el contorno fijo respeta movimiento reducido/alto contraste", glow.Visibility == Visibility.Visible && !ready);
                    if (captureDir is { Length: > 0 })
                        foreach (var (dpi, scale) in new[] { (96d, "100"), (120d, "125"), (144d, "150") })
                            ApprovalNotchTests.Save(shell, dpi, Path.Combine(captureDir, $"notch-beam-static-{tag}-{scale}.png"));
                    model.SetAgentGlowForTest(null);
                    ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(100));
                    check($"[{tag}] al apagar el estado se borra la cola anterior", !(bool)trailReady.GetValue(trail)! && glow.Visibility == Visibility.Collapsed);
                }
            prefs.SetSwitch(PrefKeys.AgentGlow, false);
            model.SetAgentGlowForTest(AgentGlowState.Working);
            ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(500));
            check("el interruptor de Ajustes apaga la luz", model.AgentGlowNow == AgentGlowState.Off && glow.Visibility == Visibility.Collapsed);
            L10n.Init(Language.Es);
            Theme.ForceHighContrast = false;
            Theme.Apply();
            prefs.SetSwitch(PrefKeys.ReducedMotion, false);
            prefs.SetSwitch(PrefKeys.AgentGlow, true);
            model.SetAgentGlowForTest(AgentGlowState.Working);
            var viewField = typeof(NotchWindow).GetField("view", BindingFlags.Instance | BindingFlags.NonPublic)!;
            double ShellCenterX() => shell.PointToScreen(new Point(shell.ActualWidth / 2, shell.ActualHeight / 2)).X;
            notchState.Expand();
            apply.Invoke(notch, null);
            ((System.Windows.Threading.DispatcherTimer)typeof(NotchWindow).GetField("transitionTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notch)!).Stop();
            ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(200));
            bool previewResponsive = notchState.Mode == NotchMode.Expanded && viewField.GetValue(notch) is null;
            if (previewResponsive && captureDir is { Length: > 0 }) ApprovalNotchTests.Save((FrameworkElement)notch.Content, 120, Path.Combine(captureDir, "notch-open-preview-es-normal-120.png"));
            notch.UpdateLayout();
            ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(650));
            bool previewStaysLight = previewResponsive && viewField.GetValue(notch) is null;
            check("el notch compacto mantiene abierta solo la vista corta sin construir el panel completo", previewStaysLight);
            int openFramesBefore = (int)beamFrames.GetValue(notch)!;
            ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(300));
            int openFrames = (int)beamFrames.GetValue(notch)! - openFramesBefore;
            bool beamShapeCurrent = !(bool)typeof(NotchWindow).GetField("beamShapeDirty", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notch)!;
            check($"el beam acompaña el notch abierto sin pasar de 60 fps ({openFrames} fotogramas en 300 ms)", glow.Visibility == Visibility.Visible && beamShapeCurrent && openFrames is > 0 and <= 22);
            if (previewStaysLight && captureDir is { Length: > 0 })
            {
                foreach (var (dpi, scale) in new[] { (96d, "100"), (120d, "125"), (144d, "150") })
                    ApprovalNotchTests.Save((FrameworkElement)notch.Content, dpi, Path.Combine(captureDir, $"notch-open-shelf-beam-es-normal-{scale}.png"));
                ApprovalNotchTests.Save((FrameworkElement)notch.Content, 120, Path.Combine(captureDir, "notch-open-shelf-beam-es-normal-frame-a.png"));
                ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(250));
                ApprovalNotchTests.Save((FrameworkElement)notch.Content, 120, Path.Combine(captureDir, "notch-open-shelf-beam-es-normal-frame-b.png"));
            }
            var open = Stopwatch.StartNew();
            var centerFrames = new List<double>();
            EventHandler recordCenter = (_, _) =>
            {
                if (notchState.Mode == NotchMode.Pinned && shell.ActualWidth > 0)
                    centerFrames.Add(ShellCenterX());
            };
            CompositionTarget.Rendering += recordCenter;
            notch.Reveal();
            open.Stop();
            notch.UpdateLayout();
            bool dockBuilt = notchState.Mode == NotchMode.Pinned && dock.Child is not null && dock.Visibility == Visibility.Visible;
            check("al fijar el notch se crea el dock y el panel completo", dockBuilt && viewField.GetValue(notch) is ContentView);
            check("el resorte de apertura dura 340 ms", Motion.OpenWidthDuration == TimeSpan.FromMilliseconds(340)
                && Motion.OpenHeightDuration == TimeSpan.FromMilliseconds(340));
            check($"al fijar el notch responde en {open.ElapsedMilliseconds} ms", dockBuilt && viewField.GetValue(notch) is ContentView);
            if (dockBuilt && captureDir is { Length: > 0 }) ApprovalNotchTests.Save((FrameworkElement)notch.Content, 120, Path.Combine(captureDir, "notch-expand-transition-es-normal-start.png"));
            ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(55));
            if (dockBuilt && captureDir is { Length: > 0 }) ApprovalNotchTests.Save((FrameworkElement)notch.Content, 120, Path.Combine(captureDir, "notch-expand-transition-es-normal-middle.png"));
            ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(600));
            CompositionTarget.Rendering -= recordCenter;
            notch.UpdateLayout();
            var notchMonitor = (Win32.RECT)typeof(NotchWindow).GetField("monitor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notch)!;
            double monitorCenter = notchMonitor.Left + notchMonitor.Width / 2d;
            double centerDrift = centerFrames.Count == 0 ? double.PositiveInfinity : centerFrames.Max() - centerFrames.Min();
            check($"al ampliar el centro horizontal no se desplaza ({centerFrames.Count} frames, deriva {centerDrift:0.0}px)", centerDrift <= 3);
            check("el panel ampliado termina centrado en el monitor", Math.Abs(ShellCenterX() - monitorCenter) <= 3);
            if (dockBuilt && captureDir is { Length: > 0 })
                foreach (var (language, locale) in new[] { (Language.Es, "es"), (Language.En, "en") })
                    foreach (bool highContrast in new[] { false, true })
                    {
                        L10n.Init(language);
                        Theme.ForceHighContrast = highContrast;
                        Theme.Apply();
                        notch.UpdateLayout();
                        var tag = $"{locale}-{(highContrast ? "hc" : "normal")}";
                        foreach (var (dpi, scale) in new[] { (96d, "100"), (120d, "125"), (144d, "150") })
                            ApprovalNotchTests.Save((FrameworkElement)notch.Content, dpi, Path.Combine(captureDir, $"notch-expand-final-{tag}-{scale}.png"));
                    }
        }
        finally
        {
            notch.Stop(); notch.Close(); model.Hub.Dispose();
            Theme.ForceHighContrast = originalContrast;
            Theme.Apply();
            L10n.Init(originalLanguage == "en" ? Language.En : Language.Es);
        }
    }
}
