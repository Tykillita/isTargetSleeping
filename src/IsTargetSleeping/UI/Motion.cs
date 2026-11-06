using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using IsTargetSleeping;

namespace IsTargetSleeping.UI;

/// Movimiento compartido del panel y la lente: cada destino sustituye la animación anterior y respeta movimiento reducido.
public static class Motion
{
    public static readonly TimeSpan PanelDuration = TimeSpan.FromMilliseconds(180);

    /// Mientras el notch se anima (abrir, cerrar, cambiar de largo), el trabajo pesado del hilo de la interfaz espera: leer los datos de los widgets (rendimiento, batería,
    /// audio…) tardaba un centenar de milisegundos y, si caía a mitad de la animación, se saltaba de golpe unos 100 ms de movimiento. Se marca con `Busy` y se consulta con `IsAnimating`.
    private static long animatingUntil;
    public static bool IsAnimating => Environment.TickCount64 < Interlocked.Read(ref animatingUntil);
    public static void Busy(TimeSpan duration) => Interlocked.Exchange(ref animatingUntil, Math.Max(Interlocked.Read(ref animatingUntil), Environment.TickCount64 + (long)duration.TotalMilliseconds));

    // Resortes amortiguados reales (`SpringEase`), con el ritmo medido en el notch de Nusa (ráfaga de unos 90 fotogramas por segundo): abre en poco más de 200 ms y cierra en
    // unos 180. Con un rebote visible: al abrir se pasa un 6-8 % y vuelve; al cerrar se pasa un poco por debajo de la pastilla y vuelve. La forma sigue al tamaño en cada
    // fotograma (con sus esquinas) y la ventana deja margen, así que el rebote no se recorta ni deja el notch cuadrado.
    public static readonly TimeSpan OpenWidthDuration = TimeSpan.FromMilliseconds(460), OpenHeightDuration = TimeSpan.FromMilliseconds(460), CloseDuration = TimeSpan.FromMilliseconds(340);
    public static IEasingFunction OpenWidthEase() => new SpringEase { Damping = 0.7, Frequency = 15, Seconds = 0.46 };
    public static IEasingFunction OpenHeightEase() => new SpringEase { Damping = 0.7, Frequency = 15, Seconds = 0.46 };
    public static IEasingFunction CloseEase() => new SpringEase { Damping = 1.0, Frequency = 17, Seconds = 0.34 };

    /// Cuando la pastilla cerrada cambia de largo (la letra, un aviso): el mismo resorte que al abrir, un poco más lento. Forma y contenido usan la misma curva y duración.
    public static readonly TimeSpan WidthDuration = TimeSpan.FromMilliseconds(320);
    public static IEasingFunction WidthEase() => new SpringEase { Damping = 0.95, Frequency = 24, Seconds = 0.32 };

    /// El mini dock de los puntos (sale al pasar el ratón por ellos): el borde de abajo solo se mueve unos píxeles, así que un resorte corto y casi sin rebote, rápido como el de Nusa.
    public static readonly TimeSpan DockDuration = TimeSpan.FromMilliseconds(200);
    public static IEasingFunction DockEase() => new SpringEase { Damping = 0.9, Frequency = 26, Seconds = 0.2 };

    public static void Spring(ScaleTransform scale, double value, int milliseconds, bool reduced, bool bounce)
    {
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        if (reduced) { scale.ScaleX = scale.ScaleY = value; return; }
        var easing = Ease(bounce);
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(value, duration) { EasingFunction = easing });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(value, duration) { EasingFunction = easing });
    }

    public static IEasingFunction Ease(bool spring = false) => spring
        ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = Math.Clamp((1 - LensPhysics.Damping) * 1.4, .2, .75) }
        : new QuadraticEase { EasingMode = EasingMode.EaseOut };
}

/// Un resorte amortiguado (masa y muelle): `Frequency` es la frecuencia natural (rad/s), `Damping` la razón de amortiguamiento (1 = crítico, sin rebote; menos de 1, rebota)
/// y `Seconds` lo que dura la animación (el tiempo normalizado 0..1 se pasa a segundos). Parte de 0 y se asienta en 1.
public sealed class SpringEase : EasingFunctionBase
{
    public double Damping { get; set; } = 0.9;
    public double Frequency { get; set; } = 28;
    public double Seconds { get; set; } = 0.26;

    /// La curva ya es el resorte entero: sin esto, WPF la aplicaba "hacia fuera" (1 - f(1 - t)) y el notch se hundía antes de abrirse y cerraba de golpe.
    public SpringEase() { EasingMode = EasingMode.EaseIn; }

    protected override double EaseInCore(double normalizedTime)
    {
        if (normalizedTime <= 0) return 0;
        double s = normalizedTime * Seconds, z = Math.Clamp(Damping, 0.05, 1.5), wn = Frequency;
        if (z >= 1) return 1 - Math.Exp(-wn * s) * (1 + wn * s);
        double wd = wn * Math.Sqrt(1 - z * z);
        return 1 - Math.Exp(-z * wn * s) * (Math.Cos(wd * s) + z * wn / wd * Math.Sin(wd * s));
    }

    protected override Freezable CreateInstanceCore() => new SpringEase { Damping = Damping, Frequency = Frequency, Seconds = Seconds };
}
