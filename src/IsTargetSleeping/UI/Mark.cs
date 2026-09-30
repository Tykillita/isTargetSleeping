using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IsTargetSleeping.UI;

/// Ícono de la bandeja, de la cabecera y de la app, todos a partir de `Logo`.
public static class Mark
{
    public enum Dot { None, On, Busy }

    public static readonly Color Amber = Theme.Hex(0xD9A441);
    public static readonly Color IconBackground = Theme.Hex(0x0C0C0E);

    /// Bandeja: la mira y el ojo con pestañas (sin ellas parece una sonrisa), con el
    /// punto de estado arriba a la derecha. Blanco sobre barra oscura y casi negro
    /// sobre barra clara, como los íconos del sistema.
    public static BitmapSource StatusBitmap(int px, Dot dot, bool dimmed, bool lightTaskbar)
    {
        var baseColor = lightTaskbar ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Colors.White;
        var color = Theme.Brush(Theme.WithOpacity(baseColor, dimmed ? 0.55 : 1));

        // La mira con sus marcas ocupa ~770 de los 1024 (y el punto sube un poco
        // más): se encuadra esa zona para que llene el ícono sin recortar el punto.
        const double x0 = 122, y0 = 104, span = 800;
        double s = px / span;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var rect = new Rect(-x0 * s, -y0 * s, Logo.Canvas * s, Logo.Canvas * s);
            Logo.Draw(dc, rect, color, parts: Logo.Parts.Lashes, weight: px <= 20 ? 1.5 : 1.35);
            if (dot != Dot.None)
            {
                var c = new Point(rect.X + Logo.DotCenter.X * s, rect.Y + Logo.DotCenter.Y * s);
                double r = px * 0.15;
                dc.DrawEllipse(Theme.Brush(dot == Dot.On ? Theme.Current.On : Amber), null, c, r, r);
            }
        }
        return Render(visual, px);
    }

    /// Bandeja con el % de RAM (como Mem Reduct): el número en el color de la barra
    /// de tareas, ámbar con presión alta y coral con crítica, y el punto de estado.
    public static BitmapSource PercentBitmap(int px, int percent, SystemMemory.Level pressure, Dot dot, bool lightTaskbar)
    {
        var baseColor = lightTaskbar ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Colors.White;
        var color = pressure switch
        {
            SystemMemory.Level.Critical => Theme.Current.Danger,
            SystemMemory.Level.Warning => Theme.Current.Warm,
            _ => baseColor,
        };
        var text = Math.Clamp(percent, 0, 99).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var face = new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.SemiCondensed);
            var formatted = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                face, px * 0.72, Theme.Brush(color), 1.0);
            dc.DrawText(formatted, new Point((px - formatted.Width) / 2, (px - formatted.Height) / 2 + px * 0.04));
            if (dot != Dot.None)
            {
                double r = px * 0.13;
                dc.DrawEllipse(Theme.Brush(dot == Dot.On ? Theme.Current.On : Amber), null, new Point(px - r, r), r, r);
            }
        }
        return Render(visual, px);
    }

    /// Ícono de la app: el logo azul sobre fondo casi negro.
    public static BitmapSource AppIcon(int px)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            Logo.DrawIcon(dc, px, Theme.Brush(IconBackground), Theme.Brush(Theme.Blue));
        return Render(visual, px);
    }

    public static BitmapSource Render(Visual visual, int px)
    {
        var bmp = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    public static byte[] Png(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// .ico con varias resoluciones en PNG (el formato que Windows usa desde Vista).
    public static byte[] Ico(IEnumerable<int> sizes)
    {
        var images = sizes.Select(s => (Size: s, Data: Png(AppIcon(s)))).ToList();
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((short)0); w.Write((short)1); w.Write((short)images.Count);
        int offset = 6 + 16 * images.Count;
        foreach (var (size, data) in images)
        {
            w.Write((byte)(size >= 256 ? 0 : size)); w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(data.Length); w.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in images) w.Write(data);
        return ms.ToArray();
    }

    /// HICON para Shell_NotifyIcon a partir de un bitmap de WPF.
    public static IntPtr ToHIcon(BitmapSource source)
    {
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
        var pixels = new byte[stride * h];
        bgra.CopyPixels(pixels, stride, 0);

        var header = new Win32.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h,   // de arriba abajo
            biPlanes = 1,
            biBitCount = 32,
        };
        var color = Win32.CreateDIBSection(IntPtr.Zero, ref header, 0, out var bits, IntPtr.Zero, 0);
        Marshal.Copy(pixels, 0, bits, pixels.Length);
        var mask = Win32.CreateBitmap(w, h, 1, 1, IntPtr.Zero);
        var info = new Win32.ICONINFO { fIcon = true, hbmMask = mask, hbmColor = color };
        var icon = Win32.CreateIconIndirect(ref info);
        Win32.DeleteObject(color);
        Win32.DeleteObject(mask);
        return icon;
    }
}

/// El logo como elemento de la interfaz (cabecera del panel y «Acerca de»).
public sealed class LogoView : FrameworkElement
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(Brush), typeof(LogoView),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsIconProperty = DependencyProperty.Register(
        nameof(IsIcon), typeof(bool), typeof(LogoView),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Color { get => (Brush)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    public bool IsIcon { get => (bool)GetValue(IsIconProperty); set => SetValue(IsIconProperty, value); }

    public LogoView() => Theme.Changed += InvalidateVisual;

    protected override void OnRender(DrawingContext dc)
    {
        double side = Math.Min(ActualWidth, ActualHeight);
        if (IsIcon)
        {
            Logo.DrawIcon(dc, side, Theme.Brush(Mark.IconBackground), Theme.Brush(Theme.Current.On));
            return;
        }
        Logo.Draw(dc, new Rect(0, 0, side, side), Color, weight: side < 40 ? 1.25 : 1);
    }
}
