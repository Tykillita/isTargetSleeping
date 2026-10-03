namespace IsTargetSleeping;

/// Las «luces» del logo de Windows, panel a panel (0 arriba a la izquierda, 1 arriba a la
/// derecha, 2 abajo a la izquierda, 3 abajo a la derecha): color, opacidad de cada panel y
/// un destello en diagonal (`Shine` 0 … 1, o −1 si no hay). Continuas en el tiempo.
public readonly record struct PetGlow(uint Rgb = 0xFFFFFF, double A0 = 0, double A1 = 0, double A2 = 0, double A3 = 0, double Shine = -1)
{
    public PetGlow() : this(Rgb: 0xFFFFFF) { }

    public double this[int pane] => pane switch { 0 => A0, 1 => A1, 2 => A2, _ => A3 };
    public bool Dark => A0 <= 0 && A1 <= 0 && A2 <= 0 && A3 <= 0 && Shine < 0;

    public static PetGlow All(double alpha, uint rgb = 0xFFFFFF, double shine = -1) => new(rgb, alpha, alpha, alpha, alpha, shine);

    public static PetGlow Lerp(PetGlow a, PetGlow b, double k)
    {
        k = Math.Clamp(k, 0, 1);
        double L(double x, double y) => x + (y - x) * k;
        uint C(int shift) => (uint)Math.Round(L(a.Rgb >> shift & 0xFF, b.Rgb >> shift & 0xFF)) << shift;
        return new(C(16) | C(8) | C(0), L(a.A0, b.A0), L(a.A1, b.A1), L(a.A2, b.A2), L(a.A3, b.A3), k < 0.5 ? a.Shine : b.Shine);
    }

    /// Redondeada a lo que se nota (para no repintar si no cambia).
    public PetGlow Quantized() => new(Rgb, Q(A0), Q(A1), Q(A2), Q(A3), Shine < 0 ? -1 : Math.Round(Shine * 48) / 48);
    private static double Q(double a) => Math.Round(Math.Clamp(a, 0, 1) * 64) / 64;
}

public static class PetLights
{
    /// El brillo de «Ollama encendido»: un poco más suave que el de liberar RAM.
    public const double Lit = 0.22;
    private const uint White = 0xFFFFFF, Amber = 0xF5BD57, Pink = 0xFF6FA0;

    /// Con Ollama apagado el logo se queda como lo dibuja Windows; encendido, brilla un
    /// poco y los efectos van encima.
    public static PetGlow For(PetActivity activity, double seconds, bool still)
    {
        double t = Math.Max(0, seconds);
        switch (activity)
        {
            case PetActivity.Drowsy:
                return PetGlow.All(Lit);
            case PetActivity.WakingUp:
            {
                // El brillo llega panel a panel; al final del ciclo se funde y vuelve a empezar.
                if (still) return PetGlow.All(Lit);
                double p = t % 2.0, fade = p < 1.6 ? 1 : 1 - (p - 1.6) / 0.4;
                double Pane(int i) => Lit * Smooth((p - i * 0.3) / 0.3) * fade;
                return new(White, Pane(0), Pane(1), Pane(2), Pane(3));
            }
            case PetActivity.Eating:
                return PetGlow.All(still ? 0.4 : 0.3 + 0.2 * Wave(t, 0.35), Amber);
            case PetActivity.Alert:
            {
                double p = t % 6.0;   // un destello en diagonal cada 6 s
                return PetGlow.All(Lit, White, still || p > 1.2 ? -1 : p / 1.2);
            }
            case PetActivity.Working:
            {
                if (still) return PetGlow.All(Lit);
                // Un panel destella con cada tecla (seis por segundo) y se apaga enseguida.
                int key = (int)Math.Floor(t * 6);
                int pane = (key * 3 + 1) % 4;
                double spike = 0.28 * Math.Exp(-(t - key / 6.0) / 0.09);
                return new(White, Lit + (pane == 0 ? spike : 0), Lit + (pane == 1 ? spike : 0), Lit + (pane == 2 ? spike : 0), Lit + (pane == 3 ? spike : 0));
            }
            case PetActivity.Downloading:
            {
                // Se llena como una barra y se vacía suavemente.
                double p = still ? 1.2 : t % 1.5, level = Math.Min(p, 1.2) / 1.2 * 4, fade = p < 1.2 ? 1 : 1 - (p - 1.2) / 0.3;
                double Pane(int i) => Lit + 0.23 * Math.Clamp(level - i, 0, 1) * fade;
                return new(White, Pane(0), Pane(1), Pane(2), Pane(3));
            }
            case PetActivity.Sweeping:
            {
                double p = t % 1.5;
                return PetGlow.All(Lit, White, still ? -1 : p / 1.5);
            }
            case PetActivity.Yawning:
            {
                // El brillo se va panel a panel y queda el logo de siempre.
                if (still) return new();
                double Pane(int i) => Lit * (1 - Smooth((t - i * 0.4) / 0.4));
                return new(White, Pane(0), Pane(1), Pane(2), Pane(3));
            }
            default:
                return new();
        }
    }

