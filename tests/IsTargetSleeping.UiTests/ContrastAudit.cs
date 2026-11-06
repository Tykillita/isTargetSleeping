using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Ellipse = System.Windows.Shapes.Ellipse;
using Rectangle = System.Windows.Shapes.Rectangle;
using Shape = System.Windows.Shapes.Shape;
using IsTargetSleeping;
using IsTargetSleeping.UI;
using IsTargetSleeping.UI.Widgets;

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
        if (element is UIElement child && VisualTreeHelper.GetParent(child) is Panel panel)
        {
            var bounds = child.TransformToAncestor(panel).TransformBounds(new Rect(new Point(), child.RenderSize));
            var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
            int at = panel.Children.IndexOf(child);
            for (int i = at - 1; i >= 0; i--)
                if (panel.Children[i] is Shape shape && shape.TransformToAncestor(panel).TransformBounds(new Rect(new Point(), shape.RenderSize)).Contains(center)
                    && Solid(shape.Fill) is { } fill) { result = Over(fill, result); break; }
        }
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

    private static Window Host(FrameworkElement content, double width = 700, double height = 3000, bool activate = false) =>
        new() { Content = content, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = activate,
            Left = activate ? 60 : -20000, Top = activate ? 60 : -20000, Width = width, Height = height, Background = Brushes.Black };

    private static void Capture(FrameworkElement content, Language language, string name)
    {
        if (Environment.GetEnvironmentVariable("ITS_CAPTURE_UI") is not { Length: > 0 } folder || content.ActualWidth <= 0 || content.ActualHeight <= 0) return;
        Directory.CreateDirectory(folder);
        foreach (double dpi in name.StartsWith("gallery-transition", StringComparison.Ordinal) ? new[] { 96d } : new[] { 96d, 120d, 144d })
        {
            int width = (int)Math.Ceiling(content.ActualWidth * dpi / 96);
            int height = (int)Math.Ceiling(content.ActualHeight * dpi / 96);
            var rendered = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
            rendered.Render(content);
            // RenderTargetBitmap does not include the DWM acrylic backdrop.
            var background = Theme.Flat ? Theme.Current.Bg : Theme.Hex(0x050506);
            var preview = new DrawingVisual();
            using (var drawing = preview.RenderOpen())
            {
                var bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
                drawing.DrawRectangle(Theme.Brush(Color.FromRgb(background.R, background.G, background.B)), null, bounds);
                drawing.DrawImage(rendered, bounds);
            }
            var image = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
            image.Render(preview);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            var locale = language == Language.Es ? "es" : "en";
            var theme = Theme.ForceHighContrast ? "high-contrast" : Theme.OutlineStyle ? "outline" : "normal";
            using var output = File.Create(Path.Combine(folder, $"{name}-{locale}-{theme}-{dpi:0}.png"));
            encoder.Save(output);
        }
    }

    private static void AuditFavoriteAppIcon(Action<string, bool> check, Language language)
    {
        var path = Environment.GetEnvironmentVariable("ITS_AUDIT_FAVORITE_ICON") is { Length: > 0 } samplePath ? samplePath : Environment.ProcessPath;
        if (path is null || AppIcons.For(path, large: true) is not { } icon)
        {
            check($"{language}: Windows devuelve el icono de una app", false);
            return;
        }

        var title = Path.GetFileNameWithoutExtension(path);
        var prefs = new Prefs();
        prefs.LoadDemo();
        prefs.SetList(PrefKeys.NotchFavorites, ["power", FavoriteCatalog.AppId(path)]);
        var ollama = new OllamaController();
        ollama.LoadDemo();
        var supervisor = new Supervisor(ollama, prefs);
        supervisor.LoadDemo();
        var model = new PanelViewModel(supervisor, PanelView.Settings);
        var appRow = model.FavoriteRows.Single(row => row.Title == title);
        var appFolder = model.FavoriteFolders.Tiles.Single(tile => tile.AutomationId == "folder-favorites-apps");
        check($"{language}: la app enlaza el icono en Favoritos y Mis apps", ReferenceEquals(appRow.Icon, icon) && appFolder.Minis.Any(mini => ReferenceEquals(mini.Icon, icon)));
        appFolder.Open.Execute(null);
        check($"{language}: el icono sigue en la carpeta abierta", ReferenceEquals(model.OverlayFolder?.Options.Single().Icon, icon));

        var notch = new NotchWindow(model, new ContentView(model));
        var buttonName = L10n.tr("Abrir %@", title);
        var appButton = All<Button>((DependencyObject)notch.Content).FirstOrDefault(button =>
            System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(button)?.GetName() == buttonName);
        bool dockIcon = appButton?.Content is Border { Child: Grid dockGrid } && dockGrid.Children.OfType<Image>().Any(image => ReferenceEquals(image.Source, icon));
        check($"{language}: el dock del notch muestra el icono real de la app", dockIcon);

        var open = new Command(() => { });
        var content = new StackPanel { Margin = new Thickness(16), Children =
        {
            new ContentControl { Content = appFolder },
            new ContentControl { Content = model.OverlayFolder },
            new ContentControl { Content = new DockPreview([new DockPart("", true, icon)]) },
        }};
        var host = Host(content, 380, 480);
        host.Show();
        host.UpdateLayout();
        bool visible = All<Image>(content).Any(image => ReferenceEquals(image.Source, icon) && image.Visibility == Visibility.Visible && image.ActualWidth > 0);
        check($"{language}: el icono nativo se pinta en carpeta y dock", visible);
        Capture(content, language, "favorite-app-native-icon");
        host.Close();
        notch.Stop();
        notch.Close();
        model.Hub.Dispose();
    }

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
            bool clipboardOnly = Environment.GetEnvironmentVariable("ITS_ONLY_CLIPBOARD") == "1";
            bool cameraOnly = Environment.GetEnvironmentVariable("ITS_ONLY_CAMERA") == "1";
            bool mediaOnly = Environment.GetEnvironmentVariable("ITS_ONLY_MEDIA") == "1";
            bool foldersOnly = Environment.GetEnvironmentVariable("ITS_ONLY_WIDGET_FOLDERS") == "1";
            bool calendarOnly = Environment.GetEnvironmentVariable("ITS_ONLY_CALENDAR_SETTINGS") == "1";
            bool homePagesOnly = Environment.GetEnvironmentVariable("ITS_ONLY_HOME_PAGES") == "1";
            var languages = cameraOnly ? Environment.GetEnvironmentVariable("ITS_CAMERA_LANGUAGE")?.ToLowerInvariant() switch
            {
                "es" => new[] { Language.Es },
                "en" => new[] { Language.En },
                _ => new[] { Language.En, Language.Es },
            } : mediaOnly ? Environment.GetEnvironmentVariable("ITS_MEDIA_LANGUAGE")?.ToLowerInvariant() switch
            {
                "es" => new[] { Language.Es },
                "en" => new[] { Language.En },
                _ => new[] { Language.En, Language.Es },
            } : clipboardOnly ? Environment.GetEnvironmentVariable("ITS_CLIPBOARD_LANGUAGE")?.ToLowerInvariant() switch
            {
                "es" => new[] { Language.Es },
                "en" => new[] { Language.En },
                _ => new[] { Language.En, Language.Es },
            } : foldersOnly ? Environment.GetEnvironmentVariable("ITS_FOLDER_LANGUAGE")?.ToLowerInvariant() switch
            {
                "es" => new[] { Language.Es },
                "en" => new[] { Language.En },
                _ => new[] { Language.En, Language.Es },
            } : new[] { Language.En, Language.Es };
            foreach (var language in languages)
            {
                L10n.Init(language);
                if (!clipboardOnly && !cameraOnly && !mediaOnly && !homePagesOnly) AuditFavoriteAppIcon(check, language);
                var prefs = new Prefs();
                prefs.LoadDemo();
                if (!clipboardOnly && !mediaOnly) prefs.SetSwitch(PrefKeys.ClipboardHistory, true);
                if (cameraOnly)
                {
                    prefs.SetSwitch(PrefKeys.CameraDock, true);
                    prefs.SetList(PrefKeys.NotchActivities, ["camera"]);
                }
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
                if (homePagesOnly)
                {
                    prefs.SetList(PrefKeys.NotchWidgets, ["clock"]);
                    var calendarReview = NewModel(true);
                    calendarReview.Show(PanelView.Home);
                    var calendarReviewView = new ContentView(calendarReview);
                    var calendarReviewWindow = Host(calendarReviewView);
                    calendarReviewWindow.Show();
                    calendarReviewWindow.UpdateLayout();
                    Pump(() => false, 0.6);
                    Capture(calendarReviewView, language, "calendar-solo-wide");
                    if (calendarReview.HomeWidgets.SingleOrDefault()?.Model is ClockWidget calendarWidget)
                    {
                        calendarWidget.ToggleAdder.Execute(null);
                        calendarReviewWindow.UpdateLayout();
                        Pump(() => false, 0.2);
                        Capture(calendarReviewView, language, "calendar-adder-open");
                    }
                    calendarReviewWindow.Close();
                    calendarReview.Hub.Dispose();
                    prefs.SetList(PrefKeys.NotchWidgets, []);
                    var widgetGalleryReview = NewModel(true);
                    widgetGalleryReview.Show(PanelView.Widgets);
                    widgetGalleryReview.GalleryDots.First(dot => dot.Name == WidgetCatalog.Find("notes")!.Title).Select.Execute(null);
                    widgetGalleryReview.GallerySizes.First(chip => chip.Label == "1×1").Select.Execute(null);
                    var widgetGalleryReviewView = new ContentView(widgetGalleryReview);
                    var widgetGalleryReviewWindow = Host(widgetGalleryReviewView, activate: true);
                    widgetGalleryReviewWindow.Show();
                    widgetGalleryReviewWindow.UpdateLayout();
                    var widgetGalleryReviewPanel = All<Border>(widgetGalleryReviewView).First(border => border.Name == "GalleryPanel");
                    void CaptureWidgetGalleryReview(string state)
                    {
                        widgetGalleryReviewWindow.UpdateLayout();
                        Pump(() => false, 0.3);
                        Capture(widgetGalleryReviewPanel, language, state);
                    }
                    CaptureWidgetGalleryReview("widget-add-home");
                    var galleryAddReview = All<Button>(widgetGalleryReviewPanel).First(button => button.Name == "GalleryAddButton");
                    Keyboard.Focus(galleryAddReview);
                    Pump(() => galleryAddReview.IsKeyboardFocused, 1);
                    CaptureWidgetGalleryReview("widget-add-home-focus");
                    widgetGalleryReview.Prefs.SetList(PrefKeys.NotchWidgets, ["ollama@2x2"]);
                    widgetGalleryReview.Notify();
                    CaptureWidgetGalleryReview("widget-add-page");
                    widgetGalleryReview.Prefs.SetList(PrefKeys.NotchWidgets, ["ollama@2x2", "system@2x2", "clock@2x2"]);
                    widgetGalleryReview.Notify();
                    CaptureWidgetGalleryReview("widget-add-disabled");
                    widgetGalleryReviewWindow.Close();
                    widgetGalleryReview.Hub.Dispose();
                    prefs.SetList(PrefKeys.NotchWidgets, ["ollama", "system", "clock", "battery", "media"]);
                    var pagesHomeOnly = NewModel(true);
                    pagesHomeOnly.Show(PanelView.Home);
                    var pagesViewOnly = new ContentView(pagesHomeOnly);
                    var pagesWindowOnly = Host(pagesViewOnly, activate: true);
                    pagesWindowOnly.Show();
                    pagesWindowOnly.UpdateLayout();
                    Pump(() => false, 0.6);
                    Capture(pagesViewOnly, language, "home-widget-pages");
                    var nextPageOnly = All<Button>(pagesViewOnly).FirstOrDefault(b => AutomationProperties.GetName(b) == pagesHomeOnly.NextPageLabel);
                    if (nextPageOnly is not null) Keyboard.Focus(nextPageOnly);
                    Pump(() => nextPageOnly?.IsKeyboardFocused == true, 1);
                    check($"{language}/Home con páginas: el paginador recibe el foco", nextPageOnly?.IsKeyboardFocused == true);
                    pagesWindowOnly.UpdateLayout();
                    Pump(() => false, 0.2);
                    Capture(pagesViewOnly, language, "home-widget-pages-focus");
                    pagesHomeOnly.HomeDots[2].Pick.Execute(null);
                    pagesWindowOnly.UpdateLayout();
                    Pump(() => false, 0.6);
                    Capture(pagesViewOnly, language, "home-single-widget-page");
                    pagesWindowOnly.Close();
                    pagesHomeOnly.Hub.Dispose();
                    continue;
                }
                if (Environment.GetEnvironmentVariable("ITS_PREVIEW_FOLDER_WINDOW") == "1")
                {
                    var previewModel = NewModel(true);
                    previewModel.Show(PanelView.Widgets);
                    previewModel.BodyMaxHeight = PanelViewModel.BodyMaxFor(640, wide: true);
                    var preview = new ContentView(previewModel);
                    var previewWindow = Host(preview, 700, 680, activate: true);
                    previewWindow.Title = $"isTargetSleeping · carpeta abierta · {language}";
                    previewWindow.ShowInTaskbar = true;
                    previewWindow.Show();
                    previewWindow.UpdateLayout();
                    previewModel.HomeFolders.Tiles.Single(item => item.AutomationId == "folder-home-ai").Open.Execute(null);
                    preview.UpdateLayout();
                    previewWindow.UpdateLayout();
                    if (Environment.GetEnvironmentVariable("ITS_SHOW_NOTCH_FOLDER") == "1") Pump(() => false, 60);
                    previewWindow.Close();
                    previewModel.Hub.Dispose();
                    continue;
                }
                if (Environment.GetEnvironmentVariable("ITS_ONLY_NOTCH_FOLDER") == "1")
                {
                    var notchModel = NewModel(true);
                    notchModel.Show(PanelView.Widgets);
                    var notch = new NotchWindow(notchModel, new ContentView(notchModel))
                    {
                        Title = $"isTargetSleeping · carpeta abierta · {language}",
                        ShowInTaskbar = true,
                    };
                    notch.Start();
                    notch.Reveal();
                    notchModel.Show(PanelView.Widgets);
                    Pump(() => notch.ActualWidth > 200 && notch.ActualHeight > 200, 2);
                    var tile = notchModel.HomeFolders.Tiles.Single(item => item.AutomationId == "folder-home-ai");
                    Border? folderSurface = null;
                    var folderSizes = new List<Size>();
                    EventHandler sampleFolderSize = (_, _) =>
                    {
                        folderSurface ??= All<Border>((DependencyObject)notch.Content).FirstOrDefault(border => border.CornerRadius == new CornerRadius(48));
                        if (folderSurface is { IsVisible: true, ActualWidth: > 0, ActualHeight: > 0 } surface)
                            folderSizes.Add(new Size(surface.ActualWidth, surface.ActualHeight));
                    };
                    System.Windows.Media.CompositionTarget.Rendering += sampleFolderSize;
                    tile.Open.Execute(null);
                    notch.UpdateLayout();
                    Pump(() => false, 0.2);
                    var openedSize = new Size(notch.ActualWidth, notch.ActualHeight);
                    bool folderVisible = All<ContentControl>((DependencyObject)notch.Content).Any(control => control.Content is FolderOverlay);
                    notchModel.Notify();
                    Pump(() => false, 1.3);
                    System.Windows.Media.CompositionTarget.Rendering -= sampleFolderSize;
                    notch.UpdateLayout();
                    var settledSize = new Size(notch.ActualWidth, notch.ActualHeight);
                    bool folderSizeStable = folderSizes.Count > 10
                        && folderSizes.Max(size => size.Width) - folderSizes.Min(size => size.Width) < 0.5
                        && folderSizes.Max(size => size.Height) - folderSizes.Min(size => size.Height) < 0.5;
                    if (!folderSizeStable && folderSizes.Count > 0)
                        Console.WriteLine($"   tamaño de carpeta observado: {folderSizes[0].Width:0.0}×{folderSizes[0].Height:0.0} → {folderSizes[^1].Width:0.0}×{folderSizes[^1].Height:0.0}");
                    check($"{language}/notch: carpeta visible y tamaño estable tras actualizar el panel",
                        folderVisible && openedSize.Width > 200 && openedSize.Height > 200
                        && Math.Abs(settledSize.Width - openedSize.Width) < 0.5
                        && Math.Abs(settledSize.Height - openedSize.Height) < 0.5 && folderSizeStable);
                    if (Environment.GetEnvironmentVariable("ITS_SHOW_NOTCH_FOLDER") == "1") Pump(() => false, 25);
                    notch.Stop();
                    notch.Close();
                    notchModel.Hub.Dispose();
                    continue;
                }
                if (calendarOnly)
                {
                    var widgetsModel = NewModel(true);
                    widgetsModel.Show(PanelView.Widgets);
                    var widgets = new ContentView(widgetsModel);
                    var widgetsWindow = Host(widgets, 700, 680);
                    widgetsWindow.Show();
                    widgetsWindow.UpdateLayout();
                    check($"{language}: el archivo .ics ya no aparece en Widgets",
                        !All<TextBlock>(widgets).Any(text => text.IsVisible && text.Text == widgetsModel.Hub.ClockInfo.IcsTitle));
                    Capture(widgets, language, "calendar-removed-from-widgets");
                    widgetsWindow.Close();
                    widgetsModel.Hub.Dispose();

                    var settingsModel = NewModel(true);
                    settingsModel.Show(PanelView.Settings);
                    settingsModel.CurrentSettingsTab = PanelViewModel.SettingsTab.Notch;
                    var settings = new ContentView(settingsModel);
                    var settingsWindow = Host(settings, 700, 900);
                    settingsWindow.Show();
                    settingsWindow.UpdateLayout();
                    var calendarCard = All<Border>(settings).SingleOrDefault(border => border.Name == "CalendarFileSettingsCard");
                    check($"{language}: el archivo .ics no aparece en los ajustes de Notch", calendarCard is { IsVisible: false });
                    settingsModel.CurrentSettingsTab = PanelViewModel.SettingsTab.General;
                    settings.UpdateLayout();
                    check($"{language}: el archivo .ics se configura en General", calendarCard is { IsVisible: true }
                        && ReferenceEquals(calendarCard.DataContext, settingsModel.Hub.ClockInfo)
                        && All<TextBlock>(calendarCard).Any(text => text.IsVisible && text.Text == settingsModel.Hub.ClockInfo.IcsTitle)
                        && All<TextBlock>(calendarCard).Any(text => text.IsVisible && text.Text == settingsModel.Hub.ClockInfo.IcsText)
                        && All<Button>(calendarCard).Any(button => button.IsVisible && ReferenceEquals(button.Command, settingsModel.Hub.ClockInfo.PickIcs))
                        && All<Button>(calendarCard).Any(button => ReferenceEquals(button.Command, settingsModel.Hub.ClockInfo.ClearIcs)
                            && button.IsVisible == settingsModel.Hub.ClockInfo.HasIcs));
                    if (calendarCard is not null) Capture(calendarCard, language, "calendar-file-setting");
                    Capture(settings, language, "calendar-settings-general");
                    settingsWindow.Close();
                    settingsModel.Hub.Dispose();
                    continue;
                }
                void Audit(string name, DependencyObject content, bool wide)
                {
                    if (Environment.GetEnvironmentVariable("ITS_ONLY_GALLERY") == "1" && name.StartsWith("Widgets", StringComparison.Ordinal))
                    {
                        if (wide && content is FrameworkElement target)
                        {
                            var galleryBad = Offenders(target);
                            var galleryModel = target.DataContext as PanelViewModel;
                            foreach (var line in galleryBad.Take(6)) Console.WriteLine($"   texto ilegible en {name}: {line}");
                            check($"{language}/{name}: el texto de la galería se lee", galleryBad.Count == 0);
                            if (name == "Widgets usage 1×1 700")
                            {
                                var problem = L10n.tr("La sesión caducó: abre %@ para renovarla.", "Claude Code");
                                var row = new UsageProviderRow("claude", "Claude Code", "", "", [], null, problem);
                                foreach (var (key, label) in new[] { ("UsageGlanceLine", "line"), ("UsageGlanceTile", "tile") })
                                {
                                    var sample = new ContentControl { Content = row, ContentTemplate = (DataTemplate)Application.Current.FindResource(key), Width = 210 };
                                    var window = Host(sample, 250, 100);
                                    window.Show();
                                    window.UpdateLayout();
                                    var details = All<TextBlock>(sample).Where(text => text.Text == problem && text.ActualWidth > 0 && text.ActualHeight > 0).ToArray();
                                    bool readable = details.Any(detail => detail.Visibility == Visibility.Visible && detail.TextWrapping == TextWrapping.Wrap
                                        && detail.TextTrimming == TextTrimming.None && detail.ActualWidth > 100 && detail.ActualHeight > 12);
                                    check($"{language}/Uso de IA {label}: el error de sesión completo se envuelve y se lee", readable);
                                    Capture(sample, language, $"usage-session-expired-{label}");
                                    window.Close();
                                }
                            }
                            if (name.Contains("Widgets media", StringComparison.Ordinal) && galleryModel?.GalleryPreview?.Model is MediaWidget media)
                                check($"{language}/{name}: la galería muestra el jazz original pausado con portada", media.IsFallbackPreview && !media.IsPlaying
                                    && media.MediaTitle == L10n.tr("Una noche tranquila en Basin Street")
                                    && media.Artist == L10n.tr("Jazz lento de Nueva Orleans")
                                    && media.Art is BitmapImage { PixelWidth: 512, PixelHeight: 512 });
                            var suffix = "gallery-" + name.Replace("Widgets ", "").Replace("×", "x").Replace(" ", "-").Replace(".", "");
                            check($"{language}/{name}: no se renderizan capas de nubes",
                                !All<Canvas>(target).Any(g => g.Name == "GalleryCloudLayer")
                                && !All<Border>(target).Any(b => b.Name is "GalleryLeftEdgeCloud" or "GalleryRightEdgeCloud"));
                            if (All<Border>(target).FirstOrDefault(b => b.Name == "GalleryPanel") is { } galleryPanel) Capture(galleryPanel, language, suffix);
                            if (All<Border>(target).FirstOrDefault(b => b.Name == "GalleryPreviewFrame") is { } galleryFrame) Capture(galleryFrame, language, suffix + "-center");
                            var galleryFrames = new[] { "GalleryPreviousFrame", "GalleryPreviewFrame", "GalleryNextFrame" }
                                .Select(frameName => All<Border>(target).FirstOrDefault(b => b.Name == frameName)).ToArray();
                            bool sameGalleryFill = galleryFrames.All(frame => frame is not null)
                                && ReferenceEquals(galleryFrames[0]!.Background, galleryFrames[1]!.Background)
                                && ReferenceEquals(galleryFrames[1]!.Background, galleryFrames[2]!.Background);
                            check($"{language}/{name}: las tres tarjetas comparten el mismo fondo", sameGalleryFill);
                            var sideFrames = All<Border>(target).Where(b => b.Name is "GalleryPreviousFrame" or "GalleryNextFrame").ToArray();
                            check($"{language}/{name}: laterales sin foco ni interacción", sideFrames.Length == 2 && sideFrames.All(b => !b.Focusable && !b.IsEnabled && !b.IsHitTestVisible)
                                && sideFrames.SelectMany(All<Button>).All(b => !b.IsEnabled && !b.IsHitTestVisible));
                            check($"{language}/{name}: los dos widgets vecinos caben completos y separados",
                                galleryModel?.GalleryPreviousCard is { } previousCard && galleryModel.GalleryNextCard is { } nextCard
                                && previousCard.Width > 0 && previousCard.Height > 0 && nextCard.Width > 0 && nextCard.Height > 0
                                && previousCard.Width <= galleryModel.GallerySidePreviewWidth + 0.01
                                && nextCard.Width <= galleryModel.GallerySidePreviewWidth + 0.01
                                && previousCard.Left >= -0.01
                                && previousCard.Left + previousCard.Width <= galleryModel.GalleryCarouselCenterLeft - galleryModel.GallerySideGap + 0.01
                                && nextCard.Left >= galleryModel.GalleryCarouselCenterLeft + galleryModel.GalleryWidth + galleryModel.GallerySideGap - 0.01
                                && nextCard.Left + nextCard.Width <= galleryModel.GalleryCarouselWidth + 0.01);
                            if (All<Button>(target).FirstOrDefault(b => b.Name == "GalleryAddButton") is { } addButton && addButton.DataContext is PanelViewModel model)
                            {
                                addButton.ApplyTemplate();
                                var pill = addButton.Template?.FindName("pill", addButton) as Border;
                                var label = addButton.Template?.FindName("addLabel", addButton) as TextBlock;
                                var iconCircle = addButton.Template?.FindName("plusBackground", addButton) as Ellipse;
                                var plusHorizontal = addButton.Template?.FindName("plusHorizontal", addButton) as Rectangle;
                                var plusVertical = addButton.Template?.FindName("plusVertical", addButton) as Rectangle;
                                bool iconCenteredAndEmphasized = iconCircle is not null && plusHorizontal is not null && plusVertical is not null
                                    && plusHorizontal.Width == 10 && plusHorizontal.Height == 2 && plusHorizontal.RadiusX == 1
                                    && plusVertical.Width == 2 && plusVertical.Height == 10 && plusVertical.RadiusY == 1
                                    && plusHorizontal.HorizontalAlignment == HorizontalAlignment.Center && plusHorizontal.VerticalAlignment == VerticalAlignment.Center
                                    && plusVertical.HorizontalAlignment == HorizontalAlignment.Center && plusVertical.VerticalAlignment == VerticalAlignment.Center
                                    && (plusVertical.Visibility == Visibility.Collapsed) == model.GalleryAlreadyAdded;
                                check($"{language}/{name}: el más vectorial es grueso, centrado y cambia a menos al quitar", iconCenteredAndEmphasized);
                                string background = model.GalleryUsesNoteAccent ? "NoteBand" : "Bg";
                                string foreground = model.GalleryUsesNoteAccent ? "OnNote" : "Fg";
                                check($"{language}/{name}: el amarillo queda reservado para Notas",
                                    pill is not null && label is not null
                                    && Equals(pill.Background, Application.Current.Resources[background])
                                    && Equals(label.Foreground, Application.Current.Resources[foreground]));
                            }
                            if (name.Contains("disabled", StringComparison.Ordinal))
                            {
                                var add = All<Button>(target).FirstOrDefault(b => b.Name == "GalleryAddButton");
                                check($"{language}/{name}: botón principal deshabilitado visible", add is { IsEnabled: false });
                            }
                            if (name.Contains("code 1×1", StringComparison.Ordinal) && galleryModel?.GalleryPreview?.Model is CodeWidget code)
                            {
                                string expected = language == Language.En ? "A small sample app." : "Una app pequeña de ejemplo.";
                                check($"{language}/{name}: el resumen local detecta el stack y lee el README",
                                    code.ProjectStack.Contains(".NET", StringComparison.Ordinal) && code.ProjectSummary == expected);
                            }
                            if (name.Contains("teleprompter 1×1", StringComparison.Ordinal) && galleryModel?.GalleryPreview?.Model is TeleprompterWidget teleprompter
                                && All<TeleprompterView>(target).FirstOrDefault() is { } reader)
                            {
                                double startProgress = reader.Progress;
                                var frameProgress = new List<double>();
                                EventHandler sampleFrame = (_, _) => frameProgress.Add(reader.Progress);
                                CompositionTarget.Rendering += sampleFrame;
                                Pump(() => frameProgress.Count >= 8, 1);
                                CompositionTarget.Rendering -= sampleFrame;
                                bool scrollProgresses = frameProgress.Count >= 8 && (teleprompter.ReducedMotion
                                    ? frameProgress.All(value => Math.Abs(value - startProgress) < 0.001)
                                    : frameProgress[^1] > startProgress + 0.001);
                                check($"{language}/{name}: el avance del teleprompter sigue el movimiento del texto", scrollProgresses);
                                int oldReset = teleprompter.ResetRequest;
                                teleprompter.Restart.Execute(null);
                                target.UpdateLayout();
                                bool restarted = reader.ResetRequest == oldReset + 1;
                                teleprompter.ToggleEdit.Execute(null);
                                target.UpdateLayout();
                                var editor = All<TextBox>(target).FirstOrDefault(box => box.IsVisible && AutomationProperties.GetName(box) == teleprompter.Title);
                                bool editing = teleprompter.Editing && !teleprompter.Running && reader.Visibility == Visibility.Collapsed && editor?.IsVisible == true;
                                if (editing && All<Border>(target).FirstOrDefault(b => b.Name == "GalleryPreviewFrame") is { } editFrame) Capture(editFrame, language, "gallery-teleprompter-edit");
                                teleprompter.Toggle.Execute(null);
                                target.UpdateLayout();
                                bool resumed = !teleprompter.Editing && teleprompter.Running && reader.Visibility == Visibility.Visible;
                                string script = teleprompter.Text;
                                teleprompter.Text = "";
                                target.UpdateLayout();
                                bool emptyState = All<TextBlock>(target).Any(t => t.Text == teleprompter.EmptyHint && t.IsVisible);
                                if (All<Border>(target).FirstOrDefault(b => b.Name == "GalleryPreviewFrame") is { } emptyFrame) Capture(emptyFrame, language, "gallery-teleprompter-empty");
                                teleprompter.Text = script;
                                check($"{language}/{name}: reinicia, edita, reanuda y muestra el estado vacío", restarted && editing && resumed && emptyState);
                                check($"{language}/{name}: botones de reproducción, edición y reinicio tienen nombre",
                                    All<Button>(target).Where(b => b.IsVisible).All(b => AutomationProperties.GetName(b).Length > 0));
                            }
                            if (name.Contains("awake 1×1", StringComparison.Ordinal) && galleryModel is not null)
                            {
                                var awake = galleryModel.Hub.Awake;
                                try
                                {
                                    awake.Toggle.Execute(null);
                                    target.UpdateLayout();
                                    bool active = awake.On && !awake.Failed && awake.StateTitle == (language == Language.En ? "PC awake" : "PC despierto");
                                    if (active && All<Border>(target).FirstOrDefault(b => b.Name == "GalleryPanel") is { } awakePanel)
                                        Capture(awakePanel, language, "gallery-awake-on");
                                    check($"{language}/{name}: el interruptor confirma el estado de Windows y nombra la acción siguiente",
                                        active && All<BrandSwitch>(target).Any(s => AutomationProperties.GetName(s) == awake.ToggleHelp));
                                }
                                finally { if (awake.On) awake.Toggle.Execute(null); }
                            }
                        }
                        return;
                    }
                    var bad = Offenders(content);
                    foreach (var line in bad.Take(6)) Console.WriteLine($"   texto ilegible en {name}: {line}");
                    check($"{language}/{name}/{(wide ? "ancho" : "estrecho")}: todo el texto se lee (contraste ≥ {MinRatio}:1)", bad.Count == 0);
                    if (name == "Home con notes" && wide && content is FrameworkElement visual)
                    {
                        var prompt = language == Language.En ? "Write here" : "Escribe aquí";
                        check($"{language}/Notas vacías: el mensaje para empezar está visible", All<TextBlock>(visual).Any(t => t.Text == prompt));
                        Capture(visual, language, "notes");
                    }
                    if (name == "Widgets" && wide && content is FrameworkElement gallery
                        && All<Border>(gallery).FirstOrDefault(b => b.Name == "GalleryPanel") is { } frame) Capture(frame, language, "gallery");
                }
                void Page(string name, bool wide, Action<PanelViewModel> setup, PanelView view, double hostWidth = 700, bool interactiveGallery = false)
                {
                    var model = NewModel(wide);
                    setup(model);
                    model.Show(view);
                    var content = new ContentView(model);
                    var window = Host(content, hostWidth, activate: interactiveGallery);
                    window.Show();
                    window.UpdateLayout();
                    Audit(name, content, wide);
                    if (interactiveGallery && All<Button>(content).FirstOrDefault(b => b.Name == "GalleryAddButton") is { IsEnabled: true } add
                        && All<Border>(content).FirstOrDefault(b => b.Name == "GalleryPanel") is { } panel)
                    {
                        window.Activate();
                        Keyboard.Focus(add);
                        Pump(() => add.IsKeyboardFocused, 1);
                        bool focused = add.IsKeyboardFocused;
                        check($"{language}/galería: botón recibe foco de teclado", focused);
                        if (focused) Capture(panel, language, "gallery-focus");

                        var viewport = All<Viewport3D>(content).First(v => v.Name == "GalleryMotionViewport");
                        var viewportHost = All<Grid>(content).First(grid => grid.Name == "GalleryViewportHost");
                        var previousSide = All<Border>(content).First(border => border.Name == "GalleryPreviousFrame");
                        var centerSide = All<Border>(content).First(border => border.Name == "GalleryPreviewFrame");
                        var nextSide = All<Border>(content).First(border => border.Name == "GalleryNextFrame");
                        bool SideCardsFit()
                        {
                            content.UpdateLayout();
                            Rect Bounds(Border frame) => frame.TransformToAncestor(viewportHost).TransformBounds(new Rect(new Point(), frame.RenderSize));
                            var previousBounds = Bounds(previousSide);
                            var centerBounds = Bounds(centerSide);
                            var nextBounds = Bounds(nextSide);
                            return previousBounds.Left >= -0.01 && previousBounds.Right <= viewportHost.ActualWidth + 0.01
                                && previousBounds.Right + model.GallerySideGap <= centerBounds.Left + 0.01
                                && nextBounds.Left >= centerBounds.Right + model.GallerySideGap - 0.01
                                && nextBounds.Right <= viewportHost.ActualWidth + 0.01;
                        }
                        model.GallerySizes.First(chip => chip.Label == "2×2").Select.Execute(null);
                        bool largeCardsFit = SideCardsFit();
                        model.GallerySizes.First(chip => chip.Label == "1×1").Select.Execute(null);
                        bool smallCardsFit = SideCardsFit();
                        check($"{language}/galería: los vecinos siguen completos al cambiar el tamaño central", largeCardsFit && smallCardsFit);
                        var originalFrames = new HashSet<Border>
                        {
                            All<Border>(content).First(b => b.Name == "GalleryPreviousFrame"),
                            All<Border>(content).First(b => b.Name == "GalleryPreviewFrame"),
                            All<Border>(content).First(b => b.Name == "GalleryNextFrame"),
                        };
                        var galleryAddButton = All<Button>(content).First(b => b.Name == "GalleryAddButton");
                        galleryAddButton.ApplyTemplate();
                        var galleryWidgetTitle = All<TextBlock>(content).First(t => t.Name == "GalleryWidgetTitle");
                        var galleryWidgetDescription = All<TextBlock>(content).First(t => t.Name == "GalleryWidgetDescription");
                        var galleryAddContent = (StackPanel)galleryAddButton.Template.FindName("GalleryAddButtonContent", galleryAddButton);
                        var galleryTitleShift = (TranslateTransform)galleryWidgetTitle.RenderTransform;
                        var galleryDescriptionShift = (TranslateTransform)galleryWidgetDescription.RenderTransform;
                        var galleryAddShift = (TranslateTransform)galleryAddContent.RenderTransform;
                        var original3DObjects = viewport.Children.OfType<Viewport2DVisual3D>().ToHashSet();
                        bool persistentScene = viewport.Visibility == Visibility.Visible && original3DObjects.Count == 3
                            && original3DObjects.Select(card => card.Visual).OfType<Border>().ToHashSet().SetEquals(originalFrames);
                        check($"{language}/galería: reposo y giro comparten las tres superficies 3D", persistentScene);
                        bool stableRestScale = original3DObjects.All(card =>
                        {
                            var group = card.Transform as Transform3DGroup;
                            var rotation = group?.Children.OfType<RotateTransform3D>().SingleOrDefault();
                            var scale = group?.Children.OfType<ScaleTransform3D>().SingleOrDefault();
                            if (rotation?.Rotation is not AxisAngleRotation3D axis || scale is null) return false;
                            double depth = Math.Abs(rotation.CenterZ) * (1 - Math.Cos(axis.Angle * Math.PI / 180));
                            double apparentHeight = scale.ScaleY * 4.5 / (4.5 + depth);
                            double expected = ReferenceEquals(card.Visual, All<Border>(content).First(b => b.Name == "GalleryPreviewFrame")) ? 1 : 0.92;
                            return Math.Abs(apparentHeight - expected) < 0.02;
                        });
                        check($"{language}/galería: la perspectiva de reposo no introduce zoom vertical", stableRestScale);
                        double initialHostWidth = viewportHost.ActualWidth, initialHostHeight = viewportHost.ActualHeight;
                        string previousTitle = model.GalleryTitle;
                        var outgoingCenter = All<Border>(content).First(b => b.Name == "GalleryPreviewFrame");
                        bool animationsEnabled = SystemParameters.ClientAreaAnimation;
                        model.GalleryNext.Execute(null);
                        double motionWidth = viewport.ActualWidth, motionHeight = viewport.ActualHeight;
                        var movingCards = viewport.Children.OfType<Viewport2DVisual3D>().ToArray();
                        bool sameCardInstances = movingCards.Length == 3 && movingCards.Select(card => card.Visual).OfType<Border>().ToHashSet().SetEquals(originalFrames);
                        var cardTransforms = movingCards
                            .Where(v => v.Transform is Transform3DGroup)
                            .Select(v => ((Transform3DGroup)v.Transform).Children.OfType<RotateTransform3D>().Single()).ToArray();
                        var rotations = cardTransforms.Select(transform => transform.Rotation).OfType<AxisAngleRotation3D>().ToArray();
                        var incoming = rotations.FirstOrDefault(rotation => rotation.Angle > 10);
                        var previewFrame = All<Border>(content).First(b => b.Name == "GalleryPreviewFrame");
                        var previewContent = All<ContentControl>(previewFrame).FirstOrDefault();
                        bool liveCardContent = previewContent is { Content: WidgetSlot }
                            && System.Windows.Data.BindingOperations.IsDataBound(previewContent, ContentControl.ContentProperty);
                        check($"{language}/galería: cada tarjeta conserva su contenido enlazado durante el giro", liveCardContent);
                        var copySamples = new List<(string Title, string Selected, double TitleOpacity, double DescriptionOpacity, double ActionOpacity,
                            double TitleY, double DescriptionY, double ActionY)>();
                        EventHandler sampleCopy = (_, _) => copySamples.Add((galleryWidgetTitle.Text, model.GalleryTitle,
                            galleryWidgetTitle.Opacity, galleryWidgetDescription.Opacity, galleryAddContent.Opacity,
                            galleryTitleShift.Y, galleryDescriptionShift.Y, galleryAddShift.Y));
                        CompositionTarget.Rendering += sampleCopy;
                        double cameraDistance = ((PerspectiveCamera)viewport.Camera).Position.Z;
                        double cameraWidth = 2 * cameraDistance * Math.Tan(((PerspectiveCamera)viewport.Camera).FieldOfView * Math.PI / 360);
                        var centerMesh = movingCards.FirstOrDefault(card => ReferenceEquals(card.Visual, previewFrame))?.Geometry as MeshGeometry3D;
                        bool cardsFaceCamera = movingCards.All(card => card.Geometry is MeshGeometry3D mesh
                            && Enumerable.Range(0, mesh.TriangleIndices.Count / 3).All(triangle =>
                            {
                                var first = mesh.Positions[mesh.TriangleIndices[triangle * 3]];
                                var second = mesh.Positions[mesh.TriangleIndices[triangle * 3 + 1]];
                                var third = mesh.Positions[mesh.TriangleIndices[triangle * 3 + 2]];
                                return Vector3D.CrossProduct(second - first, third - first).Z > 0;
                            }));
                        check($"{language}/galería: la cara visible de cada tarjeta apunta a la cámara", cardsFaceCamera);
                        double projectedCenterWidth = centerMesh is null ? double.NaN :
                            (centerMesh.Positions.Max(point => point.X) - centerMesh.Positions.Min(point => point.X)) * viewport.ActualWidth / cameraWidth;
                        bool motionStarted = animationsEnabled
                            ? !viewport.IsHitTestVisible && viewport.Visibility == Visibility.Visible && rotations.Length == 3 && sameCardInstances
                                && rotations.All(rotation => rotation.HasAnimatedProperties) && cardTransforms.All(transform => transform.CenterZ < 0)
                                && incoming is not null && model.GalleryTitle == previousTitle
                            : model.GalleryTitle != previousTitle && viewport.IsHitTestVisible;
                        check($"{language}/galería: las tres tarjetas giran en perspectiva antes de cambiar el título", motionStarted);
                        bool projectionFits = !animationsEnabled || double.IsFinite(projectedCenterWidth) && Math.Abs(projectedCenterWidth - previewFrame.ActualWidth) < 1;
                        check($"{language}/galería: la tarjeta central conserva su tamaño al proyectarse en 3D ({projectedCenterWidth:0.0}/{previewFrame.ActualWidth:0.0} DIP)", projectionFits);
                        if (animationsEnabled && motionStarted && incoming is not null)
                        {
                            var incomingVisual = movingCards.First(card => card.Transform is Transform3DGroup group
                                && group.Children.OfType<RotateTransform3D>().Single().Rotation == incoming).Visual;
                            var incomingScale = ((Transform3DGroup)movingCards.First(card => ReferenceEquals(card.Visual, incomingVisual)).Transform)
                                .Children.OfType<ScaleTransform3D>().Single();
                            var incomingOrbit = ((Transform3DGroup)movingCards.First(card => ReferenceEquals(card.Visual, incomingVisual)).Transform)
                                .Children.OfType<RotateTransform3D>().Single();
                            double incomingDepth = Math.Abs(incomingOrbit.CenterZ) * (1 - Math.Cos(incoming.Angle * Math.PI / 180));
                            double startHeightScale = incomingScale.ScaleY * cameraDistance / (cameraDistance + incomingDepth);
                            bool startsWithoutZoom = startHeightScale is >= 0.90 and <= 0.95;
                            check($"{language}/galería: la tarjeta entrante inicia a su escala lateral, sin salto", startsWithoutZoom);
                            double incomingStart = incoming.Angle;
                            Pump(() => incoming.Angle < incomingStart - 12 && incoming.Angle > 1, 1.5);
                            bool sideTravelsToCenter = incoming.Angle < incomingStart - 12 && incoming.Angle > 1 && model.GalleryTitle == previousTitle;
                            check($"{language}/galería: la vista lateral entra físicamente al centro sin cambiar antes el widget (ángulo {incoming.Angle:0.0}/{incomingStart:0.0})", sideTravelsToCenter);
                            double movingDepth = Math.Abs(incomingOrbit.CenterZ) * (1 - Math.Cos(incoming.Angle * Math.PI / 180));
                            double movingHeightScale = incomingScale.ScaleY * cameraDistance / (cameraDistance + movingDepth);
                            check($"{language}/galería: la tarjeta conserva casi su altura al entrar ({movingHeightScale:0.00})",
                                movingHeightScale is >= 0.90 and <= 1.02);
                            bool stableSize = viewport.Visibility == Visibility.Visible
                                && Math.Abs(viewport.ActualWidth - motionWidth) < 0.1 && Math.Abs(viewport.ActualHeight - motionHeight) < 0.1
                                && Math.Abs(viewportHost.ActualWidth - initialHostWidth) < 0.1 && Math.Abs(viewportHost.ActualHeight - initialHostHeight) < 0.1;
                            check($"{language}/galería: el área 3D conserva su tamaño durante el giro", stableSize);
                        }
                        Pump(() => model.GalleryTitle != previousTitle, 2);
                        bool changedWidget = model.GalleryTitle != previousTitle;
                        check($"{language}/galería: el título cambia al terminar el giro", changedWidget);
                        Pump(() => viewport.IsHitTestVisible && galleryWidgetTitle.Opacity >= 0.999
                            && galleryWidgetDescription.Opacity >= 0.999 && galleryAddContent.Opacity >= 0.999, 2);
                        CompositionTarget.Rendering -= sampleCopy;
                        bool copyExitsWithCarousel = copySamples.Any(s => s.Title == previousTitle && s.Selected == previousTitle
                            && s.TitleOpacity is > 0 and < 0.99 && s.DescriptionOpacity < 0.99 && s.ActionOpacity < 0.99
                            && s.TitleY < 0 && s.DescriptionY < 0 && s.ActionY > 0);
                        bool copyEntersWithNewWidget = copySamples.Any(s => s.Title == s.Selected && s.Selected != previousTitle
                            && s.TitleOpacity < 0.99 && s.DescriptionOpacity < 0.99 && s.ActionOpacity < 0.99
                            && s.TitleY > 0 && s.DescriptionY > 0 && s.ActionY > 0);
                        check($"{language}/galería: título, descripción y botón salen al final del giro", copyExitsWithCarousel);
                        check($"{language}/galería: el texto nuevo y la acción entran con movimiento sutil", copyEntersWithNewWidget);
                        bool restoredSize = Math.Abs(viewportHost.ActualWidth - model.GalleryCarouselWidth) < 0.1 && Math.Abs(viewportHost.ActualHeight - model.GalleryHeight) < 0.1;
                        var final3DObjects = viewport.Children.OfType<Viewport2DVisual3D>().ToHashSet();
                        bool sameFramesRestored = final3DObjects.SetEquals(original3DObjects)
                            && final3DObjects.Select(card => card.Visual).OfType<Border>().ToHashSet().SetEquals(originalFrames);
                        bool outgoingSizeMatchesSide = !animationsEnabled || model.GalleryPreviousCard is { } outgoingSide
                            && Math.Abs(outgoingCenter.ActualWidth - outgoingSide.Width) < 1
                            && Math.Abs(outgoingCenter.ActualHeight - outgoingSide.Height) < 1;
                        check($"{language}/galería: termina el giro, restaura el tamaño y devuelve la interacción",
                            viewport.IsHitTestVisible && viewport.Visibility == Visibility.Visible && restoredSize && sameFramesRestored && outgoingSizeMatchesSide
                            && galleryWidgetTitle.Opacity >= 0.999 && galleryWidgetDescription.Opacity >= 0.999 && galleryAddContent.Opacity >= 0.999);

                        var directDot = model.GalleryDots.LastOrDefault(dot => dot.Name != model.GalleryTitle
                            && Math.Abs(model.GalleryDots.FindIndex(candidate => candidate.Name == dot.Name)
                                - model.GalleryDots.FindIndex(candidate => candidate.Name == model.GalleryTitle)) > 1);
                        if (animationsEnabled && directDot is not null)
                        {
                            string directPreviousTitle = model.GalleryTitle;
                            var directCopySamples = new List<(string Title, string Selected, double TitleOpacity, double DescriptionOpacity, double ActionOpacity,
                                double TitleY, double DescriptionY, double ActionY)>();
                            EventHandler sampleDirectCopy = (_, _) => directCopySamples.Add((galleryWidgetTitle.Text, model.GalleryTitle,
                                galleryWidgetTitle.Opacity, galleryWidgetDescription.Opacity, galleryAddContent.Opacity,
                                galleryTitleShift.Y, galleryDescriptionShift.Y, galleryAddShift.Y));
                            CompositionTarget.Rendering += sampleDirectCopy;
                            directDot.Select.Execute(null);
                            Pump(() => galleryWidgetTitle.Opacity >= 0.999 && galleryWidgetDescription.Opacity >= 0.999
                                && galleryAddContent.Opacity >= 0.999 && viewport.IsHitTestVisible, 2);
                            CompositionTarget.Rendering -= sampleDirectCopy;
                            bool directCopyExits = directCopySamples.Any(s => s.Title == directPreviousTitle && s.Selected == directPreviousTitle
                                && s.TitleOpacity is > 0 and < 0.99 && s.DescriptionOpacity < 0.99 && s.ActionOpacity < 0.99
                                && s.TitleY < 0 && s.DescriptionY < 0 && s.ActionY > 0);
                            bool directCopyEnters = directCopySamples.Any(s => s.Title == directDot.Name && s.Selected == directDot.Name
                                && s.TitleOpacity < 0.99 && s.DescriptionOpacity < 0.99 && s.ActionOpacity < 0.99
                                && s.TitleY > 0 && s.DescriptionY > 0 && s.ActionY > 0);
                            bool directCopySettled = galleryWidgetTitle.Text == directDot.Name && galleryWidgetTitle.Opacity >= 0.999
                                && galleryWidgetDescription.Opacity >= 0.999 && galleryAddContent.Opacity >= 0.999 && viewport.IsHitTestVisible;
                            check($"{language}/galería: los puntos despiden el texto anterior", directCopyExits);
                            check($"{language}/galería: los puntos revelan el título y la acción nuevos", directCopyEnters);
                            check($"{language}/galería: los puntos terminan la entrada del texto y el botón", directCopySettled);
                        }
                    }
                    window.Close();
                }

                if (cameraOnly)
                {
                    var cometExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Perplexity", "Comet", "Application", "comet.exe");
                    check($"{language}: solo abre ajustes internos con el ejecutable de Comet conocido",
                        new CameraApplication("Comet", cometExe, _ => { }).IsComet
                        && !new CameraApplication("Comet", Path.Combine(Path.GetDirectoryName(cometExe)!, "otro", "comet.exe"), _ => { }).IsComet);
                    var cometPermissions = new CameraApplication("Comet", cometExe, _ => { });
                    var otherDesktopPermissions = new CameraApplication("Otra app", Path.Combine(Path.GetTempPath(), "otra-app.exe"), _ => { });
                    check($"{language}: los permisos por sitio de Comet se distinguen del ajuste global de Windows",
                        cometPermissions.PermissionActionLabel == (language == Language.En ? "Manage" : "Administrar")
                        && cometPermissions.PermissionSettingsUri == "chrome://settings/content/siteDetails?site=https%3A%2F%2Fdiscord.com"
                        && otherDesktopPermissions.PermissionSettingsUri == "ms-settings:privacy-webcam"
                        && cometPermissions.PermissionHelp.Contains("Discord Web", StringComparison.Ordinal)
                        && otherDesktopPermissions.PermissionHelp.Contains("global", StringComparison.OrdinalIgnoreCase));
                    var model = NewModel(true);
                    model.BodyMaxHeight = 440;
                    model.Show(PanelView.Camera);
                    var content = new ContentView(model);
                    bool showCamera = Environment.GetEnvironmentVariable("ITS_SHOW_CAMERA") == "1";
                    var window = Host(content, 700, 500, activate: showCamera);
                    model.Camera.StartMonitoring();
                    window.ShowInTaskbar = showCamera;
                    window.Title = $"isTargetSleeping · Camera audit · {language}";
                    window.Show();
                    window.UpdateLayout();
                    Pump(() => model.Camera.ActiveApplications.Count > 0 || !model.Camera.IsInUse, 2);
                    window.UpdateLayout();
                    var previewFrame = All<Border>(content).First(b => b.Name == "CameraPreviewFrame");
                    var cameraAppsCard = All<Border>(content).First(b => b.Name == "CameraAppsCard");
                    var bodyScroll = All<ScrollViewer>(content).First(s => s.Name == "BodyScroll");
                    var previewImage = All<Image>(previewFrame).FirstOrDefault();
                    var previewSwitch = All<BrandSwitch>(content).FirstOrDefault(s => ReferenceEquals(s.Command, model.ToggleCameraPreview));
                    check($"{language}: abrir la página de cámara en el modo de prueba no activa el hardware", !model.Camera.IsPreviewing && !model.Camera.HasFrame);
                    check($"{language}: el interruptor de vista previa aparece apagado, tiene nombre y controla la cámara", previewSwitch is { IsOn: false }
                        && AutomationProperties.GetName(previewSwitch) == model.CameraPreviewToggleLabel
                        && ReferenceEquals(previewSwitch.Command, model.ToggleCameraPreview));
                    check($"{language}: la vista previa corrige el efecto espejo de la cámara",
                        previewImage?.RenderTransform is System.Windows.Media.ScaleTransform flip && flip.ScaleX == -1
                        && previewImage.RenderTransformOrigin == new Point(0.5, 0.5));
                    check($"{language}: la página muestra el estado, las condiciones de privacidad y el área de vista previa",
                        All<TextBlock>(content).Any(t => t.Text == model.Camera.StatusText)
                        && All<TextBlock>(content).Any(t => t.Text == model.CameraPageDetail)
                        && previewFrame.ActualHeight >= 175 && previewFrame.ActualWidth >= 260);
                    check($"{language}: debajo de la preview aparece la tarjeta de apps activas y su estado vacío",
                        cameraAppsCard.Visibility == Visibility.Visible
                        && cameraAppsCard.TransformToAncestor(content).Transform(new Point(0, 0)).Y
                            > previewFrame.TransformToAncestor(content).Transform(new Point(0, previewFrame.ActualHeight)).Y
                        && cameraAppsCard.TransformToAncestor(bodyScroll).Transform(new Point(0, cameraAppsCard.ActualHeight)).Y <= bodyScroll.ViewportHeight
                        && All<TextBlock>(cameraAppsCard).Any(t => t.Text == model.CameraAppsTitle)
                        && All<TextBlock>(cameraAppsCard).Any(t => t.Text == model.CameraAppsDetail)
                        && (model.Camera.NoActiveApplications
                            ? All<TextBlock>(cameraAppsCard).Any(t => t.Text == model.Camera.ActiveApplicationsText && t.IsVisible)
                            : model.Camera.ActiveApplications.All(app => All<TextBlock>(cameraAppsCard).Any(t => t.Text == app.Name && t.IsVisible))));
                    if (model.Camera.ActiveApplications.Count > 0)
                    {
                        var permissionButtons = All<Button>(cameraAppsCard).ToArray();
                        check($"{language}: cada proceso activo tiene un botón accesible para administrar sus permisos",
                            permissionButtons.Length == model.Camera.ActiveApplications.Count
                            && permissionButtons.All(button => button.DataContext is CameraApplication app
                                && AutomationProperties.GetName(button) == app.PermissionActionLabel
                                && (button.ToolTip as string) == app.PermissionHelp
                                && ReferenceEquals(button.Command, app.OpenPermissions)));
                    }
                    Audit("Vista previa de cámara inactiva", content, true);
                    Capture(content, language, "camera-preview-inactive");
                    if (showCamera) Pump(() => false, 55);

                    model.Camera.StopMonitoring();
                    bool permissionOpened = false;
                    CameraApplication? demoApp = null;
                    demoApp = new CameraApplication("Comet", cometExe, app => permissionOpened = ReferenceEquals(app, demoApp));
                    typeof(CameraPreview).GetMethod("SetSystemUsage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .Invoke(model.Camera, [true, new[] { demoApp }, true]);
                    window.UpdateLayout();
                    var permissionButton = All<Button>(cameraAppsCard).SingleOrDefault();
                    check($"{language}: la app activa muestra su nombre y acción accesible de permisos",
                        All<TextBlock>(cameraAppsCard).Any(t => t.Text == "Comet" && t.IsVisible)
                        && model.Camera.StatusText == (language == Language.En ? "Camera in use" : "Cámara en uso")
                        && permissionButton is { IsEnabled: true }
                        && AutomationProperties.GetName(permissionButton) == demoApp.PermissionActionLabel
                        && (permissionButton.ToolTip as string) == demoApp.PermissionHelp
                        && ReferenceEquals(permissionButton.Command, demoApp.OpenPermissions));
                    Capture(content, language, "camera-preview-active");
                    bool permissionFocused = permissionButton?.Focus() == true && permissionButton.IsKeyboardFocused;
                    check($"{language}: el botón de permisos recibe el foco de teclado", permissionFocused);
                    Keyboard.ClearFocus();
                    permissionButton?.Command.Execute(null);
                    check($"{language}: la acción de permisos se enlaza con la app activa", permissionOpened);
                    window.Close();

                    model.Show(PanelView.Settings);
                    model.CurrentSettingsTab = PanelViewModel.SettingsTab.Notch;
                    var settings = new ContentView(model);
                    var settingsWindow = Host(settings, 700, 460);
                    settingsWindow.Show();
                    settingsWindow.UpdateLayout();
                    check($"{language}: el ajuste opcional de cámara aparece en Notch", All<FrameworkElement>(settings).Any(e => e.Visibility == Visibility.Visible && AutomationProperties.GetName(e) == model.CameraDockTitle));
                    Audit("Ajuste de cámara en el dock", settings, true);
                    Capture(settings, language, "camera-dock-setting");
                    int dockRefreshes = 0;
                    model.PropertyChanged += (_, e) => { if (string.IsNullOrEmpty(e.PropertyName)) dockRefreshes++; };
                    model.ToggleCameraDock.Execute(null);
                    check($"{language}: el botón opcional del dock se puede apagar y actualizar la vista", !model.CameraDockOn && dockRefreshes > 0);
                    model.ToggleCameraDock.Execute(null);
                    check($"{language}: el botón opcional del dock se puede volver a activar", model.CameraDockOn && dockRefreshes > 1);
                    model.CurrentSettingsTab = PanelViewModel.SettingsTab.General;
                    settings.UpdateLayout();
                    var calendarCard = All<Border>(settings).SingleOrDefault(border => border.Name == "CalendarFileSettingsCard");
                    check($"{language}: el archivo .ics se configura en General", calendarCard is { IsVisible: true }
                        && ReferenceEquals(calendarCard.DataContext, model.Hub.ClockInfo)
                        && All<TextBlock>(calendarCard).Any(text => text.IsVisible && text.Text == model.Hub.ClockInfo.IcsTitle)
                        && All<TextBlock>(calendarCard).Any(text => text.IsVisible && text.Text == model.Hub.ClockInfo.IcsText)
                        && All<Button>(calendarCard).Any(button => button.IsVisible && ReferenceEquals(button.Command, model.Hub.ClockInfo.PickIcs))
                        && All<Button>(calendarCard).Any(button => ReferenceEquals(button.Command, model.Hub.ClockInfo.ClearIcs)
                            && button.IsVisible == model.Hub.ClockInfo.HasIcs));
                    if (calendarCard is not null) Capture(calendarCard, language, "calendar-file-setting");
                    settingsWindow.Close();
                    var notch = new NotchWindow(model, new ContentView(model));
                    notch.Show();
                    notch.UpdateLayout();
                    Pump(() => false, 0.25);
                    var notchContent = (FrameworkElement)notch.Content;
                    var cameraButton = All<Button>(notchContent).FirstOrDefault(b => AutomationProperties.GetName(b) == model.CameraIndicatorLabel);
                    var cameraMarks = cameraButton is null ? Array.Empty<Ellipse>() : All<Ellipse>(cameraButton).ToArray();
                    var cameraChip = cameraButton?.Parent is StackPanel row ? row.Parent as Border : null;
                    bool dotVisible = cameraButton is { Width: 32, Height: 32, Margin: { Left: 0, Top: 0, Right: 0, Bottom: 0 }, Padding: { Left: 0, Top: 0, Right: 0, Bottom: 0 } }
                        && cameraButton.Content is Grid { SnapsToDevicePixels: true, UseLayoutRounding: true }
                        && cameraChip is { Width: 34, Height: 34 }
                        && cameraChip.CornerRadius == new CornerRadius(17)
                        && cameraChip.Padding == new Thickness(0)
                        && cameraMarks.Any(e => e.Width == 8 && e.Height == 8)
                        && cameraMarks.Any(e => e.Width == 14 && e.Height == 14 && !e.IsHitTestVisible);
                    check($"{language}: cámara sola usa un punto centrado dentro de una cápsula circular accesible", dotVisible);
                    Capture(notchContent, language, "camera-notch-dot");
                    notch.Close();
                    model.Camera.Dispose();
                    model.Hub.Dispose();
                    continue;
                }

                if (mediaOnly)
                {
                    var model = NewModel(true);
                    var media = model.Hub.Media;
                    media.Start();
                    Pump(() => media.LaunchersLoaded, 8);
                    check($"{language}: la detección de reproductores termina sin abrir ninguna app",
                        media.LaunchersLoaded && media.Launchers.Select(app => app.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == media.Launchers.Count
                        && media.Launchers.All(app => MediaSource.LauncherName(app.Name) == app.Name));

                    var musicHeader = new byte[44];
                    bool hasJazzPreview = false;
                    using (var music = typeof(MediaWidget).Assembly.GetManifestResourceStream("IsTargetSleeping.Resources.music-new-orleans-jazz.wav"))
                        if (music is not null && music.Length >= musicHeader.Length && music.Read(musicHeader, 0, musicHeader.Length) == musicHeader.Length)
                            hasJazzPreview = System.Text.Encoding.ASCII.GetString(musicHeader, 0, 4) == "RIFF"
                                && BitConverter.ToUInt16(musicHeader, 22) == 2
                                && BitConverter.ToUInt32(musicHeader, 24) == 22050
                                && BitConverter.ToUInt16(musicHeader, 34) == 16
                                && BitConverter.ToUInt32(musicHeader, 28) > 0
                                && BitConverter.ToUInt32(musicHeader, 40) / BitConverter.ToUInt32(musicHeader, 28) == 48;
                    check($"{language}: el jazz original está integrado como WAV estéreo de 48 segundos", hasJazzPreview);

                    if (media.NoSession && media.HasLaunchers)
                    {
                        var card = new ContentControl
                        {
                            Content = new WidgetSlot(media, 1, 1, false, rows: 2, grid: true),
                            ContentTemplate = (DataTemplate)Application.Current.FindResource("WidgetMediaWidget"),
                        };
                        var window = Host(card, 330, 360);
                        window.Show(); window.UpdateLayout();
                        var buttons = All<Button>(card).Where(button => button.IsVisible).ToArray();
                        check($"{language}: los accesos muestran el nombre y aceptan foco",
                            buttons.Length == media.Launchers.Count && buttons.All(button => button.Focusable
                                && media.Launchers.Any(app => app.Name == AutomationProperties.GetName(button))));
                        Audit("Música · reproductores instalados", card, true);
                        Capture(card, language, "music-idle-launchers");
                        window.Close();
                    }

                    var fallback = new MediaWidget();
                    fallback.LoadIdlePreview();
                    var fallbackCard = new ContentControl
                    {
                        Content = new WidgetSlot(fallback, 1, 1, false, rows: 2, grid: true),
                        ContentTemplate = (DataTemplate)Application.Current.FindResource("WidgetMediaWidget"),
                    };
                    var fallbackWindow = Host(fallbackCard, 330, 360);
                    fallbackWindow.Show(); fallbackWindow.UpdateLayout();
                    check($"{language}: la pista de muestra aparece pausada, sin controles de pista y con bucle apagado",
                        fallback.IsFallbackPreview && fallback.HasSession && !fallback.IsPlaying && fallback.CanRepeat && !fallback.CanShuffle && !fallback.CanSkip
                        && fallback.ToggleHelp == L10n.tr("Reproducir") && fallback.RepeatHelp == L10n.tr("Activar bucle"));
                    Audit("Música · jazz en pausa", fallbackCard, true);
                    Capture(fallbackCard, language, "music-idle-jazz");
                    fallbackWindow.Close();
                    model.Hub.Dispose();
                    continue;
                }

                if (clipboardOnly)
                {
                    var model = NewModel(true);
                    model.Clip.Add("Revisar los pendientes antes de la reunión");
                    model.Clip.Add("https://example.com/notas-de-proyecto · enlace copiado recientemente");
                    model.Clip.Add("Texto extenso de ejemplo para comprobar el truncado visual y su indicación sin desordenar la tarjeta.");
                    model.Show(PanelView.Clipboard);
                    model.ClipRows[0].Pin.Execute(null);
                    var content = new ContentView(model);
                    bool show = Environment.GetEnvironmentVariable("ITS_SHOW_CLIPBOARD") == "1";
                    var window = Host(content, 700, 430, activate: show);
                    window.ShowInTaskbar = show;
                    window.Title = $"isTargetSleeping · Portapapeles · {language}";
                    window.Show();
                    window.UpdateLayout();
                    Audit("Portapapeles con textos", content, true);
                    Capture(content, language, "clipboard-history");
                    if (show) Pump(() => false, 30);
                    window.Close();
                    model.Hub.Dispose();
                    continue;
                }
                if (Environment.GetEnvironmentVariable("ITS_ONLY_WIDGET_FOLDERS") == "1")
                {
                    var folderModel = NewModel(true);
                    folderModel.Show(PanelView.Widgets);
                    var content = new ContentView(folderModel);
                    check($"{language}: el ancho inicial coincide con el modelo", Math.Abs(content.Width - 640) < 0.5);
                    bool showFolderPreview = Environment.GetEnvironmentVariable("ITS_SHOW_WIDGET_FOLDERS") == "1";
                    var window = Host(content, 700, 900, activate: showFolderPreview);
                    window.Title = $"isTargetSleeping · carpetas · {language}";
                    window.ShowInTaskbar = showFolderPreview;
                    window.Show();
                    window.UpdateLayout();
                    if (showFolderPreview) Pump(() => false, 12);
                    var folderControl = All<ContentControl>(content).FirstOrDefault(control => control.Content is WidgetFolders);
                    FrameworkElement? folderCard = null;
                    for (DependencyObject? current = folderControl; current is not null; current = VisualTreeHelper.GetParent(current))
                        if (current is Border border) { folderCard = border; break; }
                    var allFolderGroups = new[]
                    {
                        ("home", folderModel.HomeFolders), ("peek", folderModel.PeekFolders), ("metrics", folderModel.MetricFolders),
                        ("activities", folderModel.ActivityFolders), ("pill", folderModel.PillFolders), ("dock", folderModel.DockFolders),
                        ("favorites", folderModel.FavoriteFolders),
                    };
                    var logoCount = folderModel.HomeFolders.Tiles.SelectMany(tile => tile.Minis).Count(mini => mini.Icon is BitmapSource);
                    check($"{language}/carpetas cerradas: grupos, logos y tarjeta se cargan", folderCard is not null
                        && folderModel.HomeFolders.Tiles.Count == WidgetCatalog.Folders.Count && logoCount == WidgetCatalog.All.Count);
                    if (folderCard is not null) Capture(content, language, "widget-folders-closed");
                    var folderCardStyle = Application.Current.Resources["WidgetFoldersCardBox"];
                    int styledFolderCards = 0;
                    foreach (var (groupName, folders) in allFolderGroups.Where(group => group.Item1 != "pill"))
                    {
                        var groupControl = All<ContentControl>(content).FirstOrDefault(control => ReferenceEquals(control.Content, folders));
                        FrameworkElement? groupCard = null;
                        for (DependencyObject? current = groupControl; current is not null; current = VisualTreeHelper.GetParent(current))
                            if (current is Border border) { groupCard = border; break; }
                        if (groupCard is null) continue;
                        styledFolderCards += groupCard.Style == folderCardStyle ? 1 : 0;
                        Capture(groupCard, language, $"widget-folder-card-{groupName}");
                    }
                    check($"{language}/las seis tarjetas de carpetas usan el estilo oscuro dedicado", styledFolderCards == 6);
                    var miniViews = All<Border>(content).Where(border => border.Name == "mini" && border.DataContext is FolderMini).ToArray();
                    bool miniStatesMatch = miniViews.Length > 0 && miniViews.All(border =>
                    {
                        var mini = (FolderMini)border.DataContext;
                        var dot = All<Ellipse>(border).FirstOrDefault(ellipse => ellipse.Name == "activeDot");
                        var plate = All<Border>(border).FirstOrDefault(item => item.Name == "plate");
                        return dot is not null && (dot.Visibility == Visibility.Visible) == mini.IsOn
                            && plate?.Background is not null && plate.Effect is null && plate.Margin == new Thickness(0)
                            && border.Background is SolidColorBrush { Color.A: 0 }
                            && border.BorderBrush is SolidColorBrush { Color.A: 0 }
                            && border.BorderThickness == new Thickness(0);
                    });
                    check($"{language}/miniaturas conservan la placa, sin borde intermedio y con estado por punto", miniStatesMatch);
                    var expectedLogoSurface = Theme.Flat ? Theme.Current.Card : Colors.Black;
                    bool logoSurfacesMatch = miniViews.All(border =>
                    {
                        var plate = All<Border>(border).FirstOrDefault(item => item.Name == "plate");
                        return plate?.Background is SolidColorBrush brush && brush.Color == expectedLogoSurface;
                    });
                    check($"{language}/los logos usan fondo negro y respetan el tema de alto contraste", logoSurfacesMatch);

                    check($"{language}/todas las carpetas muestran logos", allFolderGroups.SelectMany(group => group.Item2.Tiles).SelectMany(tile => tile.Minis).All(mini => mini.Icon is BitmapSource));
                    foreach (var (groupName, folders) in allFolderGroups)
                    foreach (var tile in folders.Tiles)
                    {
                        tile.Open.Execute(null);
                        content.UpdateLayout();
                        window.UpdateLayout();
                        var openLogoPlates = All<Border>(content).Where(border => border.Name == "plate" && border.DataContext is FolderOption).ToArray();
                        check($"{language}/carpeta {groupName}-{tile.AutomationId.Split('-').Last()}: todos los logos usan su fondo de tema",
                            openLogoPlates.Length == folderModel.OverlayFolder?.Options.Count
                            && openLogoPlates.All(plate => plate.Background is SolidColorBrush brush && brush.Color == expectedLogoSurface));
                        check($"{language}/carpeta {groupName}-{tile.AutomationId.Split('-').Last()}: cada opción tiene logo",
                            folderModel.OverlayFolder?.Options.Count > 0 && folderModel.OverlayFolder.Options.All(option => option.Icon is BitmapSource));
                        if (showFolderPreview && (groupName is "metrics" or "activities") && tile == folders.Tiles[0])
                            Capture(content, language, $"folder-{groupName}-open");
                        folderModel.CloseTopOverlay();
                        content.UpdateLayout();
                    }

                    foreach (string id in new[] { "ai", "time" })
                    {
                        var tile = folderModel.HomeFolders.Tiles.FirstOrDefault(item => item.AutomationId == $"folder-home-{id}");
                        if (id == "ai") folderModel.BodyMaxHeight = PanelViewModel.BodyMaxFor(768, wide: true);
                        Size contentSizeBefore = default;
                        if (id == "ai")
                        {
                            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                            contentSizeBefore = content.DesiredSize;
                        }
                        tile?.Open.Execute(null);
                        content.UpdateLayout();
                        window.UpdateLayout();
                        var overlay = All<Grid>(content).FirstOrDefault(grid => grid.Name == "WidgetOverlay");
                        var folderViewbox = overlay is null ? null : All<Viewbox>(overlay).FirstOrDefault(box => box.MaxWidth == 520);
                        var options = All<Button>(content).Where(button => button.DataContext is FolderOption).ToArray();
                        var logoTiles = All<Border>(content).Where(border => border.Name == "tile" && border.DataContext is FolderOption).ToArray();
                        var logoPlates = All<Border>(content).Where(border => border.Name == "plate" && border.DataContext is FolderOption).ToArray();
                        var openSurface = overlay is null ? null : All<Border>(overlay).FirstOrDefault(border => border.CornerRadius == new CornerRadius(48));
                        var openSheen = openSurface is null ? null : All<Border>(openSurface).FirstOrDefault(border => border.CornerRadius == new CornerRadius(47));
                        bool oneSurfaceEdge = openSurface is not null && openSheen is not null
                            && Math.Abs(openSheen.ActualWidth + openSheen.Margin.Left + openSheen.Margin.Right
                                - (openSurface.ActualWidth - openSurface.BorderThickness.Left - openSurface.BorderThickness.Right)) < 1
                            && Math.Abs(openSheen.ActualHeight + openSheen.Margin.Top + openSheen.Margin.Bottom
                                - (openSurface.ActualHeight - openSurface.BorderThickness.Top - openSurface.BorderThickness.Bottom)) < 1;
                        bool threeColumns = overlay is not null && All<UniformGrid>(overlay).Any(grid => grid.Columns == 3);
                        int expected = WidgetCatalog.Folders.First(folder => folder.Id == id).Widgets.Count;
                        check($"{language}/carpeta {id}: superficie, tres columnas y {expected} logos", overlay is { IsVisible: true }
                            && threeColumns && folderModel.OverlayFolder?.Options.Count == expected && options.Length == expected
                            && folderModel.OverlayFolder.Options.All(option => option.Icon is BitmapSource));
                        Point folderCenter = folderViewbox is null || overlay is null ? default
                            : folderViewbox.TranslatePoint(new Point(folderViewbox.ActualWidth / 2, folderViewbox.ActualHeight / 2), overlay);
                        bool folderCentered = folderViewbox is not null && overlay is not null
                            && Math.Abs(folderCenter.X - overlay.ActualWidth / 2) < 1
                            && Math.Abs(folderCenter.Y - overlay.ActualHeight / 2) < 1;
                        check($"{language}/carpeta {id}: centrada en el viewport", folderCentered);
                        check($"{language}/carpeta {id}: una sola superficie de vidrio, sin marco desplazado", oneSurfaceEdge);
                        check($"{language}/carpeta {id}: placa de app conservada, sin borde intermedio", logoTiles.Length == expected
                            && logoTiles.All(tile => tile.Background is SolidColorBrush { Color.A: 0 }
                                && tile.BorderBrush is SolidColorBrush { Color.A: 0 } && tile.BorderThickness == new Thickness(0))
                            && logoPlates.Length == expected && logoPlates.All(plate => plate.Background is not null
                                && plate.Effect is null && plate.Margin == new Thickness(0)));
                        if (id == "ai")
                        {
                            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                            var contentSizeAfter = content.DesiredSize;
                            check($"{language}/carpeta abierta: la capa modal no cambia el tamaño deseado del panel",
                                Math.Abs(contentSizeAfter.Width - contentSizeBefore.Width) < 0.5
                                && Math.Abs(contentSizeAfter.Height - contentSizeBefore.Height) < 0.5);
                            var openSize = new Size(folderViewbox?.ActualWidth ?? 0, folderViewbox?.ActualHeight ?? 0);
                            check($"{language}/carpeta abierta: el Viewbox respeta el alto útil del notch",
                                folderViewbox is not null && folderViewbox.MaxHeight == folderModel.BodyMaxHeight
                                && openSize.Width >= 320 && openSize.Height >= 400
                                && openSize.Height <= folderModel.BodyMaxHeight + 0.5);
                            folderModel.Notify();
                            Pump(() => false, 1.2);
                            content.UpdateLayout();
                            window.UpdateLayout();
                            check($"{language}/carpeta abierta: conserva su tamaño tras una actualización del panel",
                                folderViewbox is not null && Math.Abs(folderViewbox.ActualWidth - openSize.Width) < 0.5
                                && Math.Abs(folderViewbox.ActualHeight - openSize.Height) < 0.5);
                            Capture(content, language, "widget-folder-ai-open");
                            if (folderViewbox is not null) Capture(folderViewbox, language, "widget-folder-ai-surface");
                            var viewboxBeforeAvailableHeightChange = new Size(folderViewbox?.ActualWidth ?? 0, folderViewbox?.ActualHeight ?? 0);
                            double originalBodyMaxHeight = folderModel.BodyMaxHeight;
                            folderModel.BodyMaxHeight = originalBodyMaxHeight + 100;
                            content.UpdateLayout();
                            window.UpdateLayout();
                            check($"{language}/carpeta abierta: no crece al aumentar tarde el alto disponible",
                                folderViewbox is not null
                                && Math.Abs(folderViewbox.ActualWidth - viewboxBeforeAvailableHeightChange.Width) < 0.5
                                && Math.Abs(folderViewbox.ActualHeight - viewboxBeforeAvailableHeightChange.Height) < 0.5);
                            folderModel.CloseTopOverlay();
                            content.UpdateLayout();
                            folderModel.BodyMaxHeight = originalBodyMaxHeight + 100;
                            tile?.Open.Execute(null);
                            content.UpdateLayout();
                            window.UpdateLayout();
                            Pump(() => false, 1.2);
                            content.UpdateLayout();
                            window.UpdateLayout();
                            folderViewbox = All<Viewbox>(content).FirstOrDefault(box => box.MaxWidth == 520);
                            check($"{language}/carpeta abierta: se adapta al espacio al volver a abrirla",
                                overlay is { IsVisible: true } && folderViewbox is not null && folderViewbox.ActualHeight > openSize.Height + 50);
                            if (overlay is { IsVisible: true } && folderViewbox is not null) Capture(content, language, "widget-folder-ai-reopened");
                            if (overlay is { IsVisible: true } && folderViewbox is not null) Capture(folderViewbox, language, "widget-folder-ai-reopened-surface");
                            folderModel.CloseTopOverlay();
                            content.UpdateLayout();
                            folderModel.BodyMaxHeight = originalBodyMaxHeight;
                        }
                        if (showFolderPreview && id != "ai") Pump(() => false, 12);
                        folderModel.CloseTopOverlay();
                        content.UpdateLayout();
                    }
                    window.Close();
                    folderModel.Hub.Dispose();
                    continue;
                }
                if (Environment.GetEnvironmentVariable("ITS_ONLY_GALLERY") == "1")
                {
                    var galleryCases = new[]
                    {
                        ("notes", "1×1", 700d), ("notes", "2×2", 700d),
                        ("media", "1×1", 700d), ("media", "2×2", 700d),
                        ("usage", "1×1", 700d), ("lmstudio", "2×1", 700d),
                        ("system", "1×1", 700d), ("system", "2×2", 700d),
                        ("ollama", "1×1", 700d), ("code", "1×1", 700d), ("lmstudio", "1×1", 700d),
                        ("awake", "1×1", 700d), ("awake", "2×1", 700d),
                        ("weather", "1×1", 700d), ("weather", "2×1", 700d), ("weather", "2×2", 700d), ("weather", "1×2", 700d),
                        ("weather", "1×1", 440d),
                        ("teleprompter", "1×1", 700d), ("teleprompter", "1×2", 700d),
                        ("code", "1×1", 440d), ("notes", "1×1", 440d),
                    };
                    if (Environment.GetEnvironmentVariable("ITS_GALLERY_SIDES_ONLY") == "1")
                        galleryCases = [("clock", "1×1", 900d), ("clock", "2×2", 900d), ("clock", "1×2", 900d), ("clock", "1×1", 440d)];
                    if (Environment.GetEnvironmentVariable("ITS_GALLERY_USAGE_ONLY") == "1")
                        galleryCases = [("usage", "1×1", 700d), ("usage", "2×1", 700d), ("usage", "1×2", 700d), ("usage", "2×2", 700d), ("usage", "1×1", 440d)];
                    if (Environment.GetEnvironmentVariable("ITS_GALLERY_WEATHER_ONLY") == "1")
                        galleryCases = [("weather", "1×1", 700d), ("weather", "2×1", 700d), ("weather", "2×2", 700d), ("weather", "1×2", 700d), ("weather", "1×1", 440d)];
                    // Estante: ITS_GALLERY_SHELF_ONLY=1 y, para ver otro estado, ITS_SHELF_STATE=empty|drag|select (el resto, con los archivos de ejemplo).
                    if (Environment.GetEnvironmentVariable("ITS_GALLERY_SHELF_ONLY") == "1")
                        galleryCases = [("shelf", "1×1", 700d), ("shelf", "2×1", 700d), ("shelf", "2×2", 700d), ("shelf", "1×2", 700d), ("shelf", "1×1", 440d)];
                    // Estante en la vista corta (420×147, solo en su página): cada estado a tamaño real. ITS_SHELF_PEEK=1 y ITS_CAPTURE_UI=carpeta.
                    if (Environment.GetEnvironmentVariable("ITS_SHELF_PEEK") == "1")
                    {
                        var spDir = Path.Combine(Path.GetTempPath(), "its-spShelf-peek");
                        Directory.CreateDirectory(spDir);
                        string spMake(string name, int kb)
                        {
                            var file = Path.Combine(spDir, name);
                            if (!File.Exists(file)) File.WriteAllBytes(file, new byte[kb * 1024]);
                            return file;
                        }
                        var spExe = Path.Combine(spDir, "isTargetSleeping-setup.exe");
                        if (!File.Exists(spExe)) File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), spExe);
                        Directory.CreateDirectory(Path.Combine(spDir, "tools"));
                        string[] spFiles = [spExe, spMake("Escarlata.lnk", 2), Path.Combine(spDir, "tools"), spMake("Informe-trimestral-final.pdf", 480), spMake("foto.png", 1800), spMake("notas.txt", 1), spMake("datos.csv", 64), spMake("presentacion.pptx", 5200)];
                        var spShelf = new ShelfWidget(false);
                        var spSlot = new WidgetSlot(spShelf, 1, 1, true, alone: true);
                        var spCard = new ContentControl { Content = spSlot, ContentTemplate = (DataTemplate)Application.Current.FindResource("WidgetShelfWidget") };
                        var spRipple = new ShelfRipple { Source = spShelf, Margin = new Thickness(-12, -8, -12, -8) };
                        var spLayer = new Grid();
                        spLayer.Children.Add(spCard);
                        spLayer.Children.Add(spRipple);
                        var spHost = new Border { Margin = new Thickness(0, 30, 0, 4), Child = spLayer };
                        spHost.Resources["WidgetBox"] = new Style(typeof(Border))
                        {
                            Setters =
                            {
                                new Setter(Border.BackgroundProperty, Brushes.Transparent),
                                new Setter(Border.BorderThicknessProperty, new Thickness(0)),
                                new Setter(Border.PaddingProperty, new Thickness(12, 6, 12, 6)),
                            },
                        };
                        var spFrame = new Border { Width = 420, Height = 147, Background = Brushes.Black, Child = spHost };
                        var spPeekWindow = Host(spFrame, 420, 147);
                        spPeekWindow.Show(); spPeekWindow.UpdateLayout();
                        void spShot(string state, Action prepare, double settleSeconds = 8)
                        {
                            spShelf.SetDragging(false);
                            if (spShelf.Selecting) spShelf.ToggleSelect.Execute(null);
                            spShelf.ClearOrRemove.Execute(null);
                            if (spShelf.IsRemoving) Pump(() => !spShelf.IsRemoving, 1.5);
                            prepare();
                            ((System.Windows.Threading.DispatcherTimer)typeof(ShelfWidget).GetField("dragTimer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(spShelf)!).Stop();
                            spPeekWindow.UpdateLayout();
                            Pump(() => false, settleSeconds);
                            if (Environment.GetEnvironmentVariable("ITS_CAPTURE_UI") is { Length: > 0 } spFolder)
                            {
                                // Directo, sin VisualBrush: el pincel estira el contenido a sus límites reales y falsearía las medidas (la comparación con Nusa es píxel a píxel).
                                Directory.CreateDirectory(spFolder);
                                foreach (double dpi in new[] { 96d, 120d, 144d })
                                {
                                    var spBitmap = new RenderTargetBitmap((int)Math.Ceiling(420 * dpi / 96), (int)Math.Ceiling(147 * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
                                    spBitmap.Render(spFrame);
                                    var spPng = new PngBitmapEncoder();
                                    spPng.Frames.Add(BitmapFrame.Create(spBitmap));
                                    var scale = dpi == 96 ? "" : $"-{dpi / 96:P0}";
                                    using var spOut = File.Create(Path.Combine(spFolder, $"shelf-peek-{state}-{language.ToString().ToLowerInvariant()}-{(Theme.Flat ? "hc" : "normal")}{scale}.png"));
                                    spPng.Save(spOut);
                                }
                            }
                        }
                        void spFill(int n) { spShelf.AddFiles(spFiles.Take(n)); }
                        if (Environment.GetEnvironmentVariable("ITS_SHELF_RIPPLE_ONLY") == "1")
                        {
                            spShot("drag-hover", () => { spFill(3); spShelf.UpdateDragPosition(spRipple.PointToScreen(new Point(112, 72))); }, 0.16);
                            spShot("drop-ripple", () => { spFill(3); spShelf.AddFiles([spFiles[3]], spRipple.PointToScreen(new Point(302, 73))); }, 1.05);
                            spPeekWindow.Close();
                            continue;
                        }
                        spShot("empty", () => { });
                        spShot("drag-empty", () => spShelf.SetDragging(true));
                        spShot("1", () => spFill(1));
                        spShot("2", () => spFill(2));
                        spShot("3", () => spFill(3));
                        spShot("8", () => spFill(8));
                        spShot("drag-3", () => { spFill(3); spShelf.SetDragging(true); });
                        spShot("drag-hover", () => { spFill(3); spShelf.UpdateDragPosition(spRipple.PointToScreen(new Point(112, 72))); }, 0.16);
                        spShot("drop-ripple", () => { spFill(3); spShelf.AddFiles([spFiles[3]], spRipple.PointToScreen(new Point(302, 73))); }, 0.14);
                        spShot("select", () => { spFill(3); spShelf.ToggleSelect.Execute(null); });
                        spShot("select-marked", () => { spFill(3); spShelf.ToggleSelect.Execute(null); spShelf.Tiles[2].Activate.Execute(null); });
                        spPeekWindow.Close();
                        continue;
                    }
                    foreach (var (id, size, width) in galleryCases)
                    {
                        bool interactiveGallery = Environment.GetEnvironmentVariable("ITS_GALLERY_INTERACTIVE") == "1" && id == "notes" && (size is "1×1" or "2×2") && width == 700;
                        Page($"Widgets {id} {size} {width}", true, model =>
                        {
                            if (interactiveGallery)
                            {
                                model.Prefs.SetSwitch(PrefKeys.ReducedMotion, false);
                                model.Prefs.SetList(PrefKeys.NotchWidgets, []);
                            }
                            model.GalleryDots.First(dot => dot.Name == WidgetCatalog.Find(id)!.Title).Select.Execute(null);
                            model.GallerySizes.First(chip => chip.Label == size).Select.Execute(null);
                            if (id == "shelf" && model.GalleryPreview?.Model is ShelfWidget shelf)
                                switch (Environment.GetEnvironmentVariable("ITS_SHELF_STATE"))
                                {
                                    case "empty": shelf.ClearOrRemove.Execute(null); break;
                                    case "drag": shelf.ClearOrRemove.Execute(null); shelf.SetDragging(true); ((System.Windows.Threading.DispatcherTimer)typeof(ShelfWidget).GetField("dragTimer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(shelf)!).Stop(); break;
                                    case "select": shelf.ToggleSelect.Execute(null); shelf.Tiles[0].Activate.Execute(null); break;
                                }
                        }, PanelView.Widgets, width, interactiveGallery);
                    }
                    Page("Widgets disabled 2x2", true, model =>
                    {
                        model.GalleryDots.First(dot => dot.Name == WidgetCatalog.Find("notes")!.Title).Select.Execute(null);
                        model.GallerySizes.First(chip => chip.Label == "2×2").Select.Execute(null);
                        model.Prefs.SetList(PrefKeys.NotchWidgets, ["ollama@2x2", "system@2x2", "clock@2x2"]);
                        model.Notify();
                    }, PanelView.Widgets);

                    var galleryModel = NewModel(true);
                    galleryModel.Show(PanelView.Widgets);
                    galleryModel.GalleryDots[0].Select.Execute(null);
                    galleryModel.GalleryPrev.Execute(null);
                    bool wrapsPrevious = galleryModel.GalleryTitle == galleryModel.GalleryDots[^1].Name;
                    galleryModel.GalleryNext.Execute(null);
                    bool wrapsNext = galleryModel.GalleryTitle == galleryModel.GalleryDots[0].Name;
                    int selectedAt = galleryModel.GalleryDots.FindIndex(dot => dot.Name == galleryModel.GalleryTitle);
                    bool neighborsMatch = galleryModel.GalleryPreviousCard?.Slot.Id == WidgetCatalog.All[(selectedAt - 1 + WidgetCatalog.All.Count) % WidgetCatalog.All.Count].Id
                        && galleryModel.GalleryNextCard?.Slot.Id == WidgetCatalog.All[(selectedAt + 1) % WidgetCatalog.All.Count].Id;
                    check($"{language}/galería: flechas envuelven y vistas laterales muestran los vecinos", wrapsPrevious && wrapsNext && neighborsMatch);
                    galleryModel.Prefs.SetList(PrefKeys.NotchWidgets, []);
                    galleryModel.Notify();
                    galleryModel.GalleryDots.First(dot => dot.Name == WidgetCatalog.Find("notes")!.Title).Select.Execute(null);
                    galleryModel.GallerySizes.First(chip => chip.Label == "1×1").Select.Execute(null);
                    galleryModel.GalleryAdd.Execute(null);
                    bool added = galleryModel.WidgetIds.Any(id => WidgetLayout.BaseId(id) == "notes");
                    galleryModel.GalleryAdd.Execute(null);
                    bool removed = galleryModel.WidgetIds.All(id => WidgetLayout.BaseId(id) != "notes");
                    galleryModel.GallerySizes.First(chip => chip.Label == "2×2").Select.Execute(null);
                    bool sizeChanged = galleryModel.GalleryWidth == 2 * GridPanel.CellWidth && galleryModel.GalleryHeight == 2 * GridPanel.RowHeight;
                    galleryModel.Prefs.SetList(PrefKeys.NotchWidgets, ["system@2", "clock"]);
                    galleryModel.Notify();
                    galleryModel.GalleryDots.First(dot => dot.Name == WidgetCatalog.Find("clock")!.Title).Select.Execute(null);
                    galleryModel.GallerySizes.First(chip => chip.Label == "2×2").Select.Execute(null);
                    bool resizeMovesToPage = galleryModel.GalleryAddLabel == L10n.tr("Usar este tamaño en la página");
                    galleryModel.GalleryAdd.Execute(null);
                    bool resizeKeepsOrder = PanelViewModel.HomePages(galleryModel.WidgetIds).SelectMany(page => page)
                        .SequenceEqual(["system@2", "clock@2x2"]);
                    galleryModel.Prefs.SetList(PrefKeys.NotchWidgets, ["ollama@2x2"]);
                    galleryModel.Notify();
                    galleryModel.GalleryDots.First(dot => dot.Name == WidgetCatalog.Find("notes")!.Title).Select.Execute(null);
                    galleryModel.GallerySizes.First(chip => chip.Label == "1×1").Select.Execute(null);
                    bool addPageLabel = galleryModel.GalleryAddLabel == L10n.tr("Añadir como página");
                    galleryModel.GalleryAdd.Execute(null);
                    bool addedPage = PanelViewModel.HomePages(galleryModel.WidgetIds).Count == 2
                        && galleryModel.GalleryAddLabel == L10n.tr("Quitar de su página");
                    galleryModel.GalleryAdd.Execute(null);
                    bool removedPage = PanelViewModel.HomePages(galleryModel.WidgetIds).Count == 1;
                    galleryModel.Prefs.SetList(PrefKeys.NotchWidgets, ["ollama@2x2", "system@2x2", "clock@2x2"]);
                    galleryModel.Notify();
                    bool disabledAtCapacity = !galleryModel.GalleryCanAdd;
                    check($"{language}/galería: el tamaño y el botón distinguen Home de una página", resizeMovesToPage && resizeKeepsOrder
                        && addPageLabel && addedPage && removedPage);
                    check($"{language}/galería: seleccionar tamaño, añadir/quitar y deshabilitar al llenar el Home", added && removed && sizeChanged && disabledAtCapacity);
                    continue;
                }

                foreach (var wide in new[] { true, false })
                {
                    // Cada página y cada pestaña.
                    Page("Ollama", wide, _ => { }, PanelView.Main);
                    Page("Home", wide, _ => { }, PanelView.Home);
                    Page("Widgets", wide, model =>
                    {
                        model.GalleryDots.First(dot => dot.Name == WidgetCatalog.Find("notes")!.Title).Select.Execute(null);
                        model.GallerySizes.First(size => size.Label == "1×1").Select.Execute(null);
                    }, PanelView.Widgets);
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
                            if (!Theme.Flat)
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
                    prefs.SetList(PrefKeys.NotchWidgets, [widget.Id == "notes" ? "notes@1" : widget.Id]);
                    prefs.SetList(PrefKeys.NotchPeekWidgets, [widget.Id]);
                    var home = NewModel(true);
                    home.Show(PanelView.Home);
                    var homeView = new ContentView(home);
                    var homeWindow = Host(homeView);
                    homeWindow.Show();
                    homeWindow.UpdateLayout();
                    Audit($"Home con {widget.Id}", homeView, true);
                    if (widget.Id == "clock")
                    {
                        Capture(homeView, language, "calendar-solo-wide");
                        if (home.HomeWidgets.SingleOrDefault()?.Model is ClockWidget clock)
                        {
                            clock.ToggleAdder.Execute(null);
                            homeWindow.UpdateLayout();
                            Audit("Calendario con formulario abierto", homeView, true);
                            Capture(homeView, language, "calendar-adder-open");
                        }
                    }
                    homeWindow.Close();
                    var peekModel = NewModel(true);
                    var peek = new NotchPeek(peekModel);
                    var peekWindow = Host(peek, 700, 600);
                    peekWindow.Show();
                    peekWindow.UpdateLayout();
                    Audit($"Versión corta con {widget.Id}", peek, true);
                    peekWindow.Close();
                }

                // Home conserva sus cuatro celdas; cada widget que excede ese espacio ocupa una página propia.
                prefs.SetList(PrefKeys.NotchWidgets, ["ollama", "system", "clock", "battery", "media"]);
                var pagedHome = NewModel(true);
                pagedHome.Show(PanelView.Home);
                var pagedView = new ContentView(pagedHome);
                var pagedWindow = Host(pagedView, activate: true);
                pagedWindow.Show();
                pagedWindow.UpdateLayout();
                Audit("Home con páginas de widgets", pagedView, true);
                Capture(pagedView, language, "home-widget-pages");
                var nextPage = All<Button>(pagedView).FirstOrDefault(b => AutomationProperties.GetName(b) == pagedHome.NextPageLabel);
                if (nextPage is not null) Keyboard.Focus(nextPage);
                Pump(() => nextPage?.IsKeyboardFocused == true, 1);
                check($"{language}/Home con páginas: el paginador recibe el foco", nextPage?.IsKeyboardFocused == true);
                pagedWindow.UpdateLayout();
                Capture(pagedView, language, "home-widget-pages-focus");
                pagedHome.HomeDots[2].Pick.Execute(null);
                pagedWindow.UpdateLayout();
                Audit("Página individual de batería", pagedView, true);
                Capture(pagedView, language, "home-single-widget-page");
                pagedWindow.Close();

                prefs.SetList(PrefKeys.NotchWidgets, WidgetCatalog.DefaultWidgets);
                prefs.SetList(PrefKeys.NotchPeekWidgets, WidgetCatalog.DefaultPeek);
            }
            if (clipboardOnly || cameraOnly || mediaOnly || foldersOnly || calendarOnly
                || homePagesOnly || Environment.GetEnvironmentVariable("ITS_ONLY_GALLERY") == "1") return;
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
