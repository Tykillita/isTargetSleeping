namespace IsTargetSleeping;

/// Lo que pasa en este instante, visto desde la mascota.
/// `Hiding`: escondida tras el logo. `HoverX`/`HoverY`: dónde está el ratón sobre ella (−1 … 1).
public readonly record struct PetSituation(
    PetActivity Activity, PetReaction Reaction = PetReaction.None, double ReactionSince = 0,
    PetPressure Pressure = PetPressure.Normal, bool Still = false, bool Hiding = false,
    bool Walking = false, double WalkDistance = 0, int Facing = 1, double Lean = 0, bool Turning = false,
    bool Hovered = false, double HoverX = 0, double HoverY = 0);

/// Un fotograma: la pose, cuánto se desplaza toda la mascota (en píxeles de rejilla), las
/// luces del logo y cada cuánto hace falta el siguiente (0: no hace falta).
public readonly record struct PetFrame(PetPose Pose, double Dx, double Dy, PetGlow Glow, double Fps);

/// Junta las capas en una pose por fotograma: la actividad (con su transición al
/// cambiar), los gestos sueltos, las reacciones, cómo trata al ratón cada carácter y
/// mezclas cortas entre todo ello para que nada salte. Encima, muelles (el ojo que sigue
/// al ratón, a la velocidad de cada una; la antena que se queda atrás), las partículas
/// que decide su perfil y las luces del logo con fundidos.
public sealed class PetDirector
{
    private readonly IPetAnimationProfile animations;
    public const double BlendTime = 0.25, HoverBlend = 0.2;
    public PetIdle Idle { get; }
    public PetParticles Particles { get; }

    private (PetActivity Activity, bool Hiding, bool Walking, bool Still)? shown;
    private PetClip? transition;
    private double changedAt = double.NegativeInfinity, blendStart = double.NegativeInfinity;
    private PetPose last = new(), blendFrom = new();
    private (PetActivity, PetReaction)? glowKey;
    private PetGlow lastGlow, glowFrom;
    private double glowStart = double.NegativeInfinity;
    private double lookX, lookY, lookVx, lookVy, antenna, antennaV, lastDx, lastDy;
    private (PetReaction, double)? burst;
    private double hoverK, hoverSince = double.NaN, hoverSeconds;

    public PetDirector(PetAnchors anchors, int seed = 3, IPetAnimationProfile? animations = null)
    {
        this.animations = animations ?? PetProfiles.Mira;
        Idle = new PetIdle(seed + 1, this.animations);
        Particles = new PetParticles(anchors, seed + 2);
    }

