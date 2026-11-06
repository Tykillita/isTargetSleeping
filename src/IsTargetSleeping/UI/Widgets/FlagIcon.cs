using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IsTargetSleeping.UI;

/// La bandera de un país (código ISO en minúsculas). Windows no pinta las banderas emoji (salen como dos letras), así que son imágenes
/// de «flag-icons» (licencia MIT, ver Resources/Flags/LICENSE-flag-icons.txt) a 88 px de ancho, 4:3, para que se vean nítidas también a 150 %.
public sealed class FlagIcon : FrameworkElement
{
    public static readonly DependencyProperty CodeProperty = DependencyProperty.Register(nameof(Code), typeof(string), typeof(FlagIcon),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public string Code { get => (string)GetValue(CodeProperty); set => SetValue(CodeProperty, value); }

    private static readonly Dictionary<string, BitmapImage?> cache = [];

    public FlagIcon() { Width = 22; Height = 16.5; SnapsToDevicePixels = true; RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality); }

    private static BitmapImage? Load(string code)
    {
        code = code.ToLowerInvariant();
        if (cache.TryGetValue(code, out var cached)) return cached;
        BitmapImage? image = null;
        try
        {
            image = new BitmapImage(new Uri($"pack://application:,,,/isTargetSleeping;component/Resources/Flags/{code}.png", UriKind.Absolute));
            image.Freeze();
        }
        catch (Exception) { /* un código sin imagen: se pinta el hueco gris */ }
        return cache[code] = image;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 2) return;
        var box = new Rect(0, 0, w, h);
        dc.PushClip(new RectangleGeometry(box, 2.5, 2.5));
        if (Load(Code) is { } image) dc.DrawImage(image, box);
        else dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)), null, box);
        dc.Pop();
        dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)), 0.7), new Rect(0.35, 0.35, w - 0.7, h - 0.7), 2.5, 2.5);
    }
}
