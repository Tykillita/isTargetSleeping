namespace IsTargetSleeping;

/// Personalidad pura, sin ventanas ni dibujo. Los clips se crean una vez y se reutilizan.
public interface IPetAnimationProfile
{
    double WalkSpeed { get; }
    double MinWait { get; }
    double MaxWait { get; }
    PetClip For(PetActivity activity);
    PetClip For(PetReaction reaction);
    PetClip Gesture(PetGesture gesture);
    PetClip? Transition(PetActivity from, PetActivity to, bool fromHiding, bool toHiding);
    PetClip Peek { get; }
    PetPose PeekStill { get; }
    PetPose Still(PetActivity activity);
    PetPose Still(PetReaction reaction);
    PetPose Walk(double distance, int facing, double lean, bool turning);
    (double Dx, double Dy) Motion(PetActivity activity, double seconds);
    (double Dx, double Dy) Motion(PetReaction reaction, double seconds);
    (double Dx, double Dy) Motion(PetGesture gesture, double seconds);
    double Breath(PetActivity activity, double seconds);
    IReadOnlyList<(PetGesture Gesture, int Weight)> IdleGestures(bool drowsy);
    double ReactionDuration(PetReaction reaction);
}

public static class PetProfiles
{
    public static IPetAnimationProfile Mira { get; } = new MiraAnimationProfile();
    public static IPetAnimationProfile Llama { get; } = new AnimalAnimationProfile(AnimalCharacter.Llama);
    public static IPetAnimationProfile Capybara { get; } = new AnimalAnimationProfile(AnimalCharacter.Capybara);
    public static IPetAnimationProfile OrangeCat { get; } = new AnimalAnimationProfile(AnimalCharacter.Cat);
}

/// Adaptador de Mira: conserva sus claves, tiempos, gestos y paseo originales.
public sealed class MiraAnimationProfile : IPetAnimationProfile
{
    private readonly Dictionary<PetActivity, PetClip> activities = Enum.GetValues<PetActivity>().ToDictionary(a => a, PetAnimations.For);
    private readonly Dictionary<PetReaction, PetClip> reactions = Enum.GetValues<PetReaction>().ToDictionary(r => r, PetAnimations.For);
    private readonly Dictionary<PetGesture, PetClip> gestures = Enum.GetValues<PetGesture>().ToDictionary(g => g, PetAnimations.Gesture);
    private static readonly (PetGesture, int)[] Awake = [(PetGesture.LookAround, 3), (PetGesture.Stretch, 2), (PetGesture.Scratch, 2), (PetGesture.Wave, 2), (PetGesture.Hop, 2), (PetGesture.Yawn, 1)];
    private static readonly (PetGesture, int)[] Drowsy = [(PetGesture.Nod, 4), (PetGesture.Yawn, 3), (PetGesture.Stretch, 2), (PetGesture.Scratch, 1), (PetGesture.LookAround, 1)];
    public double WalkSpeed => 12;
    public double MinWait => 6;
    public double MaxWait => 15;
    public PetClip For(PetActivity activity) => activities[activity];
    public PetClip For(PetReaction reaction) => reactions[reaction];
    public PetClip Gesture(PetGesture gesture) => gestures[gesture];
    public PetClip? Transition(PetActivity from, PetActivity to, bool fromHiding, bool toHiding) => PetAnimations.Transition(from, to, fromHiding, toHiding);
    public PetClip Peek => PetAnimations.Peek;
    public PetPose PeekStill => PetAnimations.PeekStill;
    public PetPose Still(PetActivity activity) => PetAnimations.Still(activity);
    public PetPose Still(PetReaction reaction) => PetAnimations.Still(reaction);
    public PetPose Walk(double distance, int facing, double lean, bool turning) => PetAnimations.Walk(distance, facing, lean, turning);
    public (double Dx, double Dy) Motion(PetActivity activity, double seconds) => PetAnimations.Motion(activity, seconds);
    public (double Dx, double Dy) Motion(PetReaction reaction, double seconds) => PetAnimations.Motion(reaction, seconds);
    public (double Dx, double Dy) Motion(PetGesture gesture, double seconds) => PetAnimations.Motion(gesture, seconds);
    public double Breath(PetActivity activity, double seconds) => PetAnimations.Breath(activity, seconds);
    public IReadOnlyList<(PetGesture Gesture, int Weight)> IdleGestures(bool drowsy) => drowsy ? Drowsy : Awake;
    public double ReactionDuration(PetReaction reaction) => reaction == PetReaction.None ? 0 : For(reaction).Length;
}

public enum AnimalCharacter { Llama, Capybara, Cat }

