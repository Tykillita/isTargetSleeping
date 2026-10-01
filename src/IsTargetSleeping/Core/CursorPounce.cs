namespace IsTargetSleeping;

/// Cuándo se lanza el gato sobre el cursor: cuando lleva `Hold` segundos quieto encima
/// de él (moverse menos de `Tolerance` píxeles no cuenta) y ya pasó el enfriamiento
/// desde el último salto. Tras lanzarse, el cursor tiene que volver a quedarse quieto.
public sealed class CursorPounce(double hold = 1.2, double cooldown = 4, int tolerance = 2)
{
    private double since = double.NaN, readyAt = double.NegativeInfinity;
    private int x, y;

    /// Llamar en cada fotograma; devuelve true el instante en que se lanza.
    public bool Update(double now, bool hovered, int cursorX, int cursorY)
    {
        if (!hovered)
        {
            since = double.NaN;
            return false;
        }
        if (double.IsNaN(since) || Math.Abs(cursorX - x) > tolerance || Math.Abs(cursorY - y) > tolerance)
        {
            (since, x, y) = (now, cursorX, cursorY);
            return false;
        }
        if (now - since < hold || now < readyAt) return false;
        readyAt = now + cooldown;
        since = now;
        return true;
    }
}
