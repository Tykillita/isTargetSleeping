using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace IsTargetSleeping.UI;

/// La lente de cristal del dock, a imitación del selector de «Liquid Glass» de iOS 26. Un solo indicador que viaja entre los botones:
/// en reposo es un disco de cristal tras el botón elegido; al pulsar crece hasta una cápsula más ancha y alta que la barra (sobresale por
/// arriba y por abajo), amplía los iconos que tiene debajo, y sigue al puntero con un muelle, estirándose con la velocidad. Al soltar
/// se asienta en el botón más cercano. La capa no recibe el ratón: quien la usa le pasa el puntero (BeginDrag, DragTo, EndDrag).
public sealed class DockLens : IDisposable
{
    private const double BaseSize = 28, GrowWidth = 32, GrowHeight = 16, StretchWidth = 14, SquashHeight = 3, Zoom = 0.42, Rubber = 14;
    private readonly Canvas layer = new() { IsHitTestVisible = false };
    private readonly Border lens;
    private readonly Canvas magnified = new();
    private readonly Border highlight, fill;
    private readonly DropShadowEffect shadow = new() { Color = Colors.Black, BlurRadius = 12, ShadowDepth = 4, Direction = 270, Opacity = 0.15 };
    private readonly LensPhysics physics = new();
    private readonly Func<bool> reducedMotion;
    private List<Item> items = [];
    private int selected = -1, attempts;
    private bool dragging, placed, subscribed;
    private TimeSpan lastFrame;
    private int frames;
    private double frameSeconds, slowest;

    /// Los fotogramas pintados desde que se pulsó la lente, con su duración media y la del más lento (para comprobar que va fluida).
    public (int Frames, double AverageMs, double SlowestMs) FrameStats => (frames, frames == 0 ? 0 : frameSeconds / frames * 1000, slowest * 1000);

    private sealed record Item(Border Back, TextBlock Glyph, TextBlock Copy, TextBlock Red, TextBlock Blue);

    public DockLens(Func<bool> reducedMotion)
    {
        this.reducedMotion = reducedMotion;
        highlight = new Border
        {
            Height = 5, Margin = new Thickness(10, 2, 10, 0), VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(2.5), IsHitTestVisible = false,
            Background = Gradient((0xA0, 0.0), (0x00, 1.0)),
        };
        // El relleno de cristal se aclara al pulsar: en reposo la lente es un disco tenue y pulsada es casi transparente, como la de iOS.
        fill = new Border { CornerRadius = new CornerRadius(BaseSize / 2), IsHitTestVisible = false, Background = Gradient((0x2C, 0.0), (0x0A, 1.0)) };
        lens = new Border
        {
            Width = BaseSize, Height = BaseSize, CornerRadius = new CornerRadius(BaseSize / 2), ClipToBounds = true, IsHitTestVisible = false, Opacity = 0,
            BorderThickness = new Thickness(1.3), Effect = shadow,
            // El cristal: casi transparente, con luz arriba. El filo es blanco y neutro: el color solo lo ponen los iconos al refractarse.
            BorderBrush = Rim(),
            Child = new Grid { Children = { fill, magnified, highlight } },
        };
        layer.Children.Add(lens);
    }

    /// La capa para poner encima de los botones (mismo tamaño que ellos).
    public Canvas Layer => layer;
    public bool Dragging => dragging;
    public int Selected => selected;
    public double CenterX => physics.X;
    public double LensWidth => lens.Width;
    public double LensHeight => lens.Height;

