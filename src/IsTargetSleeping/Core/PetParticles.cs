namespace IsTargetSleeping;

/// Las comunes (Z, corazones, destellos, estrellas, polvo, sudor, lágrimas) y las de cada
/// carácter: vapor y burbujas del baño, hojas que caen, notas al tararear, el «~» del
/// ronroneo, el resoplido, los bits de Mira y las señales de su antena.
public enum ParticleKind { Z, Heart, Sparkle, Star, Dust, Sweat, Tear, Steam, Bubble, Leaf, Note, Purr, Puff, Bit, Signal }

/// Dónde salen las partículas en la rejilla de cada especie: encima de la cabeza (las Z),
/// el centro del cuerpo, el ojo, el suelo y la boca (resoplidos y notas; sin ella, bajo el ojo).
public readonly record struct PetAnchors(double HeadX, double HeadY, double CenterX, double CenterY, double EyeX, double EyeY, double GroundY,
    double MouthX = double.NaN, double MouthY = double.NaN)
{
    public (double X, double Y) Mouth => (double.IsNaN(MouthX) ? EyeX : MouthX, double.IsNaN(MouthY) ? EyeY + 3 : MouthY);
}

/// Una partícula, en píxeles de rejilla (con decimales) y segundos.
public record struct Particle(ParticleKind Kind, double X, double Y, double Vx, double Vy, double Age, double Life, double Phase)
{
    private readonly double K => Math.Clamp(Age / Life, 0, 1);

    /// Cuánto se ve: aparece y se desvanece al final de su vida.
    public readonly double Alpha => Kind switch
    {
        ParticleKind.Sparkle => Math.Sin(Math.PI * K),
        ParticleKind.Steam => 0.75 * Math.Sin(Math.PI * K),
        ParticleKind.Bubble => K < 0.85 ? Math.Clamp(Age / 0.15, 0, 1) : 1 - (K - 0.85) / 0.15,   // se revienta al final
        ParticleKind.Signal or ParticleKind.Puff => 1 - K * K,
        _ => Math.Clamp(Age / 0.15, 0, 1) * Math.Clamp((Life - Age) / Math.Min(0.6, Life * 0.5), 0, 1),
    };

    /// Su tamaño: las Z crecen al subir; los destellos aparecen y desaparecen encogiendo; el
    /// vapor, el resoplido y las señales se hinchan; la burbuja crece un poco al reventar.
    public readonly double Scale => Kind switch
    {
        ParticleKind.Z => 0.7 + 0.6 * K,
        ParticleKind.Sparkle => Math.Sin(Math.PI * K),
        ParticleKind.Steam => 1 + 0.9 * K,
        ParticleKind.Puff => 0.8 + 0.9 * Math.Sqrt(K),
        ParticleKind.Signal => 0.6 + 1.2 * K,
        ParticleKind.Bubble => K < 0.85 ? 1 : 1.6,
        _ => 1,
    };
}

