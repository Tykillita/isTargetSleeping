using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IsTargetSleeping;
using IsTargetSleeping.UI;
using IsTargetSleeping.UI.Pets;

// Uses the app's real views, resources, renderer and animation director. Demo mode
// never starts engines, installs the memory agent or writes user preferences.
internal static class Program
{
    private const int Scale = 4, Fps = 60;
    private static string root = "", output = "";
    private static readonly Dictionary<string, object> manifest = [];

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 1) throw new ArgumentException("Pass the repository root.");
        root = Path.GetFullPath(args[0]);
        output = Path.Combine(root, "obj", "tour-work", "native");
        Directory.CreateDirectory(output);
        int result = 1;
        var app = new App();
        app.InitializeComponent();
        app.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                Theme.Apply();
                if (!args.Contains("--pets-only"))
                    foreach (var lang in new[] { "es", "en" }) CaptureViews(lang);
                if (!args.Contains("--views-only")) CapturePets();
                manifest["version"] = File.ReadAllText(Path.Combine(root, "VERSION")).Trim();
                manifest["scale"] = Scale;
                manifest["fps"] = Fps;
                File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest,
                    new JsonSerializerOptions { WriteIndented = true }));
                result = 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); }
            app.Shutdown();
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        app.Run();
        return result;
    }

    private static void CaptureViews(string lang)
    {
        L10n.Init(lang == "es" ? Language.Es : Language.En);
        foreach (var page in new[] { PanelView.Main, PanelView.Activity, PanelView.Settings })
        {
            var (view, frame, _) = View(page);
            string name = page switch { PanelView.Main => "panel", PanelView.Activity => "activity", _ => "settings" };
            Save(frame, new Rect(frame.RenderSize), Path.Combine(root, "docs", "images", $"{name}-{lang}.png"));
            SaveCards(view, frame, $"{name}-{lang}");
            Console.WriteLine($"{name}-{lang}: {frame.RenderSize.Width * Scale:0} × {frame.RenderSize.Height * Scale:0}");
        }
        foreach (var state in new[] { "loaded", "sleep", "game" })
        {
            var (view, frame, _) = View(PanelView.Main, state == "game", state != "sleep");
            Save(frame, new Rect(frame.RenderSize), Path.Combine(output, $"{state}-{lang}.png"));
            SaveCards(view, frame, $"{state}-{lang}");
        }
    }

    private static (ContentView, Border, PanelViewModel) View(PanelView page, bool game = false, bool loaded = true)
    {
        var controller = new OllamaController();
        controller.LoadDemo(loaded);
        var prefs = new Prefs();
        prefs.LoadDemo();
        var supervisor = new Supervisor(controller, prefs);
        supervisor.LoadDemo(game);
        var model = new PanelViewModel(supervisor, page);
        if (page == PanelView.Activity)
        {
            // Set sample data before creating bindings. No live process names or icons.
            bool spanish = L10n.Effective == "es";
            Set(model, nameof(PanelViewModel.ProcessSummaryRows), new List<ProcessSummaryRow>
            {
                new(spanish ? "Navegador web" : "Web browser", "1.6 GB", L10n.tr("%d procesos", 12), null),
                new(spanish ? "Modelo local" : "Local model", "1.2 GB", L10n.tr("%d procesos", 2), null),
                new("Editor", "0.8 GB", L10n.tr("%d procesos", 4), null),
                new("Windows Explorer", "0.3 GB", L10n.tr("%d procesos", 2), null),
                new("isTargetSleeping", "0.1 GB", L10n.tr("%d procesos", 1), Mark.AppIcon(24)),
            });
        }
        var view = new ContentView(model);
        var desktop = new Canvas { Background = Brush(0x0A0A0B) };
        desktop.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 90 };
        var frame = new Border
        {
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            BorderBrush = Brush(0x3A3A3E), ClipToBounds = true,
            Child = new Grid { Children = { desktop, new Border { Background = Theme.Brush(Theme.Hex(0x1C1C1C, 0.55)) }, view } },
        };
        Layout(frame);
        foreach (var (color, x, y, d) in new[] { (0x2A2A2Eu, .10, .10, 260), (0x1F1F22u, .95, .45, 240), (0x26262Au, .05, .80, 220) })
        {
            var blob = new System.Windows.Shapes.Ellipse { Width = d, Height = d, Fill = Theme.Brush(Theme.Hex(color, .85)) };
            Canvas.SetLeft(blob, x * frame.RenderSize.Width - d / 2.0);
            Canvas.SetTop(blob, y * frame.RenderSize.Height - d / 2.0);
            desktop.Children.Add(blob);
        }
        Layout(frame);
        return (view, frame, model);
    }

    private static void SaveCards(ContentView view, Border frame, string name)
    {
        var style = view.FindResource("CardBox");
        var cards = Descendants(view).OfType<Border>().Where(b => ReferenceEquals(b.Style, style)
            && b.Visibility == Visibility.Visible && b.ActualHeight > 10).ToList();
        var entries = new List<object>();
        for (int i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            var bounds = card.TransformToAncestor(frame).TransformBounds(new Rect(card.RenderSize));
            string path = $"{name}-card-{i:00}.png";
            Save(frame, bounds, Path.Combine(output, path));
            string text = string.Join(" | ", Descendants(card).OfType<TextBlock>().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
            entries.Add(new { file = path, text, x = bounds.X * Scale, y = bounds.Y * Scale,
                width = bounds.Width * Scale, height = bounds.Height * Scale });
        }
        manifest[name] = entries;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void CapturePets()
    {
        // Every frame is evaluated by the current app, rather than stretching
        // a short sprite sheet into a slow or stepped approximation.
        foreach (var pet in PetCatalog.All)
        foreach (var (name, activity, from, seconds) in new[]
        {
            ("portrait", PetActivity.Alert, PetActivity.Hidden, 6),
            ("sleep", PetActivity.DeepSleep, PetActivity.Hidden, 3),
            ("eat", PetActivity.Eating, PetActivity.Alert, 3),
            ("work", PetActivity.Working, PetActivity.Alert, 3),
            ("clean", PetActivity.Sweeping, PetActivity.DeepSleep, 4),
            ("wake", PetActivity.WakingUp, PetActivity.DeepSleep, 5),
        })
        {
            string directory = Path.Combine(output, "pets", pet.Id, name);
            Directory.CreateDirectory(directory);
            var director = new PetDirector(pet.Anchors, 42, pet.Animations);
            if (from != PetActivity.Hidden) director.Step(-1.0 / Fps, 1.0 / Fps, new(from));
            for (int i = 0; i < seconds * Fps; i++)
            {
                var shot = director.Step(i / (double)Fps, 1.0 / Fps, new(activity, From: from));
                var canvas = new PixelCanvas(pet.Size.Width * Scale, pet.Size.Height * Scale);
                pet.Draw(canvas, shot.Pose, Scale);
                ParticleSprites.Draw(canvas, director.Particles.Live, Scale);
                if (activity == PetActivity.Working) ParticleSprites.Thinking(canvas, pet.Anchors, Scale, i / (double)Fps);
                var sprite = BitmapSource.Create(canvas.Width, canvas.Height, 96, 96, PixelFormats.Pbgra32,
                    null, canvas.ToBgra(), canvas.Width * 4);
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                    dc.DrawImage(sprite, new Rect(4 * Scale + shot.Dx * Scale, 4 * Scale + shot.Dy * Scale, canvas.Width, canvas.Height));
                Png(visual, (pet.Size.Width + 8) * Scale, (pet.Size.Height + 8) * Scale,
                    Path.Combine(directory, $"{i:0000}.png"));
            }
            Console.WriteLine($"{pet.Id}/{name}: {seconds * Fps} native frames");
        }
    }

    private static void Set(object target, string property, object value) =>
        target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!.SetValue(target, value);

    private static Brush Brush(uint hex) => Theme.Brush(Theme.Hex(hex));

    private static void Layout(FrameworkElement frame)
    {
        frame.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        frame.Arrange(new Rect(frame.DesiredSize));
        frame.UpdateLayout();
    }

    private static void Save(FrameworkElement frame, Rect bounds, string path)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, bounds.Width, bounds.Height), 8, 8));
            dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
            dc.DrawRectangle(new VisualBrush(frame) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                null, new Rect(frame.RenderSize));
        }
        Png(visual, (int)Math.Ceiling(bounds.Width * Scale), (int)Math.Ceiling(bounds.Height * Scale), path, 96 * Scale);
    }

    private static void Png(Visual visual, int width, int height, string path, double dpi = 96)
    {
        var bitmap = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