/// Claves anatómicas propias: orejas, cola, cuello (Stretch), patas y posturas de reposo.
/// Cada animal tiene sus clips y su ritmo; ninguno depende del dibujo de Mira.
public sealed class AnimalAnimationProfile : IPetAnimationProfile
{
    private readonly AnimalCharacter character;
    private readonly double pace;
    private readonly Dictionary<PetActivity, PetClip> activities = [];
    private readonly Dictionary<PetReaction, PetClip> reactions = [];
    private readonly Dictionary<PetGesture, PetClip> gestures = [];
    private readonly (PetGesture, int)[] awake, sleepy;
    private PetPose Rest => new(ArmL: PetArm.Down, ArmR: PetArm.Down, Eye: Calm ? 0.8 : 1);
    private PetPose Asleep => Rest with { Eye = 0, Sit = 1, Squash = Cat ? 0.8 : 0.5, BedIn = 1, Tint = PetTint.Sleepy, EarL = -0.5, EarR = -0.5, Tail = Cat ? -1 : 0 };
    private bool Cat => character == AnimalCharacter.Cat;
    private bool Calm => character == AnimalCharacter.Capybara;
    public double WalkSpeed => Calm ? 7 : Cat ? 18 : 14;
    public double MinWait => Calm ? 12 : Cat ? 4 : 6;
    public double MaxWait => Calm ? 22 : Cat ? 9 : 12;
    public PetClip Peek { get; }
    public PetPose PeekStill => Rest with { Behind = true, Peek = 2, Sit = 0.6, Squash = 0.6, LookX = -1 };

    public AnimalAnimationProfile(AnimalCharacter character)
    {
        this.character = character;
        pace = Calm ? 1.6 : Cat ? 0.8 : 1.05;
        awake = Calm
            ? [(PetGesture.Yawn, 4), (PetGesture.Nod, 0), (PetGesture.Stretch, 2), (PetGesture.Scratch, 1), (PetGesture.LookAround, 2), (PetGesture.Wave, 1)]
            : Cat ? [(PetGesture.Scratch, 5), (PetGesture.Hop, 4), (PetGesture.LookAround, 3), (PetGesture.Stretch, 3), (PetGesture.Wave, 1), (PetGesture.Yawn, 1)]
            : [(PetGesture.LookAround, 5), (PetGesture.Stretch, 4), (PetGesture.Wave, 2), (PetGesture.Scratch, 2), (PetGesture.Hop, 1), (PetGesture.Yawn, 1)];
        sleepy = [(PetGesture.Nod, 5), (PetGesture.Yawn, 4), (PetGesture.Stretch, 2), (PetGesture.Scratch, 1)];
        BuildActivities();
        BuildReactions();
        BuildGestures();
        Peek = Clip(7, true, (0, PeekStill with { Peek = 0 }), (1, PeekStill with { Peek = 0 }),
            (2, PeekStill with { Peek = 3, EarL = 0.8, Tail = Cat ? 0.7 : 0 }),
            (3.5, PeekStill with { Peek = 3, LookX = 1 }), (4.5, PeekStill with { Peek = 0, Blush = 0.5 }), (6, PeekStill));
    }

