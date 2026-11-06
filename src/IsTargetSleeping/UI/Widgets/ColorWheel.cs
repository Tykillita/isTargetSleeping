using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;

namespace IsTargetSleeping.UI;

/// La rueda de colores de las luces: un anillo con todos los tonos y un círculo en el elegido. Se arrastra con el ratón; el tono (0–360) llega al comando.
public sealed class ColorWheel : FrameworkElement
{
    public static readonly DependencyProperty HueProperty = DependencyProperty.Register(nameof(Hue), typeof(double), typeof(ColorWheel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Hue { get => (double)GetValue(HueProperty); set => SetValue(HueProperty, value); }
    /// El pincel del disco del centro (el color que tienen las luces ahora).
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(ColorWheel),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    /// La opacidad del disco del centro: el brillo (con las luces apagadas, casi nada).
    public static readonly DependencyProperty FillOpacityProperty = DependencyProperty.Register(nameof(FillOpacity), typeof(double), typeof(ColorWheel),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public double FillOpacity { get => (double)GetValue(FillOpacityProperty); set => SetValue(FillOpacityProperty, value); }
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(ColorWheel));
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }

    private static readonly Brush[] Wedges = Enumerable.Range(0, 120).Select(i =>
    {
        var (r, g, b) = LedProtocol.FromHsv(i * 3 + 1.5, 1, 1);
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b)); brush.Freeze();
        return (Brush)brush;
    }).ToArray();
    private bool dragging;
    private double preview = double.NaN;

    public ColorWheel()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
        Loaded += (_, _) => Theme.Changed += InvalidateVisual;
        Unloaded += (_, _) => Theme.Changed -= InvalidateVisual;
    }

    private double Side => Math.Min(ActualWidth, ActualHeight);

    protected override void OnRender(DrawingContext dc)
    {
        double side = Side;
        if (side < 20) return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        double outer = side / 2 - 2, inner = outer * 0.62, mid = (outer + inner) / 2;
        for (int i = 0; i < Wedges.Length; i++)
        {
            double a0 = (i * 3 - 90.4) * Math.PI / 180, a1 = ((i + 1) * 3 - 89.6) * Math.PI / 180;
            var figure = new PathFigure { StartPoint = At(c, outer, a0), IsClosed = true };
            figure.Segments.Add(new ArcSegment(At(c, outer, a1), new Size(outer, outer), 0, false, SweepDirection.Clockwise, true));
            figure.Segments.Add(new LineSegment(At(c, inner, a1), true));
            figure.Segments.Add(new ArcSegment(At(c, inner, a0), new Size(inner, inner), 0, false, SweepDirection.Counterclockwise, true));
            var geometry = new PathGeometry([figure]);
            geometry.Freeze();
            dc.DrawGeometry(Wedges[i], null, geometry);
        }
        if (Fill is { } fill)
        {
            // El disco del centro: el color de las luces, con un halo suave y su brillo como opacidad.
            double core = inner - 7;
            var halo = new RadialGradientBrush(Color.FromArgb(0x55, 255, 255, 255), Colors.Transparent) { Opacity = 0.5 };
            dc.DrawEllipse(Theme.Brush(Theme.WithOpacity(Theme.Current.Fg, 0.08)), null, c, inner - 3, inner - 3);
            dc.PushOpacity(Math.Clamp(FillOpacity, 0.12, 1));
            dc.DrawEllipse(fill, null, c, core, core);
            dc.DrawEllipse(halo, null, c, core, core);
            dc.Pop();
        }
        double hue = double.IsNaN(preview) ? Hue : preview;
        var knob = At(c, mid, (hue - 90) * Math.PI / 180);
        double r = (outer - inner) / 2 - 1.5;
        var (kr, kg, kb) = LedProtocol.FromHsv(hue, 1, 1);
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(kr, kg, kb)), new Pen(Brushes.White, 2.4), knob, r, r);
        if (IsKeyboardFocused)
            dc.DrawEllipse(null, new Pen(Theme.Brush(Theme.Current.Accent), 1.5), c, outer + 0.75, outer + 0.75);
    }

    private static Point At(Point c, double radius, double radians) => new(c.X + radius * Math.Cos(radians), c.Y + radius * Math.Sin(radians));

    private double HueAt(Point p)
    {
        var dx = p.X - ActualWidth / 2; var dy = p.Y - ActualHeight / 2;
        return (Math.Atan2(dy, dx) * 180 / Math.PI + 90 + 360) % 360;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus(); dragging = true; CaptureMouse();
        Update(e.GetPosition(this));
        e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e) { if (dragging) Update(e.GetPosition(this)); }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!dragging) return;
        dragging = false; ReleaseMouseCapture();
        preview = double.NaN;
        InvalidateVisual();
    }
    private void Update(Point p)
    {
        preview = HueAt(p);
        InvalidateVisual();
        Command?.Execute(preview);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        double step = e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift) ? 15 : 5;
        if (e.Key is Key.Left or Key.Down) { Command?.Execute((Hue - step + 360) % 360); e.Handled = true; }
        else if (e.Key is Key.Right or Key.Up) { Command?.Execute((Hue + step) % 360); e.Handled = true; }
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == HueProperty && UIElementAutomationPeer.FromElement(this) is ColorWheelPeer peer)
            peer.RaiseValueChanged((double)e.OldValue, (double)e.NewValue);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ColorWheelPeer(this);

    private sealed class ColorWheelPeer(ColorWheel owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        protected override string GetClassNameCore() => nameof(ColorWheel);
        public override object? GetPattern(PatternInterface pattern) => pattern == PatternInterface.RangeValue ? this : base.GetPattern(pattern);
        public double Value => Math.Clamp(owner.Hue, 0, 359);
        public double Minimum => 0;
        public double Maximum => 359;
        public double SmallChange => 5;
        public double LargeChange => 15;
        public bool IsReadOnly => owner.Command is null;
        public void RaiseValueChanged(double oldValue, double newValue) => RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, oldValue, newValue);
        public void SetValue(double value)
        {
            if (!double.IsFinite(value) || value is < 0 or > 359) throw new ArgumentOutOfRangeException(nameof(value));
            if (IsReadOnly) throw new InvalidOperationException("The color wheel has no command to change its value.");
            if (!owner.IsEnabled) throw new ElementNotEnabledException();
            owner.Command!.Execute(value);
        }
    }
}
