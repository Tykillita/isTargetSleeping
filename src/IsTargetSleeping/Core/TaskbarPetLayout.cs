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
public readonly record struct PetSize(int Width, int Height, int Body, int Reach, int HideRight = 0, int HideMiddle = 0);

public static class PetHiding
{
    /// Dónde va la ventana de la mascota escondida detrás del logo: el cuerpo, tapado por
    /// él, y por su izquierda asoma `peek` píxeles de rejilla más.
    public static (int X, int Y) Behind(Win32.RECT logo, PetSize grid, int scale, int peek) =>
        (logo.Right - (grid.HideRight + peek) * scale, (logo.Top + logo.Bottom) / 2 - grid.HideMiddle * scale);
}

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
        var above = Above(bar, start, monitor, dpi, grid.Width, grid.Height) with { Fallback = fallback };
        if (placement != PetPlacement.Walk || !horizontal) return above;

        // De paseo: por el mismo borde, desde la izquierda de la barra hasta la bandeja.
        int margin = above.Scale * 4;
        int end = tray is { } t && t.Left > bar.Left && t.Left < bar.Right ? t.Left : bar.Right - bar.Width / 5;
        int min = Math.Max(monitor.Left, bar.Left + margin);
        int max = Math.Min(monitor.Right - above.Width, end - above.Width - margin);
        if (max < min) return above;
        return above with { X = Math.Clamp(above.X, min, max), MinX = min, MaxX = max };
    }

    /// Se asoma por el borde interior de la barra, centrada en el botón Inicio.
    private static PetSpot Above(Win32.RECT bar, Win32.RECT start, Win32.RECT monitor, uint dpi, int gridWidth, int gridHeight)
    {
        int n = PixelScale(dpi);
        int width = gridWidth * n, height = gridHeight * n;
        int overlap = 2 * n;   // las patitas pisan el borde de la barra
        int x, y;
        if (bar.Width >= bar.Height)
        {
            x = (start.Left + start.Right - width) / 2;
            bool top = Math.Abs(bar.Top - monitor.Top) < Math.Abs(bar.Bottom - monitor.Bottom);
            y = top ? bar.Bottom - overlap : bar.Top - height + overlap;
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
