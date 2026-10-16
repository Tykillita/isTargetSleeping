using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace IsTargetSleeping.UI;

/// Un texto de una línea con un ancho máximo fijo: si cabe, se queda quieto; si no, se desplaza en bucle dentro de ese ancho
/// (con una pausa al principio) para que quien lo contiene no se ensanche. Con «movimiento reducido» se recorta con «…».
public sealed class Marquee : Border
{
    public const double Gap = 24, PixelsPerSecond = 32, StartPause = 2;
    private readonly Func<TextBlock> make;
    private readonly double max;
    private readonly bool reduced;
    private readonly StackPanel strip = new() { Orientation = Orientation.Horizontal };
    // La fila va en un Canvas: un StackPanel más ancho que su caja recibiría un recorte de diseño en sus propias coordenadas,
    // y ese recorte se movería con la animación (el final del texto nunca llegaría a verse). El Canvas coloca la fila a su tamaño,
    // sin recorte, y solo recorta la caja fija (ClipToBounds).
    private readonly Canvas canvas = new();
    private readonly TranslateTransform shift = new();
    private string text = "";

    /// `make` crea cada copia del texto con el estilo que toque (tamaño, fuente y color); `maxWidth` es el ancho máximo.
    public Marquee(Func<TextBlock> make, double maxWidth, bool reducedMotion)
    {
        (this.make, max, reduced) = (make, maxWidth, reducedMotion);
        ClipToBounds = true;
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Left;
        strip.RenderTransform = shift;
        canvas.Children.Add(strip);
        Child = canvas;
    }

    /// true si el texto no cabe y se está desplazando.
    public bool Scrolling { get; private set; }

    /// La fila con las copias del texto, la que se desplaza (para las pruebas).
    public FrameworkElement Strip => strip;

    public string Text
    {
        get => text;
        set
        {
            if (value == text) return;   // cada segundo se repite el mismo texto: no se reinicia el desplazamiento
            text = value;
            Rebuild();
            InvalidateMeasure();   // el tamaño depende del texto; el Canvas de dentro no siempre avisa a su Border
        }
    }

    private void Rebuild()
    {
        shift.BeginAnimation(TranslateTransform.XProperty, null);
        shift.X = 0;
        strip.Children.Clear();
        Scrolling = false;
        var first = make();
        first.Text = text;
        first.TextWrapping = TextWrapping.NoWrap;
        first.TextTrimming = TextTrimming.None;
        first.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = Math.Ceiling(first.DesiredSize.Width);
        strip.Children.Add(first);
        canvas.Width = Math.Min(width, max);   // lo que se ve: el texto entero si cabe, o el ancho fijo
        canvas.Height = Math.Ceiling(first.DesiredSize.Height);
        if (width <= max) return;
        if (reduced)
        {
            first.TextTrimming = TextTrimming.CharacterEllipsis;
            first.Width = max;
            return;
        }
        // Dos copias seguidas: al llegar la primera al borde izquierdo, la segunda ocupa su sitio y el bucle no da saltos.
        var second = make();
        second.Text = text;
        second.TextWrapping = TextWrapping.NoWrap;
        strip.Children.Add(new Border { Width = Gap });
        strip.Children.Add(second);
        double distance = width + Gap;
        var scroll = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        scroll.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        scroll.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(StartPause))));
        scroll.KeyFrames.Add(new LinearDoubleKeyFrame(-distance, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(StartPause + distance / PixelsPerSecond))));
        shift.BeginAnimation(TranslateTransform.XProperty, scroll);
        Scrolling = true;
    }
}

/// El indicador de reproducción de la pastilla: tres barras que suben y bajan (quietas con «movimiento reducido»).
public static class PillEqualizer
{
    public static FrameworkElement Build(bool reducedMotion)
    {
        var grid = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Height = 12 };
        var timings = new (double From, double To, double Seconds)[] { (0.25, 1, 0.45), (1, 0.3, 0.6), (0.4, 0.9, 0.35) };
        foreach (var (from, to, seconds) in timings)
        {
            var scale = new ScaleTransform(1, reducedMotion ? 0.7 : from);
            var bar = new Rectangle { Width = 2.5, Height = 12, RadiusX = 1, RadiusY = 1, Margin = new Thickness(0, 0, 2, 0), VerticalAlignment = VerticalAlignment.Bottom, RenderTransformOrigin = new Point(0.5, 1), RenderTransform = scale };
            bar.SetResourceReference(Shape.FillProperty, "Secondary");
            if (!reducedMotion)
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            grid.Children.Add(bar);
        }
        grid.Margin = new Thickness(0, 0, 4, 0);
        return grid;
    }
}
