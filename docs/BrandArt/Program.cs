using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IsTargetSleeping.UI;
using IsTargetSleeping;

namespace IsTargetSleeping { public static class AppInfo { public const string Name = "isTargetSleeping"; } }

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Pass the repository root.");
        string root = Path.GetFullPath(args[0]);
        string images = Path.Combine(root, "docs", "images");
        Directory.CreateDirectory(images);
        Brush dark = Brush("#0C0C0E"), white = Brushes.White, muted = Brush("#A0A0A5");
        var cover = new DrawingVisual();
        using (var dc = cover.RenderOpen())
        {
            dc.DrawRectangle(dark, null, new Rect(0, 0, 1280, 640));
            Text(dc, "LOCAL AI. MORE ROOM.", 82, 124, 20, muted, FontWeights.SemiBold);
            Text(dc, AppInfo.Name, 78, 206, 66, white, FontWeights.SemiBold);
            Text(dc, "Let idle models sleep.", 82, 308, 35, muted);
            Text(dc, "Get your RAM back.", 82, 355, 35, muted);
            Logo.Draw(dc, new Rect(785, 69, 470, 470), white);
            Text(dc, "WINDOWS  ·  FREE & OPEN SOURCE", 82, 535, 21, muted, FontWeights.Medium);
        }
        Save(cover, 1280, 640, Path.Combine(images, "social-preview.png"));
        var mark = new DrawingVisual();
        using (var dc = mark.RenderOpen()) Logo.Draw(dc, new Rect(0, 0, 512, 512), white);
        Save(mark, 512, 512, Path.Combine(root, "Assets", "istargetsleeping-mark-white-512.png"));
        string symbol = Logo.Svg("#FFFFFF").Replace("<svg ", "<svg x=\"785\" y=\"69\" ")
            .Replace("width=\"1024\" height=\"1024\"", "width=\"470\" height=\"470\"");
        string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="1280" height="640" viewBox="0 0 1280 640" role="img" aria-labelledby="title description">
            <title id="title">isTargetSleeping — white symbol cover</title>
            <desc id="description">Let idle models sleep. Get your RAM back. Free and open source for Windows.</desc>
            <rect width="1280" height="640" fill="#0C0C0E"/>
            <g font-family="Segoe UI,Arial,sans-serif">
              <text x="82" y="146" font-size="20" font-weight="600" fill="#A0A0A5">LOCAL AI. MORE ROOM.</text>
              <text x="78" y="277" font-size="66" font-weight="600" fill="#FFFFFF">isTargetSleeping</text>
              <text x="82" y="346" font-size="35" fill="#A0A0A5">Let idle models sleep.</text>
              <text x="82" y="393" font-size="35" fill="#A0A0A5">Get your RAM back.</text>
              <text x="82" y="558" font-size="21" font-weight="500" fill="#A0A0A5">WINDOWS  ·  FREE &amp; OPEN SOURCE</text>
            </g>
            """ + symbol + "</svg>\n";
        File.WriteAllText(Path.Combine(images, "social-preview.svg"), svg);
        Console.WriteLine("social-preview.png: 1280 × 640; white mark: 512 × 512 with transparency");
    }

    private static Brush Brush(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;
    private static void Text(DrawingContext dc, string text, double x, double y, double size, Brush color, FontWeight? weight = null)
    {
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal);
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, color, 1);
        dc.DrawText(formatted, new Point(x, y));
    }
    private static void Save(Visual visual, int width, int height, string path)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        png.Save(stream);
    }
}
