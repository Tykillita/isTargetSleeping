namespace IsTargetSleeping;

/// Dónde vive la mascota: asomada sobre Inicio, dentro de la barra a la izquierda de
/// Inicio, o paseando por el borde de la barra.
public enum PetPlacement { Above, Left, Walk }

/// Coordenadas físicas, también en pantallas a la izquierda del monitor principal.
/// `Scale`: píxeles físicos por píxel de la rejilla (entero, para que se vea nítida).
/// `MinX`/`MaxX`: el recorrido del paseo (iguales a `X` si no pasea). `Fallback`: se
/// pidió «a la izquierda» y no había sitio, así que va sobre Inicio.
/// `Reach`: hasta qué columna llega la mascota por la derecha en reposo (su mano), para
/// pegarla al logo de Windows; las columnas de más allá son para los efectos.
/// `HideRight`/`HideMiddle`: escondida detrás del logo, la columna donde acaba su cuerpo
/// (se alinea con el borde derecho del logo) y la fila de su centro (con el del logo).
/// `CenterX`: la columna del centro de su cuerpo (sin cola ni efectos), la que va sobre el
/// logo de Windows; sin indicarla, el centro del lienzo.
public readonly record struct PetSize(int Width, int Height, int Body, int Reach, int HideRight = 0, int HideMiddle = 0, double CenterX = double.NaN)
{
    public double Middle => double.IsNaN(CenterX) ? Width / 2.0 : CenterX;
}

public static class PetHiding
{
    /// Dónde va la ventana de la mascota escondida detrás del logo: el cuerpo, tapado por
    /// él, y por su izquierda asoma `peek` píxeles de rejilla más.
    public static (int X, int Y) Behind(Win32.RECT logo, PetSize grid, int scale, int peek) =>
        (logo.Right - (grid.HideRight + peek) * scale, (logo.Top + logo.Bottom) / 2 - grid.HideMiddle * scale);
}

/// Lo que importa de la ventana de primer plano para saber si es pantalla completa.
public readonly record struct ForegroundWindow(
    string? Process, string ClassName, Win32.RECT Rect, long ExStyle, bool Visible, bool Cloaked, bool Minimized);

public readonly record struct PetSpot(int X, int Y, int Width, int Height, int Scale, int MinX, int MaxX, bool Fallback = false);

public static class TaskbarPetLayout
{
    /// La barra con ocultación automática deja solo una franja de unos pocos píxeles.
    public static bool BarVisible(Win32.RECT bar, Win32.RECT monitor, uint dpi)
    {
        double minimum = 12 * Scale(dpi);
        return Math.Min(bar.Right, monitor.Right) - Math.Max(bar.Left, monitor.Left) >= minimum
            && Math.Min(bar.Bottom, monitor.Bottom) - Math.Max(bar.Top, monitor.Top) >= minimum;
    }

    /// Si la ventana de primer plano es una app a pantalla completa (juego, vídeo,
    /// presentación sin bordes) que tapa el monitor de la barra: entonces la mascota se oculta.
    /// No cuentan las del propio Windows que ocupan la pantalla sin verse: vistas previas de
    /// la barra, Alt+Tab, Vista de tareas, escritorio (todas de Explorer) y la captura de
    /// Recortes; ni las ocultas, minimizadas o capas que dejan pasar los clics.
    public static bool FullscreenApp(ForegroundWindow window, Win32.RECT monitor)
    {
        if (window.Process is null || ShellProcesses.Contains(window.Process)) return false;
        if (window.ClassName is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        if (!window.Visible || window.Cloaked || window.Minimized) return false;
        const long clickThrough = Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT;
        if ((window.ExStyle & clickThrough) == clickThrough || (window.ExStyle & Win32.WS_EX_NOACTIVATE) != 0) return false;
        var r = window.Rect;
        return r.Left <= monitor.Left && r.Top <= monitor.Top && r.Right >= monitor.Right && r.Bottom >= monitor.Bottom;
    }

    private static readonly HashSet<string> ShellProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "ShellExperienceHost", "SnippingTool", "ScreenClippingHost",
    };

    /// Cuántos píxeles físicos mide cada píxel de la rejilla: 2 a 100 %, 3 a 150 %, 4 a 200 %…
    public static int PixelScale(uint dpi) => Math.Max(1, (int)Math.Round(2 * Scale(dpi), MidpointRounding.AwayFromZero));

