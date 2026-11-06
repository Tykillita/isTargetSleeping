using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace IsTargetSleeping.UI;

/// Un anillo de progreso con carácter pero sobrio: pista tenue, arco que se aclara a lo largo del recorrido (de gris a blanco), extremos redondeados
/// y un punto luminoso en la punta. Al aparecer, el arco se dibuja de cero hasta su valor. Con alto contraste, un arco liso del color del texto.
public sealed class ArcGauge : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(nameof(Progress), typeof(double), typeof(ArcGauge),
        new PropertyMetadata(0.0, (d, e) => ((ArcGauge)d).Animate((double)e.NewValue)));
    /// De 0 a 1.
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    public static readonly DependencyProperty AlertProperty = DependencyProperty.Register(nameof(Alert), typeof(bool), typeof(ArcGauge),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    /// Pinta el anillo del color de peligro del tema (una batería casi vacía).
    public bool Alert { get => (bool)GetValue(AlertProperty); set => SetValue(AlertProperty, value); }

    private static readonly DependencyProperty ShownProperty = DependencyProperty.Register("Shown", typeof(double), typeof(ArcGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public ArcGauge()
    {
        Loaded += (_, _) => { Theme.Changed += InvalidateVisual; Animate(Progress, fromZero: true); };
        Unloaded += (_, _) => Theme.Changed -= InvalidateVisual;
    }

    private void Animate(double to, bool fromZero = false)
    {
        if (!IsLoaded || !SystemParameters.ClientAreaAnimation) { SetValue(ShownProperty, to); return; }
        BeginAnimation(ShownProperty, new DoubleAnimation(fromZero ? 0 : (double)GetValue(ShownProperty), to, TimeSpan.FromMilliseconds(900)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    // El arco empieza tenue y termina en el color del texto: el mismo degradado de brillo, sin tonos.
    private Color At(double t) => Theme.WithOpacity(Alert ? Theme.Current.Danger : Theme.Current.Fg, 0.38 + 0.62 * Math.Clamp(t, 0, 1));

    protected override void OnRender(DrawingContext dc)
    {
        var theme = Theme.Current;
        double side = Math.Min(ActualWidth, ActualHeight), stroke = Math.Max(3, side * 0.11), r = side / 2 - stroke / 2 - 1;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        double shown = Math.Clamp((double)GetValue(ShownProperty), 0, 1);
        bool flat = Theme.Flat;
        dc.DrawEllipse(null, new Pen(Theme.Brush(Theme.WithOpacity(theme.Fg, 0.14)), stroke), c, r, r);
        if (shown <= 0.001) return;

        // El arco, en tramos cortos con el color que toca a cada altura (WPF no tiene degradado angular).
        double sweep = shown * 360 - 0.01, start = -90;
        int segments = Math.Max(1, (int)Math.Ceiling(sweep / 2));   // tramos finos: en un anillo grande los de 6° dejaban costuras visibles
        for (int i = 0; i < segments; i++)
        {
            double a0 = start + sweep * i / segments, a1 = start + sweep * (i + 1) / segments + (i < segments - 1 ? 0.8 : 0);   // un pequeño solape tapa las costuras
            var pen = new Pen(Theme.Brush(flat ? theme.Fg : At((a0 + 90) / 360)), stroke) { StartLineCap = i == 0 ? PenLineCap.Round : PenLineCap.Flat, EndLineCap = i == segments - 1 ? PenLineCap.Round : PenLineCap.Flat };
            var figure = new PathFigure { StartPoint = Polar(c, r, a0) };
            figure.Segments.Add(new ArcSegment(Polar(c, r, a1), new Size(r, r), 0, a1 - a0 > 180, SweepDirection.Clockwise, true));
            var geometry = new PathGeometry([figure]);
            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
        // La punta: un punto blanco con halo del color del final del arco.
        var tip = Polar(c, r, start + sweep);
        if (!flat)
        {
            var halo = new RadialGradientBrush(Theme.WithOpacity(theme.Fg, 0.45), Color.FromArgb(0, theme.Fg.R, theme.Fg.G, theme.Fg.B));
            halo.Freeze();
            dc.DrawEllipse(halo, null, tip, stroke * 1.6, stroke * 1.6);
        }
        dc.DrawEllipse(Theme.Brush(theme.Fg), null, tip, stroke * 0.32, stroke * 0.32);
    }

    private static Point Polar(Point centre, double r, double degrees) =>
        new(centre.X + r * Math.Cos(degrees * Math.PI / 180), centre.Y + r * Math.Sin(degrees * Math.PI / 180));
}