    /// Las luces propias de una reacción, o null si se quedan las de la actividad.
    public static PetGlow? For(PetReaction reaction, double seconds, bool still)
    {
        double t = Math.Max(0, seconds);
        return reaction switch
        {
            PetReaction.Sparkle => PetGlow.All(still ? 0.38 : 0.3 + 0.15 * Wave(t, 0.25)),
            PetReaction.Hearts => PetGlow.All(still ? 0.38 : 0.3 + 0.15 * Wave(t, 0.3), Pink),
            PetReaction.Dizzy => still ? PetGlow.All(Lit) : new(White,
                Lit + 0.2 * Math.Max(0, Math.Sin(2 * Math.PI * (t * 5))), Lit + 0.2 * Math.Max(0, Math.Sin(2 * Math.PI * (t * 5 + 0.37))),
                Lit + 0.2 * Math.Max(0, Math.Sin(2 * Math.PI * (t * 5 + 0.61))), Lit + 0.2 * Math.Max(0, Math.Sin(2 * Math.PI * (t * 5 + 0.83)))),
            PetReaction.Nap => still ? new() : new(White,
                Lit * (1 - Smooth(t / 0.4)), Lit * (1 - Smooth((t - 0.3) / 0.4)), Lit * (1 - Smooth((t - 0.6) / 0.4)), Lit * (1 - Smooth((t - 0.9) / 0.4))),
            _ => null,
        };
    }

    private static double Smooth(double k)
    {
        k = Math.Clamp(k, 0, 1);
        return k * k * (3 - 2 * k);
    }

    private static double Wave(double t, double period) => 0.5 - 0.5 * Math.Cos(2 * Math.PI * t / period);
}

/// Dónde está la mascota en modo paseo, en píxeles físicos. Despierta elige destinos al
/// azar dentro del recorrido: acelera, frena al llegar, y para cambiar de sentido se para
/// y se da la vuelta. Cuando se duerme vuelve a casa (Inicio); si trabaja o la miras, se
/// queda quieta. El azar tiene semilla: con la misma, el mismo camino.
public sealed class PetWalker(int seed = 7)
{
    public const double TurnTime = 0.25;
    private readonly Random random = new(seed);
    private double? target;
    private double restUntil, turnUntil = double.NegativeInfinity;

    public double X { get; private set; } = double.NaN;
    public double Speed { get; private set; }
    /// Lo recorrido en total: el paso de los pies va con esto, no con el tiempo.
    public double Distance { get; private set; }
    public bool Walking => Speed > 0.5;
    public bool Turning { get; private set; }
    /// Hacia dónde mira o camina: −1 izquierda, 1 derecha.
    public int Facing { get; private set; } = 1;
    /// Se inclina al acelerar (hacia delante) y al frenar (hacia atrás): −1 … 1.
    public double Lean { get; private set; }

    public enum Mode { Roam, GoHome, Stay }

    /// `speed` en píxeles por segundo; `min`/`max`: el recorrido; `home`: junto a Inicio.
    public void Update(double now, double dt, int min, int max, int home, Mode mode, double speed)
    {
        if (max < min) max = min;
        home = Math.Clamp(home, min, max);
        if (double.IsNaN(X)) X = home;
        X = Math.Clamp(X, min, max);
        dt = Math.Max(0, dt);
        switch (mode)
        {
            case Mode.Stay: target = null; break;
            case Mode.GoHome: target = home; break;
            default:
                if (target is null && now >= restUntil && Speed == 0)
                {
                    // Un destino a una distancia apreciable, dentro del recorrido.
                    double span = max - min;
                    if (span >= 8)
                    {
                        double next = min + random.NextDouble() * span;
                        if (Math.Abs(next - X) < span * 0.15) next = X > (min + max) / 2.0 ? min + span * 0.1 : max - span * 0.1;
                        target = next;
                    }
                }
                break;
        }

        double accel = speed / 0.35;   // de parada a toda velocidad en 0,35 s
        double before = Speed;
        if (Turning && now >= turnUntil) { Turning = false; Facing = -Facing; }
        if (target is not { } goal || Turning)
        {
            Speed = Math.Max(0, Speed - accel * dt);   // frena
        }
        else
        {
            double gap = goal - X;
            int direction = Math.Sign(gap);
            if (direction != 0 && direction != Facing)
            {
                // Para dar la vuelta: frena, y ya parada se gira.
                Speed = Math.Max(0, Speed - accel * dt);
                if (Speed == 0) { Turning = true; turnUntil = now + TurnTime; }
            }
            else
            {
                double room = Math.Sqrt(2 * accel * Math.Abs(gap));   // frena a tiempo para llegar parada
                Speed = Math.Min(Math.Min(speed, room), Speed + accel * dt);
                if (Math.Abs(gap) <= Math.Max(0.05, Speed * dt))   // solo si el paso siguiente se pasaría
                {
                    Distance += Math.Abs(gap);
                    X = goal;
                    Speed = 0;
                    target = null;
                    if (mode == Mode.Roam) restUntil = now + 2 + random.NextDouble() * 5;   // se para a mirar
                    Lean = 0;
                    return;
                }
            }
        }
        double step = Speed * dt;
        X = Math.Clamp(X + Facing * step, min, max);
        Distance += step;
        double change = dt > 0 ? (Speed - before) / dt / accel : 0;
        Lean = Math.Clamp(change, -1, 1) * 0.6 * Facing;
    }
}