    /// `grid`: el tamaño de la mascota en su rejilla (`Body`: las filas de abajo que ocupa
    /// el cuerpo; encima quedan las Z, los corazones…). `tray`: la bandeja (fin del paseo).
    /// `logo`: el logo de Windows tal como se ve, si se reconoció (si no, se estima).
    public static PetSpot Place(PetPlacement placement, Win32.RECT bar, Win32.RECT start, Win32.RECT? tray,
        Win32.RECT monitor, uint dpi, PetSize grid, Win32.RECT? logo = null)
    {
        bool horizontal = bar.Width >= bar.Height;
        if (placement == PetPlacement.Left && horizontal && Left(bar, start, logo, monitor, dpi, grid) is { } inside)
            return inside;
        bool fallback = placement == PetPlacement.Left;
        var above = Above(bar, start, logo, monitor, dpi, grid) with { Fallback = fallback };
        if (placement != PetPlacement.Walk || !horizontal) return above;

        // De paseo: por el mismo borde, desde la izquierda de la barra hasta la bandeja.
        int margin = above.Scale * 4;
        int end = tray is { } t && t.Left > bar.Left && t.Left < bar.Right ? t.Left : bar.Right - bar.Width / 5;
        int min = Math.Max(monitor.Left, bar.Left + margin);
        int max = Math.Min(monitor.Right - above.Width, end - above.Width - margin);
        if (max < min) return above;
        return above with { X = Math.Clamp(above.X, min, max), MinX = min, MaxX = max };
    }

    /// De pie justo encima del logo de Windows, con el centro de su cuerpo sobre el del logo
    /// (sin reconocer el logo, se estima: ~22 px a 100 %, centrado en el botón Inicio). Con la
    /// barra arriba, cuelga por debajo de ella.
    private static PetSpot Above(Win32.RECT bar, Win32.RECT start, Win32.RECT? logo, Win32.RECT monitor, uint dpi, PetSize grid)
    {
        int n = PixelScale(dpi);
        int width = grid.Width * n, height = grid.Height * n;
        int overlap = 2 * n;   // las patitas pisan el borde de la barra
        int x, y;
        if (bar.Width >= bar.Height)
        {
            int half = (int)Math.Round(11 * Scale(dpi));
            int middle = (start.Left + start.Right) / 2, logoTop = (start.Top + start.Bottom) / 2 - half;
            if (logo is { } l) (middle, logoTop) = ((l.Left + l.Right) / 2, l.Top);
            x = middle - (int)Math.Round(grid.Middle * n);
            bool top = Math.Abs(bar.Top - monitor.Top) < Math.Abs(bar.Bottom - monitor.Bottom);
            // Un píxel de aire entre las patas y el logo.
            y = top ? bar.Bottom - overlap : logoTop - Math.Max(1, n / 2) - height;
        }
        else
        {
            bool left = Math.Abs(bar.Left - monitor.Left) < Math.Abs(bar.Right - monitor.Right);
            x = left ? bar.Right - overlap : bar.Left - width + overlap;
            y = (start.Top + start.Bottom - height) / 2;
        }
        x = Math.Clamp(x, monitor.Left, Math.Max(monitor.Left, monitor.Right - width));
        y = Math.Clamp(y, monitor.Top, Math.Max(monitor.Top, monitor.Bottom - height));
        return new(x, y, width, height, n, x, x);
    }

    /// Dentro de la barra, a la izquierda de Inicio y pegada al logo de Windows (su mano
    /// lo toca, como si fueran un solo dibujo): el cuerpo cabe en el alto de la barra y los
    /// efectos de encima pueden asomar. Null si no hay sitio (barra alineada a la izquierda,
    /// Inicio pegado al borde).
    private static PetSpot? Left(Win32.RECT bar, Win32.RECT start, Win32.RECT? logo, Win32.RECT monitor, uint dpi, PetSize grid)
    {
        int pad = Math.Max(1, (int)Math.Round(2 * Scale(dpi)));
        int n = Math.Min(PixelScale(dpi), (bar.Height - 2 * pad) / grid.Body);
        if (n < 1) return null;
        int width = grid.Width * n, height = grid.Height * n;
        // El botón es más ancho que el logo: sin reconocerlo, ~22 px a 100 %, centrado en el botón.
        int logoLeft = logo?.Left ?? (start.Left + start.Right) / 2 - (int)Math.Round(11 * Scale(dpi));
        int x = logoLeft - grid.Reach * n;
        // Un mínimo de aire con el borde de la barra (y con Widgets, que vive en la esquina).
        if (x < Math.Max(bar.Left, monitor.Left) + (int)Math.Round(56 * Scale(dpi))) return null;
        // El cuerpo, a la altura del logo (o centrado en la barra).
        int middle = logo is { } l ? (l.Top + l.Bottom) / 2 : bar.Top + bar.Height / 2;
        int bodyTop = Math.Clamp(middle - grid.Body * n / 2, bar.Top, Math.Max(bar.Top, bar.Bottom - grid.Body * n));
        int y = bodyTop - (grid.Height - grid.Body) * n;
        return new(x, y, width, height, n, x, x);
    }

    private static double Scale(uint dpi) => (dpi == 0 ? 96 : dpi) / 96.0;
}