    public PetFrame Step(double now, double dt, PetSituation s)
    {
        dt = Math.Clamp(dt, 0, 0.1);
        var activity = s.Activity;

        // Al cambiar de actividad (o esconderse, o echar a andar): su transición, y una mezcla corta.
        var key = (activity, s.Hiding, s.Walking, s.Still);
        if (shown != key)
        {
            transition = s.Still || shown is not { } before ? null
                : animations.Transition(before.Activity, activity, before.Hiding, s.Hiding);
            changedAt = now;
            blendFrom = last;
            blendStart = shown is null || s.Still ? double.NegativeInfinity : now;
            shown = key;
        }
        double inTransition = now - changedAt;
        bool transiting = transition is not null && inTransition < transition.Length;
        double loopTime = inTransition - (transition?.Length ?? 0);

        PetPose pose;
        if (s.Still) pose = s.Hiding ? animations.PeekStill : animations.Still(activity);
        else if (transiting) pose = transition!.At(inTransition);
        else if (s.Walking) pose = animations.Walk(s.WalkDistance, s.Facing, s.Lean, s.Turning);
        else pose = s.Hiding ? animations.Peek.At(loopTime) : animations.For(activity).At(loopTime);

        // Gestos sueltos, solo quieta y tranquila.
        Idle.Update(now, !s.Still && !transiting && s.Reaction == PetReaction.None && !s.Walking && !s.Hovered && !s.Hiding
            && activity is PetActivity.Alert or PetActivity.Drowsy, activity == PetActivity.Drowsy);
        double gestureWeight = Idle.Weight(now);
        if (gestureWeight > 0) pose = PetPose.Lerp(pose, animations.Gesture(Idle.Current).At(now - Idle.Since), gestureWeight);

        // Reacciones: entran y salen mezclándose.
        double sinceReaction = now - s.ReactionSince;
        if (s.Reaction != PetReaction.None)
        {
            var reacting = s.Still ? animations.Still(s.Reaction) : animations.For(s.Reaction).At(sinceReaction);
            double length = animations.ReactionDuration(s.Reaction);
            double weight = s.Still ? 1 : Math.Clamp(Math.Min(sinceReaction / 0.1, (length - sinceReaction) / 0.15), 0, 1);
            pose = PetPose.Lerp(pose, reacting, weight);
        }

        if (!s.Still && now - blendStart < BlendTime) pose = PetPose.Lerp(blendFrom, pose, Easing.Apply(Ease.InOut, (now - blendStart) / BlendTime));

        // El ratón encima: cada una reacciona a su manera (entra y sale mezclándose).
        bool hover = s.Hovered && !s.Still && s.Reaction == PetReaction.None && !s.Hiding && !s.Walking
            && activity is not (PetActivity.DeepSleep or PetActivity.Yawning or PetActivity.Hidden);
        if (!s.Hovered || s.Still) hoverSince = double.NaN;
        else if (hover && double.IsNaN(hoverSince)) hoverSince = now;
        if (!double.IsNaN(hoverSince)) hoverSeconds = now - hoverSince;
        hoverK = s.Still ? 0 : Math.Clamp(hoverK + (hover ? dt : -dt) / HoverBlend, 0, 1);
        if (hoverK > 0)
            pose = PetPose.Lerp(pose, animations.Hover(pose, Math.Clamp(s.HoverX, -1, 1), Math.Clamp(s.HoverY, -1, 1), hoverSeconds),
                Easing.Apply(Ease.InOut, hoverK));

        // Respira, mira al ratón (con un muelle: sin saltos) y abre un ojo si la despiertas.
        if (!s.Still) pose = pose with { Squash = pose.Squash + animations.Breath(activity, now) };
        double wantX = pose.LookX, wantY = pose.LookY;
        if (s.Hovered && s.Reaction == PetReaction.None) (wantX, wantY) = (Math.Clamp(s.HoverX, -1, 1), Math.Clamp(s.HoverY, -1, 1));
        if (s.Still) (lookX, lookY) = (wantX, wantY);
        else
        {
            Spring(ref lookX, ref lookVx, wantX, animations.LookSpeed, 1, dt);
            Spring(ref lookY, ref lookVy, wantY, animations.LookSpeed, 1, dt);
        }
        pose = pose with { LookX = lookX, LookY = lookY };
        if (s.Hovered && activity == PetActivity.DeepSleep) pose = pose with { Eye = Math.Max(pose.Eye, 0.45) };
        if (s.Pressure == PetPressure.Critical && pose.Tint == PetTint.Normal) pose = pose with { Tint = PetTint.Danger };

        // Toda la mascota se mueve (saltos, botes, balanceos).
        var (dx, dy) = s.Still ? (0.0, 0.0) : animations.Motion(activity, loopTime);
        if (!s.Still && s.Reaction != PetReaction.None)
        {
            var r = animations.Motion(s.Reaction, sinceReaction);
            (dx, dy) = (dx + r.Dx, dy + r.Dy);
        }
        if (gestureWeight > 0)
        {
            var g = animations.Motion(Idle.Current, now - Idle.Since);
            (dx, dy) = (dx + g.Dx, dy + g.Dy);
        }

        // La antena se queda atrás al moverse y vuelve oscilando.
        if (!s.Still && dt > 0)
        {
            double push = -((dx - lastDx) / dt) * 0.08 - s.Lean * 0.8 + ((dy - lastDy) / dt) * 0.03;
            Spring(ref antenna, ref antennaV, Math.Clamp(push, -1.2, 1.2), 14, 0.3, dt);
        }
        (lastDx, lastDy) = (dx, dy);
        pose = pose with { Antenna = s.Still ? 0 : antenna };

        UpdateParticles(now, dt, s, activity, transiting, gestureWeight > 0);
        var glow = Glow(now, s, activity, loopTime, sinceReaction);

        last = pose;
        bool asleep = activity == PetActivity.DeepSleep && !transiting && s.Reaction == PetReaction.None && !s.Hovered;
        double fps = s.Still ? (s.Reaction != PetReaction.None || Particles.Live.Count > 0 ? 10 : 0) : asleep ? 20 : 60;
        return new(pose, dx, dy, glow, fps);
    }

