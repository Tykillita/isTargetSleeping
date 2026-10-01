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

    /// Bandeja: la mira dibujada a mano para el tamaño del ícono, no el logo grande
    /// reducido. Las marcas de la cruz son rectángulos encajados en píxeles, el anillo
    /// tiene un trazo de píxeles enteros y el punto de estado es una insignia con su
    /// hueco alrededor, como los de Windows. El ojo cuenta el estado del modelo: cerrado
    /// (con pestañas) mientras duerme y abierto —el centro de la diana— si hay un modelo
    /// en memoria. Blanco sobre barra oscura y casi negro sobre barra clara, como los
    /// íconos del sistema; con Ollama apagado, algo más suave (sin quedar lavado).
    public static BitmapSource StatusBitmap(int px, Dot dot, bool dimmed, bool lightTaskbar, bool awake = false) =>
        PoseBitmap(px, new TrayPose(1, 0, 0, 0, 0, 0, awake ? 1 : 0, dot == Dot.None ? 0 : 1, dot == Dot.Busy ? 1 : 0,
            dimmed ? TrayPose.DimInk : 1), lightTaskbar);

    public static BitmapSource PoseBitmap(int px, TrayPose pose, bool lightTaskbar)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) DrawPose(dc, px, pose, lightTaskbar);
        return Render(visual, px);
    }

    /// Cualquier fotograma de la animación (`TrayMotion`) con la misma geometría: las
    /// poses fijas dan exactamente el ícono de siempre. El anillo parcial y el arco del
    /// radar son arcos con el mismo trazo y extremos redondos; el desplazamiento de las
    /// marcas se redondea al píxel para que no se emborronen; el ojo pasa del párpado
    /// (que se desvanece) a la diana (que crece).
    public static void DrawPose(DrawingContext dc, int px, TrayPose pose, bool lightTaskbar)
    {
        var ink = Theme.WithOpacity(lightTaskbar ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Colors.White, pose.Ink);
        var brush = Theme.Brush(ink);

        double u = px / 16.0;                                   // rejilla de diseño de 16
        int sw = px < 20 ? 1 : px < 28 ? 2 : (int)Math.Round(px / 12.0);   // marcas: 1 px a 16, 2 a 20–24, 3 a 32…
        // El anillo, un poco más grueso que 1 px a 16: un círculo de 1 px se reparte entre
        // dos píxeles y se ve gris y punteado al lado de las marcas nítidas.
        double ringW = px < 20 ? 1.5 : sw;
        double c = Math.Floor(px / 2.0) - (sw % 2 == 1 ? 0.5 : 0);   // centro en medio píxel si el trazo es impar
        int r = (int)Math.Round(5.25 * u, MidpointRounding.AwayFromZero);   // radio del anillo
        int tick = (int)Math.Round(3.5 * u, MidpointRounding.AwayFromZero); // largo de cada marca desde el borde
        int edge = (int)Math.Floor(c - r - sw / 2.0) - (int)Math.Round(1.5 * u);   // dónde empiezan las marcas
        edge = Math.Max(0, edge);
        var center = new Point(c, c);

        // Insignia de estado: arriba a la derecha, con un hueco que recorta la mira. Al
        // saltar crece (y el hueco con ella); al encoger se va apagando.
        double dotRest = Math.Max(2.5, 2.6 * u), dotR = dotRest * pose.Dot;
        var dotC = new Point(px - dotRest - 0.25 * u, dotRest + 0.25 * u);
        bool badge = pose.Dot > 0.01;
        if (badge)
        {
            double holeR = dotR + Math.Max(1, u) * Math.Min(1, pose.Dot);
            var hole = new EllipseGeometry(dotC, holeR, holeR);
            dc.PushClip(Geometry.Combine(new RectangleGeometry(new Rect(0, 0, px, px)), hole, GeometryCombineMode.Exclude, null));
        }

        // El anillo tenue bajo el radar.
        if (pose.RingFaint > 0.01)
            dc.DrawEllipse(null, new Pen(Theme.Brush(Theme.WithOpacity(ink, 0.32 * pose.RingFaint)), ringW), center, r, r);
        // El anillo sólido, centrado abajo: se deshace ◠ → ◡ → ·.
        if (pose.Ring >= 0.999) dc.DrawEllipse(null, new Pen(brush, ringW), center, r, r);
        else if (pose.Ring > 0.001) DrawArc(dc, new Pen(brush, ringW), center, r, 180 - 180 * pose.Ring, 360 * pose.Ring);
        // El arco del radar, de ámbar a tinta mientras se cierra en el anillo.
        if (pose.ArcLength > 0.5)
        {
            var pen = new Pen(Theme.Brush(Lerp(ink, Theme.WithOpacity(Amber, pose.Ink), pose.ArcTint)), ringW);
            if (pose.ArcLength >= 359.5) dc.DrawEllipse(null, pen, center, r, r);
            else DrawArc(dc, pen, center, r, pose.ArcStart, pose.ArcLength);
        }

        // Las cuatro marcas, del borde hacia dentro, cruzando el anillo (con el rebote del fijado).
        int shift = (int)Math.Round(pose.TickShift * u, MidpointRounding.AwayFromZero);
        double half = sw / 2.0;
        int near = edge + shift, far = px - edge - shift;
        dc.DrawRectangle(brush, null, new Rect(c - half, near, sw, tick));            // N
        dc.DrawRectangle(brush, null, new Rect(c - half, far - tick, sw, tick));      // S
        dc.DrawRectangle(brush, null, new Rect(near, c - half, tick, sw));            // O
        dc.DrawRectangle(brush, null, new Rect(far - tick, c - half, tick, sw));      // E

        if (pose.Eye > 0.01)
        {
            // Despierto: el ojo abierto es el centro de la diana; al abrirse crece desde el párpado.
            double pupil = Math.Max(1.5, 1.75 * u) * pose.Eye;
            dc.DrawEllipse(brush, null, center, pupil, pupil);
        }
        if (pose.Eye < 0.99)
        {
            // Dormido: el párpado cerrado con sus pestañas (sin ellas parece una sonrisa).
            // Las pestañas salen de la curva del párpado hacia abajo y hacia fuera.
            bool fading = pose.Eye > 0.01;
            if (fading) dc.PushOpacity(1 - pose.Eye);
            double lw = px < 20 ? 1.25 : sw * 0.85;
            var lid = new Pen(brush, lw) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            // Párpado: ancho, altura de los extremos y cuánto baja (más plano a 16 px, o parece una ✓).
            double w = 2.9 * u, top = c - (px < 20 ? 0.3 : 0.9) * u, sag = (px < 20 ? 1.1 : 1.9) * u;
            var eye = new StreamGeometry();
            using (var g = eye.Open())
            {
                g.BeginFigure(new Point(c - w, top), false, false);
                g.QuadraticBezierTo(new Point(c, top + 2 * sag), new Point(c + w, top), true, false);
            }
            dc.DrawGeometry(null, lid, eye);
            // Punto de la curva a una fracción t del ancho (t = 0 es el centro).
            Point On(double t) => new(c + t * w, top + sag * (1 - t * t));
            double len = 1.25 * u;
            var mid = On(0);
            dc.DrawLine(lid, mid, new Point(mid.X, mid.Y + len));
            if (px >= 20)
            {
                foreach (var t in new[] { -0.62, 0.62 })
                {
                    var p = On(t);
                    dc.DrawLine(lid, p, new Point(p.X + Math.Sign(t) * 0.75 * len, p.Y + 0.8 * len));
                }
            }
            if (fading) dc.Pop();
        }

        if (badge)
        {
            dc.Pop();
            var tint = Lerp(Theme.Current.On, Amber, pose.DotTint);
            dc.DrawEllipse(Theme.Brush(Theme.WithOpacity(tint, Math.Min(1, pose.Dot))), null, dotC, dotR, dotR);
        }
    }

    /// Arco de la circunferencia: grados, 0 arriba y en el sentido del reloj.
    private static void DrawArc(DrawingContext dc, Pen pen, Point center, double r, double start, double length)
    {
        pen.StartLineCap = pen.EndLineCap = PenLineCap.Round;
        Point At(double deg)
        {
            double a = deg * Math.PI / 180;
            return new(center.X + r * Math.Sin(a), center.Y - r * Math.Cos(a));
        }
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(At(start), false, false);
            if (length < 0.01) g.LineTo(At(start), true, false);   // un punto: el extremo redondo
            else g.ArcTo(At(start + length), new Size(r, r), 0, length > 180, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
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