    private static LinearGradientBrush Gradient(params (byte Alpha, double Offset)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (alpha, offset) in stops) brush.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, 255, 255, 255), offset));
        brush.Freeze();
        return brush;
    }

    private static Brush Fringe(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(0xC0, r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Brush Rim()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xD0, 255, 255, 255), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x38, 255, 255, 255), 0.5));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x5A, 255, 255, 255), 1));
        brush.Freeze();
        return brush;
    }

    /// Los botones sobre los que viaja (de izquierda a derecha): el fondo de cada uno y su icono. Cada icono tiene una copia ampliada
    /// dentro de la lente, y el original se desvanece mientras la lente lo cubre.
    public void SetButtons(IReadOnlyList<(Border Back, TextBlock Glyph)> buttons)
    {
        foreach (var item in items) item.Glyph.Opacity = 1;
        magnified.Children.Clear();
        items = buttons.Select(b =>
        {
            // Tres capas por icono: una franja roja y otra azul desplazadas en sentidos contrarios (aberración cromática) y, encima, el icono.
            TextBlock Layer(Brush? brush)
            {
                var t = new TextBlock { Text = b.Glyph.Text, FontSize = b.Glyph.FontSize, Opacity = 0, IsHitTestVisible = false };
                t.SetResourceReference(TextBlock.FontFamilyProperty, "Icons");
                if (brush is null) t.SetResourceReference(TextBlock.ForegroundProperty, "Fg"); else t.Foreground = brush;
                magnified.Children.Add(t);
                return t;
            }
            var red = Layer(Fringe(0xFF, 0x4A, 0x6E));
            var blue = Layer(Fringe(0x4A, 0x9B, 0xFF));
            var copy = Layer(null);
            return new Item(b.Back, b.Glyph, copy, red, blue);
        }).ToList();
        placed = false;
        attempts = 0;
    }

    /// Pone la lente tras el botón `index` (-1: ninguno, se esconde). Si ya estaba colocada, viaja hasta allí con el muelle.
    public void Select(int index)
    {
        if (index == selected && placed && !dragging) return;
        selected = index;
        if (dragging) return;
        var centers = Centers();
        if (centers is null)
        {
            // Todavía sin medir (el dock acaba de crearse o está oculto): se reintenta cuando haya diseño.
            if (attempts++ < 30) layer.Dispatcher.BeginInvoke(() => Select(selected), System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }
        if (index >= 0)
        {
            physics.TargetX = centers[index];
            physics.TargetGrow = 0;
            if (!placed || reducedMotion()) { physics.X = physics.TargetX; physics.VelocityX = 0; }
            placed = true;
        }
        Kick();
    }

    public void BeginDrag(Point pointer)
    {
        var centers = Centers();
        if (centers is null) return;
        dragging = true;
        (frames, frameSeconds, slowest) = (0, 0, 0);
        if (!placed) { physics.X = centers[Math.Max(selected, 0)]; placed = true; }
        physics.TargetGrow = 1;
        physics.TargetX = LensPhysics.Follow(centers, pointer.X, Rubber);
        Kick();
    }

    public void DragTo(Point pointer)
    {
        if (!dragging || Centers() is not { } centers) return;
        physics.TargetX = LensPhysics.Follow(centers, pointer.X, Rubber);
        Kick();
    }

    /// Suelta la lente: se asienta en el botón más cercano al puntero, que es el que se devuelve (-1 si no hay botones).
    public int EndDrag(Point pointer)
    {
        if (!dragging) return -1;
        dragging = false;
        var centers = Centers();
        if (centers is null) return -1;
        int index = LensPhysics.Nearest(centers, pointer.X);
        selected = index;
        physics.TargetX = centers[index];
        physics.TargetGrow = 0;
        Kick();
        return index;
    }

    /// El ratón se perdió a medias: la lente vuelve al botón que estaba elegido.
    public void CancelDrag()
    {
        if (!dragging) return;
        dragging = false;
        if (Centers() is { } centers && selected >= 0) physics.TargetX = centers[selected];
        physics.TargetGrow = 0;
        Kick();
    }

    private double[]? Centers()
    {
        if (items.Count == 0) return null;
        var centers = new double[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            var back = items[i].Back;
            if (back.ActualWidth <= 0) return null;
            try { centers[i] = back.TranslatePoint(new Point(back.ActualWidth / 2, back.ActualHeight / 2), layer).X; }
            catch (InvalidOperationException) { return null; }
        }
        return centers;
    }

    private void Kick()
    {
        if (reducedMotion()) { Advance(0); return; }
        if (!subscribed) { CompositionTarget.Rendering += OnFrame; subscribed = true; lastFrame = TimeSpan.Zero; }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == lastFrame) return;
        double seconds = lastFrame == TimeSpan.Zero ? 1.0 / 60 : (now - lastFrame).TotalSeconds;
        lastFrame = now;
        frames++;
        frameSeconds += seconds;
        slowest = Math.Max(slowest, seconds);
        Advance(seconds);
        if (!dragging && physics.Settled) Unsubscribe();
    }

    private void Unsubscribe()
    {
        if (!subscribed) return;
        CompositionTarget.Rendering -= OnFrame;
        subscribed = false;
    }

    /// Avanza la animación `seconds` segundos y recoloca la lente (lo llama cada fotograma; las pruebas lo usan para avanzar a mano).
    public void Advance(double seconds)
    {
        if (reducedMotion()) { physics.Snap(); physics.VelocityX = 0; }
        else physics.Step(seconds);
        Layout();
    }

    private void Layout()
    {
        var centers = Centers();
        if (centers is null) return;
        double grow = Math.Max(0, physics.Grow), stretch = physics.Stretch;
        double width = BaseSize + GrowWidth * grow + StretchWidth * stretch;
        double height = BaseSize + GrowHeight * grow - SquashHeight * stretch;
        double hostHeight = layer.ActualHeight > 0 ? layer.ActualHeight : NotchLayout.DockHeight + 2 * NotchLayout.DockPad;
        double left = physics.X - width / 2;
        lens.Width = width;
        lens.Height = height;
        lens.CornerRadius = new CornerRadius(height / 2);
        fill.CornerRadius = lens.CornerRadius;
        fill.Opacity = 1 - 0.6 * Math.Min(grow, 1);
        Canvas.SetLeft(lens, left);
        Canvas.SetTop(lens, (hostHeight - height) / 2);
        lens.Opacity = selected >= 0 || dragging || grow > 0.02 ? 1 : 0;
        shadow.Opacity = 0.12 + 0.43 * Math.Min(grow, 1);
        highlight.Opacity = 0.3 + 0.7 * Math.Min(grow, 1);
        // La ampliación: dentro de la lente cada icono se ve más grande y macizo; el original se desvanece según lo cubra la lente.
        double zoom = 1 + Zoom * Math.Min(grow, 1.2);
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            // Un icono entra en la lente aunque solo lo cubra a medias: la parte de dentro se ve ampliada y el filo la recorta.
            double offset = centers[i] - physics.X;
            double cover = Math.Clamp((width / 2 + 8 - Math.Abs(offset)) / 14, 0, 1) * Math.Clamp(grow * 1.4, 0, 1);
            item.Copy.FontSize = item.Glyph.FontSize * zoom;
            item.Copy.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(item.Copy, centers[i] - left - 1.3 - item.Copy.DesiredSize.Width / 2);
            Canvas.SetTop(item.Copy, height / 2 - 1.3 - item.Copy.DesiredSize.Height / 2);
            item.Copy.Opacity = cover;
            item.Glyph.Opacity = 1 - Math.Min(cover * 1.6, 1);
            // La aberración la provoca cada icono: la luz se abre en rojo y azul según dónde esté dentro de la lente (nada en el centro, más
            // hacia el borde, hacia fuera el rojo y hacia dentro el azul) y se arrastra con la velocidad (el rojo se queda atrás). Sin
            // pulsar o sin moverse apenas hay: no hay un aro de color fijo.
            double side = Math.Clamp(offset / (width / 2), -1.2, 1.2);
            double speed = physics.Stretch * Math.Sign(physics.VelocityX);
            double split = (side * Math.Abs(side) * 1.4 - speed * 2.6) * Math.Min(grow, 1);
            foreach (var (fringe, direction) in new[] { (item.Red, 1.0), (item.Blue, -1.0) })
            {
                fringe.FontSize = item.Copy.FontSize;
                Canvas.SetLeft(fringe, Canvas.GetLeft(item.Copy) + direction * split);
                Canvas.SetTop(fringe, Canvas.GetTop(item.Copy));
                fringe.Opacity = cover * Math.Min(Math.Abs(split) / 0.6, 1) * 0.9;
            }
        }
    }

    public void Dispose()
    {
        Unsubscribe();
        foreach (var item in items) item.Glyph.Opacity = 1;
    }
}
