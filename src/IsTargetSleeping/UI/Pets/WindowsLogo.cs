using System.Runtime.InteropServices;

namespace IsTargetSleeping.UI.Pets;

/// El logo de Windows del botón Inicio tal como se ve en pantalla: qué píxeles son logo
/// (para pintar luces encima sin tocar el fondo de la barra) y su borde real (para pegar
/// la mascota). Se captura el botón y se quedan los píxeles azules del logo.
public sealed class WindowsLogo
{
    public Win32.RECT Bounds { get; }
    private readonly bool[] mask;

    private WindowsLogo(Win32.RECT bounds, bool[] mask)
    {
        Bounds = bounds;
        this.mask = mask;
    }

    /// Captura el botón Inicio (en físicos). Null si no se reconoce un logo cuadrado y azul
    /// (otro tema, Inicio pulsado…): entonces la mascota sigue sin luces.
    /// Llamar con la mascota oculta, para que no salga en la captura.
    public static WindowsLogo? Probe(Win32.RECT start)
    {
        int w = start.Width, h = start.Height;
        if (w <= 4 || h <= 4 || w > 400 || h > 400) return null;
        var pixels = Capture(start.Left, start.Top, w, h);
        if (pixels is null) return null;

        // El logo es azul (claro arriba, más intenso abajo) sobre la barra, clara u oscura.
        var blue = new bool[w * h];
        int left = w, top = h, right = -1, bottom = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                int b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
                if (b < 140 || b - r < 60 || b - g < -30) continue;
                blue[y * w + x] = true;
                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
        if (right < 0) return null;
        int lw = right - left + 1, lh = bottom - top + 1;
        // Un cuadrado de un tamaño razonable para el botón, casi lleno (cuatro paneles).
        if (lw < h / 5 || lw > h * 4 / 5 || Math.Abs(lw - lh) > Math.Max(2, lw / 6)) return null;
        var mask = new bool[lw * lh];
        int count = 0;
        for (int y = 0; y < lh; y++)
            for (int x = 0; x < lw; x++)
                if (blue[(top + y) * w + left + x]) { mask[y * lw + x] = true; count++; }
        if (count < lw * lh / 2) return null;
        var bounds = new Win32.RECT { Left = start.Left + left, Top = start.Top + top, Right = start.Left + right + 1, Bottom = start.Top + bottom + 1 };
        return new WindowsLogo(bounds, mask);
    }

    /// Las luces encima del logo: BGRA premultiplicado del tamaño de `Bounds`, transparente
    /// fuera de los paneles. Null si no hay luces.
    public byte[]? Lights(PetGlow glow)
    {
        if (glow.Dark) return null;
        int w = Bounds.Width, h = Bounds.Height;
        var bytes = new byte[w * h * 4];
        bool any = false;
        uint rgb = glow.Rgb;
        double position = -h + (w + h) * glow.Shine, band = Math.Max(2, w / 6.0);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!mask[y * w + x]) continue;
                double alpha = glow[(x * 2 >= w ? 1 : 0) + (y * 2 >= h ? 2 : 0)];
                // Encima del brillo, un destello en diagonal que cruza el logo.
                if (glow.Shine >= 0) alpha = Math.Max(alpha, 0.55 * Math.Max(0, 1 - Math.Abs(x - y - position) / band));
                if (alpha <= 0.004) continue;
                byte a = (byte)Math.Round(Math.Min(1, alpha) * 255);
                int i = (y * w + x) * 4;
                bytes[i] = (byte)((rgb & 0xFF) * a / 255);
                bytes[i + 1] = (byte)((rgb >> 8 & 0xFF) * a / 255);
                bytes[i + 2] = (byte)((rgb >> 16 & 0xFF) * a / 255);
                bytes[i + 3] = a;
                any = true;
            }
        return any ? bytes : null;
    }

    /// BGRA de un trozo de pantalla.
    private static byte[]? Capture(int x, int y, int w, int h)
    {
        var screen = Win32.GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) return null;
        var memory = Win32.CreateCompatibleDC(screen);
        var header = new Win32.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<Win32.BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32,
        };
        var dib = Win32.CreateDIBSection(memory, ref header, 0, out var bits, IntPtr.Zero, 0);
        try
        {
            if (dib == IntPtr.Zero) return null;
            var old = Win32.SelectObject(memory, dib);
            bool ok = Win32.BitBlt(memory, 0, 0, w, h, screen, x, y, Win32.SRCCOPY);
            Win32.SelectObject(memory, old);
            if (!ok) return null;
            var pixels = new byte[w * h * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            return pixels;
        }
        finally
        {
            if (dib != IntPtr.Zero) Win32.DeleteObject(dib);
            Win32.DeleteDC(memory);
            Win32.ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
