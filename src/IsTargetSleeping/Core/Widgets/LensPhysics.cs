namespace IsTargetSleeping;

/// El movimiento «líquido» de la lente de cristal del dock (como la de la barra de pestañas de iOS 26): un muelle con un poco de
/// rebote que persigue al puntero mientras se arrastra, y otro que la agranda al pulsar. Pura aritmética, sin WPF, para poder probarla.
/// X va en las mismas unidades que las posiciones de los botones (DIP); `Grow` va de 0 (reposo) a 1 (pulsada).
public sealed class LensPhysics
{
    /// Rigidez y amortiguación (1 = sin rebote): la posición rebasa su destino ~10 % y se asienta en ~0,35 s.
    public const double Stiffness = 360, Damping = 0.58, GrowStiffness = 520, GrowDamping = 0.7;
    /// Velocidad (DIP/s) a la que la lente alcanza su estiramiento máximo.
    public const double FullSpeed = 900;

    public double X, VelocityX, TargetX;
    public double Grow, VelocityGrow, TargetGrow;

    /// 0 (quieta) a 1 (a toda velocidad): cuánto se estira la lente en el sentido del movimiento.
    public double Stretch => Math.Min(Math.Abs(VelocityX) / FullSpeed, 1);

    public bool Settled =>
        Math.Abs(TargetX - X) < 0.05 && Math.Abs(VelocityX) < 2 && Math.Abs(TargetGrow - Grow) < 0.002 && Math.Abs(VelocityGrow) < 0.05;

    /// Avanza `seconds` segundos. Se trocea en pasos cortos para que el muelle sea estable aunque un fotograma tarde.
    public void Step(double seconds)
    {
        seconds = Math.Clamp(seconds, 0, 1.0 / 20);
        int steps = Math.Max(1, (int)Math.Ceiling(seconds / 0.004));
        double h = seconds / steps;
        for (int i = 0; i < steps; i++)
        {
            Spring(ref X, ref VelocityX, TargetX, Stiffness, Damping, h);
            Spring(ref Grow, ref VelocityGrow, TargetGrow, GrowStiffness, GrowDamping, h);
        }
    }

    private static void Spring(ref double x, ref double v, double target, double stiffness, double damping, double h)
    {
        double c = 2 * damping * Math.Sqrt(stiffness);
        v += (stiffness * (target - x) - c * v) * h;
        x += v * h;
    }

    /// Salta ya a donde iba, sin animar.
    public void Snap()
    {
        (X, VelocityX, Grow, VelocityGrow) = (TargetX, 0, TargetGrow, 0);
    }

    /// El botón cuyo centro está más cerca de `x` (-1 si no hay botones).
    public static int Nearest(IReadOnlyList<double> centers, double x)
    {
        int best = -1;
        for (int i = 0; i < centers.Count; i++)
            if (best < 0 || Math.Abs(centers[i] - x) < Math.Abs(centers[best] - x)) best = i;
        return best;
    }

    /// Hasta dónde sigue la lente al puntero: de un extremo al otro de los botones, con un poco de holgura (`rubber`) más allá.
    public static double Follow(IReadOnlyList<double> centers, double x, double rubber) =>
        centers.Count == 0 ? x : Math.Clamp(x, centers[0] - rubber, centers[^1] + rubber);
}
