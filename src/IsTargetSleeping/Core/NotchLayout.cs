namespace IsTargetSleeping;

/// La pastilla colapsada, la vista expandida y la zona de ratón que la despierta. Píxeles físicos.
public readonly record struct NotchRects(Win32.RECT Pill, Win32.RECT Expanded, Win32.RECT Hot);

/// Dónde cae el dock tipo notch en un monitor: centrado arriba, pegado al borde (o justo
/// debajo de una barra de tareas superior). Lógica pura, sin WPF, para poder probarla.
public static class NotchLayout
{
    public const double PillWidth = 150, PillHeight = 30;   // la pastilla cerrada mínima; crece solo lo que pidan sus datos y actividades
    /// «Notch mínimo»: sin nada que enseñar, la pastilla se queda en una franja negra fina (DIP). La zona de ratón sigue siendo la de la pastilla entera.
    public const double StripWidth = 96, StripHeight = 5;
    /// Los botones flotantes que cuelgan debajo del notch abierto: su tamaño y el hueco que los separa.
    public const double DockWidth = 276, DockHeight = 34, DockGap = 8;
    /// La lente de cristal del dock sobresale de la barra por arriba y por abajo: la ventana deja ese margen (DIP) a cada lado.
    public const double DockPad = 6;
    /// La zona que lo despierta: un poco más ancha que la pastilla y hasta el borde de la pantalla.
    private const double HotSide = 28, HotBelow = 8;

    /// `monitor` y `work` son los rectángulos del monitor y de su área de trabajo; `scale`, dpi/96;
    /// el tamaño expandido viene en DIP (lo que mide el contenido).
    /// `pillWidth` (DIP) crece cuando la pastilla enseña actividades en vivo junto a sus datos. Con `strip`, la pastilla es la franja fina.
    public static NotchRects Place(Win32.RECT monitor, Win32.RECT work, double scale, double expandedWidth, double expandedHeight, double pillWidth = PillWidth, bool strip = false, double pillHeight = PillHeight)
    {
        int top = Math.Max(monitor.Top, work.Top);
        int centerX = monitor.Left + monitor.Width / 2;
        int fullW = Px(Math.Max(PillWidth, pillWidth), scale), fullH = Px(Math.Max(PillHeight, pillHeight), scale);
        int pillW = strip ? Px(StripWidth, scale) : fullW, pillH = strip ? Px(StripHeight, scale) : fullH;
        int width = Math.Clamp(Px(expandedWidth, scale), pillW, Math.Max(pillW, monitor.Width));
        int height = Math.Clamp(Px(expandedHeight, scale), pillH, Math.Max(pillH, work.Bottom - top));
        var pill = At(centerX, top, pillW, pillH);
        var cover = strip ? At(centerX, top, fullW, fullH) : pill;
        int side = Px(HotSide, scale);
        var hot = new Win32.RECT
        {
            Left = cover.Left - side, Right = cover.Right + side,
            Top = monitor.Top, Bottom = cover.Bottom + Px(HotBelow, scale),
        };
        return new NotchRects(pill, At(centerX, top, width, height), hot);
    }

    /// Los botones flotantes, centrados justo debajo de la isla. El ancho lo da lo que midan sus botones (por defecto, DockWidth).
    public static Win32.RECT DockBelow(Win32.RECT island, double scale, double widthDip = DockWidth)
    {
        int width = Px(widthDip, scale), top = island.Bottom + Px(DockGap, scale), centerX = island.Left + island.Width / 2;
        return new() { Left = centerX - width / 2, Top = top, Right = centerX - width / 2 + width, Bottom = top + Px(DockHeight, scale) };
    }

    /// El rectángulo de los botones con el margen por arriba y por abajo para la lente (lo que ocupa su ventana).
    public static Win32.RECT WithOvershoot(Win32.RECT dock, double scale) =>
        new() { Left = dock.Left, Right = dock.Right, Top = dock.Top - Px(DockPad, scale), Bottom = dock.Bottom + Px(DockPad, scale) };

    public static Win32.RECT Union(Win32.RECT a, Win32.RECT b) => new()
    {
        Left = Math.Min(a.Left, b.Left), Top = Math.Min(a.Top, b.Top), Right = Math.Max(a.Right, b.Right), Bottom = Math.Max(a.Bottom, b.Bottom),
    };

    public static bool Contains(Win32.RECT r, int x, int y) => x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;

    public static Win32.RECT Inflate(Win32.RECT r, int by) =>
        new() { Left = r.Left - by, Top = r.Top - by, Right = r.Right + by, Bottom = r.Bottom + by };

    private static int Px(double dip, double scale) => (int)Math.Round(dip * scale);

    private static Win32.RECT At(int centerX, int top, int width, int height) =>
        new() { Left = centerX - width / 2, Top = top, Right = centerX - width / 2 + width, Bottom = top + height };
}
