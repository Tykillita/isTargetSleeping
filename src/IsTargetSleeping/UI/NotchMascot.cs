using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Stroke = System.Windows.Shapes.Path;   // `Path` choca con System.IO.Path de los usings globales

namespace IsTargetSleeping.UI;

public enum MascotPose { Open, Left, Right, Squint, Smile, Sleep, Happy }

/// La mascota del notch cerrado: se adueña de la pastilla con su propio cuerpo blanco (hombros y esquinas como el notch) y dos ojos que miran,
/// parpadean, sonríen y se duermen. Dibujada en vectores, sin imágenes. Con `Reduced` se queda con los ojos abiertos y no anima nada.
public sealed class NotchMascot : Grid
{
    // Rostro del SVG del navbar, adaptado al ancho de la pastilla de 150 px.
    private const double Ear = 7, Radius = 14, EyeGap = 11.36, LookShift = 2.95;
    private static readonly Rect EyeBox = new(0, 0, 12, 14);

    private static readonly Geometry OpenEye = Frozen(new EllipseGeometry(new Point(6, 7), 2.84, 3.36));
    private static readonly Geometry SquintEye = Frozen(new EllipseGeometry(new Point(6, 7), 2.84, 2.14));
    private static readonly Geometry SmileEye = Frozen(Geometry.Parse("M 0.3,5 Q 6,10 11.7,5"));
    private static readonly Geometry SleepEye = Frozen(Geometry.Parse("M 0.3,8.07 Q 6,8.93 11.7,8.07"));
    private static readonly Geometry HappyEye = Frozen(Geometry.Parse("M 0.3,8.43 Q 6,2.72 11.7,8.43"));   // ojos en arco hacia arriba: «^ ^»

    private readonly Stroke body = new() { IsHitTestVisible = false };
    private readonly Stroke[] eyes = [new(), new()];
    private readonly TranslateTransform[] look = [new(-EyeGap, 0), new(EyeGap, 0)];
    private readonly ScaleTransform[] blink = [new(1, 1), new(1, 1)];
    private readonly DispatcherTimer brain = new();
    private readonly Random rng = new();
    private readonly Brush ink;
    private readonly Ellipse[] blush = [new(), new()];
    private readonly DispatcherTimer greetTimer = new() { Interval = TimeSpan.FromMilliseconds(380) };
    private MascotPose pose = MascotPose.Sleep;
    private bool awake, greeting;

    public bool Reduced { get; set; }

    /// El saludo cuando el usuario vuelve: abre los ojos y, un instante después, se pone feliz (ojos en arco) y sonrojada.
    public bool Greeting
    {
        get => greeting;
        set
        {
            if (greeting == value) return;
            greeting = value;
            if (value) Greet(); else { greetTimer.Stop(); ShowBlush(false); }
        }
    }

    /// La tinta de los ojos: los indicadores de la pastilla (que van encima del cuerpo blanco) la usan en lugar del blanco del tema.
    public Brush Ink => ink;

    /// Dormida (ojos cerrados) o despierta (mira, parpadea, sonríe). Tras `Show()` empieza dormida.
    public bool Awake
    {
        get => awake;
        set
        {
            if (awake == value) return;
            awake = value;
            if (value) Wake(); else Doze();
        }
    }


