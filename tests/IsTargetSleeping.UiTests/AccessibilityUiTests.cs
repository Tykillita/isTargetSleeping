using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using IsTargetSleeping;
using IsTargetSleeping.UI;

internal static class AccessibilityUiTests
{
    public static void Run(Action<string, bool> check)
    {
        var seek = new SeekBar { Value = 0.25 };
        seek.Command = new ParamCommand(value => seek.Value = (double)value!);
        var seekPeer = (IRangeValueProvider)UIElementAutomationPeer.CreatePeerForElement(seek)!.GetPattern(PatternInterface.RangeValue)!;
        seekPeer.SetValue(0.72);
        check("SeekBar expone UIA RangeValue y ejecuta SetValue", seekPeer.Value == 0.72 && seekPeer.Minimum == 0 && seekPeer.Maximum == 1 && !seekPeer.IsReadOnly);
        bool seekRejectsOutOfRange = false;
        try { seekPeer.SetValue(double.PositiveInfinity); } catch (ArgumentOutOfRangeException) { seekRejectsOutOfRange = true; }
        check("SeekBar rechaza valores no finitos", seekRejectsOutOfRange);
        var readOnlySeekPeer = (IRangeValueProvider)UIElementAutomationPeer.CreatePeerForElement(new SeekBar())!.GetPattern(PatternInterface.RangeValue)!;
        bool readOnlySeekRejectsWrite = false;
        try { readOnlySeekPeer.SetValue(0.5); } catch (InvalidOperationException) { readOnlySeekRejectsWrite = true; }
        check("SeekBar anuncia lectura sola y rechaza escritura sin comando", readOnlySeekPeer.IsReadOnly && readOnlySeekRejectsWrite);

        var ruler = new RulerPicker();
        var rulerPeer = (IRangeValueProvider)UIElementAutomationPeer.CreatePeerForElement(ruler)!.GetPattern(PatternInterface.RangeValue)!;
        rulerPeer.SetValue(42);
        check("RulerPicker expone el día como valor UIA", ruler.Offset == 42 && rulerPeer.Minimum == 0 && rulerPeer.Maximum == 365);

        var wheel = new ColorWheel { Hue = 45 };
        wheel.Command = new ParamCommand(value => wheel.Hue = (double)value!);
        var wheelPeer = (IRangeValueProvider)UIElementAutomationPeer.CreatePeerForElement(wheel)!.GetPattern(PatternInterface.RangeValue)!;
        wheelPeer.SetValue(210);
        check("ColorWheel expone el tono como valor UIA", wheel.Hue == 210 && wheelPeer.Minimum == 0 && wheelPeer.Maximum == 359);

        CheckFocusIndicators(check, seek, ruler, wheel);
        CheckOverlayFocus(check);
        CheckLightStripThemeSync(check);
    }

    private static void CheckFocusIndicators(Action<string, bool> check, SeekBar seek, RulerPicker ruler, ColorWheel wheel)
    {
        Window? host = null;
        try
        {
            var power = new BrandSwitch();
            AutomationProperties.SetName(power, "LED");
            var day = new ToggleButton { Content = "Mon", Style = (Style)Application.Current.FindResource("TeamDayToggle") };
            host = new Window
            {
                Content = new StackPanel { Children = { power, day, seek, ruler, wheel } },
                Width = 200, Height = 220, ShowInTaskbar = false, Left = -20000, Top = -20000
            };
            host.Show();
            host.Activate();
            Pump();
            check("interruptores principales conservan anillo de foco", power.Focusable && power.FocusVisualStyle is not null
                && day.Focusable && day.FocusVisualStyle is not null);
            bool controlsFocusable = seek.Focus() && seek.IsKeyboardFocused && ruler.Focus() && ruler.IsKeyboardFocused
                && wheel.Focus() && wheel.IsKeyboardFocused;
            check("SeekBar, RulerPicker y ColorWheel reciben foco de teclado", controlsFocusable);
        }
        catch (Exception e) { check($"indicadores de foco: {e.GetType().Name}: {e.Message}", false); }
        finally { host?.Close(); }
    }

