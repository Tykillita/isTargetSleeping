using System.Collections.Specialized;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IsTargetSleeping;
using IsTargetSleeping.UI;
using IsTargetSleeping.UI.Widgets;
using Ellipse = System.Windows.Shapes.Ellipse;

internal static class ShelfDropTests
{
    public static void Run(Action<string, bool> check)
    {
        string dataDir = Path.Combine(Path.GetTempPath(), "its-shelf-drop-" + Guid.NewGuid().ToString("N"));
        string? previous = Environment.GetEnvironmentVariable("ITS_DATA_DIR");
        Directory.CreateDirectory(dataDir);
        Environment.SetEnvironmentVariable("ITS_DATA_DIR", dataDir);
        try
        {
            var template = (DataTemplate)Application.Current.FindResource("WidgetShelfWidget");
            var widget = (FrameworkElement)template.LoadContent();
            var shelf = new ShelfWidget(persist: false);
            widget.DataContext = new WidgetSlot(shelf, 1, 1, peek: true, alone: true);
            widget.Measure(new Size(416, 140));
            widget.Arrange(new Rect(0, 0, 416, 140));
            var dropGrid = Descendants<Grid>(widget).Single(grid => grid.MinHeight == 52);
            check("el área visible del estante acepta hit-test de arrastre", dropGrid.IsHitTestVisible && dropGrid.AllowDrop);

            Point? dragAt = null, droppedAt = null;
            int dragEnded = 0;
            shelf.DragMoved += point => dragAt = point;
            shelf.DragEnded += () => dragEnded++;
            shelf.Dropped += point => droppedAt = point;
            var cursor = new Point(132.5, 74.25);
            shelf.UpdateDragPosition(cursor);
            check("el arrastre conserva la posición real del cursor", shelf.Dragging && dragAt == cursor);
            shelf.SetDragging(false);
            check("la luz se apaga al terminar el arrastre", !shelf.Dragging && dragEnded == 1);

            string first = Path.Combine(dataDir, "from-explorer.txt"), second = Path.Combine(dataDir, "from-string-collection.txt");
            File.WriteAllText(first, "first");
            File.WriteAllText(second, "second");
            var release = new Point(287.75, 63.5);
            shelf.AddFiles([first], release);
            check("la onda recibe el punto exacto donde se soltó el archivo", droppedAt == release);
            shelf.AddFiles([first, second]);
            var shelfWindow = new Window { Content = widget, Width = 416, Height = 140, Left = -10000, Top = -10000, Opacity = 0, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
            try
            {
                shelfWindow.Show();
                shelfWindow.UpdateLayout();
                var ripple = Descendants<ShelfRipple>(widget).Single();
                var screenCursor = ripple.PointToScreen(new Point(116, 62));
                shelf.UpdateDragPosition(screenCursor);
                var aura = ripple.Children.OfType<Ellipse>().Single();
                var localCursor = ripple.PointFromScreen(screenCursor);
                check("el aura perlada sigue el punto real del cursor sobre el estante",
                    Math.Abs(Canvas.GetLeft(aura) + aura.Width / 2 - localCursor.X) < 0.1
                    && Math.Abs(Canvas.GetTop(aura) + aura.Height / 2 - localCursor.Y) < 0.1);
                shelf.AddFiles([first], screenCursor);
                var drop = ripple.PointFromScreen(screenCursor);
                double reachX = Math.Max(Math.Abs(drop.X), Math.Abs(ripple.ActualWidth - drop.X));
                double reachY = Math.Max(Math.Abs(drop.Y), Math.Abs(ripple.ActualHeight - drop.Y));
                var rings = ripple.Children.OfType<Ellipse>().Where(item => item.Stroke is not null).OrderBy(item => item.Width).ToArray();
                bool wideWaterDrop = SystemParameters.ClientAreaAnimation
                    ? rings.Length == 3 && rings[^1].Width >= 2 * Math.Sqrt(reachX * reachX + reachY * reachY)
                        && rings[1].Width - rings[0].Width >= rings[^1].Width * 0.30
                        && rings[2].Width - rings[1].Width >= rings[^1].Width * 0.30
                    : rings.Length == 0;
                check("la onda se separa en tres anillos que recorren todo el estante", wideWaterDrop);
                shelf.SetDragging(false);
                Pump(TimeSpan.FromMilliseconds(260));
                var fly = Descendants<ShelfRemovalAnimation>(widget).Single();
                shelf.Tiles[0].Remove.Execute(null);
                check("quitar una tarjeta la mantiene hasta que termina su vuelo", shelf.IsRemoving && shelf.Tiles.Count == 2 && fly.Children.Count == 1);
                Pump(TimeSpan.FromMilliseconds(950));
                check("quitar una tarjeta la lleva al centro de Clear all y luego la elimina", !shelf.IsRemoving && shelf.Tiles.Count == 1 && fly.Children.Count == 0);
                shelf.AddFiles([first]);
                shelfWindow.UpdateLayout();
                shelf.ClearOrRemove.Execute(null);
                check("Clear all hace llegar cada tarjeta al botón antes de vaciar el estante", shelf.IsRemoving && shelf.Tiles.Count == 2 && fly.Children.Count == 2);
                Pump(TimeSpan.FromMilliseconds(1050));
                check("Clear all termina el vuelo antes de vaciar la lista", !shelf.IsRemoving && shelf.IsEmpty && fly.Children.Count == 0);
            }
            finally { shelfWindow.Close(); }

            var prefs = new Prefs();
            prefs.SetList(PrefKeys.NotchPeekWidgets, ["shelf", "pomodoro"]);
            var model = new PanelViewModel(new Supervisor(new OllamaController(), prefs));
            var notch = new NotchWindow(model, new Border());
            try
            {
                model.Hub.BatteryInfo.LoadDemo();
                model.SetPeekVisible(true);
                model.RaiseNotice("aviso de prueba");
                model.Hub.Pomodoro.Toggle.Execute(null);
                model.Hub.Shelf.AddFiles([first]);
                var peek = new NotchPeek(model);
                peek.Width = 420;
                peek.SetContentMinHeight(140);
                try
                {
                    typeof(NotchPeek).GetMethod("UpdateHeader", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(peek, null);
                    var bell = (FrameworkElement)peek.FindName("headerBell")!;
                    var pomo = (FrameworkElement)peek.FindName("headerPomo")!;
                    var battery = (FrameworkElement)peek.FindName("headerRing")!;
                    var date = (FrameworkElement)peek.FindName("headerDate")!;
                    check("la cabecera del estante oculta avisos y Pomodoro, pero conserva batería y fecha",
                        bell.Visibility == Visibility.Collapsed && pomo.Visibility == Visibility.Collapsed
                        && battery.Visibility == Visibility.Visible && date.Visibility == Visibility.Visible);

                    if (Environment.GetEnvironmentVariable("ITS_CAPTURE_UI") is { Length: > 0 } captureDir)
                    {
                        var originalLanguage = L10n.Effective;
                        bool originalContrast = Theme.ForceHighContrast;
                        var visualMode = (NotchState)typeof(NotchWindow).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notch)!;
                        visualMode.Expand();
                        typeof(NotchWindow).GetField("glassBody", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(notch, true);
                        var backdrop = (NotchBackdrop)typeof(NotchWindow).GetField("backdrop", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notch)!;
                        typeof(NotchBackdrop).GetProperty(nameof(NotchBackdrop.Luminance))!.SetValue(backdrop, 0.4);
                        var paintBody = typeof(NotchWindow).GetMethod("PaintBody", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        var body = (System.Windows.Shapes.Path)typeof(NotchWindow).GetField("body", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notch)!;
                        // Fondo conocido bajo el relleno real del notch, para ver si el azul se interrumpe en la cabecera.
                        var wallpaper = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                        wallpaper.GradientStops.Add(new GradientStop(Color.FromRgb(0x12, 0x50, 0x5C), 0));
                        wallpaper.GradientStops.Add(new GradientStop(Color.FromRgb(0x1A, 0x2E, 0x5E), 0.55));
                        wallpaper.GradientStops.Add(new GradientStop(Color.FromRgb(0x3D, 0x2C, 0x8C), 1));
                        var frame = new Grid { Width = 420, Height = 140 };
                        var shellFill = new Border { CornerRadius = new CornerRadius(24), Background = wallpaper };
                        frame.Children.Add(shellFill);
                        var bodyFill = new Border { CornerRadius = new CornerRadius(24) };
                        frame.Children.Add(bodyFill);
                        frame.Children.Add(peek);
                        var window = new Window { Content = frame, WindowStyle = WindowStyle.None, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000, Width = 420, Height = 140 };
                        try
                        {
                            window.Show();
                            var footer = (FrameworkElement)peek.FindName("peekFooter")!;
                            var pager = (FrameworkElement)peek.FindName("pager")!;
                            var dots = (UIElement)peek.FindName("dots")!;
                            var dockRow = (FrameworkElement)peek.FindName("dockRow")!;
                            var setDock = typeof(NotchPeek).GetMethod("SetDock", BindingFlags.Instance | BindingFlags.NonPublic)!;
                            window.UpdateLayout();
                            var pagerYClosed = pager.TransformToAncestor(peek).Transform(new Point()).Y;
                            bool footerAvailable = footer.Visibility == Visibility.Visible && footer.ActualHeight >= 13 && dockRow.Visibility == Visibility.Collapsed;
                            setDock.Invoke(peek, [true]);
                            Pump(TimeSpan.FromMilliseconds(200));
                            peek.Measure(new Size(420, double.PositiveInfinity));
                            frame.Height = window.Height = peek.DesiredSize.Height;
                            window.UpdateLayout();
                            var pagerYOpen = pager.TransformToAncestor(peek).Transform(new Point()).Y;
                            var footerYOpen = footer.TransformToAncestor(peek).Transform(new Point()).Y;
                            bool attached = dockRow.Visibility == Visibility.Visible && dockRow.ActualHeight == 22
                                && Grid.GetRow(dockRow) == 1 && Math.Abs(pagerYOpen - pagerYClosed) < 0.1
                                && footer.ActualHeight >= 49 && footerYOpen + footer.ActualHeight <= peek.ActualHeight + 0.1;
                            check("el mini dock queda dentro del footer expandido y mantiene fijos los puntos", footerAvailable && attached);
                            foreach (var (language, locale) in new[] { (Language.Es, "es"), (Language.En, "en") })
                                foreach (bool highContrast in new[] { false, true })
                                {
                                    L10n.Init(language);
                                    Theme.ForceHighContrast = highContrast;
                                    Theme.Apply();
                                    model.Notify();
                                    paintBody.Invoke(notch, null);
                                    bodyFill.Background = body.Fill;
                                    typeof(NotchPeek).GetMethod("UpdateHeader", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(peek, null);
                                    window.UpdateLayout();
                                    foreach (var state in new[] { "open", "closed" })
                                    {
                                        if (state == "closed")
                                        {
                                            setDock.Invoke(peek, [false]);
                                            Pump(TimeSpan.FromMilliseconds(180));
                                        }
                                        else if (dockRow.Visibility != Visibility.Visible)
                                        {
                                            setDock.Invoke(peek, [true]);
                                            Pump(TimeSpan.FromMilliseconds(180));
                                        }
                                        if (state == "open")
                                        {
                                            dockRow.BeginAnimation(UIElement.OpacityProperty, null);
                                            dockRow.Opacity = 1;
                                            dots.BeginAnimation(UIElement.OpacityProperty, null);
                                            dots.Opacity = 0;
                                        }
                                        else
                                        {
                                            dots.BeginAnimation(UIElement.OpacityProperty, null);
                                            dots.Opacity = 1;
                                        }
                                        peek.Measure(new Size(420, double.PositiveInfinity));
                                        var height = peek.DesiredSize.Height;
                                        frame.Height = window.Height = height;
                                        window.UpdateLayout();
                                        foreach (double dpi in new[] { 96d, 120d, 144d })
                                        {
                                            var dockBitmap = new RenderTargetBitmap((int)Math.Ceiling(420 * dpi / 96), (int)Math.Ceiling(height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
                                            dockBitmap.Render(frame);
                                            var dockEncoder = new PngBitmapEncoder();
                                            dockEncoder.Frames.Add(BitmapFrame.Create(dockBitmap));
                                            using var dockOutput = File.Create(Path.Combine(captureDir, $"peek-footer-{state}-{locale}-{(highContrast ? "hc" : "normal")}-{dpi:0}.png"));
                                            dockEncoder.Save(dockOutput);
                                        }
                                    }
                                    foreach (double dpi in new[] { 96d, 120d, 144d })
                                    {
                                        var bitmap = new RenderTargetBitmap(420 * (int)(dpi / 96), 147 * (int)(dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
                                        bitmap.Render(frame);
                                        var encoder = new PngBitmapEncoder();
                                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                                        Directory.CreateDirectory(captureDir);
                                        using var output = File.Create(Path.Combine(captureDir, $"shelf-header-{locale}-{(highContrast ? "hc" : "normal")}-{dpi:0}.png"));
                                        encoder.Save(output);
                                    }
                                }
                            setDock.Invoke(peek, [false]);
                            Pump(TimeSpan.FromMilliseconds(180));
                            check("al cerrar el mini dock se recoge su fila y el footer vuelve a su altura", dockRow.Visibility == Visibility.Collapsed && footer.ActualHeight <= 14.1);
                        }
                        finally
                        {
                            window.Close();
                            Theme.ForceHighContrast = originalContrast;
                            Theme.Apply();
                            L10n.Init(originalLanguage == "en" ? Language.En : Language.Es);
                            model.Notify();
                        }
                    }
                }
                finally
                {
                    peek.Stop();
                    if (model.Hub.Pomodoro.Running) model.Hub.Pomodoro.Toggle.Execute(null);
                    model.SetPeekVisible(false);
                }

                var dropTarget = new Border { AllowDrop = true };
                ((Panel)notch.Content).Children.Add(dropTarget);
                dropTarget.Drop += (_, args) => args.Handled = true;
                Drop(dropTarget, new DataObject(DataFormats.FileDrop, new[] { first }));
                var shellFiles = new StringCollection();
                shellFiles.Add(second);
                var shellData = new DataObject();
                shellData.SetData(DataFormats.FileDrop, shellFiles);
                Drop(dropTarget, shellData);

                var restored = new ShelfWidget(persist: true);
                check("soltar rutas FileDrop en el notch las guarda en Shelf", restored.Tiles.Select(tile => tile.Path).SequenceEqual([first, second]));

                Action? finishRemoval = null;
                model.Hub.Shelf.RemovalAnimationRequested += (_, finish) => { finishRemoval = finish; return true; };
                model.Hub.Shelf.ClearOrRemove.Execute(null);
                var notchState = (NotchState)typeof(NotchWindow).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notch)!;
                notchState.Expand();
                notch.Collapse();
                check("el notch conserva la vista abierta mientras el estante anima el borrado", model.Hub.Shelf.IsRemoving && notchState.Mode == NotchMode.Expanded);
                finishRemoval!();
                Pump(TimeSpan.FromMilliseconds(50));
                check("el cierre solicitado se aplica cuando termina la animación", !model.Hub.Shelf.IsRemoving && notchState.Mode == NotchMode.Collapsed);
            }
            finally
            {
                notch.Stop();
                notch.Close();
                model.Hub.Dispose();
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("ITS_DATA_DIR", previous);
            try { Directory.Delete(dataDir, true); } catch { }
        }
    }

    private static void Drop(UIElement target, IDataObject data)
    {
        // WPF keeps DragEventArgs construction internal; raise the same preview/bubble pair as an OLE drop.
        var constructor = typeof(DragEventArgs).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        DragEventArgs Args(RoutedEvent routedEvent)
        {
            var args = (DragEventArgs)constructor.Invoke([data, DragDropKeyStates.None, DragDropEffects.Copy, target, new Point(0, 0)]);
            args.RoutedEvent = routedEvent;
            return args;
        }
        target.RaiseEvent(Args(System.Windows.DragDrop.PreviewDropEvent));
        target.RaiseEvent(Args(System.Windows.DragDrop.DropEvent));
    }

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
