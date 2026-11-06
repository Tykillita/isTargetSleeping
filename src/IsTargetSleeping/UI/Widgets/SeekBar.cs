using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;

namespace IsTargetSleeping.UI;

/// Barra de progreso en la que se puede hacer clic o arrastrar para ir a otro punto (0 a 1): el comando recibe la fracción al soltar.
/// Más alta que lo que se pinta, para que sea fácil de acertar; con las flechas se mueve un 5 %.
public sealed class SeekBar : FrameworkElement
{
    private const double TrackHeight = 4, KnobSize = 11, Step = 0.05;
    private bool dragging;
    private double preview;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(SeekBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    /// El color de lo ya sonado (el de la portada); sin él, el blanco del tema.
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(SeekBar), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(SeekBar));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(SeekBar), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    /// El color del carril (sin él, el del tema).
    public Brush? TrackBrush { get => (Brush?)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public Brush? Accent { get => (Brush?)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }

    public SeekBar()
    {
        Height = 18;
        Cursor = Cursors.Hand;
        Focusable = true;
        Theme.Changed += InvalidateVisual;
    }

    private double FractionAt(MouseEventArgs e) => ActualWidth <= 0 ? 0 : Math.Clamp(e.GetPosition(this).X / ActualWidth, 0, 1);

    private void Commit(double fraction)
    {
        if (Command?.CanExecute(fraction) == true) Command.Execute(fraction);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        dragging = true;
        preview = FractionAt(e);
        CaptureMouse();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging) { InvalidateVisual(); return; }
        preview = FractionAt(e);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!dragging) return;
        dragging = false;
        ReleaseMouseCapture();
        Commit(FractionAt(e));
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseEnter(MouseEventArgs e) { base.OnMouseEnter(e); InvalidateVisual(); }
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); InvalidateVisual(); }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ValueProperty && UIElementAutomationPeer.FromElement(this) is SeekBarPeer peer)
            peer.RaiseValueChanged((double)e.OldValue, (double)e.NewValue);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Left or Key.Right)) return;
        Commit(Math.Clamp(Value + (e.Key == Key.Right ? Step : -Step), 0, 1));
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var t = Theme.Current;
        double w = ActualWidth, h = ActualHeight, y = (h - TrackHeight) / 2, r = TrackHeight / 2;
        double shown = Math.Clamp(dragging ? preview : Value, 0, 1);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));   // toda la altura recibe el ratón
        dc.DrawRoundedRectangle(TrackBrush ?? Theme.Brush(t.Track), null, new Rect(0, y, w, TrackHeight), r, r);
        double fill = Math.Max(0, w * shown);
        if (fill > 0) dc.DrawRoundedRectangle(Accent ?? Theme.Brush(Theme.WithOpacity(t.Fg, IsMouseOver || dragging ? 0.95 : 0.75)), null, new Rect(0, y, fill, TrackHeight), r, r);
        if (IsMouseOver || dragging || IsKeyboardFocused)
            dc.DrawEllipse(Accent ?? Theme.Brush(t.Fg), null, new Point(Math.Clamp(fill, KnobSize / 2, Math.Max(KnobSize / 2, w - KnobSize / 2)), h / 2), KnobSize / 2, KnobSize / 2);
        if (IsKeyboardFocused && w > 3 && h > 3)
            dc.DrawRoundedRectangle(null, new Pen(Theme.Brush(t.Accent), 1.25), new Rect(1, 1, w - 2, h - 2), 3, 3);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SeekBarPeer(this);

    private sealed class SeekBarPeer(SeekBar owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        protected override string GetClassNameCore() => nameof(SeekBar);
        public override object? GetPattern(PatternInterface pattern) => pattern == PatternInterface.RangeValue ? this : base.GetPattern(pattern);
        public double Value => owner.Value;
        public double Minimum => 0;
        public double Maximum => 1;
        public double SmallChange => Step;
        public double LargeChange => Step;
        public bool IsReadOnly => owner.Command is null;
        public void RaiseValueChanged(double oldValue, double newValue) => RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, oldValue, newValue);
        public void SetValue(double value)
        {
            if (!double.IsFinite(value) || value is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(value));
            if (IsReadOnly) throw new InvalidOperationException("The seek bar has no command to change its value.");
            if (!owner.IsEnabled) throw new ElementNotEnabledException();
            owner.Commit(value);
        }
    }
}
