using System.Windows;
using System.Windows.Input;

namespace IsTargetSleeping.UI;

/// La rueda del ratón desplaza la fila de mosaicos del Estante de lado a lado (sin barra visible, como en Nusa).
public static class ShelfScroll
{
    public static readonly DependencyProperty WheelProperty =
        DependencyProperty.RegisterAttached("Wheel", typeof(bool), typeof(ShelfScroll), new PropertyMetadata(false, OnChanged));

    public static bool GetWheel(DependencyObject d) => (bool)d.GetValue(WheelProperty);
    public static void SetWheel(DependencyObject d, bool value) => d.SetValue(WheelProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not System.Windows.Controls.ScrollViewer viewer) return;
        viewer.PreviewMouseWheel -= OnWheel;
        if (e.NewValue is true) viewer.PreviewMouseWheel += OnWheel;
    }

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var viewer = (System.Windows.Controls.ScrollViewer)sender;
        if (viewer.ScrollableWidth <= 0) return;
        viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset - e.Delta);
        e.Handled = true;
    }
}
