using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IsTargetSleeping.UI;

/// Recorta lo de dentro a un rectángulo de esquinas redondas (WPF no recorta a las esquinas con ClipToBounds): para fondos que se salen.
public sealed class RoundedClip : Decorator
{
    public static readonly DependencyProperty RadiusProperty =
        DependencyProperty.Register(nameof(Radius), typeof(double), typeof(RoundedClip), new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsArrange));

    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }

    /// No cuenta para el tamaño de lo que lo rodea (es un fondo): ocupa el sitio que le den, aunque lo de dentro sea mayor.
    protected override Size MeasureOverride(Size constraint)
    {
        Child?.Measure(new Size(0, 0));
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var size = base.ArrangeOverride(arrangeSize);
        Clip = new RectangleGeometry(new Rect(size), Radius, Radius);
        return size;
    }
}
