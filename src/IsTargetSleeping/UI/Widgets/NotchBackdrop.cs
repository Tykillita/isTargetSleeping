using System.Diagnostics;
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
        Element.BeginAnimation(UIElement.OpacityProperty, null);
        Element.Opacity = 0;
        small = null;
    }

    /// Copia, desenfoca y enseña la zona `area` (píxeles de pantalla) SIN bloquear la interfaz: la copia y el desenfoque corren en otro hilo y la imagen entra (con un fundido)
    /// cuando está lista; `done` se llama entonces en el hilo de la interfaz (también si no se pudo: alto contraste, zona rara, error de GDI o una herramienta de captura delante;
    /// entonces el vidrio se queda sin fondo, solo con su tinte). Antes se esperaba a la copia hasta 250 ms con la interfaz parada, justo cuando el notch empezaba a abrirse
    /// (con un fondo de vídeo, DWM tarda): la animación de apertura se saltaba casi todos sus fotogramas y se veía a golpes.
    public void CaptureAsync(Win32.RECT area, double scale, Action done)
    {
        int width = area.Width, height = area.Height;
        if (Theme.Flat || width < 40 || height < 20 || (long)width * height > 6_000_000) { Hide(); done(); return; }
        // La copia de pantalla (GDI) espera a DWM: con la herramienta de recortes o una captura en curso puede quedarse esperando y dejar la pantalla
        // congelada. Mientras una de esas herramientas está delante no se copia.
        if (DateTime.UtcNow < pausedUntil || CaptureToolInFront()) { Hide(); done(); return; }
        if (Interlocked.Exchange(ref grabbing, 1) == 1) return;   // una copia anterior sigue en marcha
        var dispatcher = Element.Dispatcher;
        int cell = Math.Max(4, (int)Math.Round(14 * scale));
        _ = Task.Run(() =>
        {
            byte[]? reduced = null;
            int w = 0, h = 0;
            double luma = 0;
            var clock = Stopwatch.StartNew();
            try
            {
                var pixels = Grab(area);
                if (pixels is not null)
                {
                    reduced = BackdropBlur.Reduce(pixels, width, height, cell, out w, out h, 1.7, 0.1);
                    luma = BackdropBlur.Luminance(reduced, w, h);
                }
            }
            catch (Exception e) { AppLog.Write($"fondo del notch: {e.Message}"); }
            finally { Interlocked.Exchange(ref grabbing, 0); }
            long took = clock.ElapsedMilliseconds;
            dispatcher.BeginInvoke(() =>
            {
                // Si DWM tarda de más tres veces seguidas, el notch se queda sin fondo de vidrio unos minutos en vez de seguir copiando.
                if (took > SlowGrabMs * 4 && ++slowGrabs >= 3)
                {
                    slowGrabs = 0;
                    pausedUntil = DateTime.UtcNow.AddMinutes(10);
                    AppLog.Write($"fondo del notch: la copia de pantalla tardó {took} ms tres veces seguidas; sin fondo de vidrio durante 10 minutos");
                }
                else if (took <= SlowGrabMs * 4) slowGrabs = 0;
                if (reduced is null) { Hide(); done(); return; }
                Luminance = luma;
                small = reduced;
                (smallWidth, smallHeight, cellDip) = (w, h, cell / scale);
                Version++;
                var image = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, reduced, w * 4);
                image.Freeze();
                Element.Source = image;
                Element.Width = width / scale;
                Element.Height = height / scale;
                bool visible = Element.Opacity > 0;
                Element.BeginAnimation(UIElement.OpacityProperty, null);
                if (visible) Element.Opacity = 1;
                else Element.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
                done();
            });
        });
    }

    private const int SlowGrabMs = 120;
    private static DateTime pausedUntil;
    private static int grabbing, slowGrabs;

    /// La herramienta de recortes (o la barra de juegos) tiene el primer plano.
    private static bool CaptureToolInFront()
    {
        var window = Win32.GetForegroundWindow();
        if (window == IntPtr.Zero || Win32.GetWindowThreadProcessId(window, out int pid) == 0) return false;
        try
        {
            using var process = Process.GetProcessById(pid);
            return ProcessMonitor.IsScreenCapture(process.ProcessName);
        }
        catch { return false; }
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