    private static void CheckOverlayFocus(Action<string, bool> check)
    {
        var previousDataDir = Environment.GetEnvironmentVariable("ITS_DATA_DIR");
        var isolatedDataDir = Path.Combine(Path.GetTempPath(), "its-ui-focus-" + Guid.NewGuid().ToString("N"));
        PanelViewModel? model = null;
        Window? host = null;
        try
        {
            Environment.SetEnvironmentVariable("ITS_DATA_DIR", isolatedDataDir);
            var prefs = new Prefs();
            prefs.LoadDemo();
            var ollama = new OllamaController();
            ollama.LoadDemo();
            var supervisor = new Supervisor(ollama, prefs);
            supervisor.LoadDemo();
            model = new PanelViewModel(supervisor, PanelView.Widgets);
            var view = new ContentView(model);
            host = new Window { Content = view, Width = 700, Height = 700, WindowStyle = WindowStyle.None, ShowInTaskbar = false, Left = -20000, Top = -20000 };
            host.Show();
            host.Activate();
            view.UpdateLayout();
            var tile = model.HomeFolders.Tiles.FirstOrDefault();
            var opener = tile is null ? null : Descendants(view).OfType<Button>().FirstOrDefault(button => ReferenceEquals(button.DataContext, tile));
            if (tile is null || opener is null)
            {
                check("carpeta de widgets: origen de foco localizable", false);
                return;
            }

            opener.Focus();
            string openerId = AutomationProperties.GetAutomationId(opener);
            tile.Open.Execute(null);
            view.UpdateLayout();
            Pump();
            bool focusedInFolder = Keyboard.FocusedElement is Button { DataContext: FolderOption };
            var folderRoot = FindByName(view, "WidgetOverlay");
            bool folderContainsKeyboardNavigation = KeyboardNavigation.GetTabNavigation(folderRoot) == KeyboardNavigationMode.Cycle
                && KeyboardNavigation.GetControlTabNavigation(folderRoot) == KeyboardNavigationMode.Cycle
                && KeyboardNavigation.GetDirectionalNavigation(folderRoot) == KeyboardNavigationMode.Cycle;
            check("abrir carpeta mueve el foco dentro y contiene la navegación", focusedInFolder && folderContainsKeyboardNavigation);

            model.CloseTopOverlay();
            view.UpdateLayout();
            Pump();
            check("cerrar carpeta devuelve el foco a quien la abrió", FocusedAutomationId() == openerId);

            tile.Open.Execute(null);
            view.UpdateLayout();
            Pump();
            var option = Descendants(view).OfType<Button>().FirstOrDefault(button => button.DataContext is FolderOption);
            if (option is null)
            {
                check("hoja de widgets: opción accesible para abrir", false);
                return;
            }
            option.Focus();
            string optionId = AutomationProperties.GetAutomationId(option);
            ((FolderOption)option.DataContext!).Open.Execute(null);
            view.UpdateLayout();
            Pump();
            bool focusedInSheet = Keyboard.FocusedElement is UIElement focused && IsDescendantOf(focused, FindByName(view, "WidgetSheet"));
            var sheetRoot = FindByName(view, "WidgetSheet");
            bool sheetContainsKeyboardNavigation = KeyboardNavigation.GetTabNavigation(sheetRoot) == KeyboardNavigationMode.Cycle
                && KeyboardNavigation.GetControlTabNavigation(sheetRoot) == KeyboardNavigationMode.Cycle
                && KeyboardNavigation.GetDirectionalNavigation(sheetRoot) == KeyboardNavigationMode.Cycle;
            check("abrir hoja mueve el foco y contiene la navegación", focusedInSheet && sheetContainsKeyboardNavigation);

            model.CloseTopOverlay();
            view.UpdateLayout();
            Pump();
            check("cerrar hoja devuelve el foco a su opción", FocusedAutomationId() == optionId);

            model.CloseTopOverlay();
            view.UpdateLayout();
            Pump();
            check("cerrar la carpeta devuelve el foco a quien la abrió", FocusedAutomationId() == openerId);
        }
        catch (Exception e)
        {
            check($"overlay/foco: {e.GetType().Name}: {e.Message}", false);
        }
        finally
        {
            host?.Close();
            model?.Hub.Dispose();
            Environment.SetEnvironmentVariable("ITS_DATA_DIR", previousDataDir);
            try { Directory.Delete(isolatedDataDir, true); } catch (IOException) { }
        }
    }

    private static void CheckLightStripThemeSync(Action<string, bool> check)
    {
        var oldHighContrast = Theme.ForceHighContrast;
        Window? host = null;
        try
        {
            Theme.ForceHighContrast = false;
            Theme.Apply();
            var strip = new LightStrip { IsOn = true, Effect = 0x87 };
            host = new Window { Content = strip, Width = 180, Height = 50, ShowInTaskbar = false, Left = -20000, Top = -20000 };
            host.Show();
            Pump();
            var timer = (DispatcherTimer)typeof(LightStrip).GetField("Clock", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            if (SystemParameters.HighContrast)
            {
                check("LightStrip no anima con alto contraste real de Windows", !timer.IsEnabled);
                return;
            }
            bool animatesNormally = timer.IsEnabled;
            Theme.ForceHighContrast = true;
            Theme.Apply();
            bool stopsInHighContrast = !timer.IsEnabled;
            Theme.ForceHighContrast = false;
            Theme.Apply();
            bool resumesAfterHighContrast = timer.IsEnabled;
            strip.Visibility = Visibility.Hidden;
            Pump();
            bool stopsWhenHidden = !timer.IsEnabled;
            strip.Visibility = Visibility.Visible;
            Pump();
            bool resumesWhenVisible = timer.IsEnabled;
            check($"LightStrip alto contraste: normal={animatesNormally}, pausa={stopsInHighContrast}, reanuda={resumesAfterHighContrast}, sistema={SystemParameters.HighContrast}",
                animatesNormally && stopsInHighContrast && resumesAfterHighContrast);
            check("LightStrip detiene el reloj oculto y lo reanuda al mostrarse", stopsWhenHidden && resumesWhenVisible);
        }
        catch (Exception e) { check($"LightStrip/alto contraste: {e.GetType().Name}: {e.Message}", false); }
        finally
        {
            host?.Close();
            Theme.ForceHighContrast = oldHighContrast;
            Theme.Apply();
        }
    }

    private static DependencyObject FindByName(DependencyObject root, string name) => Descendants(root).First(d => d is FrameworkElement e && e.Name == name);
    private static bool IsDescendantOf(DependencyObject element, DependencyObject root) => Descendants(root).Any(d => ReferenceEquals(d, element));
    private static string FocusedAutomationId() => Keyboard.FocusedElement is DependencyObject focused ? AutomationProperties.GetAutomationId(focused) : "";

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
