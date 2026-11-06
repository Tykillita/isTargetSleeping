using System.Windows;
using System.Windows.Media;

namespace IsTargetSleeping.UI;

/// El icono de un tipo de aparato (portátil, ratón, teclado, auriculares, altavoz, mando, móvil, reloj, lápiz o, si no se sabe, Bluetooth): trazos finos
/// y redondeados sobre una cuadrícula de 24, del color del texto, como los símbolos de las baterías de iOS.
public sealed class DeviceIcon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(DeviceKind), typeof(DeviceIcon),
        new FrameworkPropertyMetadata(DeviceKind.Other, FrameworkPropertyMetadataOptions.AffectsRender));
    public DeviceKind Kind { get => (DeviceKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }

    /// Un color propio (el notch es negro siempre); sin él, el del tema.
    public Brush? Ink { get; set; }

    private static readonly Dictionary<DeviceKind, Geometry> Shapes = new()
    {
        [DeviceKind.Laptop] = Parse("M5,6 H19 A1,1 0 0 1 20,7 V16 H4 V7 A1,1 0 0 1 5,6 Z M2,19 H22 M10,19 H14"),
        [DeviceKind.Mouse] = Parse("M12,3 C8.5,3 6.5,5.5 6.5,9.5 V14 C6.5,18 8.8,21 12,21 C15.2,21 17.5,18 17.5,14 V9.5 C17.5,5.5 15.5,3 12,3 Z M12,3 V9 M6.5,9.5 H17.5"),
        [DeviceKind.Keyboard] = Parse("M4,6.5 H20 A1.5,1.5 0 0 1 21.5,8 V16 A1.5,1.5 0 0 1 20,17.5 H4 A1.5,1.5 0 0 1 2.5,16 V8 A1.5,1.5 0 0 1 4,6.5 Z M6.5,10 H6.6 M10,10 H10.1 M13.5,10 H13.6 M17,10 H17.1 M7,14 H17"),
        [DeviceKind.Headphones] = Parse("M4.5,15.5 V12.5 A7.5,7.5 0 0 1 19.5,12.5 V15.5 M4.5,13.5 H7.5 V19.5 H5.5 A1,1 0 0 1 4.5,18.5 Z M19.5,13.5 H16.5 V19.5 H18.5 A1,1 0 0 0 19.5,18.5 Z"),
        [DeviceKind.Speaker] = Parse("M8,3 H16 A1.5,1.5 0 0 1 17.5,4.5 V19.5 A1.5,1.5 0 0 1 16,21 H8 A1.5,1.5 0 0 1 6.5,19.5 V4.5 A1.5,1.5 0 0 1 8,3 Z M12,7 H12.1 M9,15 A3,3 0 1 0 15,15 A3,3 0 1 0 9,15"),
        [DeviceKind.Gamepad] = Parse("M7,8.5 H17 C19.8,8.5 21.8,12 21.8,15.2 C21.8,17.2 20.6,18.2 19.2,18.2 C17.8,18.2 17,17 16.2,16 H7.8 C7,17 6.2,18.2 4.8,18.2 C3.4,18.2 2.2,17.2 2.2,15.2 C2.2,12 4.2,8.5 7,8.5 Z M7,11 V14 M5.5,12.5 H8.5 M16,11.7 H16.1 M18,13.5 H18.1"),
        [DeviceKind.Phone] = Parse("M8.5,2.5 H15.5 A2,2 0 0 1 17.5,4.5 V19.5 A2,2 0 0 1 15.5,21.5 H8.5 A2,2 0 0 1 6.5,19.5 V4.5 A2,2 0 0 1 8.5,2.5 Z M10.5,18.5 H13.5"),
        [DeviceKind.Watch] = Parse("M9,6.5 L9.8,2.5 H14.2 L15,6.5 M9,17.5 L9.8,21.5 H14.2 L15,17.5 M8.5,6.5 H15.5 A1.5,1.5 0 0 1 17,8 V16 A1.5,1.5 0 0 1 15.5,17.5 H8.5 A1.5,1.5 0 0 1 7,16 V8 A1.5,1.5 0 0 1 8.5,6.5 Z M12,9.5 V12 L13.8,13.2"),
        [DeviceKind.Pen] = Parse("M4.5,19.5 L5.5,15 L16.5,4 L20,7.5 L9,18.5 Z M14.5,6 L18,9.5"),
        [DeviceKind.Other] = Parse("M7,7.5 L17,16.5 L12,21 V3 L17,7.5 L7,16.5"),
    };

    private static Geometry Parse(string data) { var g = Geometry.Parse(data); g.Freeze(); return g; }

    public DeviceIcon()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => Theme.Changed += InvalidateVisual;
        Unloaded += (_, _) => Theme.Changed -= InvalidateVisual;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double side = Math.Min(ActualWidth, ActualHeight);
        if (side <= 0) return;
        var scale = side / 24.0;
        var pen = new Pen(Ink ?? Theme.Brush(Theme.Current.Fg),Math.Max(1.2, 1.7 * scale)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.PushTransform(new TranslateTransform((ActualWidth - side) / 2, (ActualHeight - side) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        // El grosor ya va escalado: se compensa para que no engorde con el tamaño.
        pen.Thickness = Math.Max(1.2, 1.7 * scale) / scale;
        dc.DrawGeometry(null, pen, Shapes[Kind]);
        dc.Pop(); dc.Pop();
    }
}
