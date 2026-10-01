namespace IsTargetSleeping;

/// Una partícula que se suelta cada `Every` segundos mientras dura lo que la provoca.
public readonly record struct PetDrip(ParticleKind Kind, double Every);

/// Personalidad pura, sin ventanas ni dibujo: su coreografía, cómo trata al ratón y qué
/// partículas suelta. Los clips se crean una vez y se reutilizan.
public interface IPetAnimationProfile
{
    double WalkSpeed { get; }
    double MinWait { get; }
    double MaxWait { get; }
    /// Lo rápido que el ojo sigue al ratón (la ω de su muelle).
    double LookSpeed { get; }
    /// Si al esperar un modelo se esconde tras el logo de Windows (si no, `Peek` es su forma de esperar).
    bool HidesBehindLogo { get; }
    /// Si se lanza sobre el cursor cuando se queda quieto encima.
    bool PouncesOnCursor { get; }
    PetClip For(PetActivity activity);
    PetClip For(PetReaction reaction);
    PetClip Gesture(PetGesture gesture);
    PetClip? Transition(PetActivity from, PetActivity to, bool fromHiding, bool toHiding);
    PetClip Peek { get; }
    PetPose PeekStill { get; }
    PetPose Still(PetActivity activity);
    PetPose Still(PetReaction reaction);
    PetPose Walk(double distance, int facing, double lean, bool turning);
    /// Cómo cambia la pose con el ratón encima (`hoverX`/`hoverY`: −1 … 1; `seconds`: desde que llegó).
    PetPose Hover(PetPose pose, double hoverX, double hoverY, double seconds);
    (double Dx, double Dy) Motion(PetActivity activity, double seconds);
    (double Dx, double Dy) Motion(PetReaction reaction, double seconds);
    (double Dx, double Dy) Motion(PetGesture gesture, double seconds);
    double Breath(PetActivity activity, double seconds);
    IReadOnlyList<(PetGesture Gesture, int Weight)> IdleGestures(bool drowsy);
    double ReactionDuration(PetReaction reaction);
    /// Lo que suelta durante una actividad, una reacción o un gesto, y la ráfaga al empezar una reacción.
    IReadOnlyList<PetDrip> Drips(PetActivity activity);
    IReadOnlyList<PetDrip> Drips(PetReaction reaction);
    IReadOnlyList<PetDrip> GestureParticles(PetGesture gesture);
    IReadOnlyList<(ParticleKind Kind, int Count)> Burst(PetReaction reaction);
}

public static class PetProfiles
{
    public static IPetAnimationProfile Mira { get; } = new MiraProfile();
    public static IPetAnimationProfile Llama { get; } = new LlamaProfile();
    public static IPetAnimationProfile Capybara { get; } = new CapybaraProfile();
    public static IPetAnimationProfile OrangeCat { get; } = new CatProfile();
    public static IReadOnlyList<IPetAnimationProfile> All { get; } = [Mira, Llama, Capybara, OrangeCat];
}
