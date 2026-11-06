using System.Windows;
using System.Windows.Media;

namespace IsTargetSleeping.UI;

/// El icono del tiempo (código WMO de Open-Meteo): sol o luna, nubes con volumen, lluvia, nieve, tormenta o niebla, dibujado con formas y degradados.
/// Las fuentes de iconos de Windows no traen un juego de tiempo completo, y así se ve igual en todas las versiones.
/// Con `Animated` se mueve muy poco: los rayos del sol giran despacio, las estrellas titilan, las nubes se mecen, la lluvia y la nieve caen, la tormenta da un destello y la niebla se desliza.
/// Con `Night` el cielo despejado o poco nublado enseña una luna con estrellas en vez del sol. Con alto contraste se dibuja plano, con el color del texto.
public sealed class WeatherIcon : FrameworkElement
{
    public static readonly DependencyProperty CodeProperty = DependencyProperty.Register(nameof(Code), typeof(int), typeof(WeatherIcon),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));
    public int Code { get => (int)GetValue(CodeProperty); set => SetValue(CodeProperty, value); }

    public static readonly DependencyProperty NightProperty = DependencyProperty.Register(nameof(Night), typeof(bool), typeof(WeatherIcon),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool Night { get => (bool)GetValue(NightProperty); set => SetValue(NightProperty, value); }

    public static readonly DependencyProperty AnimatedProperty = DependencyProperty.Register(nameof(Animated), typeof(bool), typeof(WeatherIcon),
        new PropertyMetadata(false, (d, _) => ((WeatherIcon)d).UpdateAnimation()));
    public bool Animated { get => (bool)GetValue(AnimatedProperty); set => SetValue(AnimatedProperty, value); }

    // La vuelta completa de la animación, de 0 a 1 cada 8 s.
    private static readonly DependencyProperty PhaseProperty = DependencyProperty.Register("Phase", typeof(double), typeof(WeatherIcon),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly TimeSpan AnimationPeriod = TimeSpan.FromSeconds(8);

    private static readonly Dictionary<Color, SolidColorBrush> SolidBrushes = [];
    private static readonly Dictionary<(Color Color, double Width), Pen> RoundPens = [];
    private static readonly Dictionary<Color, Brush> HaloBrushes = [];
    private static readonly Color NightGlow = Hex("#8EA4FF"), SnowColor = Hex("#EAF3FF"), SunRayColor = Hex("#FFC43D"), MoonStarColor = Hex("#FFF6C2");
    private readonly TranslateTransform contentOffset = new();
    private readonly ScaleTransform contentScale = new();
    private readonly TranslateTransform cloudOffset = new();
    private readonly ScaleTransform moonScale = new();
    private readonly TranslateTransform moonOffset = new();
    private readonly TransformGroup moonTransform = new();
    private bool animationRunning;

    public WeatherIcon()
    {
        Width = 28; Height = 28;
        moonTransform.Children.Add(moonScale);
        moonTransform.Children.Add(moonOffset);
        Loaded += (_, _) => { Theme.Changed += InvalidateVisual; UpdateAnimation(); };
        Unloaded += (_, _) => { Theme.Changed -= InvalidateVisual; UpdateAnimation(); };
        IsVisibleChanged += (_, _) => UpdateAnimation();
    }

    private void UpdateAnimation()
    {
        // Sin animación si Windows las tiene apagadas, si no se pidió o si el icono no se ve: no gasta nada.
        bool run = Animated && IsLoaded && IsVisible && SystemParameters.ClientAreaAnimation;
        if (run == animationRunning) return;
        animationRunning = run;
        if (run) CompositionTarget.Rendering += OnRendering;
        else CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs frame) return;
        double phase = frame.RenderingTime.Ticks % AnimationPeriod.Ticks / (double)AnimationPeriod.Ticks;
        SetValue(PhaseProperty, phase);
    }

    // ── Pinceles y formas fijos (se crean una vez) ──────────────────────────────────────────────────────────────────────────────
    private static T F<T>(T freezable) where T : Freezable { freezable.Freeze(); return freezable; }
    private static LinearGradientBrush Vertical(Color top, Color bottom) => F(new LinearGradientBrush(top, bottom, 90));
    private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static readonly Brush CloudFill = Vertical(Hex("#FFFFFF"), Hex("#C3CEE4")), CloudStorm = Vertical(Hex("#8D96AD"), Hex("#4A5168")), CloudStormFlash = Vertical(Hex("#E8ECF8"), Hex("#8D96AD")),
        CloudShade = F(new LinearGradientBrush(Color.FromArgb(0, 0x6B, 0x7C, 0xA8), Color.FromArgb(110, 0x6B, 0x7C, 0xA8), 90)),
        CloudGloss = F(new RadialGradientBrush(Color.FromArgb(150, 255, 255, 255), Color.FromArgb(0, 255, 255, 255))),
        SunCore = F(new RadialGradientBrush(Hex("#FFF6C2"), Hex("#FFB52E")) { GradientOrigin = new Point(0.4, 0.35) }),
        MoonFace = F(new RadialGradientBrush(Hex("#FFFBE8"), Hex("#CFC8B0")) { GradientOrigin = new Point(0.35, 0.35) }),
        Bolt = Vertical(Hex("#FFE98A"), Hex("#FF9F1C")), Raindrop = Vertical(Hex("#9AD3FF"), Hex("#3F8FFF"));
    private static readonly Brush Snow = F(new SolidColorBrush(SnowColor));
    private static readonly Pen RainPen = F(new Pen(Raindrop, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
    private static readonly Pen FogPen = F(new Pen(F(new LinearGradientBrush(new GradientStopCollection
    {
        new(Color.FromArgb(0, 0xE6, 0xEC, 0xF8), 0), new(Color.FromArgb(235, 0xE6, 0xEC, 0xF8), 0.3),
        new(Color.FromArgb(235, 0xE6, 0xEC, 0xF8), 0.7), new(Color.FromArgb(0, 0xE6, 0xEC, 0xF8), 1),
    }, new Point(0, 0), new Point(1, 0)) { MappingMode = BrushMappingMode.RelativeToBoundingBox }), 1.9)
    { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
    private static readonly Pen SunRayPen = F(new Pen(F(new SolidColorBrush(SunRayColor)), 1.5)
    { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

    private static SolidColorBrush Solid(Color color)
    {
        if (!SolidBrushes.TryGetValue(color, out var brush)) SolidBrushes[color] = brush = F(new SolidColorBrush(color));
        return brush;
    }

    private static Pen RoundPen(Brush brush, double width)
    {
        if (brush is not SolidColorBrush solid) return F(new Pen(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        var key = (solid.Color, width);
        if (!RoundPens.TryGetValue(key, out var pen))
            RoundPens[key] = pen = F(new Pen(Solid(solid.Color), width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        return pen;
    }

    private static Brush Halo(Color color)
    {
        if (!HaloBrushes.TryGetValue(color, out var brush))
        {
            var center = Theme.WithOpacity(color, 0.34);
            HaloBrushes[color] = brush = F(new RadialGradientBrush(center, Color.FromArgb(0, color.R, color.G, color.B)));
        }
        return brush;
    }

    // Una nube: tres círculos y una base como una sola forma (una unión), para poder darle un solo degradado sin costuras.
    private static readonly Geometry CloudShape = F(new GeometryGroup
    {
        FillRule = FillRule.Nonzero,
        Children =
        {
            new EllipseGeometry(new Point(8.2, 15.2), 4.4, 4.4), new EllipseGeometry(new Point(13.2, 11.2), 5.6, 5.6), new EllipseGeometry(new Point(18.2, 15.4), 3.9, 3.9),
            new RectangleGeometry(new Rect(5.5, 14.2, 15.2, 5.6), 2.8, 2.8),
        },
    });
    private static readonly Geometry BoltShape = F(Geometry.Parse("M13.2 14.3 L8.4 20.4 H11.6 L10.4 24.2 L16 17.2 H12.8 L14.2 14.3 Z"));
    private static readonly Geometry Crescent = F(new CombinedGeometry(GeometryCombineMode.Exclude, new EllipseGeometry(new Point(12, 12), 5.6, 5.6), new EllipseGeometry(new Point(14.6, 10.4), 4.9, 4.9)));

    protected override void OnRender(DrawingContext dc)
    {
        double side = Math.Min(ActualWidth, ActualHeight);
        if (side < 4) return;
        double phase = (double)GetValue(PhaseProperty), wave = Math.Sin(phase * 2 * Math.PI);
        var theme = Theme.Current;
        bool flat = Theme.Flat;
        var ink = Solid(theme.Fg);
        Brush cloud = flat ? ink : CloudFill, storm = flat ? ink : CloudStorm, warm = flat ? ink : SunCore;

        // Un resplandor suave detrás, del color del tiempo: ámbar con sol, azul con lluvia o tormenta, gris con nubes o niebla, azulado de noche.
        bool clearSky = Code is 0 or 1 or 2;
        var glow = clearSky ? (Night ? NightGlow : theme.Warm) : Code is >= 51 and <= 67 or >= 80 and <= 82 or >= 95 ? theme.On : theme.Muted;
        if (!flat)
            dc.DrawEllipse(Halo(glow), null, new Point(ActualWidth / 2, ActualHeight / 2), side * 0.62, side * 0.62);
        contentOffset.X = (ActualWidth - side) / 2;
        contentOffset.Y = (ActualHeight - side) / 2;
        contentScale.ScaleX = contentScale.ScaleY = side / 24;
        dc.PushTransform(contentOffset);
        dc.PushTransform(contentScale);
        switch (Code)
        {
            case 0: Body(dc, warm, 12, 12, 4.8 + 0.2 * wave, phase, flat); break;
            case 1 or 2: Body(dc, warm, 9, 9, 3.8 + 0.15 * wave, phase, flat); Cloud(dc, cloud, 2.5 + 0.7 * wave, 3.5, false, flat); break;
            case 3: Cloud(dc, cloud, 0.8 * wave, 1, true, flat); break;
            case 45 or 48:
                for (int i = 0; i < 4; i++)
                {
                    double shift = 1.4 * Math.Sin(phase * 2 * Math.PI + i * Math.PI / 2), x0 = 3.5 + (i % 2) * 2.2 + shift, x1 = 20.5 - (i % 2) * 2.2 + shift;
                    dc.DrawLine(flat ? RoundPen(ink, 1.9) : FogPen, new Point(x0, 6.5 + i * 3.7), new Point(x1, 6.5 + i * 3.7));
                }
                break;
            case >= 51 and <= 67 or >= 80 and <= 82: Cloud(dc, cloud, 0.4 * wave, -2.5, true, flat); Drops(dc, phase, flat, ink); break;
            case >= 71 and <= 77 or 85 or 86:
                Cloud(dc, cloud, 0.4 * wave, -2.5, true, flat);
                for (int i = 0; i < 3; i++)
                {
                    double fall = (phase + i / 3.0) % 1;
                    dc.PushOpacity(Math.Min(1, 3 * (1 - fall)));
                    Flake(dc, flat ? ink : Snow, 7 + i * 5 + 0.8 * Math.Sin(fall * 2 * Math.PI), 19.8 + fall * 4, 1.5, fall * 90);
                    dc.Pop();
                }
                break;
            case >= 95:
                bool flash = phase is > 0.78 and < 0.86;
                Cloud(dc, flat ? ink : flash ? CloudStormFlash : storm, 0.4 * wave, -2.5, true, flat);
                // El rayo da un destello breve una vez por vuelta.
                dc.PushOpacity(flash ? 1 : 0.82);
                dc.DrawGeometry(flat ? ink : Bolt, null, BoltShape);
                dc.Pop();
                break;
            default: Cloud(dc, cloud, 0.8 * wave, 1, true, flat); break;
        }
        dc.Pop();
        dc.Pop();
    }

    /// Sol de día (disco con degradado y rayos largos y cortos) o luna con estrellas de noche.
    private void Body(DrawingContext dc, Brush brush, double cx, double cy, double r, double phase, bool flat)
    {
        if (!Night)
        {
            dc.DrawEllipse(brush, null, new Point(cx, cy), r, r);
            var pen = flat ? RoundPen(brush, 1.5) : SunRayPen;
            for (int i = 0; i < 8; i++)
            {
                double a = (i * 45 + phase * 45) * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a), near = r + 1.8, far = r + (i % 2 == 0 ? 4.4 : 3.4);
                dc.DrawLine(pen, new Point(cx + c * near, cy + s * near), new Point(cx + c * far, cy + s * far));
            }
            return;
        }
        // Luna creciente: el círculo grande menos otro desplazado, con degradado crema; escalada y movida al sitio del sol.
        double k = r / 4.8;
        moonScale.ScaleX = moonScale.ScaleY = k;
        moonScale.CenterX = moonScale.CenterY = 12;
        moonOffset.X = cx - 12;
        moonOffset.Y = cy - 12;
        dc.PushTransform(moonTransform);
        dc.DrawGeometry(flat ? brush : MoonFace, null, Crescent);
        dc.Pop();
        // Estrellas que titilan, cada una con su fase.
        var star = flat ? brush : Solid(MoonStarColor);
        Star(dc, star, cx + r * 1.15, cy - r * 0.95, 1.2, phase, 0);
        Star(dc, star, cx + r * 0.6, cy + r * 1.2, 0.85, phase, 0.33);
        Star(dc, star, cx - r * 0.2, cy - r * 1.4, 0.7, phase, 0.66);
    }

    private static void Star(DrawingContext dc, Brush brush, double x, double y, double size, double phase, double offset)
    {
        double twinkle = 0.45 + 0.55 * (0.5 + 0.5 * Math.Sin((phase + offset) * 2 * Math.PI));
        dc.PushOpacity(twinkle);
        Flake(dc, brush, x, y, size, 0);
        dc.Pop();
    }

    /// Una nube con degradado, un poco de sombra abajo y un brillo arriba a la izquierda (con `volume`); sin ellos, solo la forma.
    private void Cloud(DrawingContext dc, Brush brush, double dx, double dy, bool volume, bool flat)
    {
        cloudOffset.X = dx;
        cloudOffset.Y = dy;
        dc.PushTransform(cloudOffset);
        dc.DrawGeometry(brush, null, CloudShape);
        if (!flat)
        {
            dc.PushClip(CloudShape);
            dc.DrawRectangle(CloudShade, null, new Rect(3, 14, 20, 7));
            dc.DrawEllipse(CloudGloss, null, new Point(11, 9.4), 4.2, 3);
            dc.Pop();
        }
        dc.Pop();
    }

    /// Una estrella o copo de varias puntas (tres líneas cruzadas) de radio r, girada `degrees`.
    private static void Flake(DrawingContext dc, Brush brush, double cx, double cy, double r, double degrees)
    {
        var pen = RoundPen(brush, Math.Max(0.5, r * 0.45));
        for (int i = 0; i < 3; i++)
        {
            double a = (degrees + i * 60) * Math.PI / 180, c = Math.Cos(a) * r, s = Math.Sin(a) * r;
            dc.DrawLine(pen, new Point(cx - c, cy - s), new Point(cx + c, cy + s));
        }
    }

    /// Gotas de dos tamaños que caen bajo la nube y se desvanecen al llegar abajo.
    private static void Drops(DrawingContext dc, double phase, bool flat, Brush ink)
    {
        var pen = flat ? RoundPen(ink, 1.5) : RainPen;
        for (int i = 0; i < 4; i++)
        {
            double fall = (phase * 2 + i / 4.0) % 1, length = i % 2 == 0 ? 2.8 : 2.0;
            double x = 7.5 + i * 3.2 - fall * 1.1, y = 17.4 + fall * 5.2;
            dc.PushOpacity(Math.Min(1, 2.5 * (1 - fall)));
            dc.DrawLine(pen, new Point(x, y), new Point(x - 0.8, y + length));
            dc.Pop();
        }
    }
}
