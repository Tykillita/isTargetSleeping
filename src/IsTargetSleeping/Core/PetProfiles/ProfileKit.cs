namespace IsTargetSleeping;

/// Lo común a los perfiles: construir clips con claves, guardarlos una vez, las
/// transiciones genéricas (accesorios, cama, esconderse) y las partículas por tablas.
/// Cada mascota llena sus tablas en el constructor y llama a `Complete()` al final: lo
/// que no defina queda en su pose de reposo.
public abstract class ProfileKit : IPetAnimationProfile
{
    protected static PetKey K(double t, PetPose pose, Ease ease = Ease.InOut) => new(t, pose, ease);
    protected static PetClip Loop(double length, params PetKey[] keys) => new(keys, loop: true, length: length);
    protected static PetClip Once(params PetKey[] keys) => new(keys, loop: false);
    protected static PetDrip[] D(params (ParticleKind Kind, double Every)[] drips) => drips.Select(d => new PetDrip(d.Kind, d.Every)).ToArray();
    private static readonly PetDrip[] Nothing = [];
    private static readonly (ParticleKind, int)[] NoBurst = [];

    protected readonly Dictionary<PetActivity, PetClip> Activities = [];
    protected readonly Dictionary<PetReaction, PetClip> Reactions = [];
    protected readonly Dictionary<PetGesture, PetClip> Gestures = [];
    protected readonly Dictionary<PetActivity, PetDrip[]> ActivityDrips = [];
    protected readonly Dictionary<PetReaction, PetDrip[]> ReactionDrips = [];
    protected readonly Dictionary<PetGesture, PetDrip[]> GestureDrips = [];
    protected readonly Dictionary<PetReaction, (ParticleKind, int)[]> Bursts = [];
    protected (PetGesture, int)[] AwakeGestures = [], DrowsyGestures = [];

    /// De pie, tranquila. Y dormida en su cama (el final del bostezo).
    protected abstract PetPose Rest { get; }
    protected abstract PetPose Asleep { get; }
    /// A medio camino al entrar o salir de detrás del logo.
    protected virtual PetPose Duck => Rest with { Sit = 0.4, Squash = 0.4, EarL = -0.4, EarR = -0.4 };

    public abstract double WalkSpeed { get; }
    public abstract double MinWait { get; }
    public abstract double MaxWait { get; }
    public virtual double LookSpeed => 16;
    public virtual bool HidesBehindLogo => true;
    public virtual bool PouncesOnCursor => false;
    public PetClip Peek { get; protected set; } = PetClip.Hold(new());
    public PetPose PeekStill { get; protected set; } = new();

    protected void Complete()
    {
        foreach (var a in Enum.GetValues<PetActivity>()) Activities.TryAdd(a, PetClip.Hold(Rest));
        foreach (var r in Enum.GetValues<PetReaction>()) Reactions.TryAdd(r, PetClip.Hold(Rest));
        foreach (var g in Enum.GetValues<PetGesture>()) Gestures.TryAdd(g, PetClip.Hold(Rest));
    }

    public PetClip For(PetActivity activity) => Activities[activity];
    public PetClip For(PetReaction reaction) => Reactions[reaction];
    public PetClip Gesture(PetGesture gesture) => Gestures[gesture];
    public double ReactionDuration(PetReaction reaction) => reaction == PetReaction.None ? 0 : For(reaction).Length;
    public IReadOnlyList<(PetGesture Gesture, int Weight)> IdleGestures(bool drowsy) => drowsy ? DrowsyGestures : AwakeGestures;
    public IReadOnlyList<PetDrip> Drips(PetActivity activity) => ActivityDrips.GetValueOrDefault(activity, Nothing);
    public IReadOnlyList<PetDrip> Drips(PetReaction reaction) => ReactionDrips.GetValueOrDefault(reaction, Nothing);
    public IReadOnlyList<PetDrip> GestureParticles(PetGesture gesture) => GestureDrips.GetValueOrDefault(gesture, Nothing);
    public IReadOnlyList<(ParticleKind Kind, int Count)> Burst(PetReaction reaction) => Bursts.GetValueOrDefault(reaction, NoBurst);

    /// «Movimiento reducido»: una pose fija que se entienda sola.
    public virtual PetPose Still(PetActivity activity) => activity switch
    {
        PetActivity.DeepSleep or PetActivity.Yawning => Asleep,
        PetActivity.Hidden => Rest,
        _ => For(activity).At(0),
    };
    public virtual PetPose Still(PetReaction reaction) => reaction switch
    {
        PetReaction.None => Rest,
        PetReaction.Nap => Asleep,
        _ => For(reaction).At(ReactionDuration(reaction) * 0.4),
    };

    public virtual PetPose Hover(PetPose pose, double hoverX, double hoverY, double seconds) => pose;
    public abstract PetPose Walk(double distance, int facing, double lean, bool turning);
    public virtual (double Dx, double Dy) Motion(PetActivity activity, double seconds) => (0, 0);
    public virtual (double Dx, double Dy) Motion(PetReaction reaction, double seconds) => (0, 0);
    public virtual (double Dx, double Dy) Motion(PetGesture gesture, double seconds) => (0, 0);
    public abstract double Breath(PetActivity activity, double seconds);

    /// Esconderse o salir agachándose; meterse en la cama o salir de ella; guardar un
    /// accesorio y sacar otro; y si no, una mezcla corta.
    public virtual PetClip? Transition(PetActivity from, PetActivity to, bool fromHiding, bool toHiding)
    {
        if (fromHiding != toHiding)
            return Once(K(0, fromHiding ? PeekStill : Still(from)), K(0.35, Duck), K(0.7, toHiding ? PeekStill : For(to).At(0)));
        if (from == to) return null;
        var before = Still(from);
        var after = For(to).At(0);
        if (to == PetActivity.DeepSleep && from != PetActivity.Yawning)
            return Once(K(0, before), K(0.5, PetPose.Lerp(Rest, Asleep, 0.5) with { Eye = 0.4, Tint = PetTint.Normal }), K(1.2, Asleep));
        if (from == PetActivity.DeepSleep)
            return Once(K(0, Asleep), K(0.4, Asleep with { Eye = 0.3 }),
                K(0.9, PetPose.Lerp(Asleep, after, 0.6) with { Tint = PetTint.Normal }), K(1.3, after));
        // El cuerpo va de una pose a otra durante toda la transición; el accesorio sale y entra.
        if (before.Prop != after.Prop)
            return Once(K(0, before), K(0.35, PetPose.Lerp(before, after, 0.4) with { Prop = before.Prop, PropIn = 0 }),
                K(0.45, PetPose.Lerp(before, after, 0.6) with { Prop = after.Prop, PropIn = 0 }), K(0.8, after));
        return Once(K(0, before), K(0.5, after));
    }

    /// En el aire entre `start` y `end` (en segundos): una parábola de `height` píxeles de rejilla.
    protected static double Arc(double t, double start, double end, double height)
    {
        if (t <= start || t >= end) return 0;
        double p = (t - start) / (end - start);
        return -height * 4 * p * (1 - p);
    }
}
