using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace IsTargetSleeping.UI;

/// Captura cada tarjeta antes de quitarla y la anima hasta el centro del botón rojo.
public sealed class ShelfRemovalAnimation : Canvas
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(ShelfWidget), typeof(ShelfRemovalAnimation), new PropertyMetadata(null, OnSourceChanged));
    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(
        nameof(Target), typeof(FrameworkElement), typeof(ShelfRemovalAnimation));

    private ShelfWidget? subscribed;
    private Action? finish;
    private readonly List<(FrameworkElement Element, double Opacity)> hidden = [];

    public ShelfWidget? Source { get => (ShelfWidget?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public FrameworkElement? Target { get => (FrameworkElement?)GetValue(TargetProperty); set => SetValue(TargetProperty, value); }

    public ShelfRemovalAnimation()
    {
        IsHitTestVisible = false;
        ClipToBounds = false;
        Loaded += (_, _) => Observe(Source);
        Unloaded += (_, _) => { Observe(null); FinishNow(); };
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var layer = (ShelfRemovalAnimation)d;
        if (layer.IsLoaded) { layer.FinishNow(); layer.Observe((ShelfWidget?)e.NewValue); }
    }

    private void Observe(ShelfWidget? source)
    {
        if (ReferenceEquals(subscribed, source)) return;
        if (subscribed is not null) subscribed.RemovalAnimationRequested -= OnRemovalAnimationRequested;
        subscribed = source;
        if (IsLoaded && subscribed is not null) subscribed.RemovalAnimationRequested += OnRemovalAnimationRequested;
    }

    private bool OnRemovalAnimationRequested(IReadOnlyList<string> paths, Action complete)
    {
        var targetElement = Target;
        if (!IsLoaded || !IsVisible || Source is not { ReducedMotion: false } || !SystemParameters.ClientAreaAnimation ||
            paths.Count == 0 || targetElement is not { IsVisible: true, ActualWidth: > 0, ActualHeight: > 0 }) return false;

        var host = VisualTreeHelper.GetParent(this);
        if (host is null) return false;

        try
        {
            var targetCenter = targetElement.TransformToVisual(this).Transform(new Point(targetElement.ActualWidth / 2, targetElement.ActualHeight / 2));
            var flights = paths.Select(path => Capture(host, path)).Where(flight => flight is not null).Cast<Flight>().ToArray();
            if (flights.Length == 0) return false;

            // Mantiene la papelera y el espacio de las tarjetas hasta que llegue la última captura.
            hidden.AddRange(flights.Select(flight => (flight.Element, flight.Element.Opacity)));
            foreach (var flight in flights) flight.Element.Opacity = 0;
            finish = complete;
            var remaining = flights.Length;
            for (int i = 0; i < flights.Length; i++)
            {
                var flight = flights[i];
                try { Fly(flight, targetCenter, i, () => { if (--remaining == 0) FinishNow(); }); }
                catch (Exception e) { AppLog.Write($"estante: animación de retirada: {e.Message}"); if (--remaining == 0) FinishNow(); }
            }
            return true;
        }
        catch (Exception e)
        {
            AppLog.Write($"estante: animación de retirada: {e.Message}");
            FinishNow();
            return false;
        }
    }

    private Flight? Capture(DependencyObject host, string path)
    {
        var tile = Source?.Tiles.FirstOrDefault(item => StringComparer.OrdinalIgnoreCase.Equals(item.Path, path));
        if (tile is null) return null;
        foreach (var items in VisualChildren<ItemsControl>(host))
        {
            if (items.ItemContainerGenerator.ContainerFromItem(tile) is not ContentPresenter presenter || !presenter.IsVisible) continue;
            if (presenter.ContentTemplate?.FindName("tileButton", presenter) is not FrameworkElement element || !element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0) continue;
            var origin = element.TransformToVisual(this).Transform(new Point());
            var dpi = VisualTreeHelper.GetDpi(element);
            var bitmap = new RenderTargetBitmap(
                Math.Max(1, (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX)),
                Math.Max(1, (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY)),
                96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            bitmap.Render(element);
            bitmap.Freeze();
            return new Flight(element, bitmap, origin, element.ActualWidth, element.ActualHeight);
        }
        return null;
    }

    private void Fly(Flight flight, Point target, int index, Action completed)
    {
        var image = new Image
        {
            Source = flight.Bitmap,
            Width = flight.Width,
            Height = flight.Height,
            Stretch = Stretch.Fill,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(),
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        Children.Add(image);
        var start = flight.Origin;
        Canvas.SetLeft(image, start.X);
        Canvas.SetTop(image, start.Y);

        // Primero la tarjeta se «despega» un poco (se agranda y se queda un instante), luego vuela en arco suave hacia la papelera, se encoge y se apaga al llegar.
        var duration = TimeSpan.FromMilliseconds(820);
        var delay = TimeSpan.FromMilliseconds(index * 85);
        KeySpline Spline(double x1, double y1, double x2, double y2) => new(x1, y1, x2, y2);
        TimeSpan At(double part) => TimeSpan.FromMilliseconds(820 * part);
        var targetLeft = target.X - flight.Width / 2;
        var targetTop = target.Y - flight.Height / 2;
        // X e Y con curvas distintas: la tarjeta sube un poco antes de caer hacia la papelera y el recorrido sale curvo, no una recta.
        var flyX = new DoubleAnimationUsingKeyFrames { BeginTime = delay };
        flyX.KeyFrames.Add(new SplineDoubleKeyFrame(start.X, KeyTime.FromTimeSpan(At(0.16)), Spline(0.3, 0, 0.3, 1)));
        flyX.KeyFrames.Add(new SplineDoubleKeyFrame(targetLeft, KeyTime.FromTimeSpan(duration), Spline(0.5, 0, 0.15, 1)));
        var flyY = new DoubleAnimationUsingKeyFrames { BeginTime = delay };
        flyY.KeyFrames.Add(new SplineDoubleKeyFrame(start.Y - 7, KeyTime.FromTimeSpan(At(0.22)), Spline(0.2, 0.6, 0.3, 1)));
        flyY.KeyFrames.Add(new SplineDoubleKeyFrame(targetTop, KeyTime.FromTimeSpan(duration), Spline(0.55, 0, 0.2, 1)));
        var scale = (ScaleTransform)image.RenderTransform;
        DoubleAnimationUsingKeyFrames Shrink()
        {
            var k = new DoubleAnimationUsingKeyFrames { BeginTime = delay };
            k.KeyFrames.Add(new SplineDoubleKeyFrame(1.07, KeyTime.FromTimeSpan(At(0.2)), Spline(0.2, 0.6, 0.3, 1)));
            k.KeyFrames.Add(new SplineDoubleKeyFrame(0.08, KeyTime.FromTimeSpan(duration), Spline(0.5, 0, 0.2, 1)));
            return k;
        }
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(260))
        {
            BeginTime = delay + At(0.68),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
        };
        flyX.Completed += (_, _) => { Children.Remove(image); completed(); };
        image.BeginAnimation(Canvas.LeftProperty, flyX);
        image.BeginAnimation(Canvas.TopProperty, flyY);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Shrink());
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Shrink());
        image.BeginAnimation(OpacityProperty, fade);
    }

    private void FinishNow()
    {
        foreach (var (element, opacity) in hidden) element.Opacity = opacity;
        hidden.Clear();
        Children.Clear();
        var done = finish;
        finish = null;
        done?.Invoke();
    }

    private static IEnumerable<T> VisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is not Visual) yield break;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in VisualChildren<T>(child)) yield return nested;
        }
    }

    private sealed record Flight(FrameworkElement Element, BitmapSource Bitmap, Point Origin, double Width, double Height);
}
