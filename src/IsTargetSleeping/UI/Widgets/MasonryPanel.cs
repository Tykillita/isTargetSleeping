using System.Windows;
using System.Windows.Controls;

namespace IsTargetSleeping.UI;

/// Los widgets del Home en dos columnas que se rellenan por la más corta (como un muro de ladrillos), en vez de filas con la altura del más alto:
/// un widget bajo no deja un hueco al lado de uno alto, sino que el siguiente se pone debajo. Los de dos celdas (anchos) ocupan las dos columnas.
/// El orden se respeta de arriba abajo y, en cada altura, de izquierda a derecha.
public sealed class MasonryPanel : Panel
{
    private const double WideFrom = 450;

    private (double[] X, double[] Y, Size Total) Place()
    {
        var kids = InternalChildren.Cast<UIElement>().ToList();
        double narrow = kids.Where(k => k.DesiredSize.Width < WideFrom).Select(k => k.DesiredSize.Width).DefaultIfEmpty(0).Max();
        double total = Math.Max(narrow * 2, kids.Select(k => k.DesiredSize.Width).DefaultIfEmpty(0).Max());
        if (narrow == 0) narrow = total / 2;
        var xs = new double[kids.Count];
        var ys = new double[kids.Count];
        double left = 0, right = 0;
        for (int i = 0; i < kids.Count; i++)
        {
            var size = kids[i].DesiredSize;
            if (size.Width >= WideFrom)
            {
                double y = Math.Max(left, right);
                (xs[i], ys[i], left, right) = ((total - size.Width) / 2, y, y + size.Height, y + size.Height);
            }
            else if (left <= right) { (xs[i], ys[i], left) = (0, left, left + size.Height); }
            else { (xs[i], ys[i], right) = (narrow, right, right + size.Height); }
        }
        return (xs, ys, new Size(total, Math.Max(left, right)));
    }

    protected override Size MeasureOverride(Size available)
    {
        foreach (UIElement child in InternalChildren) child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Place().Total;
    }

    protected override Size ArrangeOverride(Size final)
    {
        var (xs, ys, _) = Place();
        for (int i = 0; i < InternalChildren.Count; i++)
            InternalChildren[i].Arrange(new Rect(xs[i], ys[i], InternalChildren[i].DesiredSize.Width, InternalChildren[i].DesiredSize.Height));
        return final;
    }
}