    /// Un muelle hacia `target`: `omega` lo rápido que llega, `damping` 1 sin pasarse, menos rebota.
    private static void Spring(ref double x, ref double v, double target, double omega, double damping, double dt)
    {
        int steps = Math.Max(1, (int)Math.Ceiling(dt / 0.008));   // pasos pequeños: estable a cualquier fps
        double h = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            double a = omega * omega * (target - x) - 2 * damping * omega * v;
            v += a * h;
            x += v * h;
        }
    }

    /// Lo que suelta cada perfil: durante la actividad, el gesto y la reacción, y una
    /// ráfaga al empezar cada reacción. El sudor por falta de memoria es de todas.
    private void UpdateParticles(double now, double dt, PetSituation s, PetActivity activity, bool transiting, bool gesturing)
    {
        if (s.Still) { Particles.Clear(); return; }
        Particles.Update(dt);
        void Drip(IReadOnlyList<PetDrip> drips)
        {
            foreach (var d in drips) Particles.Drip(d.Kind, d.Every, now);
        }
        if (!transiting && !s.Hiding) Drip(animations.Drips(activity));
        if (s.Reaction == PetReaction.Nap && now - s.ReactionSince > animations.ReactionDuration(PetReaction.Nap) * 0.75)
            Drip(animations.Drips(PetActivity.DeepSleep));
        if (gesturing) Drip(animations.GestureParticles(Idle.Current));
        if (s.Pressure == PetPressure.High && activity != PetActivity.DeepSleep) Particles.Drip(ParticleKind.Sweat, 2.5, now);
        if (s.Reaction == PetReaction.None) { burst = null; return; }
        Drip(animations.Drips(s.Reaction));
        if (burst != (s.Reaction, s.ReactionSince))
        {
            burst = (s.Reaction, s.ReactionSince);
            foreach (var (kind, count) in animations.Burst(s.Reaction)) Particles.Emit(kind, count);
        }
    }

    /// Las luces del logo, y un fundido corto cuando cambian de origen (actividad o reacción).
    private PetGlow Glow(double now, PetSituation s, PetActivity activity, double loopTime, double sinceReaction)
    {
        double reactionTime = s.Reaction == PetReaction.None ? 0 : sinceReaction * PetBrain.Duration(s.Reaction) / animations.ReactionDuration(s.Reaction);
        var own = s.Reaction != PetReaction.None ? PetLights.For(s.Reaction, reactionTime, s.Still) : null;
        var glow = own ?? PetLights.For(activity, loopTime, s.Still);
        var key = (activity, own is null ? PetReaction.None : s.Reaction);
        if (glowKey != key)
        {
            glowFrom = lastGlow;
            glowStart = glowKey is null || s.Still ? double.NegativeInfinity : now;
            glowKey = key;
        }
        if (now - glowStart < BlendTime) glow = PetGlow.Lerp(glowFrom, glow, (now - glowStart) / BlendTime);
        lastGlow = glow;
        return glow;
    }
}
