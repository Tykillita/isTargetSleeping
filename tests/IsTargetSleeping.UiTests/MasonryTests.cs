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
        var kids = new[] { Box(306, 290), Box(306, 80), Box(306, 210), Box(612, 100), Box(306, 100) };
        var panel = new MasonryPanel();
        foreach (var k in kids) panel.Children.Add(k);
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        panel.Arrange(new Rect(panel.DesiredSize));
        Point At(int i) => new(VisualTreeHelper.GetOffset(kids[i]).X, VisualTreeHelper.GetOffset(kids[i]).Y);
        check("masonry: mide dos columnas de ancho y la altura de la más larga", panel.DesiredSize.Width == 612 && panel.DesiredSize.Height == 490);
        check("masonry: el primero a la izquierda y el segundo a la derecha, arriba", At(0) == new Point(0, 0) && At(1) == new Point(306, 0));
        check("masonry: el tercero va debajo del segundo (la columna más corta), sin hueco", At(2) == new Point(306, 80));
        check("masonry: uno ancho ocupa las dos columnas, por debajo de ambas", At(3) == new Point(0, 290));
        check("masonry: después de uno ancho se vuelve a empezar por la izquierda", At(4) == new Point(0, 390));
        var empty = new MasonryPanel();
        empty.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        check("masonry: sin widgets no mide nada", empty.DesiredSize == new Size(0, 0));
    }
}
