using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace IsTargetSleeping.UI;

/// El tacómetro del esfuerzo: un dial de 270° con una marca por nivel, un arco que se llena del azul al ámbar hasta la aguja y una
/// aguja con punta y buje. La aguja se mueve con suavidad entre niveles (quieta con «movimiento reducido»). En Auto no hay nivel:
/// la aguja queda arriba, atenuada, y el arco sin llenar, porque ahí decide el router paso a paso.
public sealed class TeamGauge : TeamPulseElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(nameof(Fraction), typeof(double), typeof(TeamGauge),
        new FrameworkPropertyMetadata(-1d, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((TeamGauge)d).Retarget()));
    private static readonly DependencyProperty ShownProperty = DependencyProperty.Register(nameof(Shown), typeof(double), typeof(TeamGauge),
        new FrameworkPropertyMetadata(0.5, FrameworkPropertyMetadataOptions.AffectsRender));

    /// 0 (Bajo) … 1 (Máximo); un valor negativo es Auto.
    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    /// Donde está la aguja ahora mismo (sigue a Fraction, animada).
    private double Shown { get => (double)GetValue(ShownProperty); set => SetValue(ShownProperty, value); }

    /// Marcas del dial: las del nivel Bajo a Máximo.
    private const int Ticks = 4;
    private const double StartDegrees = 135, SweepDegrees = 270;

    static TeamGauge()
    {
        WidthProperty.OverrideMetadata(typeof(TeamGauge), new FrameworkPropertyMetadata(26d));
        HeightProperty.OverrideMetadata(typeof(TeamGauge), new FrameworkPropertyMetadata(26d));
    }

    public TeamGauge() => Loaded += (_, _) => { BeginAnimation(ShownProperty, null); Shown = Target; };

    private double Target => Fraction < 0 ? 0.5 : Math.Clamp(Fraction, 0, 1);

    private void Retarget()
    {
        if (IsLoaded && Motion) BeginAnimation(ShownProperty, new DoubleAnimation(Target, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        else { BeginAnimation(ShownProperty, null); Shown = Target; }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double s = Math.Min(ActualWidth, ActualHeight);
        if (s < 12) return;
        var theme = Theme.Current;
        var center = new Point(s / 2, s / 2 + s * 0.04);
        double radius = s * 0.40, width = Math.Max(1.8, s / 9);
        bool auto = Fraction < 0;
        double shown = Math.Clamp(Shown, 0, 1);

        // Pista: el arco entero, tenue.
        dc.DrawGeometry(null, new Pen(Theme.Brush(Theme.WithOpacity(theme.Muted, 0.45)), width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
            Arc(center, radius, StartDegrees, SweepDegrees));

        // Relleno hasta la aguja: del azul (bajo) al ámbar (alto), con el degradado fijo sobre el dial entero.
        if (!auto && shown > 0.001)
        {
            var gradient = new LinearGradientBrush(theme.On, theme.Warm, new Point(0, 0), new Point(1, 0))
            { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(s * 0.12, 0), EndPoint = new Point(s * 0.88, 0) };
            dc.DrawGeometry(null, new Pen(gradient, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                Arc(center, radius, StartDegrees, Math.Max(6, SweepDegrees * shown)));
        }

        // Una marca por nivel, por dentro del arco; las ya alcanzadas se encienden.
        for (int i = 0; i < Ticks; i++)
        {
            double f = i / (Ticks - 1d), a = (StartDegrees + SweepDegrees * f) * Math.PI / 180;
            bool reached = !auto && f <= shown + 0.001;
            var brush = Theme.Brush(reached ? theme.Fg : Theme.WithOpacity(theme.Muted, 0.7));
            dc.DrawLine(new Pen(brush, Math.Max(1, s / 22)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                new Point(center.X + Math.Cos(a) * radius * 0.62, center.Y + Math.Sin(a) * radius * 0.62),
                new Point(center.X + Math.Cos(a) * radius * 0.78, center.Y + Math.Sin(a) * radius * 0.78));
        }

        // Aguja con punta: más ancha en el buje y afilada hacia fuera. En Auto, atenuada.
        double angle = (StartDegrees + SweepDegrees * shown) * Math.PI / 180;
        var dir = new Vector(Math.Cos(angle), Math.Sin(angle));
        var side = new Vector(-dir.Y, dir.X);
        double length = radius * 0.86, half = s * 0.055;
        var needle = new StreamGeometry();
        using (var ctx = needle.Open())
        {
            ctx.BeginFigure(center + side * half, true, true);
            ctx.LineTo(center + dir * length, true, false);
            ctx.LineTo(center - side * half, true, false);
        }
        needle.Freeze();
        var ink = auto ? Theme.WithOpacity(theme.Fg, 0.55) : theme.Fg;
        dc.DrawGeometry(Theme.Brush(ink), null, needle);

        // Buje: disco de tinta con un punto del fondo.
        dc.DrawEllipse(Theme.Brush(ink), null, center, s * 0.095, s * 0.095);
        dc.DrawEllipse(Theme.Brush(theme.Bg.A < 255 ? Color.FromRgb(20, 20, 24) : theme.Bg), null, center, s * 0.035, s * 0.035);
    }

    private static StreamGeometry Arc(Point center, double radius, double startDegrees, double sweepDegrees)
    {
        Point At(double degrees) => new(center.X + Math.Cos(degrees * Math.PI / 180) * radius, center.Y + Math.Sin(degrees * Math.PI / 180) * radius);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(At(startDegrees), false, false);
            ctx.ArcTo(At(startDegrees + sweepDegrees), new Size(radius, radius), 0, sweepDegrees > 180, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }
}

/// Lo que la caja de la tarea hace con el ratón y el teclado y XAML no puede: soltar imágenes encima, pegar una captura
/// desde el portapapeles e Intro para lanzar (Mayús+Intro escribe un salto de línea). Se pone <c>Target</c> a la tarea del equipo.
public static class TeamAttach
{
    public static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached("Target", typeof(TeamWidget), typeof(TeamAttach),
        new PropertyMetadata(null, OnTargetChanged));

    public static TeamWidget? GetTarget(DependencyObject d) => (TeamWidget?)d.GetValue(TargetProperty);
    public static void SetTarget(DependencyObject d, TeamWidget? value) => d.SetValue(TargetProperty, value);

    /// ¿Está este elemento dentro de una zona donde se sueltan imágenes?
    public static bool Inside(DependencyObject element)
    {
        for (var d = element; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (GetTarget(d) is not null) return true;
        return false;
    }

    private static void OnTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element || e.OldValue is not null || e.NewValue is null) return;
        element.AllowDrop = true;
        element.PreviewDragOver += (_, args) =>
        {
            args.Effects = args.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            args.Handled = true;
        };
        element.PreviewDrop += (sender, args) =>
        {
            if (GetTarget((DependencyObject)sender) is { } widget && args.Data.GetData(DataFormats.FileDrop) is string[] files) widget.AddImages(files);
            args.Handled = true;
        };
        if (element is not TextBox box) return;
        box.PreviewKeyDown += (sender, args) =>
        {
            // Una captura o archivos de imagen copiados se adjuntan; el texto se pega como siempre.
            bool paste = args.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control || args.Key == Key.Insert && Keyboard.Modifiers == ModifierKeys.Shift;
            if (paste && GetTarget((DependencyObject)sender) is { } pasting && pasting.PasteAttachments()) { args.Handled = true; return; }
            if (args.Key != Key.Enter || (Keyboard.Modifiers & ModifierKeys.Shift) != 0) return;
            if (GetTarget((DependencyObject)sender) is not { } widget) return;
            widget.Launch.Execute(null);
            args.Handled = true;
        };
    }
}
