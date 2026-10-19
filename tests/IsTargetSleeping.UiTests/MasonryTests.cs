using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IsTargetSleeping.UI;

/// El reparto de los widgets del Home en dos columnas: el siguiente va a la más corta, los anchos ocupan las dos y el orden se respeta.
internal static class MasonryTests
{
    public static void Run(Action<string, bool> check)
    {
        FrameworkElement Box(double w, double h) => new Border { Width = w, Height = h };
        var kids = new[] { Box(306, 290), Box(306, 80), Box(306, 210), Box(306, 100), Box(612, 90) };
        var panel = new MasonryPanel();
        foreach (var k in kids) panel.Children.Add(k);
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        panel.Arrange(new Rect(panel.DesiredSize));
        Point At(int i) => new(VisualTreeHelper.GetOffset(kids[i]).X, VisualTreeHelper.GetOffset(kids[i]).Y);
        check("filas de widgets: mide dos columnas de ancho y suma la altura de cada fila", panel.DesiredSize.Width == 612 && panel.DesiredSize.Height == 590);
        check("widgets compactos: los dos de cada fila empiezan alineados", At(0) == new Point(0, 0) && At(1) == new Point(306, 0) && At(2) == new Point(0, 290) && At(3) == new Point(306, 290));
        check("widget ancho: ocupa las dos columnas después de la fila más alta", At(4) == new Point(0, 500));
        var empty = new MasonryPanel();
        empty.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        check("filas de widgets: sin widgets no mide nada", empty.DesiredSize == new Size(0, 0));
    }
}
