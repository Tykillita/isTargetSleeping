namespace IsTargetSleeping;

/// Una partícula que se suelta cada `Every` segundos mientras dura lo que la provoca.
public readonly record struct PetDrip(ParticleKind Kind, double Every);

/// Una actividad y de dónde venía (`From`): la misma actividad puede verse distinta según
/// el origen (la capibara limpia escurriéndose si sale de la tina y, despierta, frotándose
/// el trasero). Sin origen (`Hidden`), la versión de siempre.
public readonly record struct PetContext(PetActivity Activity, PetActivity From = PetActivity.Hidden)
{
    /// Viene de la cama (dormida o bostezando camino de ella).
    public bool AfterBed => From.InBed();
    public static implicit operator PetContext(PetActivity activity) => new(activity);
}

/// Personalidad pura, sin ventanas ni dibujo: su coreografía, cómo trata al ratón y qué
/// partículas suelta. Los clips se crean una vez y se reutilizan. Las reacciones dependen
/// de la actividad en la que empiezan (`during`): dormida reacciona sin salir de la cama y
/// el final de una limpieza se hace donde limpia (y como limpia, según de dónde venía).
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
    /// El bucle de la actividad según de dónde venía.
    PetClip For(PetContext activity);
    PetClip For(PetReaction reaction, PetContext during);
    PetClip Gesture(PetGesture gesture);
    PetClip? Transition(PetActivity from, PetActivity to, bool fromHiding, bool toHiding);
    /// Cuánto se mantiene como mínimo una actividad al entrar en ella, aunque las señales ya
    /// pidan otra, para que su animación se vea entera (0: nada).
    double HoldFor(PetActivity from, PetActivity to);
    PetClip Peek { get; }
    PetPose PeekStill { get; }
    PetPose Still(PetActivity activity);
    PetPose Still(PetContext activity);
    PetPose Still(PetReaction reaction, PetContext during);
    PetPose Walk(double distance, int facing, double lean, bool turning);
    /// Cómo cambia la pose con el ratón encima (`hoverX`/`hoverY`: −1 … 1; `seconds`: desde que llegó).
    PetPose Hover(PetPose pose, double hoverX, double hoverY, double seconds);
    (double Dx, double Dy) Motion(PetActivity activity, double seconds);
    (double Dx, double Dy) Motion(PetContext activity, double seconds);
    (double Dx, double Dy) Motion(PetReaction reaction, double seconds, PetContext during);
    (double Dx, double Dy) Motion(PetGesture gesture, double seconds);
    double Breath(PetActivity activity, double seconds);
    IReadOnlyList<(PetGesture Gesture, int Weight)> IdleGestures(bool drowsy);
    double ReactionDuration(PetReaction reaction, PetContext during);
    /// Lo que suelta durante una actividad, una reacción o un gesto, y la ráfaga al empezar una reacción.
    IReadOnlyList<PetDrip> Drips(PetActivity activity);
    IReadOnlyList<PetDrip> Drips(PetContext activity);
    IReadOnlyList<PetDrip> Drips(PetReaction reaction, PetContext during);
    IReadOnlyList<PetDrip> GestureParticles(PetGesture gesture);
    IReadOnlyList<(ParticleKind Kind, int Count)> Burst(PetReaction reaction, PetContext during);
}

public static class PetProfiles
{
    public static IPetAnimationProfile Mira { get; } = new MiraProfile();
    public static IPetAnimationProfile Llama { get; } = new LlamaProfile();
    public static IPetAnimationProfile Capybara { get; } = new CapybaraProfile();
    public static IPetAnimationProfile OrangeCat { get; } = new CatProfile();
    public static IReadOnlyList<IPetAnimationProfile> All { get; } = [Mira, Llama, Capybara, OrangeCat];
}
