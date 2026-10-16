using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace IsTargetSleeping.UI;

/// Deslizar sobre un elemento: con el ratón o el dedo, de derecha a izquierda ejecuta `Next` y de izquierda a derecha `Previous`;
/// con dos dedos en el trackpad (desplazamiento horizontal), hacia la derecha `Next` y hacia la izquierda `Previous`.
/// Un clic o un arrastre corto no cuentan, así que los botones de dentro siguen funcionando.
public static class Swipe
{
    private const double MinDistance = 60, MaxDrift = 40;
    private const int WM_MOUSEHWHEEL = 0x020E;
    /// Una muesca de rueda vale 120: con tres (o la suma de los toques pequeños de un trackpad) se salta de pista.
    private const int WheelThreshold = 360;
    private static readonly TimeSpan WheelReset = TimeSpan.FromMilliseconds(500), WheelCooldown = TimeSpan.FromMilliseconds(700);
    private static readonly List<WeakReference<UIElement>> watched = [];
    private static readonly HashSet<HwndSource> hooked = [];
    private static readonly DependencyProperty WheelProperty = DependencyProperty.RegisterAttached("Wheel", typeof(WheelState), typeof(Swipe));

    private sealed class WheelState { public int Sum; public DateTime Last, Fired; }

    public static readonly DependencyProperty NextProperty =
        DependencyProperty.RegisterAttached("Next", typeof(ICommand), typeof(Swipe), new PropertyMetadata(null, OnChanged));
    public static readonly DependencyProperty PreviousProperty =
        DependencyProperty.RegisterAttached("Previous", typeof(ICommand), typeof(Swipe), new PropertyMetadata(null, OnChanged));
    private static readonly DependencyProperty StartProperty =
        DependencyProperty.RegisterAttached("Start", typeof(Point?), typeof(Swipe));

    public static ICommand? GetNext(DependencyObject d) => (ICommand?)d.GetValue(NextProperty);
    public static void SetNext(DependencyObject d, ICommand? value) => d.SetValue(NextProperty, value);
    public static ICommand? GetPrevious(DependencyObject d) => (ICommand?)d.GetValue(PreviousProperty);
    public static void SetPrevious(DependencyObject d, ICommand? value) => d.SetValue(PreviousProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        element.PreviewMouseLeftButtonDown -= OnDown;
        element.PreviewMouseLeftButtonUp -= OnUp;
        element.PreviewMouseLeftButtonDown += OnDown;
        element.PreviewMouseLeftButtonUp += OnUp;
        if (watched.Any(w => w.TryGetTarget(out var t) && ReferenceEquals(t, element))) return;
        watched.Add(new WeakReference<UIElement>(element));
        if (element is FrameworkElement framework) framework.Loaded += (_, _) => Hook(element);
        Hook(element);
    }

    /// El desplazamiento horizontal del trackpad no llega a WPF como evento: se lee del mensaje de Windows.
    private static void Hook(UIElement element)
    {
        if (PresentationSource.FromVisual(element) is HwndSource source && hooked.Add(source)) source.AddHook(OnMessage);
    }

    private static IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_MOUSEHWHEEL) return IntPtr.Zero;
        int delta = (short)((long)wParam >> 16 & 0xFFFF);
        foreach (var weak in watched.ToList())
        {
            if (!weak.TryGetTarget(out var element)) { watched.Remove(weak); continue; }
            if (!element.IsVisible || !ContainsCursor(element)) continue;
            handled = true;
            var state = (WheelState?)element.GetValue(WheelProperty) ?? new WheelState();
            element.SetValue(WheelProperty, state);
            var now = DateTime.Now;
            if (now - state.Last > WheelReset) state.Sum = 0;
            (state.Last, state.Sum) = (now, state.Sum + delta);
            if (Math.Abs(state.Sum) >= WheelThreshold && now - state.Fired > WheelCooldown)
            {
                var command = state.Sum > 0 ? GetNext(element) : GetPrevious(element);
                (state.Sum, state.Fired) = (0, now);
                if (command?.CanExecute(null) == true) command.Execute(null);
            }
            break;
        }
        return IntPtr.Zero;
    }

    /// IsMouseOver se actualiza con retraso: el cursor real es lo que manda.
    private static bool ContainsCursor(UIElement element)
    {
        if (!Win32.GetCursorPos(out var cursor)) return false;
        var local = element.PointFromScreen(new Point(cursor.X, cursor.Y));
        return local.X >= 0 && local.Y >= 0 && local.X < element.RenderSize.Width && local.Y < element.RenderSize.Height;
    }

    private static void OnDown(object sender, MouseButtonEventArgs e)
    {
        // Arrastrar la barra de progreso es ir a otro punto de la canción, no saltar de pista.
        for (var d = e.OriginalSource as DependencyObject; d is not null; d = d is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is SeekBar) return;
        ((DependencyObject)sender).SetValue(StartProperty, e.GetPosition((IInputElement)sender));
    }

    private static void OnUp(object sender, MouseButtonEventArgs e)
    {
        var element = (DependencyObject)sender;
        if (element.GetValue(StartProperty) is not Point start) return;
        element.ClearValue(StartProperty);
        var end = e.GetPosition((IInputElement)sender);
        var (dx, dy) = (end.X - start.X, end.Y - start.Y);
        if (Math.Abs(dx) < MinDistance || Math.Abs(dy) > MaxDrift) return;
        var command = dx < 0 ? GetNext(element) : GetPrevious(element);
        if (command?.CanExecute(null) == true) command.Execute(null);
    }
}
