using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IsTargetSleeping.UI;

/// Lo que hay detrás del notch, desenfocado, para el vidrio líquido de su cuerpo (ver `BackdropBlur`). Se copia la zona de pantalla que
/// ocupa el notch abierto: una ventana en capas (como el propio notch) no sale en esa copia, así que no se ve a sí misma. La copia vive solo
/// en memoria, reducida a unas decenas de píxeles de color; no se guarda ni sale del PC.
public sealed class NotchBackdrop
{
    public Image Element { get; } = new() { Stretch = Stretch.Fill, IsHitTestVisible = false, Opacity = 0 };

    public NotchBackdrop()
    {
        RenderOptions.SetBitmapScalingMode(Element, BitmapScalingMode.HighQuality);
    }

    /// Claridad media de la última copia (0 negro … 1 blanco).
    public double Luminance { get; private set; }
    /// Cambia con cada copia nueva (para saber cuándo repintar lo que depende de sus colores).
    public int Version { get; private set; }
    private byte[]? small;
    private int smallWidth, smallHeight;
    private double cellDip;

    /// Los colores de lo de detrás a lo largo de una línea horizontal a `yDip` (en el sistema del notch, desde su borde de arriba), de
    /// izquierda a derecha, aclarados un poco: son los que refracta el canto de vidrio del borde de abajo. Null si no hay copia.
    public Color[]? EdgeColors(double yDip, int count)
    {
        if (small is null || Element.Opacity == 0 || count < 1) return null;
        int row = Math.Clamp((int)(yDip / cellDip), 0, smallHeight - 1);
        var colors = new Color[count];
        for (int i = 0; i < count; i++)
        {
            int column = Math.Clamp((int)((i + 0.5) / count * smallWidth), 0, smallWidth - 1), o = (row * smallWidth + column) * 4;
            // Hacia el blanco: el canto de un vidrio refleja más luz de la que recibe el fondo.
            byte Lift(byte c) => (byte)(c + (255 - c) * 0.35);
            colors[i] = Color.FromRgb(Lift(small[o + 2]), Lift(small[o + 1]), Lift(small[o]));
        }
        return colors;
    }

    public void Hide()
    {
        Element.Opacity = 0;
        small = null;
    }

    /// Copia, desenfoca y enseña la zona `area` (píxeles de pantalla). Devuelve false si no pudo (alto contraste, zona rara o error de GDI):
    /// entonces el vidrio se queda sin fondo, solo con su tinte.
    public bool Capture(Win32.RECT area, double scale)
    {
        int width = area.Width, height = area.Height;
        if (SystemParameters.HighContrast || width < 40 || height < 20 || (long)width * height > 6_000_000) { Hide(); return false; }
        try
        {
            var pixels = Grab(area);
            if (pixels is null) return false;
            var small = BackdropBlur.Reduce(pixels, width, height, Math.Max(4, (int)Math.Round(14 * scale)), out int w, out int h, 1.7, 0.1);
            Luminance = BackdropBlur.Luminance(small, w, h);
            this.small = small;
            (smallWidth, smallHeight, cellDip) = (w, h, Math.Max(4, (int)Math.Round(14 * scale)) / scale);
            Version++;
            var image = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, small, w * 4);
            image.Freeze();
            Element.Source = image;
            Element.Width = width / scale;
            Element.Height = height / scale;
            Element.Opacity = 1;
            return true;
        }
        catch (Exception e)
        {
            AppLog.Write($"fondo del notch: {e.Message}");
            return false;
        }
    }

    /// Los píxeles de la pantalla en `area` (BGRA, de arriba abajo). Sin CAPTUREBLT, BitBlt deja fuera las ventanas en capas.
    private static byte[]? Grab(Win32.RECT area)
    {
        int width = area.Width, height = area.Height;
        var screen = Win32.GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) return null;
        var memory = Win32.CreateCompatibleDC(screen);
        IntPtr bitmap = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            var info = new Win32.BITMAPINFOHEADER { biSize = Marshal.SizeOf<Win32.BITMAPINFOHEADER>(), biWidth = width, biHeight = -height, biPlanes = 1, biBitCount = 32 };
            bitmap = Win32.CreateDIBSection(screen, ref info, 0, out var bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero) return null;
            previous = Win32.SelectObject(memory, bitmap);
            if (!Win32.BitBlt(memory, 0, 0, width, height, screen, area.Left, area.Top, Win32.SRCCOPY)) return null;
            var pixels = new byte[width * height * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            return pixels;
        }
        finally
        {
            if (previous != IntPtr.Zero) Win32.SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero) Win32.DeleteObject(bitmap);
            Win32.DeleteDC(memory);
            Win32.ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
