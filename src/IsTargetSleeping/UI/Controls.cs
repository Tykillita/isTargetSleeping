using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace IsTargetSleeping.UI;

/// Interruptor con la marca: cápsula plana, sin el brillo del de sistema.
public sealed class BrandSwitch : ButtonBase
{
    public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(
        nameof(IsOn), typeof(bool), typeof(BrandSwitch), new PropertyMetadata(false, (d, _) => ((BrandSwitch)d).Update(true)));

    public bool IsOn { get => (bool)GetValue(IsOnProperty); set => SetValue(IsOnProperty, value); }

    private readonly Border track = new() { Width = 36, Height = 20, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
    private readonly Ellipse knob = new() { Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 0) };
    private readonly TranslateTransform shift = new();

    public BrandSwitch()
    {
        knob.RenderTransform = shift;
        track.Child = knob;
        Content = track;   // la plantilla (App.xaml) es solo un ContentPresenter
        Cursor = Cursors.Hand;
        Focusable = false;
        VerticalAlignment = VerticalAlignment.Center;
        Loaded += (_, _) => Update(false);
    }

    private void Update(bool animate)
    {
        track.SetResourceReference(Border.BackgroundProperty, IsOn ? "Accent" : "Track");
        track.SetResourceReference(Border.BorderBrushProperty, IsOn ? "Accent" : "Line");
        knob.SetResourceReference(Shape.FillProperty, IsOn ? "OnAccent" : "Muted");
        double to = IsOn ? 16 : 0;
        if (animate && IsLoaded)
            shift.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(to, TimeSpan.FromMilliseconds(150)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } });
        else
        {
            shift.BeginAnimation(TranslateTransform.XProperty, null);
            shift.X = to;
        }
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty) Opacity = IsEnabled ? 1 : 0.5;
    }

    /// Para lectores de pantalla y UI Automation: un interruptor con el patrón Toggle.
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new SwitchPeer(this);

    private sealed class SwitchPeer(BrandSwitch owner)
        : System.Windows.Automation.Peers.FrameworkElementAutomationPeer(owner), System.Windows.Automation.Provider.IToggleProvider
    {
        protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() =>
            System.Windows.Automation.Peers.AutomationControlType.CheckBox;

        protected override string GetClassNameCore() => nameof(BrandSwitch);

        public override object? GetPattern(System.Windows.Automation.Peers.PatternInterface pattern) =>
            pattern == System.Windows.Automation.Peers.PatternInterface.Toggle ? this : base.GetPattern(pattern);

        public System.Windows.Automation.ToggleState ToggleState =>
            owner.IsOn ? System.Windows.Automation.ToggleState.On : System.Windows.Automation.ToggleState.Off;

        public void Toggle()
        {
            if (owner.IsEnabled && owner.Command?.CanExecute(owner.CommandParameter) == true) owner.Command.Execute(owner.CommandParameter);
        }
    }
}

