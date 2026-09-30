using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace IsTargetSleeping.UI;

/// Vidrio de verdad: el material acrílico que dibuja el propio DWM de Windows 11
/// (el de los menús y flyouts del sistema), que desenfoca lo que hay detrás de la
/// ventana. WPF pinta encima con fondo transparente y el tinte negro del tema.
public static class Glass
{
    private const int DWMSBT_TRANSIENTWINDOW = 3;   // acrílico

    /// Llamar en SourceInitialized. `extendFrame`: si la ventana no usa WindowChrome,
    /// hay que extender el marco a mano para que el acrílico ocupe toda el área.
    public static void Apply(Window window, bool extendFrame)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        int dark = 1;
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        int round = Win32.DWMWCP_ROUND;
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        int border = 0x3E3A3A;   // COLORREF (0x00BBGGRR): borde de luz gris sobre el negro
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_BORDER_COLOR, ref border, sizeof(int));
        // Barra de título negra aunque Windows tenga activado el color de énfasis en los títulos.
        // El panel no tiene barra de título: ahí DWM no debe pintar ningún color (dejaría
        // una franja opaca de 31 px arriba, sin acrílico, detrás de la cabecera).
        int caption = window is PanelWindow ? unchecked((int)0xFFFFFFFE) /* DWMWA_COLOR_NONE */ : 0x0E0C0C, text = 0xF7F5F5;
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_TEXT_COLOR, ref text, sizeof(int));

        if (!Theme.BackdropSupported) return;
        if (HwndSource.FromHwnd(hwnd) is { } source) source.CompositionTarget.BackgroundColor = Colors.Transparent;
        window.Background = Brushes.Transparent;
        if (extendFrame)
        {
            var margins = new Win32.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            Win32.DwmExtendFrameIntoClientArea(hwnd, ref margins);
        }
        int backdrop = DWMSBT_TRANSIENTWINDOW;
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
    }
}
