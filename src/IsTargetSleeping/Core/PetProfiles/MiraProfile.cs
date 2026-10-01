namespace IsTargetSleeping;

/// Mira, vigilante y algo hacker: patrulla con el ojo como un radar, fija la retícula
/// sobre el cursor, su antena emite señales y al trabajar le suben bits. Conserva sus
/// claves, tiempos y paseo originales (`PetAnimations`); al clic hace «ping».
public sealed class MiraProfile : ProfileKit
{
    protected override PetPose Rest => new();
    protected override PetPose Asleep => PetAnimations.Asleep;
    public override double WalkSpeed => 12;
    public override double MinWait => 6;
    public override double MaxWait => 15;
    public override double LookSpeed => 16;

    public MiraProfile()
    {
        foreach (var a in Enum.GetValues<PetActivity>()) Activities[a] = PetAnimations.For(a);
        foreach (var r in Enum.GetValues<PetReaction>()) Reactions[r] = PetAnimations.For(r);
        foreach (var g in Enum.GetValues<PetGesture>()) Gestures[g] = PetAnimations.Gesture(g);
        // Clic: el salto de siempre y, a la vez, un anillo de radar que sale del ojo (la
        // antena destella con la ráfaga de señales).
        PetPose jump(double squash, double arms, double scan) => new(Squash: squash, ArmL: arms, ArmR: arms, Scan: scan);
        Reactions[PetReaction.Jump] = Once(
            K(0, new()), K(0.09, jump(0.8, PetArm.Down, 0.1), Ease.Out), K(0.22, jump(-0.6, PetArm.Up, 0.28), Ease.Linear),
            K(0.32, jump(0, PetArm.Out, 0.42)), K(0.4, jump(0.7, PetArm.Down, 0.53), Ease.Linear), K(0.5, jump(0, PetArm.Out, 0.66), Ease.Out),
            K(0.75, jump(0, PetArm.Out, 1), Ease.Linear), K(0.76, new(), Ease.Step));
        Peek = PetAnimations.Peek;
        PeekStill = PetAnimations.PeekStill;
        AwakeGestures = [(PetGesture.Scan, 3), (PetGesture.Ping, 2), (PetGesture.Calibrate, 2), (PetGesture.LookAround, 2),
            (PetGesture.Wave, 1), (PetGesture.Hop, 1), (PetGesture.Stretch, 1), (PetGesture.Scratch, 1)];
        DrowsyGestures = [(PetGesture.Nod, 4), (PetGesture.Yawn, 3), (PetGesture.Stretch, 2), (PetGesture.Scratch, 1), (PetGesture.Calibrate, 1)];

        ActivityDrips[PetActivity.DeepSleep] = D((ParticleKind.Z, 1.1));
        ActivityDrips[PetActivity.Eating] = D((ParticleKind.Sparkle, 0.35));
        ActivityDrips[PetActivity.Sweeping] = D((ParticleKind.Dust, 0.12));
        ActivityDrips[PetActivity.Working] = D((ParticleKind.Bit, 0.22));
        ReactionDrips[PetReaction.Sad] = D((ParticleKind.Tear, 0.5));
        ReactionDrips[PetReaction.Hearts] = D((ParticleKind.Heart, 0.22));
        ReactionDrips[PetReaction.Sparkle] = D((ParticleKind.Sparkle, 0.07), (ParticleKind.Bit, 0.15));
        GestureDrips[PetGesture.Ping] = D((ParticleKind.Signal, 0.3));
        Bursts[PetReaction.Jump] = [(ParticleKind.Signal, 1)];
        Bursts[PetReaction.Dizzy] = [(ParticleKind.Star, 3)];
        Complete();
    }

    /// Fija la retícula sobre el cursor y, al llegar, un barrido de radar.
    public override PetPose Hover(PetPose pose, double hoverX, double hoverY, double seconds) =>
        pose with { Lock = 1, Scan = seconds < 0.7 ? seconds / 0.7 : 0 };

    public override PetClip? Transition(PetActivity from, PetActivity to, bool fromHiding, bool toHiding) =>
        PetAnimations.Transition(from, to, fromHiding, toHiding);
    public override PetPose Still(PetActivity activity) => PetAnimations.Still(activity);
    public override PetPose Still(PetReaction reaction) => PetAnimations.Still(reaction);
    public override PetPose Walk(double distance, int facing, double lean, bool turning) => PetAnimations.Walk(distance, facing, lean, turning);
    public override (double Dx, double Dy) Motion(PetActivity activity, double seconds) => PetAnimations.Motion(activity, seconds);
    public override (double Dx, double Dy) Motion(PetReaction reaction, double seconds) => PetAnimations.Motion(reaction, seconds);
    public override (double Dx, double Dy) Motion(PetGesture gesture, double seconds) => PetAnimations.Motion(gesture, seconds);
    public override double Breath(PetActivity activity, double seconds) => PetAnimations.Breath(activity, seconds);
}
