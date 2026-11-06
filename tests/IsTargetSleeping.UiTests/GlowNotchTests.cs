using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using ShapePath = System.Windows.Shapes.Path;
using IsTargetSleeping;
using IsTargetSleeping.UI;

/// La luz de los agentes en el borde del notch cerrado (pantalla secundaria, sin traer nada al frente). Solo con ITS_ONLY_APPROVAL=1.
internal static class GlowNotchTests
{
    public static void Run(Action<string, bool> check)
    {
        if (ApprovalNotchTests.SkipWithoutSecondary(check, out var screen)) return;
        var captureDir = Environment.GetEnvironmentVariable("ITS_CAPTURE_UI");
        if (captureDir is { Length: > 0 }) Directory.CreateDirectory(captureDir);
        L10n.Init(Language.Es);
        Theme.Apply();
        var controller = new OllamaController();
        var prefs = new Prefs();
        controller.LoadDemo(); prefs.LoadDemo();
        prefs.SetSwitch(PrefKeys.ReducedMotion, true);   // sin latido: la captura enseña la luz fija
        var supervisor = new Supervisor(controller, prefs);
        supervisor.LoadDemo();
        var model = new PanelViewModel(supervisor);
        var notch = new NotchWindow(model, new ContentView(model), screen);
        try
        {
            notch.Start();
            var glow = (ShapePath)typeof(NotchWindow).GetField("stateGlow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notch)!;
            ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(700));
            check("sin agentes ni avisos la luz está apagada", glow.Visibility == Visibility.Collapsed);
            foreach (var state in new[] { AgentGlowState.Working, AgentGlowState.Attention, AgentGlowState.Alert, AgentGlowState.Done })
            {
                model.SetAgentGlowForTest(state);
                ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(500));
                notch.UpdateLayout();
                var (r, g, b) = AgentGlowRules.Color(state);
                check($"[luz {state}] el borde se enciende con su color", glow.Visibility == Visibility.Visible
                    && glow.Stroke is SolidColorBrush { Color: var c } && c.R == r && c.G == g && c.B == b);
                if (captureDir is { Length: > 0 }) ApprovalNotchTests.Save((FrameworkElement)notch.Content, 120, Path.Combine(captureDir, $"notch-luz-{state}-120.png"));
            }
            model.SetAgentGlowForTest(null);
            ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(500));
            check("al soltar el estado forzado vuelve a apagarse", glow.Visibility == Visibility.Collapsed);
            prefs.SetSwitch(PrefKeys.AgentGlow, false);
            model.SetAgentGlowForTest(AgentGlowState.Working);
            ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(500));
            check("el interruptor de Ajustes apaga la luz", model.AgentGlowNow == AgentGlowState.Off && glow.Visibility == Visibility.Collapsed);
        }
        finally { notch.Stop(); notch.Close(); model.Hub.Dispose(); }
    }
}
