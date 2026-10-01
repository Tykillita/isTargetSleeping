namespace IsTargetSleeping;

/// La capibara: zen e imperturbable. Lenta, con los ojos entornados; casi nada la
/// altera (al clic, un parpadeo lento y una burbuja). Duerme en un baño termal con
/// vapor, a veces se le posa un pajarito y la mandarina de la cabeza se tambalea. No se
/// esconde tras el logo: espera apoyada en él, tranquila.
public sealed class CapybaraProfile : ProfileKit
{
    protected override PetPose Rest => new(ArmL: PetArm.Down, ArmR: PetArm.Down, Eye: 0.55);
    protected override PetPose Asleep => Rest with { Eye = 0, Sit = 1, Squash = 0.4, BedIn = 1, Tint = PetTint.Sleepy };
    private PetPose Dozy => Rest with { Eye = 0.3, Sit = 0.4 };
    public override double WalkSpeed => 7;
    public override double MinWait => 12;
    public override double MaxWait => 22;
    public override double LookSpeed => 4;
    public override bool HidesBehindLogo => false;

    public CapybaraProfile()
    {
        var r = Rest;
        var d = Dozy;
        Activities[PetActivity.DeepSleep] = Loop(5.5, K(0, Asleep), K(2.4, Asleep with { Squash = 0.6 }));
        Activities[PetActivity.WakingUp] = Loop(6.4, K(0, d with { Eye = 0 }), K(1.5, r with { Eye = 0.15, Mouth = 0.6, Stretch = 0.3 }),
            K(3, r with { Eye = 0.3, Mouth = 0.1 }), K(4.5, r with { Eye = 0.45, LookX = 0.4 }), K(5.8, d));
        Activities[PetActivity.Drowsy] = Loop(9, K(0, d), K(3, d with { Eye = 0, Bob = 0.4 }), K(4.2, d with { Eye = 0.35 }),
            K(6.5, d with { LookX = -0.4 }), K(8, d));
        // Casi inmóvil: un parpadeo lento y una mirada de reojo de vez en cuando.
        Activities[PetActivity.Alert] = Loop(9, K(0, r), K(2.5, r), K(2.8, r with { Eye = 0 }), K(3.4, r),
            K(5, r with { LookX = 0.4, EarR = 0.5 }), K(7, r with { LookX = 0.4 }), K(8, r));
        var eat = r with { Prop = PetProp.Crumb, LookY = 0.3 };
        Activities[PetActivity.Eating] = Loop(1.6, K(0, eat with { Mouth = 0.5 }), K(0.8, eat with { Blush = 0.2 }));
        var work = r with { Prop = PetProp.Laptop, LookY = 0.55, Eye = 0.5 };
        Activities[PetActivity.Working] = Loop(2.4, K(0, work), K(1.2, work with { Bob = 0.3, LookX = 0.3 }), K(1.8, work with { LookX = -0.2 }));
        // La caja en equilibrio sobre la cabeza, con la mandarina encima; pasitos lentos.
        var box = r with { Prop = PetProp.Box };
        Activities[PetActivity.Downloading] = Loop(1.4, K(0, box with { Feet = PetFeet.StepLeft, EarL = 0.3 }), K(0.35, box),
            K(0.7, box with { Feet = PetFeet.StepRight, EarL = -0.3, Squash = 0.1 }), K(1.05, box));
        var broom = r with { Prop = PetProp.Broom };
        Activities[PetActivity.Sweeping] = Loop(2.6, K(0, broom with { Sway = -1, LookX = -0.4 }), K(1.3, broom with { Sway = 1, LookX = 0.4 }));
        Activities[PetActivity.Yawning] = Once(K(0, d), K(1.2, r with { Eye = 0, Mouth = 1, Stretch = 0.4 }),
            K(2.2, r with { Eye = 0, Mouth = 0.3, Sit = 0.5, BedIn = 0.4 }), K(3.4, Asleep));

        // Clic: un parpadeo lento, nada más (y una burbuja).
        Reactions[PetReaction.Jump] = Once(K(0, r), K(0.45, r with { Eye = 0 }), K(1.0, r with { Eye = 0, Blush = 0.2 }), K(1.45, r));
        Reactions[PetReaction.Hearts] = Once(K(0, r), K(0.6, r with { Eye = 0, Blush = 0.6, Lean = 0.2 }),
            K(1.6, r with { Eye = 0, Blush = 0.6, Lean = -0.2 }), K(2.2, r));
        Reactions[PetReaction.Sparkle] = Once(K(0, r), K(0.5, r with { Eye = 0.8, Squash = -0.1, Blush = 0.2 }), K(1.2, r with { Eye = 0.8 }), K(1.7, r));
        Reactions[PetReaction.Dizzy] = Once(K(0, r), K(0.6, r with { Eye = 0.3, LookX = -0.6 }), K(1.2, r with { Eye = 0.3, LookX = 0.6 }),
            K(1.8, r with { Eye = 0.4 }), K(2.3, r));
        // Ni la tristeza la saca de quicio: baja la mirada mientras cae una hoja.
        Reactions[PetReaction.Sad] = Once(K(0, r), K(0.8, r with { Eye = 0.3, LookY = 0.4, Sit = 0.3 }),
            K(2.0, r with { Eye = 0.3, LookY = 0.4, Sit = 0.3 }), K(2.6, r));
        Reactions[PetReaction.Nap] = Once(K(0, r with { Eye = 0.4 }), K(0.9, r with { Eye = 0, Mouth = 0.6 }),
            K(1.8, Asleep with { BedIn = 0.6 }), K(2.6, Asleep));

        // Un pajarito llega volando, se posa en su lomo un rato y se va.
        Gestures[PetGesture.Bird] = Once(K(0, r), K(2.4, r with { Bird = 1, Eye = 0.45 }, Ease.Out),
            K(3.2, r with { Bird = 1, Eye = 0.25, Blush = 0.2 }), K(6.6, r with { Bird = 1, Eye = 0.25, Blush = 0.2 }),
            K(8, r with { Bird = 0 }, Ease.In), K(8.6, r));
        // Se mete un rato en su baño termal.
        Gestures[PetGesture.Bath] = Once(K(0, r), K(1.6, r with { Bath = 1, Sit = 0.5, Eye = 0.25 }),
            K(8.4, r with { Bath = 1, Sit = 0.5, Eye = 0.2, Blush = 0.2 }), K(10, r));
        Gestures[PetGesture.Yawn] = Once(K(0, r), K(1, r with { Eye = 0, Mouth = 1, Stretch = 0.3 }), K(2.2, r with { Eye = 0.2, Mouth = 0.3 }), K(3.2, r));
        Gestures[PetGesture.Nod] = Once(K(0, d), K(1.6, d with { Eye = 0, Bob = 0.6 }, Ease.In), K(2.3, d with { Eye = 0.4, Bob = -0.1 }), K(3.6, d));
        AwakeGestures = [(PetGesture.Bird, 3), (PetGesture.Bath, 2), (PetGesture.Yawn, 2)];
        DrowsyGestures = [(PetGesture.Nod, 4), (PetGesture.Yawn, 2), (PetGesture.Bath, 2), (PetGesture.Bird, 1)];

        // Esperando un modelo: sentada junto al logo, apoyada en él, sin esconderse.
        PeekStill = r with { Lean = 0.5, Sit = 0.3, Eye = 0.35 };
        Peek = Loop(9, K(0, PeekStill), K(3, PeekStill with { Eye = 0.25 }), K(3.4, PeekStill with { Eye = 0 }),
            K(4.6, PeekStill with { Eye = 0.3, Blush = 0.15 }), K(7, PeekStill with { LookX = 0.4 }), K(8.2, PeekStill));

        ActivityDrips[PetActivity.DeepSleep] = D((ParticleKind.Steam, 0.8), (ParticleKind.Bubble, 1.7));
        ActivityDrips[PetActivity.Alert] = D((ParticleKind.Leaf, 7));
        ActivityDrips[PetActivity.Working] = D((ParticleKind.Leaf, 4));
        ActivityDrips[PetActivity.Eating] = D((ParticleKind.Sparkle, 0.6));
        ActivityDrips[PetActivity.Sweeping] = D((ParticleKind.Leaf, 0.9), (ParticleKind.Dust, 0.3));
        ReactionDrips[PetReaction.Hearts] = D((ParticleKind.Heart, 0.45));
        ReactionDrips[PetReaction.Sad] = D((ParticleKind.Leaf, 1.0));
        ReactionDrips[PetReaction.Sparkle] = D((ParticleKind.Sparkle, 0.12));
        GestureDrips[PetGesture.Bath] = D((ParticleKind.Steam, 0.55), (ParticleKind.Bubble, 1.8));
        Bursts[PetReaction.Jump] = [(ParticleKind.Bubble, 1)];
        Bursts[PetReaction.Dizzy] = [(ParticleKind.Star, 3)];
        Complete();
    }

