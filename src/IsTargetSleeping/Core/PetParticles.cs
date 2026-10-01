namespace IsTargetSleeping;

public enum ParticleKind { Z, Heart, Sparkle, Star, Dust, Sweat, Tear }

/// Dónde salen las partículas en la rejilla de cada especie: encima de la cabeza (las Z),
/// el centro del cuerpo, el ojo y el suelo.
public readonly record struct PetAnchors(double HeadX, double HeadY, double CenterX, double CenterY, double EyeX, double EyeY, double GroundY);

/// Una partícula, en píxeles de rejilla (con decimales) y segundos.
public record struct Particle(ParticleKind Kind, double X, double Y, double Vx, double Vy, double Age, double Life, double Phase)
{
    /// Cuánto se ve: aparece y se desvanece al final de su vida.
    public readonly double Alpha => Kind switch
    {
        ParticleKind.Sparkle => Math.Sin(Math.PI * Math.Clamp(Age / Life, 0, 1)),
        _ => Math.Clamp(Age / 0.15, 0, 1) * Math.Clamp((Life - Age) / Math.Min(0.6, Life * 0.5), 0, 1),
    };

    /// Su tamaño: las Z crecen al subir; los destellos aparecen y desaparecen encogiendo.
    public readonly double Scale => Kind switch
    {
        ParticleKind.Z => 0.7 + 0.6 * Math.Clamp(Age / Life, 0, 1),
        ParticleKind.Sparkle => Math.Sin(Math.PI * Math.Clamp(Age / Life, 0, 1)),
        _ => 1,
    };
}

/// Las partículas de la mascota (Z, corazones, destellos, estrellas, polvo, sudor y
/// lágrimas), con su física sencilla. Las actividades las sueltan poco a poco y las
/// reacciones en ráfagas. El azar tiene semilla.
public sealed class PetParticles(PetAnchors anchors, int seed = 5)
{
    private readonly Random random = new(seed);
    private readonly List<Particle> live = [];
    private readonly Dictionary<ParticleKind, double> due = [];

    public IReadOnlyList<Particle> Live => live;
    public PetAnchors Anchors => anchors;

    public void Clear()
    {
        live.Clear();
        due.Clear();
    }

    public void Emit(ParticleKind kind, int count = 1)
    {
        for (int i = 0; i < count; i++) live.Add(Spawn(kind));
    }

    /// Suelta una partícula de este tipo cada `every` segundos mientras se llame.
    public void Drip(ParticleKind kind, double every, double now)
    {
        if (!due.TryGetValue(kind, out var at) || now >= at + every * 3) at = now;   // primera vez, o tras una pausa
        if (now < at) return;
        Emit(kind);
        due[kind] = at + every;
    }

    public void Update(double dt)
    {
        dt = Math.Clamp(dt, 0, 0.1);
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var p = live[i];
            p.Age += dt;
            if (p.Age >= p.Life) { live.RemoveAt(i); continue; }
            switch (p.Kind)
            {
                case ParticleKind.Z:
                case ParticleKind.Heart:
                    // Suben ondulando.
                    p.X += (p.Vx + Math.Cos(p.Phase + p.Age * 4) * 1.2) * dt;
                    p.Y += p.Vy * dt;
                    break;
                case ParticleKind.Star:
                    // Orbitan sobre la cabeza.
                    double angle = p.Phase + p.Age * 5;
                    p.X = anchors.HeadX + 7 * Math.Cos(angle);
                    p.Y = anchors.HeadY - 1 + 2 * Math.Sin(angle);
                    break;
                case ParticleKind.Sparkle:
                    break;
                default:
                    // Polvo, sudor y lágrimas: caen.
                    p.Vy += 18 * dt;
                    p.X += p.Vx * dt;
                    p.Y += p.Vy * dt;
                    break;
            }
            live[i] = p;
        }
    }

    private Particle Spawn(ParticleKind kind)
    {
        double R(double a, double b) => a + random.NextDouble() * (b - a);
        var a = anchors;
        return kind switch
        {
            ParticleKind.Z => new(kind, a.HeadX + 5, a.HeadY + 1, R(0.6, 1.2), R(-2.6, -2.0), 0, 2.6, R(0, 6.28)),
            ParticleKind.Heart => new(kind, a.CenterX + R(-7, 7), a.HeadY + R(0, 3), R(-0.5, 0.5), R(-4.5, -3.2), 0, 1.2, R(0, 6.28)),
            ParticleKind.Sparkle => new(kind, a.CenterX + R(-11, 11), a.CenterY + R(-11, 3), 0, 0, 0, R(0.45, 0.7), 0),
            ParticleKind.Star => new(kind, a.HeadX, a.HeadY, 0, 0, 0, 1.5, live.Count(p => p.Kind == ParticleKind.Star) * 2.09),
            ParticleKind.Dust => new(kind, a.CenterX + R(-10, 10), a.GroundY - 0.5, R(-4, 4), R(-5, -2.5), 0, R(0.4, 0.7), 0),
            ParticleKind.Sweat => new(kind, a.CenterX + 7.5, a.HeadY + 3, 0.6, 0.5, 0, 0.9, 0),
            _ => new(kind, a.EyeX - 2.5, a.EyeY + 1.5, R(-0.4, 0.2), 1, 0, 0.8, 0),
        };
    }
}
