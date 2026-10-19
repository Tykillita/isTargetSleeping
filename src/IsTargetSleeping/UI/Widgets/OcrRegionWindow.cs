using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingGraphics = System.Drawing.Graphics;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace IsTargetSleeping.UI;

/// Selección efímera de pantalla para OCR; no guarda la captura.
public sealed class OcrRegionWindow : Window
{
    private readonly Canvas canvas = new();
    private readonly WpfRectangle selection = new() { Stroke = Brushes.White, StrokeThickness = 1.5, Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(45, 100, 170, 255)), Visibility = Visibility.Collapsed };
    private Point start;
    public DrawingRectangle? SelectedPixels { get; private set; }

    public OcrRegionWindow()
    {
        WindowStyle = WindowStyle.None; WindowState = WindowState.Maximized; AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)); Topmost = true; ShowInTaskbar = false;
        Cursor = Cursors.Cross; Content = canvas; canvas.Children.Add(selection);
        PreviewMouseLeftButtonDown += Begin; PreviewMouseMove += Move; PreviewMouseLeftButtonUp += End;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) DialogResult = false; };
    }

    private void Begin(object sender, MouseButtonEventArgs e)
    {
        start = e.GetPosition(canvas); Mouse.Capture(canvas);
        selection.Visibility = Visibility.Visible; Canvas.SetLeft(selection, start.X); Canvas.SetTop(selection, start.Y);
        e.Handled = true;
    }

    private void Move(object sender, MouseEventArgs e)
    {
        if (!canvas.IsMouseCaptured) return;
        var point = e.GetPosition(canvas); SetRect(point);
    }

    private void SetRect(Point point)
    {
        Canvas.SetLeft(selection, Math.Min(start.X, point.X)); Canvas.SetTop(selection, Math.Min(start.Y, point.Y));
        selection.Width = Math.Abs(point.X - start.X); selection.Height = Math.Abs(point.Y - start.Y);
    }

    private void End(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(canvas); SetRect(point); Mouse.Capture(null);
        var a = PointToScreen(start); var b = PointToScreen(point);
        int left = (int)Math.Round(Math.Min(a.X, b.X)), top = (int)Math.Round(Math.Min(a.Y, b.Y));
        int width = (int)Math.Round(Math.Abs(a.X - b.X)), height = (int)Math.Round(Math.Abs(a.Y - b.Y));
        if (width > 2 && height > 2) SelectedPixels = new DrawingRectangle(left, top, width, height);
        DialogResult = SelectedPixels is not null; e.Handled = true;
    }

    public static async Task<string?> ReadScreenSelection()
    {
        var window = new OcrRegionWindow();
        if (window.ShowDialog() != true || window.SelectedPixels is not { } area) return null;
        var path = Path.Combine(Path.GetTempPath(), $"istargetsleeping-ocr-{Guid.NewGuid():N}.png");
        try
        {
            using (var bitmap = new DrawingBitmap(area.Width, area.Height))
            using (var graphics = DrawingGraphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(area.Left, area.Top, 0, 0, bitmap.Size);
                bitmap.Save(path, ImageFormat.Png);
            }
            return await Ocr.ReadImage(path);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