    public NotchMascot()
    {
        IsHitTestVisible = false;
        var white = new SolidColorBrush(Color.FromRgb(0xF7, 0xF8, 0xFB));
        white.Freeze();
        body.Fill = SystemParameters.HighContrast ? SystemColors.WindowBrush : white;
        Children.Add(body);

        ink = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(Color.FromRgb(0x0B, 0x0C, 0x10));
        for (int i = 0; i < 2; i++)
        {
            var eye = eyes[i];
            eye.Width = EyeBox.Width; eye.Height = EyeBox.Height; eye.Stretch = Stretch.None;
            eye.HorizontalAlignment = HorizontalAlignment.Center; eye.VerticalAlignment = VerticalAlignment.Center;
            eye.Margin = new Thickness(0, 0, 0, 14.7);   // adapta la cara del SVG del navbar a la pastilla visible de 30 px
            eye.RenderTransformOrigin = new Point(0.5, 0.5);
            eye.RenderTransform = new TransformGroup { Children = { blink[i], look[i] } };
            eye.StrokeThickness = 2.2; eye.StrokeStartLineCap = eye.StrokeEndLineCap = PenLineCap.Round;
            Children.Add(eye);
        }
        // Las mejillas: dos manchas rosas bajo los ojos, invisibles salvo en el saludo (en alto contraste, nunca).
        for (int i = 0; i < 2; i++)
        {
            blush[i] = new Ellipse
            {
                Width = 9.1, Height = 2.6, Opacity = 0, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 3.3), RenderTransform = new TranslateTransform((i == 0 ? -1 : 1) * (EyeGap + 4.55), 0),
                Fill = SystemParameters.HighContrast ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(0xFF, 0x8F, 0xA8)),
            };
            Children.Add(blush[i]);
        }
        Apply(MascotPose.Sleep, animate: false);
        brain.Tick += (_, _) => Think();
        greetTimer.Tick += (_, _) => { greetTimer.Stop(); if (greeting) { Apply(MascotPose.Happy, animate: true); ShowBlush(true); } };
    }

    /// Aparece dormida; la ventana la despierta con `Awake` o la saluda con `Greeting`.
    public void Show()
    {
        brain.Stop();
        greetTimer.Stop();
        awake = greeting = false;
        ShowBlush(false);
        Apply(MascotPose.Sleep, animate: false);
    }

    public void Stop()
    {
        brain.Stop();
        greetTimer.Stop();
    }

    private void Greet()
    {
        brain.Stop();
        awake = true;
        Apply(MascotPose.Open, animate: true);
        if (Reduced) { Apply(MascotPose.Happy, animate: false); ShowBlush(true); return; }
        Blink();
        greetTimer.Start();
    }

    private void ShowBlush(bool on)
    {
        foreach (var spot in blush)
        {
            if (Reduced) { spot.BeginAnimation(OpacityProperty, null); spot.Opacity = on ? 0.82 : 0; }
            else spot.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 0.82 : 0, TimeSpan.FromMilliseconds(on ? 260 : 120)));
        }
    }

    private void Wake()
    {
        Apply(MascotPose.Open, animate: true);
        if (Reduced) return;
        Blink();   // el saludo: abre los ojos y parpadea
        brain.Interval = TimeSpan.FromMilliseconds(900 + rng.Next(900));
        brain.Start();
    }

    private void Doze()
    {
        brain.Stop();
        Apply(MascotPose.Sleep, animate: true);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        double w = info.NewSize.Width, h = info.NewSize.Height;
        if (w <= 0 || h <= 0) return;
        body.Data = Shape(w, h);
    }

    /// El mismo contorno que el notch cerrado (`NotchShape` de la ventana): hombros de 7 y esquinas de 14.
    private static Geometry Shape(double w, double h)
    {
        double e = Math.Min(Ear, h / 4), r = Math.Min(Radius, h - e);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, 0), true, true);
            c.LineTo(new Point(w, 0), true, false);
            c.ArcTo(new Point(w - e, e), new Size(e, e), 0, false, SweepDirection.Counterclockwise, true, false);
            c.LineTo(new Point(w - e, h - r), true, false);
            c.ArcTo(new Point(w - e - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(e + r, h), true, false);
            c.ArcTo(new Point(e, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(e, e), true, false);
            c.ArcTo(new Point(0, 0), new Size(e, e), 0, false, SweepDirection.Counterclockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    /// Cada pocos segundos: de los ojos abiertos pasa a parpadear, mirar a un lado, entornar, sonreír o dormitar; de cualquier otra pose vuelve.
    private void Think()
    {
        double r = rng.NextDouble();
        if (pose != MascotPose.Open)
        {
            Apply(MascotPose.Open, animate: true);
            brain.Interval = TimeSpan.FromMilliseconds(1200 + rng.Next(1800));
            return;
        }
        if (r < 0.40) Blink();
        else
        {
            var next = r < 0.60 ? MascotPose.Left : r < 0.80 ? MascotPose.Right : r < 0.93 ? MascotPose.Smile : MascotPose.Squint;
            Apply(next, animate: true);
        }
        brain.Interval = TimeSpan.FromMilliseconds(900 + rng.Next(1100));
    }

    private void Blink()
    {
        foreach (var scale in blink)
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.08, TimeSpan.FromMilliseconds(70)) { AutoReverse = true });
    }

    private void Apply(MascotPose next, bool animate)
    {
        pose = next;
        var shape = next switch { MascotPose.Squint => SquintEye, MascotPose.Smile => SmileEye, MascotPose.Sleep => SleepEye, MascotPose.Happy => HappyEye, _ => OpenEye };
        bool line = next is MascotPose.Smile or MascotPose.Sleep or MascotPose.Happy;
        double shift = next == MascotPose.Left ? -LookShift : next == MascotPose.Right ? LookShift : 0;
        for (int i = 0; i < 2; i++)
        {
            eyes[i].Data = shape;
            eyes[i].StrokeThickness = line ? 2.4 : 0;
            eyes[i].Stroke = ink;
            eyes[i].Fill = line ? null : ink;   // las sonrisas y los párpados son trazos abiertos: sin relleno
            double x = (i == 0 ? -EyeGap : EyeGap) + shift;
            if (animate && !Reduced) look[i].BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(160)) { EasingFunction = new QuadraticEase() });
            else { look[i].BeginAnimation(TranslateTransform.XProperty, null); look[i].X = x; }
        }
    }

    private static Geometry Frozen(Geometry g) { g.Freeze(); return g; }
}