    private PetClip Clip(double length, bool loop, params (double T, PetPose Pose)[] keys) =>
        new(keys.Select(k => new PetKey(k.T * pace, k.Pose)).ToArray(), loop, length * pace);
    private void BuildActivities()
    {
        var r = Rest;
        var d = r with { Eye = 0.4, Sit = Calm ? 0.45 : 0.15 };
        activities[PetActivity.Hidden] = PetClip.Hold(r);
        activities[PetActivity.DeepSleep] = Clip(3.6, true, (0, Asleep), (1.6, Asleep with { Squash = Asleep.Squash + 0.25 }));
        activities[PetActivity.WakingUp] = Clip(4, true, (0, d with { Eye = 0 }),
            (0.8, r with { Eye = 0.2, Stretch = Calm ? 0.3 : 1, EarL = 0.7, EarR = 0.7, Mouth = 0.7 }),
            (1.6, r with { ArmR = PetArm.Rub, Eye = 0.35, Tail = Cat ? 0.7 : 0 }),
            (2.8, r with { Eye = 0.6, LookX = 0.6 }), (3.8, d));
        activities[PetActivity.Drowsy] = Clip(6, true, (0, d), (1.8, d with { Eye = 0, Bob = 0.6, EarL = -0.4, EarR = -0.4 }),
            (2.6, d with { Eye = Calm ? 0.55 : 0.8, Bob = -0.2 }), (4, d with { LookX = -0.5 }), (5.5, d));
        activities[PetActivity.Alert] = Clip(Cat ? 4.5 : Calm ? 9 : 6, true, (0, r),
            (1, r with { LookX = -0.8, EarL = 0.8, Tail = Cat ? 0.8 : 0 }),
            (1.8, r with { Eye = 0 }), (2.1, r),
            (3, r with { LookX = 0.8, EarR = 0.8, Tail = Cat ? -0.5 : 0 }), (4, r));
        var eat = r with { Prop = PetProp.Crumb, ArmR = PetArm.Rub, LookY = 0.3 };
        activities[PetActivity.Eating] = Clip(0.9, true, (0, eat), (0.22, eat with { Mouth = 0.9, Squash = 0.2, EarL = Cat ? 0.8 : 0.3 }),
            (0.45, eat), (0.68, eat with { Mouth = 0.5, Blush = Calm ? 0.25 : 0.1, Tail = Cat ? 0.6 : 0 }));
        var work = r with { Prop = PetProp.Laptop, LookY = 0.55, Eye = Calm ? 0.65 : 1 };
        activities[PetActivity.Working] = Clip(Calm ? 1.8 : 0.8, true, (0, work with { ArmL = 78, ArmR = 45 }),
            (Calm ? 0.6 : 0.2, work with { ArmL = 45, ArmR = 78, EarR = 0.4, Tail = Cat ? 0.9 : 0 }),
            (Calm ? 1.2 : 0.4, work with { ArmL = 78, ArmR = 45, Tail = Cat ? -0.5 : 0 }),
            (Calm ? 1.6 : 0.6, work with { ArmL = 45, ArmR = 78 }));
        var box = r with { Prop = PetProp.Box, ArmL = PetArm.Up, ArmR = PetArm.Up };
        activities[PetActivity.Downloading] = Clip(0.8, true, (0, box with { Feet = PetFeet.StepLeft, EarL = -0.5, Tail = Cat ? 0.8 : 0 }),
            (0.2, box), (0.4, box with { Feet = PetFeet.StepRight, Squash = Calm ? 0.1 : 0.3 }), (0.6, box));
        var broom = r with { Prop = PetProp.Broom };
        activities[PetActivity.Sweeping] = Clip(1.8, true, (0, broom with { Sway = -1, LookX = -0.6, ArmR = 65, Tail = Cat ? -0.7 : 0 }),
            (0.9, broom with { Sway = 1, LookX = 0.6, ArmR = 30, Tail = Cat ? 0.7 : 0 }));
        activities[PetActivity.Yawning] = Clip(2.6, false, (0, d),
            (0.8, r with { Eye = 0, Mouth = 1, Stretch = Calm ? 0.4 : 1, Tail = Cat ? 0.8 : 0 }),
            (1.4, r with { Eye = 0, Mouth = 0.4, Sit = 0.5, BedIn = 0.4 }), (2.6, Asleep));
    }

    private void BuildReactions()
    {
        var r = Rest;
        reactions[PetReaction.None] = PetClip.Hold(r);
        reactions[PetReaction.Jump] = Clip(0.65, false, (0, r), (0.12, r with { Squash = 0.6, EarL = -0.4, EarR = -0.4 }),
            (0.3, r with { Squash = -0.5, EarL = 0.7, EarR = 0.7, Tail = Cat ? 1 : 0 }), (0.52, r with { Squash = 0.45 }), (0.65, r));
        reactions[PetReaction.Hearts] = Clip(1.6, false, (0, r),
            (0.3, r with { Eye = 0.1, Blush = 0.9, EarL = 0.5, EarR = 0.5, Tail = Cat ? 1 : 0, Stretch = Calm ? 0 : 0.2 }),
            (0.7, r with { Eye = 0, Blush = 0.8, ArmR = Cat ? PetArm.Rub : PetArm.Wave, Tail = Cat ? -0.6 : 0 }),
            (1.1, r with { Eye = 0.1, Blush = 0.7, Tail = Cat ? 0.9 : 0 }), (1.6, r));
        reactions[PetReaction.Sparkle] = Clip(1.25, false, (0, r),
            (0.3, r with { Stretch = Calm ? 0.15 : 0.6, ArmL = PetArm.Up, ArmR = PetArm.Up, EarL = 0.8, EarR = 0.8 }),
            (0.65, r with { Blush = 0.4, Tail = Cat ? 1 : 0 }), (0.9, r with { Squash = -0.25 }), (1.25, r));
        reactions[PetReaction.Dizzy] = Clip(1.8, false, (0, r), (0.4, r with { Eye = 0.5, LookX = -1, EarL = -0.8, EarR = 0.5, Tail = Cat ? -1 : 0 }),
            (0.8, r with { Eye = 0.4, LookX = 1, EarL = 0.5, EarR = -0.8, Tail = Cat ? 1 : 0 }),
            (1.2, r with { Eye = 0.5, LookX = -0.8 }), (1.8, r));
        reactions[PetReaction.Sad] = Clip(2.2, false, (0, r),
            (0.5, r with { Eye = 0.35, LookY = 0.6, Sit = 0.5, EarL = -1, EarR = -1, Tail = Cat ? -1 : 0 }),
            (1.7, r with { Eye = 0.4, EarL = -0.8, EarR = -0.8, Tail = Cat ? -0.7 : 0 }), (2.2, r));
        reactions[PetReaction.Nap] = Clip(2, false, (0, r with { Eye = 0.5 }),
            (0.6, r with { Eye = 0, Mouth = 0.8, Stretch = Calm ? 0.3 : 0.8 }), (1.3, Asleep with { BedIn = 0.6 }), (2, Asleep));
    }

