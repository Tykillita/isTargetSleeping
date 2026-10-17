using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IsTargetSleeping;
using IsTargetSleeping.UI;

/// Texto que no se lee: recorre todas las superficies de la app (páginas, pestañas, widgets uno a uno, versión corta, dock y pastilla,
/// confirmación de procesos, editor y sus colores de sintaxis, menú, tooltip y «Acerca de»; ancho y estrecho, es/en) y falla si alguna letra
/// tiene menos de 3:1 de contraste con lo que hay detrás (letra oscura sobre fondo oscuro, o demasiado tenue). Los botones blancos y la opción
/// elegida llevan letra oscura sobre fondo claro a propósito y pasan.
internal static class ContrastAudit
{
    private const double MinRatio = 3.0;

    private static double Channel(double v) { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
    private static double Luminance(Color c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

    /// El color sobre otro (la transparencia del primero deja ver el segundo, que se da por opaco).
    private static Color Over(Color top, Color back)
    {
        double a = top.A / 255.0;
        return Color.FromRgb((byte)(top.R * a + back.R * (1 - a)), (byte)(top.G * a + back.G * (1 - a)), (byte)(top.B * a + back.B * (1 - a)));
    }

    public static double Ratio(Color foreground, Color background)
    {
        double a = Luminance(Over(foreground, background)), b = Luminance(background);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static Color? Solid(Brush? brush) => brush switch
    {
        SolidColorBrush { Opacity: > 0.4 } s when s.Color.A > 40 => s.Color,
        GradientBrush { Opacity: > 0.4 } g when g.GradientStops.Count > 0 && g.GradientStops[0].Color.A > 40 => g.GradientStops[0].Color,
        _ => null,
    };

    private static readonly Color Black = Color.FromRgb(0, 0, 0);

    /// Lo que hay detrás del elemento: los fondos con cuerpo que lo cubren, de fuera adentro, sobre negro (las ventanas del notch son negras).
    private static Color BackdropOf(DependencyObject element)
    {
        var layers = new List<Color>();
        for (var d = VisualTreeHelper.GetParent(element); d is not null; d = VisualTreeHelper.GetParent(d))
        {
            Brush? background = d switch { Border b => b.Background, Panel p => p.Background, Control c => c.Background, _ => null };
            if (Solid(background) is { } color) layers.Add(color);
        }
        var result = Black;
        for (int i = layers.Count - 1; i >= 0; i--) result = Over(layers[i], result);
        return result;
    }

    private static bool Shown(DependencyObject element)
    {
        for (var d = element; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is UIElement { Visibility: not Visibility.Visible } or UIElement { Opacity: < 0.05 }) return false;
        return true;
    }

    private static string Chain(DependencyObject element)
    {
        var parts = new List<string>();
        int count = 0;
        for (var d = element; d is not null && count < 40; d = VisualTreeHelper.GetParent(d), count++)
            if (d is FrameworkElement { Name: { Length: > 0 } n } fe) parts.Add($"{n}={fe.Visibility}");
            else if (d is UIElement { Visibility: not Visibility.Visible } ui) parts.Add($"{d.GetType().Name}={ui.Visibility}");
        return string.Join(" < ", parts);
    }

    private static string Short(string text) => text.Length > 40 ? text[..40] : text;

    public static List<string> Offenders(DependencyObject root)
    {
        var found = new List<string>();
        void Check(DependencyObject owner, Brush? foreground, string text, double opacity)
        {
            if (Solid(foreground) is not { } fg || !Shown(owner)) return;
            var back = BackdropOf(owner);
            double ratio = Ratio(Color.FromArgb((byte)(fg.A * Math.Clamp(opacity, 0, 1)), fg.R, fg.G, fg.B), back);
            if (ratio < MinRatio) found.Add($"«{Short(text)}» ({string.Join(" ", text.Take(6).Select(c => ((int)c).ToString("X")))}) letra={fg} fondo={back} contraste={ratio:0.0} en {Chain(owner)}");
        }
        void Walk(DependencyObject d)
        {
            if (d is TextBlock { ActualWidth: > 0 } t && !string.IsNullOrWhiteSpace(t.Text)) Check(t, t.Foreground, t.Text, 1);
            else if (d is TextBox { ActualWidth: > 0 } box) Check(box, box.Foreground, string.IsNullOrEmpty(box.Text) ? "(cuadro de texto)" : box.Text, 1);
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(root);
        return found;
    }

    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var inner in All<T>(child)) yield return inner;
        }
    }

    private static void Pump(Func<bool> done, double seconds = 4)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var poll = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        var started = DateTime.UtcNow;
        poll.Tick += (_, _) => { if (done() || DateTime.UtcNow - started > TimeSpan.FromSeconds(seconds)) { poll.Stop(); frame.Continue = false; } };
        poll.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static Window Host(FrameworkElement content, double width = 700, double height = 3000) =>
        new() { Content = content, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000, Width = width, Height = height, Background = Brushes.Black };

    public static void Run(Action<string, bool> check)
    {
        long mib = 1024 * 1024;
        ProcessSnapshot Make(int pid, string name, long ram, bool window) => new()
        {
            Identity = new ProcessIdentity(pid, 100), Name = name, Executable = name.ToLowerInvariant() + ".exe", Path = @"C:\Tools\" + name.ToLowerInvariant() + ".exe",
            OwnerSid = "S-1-user", User = @"PC\demo", SessionId = 1, HasWindow = window, IsSystem = false, IsCritical = false,
            WorkingSetBytes = ram * 2, PrivateWorkingSetBytes = ram, CommitBytes = ram * 3, CpuPercent = (pid % 13) / 3.0, ParentPid = 1,
        };
        var chrome = Make(11, "Chrome", 900 * mib, true);
        var sample = new ProcessSample(DateTimeOffset.Now, 16L * 1024 * mib, true,
            [chrome, Make(12, "Chrome", 300 * mib, false), Make(21, "Code", 700 * mib, true), Make(31, "Ollama", 500 * mib, false)]);
        var box = Path.Combine(Path.GetTempPath(), "its-contrast-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(box, "carpeta"));
        File.WriteAllText(Path.Combine(box, "carpeta", "a.txt"), new string('x', 2048));
        File.WriteAllText(Path.Combine(box, "suelto.cs"), "// comentario\nusing System;\nclass A { string s = \"texto\"; int n = 42; void F() { return; } }\n");
        try
        {
            foreach (var language in new[] { Language.En, Language.Es })
            {
                L10n.Init(language);
                var prefs = new Prefs();
                prefs.LoadDemo();
                prefs.SetSwitch(PrefKeys.ClipboardHistory, true);
                var ollama = new OllamaController();
                ollama.LoadDemo();
                var supervisor = new Supervisor(ollama, prefs);
                supervisor.LoadDemo();
                PanelViewModel NewModel(bool wide)
                {
                    var m = new PanelViewModel(supervisor,
                        previewProcesses: r => Task.FromResult(new ProcessActionPreview(r, [chrome], 900 * mib, true)),
                        executeProcesses: (r, _) => Task.FromResult(new ProcessActionResult(r.Id, ProcessActionOutcome.Success, DateTimeOffset.Now, DateTimeOffset.Now, [])));
                    m.WideLayout = wide;
                    m.BodyMaxHeight = 3000;
                    m.Procs.Apply(sample);
                    return m;
                }
                void Audit(string name, DependencyObject content, bool wide)
                {
                    var bad = Offenders(content);
                    foreach (var line in bad.Take(6)) Console.WriteLine($"   texto ilegible en {name}: {line}");
                    check($"{language}/{name}/{(wide ? "ancho" : "estrecho")}: todo el texto se lee (contraste ≥ {MinRatio}:1)", bad.Count == 0);
                }
                void Page(string name, bool wide, Action<PanelViewModel> setup, PanelView view)
                {
                    var model = NewModel(wide);
                    setup(model);
                    model.Show(view);
                    var content = new ContentView(model);
                    var window = Host(content);
                    window.Show();
                    window.UpdateLayout();
                    Audit(name, content, wide);
                    window.Close();
                }

                foreach (var wide in new[] { true, false })
                {
                    // Cada página y cada pestaña.
                    Page("Ollama", wide, _ => { }, PanelView.Main);
                    Page("Home", wide, _ => { }, PanelView.Home);
                    Page("Widgets", wide, _ => { }, PanelView.Widgets);
                    for (int t = 0; t < 5; t++) { int tab = t; Page($"Ajustes {tab}", wide, m => m.CurrentSettingsTab = (PanelViewModel.SettingsTab)tab, PanelView.Settings); }
                    for (int t = 0; t < 3; t++) { int tab = t; Page($"Actividad {tab}", wide, m => { m.CurrentActivityTab = (PanelViewModel.ActivityTab)tab; m.Procs.Selected = m.Procs.Rows[0]; }, PanelView.Activity); }

                    // La bandeja con archivos, el historial del portapapeles con textos y el editor con un archivo de código.
                    {
                        var model = NewModel(wide);
                        model.AddToTray([Path.Combine(box, "carpeta"), Path.Combine(box, "suelto.cs")]);
                        model.Show(PanelView.Tray);
                        var content = new ContentView(model);
                        var window = Host(content);
                        window.Show();
                        Pump(() => model.TrayRows.Count == 2 && !model.TrayRows[0].Detail.Contains("…"), 1.5);
                        window.UpdateLayout();
                        Audit("Bandeja con archivos", content, wide);
                        window.Close();
                    }
                    {
                        var model = NewModel(wide);
                        model.Clip.Add("un texto copiado de ejemplo");
                        model.Clip.Add("otro texto copiado, bastante más largo que el anterior para que se recorte en la fila");
                        model.Show(PanelView.Clipboard);
                        model.Notify();
                        var content = new ContentView(model);
                        var window = Host(content);
                        window.Show();
                        window.UpdateLayout();
                        Audit("Portapapeles con textos", content, wide);
                        window.Close();
                    }
                    {
                        var model = NewModel(wide);
                        model.OpenEditor(Path.Combine(box, "suelto.cs"));
                        var content = new ContentView(model);
                        var window = Host(content);
                        window.Show();
                        window.UpdateLayout();
                        Audit("Editor", content, wide);
                        var editor = All<ICSharpCode.AvalonEdit.TextEditor>(content).FirstOrDefault();
                        check($"{language}/Editor/{(wide ? "ancho" : "estrecho")}: hay editor de código", editor is not null);
                        if (editor is not null)
                        {
                            var back = BackdropOf(editor);
                            var tooFaint = new List<string>();
                            if (Solid(editor.Background) is { } own) back = Over(own, back);
                            if (Solid(editor.Foreground) is { } fg && Ratio(fg, back) < MinRatio) tooFaint.Add($"letra {fg}");
                            if (Solid(editor.LineNumbersForeground) is { } ln && Ratio(ln, back) < MinRatio) tooFaint.Add($"números de línea {ln}");
                            foreach (var kind in Enum.GetValues<TokenKind>())
                                if (SyntaxPalette.For(kind) is { } brush && Solid(brush) is { } c && Ratio(c, back) < MinRatio) tooFaint.Add($"{kind} {c}");
                            foreach (var line in tooFaint) Console.WriteLine($"   color de editor ilegible: {line}");
                            check($"{language}/Editor/{(wide ? "ancho" : "estrecho")}: letra, números de línea y los {Enum.GetValues<TokenKind>().Length} colores de sintaxis se leen sobre el fondo del editor", tooFaint.Count == 0);
                        }
                        window.Close();
                    }

                    // La confirmación de finalizar procesos, con y sin reintento como administrador.
                    {
                        var model = NewModel(wide);
                        model.CurrentActivityTab = PanelViewModel.ActivityTab.Processes;
                        model.Show(PanelView.Activity);
                        var content = new ContentView(model);
                        var window = Host(content);
                        window.Show();
                        model.Procs.Selected = model.Procs.Rows[0];
                        model.Procs.ActApplication.Execute(null);
                        Pump(() => model.Procs.HasConfirmation);
                        window.UpdateLayout();
                        check($"{language}/Confirmación/{(wide ? "ancho" : "estrecho")}: la tarjeta de confirmación se ve", model.Procs.HasConfirmation
                            && All<TextBlock>(content).Any(t => t.IsVisible && t.Text == model.Procs.Confirmation!.Title));
                        Audit("Confirmación de procesos", content, wide);
                        model.Procs.Cancel.Execute(null);
                        Pump(() => !model.Procs.Operating);
                        window.Close();
                    }
                }

                // La versión corta (hover) con cada widget solo, el Home con cada widget solo y la lista de widgets por defecto.
                foreach (var widget in WidgetCatalog.All)
                {
                    prefs.SetList(PrefKeys.NotchWidgets, [widget.Id]);
                    prefs.SetList(PrefKeys.NotchPeekWidgets, [widget.Id]);
                    var home = NewModel(true);
                    home.Show(PanelView.Home);
                    var homeView = new ContentView(home);
                    var homeWindow = Host(homeView);
                    homeWindow.Show();
                    homeWindow.UpdateLayout();
                    Audit($"Home con {widget.Id}", homeView, true);
                    homeWindow.Close();
                    var peekModel = NewModel(true);
                    var peek = new NotchPeek(peekModel);
                    var peekWindow = Host(peek, 700, 600);
                    peekWindow.Show();
                    peekWindow.UpdateLayout();
                    Audit($"Versión corta con {widget.Id}", peek, true);
                    peekWindow.Close();
                }
                prefs.SetList(PrefKeys.NotchWidgets, WidgetCatalog.DefaultWidgets);
                prefs.SetList(PrefKeys.NotchPeekWidgets, WidgetCatalog.DefaultPeek);
            }
            // Lo que abre ventanas y popups propios (notch, «Acerca de», menú, tooltip) va después de todo lo demás: deja estado en WPF.
            foreach (var language in new[] { Language.En, Language.Es })
            {
                L10n.Init(language);
                var prefs = new Prefs();
                prefs.LoadDemo();
                var ollama = new OllamaController();
                ollama.LoadDemo();
                var supervisor = new Supervisor(ollama, prefs);
                supervisor.LoadDemo();
                PanelViewModel NewModel(bool wide)
                {
                    var m = new PanelViewModel(supervisor) { WideLayout = wide, BodyMaxHeight = 3000 };
                    m.Procs.Apply(sample);
                    return m;
                }
                void Audit(string name, DependencyObject content, bool wide)
                {
                    var bad = Offenders(content);
                    foreach (var line in bad.Take(6)) Console.WriteLine($"   texto ilegible en {name}: {line}");
                    check($"{language}/{name}/{(wide ? "ancho" : "estrecho")}: todo el texto se lee (contraste ≥ {MinRatio}:1)", bad.Count == 0);
                }
                // El notch cerrado: la pastilla y el dock (con favoritos y los botones opcionales).
                {
                    prefs.SetList(PrefKeys.NotchDockPages, ["ollama", "activity"]);
                    var model = NewModel(true);
                    var notch = new NotchWindow(model, new ContentView(model));
                    var root = (FrameworkElement)notch.Content;
                    root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    root.Arrange(new Rect(root.DesiredSize));
                    root.UpdateLayout();
                    Audit("Pastilla y dock del notch", root, true);
                    notch.Stop();
                    notch.Close();
                    prefs.SetList(PrefKeys.NotchDockPages, Array.Empty<string>());
                }

                // «Acerca de», el menú de la bandeja y los tooltips: lo que dibuja la propia app fuera del panel.
                {
                    AboutWindow.Present();
                    var about = Application.Current.Windows.OfType<AboutWindow>().First();
                    about.UpdateLayout();
                    Audit("Acerca de", (DependencyObject)about.Content, false);
                    about.Close();
                    var menu = new System.Windows.Controls.ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.Absolute, HorizontalOffset = -20000, VerticalOffset = -20000 };
                    menu.Items.Add(new MenuItem { Header = L10n.tr("Ajustes") });
                    menu.Items.Add(new MenuItem { Header = L10n.tr("Actividad"), IsChecked = true });
                    menu.Items.Add(new MenuItem { Header = L10n.tr("Liberar RAM"), IsEnabled = false });
                    menu.Items.Add(new Separator());
                    menu.Items.Add(new MenuItem { Header = L10n.tr("Salir") });
                    menu.IsOpen = true;
                    menu.UpdateLayout();
                    check($"{language}/Menú: el menú de la bandeja se abre", All<MenuItem>(menu).Any() || menu.Items.Count == 5);
                    var menuBad = Offenders(menu);
                    foreach (var line in menuBad.Take(6)) Console.WriteLine($"   texto ilegible en el menú: {line}");
                    check($"{language}/Menú: todo el texto del menú de la bandeja se lee", menuBad.Count == 0);
                    menu.IsOpen = false;
                    var tipHost = new Border { Width = 40, Height = 20, Background = Brushes.Black };
                    var tipWindow = Host(tipHost, 200, 100);
                    tipWindow.Show();
                    var tip = new System.Windows.Controls.ToolTip { Content = "Texto de ayuda", PlacementTarget = tipHost, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, IsOpen = true };
                    tip.UpdateLayout();
                    var tipBad = Offenders(tip);
                    foreach (var line in tipBad.Take(3)) Console.WriteLine($"   texto ilegible en el tooltip: {line}");
                    check($"{language}/Tooltip: el texto de ayuda se lee", tipBad.Count == 0 && All<TextBlock>(tip).Any(t => t.Text == "Texto de ayuda"));
                    tip.IsOpen = false;
                    tipWindow.Close();
                }
            }
        }
        finally { try { Directory.Delete(box, true); } catch (IOException) { } }
    }
}