/// Barra de memoria: pista, memoria usada y la parte del modelo, animadas.
public sealed class MemoryBar : FrameworkElement
{
    // Lo que se pinta, que persigue a Used y Model con una animación.
    private static readonly DependencyProperty ShownUsedProperty = DependencyProperty.Register(
        "ShownUsed", typeof(double), typeof(MemoryBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty ShownModelProperty = DependencyProperty.Register(
        "ShownModel", typeof(double), typeof(MemoryBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UsedProperty = DependencyProperty.Register(
        nameof(Used), typeof(double), typeof(MemoryBar), new PropertyMetadata(0.0, (d, e) => ((MemoryBar)d).Animate(ShownUsedProperty, (double)e.NewValue)));

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(double), typeof(MemoryBar), new PropertyMetadata(0.0, (d, e) => ((MemoryBar)d).Animate(ShownModelProperty, (double)e.NewValue)));

    /// Fracciones de 0 a 1 del total.
    public double Used { get => (double)GetValue(UsedProperty); set => SetValue(UsedProperty, value); }
    public double Model { get => (double)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }

    public MemoryBar()
    {
        Height = 8;
        Theme.Changed += InvalidateVisual;
    }

    private void Animate(DependencyProperty target, double to)
    {
        if (!IsLoaded) { SetValue(target, to); return; }
        BeginAnimation(target, new DoubleAnimation(to, TimeSpan.FromMilliseconds(400)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } });
    }

    protected override void OnRender(DrawingContext dc)
    {
        var t = Theme.Current;
        double w = ActualWidth, h = ActualHeight, r = h / 2;
        var full = new Rect(0, 0, w, h);
        dc.PushClip(new RectangleGeometry(full, r, r));
        dc.DrawRoundedRectangle(Theme.Brush(t.Track), null, full, r, r);

        double usedW = w * Math.Clamp((double)GetValue(ShownUsedProperty), 0, 1);
        double modelW = Math.Min(usedW, w * Math.Clamp((double)GetValue(ShownModelProperty), 0, 1));
        if (usedW > 0) dc.DrawRoundedRectangle(Theme.Brush(Theme.WithOpacity(t.Muted, 0.55)), null, new Rect(0, 0, usedW, h), r, r);
        if (modelW > 0)
        {
            var gradient = new LinearGradientBrush(Theme.WithOpacity(t.Accent, 0.75), t.Accent, 0);
            gradient.Freeze();
            dc.DrawRoundedRectangle(gradient, null, new Rect(0, 0, Math.Max(6, modelW), h), r, r);
        }
        dc.Pop();
    }
}

/// Gráfica de los últimos 30 min, con el mismo estilo que MemoryBar: la RAM en
/// uso en gris translúcido y, encima, la parte del modelo en blanco.
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples), typeof(IReadOnlyList<MemorySample>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<MemorySample>? Samples { get => (IReadOnlyList<MemorySample>?)GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }

    public Sparkline()
    {
        Height = 64;
        Theme.Changed += InvalidateVisual;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var t = Theme.Current;
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRoundedRectangle(Theme.Brush(t.Track), null, new Rect(0, 0, w, h), 6, 6);
        // Líneas guía al 50 % y al 100 %.
        var guide = new Pen(Theme.Brush(t.Line), 1);
        guide.Freeze();
        dc.DrawLine(guide, new Point(0, Math.Round(h * 0.5) + 0.5), new Point(w, Math.Round(h * 0.5) + 0.5));

        var samples = Samples;
        if (samples is null || samples.Count < 2) return;
        var end = samples[^1].At;
        var start = end - StatsStore.SampleWindow;
        double X(DateTime at) => Math.Clamp((at - start).TotalSeconds / StatsStore.SampleWindow.TotalSeconds, 0, 1) * w;
        double Y(long value, long total) => h - Math.Clamp(total > 0 ? (double)value / total : 0, 0, 1) * (h - 4);

        Geometry Area(Func<MemorySample, long> pick, out Geometry line)
        {
            var area = new StreamGeometry();
            var stroke = new StreamGeometry();
            using (var a = area.Open())
            using (var l = stroke.Open())
            {
                a.BeginFigure(new Point(X(samples[0].At), h), true, true);
                l.BeginFigure(new Point(X(samples[0].At), Y(pick(samples[0]), samples[0].Total)), false, false);
                foreach (var s in samples)
                {
                    var p = new Point(X(s.At), Y(pick(s), s.Total));
                    a.LineTo(p, true, true);
                    l.LineTo(p, true, true);
                }
                a.LineTo(new Point(X(samples[^1].At), h), true, true);
            }
            area.Freeze();
            stroke.Freeze();
            line = stroke;
            return area;
        }

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h), 6, 6));
        var usedArea = Area(s => s.Used, out var usedLine);
        dc.DrawGeometry(Theme.Brush(Theme.WithOpacity(t.Muted, 0.35)), null, usedArea);
        dc.DrawGeometry(null, new Pen(Theme.Brush(Theme.WithOpacity(t.Muted, 0.9)), 1.2) { LineJoin = PenLineJoin.Round }, usedLine);
        var modelArea = Area(s => s.Model, out var modelLine);
        var fill = new LinearGradientBrush(Theme.WithOpacity(t.Accent, 0.45), Theme.WithOpacity(t.Accent, 0.12), 90);
        fill.Freeze();
        dc.DrawGeometry(fill, null, modelArea);
        dc.DrawGeometry(null, new Pen(Theme.Brush(t.Accent), 1.4) { LineJoin = PenLineJoin.Round }, modelLine);
        dc.Pop();
    }
}

/// Íconos pequeños de los .exe (para «Quién lo despierta»), con caché.
public static class AppIcons
{
    private static readonly Dictionary<string, ImageSource?> cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? For(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        if (cache.TryGetValue(path, out var known)) return known;
        ImageSource? image = null;
        var info = new Win32.SHFILEINFO();
        if (Win32.SHGetFileInfo(path, 0, ref info, System.Runtime.InteropServices.Marshal.SizeOf<Win32.SHFILEINFO>(),
                Win32.SHGFI_ICON | Win32.SHGFI_SMALLICON) != IntPtr.Zero && info.hIcon != IntPtr.Zero)
        {
            try
            {
                image = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
            }
            catch { }
            finally { Win32.DestroyIcon(info.hIcon); }
        }
        return cache[path] = image;
    }
}

/// Indicador de progreso pequeño.
public sealed class Spinner : FrameworkElement
{
    private readonly RotateTransform rotation = new();

    public Spinner()
    {
        Width = Height = 14;
        RenderTransform = rotation;
        RenderTransformOrigin = new Point(0.5, 0.5);
        IsVisibleChanged += (_, _) => Spin();
        Loaded += (_, _) => Spin();
        Theme.Changed += InvalidateVisual;
    }

    private void Spin()
    {
        rotation.BeginAnimation(RotateTransform.AngleProperty, IsVisible
            ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever }
            : null);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double s = Math.Min(ActualWidth, ActualHeight), stroke = Math.Max(1.5, s / 8), r = (s - stroke) / 2;
        var c = new Point(s / 2, s / 2);
        var t = Theme.Current;
        dc.DrawEllipse(null, new Pen(Theme.Brush(Theme.WithOpacity(t.Muted, 0.35)), stroke), c, r, r);
        var arc = new StreamGeometry();
        using (var g = arc.Open())
        {
            g.BeginFigure(new Point(c.X, c.Y - r), false, false);
            g.ArcTo(new Point(c.X + r, c.Y), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
        }
        dc.DrawGeometry(null, new Pen(Theme.Brush(t.Secondary), stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arc);
    }
}
