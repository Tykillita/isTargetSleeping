using System.IO;
using System.Windows;
using System.Windows.Input;

namespace IsTargetSleeping.UI;

/// Permite arrastrar un archivo o carpeta hacia fuera de la ventana (al Explorador, a un correo…) desde cualquier elemento.
/// Solo ofrece la ruta: el que recibe decide si copia o mueve.
public static class FileDrag
{
    private const double MinDistance = 6;

    public static readonly DependencyProperty PathProperty =
        DependencyProperty.RegisterAttached("Path", typeof(string), typeof(FileDrag), new PropertyMetadata(null, OnChanged));
    private static readonly DependencyProperty StartProperty =
        DependencyProperty.RegisterAttached("Start", typeof(Point?), typeof(FileDrag));

    public static string? GetPath(DependencyObject d) => (string?)d.GetValue(PathProperty);
    public static void SetPath(DependencyObject d, string? value) => d.SetValue(PathProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        element.PreviewMouseLeftButtonDown -= OnDown;
        element.PreviewMouseMove -= OnMove;
        element.PreviewMouseLeftButtonUp -= OnUp;
        if (e.NewValue is null) return;
        element.PreviewMouseLeftButtonDown += OnDown;
        element.PreviewMouseMove += OnMove;
        element.PreviewMouseLeftButtonUp += OnUp;
    }

    private static void OnDown(object sender, MouseButtonEventArgs e)
    {
        // Los botones de dentro de la fila siguen siendo botones.
        for (var d = e.OriginalSource as DependencyObject; d is not null && !ReferenceEquals(d, sender); d = System.Windows.Media.VisualTreeHelper.GetParent(d))
            if (d is System.Windows.Controls.Primitives.ButtonBase) return;
        ((DependencyObject)sender).SetValue(StartProperty, e.GetPosition((IInputElement)sender));
    }

    private static void OnUp(object sender, MouseButtonEventArgs e) => ((DependencyObject)sender).ClearValue(StartProperty);

    private static void OnMove(object sender, MouseEventArgs e)
    {
        var element = (DependencyObject)sender;
        if (e.LeftButton != MouseButtonState.Pressed || element.GetValue(StartProperty) is not Point start) return;
        var now = e.GetPosition((IInputElement)sender);
        if (Math.Abs(now.X - start.X) < MinDistance && Math.Abs(now.Y - start.Y) < MinDistance) return;
        element.ClearValue(StartProperty);
        if (GetPath(element) is not { } path || !(File.Exists(path) || Directory.Exists(path))) return;
        DragDrop.DoDragDrop(element, new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
    }
}