    public override PetPose Still(PetActivity activity) => activity switch
    {
        PetActivity.WakingUp => Rest with { Eye = 0.3, Mouth = 0.3 },
        PetActivity.Drowsy => Dozy,
        PetActivity.Eating => Rest with { Prop = PetProp.Crumb, Mouth = 0.5 },
        PetActivity.Working => Rest with { Prop = PetProp.Laptop, LookY = 0.55, Eye = 0.5 },
        PetActivity.Sweeping => Rest with { Prop = PetProp.Broom, Sway = -1 },
        _ => base.Still(activity),
    };

    /// Lo mira despacio (`LookSpeed` bajo) y sin abrir más los ojos.
    public override PetPose Hover(PetPose pose, double hoverX, double hoverY, double seconds) =>
        pose with { Eye = Math.Min(pose.Eye, 0.5), Blush = Math.Max(pose.Blush, 0.15) };

    public override PetPose Walk(double distance, int facing, double lean, bool turning)
    {
        double phase = distance * Math.PI / 4;
        return Rest with
        {
            Feet = turning ? PetFeet.Stand : Math.Sin(phase) > 0 ? PetFeet.StepLeft : PetFeet.StepRight,
            Bob = turning ? 0 : -Math.Abs(Math.Sin(phase)) * 0.15, Lean = lean, LookX = facing * 0.7,
            EarL = Math.Sin(phase) * 0.3, EarR = -Math.Sin(phase) * 0.3,
        };
    }

    public override (double Dx, double Dy) Motion(PetActivity activity, double seconds) => activity switch
    {
        PetActivity.Downloading => (0, -Math.Abs(Math.Sin(Math.PI * seconds / 0.7)) * 0.25),
        PetActivity.Sweeping => (Math.Sin(Math.PI * seconds / 1.3) * 0.3, 0),
        _ => (0, 0),
    };

    public override (double Dx, double Dy) Motion(PetReaction reaction, double seconds) =>
        reaction == PetReaction.Hearts ? (Math.Sin(Math.PI * Math.Min(seconds, 2.2) / 1.1) * 0.2, 0) : (0, 0);

    public override double Breath(PetActivity activity, double seconds) =>
        activity is PetActivity.Alert or PetActivity.Drowsy or PetActivity.Working or PetActivity.Eating
            ? 0.08 * Math.Sin(2 * Math.PI * seconds / 4.5) : 0;
}
