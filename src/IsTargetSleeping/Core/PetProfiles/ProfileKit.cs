namespace IsTargetSleeping;

/// Lo común a los perfiles: construir clips con claves, guardarlos una vez, las
/// transiciones genéricas (accesorios, cama, esconderse) y las partículas por tablas.
/// Cada mascota llena sus tablas en el constructor y llama a `Complete()` al final: lo
/// que no defina queda en su pose de reposo, y en la cama, dormida.
/// Las reacciones pueden tener una versión para una actividad concreta (`Reactions` con
/// la actividad en la clave, con sus partículas): en la cama (`InBed`) o el final de una
/// limpieza. Si la hay, sus partículas son solo las suyas. Una actividad (y sus reacciones)
/// puede tener además una versión «tras la cama» (`AfterBed`), para cuando empieza saliendo
/// de ella: la capibara, al limpiar recién salida de la tina, se escurre el agua.
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
    protected readonly Dictionary<(PetReaction, PetActivity), PetClip> ContextReactions = [];
    protected readonly Dictionary<(PetReaction, PetActivity), PetDrip[]> ContextDrips = [];
    protected readonly Dictionary<(PetReaction, PetActivity), (ParticleKind, int)[]> ContextBursts = [];
    protected readonly Dictionary<PetActivity, PetClip> AfterBedActivities = [];
    protected readonly Dictionary<PetActivity, PetDrip[]> AfterBedDrips = [];
    protected readonly Dictionary<(PetReaction, PetActivity), PetClip> AfterBedReactions = [];
    protected readonly Dictionary<(PetReaction, PetActivity), PetDrip[]> AfterBedReactionDrips = [];
    protected readonly Dictionary<(PetReaction, PetActivity), (ParticleKind, int)[]> AfterBedBursts = [];
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

    /// Una reacción dormida, sin salir de la cama (vale también al bostezar camino de ella).
    protected void InBed(PetReaction reaction, PetClip clip, PetDrip[]? drips = null, (ParticleKind, int)[]? burst = null)
    {
        foreach (var bed in new[] { PetActivity.DeepSleep, PetActivity.Yawning })
        {
            ContextReactions[(reaction, bed)] = clip;
            if (drips is not null) ContextDrips[(reaction, bed)] = drips;
            if (burst is not null) ContextBursts[(reaction, bed)] = burst;
        }
    }

    /// La versión de una actividad cuando empieza saliendo de la cama.
    protected void AfterBed(PetActivity activity, PetClip clip, PetDrip[]? drips = null)
    {
        AfterBedActivities[activity] = clip;
        if (drips is not null) AfterBedDrips[activity] = drips;
    }

    /// Una reacción durante esa versión «tras la cama» (el final de su limpieza, por ejemplo).
    protected void AfterBed(PetReaction reaction, PetActivity during, PetClip clip, PetDrip[]? drips = null, (ParticleKind, int)[]? burst = null)
    {
        AfterBedReactions[(reaction, during)] = clip;
        if (drips is not null) AfterBedReactionDrips[(reaction, during)] = drips;
        if (burst is not null) AfterBedBursts[(reaction, during)] = burst;
    }

    protected void Complete()
    {
        foreach (var a in Enum.GetValues<PetActivity>()) Activities.TryAdd(a, PetClip.Hold(Rest));
        foreach (var r in Enum.GetValues<PetReaction>()) Reactions.TryAdd(r, PetClip.Hold(Rest));
        foreach (var g in Enum.GetValues<PetGesture>()) Gestures.TryAdd(g, PetClip.Hold(Rest));
        // En la cama, lo que no tenga versión propia: entreabre un ojo y sigue durmiendo.
        foreach (var r in Enum.GetValues<PetReaction>().Where(r => r != PetReaction.None))
            if (!ContextReactions.ContainsKey((r, PetActivity.DeepSleep)))
                InBed(r, r == PetReaction.Nap ? Once(K(0, Asleep), K(1, Asleep))
                    : Once(K(0, Asleep), K(0.35, Asleep with { Eye = 0.35 }), K(1.1, Asleep with { Eye = 0.35 }), K(1.5, Asleep)));
    }

    public PetClip For(PetActivity activity) => Activities[activity];
    public PetClip For(PetContext activity) =>
        activity.AfterBed && AfterBedActivities.TryGetValue(activity.Activity, out var clip) ? clip : Activities[activity.Activity];
    public PetClip For(PetReaction reaction, PetContext during) => Contextual(reaction, during) ?? Reactions[reaction];
    public PetClip Gesture(PetGesture gesture) => Gestures[gesture];
    public double ReactionDuration(PetReaction reaction, PetContext during) => reaction == PetReaction.None ? 0 : For(reaction, during).Length;
    public IReadOnlyList<(PetGesture Gesture, int Weight)> IdleGestures(bool drowsy) => drowsy ? DrowsyGestures : AwakeGestures;
    public IReadOnlyList<PetDrip> Drips(PetActivity activity) => ActivityDrips.GetValueOrDefault(activity, Nothing);
    public IReadOnlyList<PetDrip> Drips(PetContext activity) =>
        activity.AfterBed && AfterBedDrips.TryGetValue(activity.Activity, out var drips) ? drips : Drips(activity.Activity);
    public IReadOnlyList<PetDrip> Drips(PetReaction reaction, PetContext during) =>
        AfterBedReaction(reaction, during) ? AfterBedReactionDrips.GetValueOrDefault((reaction, during.Activity), Nothing)
        : ContextReactions.ContainsKey((reaction, during.Activity)) ? ContextDrips.GetValueOrDefault((reaction, during.Activity), Nothing)
        : ReactionDrips.GetValueOrDefault(reaction, Nothing);
    public IReadOnlyList<PetDrip> GestureParticles(PetGesture gesture) => GestureDrips.GetValueOrDefault(gesture, Nothing);
    public IReadOnlyList<(ParticleKind Kind, int Count)> Burst(PetReaction reaction, PetContext during) =>
        AfterBedReaction(reaction, during) ? AfterBedBursts.GetValueOrDefault((reaction, during.Activity), NoBurst)
        : ContextReactions.ContainsKey((reaction, during.Activity)) ? ContextBursts.GetValueOrDefault((reaction, during.Activity), NoBurst)
        : Bursts.GetValueOrDefault(reaction, NoBurst);

    private bool AfterBedReaction(PetReaction reaction, PetContext during) =>
        during.AfterBed && AfterBedReactions.ContainsKey((reaction, during.Activity));

    /// La versión propia de la reacción en esa actividad (y tras la cama, si la tiene); null si no hay.
    private PetClip? Contextual(PetReaction reaction, PetContext during) =>
        (during.AfterBed ? AfterBedReactions.GetValueOrDefault((reaction, during.Activity)) : null)
        ?? ContextReactions.GetValueOrDefault((reaction, during.Activity));

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
        _ => Reactions[reaction].At(Reactions[reaction].Length * 0.4),
    };
    public virtual PetPose Still(PetContext activity) => Still(activity.Activity);
    public PetPose Still(PetReaction reaction, PetContext during) =>
        reaction == PetReaction.Nap || Contextual(reaction, during) is not { } clip ? Still(reaction) : clip.At(clip.Length * 0.4);

    public virtual PetPose Hover(PetPose pose, double hoverX, double hoverY, double seconds) => pose;
    public abstract PetPose Walk(double distance, int facing, double lean, bool turning);
    public virtual (double Dx, double Dy) Motion(PetActivity activity, double seconds) => (0, 0);
    public virtual (double Dx, double Dy) Motion(PetContext activity, double seconds) => Motion(activity.Activity, seconds);
    public virtual (double Dx, double Dy) Motion(PetReaction reaction, double seconds) => (0, 0);
    /// En la cama no se mueve de su sitio; en otra actividad con versión propia, lo que diga `ContextMotion`.
    public (double Dx, double Dy) Motion(PetReaction reaction, double seconds, PetContext during) =>
        during.Activity.InBed() ? (0, 0) : Contextual(reaction, during) is not null ? ContextMotion(reaction, seconds, during) : Motion(reaction, seconds);
    protected virtual (double Dx, double Dy) ContextMotion(PetReaction reaction, double seconds, PetContext during) => Motion(reaction, seconds);
    public virtual (double Dx, double Dy) Motion(PetGesture gesture, double seconds) => (0, 0);
    public abstract double Breath(PetActivity activity, double seconds);

    /// La limpieza se ve entera: su entrada y una vuelta (como mucho `PetBrain.CleanHoldMax`).
    public virtual double HoldFor(PetActivity from, PetActivity to) => to == PetActivity.Sweeping
        ? Math.Min(PetBrain.CleanHoldMax, (Transition(from, to, false, false)?.Length ?? 0) + For(new PetContext(to, from)).Length) : 0;

    /// Esconderse o salir agachándose; meterse en la cama o salir de ella; guardar un
    /// accesorio y sacar otro; y si no, una mezcla corta.
    public virtual PetClip? Transition(PetActivity from, PetActivity to, bool fromHiding, bool toHiding)
    {
        if (fromHiding != toHiding)
            return Once(K(0, fromHiding ? PeekStill : Still(from)), K(0.35, Duck), K(0.7, toHiding ? PeekStill : For(to).At(0)));
        if (from == to) return null;
        var before = Still(from);
        var after = For(new PetContext(to, from)).At(0);
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
