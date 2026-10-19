using System.Windows;
using System.Windows.Controls;

namespace IsTargetSleeping.UI;

/// Los widgets del Home en dos columnas por filas. Los widgets compactos de cada fila empiezan alineados y la siguiente fila espera al más alto;
/// los de dos celdas ocupan el ancho completo.
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
        double y = 0;
        for (int i = 0; i < kids.Count;)
        {
            var size = kids[i].DesiredSize;
            if (size.Width >= WideFrom)
            {
                (xs[i], ys[i]) = ((total - size.Width) / 2, y);
                y += size.Height;
                i++;
            }
            else
            {
                xs[i] = 0;
                ys[i] = y;
                double rowHeight = size.Height;
                i++;
                if (i < kids.Count && kids[i].DesiredSize.Width < WideFrom)
                {
                    xs[i] = narrow;
                    ys[i] = y;
                    rowHeight = Math.Max(rowHeight, kids[i].DesiredSize.Height);
                    i++;
                }
                y += rowHeight;
            }
        }
        return (xs, ys, new Size(total, y));
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