    private void BuildGestures()
    {
        var r = Rest;
        gestures[PetGesture.None] = PetClip.Hold(r);
        gestures[PetGesture.Stretch] = Clip(2.2, false, (0, r), (0.6, r with { Stretch = 1, Eye = 0.3, ArmL = PetArm.Up, ArmR = PetArm.Up, Tail = Cat ? 0.8 : 0 }),
            (1.4, r with { Stretch = 0.85, EarL = 0.7, EarR = 0.7 }), (2.2, r));
        gestures[PetGesture.Scratch] = Clip(2.4, false, (0, r),
            (0.5, r with { ArmR = PetArm.Rub, Eye = Cat ? 0.2 : 0.6, EarR = -0.7, Mouth = Cat ? 0.5 : 0 }),
            (0.9, r with { ArmR = -110, Eye = 0.4, EarR = 0.4 }),
            (1.3, r with { ArmR = PetArm.Rub, Eye = 0.2, Mouth = Cat ? 0.8 : 0, Tail = Cat ? 0.6 : 0 }), (1.8, r with { ArmR = -110 }), (2.4, r));
        gestures[PetGesture.Yawn] = Clip(2, false, (0, r), (0.6, r with { Eye = 0, Mouth = 1, Stretch = 0.5, EarL = -0.5, EarR = -0.5 }),
            (1.3, r with { Eye = 0.3, Mouth = 0.5 }), (2, r));
        gestures[PetGesture.LookAround] = Clip(3, false, (0, r), (0.6, r with { LookX = -1, EarL = 1, Stretch = Calm ? 0 : 0.3 }),
            (1.5, r with { LookX = 1, EarR = 1, Tail = Cat ? 0.9 : 0 }), (2.4, r with { LookY = -0.6, EarL = 0.5, EarR = 0.5 }), (3, r));
        gestures[PetGesture.Wave] = Clip(1.8, false, (0, r), (0.4, r with { ArmR = PetArm.Wave, Blush = 0.3, EarR = 0.7 }),
            (0.8, r with { ArmR = -30, Tail = Cat ? 1 : 0 }), (1.2, r with { ArmR = PetArm.Wave }), (1.8, r));
        gestures[PetGesture.Hop] = Clip(0.9, false, (0, r), (0.15, r with { Squash = 0.6 }),
            (0.4, r with { Squash = -0.5, Tail = Cat ? 1 : 0, EarL = 0.8, EarR = 0.8 }), (0.65, r with { Squash = 0.4 }), (0.9, r));
        var d = Still(PetActivity.Drowsy);
        gestures[PetGesture.Nod] = Clip(2.6, false, (0, d), (1, d with { Eye = 0, Bob = 0.8, EarL = -0.5, EarR = -0.5 }),
            (1.5, d with { Eye = Calm ? 0.6 : 0.9, Bob = -0.3 }), (2.6, d));
    }

