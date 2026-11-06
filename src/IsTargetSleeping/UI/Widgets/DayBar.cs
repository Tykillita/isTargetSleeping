using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace IsTargetSleeping.UI;

/// El día en una barra de 0:00 a 24:00: el cielo de fondo (noche, amanecer, mediodía, atardecer, noche), lo que ya pasó con todo su color y lo que falta apagado,
/// marcas a las 6, 12 y 18 h (con un solito en el mediodía) y un punto luminoso que late en el momento actual.
public sealed class DayBar : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(nameof(Progress), typeof(double), typeof(DayBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    /// Cuánto lleva gastado el día, de 0 a 1.
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    public static readonly DependencyProperty MoonProperty = DependencyProperty.Register(nameof(Moon), typeof(double), typeof(DayBar),
        new FrameworkPropertyMetadata(0.5, FrameworkPropertyMetadataOptions.AffectsRender));
    /// La fase de la luna de hoy, de 0 (nueva) a 1 (vuelta a nueva), pasando por 0,5 (llena): es la luna que se dibuja en los extremos.
    public double Moon { get => (double)GetValue(MoonProperty); set => SetValue(MoonProperty, value); }

    // El latido del punto: de 0 a 1 cada 3 s.
    private static readonly DependencyProperty PulseProperty = DependencyProperty.Register("Pulse", typeof(double), typeof(DayBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public DayBar()
    {
        Height = 15;
        Loaded += (_, _) => { Theme.Changed += InvalidateVisual; UpdateAnimation(); };
        Unloaded += (_, _) => { Theme.Changed -= InvalidateVisual; UpdateAnimation(); };
        IsVisibleChanged += (_, _) => UpdateAnimation();
    }

    private void UpdateAnimation()
    {
        bool run = IsLoaded && IsVisible && SystemParameters.ClientAreaAnimation;
        if (!run) { BeginAnimation(PulseProperty, null); return; }
        var loop = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(3)) { RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(loop, 20);
        BeginAnimation(PulseProperty, loop);
    }

    private static Brush Frozen(Brush brush) { brush.Freeze(); return brush; }

    /// La luna con su fase real y algo de volumen: el disco entero en penumbra (luz cenicienta de la Tierra), un resplandor según lo iluminada que esté y,
    /// encima, la parte iluminada con degradado crema, unos cráteres y un filo suave en el terminador. Creciente (fase < 0,5) se ilumina por la derecha; menguante, por la izquierda.
    /// El borde interior de la parte iluminada (el terminador) es media elipse cuyo ancho va de r a 0 a r según la fase.
    internal static void DrawMoon(DrawingContext dc, Color ink, double cx, double cy, double r, double phase)
    {
        phase = (phase % 1 + 1) % 1;
        bool contrast = Theme.Flat;
        var centre = new Point(cx, cy);
        double c = Math.Cos(phase * 2 * Math.PI);                 // 1 nueva, 0 cuartos, -1 llena
        double illuminated = (1 - c) / 2;                          // 0…1: cuánto del disco está iluminado

        // Resplandor: más grande cuanto más llena.
        if (!contrast && illuminated > 0.03)
        {
            var glow = Frozen(new RadialGradientBrush(Color.FromArgb((byte)(70 * illuminated), 0xE8, 0xEE, 0xFF), Color.FromArgb(0, 0xE8, 0xEE, 0xFF)));
            dc.DrawEllipse(glow, null, centre, r * 2.3, r * 2.3);
        }
        // Penumbra: el disco entero, azulado y apagado.
        var dark = contrast ? Theme.Brush(Theme.WithOpacity(ink, 0.25))
            : Frozen(new RadialGradientBrush(Color.FromRgb(0x5A, 0x66, 0x86), Color.FromRgb(0x2B, 0x33, 0x4F)) { GradientOrigin = new Point(0.4, 0.35), Center = new Point(0.5, 0.5) });
        dc.DrawEllipse(dark, new Pen(Theme.Brush(Theme.WithOpacity(ink, 0.18)), 0.5), centre, r, r);
        if (c > 0.97) return;                                      // luna nueva: solo la penumbra

        bool waxing = phase < 0.5;
        double side = waxing ? 1 : -1;
        var figure = new PathFigure { StartPoint = new Point(cx, cy - r), IsClosed = true };
        // Borde exterior iluminado: de arriba a abajo por el lado de la luz.
        figure.Segments.Add(new ArcSegment(new Point(cx, cy + r), new Size(r, r), 0, false, waxing ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true));
        // Terminador: de abajo a arriba, por el lado de la luz en cuarto (c > 0) o por el de la sombra en gibosa (c < 0).
        figure.Segments.Add(new ArcSegment(new Point(cx, cy - r), new Size(Math.Max(0.01, Math.Abs(c) * r), r), 0, false,
            side * c > 0 ? SweepDirection.Counterclockwise : SweepDirection.Clockwise, true));
        var lit = new PathGeometry([figure]);
        lit.Freeze();

        // Parte iluminada: crema con el brillo hacia el lado de la luz (en alto contraste, del color del texto).
        Brush face = contrast ? Theme.Brush(ink)
            : Frozen(new RadialGradientBrush(Color.FromRgb(0xFF, 0xFA, 0xE6), Color.FromRgb(0xC9, 0xC2, 0xAA)) { GradientOrigin = new Point(0.5 + 0.22 * side, 0.4), Center = new Point(0.5, 0.5), RadiusX = 0.62, RadiusY = 0.62 });
        dc.DrawGeometry(face, null, lit);

        if (!contrast)
        {
            // Cráteres: manchas tenues repartidas por el disco, recortadas a la parte iluminada.
            dc.PushClip(lit);
            var spot = Frozen(new SolidColorBrush(Color.FromArgb(70, 0x8A, 0x82, 0x6C)));
            foreach (var (x, y, k) in new[] { (-0.30, -0.28, 0.26), (0.28, 0.12, 0.32), (-0.12, 0.40, 0.2), (0.22, -0.42, 0.16), (-0.46, 0.08, 0.16) })
                dc.DrawEllipse(spot, null, new Point(cx + x * r, cy + y * r), k * r, k * r);
            dc.Pop();
            // Filo suave en el terminador: una línea fina y oscura en el borde interior de la luz.
            var edge = new PathFigure { StartPoint = new Point(cx, cy + r), IsClosed = false };
            edge.Segments.Add(new ArcSegment(new Point(cx, cy - r), new Size(Math.Max(0.01, Math.Abs(c) * r), r), 0, false, side * c > 0 ? SweepDirection.Counterclockwise : SweepDirection.Clockwise, true));
            var edgeGeometry = new PathGeometry([edge]);
            edgeGeometry.Freeze();
            dc.DrawGeometry(null, new Pen(Frozen(new SolidColorBrush(Color.FromArgb(60, 0x30, 0x30, 0x40))), 0.7), edgeGeometry);
        }
    }

    // Cielo a lo largo del día: medianoche, 6 h, mediodía, 18 h, medianoche.
    private static readonly (double At, Color Color)[] Sky =
    [
        (0.00, Color.FromRgb(0x1B, 0x22, 0x4D)), (0.22, Color.FromRgb(0x4A, 0x4E, 0x8F)), (0.27, Color.FromRgb(0xF2, 0xA6, 0x5A)),
        (0.50, Color.FromRgb(0x8E, 0xC9, 0xF5)), (0.73, Color.FromRgb(0xF0, 0x87, 0x5A)), (0.80, Color.FromRgb(0x6B, 0x4E, 0x8F)), (1.00, Color.FromRgb(0x1B, 0x22, 0x4D)),
    ];

    protected override void OnRender(DrawingContext dc)
    {
        var theme = Theme.Current;
        double w = ActualWidth, h = ActualHeight;
        if (w < 20) return;
        bool contrast = Theme.Flat;
        double progress = Math.Clamp(Progress, 0, 1), pulse = Math.Sin((double)GetValue(PulseProperty) * 2 * Math.PI);
        double trackTop = h - 6, trackH = 4, x = w * progress;
        var track = new Rect(0, trackTop, w, trackH);

        // El cielo: de fondo apagado y, hasta «ahora», con todo su color.
        Brush sky;
        if (contrast) sky = Theme.Brush(theme.Fg);
        else
        {
            var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            foreach (var (at, color) in Sky) gradient.GradientStops.Add(new GradientStop(color, at));
            gradient.Freeze();
            sky = gradient;
        }
        dc.PushClip(new RectangleGeometry(track, 2, 2));
        dc.PushOpacity(contrast ? 0.25 : 0.38);
        dc.DrawRectangle(sky, null, track);
        dc.Pop();
        dc.PushClip(new RectangleGeometry(new Rect(0, trackTop, x, trackH)));
        dc.DrawRectangle(sky, null, track);
        dc.Pop();
        dc.Pop();

        // Marcas a las 6, 12 y 18 h; en el mediodía, un solito.
        var tick = new Pen(Theme.Brush(Theme.WithOpacity(theme.Fg, 0.4)), 1);
        foreach (var at in new[] { 0.25, 0.75 }) dc.DrawLine(tick, new Point(Math.Round(w * at) + 0.5, trackTop - 3), new Point(Math.Round(w * at) + 0.5, trackTop));
        double noon = w * 0.5, sunY = trackTop - 4;
        var warm = Theme.Brush(theme.Warm);
        dc.DrawEllipse(warm, null, new Point(noon, sunY), 2.1, 2.1);
        var ray = new Pen(warm, 0.8);
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            dc.DrawLine(ray, new Point(noon + Math.Cos(a) * 3.2, sunY + Math.Sin(a) * 3.2), new Point(noon + Math.Cos(a) * 4.4, sunY + Math.Sin(a) * 4.4));
        }

        // En los extremos, la noche: una luna creciente a cada lado (medianoche), como el sol del mediodía.
        DrawMoon(dc, theme.Fg, 6.2, sunY + 0.3, 4.2, Moon);
        DrawMoon(dc, theme.Fg, w - 6.2, sunY + 0.3, 4.2, Moon);

        // El momento actual: un punto con halo que late.
        double cx = Math.Clamp(x, 3.5, w - 3.5), cy = trackTop + trackH / 2;
        var halo = new RadialGradientBrush(Theme.WithOpacity(theme.Fg, 0.42 + 0.14 * pulse), Color.FromArgb(0, theme.Fg.R, theme.Fg.G, theme.Fg.B));
        halo.Freeze();
        dc.DrawEllipse(halo, null, new Point(cx, cy), 7 + 1.2 * pulse, 7 + 1.2 * pulse);
        dc.DrawEllipse(Theme.Brush(theme.Fg), null, new Point(cx, cy), 3, 3);
    }
}

/// La luna de hoy, sola y más grande, con su fase real (la misma que dibuja la barra del día en sus extremos).
public sealed class MoonDisc : FrameworkElement
{
    public static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(nameof(Phase), typeof(double), typeof(MoonDisc),
        new FrameworkPropertyMetadata(0.5, FrameworkPropertyMetadataOptions.AffectsRender));
    /// De 0 (nueva) a 1, pasando por 0,5 (llena).
    public double Phase { get => (double)GetValue(PhaseProperty); set => SetValue(PhaseProperty, value); }

    public MoonDisc() { Width = 34; Height = 34; Loaded += (_, _) => Theme.Changed += InvalidateVisual; Unloaded += (_, _) => Theme.Changed -= InvalidateVisual; }

    protected override void OnRender(DrawingContext dc) =>
        DayBar.DrawMoon(dc, Theme.Current.Fg, ActualWidth / 2, ActualHeight / 2, Math.Min(ActualWidth, ActualHeight) * 0.3, Phase);
}
