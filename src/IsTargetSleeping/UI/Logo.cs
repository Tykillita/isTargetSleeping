using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IsTargetSleeping.UI;

/// La imagen aprobada es la fuente del icono; la silueta se usa en medios monocromos.
public static class Logo
{
    private const string FacePath = "M0 610 C35 350 262 300 394 318 C590 335 688 423 842 478 C955 518 1024 578 1024 685 L1024 800 Q1024 1024 800 1024 L224 1024 Q0 1024 0 800 Z";
    private const string LeftEye = "M200 600 C175 570 179 625 204 647 C263 704 384 714 449 647 C475 620 454 591 425 608 C357 652 291 645 222 605 C214 600 207 599 200 600 Z";
    private const string RightEye = "M575 606 C556 585 554 622 581 647 C646 709 766 707 827 644 C851 617 833 590 803 606 C733 651 659 648 598 608 C590 603 582 602 575 606 Z";
    private static readonly Geometry Face = Geometry.Parse($"F0 {FacePath} {LeftEye} {RightEye}");
    private static readonly byte[] IconBytes = ReadIcon();
    private static readonly BitmapImage Icon = LoadIcon();

    private static byte[] ReadIcon()
    {
        using var stream = typeof(Logo).Assembly.GetManifestResourceStream("IsTargetSleeping.BrandLogo.png")
            ?? throw new InvalidOperationException("Missing app logo resource.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static BitmapImage LoadIcon()
    {
        using var stream = new MemoryStream(IconBytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    internal static BitmapSource IconAt(int size)
    {
        using var input = new MemoryStream(IconBytes);
        using var original = new System.Drawing.Bitmap(input);
        using var resized = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(resized))
        {
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.DrawImage(original, 0, 0, size, size);
        }
        using var png = new MemoryStream();
        resized.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        png.Position = 0;
        var result = new BitmapImage();
        result.BeginInit();
        result.CacheOption = BitmapCacheOption.OnLoad;
        result.StreamSource = png;
        result.EndInit();
        result.Freeze();
        return result;
    }

    public static void Draw(DrawingContext dc, Rect rect, Brush color, double weight = 1)
    {
        double side = Math.Min(rect.Width, rect.Height);
        dc.PushTransform(new MatrixTransform(side / 1024, 0, 0, side / 1024, rect.X, rect.Y));
        dc.DrawGeometry(color, null, Face);
        dc.Pop();
    }

    public static void DrawIcon(DrawingContext dc, double px, Brush background, Brush color) =>
        dc.DrawImage(Icon, new Rect(0, 0, px, px));

    public static string Svg(string color, string? background = null)
    {
        string contents = background is null
            ? $"<path fill=\"{color}\" fill-rule=\"evenodd\" d=\"{FacePath} {LeftEye} {RightEye}\"/>"
            : $"<image width=\"1024\" height=\"1024\" href=\"data:image/png;base64,{Convert.ToBase64String(IconBytes)}\"/>";
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1024 1024\" width=\"1024\" height=\"1024\"><title>{AppInfo.Name}</title>{contents}</svg>\n";
    }
}
