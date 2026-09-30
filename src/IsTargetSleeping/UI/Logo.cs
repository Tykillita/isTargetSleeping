using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace IsTargetSleeping.UI;

/// Logo de isTargetSleeping: una mira (el objetivo) con el ojo cerrado en el centro.
///
/// Coordenadas de un lienzo de 1024 con la y hacia abajo (las de SVG y WPF).
/// Esta única geometría genera el ícono de la app, la cabecera del panel, el
/// ícono de la bandeja y los SVG/PNG que exporta `--export-logo`.
public static class Logo
{
    public const double Canvas = 1024;
    /// Radio del cuadrado redondeado del ícono (~22,4 %).
    public const double IconRadius = 229;

    public const double RingWidth = 52, TickWidth = 46, EyeWidth = 42, LashWidth = 36;

    /// La mira: anillo centrado y cuatro marcas en cruz que lo atraviesan.
    private static readonly Point Center = new(512, 512);
    private const double RingRadius = 250;
    private const double TickFrom = 190, TickTo = 364;

    /// Ojo cerrado: arco hacia abajo con tres pestañas.
    private const string EyePath = "M 380 496 Q 512 600 644 496";
    private static readonly (Point A, Point B)[] Lashes =
    [
        (new Point(446, 535), new Point(422, 590)),
        (new Point(512, 548), new Point(512, 606)),
        (new Point(578, 535), new Point(602, 590)),
    ];
    /// Dónde va el punto de estado en la bandeja: en la diagonal superior derecha,
    /// entre las marcas N y E.
    public static readonly Point DotCenter = new(790, 212);

    [Flags]
    public enum Parts { None = 0, Lashes = 1, Full = Lashes }

    private static readonly Geometry ring = Frozen(new EllipseGeometry(Center, RingRadius, RingRadius));
    private static readonly Geometry ticks = Frozen(BuildTicks());
    private static readonly Geometry eye = Frozen(Geometry.Parse(EyePath));
    private static readonly Geometry lashes = Frozen(BuildLashes());

    private static Geometry Frozen(Geometry g) { g.Freeze(); return g; }

    private static IEnumerable<(Point A, Point B)> TickLines()
    {
        double x = Center.X, y = Center.Y;
        yield return (new Point(x, y - TickTo), new Point(x, y - TickFrom));   // N
        yield return (new Point(x + TickFrom, y), new Point(x + TickTo, y));   // E
        yield return (new Point(x, y + TickFrom), new Point(x, y + TickTo));   // S
        yield return (new Point(x - TickTo, y), new Point(x - TickFrom, y));   // O
    }

    private static Geometry BuildTicks()
    {
        var group = new GeometryGroup();
        foreach (var (a, b) in TickLines()) group.Children.Add(new LineGeometry(a, b));
        return group;
    }

    private static Geometry BuildLashes()
    {
        var group = new GeometryGroup();
        foreach (var (a, b) in Lashes) group.Children.Add(new LineGeometry(a, b));
        return group;
    }

    private static Pen Pen(Brush brush, double width)
    {
        var p = new Pen(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        p.Freeze();
        return p;
    }

    /// Dibuja el logo dentro de `rect` (el lienzo de 1024 escalado a su lado menor).
    /// `weight` engorda los trazos para tamaños pequeños.
    public static void Draw(DrawingContext dc, Rect rect, Brush color, Parts parts = Parts.Full, double weight = 1)
    {
        double k = Math.Min(rect.Width, rect.Height) / Canvas;
        dc.PushTransform(new MatrixTransform(k, 0, 0, k, rect.X, rect.Y));
        dc.DrawGeometry(null, Pen(color, RingWidth * weight), ring);
        dc.DrawGeometry(null, Pen(color, TickWidth * weight), ticks);
        dc.DrawGeometry(null, Pen(color, EyeWidth * weight), eye);
        if (parts.HasFlag(Parts.Lashes)) dc.DrawGeometry(null, Pen(color, LashWidth * weight), lashes);
        dc.Pop();
    }

    /// Ícono completo: cuadrado redondeado de fondo y logo encima.
    public static void DrawIcon(DrawingContext dc, double px, Brush background, Brush color)
    {
        double r = px * IconRadius / Canvas;
        dc.DrawRoundedRectangle(background, null, new Rect(0, 0, px, px), r, r);
        Draw(dc, new Rect(0, 0, px, px), color, Parts.Full, px < 40 ? 1.2 : 1);
    }

    /// SVG equivalente al dibujo de `Draw`. `background` añade el cuadrado del ícono.
    public static string Svg(string color, string? background = null)
    {
        static string N(double v) => v.ToString(CultureInfo.InvariantCulture);
        static string Stroke(string c, double w) =>
            $"fill=\"none\" stroke=\"{c}\" stroke-width=\"{N(w)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"";
        var s = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1024 1024\" width=\"1024\" height=\"1024\">";
        s += $"<title>{AppInfo.Name}</title>";
        if (background is not null) s += $"<rect width=\"1024\" height=\"1024\" rx=\"{N(IconRadius)}\" fill=\"{background}\"/>";
        s += $"<circle cx=\"{N(Center.X)}\" cy=\"{N(Center.Y)}\" r=\"{N(RingRadius)}\" {Stroke(color, RingWidth)}/>";
        foreach (var (a, b) in TickLines())
            s += $"<line x1=\"{N(a.X)}\" y1=\"{N(a.Y)}\" x2=\"{N(b.X)}\" y2=\"{N(b.Y)}\" {Stroke(color, TickWidth)}/>";
        s += $"<path d=\"{EyePath}\" {Stroke(color, EyeWidth)}/>";
        foreach (var (a, b) in Lashes)
            s += $"<line x1=\"{N(a.X)}\" y1=\"{N(a.Y)}\" x2=\"{N(b.X)}\" y2=\"{N(b.Y)}\" {Stroke(color, LashWidth)}/>";
        return s + "</svg>\n";
    }
}
