using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IsTargetSleeping.UI;

/// Íconos de la bandeja y de la app, incluida su adaptación para tamaños pequeños.
public static class Mark
{
    public enum Dot { None, On, Busy }

    public static readonly Color Amber = Theme.Hex(0xD9A441);
    public static readonly Color IconBackground = Theme.Hex(0x0C0C0E);

    /// Bandeja: mascota adaptada a la rejilla pequeña de Windows.
    public static BitmapSource StatusBitmap(int px, Dot dot, bool dimmed, bool lightTaskbar, bool awake = false) =>
        PoseBitmap(px, new TrayPose(1, 0, 0, 0, 0, 0, awake ? 1 : 0, dot == Dot.None ? 0 : 1, dot == Dot.Busy ? 1 : 0,
            dimmed ? TrayPose.DimInk : 1), lightTaskbar);

    public static BitmapSource PoseBitmap(int px, TrayPose pose, bool lightTaskbar)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) DrawPose(dc, px, pose, lightTaskbar);
        return Render(visual, px);
    }

    public static void DrawPose(DrawingContext dc, int px, TrayPose pose, bool lightTaskbar)
    {
        dc.PushTransform(new ScaleTransform(px / 16.0, px / 16.0));
        var rect = new Rect(0, 0, 16, 16);
        dc.DrawRoundedRectangle(Theme.Brush(Color.FromRgb(18, 18, 20)), null, rect, 3.2, 3.2);
        dc.PushClip(new RectangleGeometry(rect, 3.2, 3.2));
        dc.PushOpacity(pose.Ink);
        var face = Geometry.Parse("M0,9 C0.4,5.7 3,3.9 6.2,4.1 C9.2,4.3 10.5,5.7 12.5,6.6 C14.8,7.5 16,8.6 16,10.8 L16,16 0,16 Z");
        dc.DrawGeometry(Brushes.White, null, face);
        double glance = pose.ArcLength > 0 ? Math.Sin(pose.ArcStart * Math.PI / 180) * 0.65 : 0;
        var eyePen = new Pen(Brushes.Black, 1.1) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (pose.Eye < 0.99)
        {
            dc.PushOpacity(1 - pose.Eye);
            foreach (double x in new[] { 4.5, 10.5 })
            {
                var lid = new StreamGeometry();
                using (var g = lid.Open())
                {
                    g.BeginFigure(new Point(x - 1.4 + glance, 9.4), false, false);
                    g.QuadraticBezierTo(new Point(x + glance, 10.8), new Point(x + 1.4 + glance, 9.4), true, false);
                }
                dc.DrawGeometry(null, eyePen, lid);
            }
            dc.Pop();
        }
        if (pose.Eye > 0.01)
        {
            dc.PushOpacity(pose.Eye);
            foreach (double x in new[] { 4.5, 10.5 })
                dc.DrawEllipse(Brushes.Black, null, new Point(x + glance, 9.6), 0.95, 1.4);
            dc.Pop();
        }
        dc.Pop();
        dc.Pop();
        double dot = Math.Max(pose.Dot, pose.ArcLength > 0.5 ? Math.Min(1, pose.ArcLength / 100) : 0);
        if (dot > 0.01)
        {
            double radius = 2.15 * Math.Min(1, dot);
            var center = new Point(13.4, 2.6);
            dc.DrawEllipse(Theme.Brush(Color.FromRgb(18, 18, 20)), null, center, radius + 0.45, radius + 0.45);
            dc.DrawEllipse(Theme.Brush(Lerp(Theme.Current.On, Amber, Math.Max(pose.DotTint, pose.ArcTint))), null, center, radius, radius);
        }
        dc.Pop();
    }

    private static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        byte L(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
        return Color.FromArgb(L(a.A, b.A), L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }

    /// Bandeja con el % de RAM, al estilo de los indicadores del sistema (batería,
    /// volumen): el número nítido en el color de la barra de tareas y, debajo, una
    /// barra fina que se llena con el %. El color va solo en la barra: azul con
    /// Ollama encendido, ámbar mientras cambia de estado o con presión alta, coral
    /// con presión crítica y gris con Ollama apagado. Mientras cambia de estado, `shimmer`
    /// (0…1) es una franja de luz que recorre la barra, como un progreso indeterminado;
    /// el número no se mueve.
    public static BitmapSource PercentBitmap(int px, int percent, SystemMemory.Level pressure, Dot dot, bool lightTaskbar, double? shimmer = null)
    {
        var ink = lightTaskbar ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Colors.White;
        var fill = pressure switch
        {
            SystemMemory.Level.Critical => Theme.Current.Danger,
            SystemMemory.Level.Warning => Theme.Current.Warm,
            _ => dot switch
            {
                Dot.On => Theme.Blue,
                Dot.Busy => Amber,
                _ => Theme.WithOpacity(ink, 0.7),
            },
        };
        int value = Math.Clamp(percent, 0, 99);

        // Barra: 2 px a 16 px (100 %), 3 px a 20–24 px, 4 px a 32 px; con aire a los lados.
        int barH = Math.Max(2, (int)Math.Round(px / 8.0));
        int inset = Math.Max(1, (int)Math.Round(px * 0.08));
        int barY = px - barH - (px >= 20 ? 1 : 0);
        int gap = Math.Max(1, px / 12);
        double areaH = barY - gap;   // alto disponible para el número

        // El número: el corte óptico «Small» del tipo variable se lee mejor a tamaño de ícono.
        var family = new FontFamily(px < 24 ? "Segoe UI Variable Small, Segoe UI" : "Segoe UI Variable Text, Segoe UI");
        var face = new Typeface(family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        FormattedText Text(string s, double size) => new(s, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, face, size, Theme.Brush(ink), null, TextFormattingMode.Display, 1.0);
        // Se ajusta al hueco por la tinta de una referencia fija («88»), no por la del
        // valor: si no, un «63» (más estrecho que un «66») o un «7» salen más grandes y
        // el número cambia de tamaño al cambiar el %. Las cifras son tabulares: todas
        // caen en la misma caja, y una sola cifra se centra en ella por su avance.
        double em = px;
        var bounds = Text("88", em).BuildGeometry(new Point()).Bounds;
        double scale = Math.Min(areaH / bounds.Height, (px - 2.0) / bounds.Width);
        em = Math.Floor(em * scale * 2) / 2;
        var reference = Text("88", em);
        bounds = reference.BuildGeometry(new Point()).Bounds;
        var text = Text(value.ToString(System.Globalization.CultureInfo.InvariantCulture), em);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // Encajado en píxeles enteros: arriba del todo y centrado.
            double x = Math.Round((px - bounds.Width) / 2 - bounds.X + (reference.Width - text.Width) / 2);
            double y = Math.Round((areaH - bounds.Height) / 2 - bounds.Y);
            dc.DrawText(text, new Point(x, y));

            double width = px - 2 * inset, r = barH / 2.0;
            dc.DrawRoundedRectangle(Theme.Brush(Theme.WithOpacity(ink, lightTaskbar ? 0.18 : 0.22)), null, new Rect(inset, barY, width, barH), r, r);
            double filled = Math.Max(value > 0 ? barH : 0, Math.Round(width * value / 100.0));
            if (filled > 0) dc.DrawRoundedRectangle(Theme.Brush(fill), null, new Rect(inset, barY, filled, barH), r, r);
            if (shimmer is { } at)
            {
                double band = Math.Max(barH * 2, width * 0.4);
                double bx = inset - band + (width + band) * Math.Clamp(at, 0, 1);
                var light = Theme.WithOpacity(lightTaskbar ? Colors.White : Color.FromRgb(0xFF, 0xF4, 0xDC), lightTaskbar ? 0.8 : 0.7);
                var glow = new LinearGradientBrush(
                    [new GradientStop(Theme.WithOpacity(light, 0), 0), new GradientStop(light, 0.5), new GradientStop(Theme.WithOpacity(light, 0), 1)],
                    new Point(0, 0), new Point(1, 0));
                glow.Freeze();
                dc.PushClip(new RectangleGeometry(new Rect(inset, barY, width, barH), r, r));
                dc.DrawRectangle(glow, null, new Rect(bx, barY, band, barH));
                dc.Pop();
            }
        }
        return Render(visual, px);
    }

    /// Ícono oficial de la app en el tamaño solicitado.
    public static BitmapSource AppIcon(int px) => Logo.IconAt(px);

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

    public LogoView()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        Theme.Changed += InvalidateVisual;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double side = Math.Min(ActualWidth, ActualHeight);
        Logo.DrawIcon(dc, side, Theme.Brush(Mark.IconBackground), Color);
    }
}