/// Las partículas de la mascota, con su física sencilla. Las actividades y los gestos las
/// sueltan poco a poco y las reacciones en ráfagas; qué suelta cada una lo decide su perfil.
/// El azar tiene semilla.
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
                case ParticleKind.Bubble:
                    // Suben ondulando.
                    p.X += (p.Vx + Math.Cos(p.Phase + p.Age * 4) * 1.2) * dt;
                    p.Y += p.Vy * dt;
                    break;
                case ParticleKind.Steam:
                    // Sube despacio, ondulando más cuanto más arriba.
                    p.X += (p.Vx + Math.Sin(p.Phase + p.Age * 3) * (0.8 + p.Age)) * dt;
                    p.Y += p.Vy * dt;
                    break;
                case ParticleKind.Note:
                    // Sube balanceándose de lado a lado.
                    p.X += (p.Vx + Math.Sin(p.Phase + p.Age * 5) * 3) * dt;
                    p.Y += p.Vy * dt;
                    break;
                case ParticleKind.Leaf:
                    // Cae meciéndose, sin acelerar (la frena el aire).
                    p.X += Math.Cos(p.Phase + p.Age * 3) * 3 * dt;
                    p.Y += p.Vy * dt;
                    break;
                case ParticleKind.Purr:
                    // Vibra junto al cuerpo y se aleja un poco.
                    p.X += p.Vx * dt + Math.Sin(p.Age * 70) * 0.06;
                    p.Y += p.Vy * dt;
                    break;
                case ParticleKind.Puff:
                    // Sale disparada de la boca y se frena enseguida.
                    p.X += p.Vx * dt;
                    p.Y += p.Vy * dt;
                    p.Vx *= Math.Exp(-5 * dt);
                    p.Vy *= Math.Exp(-5 * dt);
                    break;
                case ParticleKind.Bit:
                    p.Y += p.Vy * dt;
                    break;
                case ParticleKind.Star:
                    // Orbitan sobre la cabeza.
                    double angle = p.Phase + p.Age * 5;
                    p.X = anchors.HeadX + 7 * Math.Cos(angle);
                    p.Y = anchors.HeadY - 1 + 2 * Math.Sin(angle);
                    break;
                case ParticleKind.Sparkle:
                case ParticleKind.Signal:
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
        var (mx, my) = a.Mouth;
        return kind switch
        {
            ParticleKind.Z => new(kind, a.HeadX + 5, a.HeadY + 1, R(0.6, 1.2), R(-2.6, -2.0), 0, 2.6, R(0, 6.28)),
            ParticleKind.Heart => new(kind, a.CenterX + R(-7, 7), a.HeadY + R(0, 3), R(-0.5, 0.5), R(-4.5, -3.2), 0, 1.2, R(0, 6.28)),
            ParticleKind.Sparkle => new(kind, a.CenterX + R(-11, 11), a.CenterY + R(-11, 3), 0, 0, 0, R(0.45, 0.7), 0),
            ParticleKind.Star => new(kind, a.HeadX, a.HeadY, 0, 0, 0, 1.5, live.Count(p => p.Kind == ParticleKind.Star) * 2.09),
            ParticleKind.Dust => new(kind, a.CenterX + R(-10, 10), a.GroundY - 0.5, R(-4, 4), R(-5, -2.5), 0, R(0.4, 0.7), 0),
            ParticleKind.Sweat => new(kind, a.CenterX + 7.5, a.HeadY + 3, 0.6, 0.5, 0, 0.9, 0),
            ParticleKind.Steam => new(kind, a.CenterX + R(-9, 9), a.CenterY + R(-1, 2), R(-0.3, 0.3), R(-2.6, -1.8), 0, R(1.6, 2.2), R(0, 6.28)),
            ParticleKind.Bubble => new(kind, a.CenterX + R(-8, 8), a.CenterY + R(1, 3), R(-0.3, 0.3), R(-3.4, -2.4), 0, R(0.8, 1.2), R(0, 6.28)),
            ParticleKind.Leaf => new(kind, a.CenterX + R(-10, 8), R(0.5, 2.5), 0, R(2.2, 3), 0, R(2.4, 3), R(0, 6.28)),
            ParticleKind.Note => new(kind, mx + R(1, 3), my - R(1, 2), R(0.5, 1.5), R(-3.6, -2.8), 0, 1.8, R(0, 6.28)),
            ParticleKind.Purr => new(kind, a.CenterX + (random.Next(2) == 0 ? -1 : 1) * R(6, 8), a.CenterY + R(-2, 2), R(-0.6, 0.6), R(-1.2, -0.4), 0, 0.8, 0),
            ParticleKind.Puff => new(kind, mx + 3, my, R(5, 7), R(-1.5, -0.5), 0, 0.7, 0),
            ParticleKind.Bit => new(kind, a.CenterX + R(-8, 8), a.CenterY - R(4, 7), 0, R(-4, -3), 0, R(0.8, 1.1), random.Next(2)),
            ParticleKind.Signal => new(kind, a.HeadX, a.HeadY, 0, 0, 0, 0.6, 0),
            _ => new(kind, a.EyeX - 2.5, a.EyeY + 1.5, R(-0.4, 0.2), 1, 0, 0.8, 0),
        };
    }
}
