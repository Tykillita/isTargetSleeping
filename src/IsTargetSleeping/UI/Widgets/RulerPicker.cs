using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace IsTargetSleeping.UI;

/// Elegir un día arrastrando una regla, como en el recordatorio de Droppy: marcas por día (más largas en lunes y en día 1), la fecha
/// de cada semana encima y un triángulo fijo en el centro. `Offset` son los días desde hoy (0 a 365). También con la rueda y las flechas.
/// shortcut: sin inercia al soltar (la regla se queda donde la dejas); añadir si se echa en falta el deslizamiento.
public sealed class RulerPicker : FrameworkElement
{
    private const double Spacing = 9;
    private const int MaxDays = 365;
    private bool dragging;
    private double startX, startOffset, live;

    public static readonly DependencyProperty OffsetProperty = DependencyProperty.Register(nameof(Offset), typeof(int), typeof(RulerPicker),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, null, Coerce));

    private static object Coerce(DependencyObject d, object value) => Math.Clamp((int)value, 0, MaxDays);

    public int Offset { get => (int)GetValue(OffsetProperty); set => SetValue(OffsetProperty, value); }

    public RulerPicker()
    {
        Height = 40;
        Cursor = Cursors.SizeWE;
        Focusable = true;
        Theme.Changed += InvalidateVisual;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        (dragging, startX, startOffset, live) = (true, e.GetPosition(this).X, Offset, Offset);
        Focus();
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging) return;
        live = Math.Clamp(startOffset - (e.GetPosition(this).X - startX) / Spacing, 0, MaxDays);
        Offset = (int)Math.Round(live);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!dragging) return;
        dragging = false;
        ReleaseMouseCapture();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        Offset += e.Delta < 0 ? 1 : -1;
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Left or Key.Right)) return;
        Offset += e.Key == Key.Right ? 1 : -1;
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var t = Theme.Current;
        var culture = new CultureInfo(L10n.Effective);
        var first = culture.DateTimeFormat.FirstDayOfWeek;
        double w = ActualWidth, h = ActualHeight, center = w / 2, shown = dragging ? live : Offset;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));   // toda la regla recibe el ratón
        var sans = (FontFamily)Application.Current.FindResource("Sans");
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        int from = (int)Math.Floor(shown - center / Spacing) - 1, to = (int)Math.Ceiling(shown + center / Spacing) + 1;
        for (int i = Math.Max(0, from); i <= Math.Min(MaxDays, to); i++)
        {
            var day = DateTime.Today.AddDays(i);
            double x = center + (i - shown) * Spacing;
            double fade = Math.Clamp(1.15 - Math.Abs(x - center) / center, 0.12, 1);   // se apaga hacia los bordes
            bool month = day.Day == 1, week = day.DayOfWeek == first;
            double length = month ? 20 : week ? 15 : 9;
            var color = Theme.WithOpacity(i == Offset ? t.Fg : t.Muted, (i == Offset ? 1 : 0.9) * fade);
            dc.DrawLine(new Pen(Theme.Brush(color), 1.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, new Point(x, 17), new Point(x, 17 + length));
            if (week)   // solo la fecha de cada semana: si no, las de hoy y la del lunes se pisan
            {
                var label = new FormattedText(day.ToString("d MMM", culture), culture, FlowDirection.LeftToRight, new Typeface(sans, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 9.5,
                    Theme.Brush(Theme.WithOpacity(t.Secondary, fade)), dpi);
                dc.DrawText(label, new Point(x - label.Width / 2, 1));
            }
        }
        var arrow = new StreamGeometry();
        using (var c = arrow.Open())
        {
            c.BeginFigure(new Point(center - 4, h - 1), true, true);
            c.LineTo(new Point(center + 4, h - 1), true, false);
            c.LineTo(new Point(center, h - 7), true, false);
        }
        arrow.Freeze();
        dc.DrawGeometry(Theme.Brush(t.Fg), null, arrow);
    }
}
