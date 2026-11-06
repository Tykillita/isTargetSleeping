using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using IsTargetSleeping;
using IsTargetSleeping.UI;

internal static class BrandLogoTests
{
    public static void Run(Action<string, bool> check)
    {
        static byte[] Pixels(BitmapSource image)
        {
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
            converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
            return pixels;
        }

        foreach (int size in new[] { 16, 20, 24, 32, 256 })
        {
            var icon = Mark.AppIcon(size);
            var pixels = Pixels(icon);
            check($"logo {size}: tamaño y esquinas transparentes", icon.PixelWidth == size && icon.PixelHeight == size && pixels[3] == 0);
            bool cleanTop = true;
            for (int y = 0; y < size * 0.25; y++)
            for (int x = 0; x < size; x++)
            {
                int i = (y * size + x) * 4;
                if (pixels[i + 3] > 64 && pixels[i] > 125 && pixels[i + 1] > 125 && pixels[i + 2] > 125) cleanTop = false;
            }
            check($"logo {size}: sin borde blanco superior", cleanTop);
        }
        var ico = Mark.Ico(new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 });
        check("ICO: incluye nueve tamaños", BitConverter.ToUInt16(ico, 2) == 1 && BitConverter.ToUInt16(ico, 4) == 9);
        check("SVG: conserva el logo aprobado", Logo.Svg("#fff", "#000").Contains("data:image/png;base64,"));

        using (var source = typeof(Logo).Assembly.GetManifestResourceStream("IsTargetSleeping.BrandLogo.png")!)
        {
            static byte[] Premultiplied(BitmapSource image)
            {
                var converted = new FormatConvertedBitmap(image, PixelFormats.Pbgra32, null, 0);
                var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
                converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
                return pixels;
            }
            var original = BitmapFrame.Create(source, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var master = Premultiplied(original);
            var previous = new DrawingVisual();
            using (var dc = previous.RenderOpen()) Logo.DrawIcon(dc, 32, Brushes.Black, Brushes.White);
            var oldPixels = Premultiplied(Mark.Render(previous, 32));
            var newPixels = Premultiplied(Mark.AppIcon(32));
            double oldError = 0, newError = 0;
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            for (int channel = 0; channel < 4; channel++)
            {
                int sum = 0;
                for (int sy = y * 32; sy < (y + 1) * 32; sy++)
                for (int sx = x * 32; sx < (x + 1) * 32; sx++)
                    sum += master[(sy * 1024 + sx) * 4 + channel];
                double reference = sum / 1024.0;
                int index = (y * 32 + x) * 4 + channel;
                oldError += Math.Abs(oldPixels[index] - reference);
                newError += Math.Abs(newPixels[index] - reference);
            }
            check("icono 32: reducción suave fiel al original", newError < oldError * 0.8);
        }

        foreach (bool light in new[] { false, true })
        foreach (int size in new[] { 16, 20, 24, 32 })
        {
            var off = Pixels(Mark.PoseBitmap(size, TrayPose.Off, light));
            var asleep = Pixels(Mark.PoseBitmap(size, TrayPose.On(false), light));
            var awake = Pixels(Mark.PoseBitmap(size, TrayPose.On(true), light));
            var starting = Pixels(Mark.PoseBitmap(size, TrayMotion.Searching(0), light));
            check($"bandeja {size}, clara={light}: estados y ojos distinguibles", !off.SequenceEqual(asleep) && !asleep.SequenceEqual(awake) && !off.SequenceEqual(starting));
            int top = (size / 2) * 4;
            check($"bandeja {size}, clara={light}: sin marco claro", asleep[top] < 60 && asleep[top + 1] < 60 && asleep[top + 2] < 60);
            int dot = (Math.Min(size - 1, (int)(size * 2.6 / 16)) * size + Math.Min(size - 1, (int)(size * 13.4 / 16))) * 4;
            check($"bandeja {size}, clara={light}: inicio ámbar", starting[dot + 2] > starting[dot]);
        }
        if (Environment.GetEnvironmentVariable("ITS_BRAND_CAPTURES") is { Length: > 0 } output)
            CaptureViews(output);
    }

    private static void CaptureViews(string output)
    {
        Directory.CreateDirectory(output);
        bool originalContrast = Theme.ForceHighContrast;
        try
        {
            foreach (var language in new[] { Language.Es, Language.En })
            foreach (bool contrast in new[] { false, true })
            {
                L10n.Init(language);
                Theme.ForceHighContrast = contrast;
                Theme.Apply();
                var prefs = new Prefs(); prefs.LoadDemo();
                var engine = new OllamaController(); engine.LoadDemo();
                var supervisor = new Supervisor(engine, prefs); supervisor.LoadDemo();
                var model = new PanelViewModel(supervisor);
                var panel = new ContentView(model);
                foreach (var (name, view, width) in new (string, FrameworkElement, double)[]
                {
                    ("panel", panel, 342), ("about", AboutWindow.Build(), 392)
                })
                {
                    var root = new Border { Width = width, Background = Theme.Brush(Theme.Current.Bg), Child = view };
                    root.Measure(new Size(width, double.PositiveInfinity));
                    root.Arrange(new Rect(root.DesiredSize));
                    root.UpdateLayout();
                    var frame = new System.Windows.Threading.DispatcherFrame();
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                    timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                    timer.Start();
                    System.Windows.Threading.Dispatcher.PushFrame(frame);
                    root.UpdateLayout();
                    foreach (int scale in new[] { 100, 125, 150 })
                    {
                        double factor = scale / 100.0;
                        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * factor),
                            (int)Math.Ceiling(root.ActualHeight * factor), 96 * factor, 96 * factor, PixelFormats.Pbgra32);
                        bitmap.Render(root);
                        File.WriteAllBytes(Path.Combine(output, $"{name}-{language}-{contrast}-{scale}.png"), Mark.Png(bitmap));
                    }
                }
                model.Hub.Dispose();
            }
        }
        finally { Theme.ForceHighContrast = originalContrast; Theme.Apply(); }
    }
}
