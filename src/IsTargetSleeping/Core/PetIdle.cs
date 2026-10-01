namespace IsTargetSleeping;

/// Gestos sueltos para que, quieta, no repita siempre el mismo bucle.
/// Los primeros son de todas; los demás, de un carácter: Mira (radar, señal, enfocar), la
/// llama (rumiar, sacudir una oreja, tararear), la capibara (el pajarito, el baño) y el
/// gato (acicalarse, amasar, agitar la cola).
public enum PetGesture
{
    None, Stretch, Scratch, Yawn, LookAround, Wave, Hop, Nod,
    Scan, Ping, Calibrate, Chew, EarFlick, Hum, Bird, Bath, Groom, Knead, TailSwish,
}

/// Cada 6–15 s al azar, un gesto de la lista (con pesos y sin repetir el anterior), solo
/// cuando puede: quieta, despierta o somnolienta, sin reacción en curso ni el ratón encima.
/// El azar tiene semilla: con la misma, la misma secuencia.
public sealed class PetIdle(int seed = 11, IPetAnimationProfile? animations = null)
{
    public const double MinWait = 6, MaxWait = 15, Blend = 0.2;
    private readonly Random random = new(seed);
    private readonly IPetAnimationProfile profile = animations ?? PetProfiles.Mira;
    private double next = double.NaN;
    private PetGesture last;

    public PetGesture Current { get; private set; }
    public double Since { get; private set; }

    /// `allowed`: si ahora puede hacer un gesto. `drowsy`: somnolienta (cabecea en vez de saludar).
    public void Update(double now, bool allowed, bool drowsy)
    {
        if (!allowed)
        {
            Current = PetGesture.None;
            next = now + Wait();   // al volver a estar quieta, vuelve a esperar
            return;
        }
        if (double.IsNaN(next)) next = now + Wait();
        if (Current != PetGesture.None && now - Since >= profile.Gesture(Current).Length)
        {
            Current = PetGesture.None;
            next = now + Wait();
        }
        if (Current == PetGesture.None && now >= next)
        {
            Current = Pick(drowsy);
            Since = now;
            last = Current;
        }
    }

    /// Cuánto pesa el gesto en curso (se mezcla con la actividad al entrar y al salir).
    public double Weight(double now)
    {
        if (Current == PetGesture.None) return 0;
        double t = now - Since, length = profile.Gesture(Current).Length;
        return Math.Clamp(Math.Min(t / Blend, (length - t) / Blend), 0, 1);
    }

    private double Wait() => profile.MinWait + random.NextDouble() * (profile.MaxWait - profile.MinWait);

    private PetGesture Pick(bool drowsy)
    {
        var pool = profile.IdleGestures(drowsy).Where(o => o.Gesture != last && o.Weight > 0).ToArray();
        int roll = random.Next(pool.Sum(o => o.Weight));
        foreach (var (gesture, weight) in pool)
        {
            if (roll < weight) return gesture;
            roll -= weight;
        }
        return pool[^1].Gesture;
    }
}