    public PetClip For(PetActivity activity) => activities[activity];
    public PetClip For(PetReaction reaction) => reactions[reaction];
    public PetClip Gesture(PetGesture gesture) => gestures[gesture];
    public double ReactionDuration(PetReaction reaction) => reaction == PetReaction.None ? 0 : For(reaction).Length;
    public IReadOnlyList<(PetGesture Gesture, int Weight)> IdleGestures(bool drowsy) => drowsy ? sleepy : awake;
    public PetPose Still(PetActivity activity) => activity switch
    {
        PetActivity.DeepSleep or PetActivity.Yawning => Asleep,
        PetActivity.WakingUp => Rest with { Eye = 0.5, Stretch = 0.4 },
        PetActivity.Drowsy => Rest with { Eye = 0.4, Sit = 0.3 },
        PetActivity.Eating => Rest with { Prop = PetProp.Crumb, ArmR = PetArm.Rub, Mouth = 0.5 },
        PetActivity.Working => Rest with { Prop = PetProp.Laptop, ArmL = 78, ArmR = 78, LookY = 0.55 },
        PetActivity.Downloading => Rest with { Prop = PetProp.Box, ArmL = PetArm.Up, ArmR = PetArm.Up },
        PetActivity.Sweeping => Rest with { Prop = PetProp.Broom, Sway = -1 },
        _ => Rest,
    };
    public PetPose Still(PetReaction reaction) => reaction == PetReaction.Nap ? Asleep : For(reaction).At(ReactionDuration(reaction) * 0.4);
    public PetClip? Transition(PetActivity from, PetActivity to, bool fromHiding, bool toHiding)
    {
        if (fromHiding != toHiding)
            return Clip(0.7, false, (0, fromHiding ? PeekStill : Still(from)),
                (0.35, Rest with { Sit = 0.4, Squash = 0.4, EarL = -0.4, EarR = -0.4 }), (0.7, toHiding ? PeekStill : For(to).At(0)));
        if (from == to) return null;
        var before = Still(from);
        var after = For(to).At(0);
        if (before.Prop != after.Prop)
            return Clip(0.8, false, (0, before), (0.35, before with { PropIn = 0 }),
                (0.4, after with { PropIn = 0 }), (0.8, after));
        return Clip(to == PetActivity.DeepSleep || from == PetActivity.DeepSleep ? 1.2 : 0.5, false,
            (0, before), (to == PetActivity.DeepSleep || from == PetActivity.DeepSleep ? 1.2 : 0.5, after));
    }
    public PetPose Walk(double distance, int facing, double lean, bool turning)
    {
        double phase = distance * Math.PI / (Calm ? 4 : Cat ? 2.5 : 3);
        return Rest with { Feet = turning ? PetFeet.Stand : Math.Sin(phase) > 0 ? PetFeet.StepLeft : PetFeet.StepRight,
            Bob = turning ? 0 : -Math.Abs(Math.Sin(phase)) * (Calm ? 0.15 : Cat ? 0.65 : 0.45), Lean = lean,
            LookX = facing * 0.7, EarL = Math.Sin(phase) * 0.3, EarR = -Math.Sin(phase) * 0.3,
            Tail = Cat ? Math.Sin(phase * 0.5) * 0.8 : 0 };
    }
    public (double Dx, double Dy) Motion(PetActivity activity, double seconds) => activity switch
    {
        PetActivity.Downloading => (0, -Math.Abs(Math.Sin(seconds / pace * Math.PI / 0.4)) * (Calm ? 0.15 : Cat ? 1.1 : 0.7)),
        PetActivity.Sweeping => (Math.Sin(seconds / pace * Math.PI / 0.9) * (Calm ? 0.3 : 0.8), 0),
        _ => (0, 0),
    };
    public (double Dx, double Dy) Motion(PetReaction reaction, double seconds)
    {
        double t = Math.Clamp(seconds / Math.Max(0.001, ReactionDuration(reaction)), 0, 1);
        return reaction switch
        {
            PetReaction.Jump => (0, t is > 0.18 and < 0.8 ? -(Calm ? 1.4 : Cat ? 6 : 4) * Math.Sin(Math.PI * (t - 0.18) / 0.62) : 0),
            PetReaction.Hearts => (Math.Sin(t * Math.PI * 4) * (Calm ? 0.2 : Cat ? 1 : 0.5), 0),
            PetReaction.Sparkle => (0, -Math.Sin(t * Math.PI) * (Calm ? 0.3 : Cat ? 2 : 1)),
            PetReaction.Dizzy => (Math.Sin(t * Math.PI * 6) * (1 - t), 0),
            _ => (0, 0),
        };
    }
    public (double Dx, double Dy) Motion(PetGesture gesture, double seconds)
    {
        double t = seconds / Gesture(gesture).Length;
        return gesture == PetGesture.Hop && t is > 0.15 and < 0.75
            ? (0, -Math.Sin(Math.PI * (t - 0.15) / 0.6) * (Calm ? 0.8 : Cat ? 4 : 2.5)) : (0, 0);
    }
    public double Breath(PetActivity activity, double seconds) => activity is PetActivity.Alert or PetActivity.Drowsy or PetActivity.Working or PetActivity.Eating
        ? (Calm ? 0.08 : 0.12) * Math.Sin(seconds * Math.PI * 2 / (Calm ? 4 : Cat ? 2.2 : 3)) : 0;
}
