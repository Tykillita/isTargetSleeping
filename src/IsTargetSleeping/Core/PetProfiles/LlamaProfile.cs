namespace IsTargetSleeping;

/// La llama: curiosa, orgullosa y algo dramática. Va con la cabeza alta, estira el cuello
/// hacia todo (también hacia el cursor), rumia de lado, lleva la caja en el lomo como
/// animal de carga, tararea cuando está contenta y resopla «¡pff!» si la molestas.
/// Es alta: para esconderse asoma la cabeza por encima del logo, como un periscopio.
public sealed class LlamaProfile : ProfileKit
{
    protected override PetPose Rest => new(ArmL: PetArm.Down, ArmR: PetArm.Down, Stretch: 0.3);
    protected override PetPose Asleep => Rest with
    {
        Eye = 0, Sit = 1, Squash = 0.5, BedIn = 1, Tint = PetTint.Sleepy, EarL = -0.6, EarR = -0.6, Stretch = -0.5,
    };
    private PetPose Dozy => Rest with { Eye = 0.4, Sit = 0.15, Stretch = 0 };
    public override double WalkSpeed => 14;
    public override double MinWait => 6;
    public override double MaxWait => 12;
    public override double LookSpeed => 12;

    public LlamaProfile()
    {
        var r = Rest;
        var d = Dozy;
        Activities[PetActivity.DeepSleep] = Loop(4.0, K(0, Asleep), K(1.7, Asleep with { Squash = 0.75, EarL = -0.45 }));
        // Se despierta estirando el cuello todo lo que da, bosteza y rumia un poco.
        Activities[PetActivity.WakingUp] = Loop(4.4, K(0, d with { Eye = 0 }),
            K(0.9, r with { Eye = 0.1, Stretch = 1.1, Mouth = 0.9, EarL = 0.8, EarR = 0.8, LookY = -0.6 }, Ease.Out),
            K(1.6, r with { Eye = 0.2, Stretch = 1, Mouth = 0.5 }), K(2.2, r with { Eye = 0.35, Jaw = 0.8 }),
            K(2.6, r with { Eye = 0.35, Jaw = -0.8 }), K(3.0, r with { Eye = 0.5, LookX = 0.6 }), K(3.9, d));
        // Cabecea y se despierta de golpe, ofendida; luego rumia.
        Activities[PetActivity.Drowsy] = Loop(6.4, K(0, d),
            K(1.8, d with { Eye = 0, Bob = 0.6, Stretch = -0.4, EarL = -0.4, EarR = -0.4 }, Ease.In),
            K(2.5, d with { Eye = 0.8, Bob = -0.2, Stretch = 0.2 }, Ease.Out), K(3.2, d),
            K(4.0, d with { Jaw = 0.7 }), K(4.4, d with { Jaw = -0.7 }), K(4.8, d), K(5.6, d with { LookX = -0.5 }));
        // Orgullosa: cuello alto, mira a un lado, rumia, mira al otro y parpadea.
        var proud = r with { Stretch = 0.6 };
        Activities[PetActivity.Alert] = Loop(5.4, K(0, r),
            K(0.8, proud with { LookX = -0.8, EarL = 0.8, EarR = 0.3 }), K(1.6, proud with { LookX = -0.8, EarL = 0.8, Jaw = 0.9 }),
            K(1.9, proud with { LookX = -0.8, EarL = 0.8, Jaw = -0.9 }), K(2.2, proud with { LookX = -0.8, EarL = 0.8, Jaw = 0.9 }),
            K(2.5, proud with { LookX = -0.6, EarL = 0.6 }), K(3.3, proud with { LookX = 0.8, EarR = 0.8, EarL = 0.3 }),
            K(3.45, proud with { LookX = 0.8, EarR = 0.8, Eye = 0 }), K(3.6, proud with { LookX = 0.8, EarR = 0.8 }), K(4.5, r));
        // Mastica de lado a lado.
        var eat = r with { Prop = PetProp.Crumb, ArmR = PetArm.Rub, LookY = 0.3 };
        Activities[PetActivity.Eating] = Loop(1.0, K(0, eat with { Jaw = -1, Mouth = 0.3 }), K(0.25, eat with { Jaw = 1, Mouth = 0.1 }),
            K(0.5, eat with { Jaw = -1, Mouth = 0.3 }), K(0.75, eat with { Jaw = 1, Mouth = 0.1, EarL = 0.4 }));
        var work = r with { Prop = PetProp.Laptop, LookY = 0.55, Stretch = 0.1 };
        Activities[PetActivity.Working] = Loop(0.9, K(0, work with { ArmL = 78, ArmR = 45 }),
            K(0.225, work with { ArmL = 45, ArmR = 78, EarR = 0.4 }), K(0.45, work with { ArmL = 78, ArmR = 45, Jaw = 0.5 }),
            K(0.675, work with { ArmL = 45, ArmR = 78, Jaw = -0.5 }));
        // La caja va en el lomo, con su manta; trota en el sitio con la cabeza alta.
        var load = r with { Prop = PetProp.Saddle, Stretch = 0.7 };
        Activities[PetActivity.Downloading] = Loop(0.7, K(0, load with { Feet = PetFeet.StepLeft, EarL = -0.3, EarR = 0.3 }),
            K(0.175, load), K(0.35, load with { Feet = PetFeet.StepRight, EarL = 0.3, EarR = -0.3, Squash = 0.15 }), K(0.525, load));
        var broom = r with { Prop = PetProp.Broom };
        Activities[PetActivity.Sweeping] = Loop(1.8, K(0, broom with { Sway = -1, LookX = -0.6, ArmR = 65, Lean = -0.2 }),
            K(0.9, broom with { Sway = 1, LookX = 0.6, ArmR = 30, Lean = 0.2 }));
        Activities[PetActivity.Yawning] = Once(K(0, d),
            K(0.8, r with { Eye = 0, Mouth = 1, Stretch = 1.2, LookY = -0.7, EarL = -0.5, EarR = -0.5 }, Ease.Out),
            K(1.5, r with { Eye = 0, Mouth = 0.4, Sit = 0.5, BedIn = 0.4, Stretch = 0 }), K(2.6, Asleep));

        // Clic: resopla con las orejas atrás y la cabeza hacia atrás (la nubecita, en la ráfaga).
        Reactions[PetReaction.Jump] = Once(K(0, r),
            K(0.15, r with { Stretch = -0.3, EarL = -1, EarR = -1, Eye = 0.4, Squash = 0.25 }, Ease.Out),
            K(0.3, r with { Stretch = 0.5, Mouth = 0.45, EarL = -1, EarR = -1, Eye = 0.3, LookX = -0.4 }, Ease.Out),
            K(0.65, r with { Stretch = 0.5, Mouth = 0.1, EarL = -0.8, EarR = -0.8, Eye = 0.5, LookX = -0.4 }), K(0.95, r));
        Reactions[PetReaction.Hearts] = Once(K(0, r),
            K(0.3, r with { Eye = 0, Blush = 0.9, EarL = 0.6, EarR = 0.6, Stretch = 0.8, LookY = -0.3 }),
            K(0.7, r with { Eye = 0, Blush = 0.8, Stretch = 0.9, Lean = 0.3 }), K(1.1, r with { Eye = 0, Blush = 0.8, Stretch = 0.9, Lean = -0.3 }),
            K(1.5, r with { Eye = 0.1, Blush = 0.6, Stretch = 0.8 }), K(1.9, r));
        // Orgullosísima: el cuello estirado, los ojos entornados de satisfacción.
        Reactions[PetReaction.Sparkle] = Once(K(0, r),
            K(0.35, r with { Stretch = 1.1, Eye = 0.2, EarL = 0.9, EarR = 0.9, LookY = -0.5, Blush = 0.3 }, Ease.Out),
            K(0.9, r with { Stretch = 1.1, Eye = 0.2, EarL = 0.9, EarR = 0.9, LookY = -0.5, Blush = 0.3 }), K(1.3, r));
        Reactions[PetReaction.Dizzy] = Once(K(0, r),
            K(0.35, r with { Eye = 0.4, Lean = -0.6, LookX = -1, EarL = -0.8, EarR = 0.6, Stretch = -0.2 }),
            K(0.7, r with { Eye = 0.4, Lean = 0.6, LookX = 1, EarL = 0.6, EarR = -0.8 }), K(1.05, r with { Eye = 0.4, Lean = -0.5, LookX = -0.8 }),
            K(1.4, r with { Eye = 0.5, Lean = 0.3 }), K(1.9, r));
        // Dramática: se derrumba con el cuello por el suelo.
        var drama = r with { Eye = 0.3, LookY = 0.7, Sit = 0.5, Stretch = -1, EarL = -1, EarR = -1 };
        Reactions[PetReaction.Sad] = Once(K(0, r), K(0.5, drama), K(1.9, drama with { Eye = 0.35, EarL = -0.9, EarR = -0.9 }), K(2.4, r));
        Reactions[PetReaction.Nap] = Once(K(0, r with { Eye = 0.5 }), K(0.6, r with { Eye = 0, Mouth = 0.8, Stretch = 1 }),
            K(1.3, Asleep with { BedIn = 0.6 }), K(2, Asleep));

        Gestures[PetGesture.Chew] = Once(K(0, r), K(0.3, r with { Jaw = 1, Eye = 0.7, LookX = 0.3 }),
            K(0.6, r with { Jaw = -1, Eye = 0.7, LookX = 0.3 }), K(0.9, r with { Jaw = 1, Eye = 0.7, LookX = 0.3 }),
            K(1.2, r with { Jaw = -1, Eye = 0.7, LookX = 0.3 }), K(1.5, r with { Jaw = 1, Eye = 0.7, LookX = 0.3 }),
            K(1.8, r with { Jaw = -1, Eye = 0.7, LookX = 0.3 }), K(2.1, r with { Eye = 0.7 }), K(2.5, r));
        Gestures[PetGesture.EarFlick] = Once(K(0, r), K(0.15, r with { EarL = -0.9 }), K(0.3, r with { EarL = 0.7 }), K(0.45, r),
            K(0.8, r with { EarR = -0.9 }), K(0.95, r with { EarR = 0.7 }), K(1.1, r), K(1.4, r));
        // Tararea con los ojos cerrados, meciendo la cabeza (las notas, en sus partículas).
        var hum = r with { Eye = 0, Mouth = 0.2, Blush = 0.3 };
        Gestures[PetGesture.Hum] = Once(K(0, r), K(0.4, hum with { Lean = -0.4 }), K(1.0, hum with { Lean = 0.4 }),
            K(1.6, hum with { Lean = -0.4 }), K(2.2, hum with { Lean = 0.4 }), K(2.8, r with { Eye = 0.2 }), K(3.2, r));
        var tall = r with { Stretch = 1.3, LookY = -0.8, EarL = 0.9, EarR = 0.9, Eye = 0.6 };
        Gestures[PetGesture.Stretch] = Once(K(0, r), K(0.7, tall, Ease.Out), K(1.5, tall with { Stretch = 1.2 }), K(2.2, r));
        Gestures[PetGesture.LookAround] = Once(K(0, r), K(0.6, r with { LookX = -1, EarL = 1, Stretch = 0.7, Lean = -0.3 }),
            K(1.5, r with { LookX = 1, EarR = 1, Stretch = 0.7, Lean = 0.3 }), K(2.4, r with { LookY = -0.6, EarL = 0.5, EarR = 0.5, Stretch = 0.9 }), K(3, r));
        Gestures[PetGesture.Nod] = Once(K(0, d), K(1, d with { Eye = 0, Bob = 0.8, Stretch = -0.5, EarL = -0.5, EarR = -0.5 }, Ease.In),
            K(1.5, d with { Eye = 0.9, Bob = -0.3, Stretch = 0.3 }, Ease.Out), K(2.6, d));
        Gestures[PetGesture.Yawn] = Once(K(0, r), K(0.6, r with { Eye = 0, Mouth = 1, Stretch = 1, EarL = -0.5, EarR = -0.5 }),
            K(1.3, r with { Eye = 0.3, Mouth = 0.5 }), K(2, r));
        AwakeGestures = [(PetGesture.Chew, 4), (PetGesture.LookAround, 3), (PetGesture.Stretch, 3), (PetGesture.EarFlick, 3), (PetGesture.Hum, 2)];
        DrowsyGestures = [(PetGesture.Nod, 4), (PetGesture.Yawn, 3), (PetGesture.Chew, 2), (PetGesture.EarFlick, 1)];

        // Esperando un modelo: agachada tras el logo, sube el cuello como un periscopio,
        // mira a los lados, se agacha de golpe y vuelve a asomar, ruborizada.
        PeekStill = r with { Behind = true, PeekY = -4, Sit = 1, Squash = 0.4, Stretch = -0.6, OneArm = true };
        var up = PeekStill with { PeekY = 3, Stretch = 1, EarL = 0.8, EarR = 0.8 };
        Peek = Loop(8, K(0, PeekStill), K(1.0, PeekStill), K(2.0, up, Ease.Out), K(2.6, up with { LookX = -1 }),
            K(3.4, up with { LookX = 1 }), K(3.55, up with { LookX = 1, Eye = 0 }), K(3.7, up with { LookX = 1 }), K(4.0, up with { LookX = 1 }),
            K(4.5, PeekStill with { Stretch = 0.2, Blush = 0.5 }, Ease.In), K(5.6, PeekStill with { Blush = 0.4 }),
            K(6.6, PeekStill with { PeekY = 1.5, Stretch = 0.8, LookX = -0.6, Blush = 0.3 }), K(7.4, PeekStill));

        ActivityDrips[PetActivity.DeepSleep] = D((ParticleKind.Z, 1.5), (ParticleKind.Note, 3.2));
        ActivityDrips[PetActivity.Working] = D((ParticleKind.Note, 1.6));   // tararea mientras trabaja
        ActivityDrips[PetActivity.Eating] = D((ParticleKind.Sparkle, 0.5));
        ActivityDrips[PetActivity.Sweeping] = D((ParticleKind.Dust, 0.15));
        ReactionDrips[PetReaction.Sad] = D((ParticleKind.Tear, 0.45));
        ReactionDrips[PetReaction.Hearts] = D((ParticleKind.Heart, 0.25), (ParticleKind.Note, 0.6));
        ReactionDrips[PetReaction.Sparkle] = D((ParticleKind.Sparkle, 0.08));
        GestureDrips[PetGesture.Hum] = D((ParticleKind.Note, 0.45));
        Bursts[PetReaction.Jump] = [(ParticleKind.Puff, 2)];
        Bursts[PetReaction.Dizzy] = [(ParticleKind.Star, 3)];
        Complete();
    }

    public override PetPose Still(PetActivity activity) => activity switch
    {
        PetActivity.WakingUp => Rest with { Eye = 0.5, Stretch = 1 },
        PetActivity.Drowsy => Dozy,
        PetActivity.Alert => Rest with { Stretch = 0.6 },
        PetActivity.Eating => Rest with { Prop = PetProp.Crumb, ArmR = PetArm.Rub, Jaw = 0.8, Mouth = 0.3 },
        PetActivity.Working => Rest with { Prop = PetProp.Laptop, ArmL = 78, ArmR = 78, LookY = 0.55 },
        PetActivity.Downloading => Rest with { Prop = PetProp.Saddle, Stretch = 0.7 },
        PetActivity.Sweeping => Rest with { Prop = PetProp.Broom, Sway = -1 },
        _ => base.Still(activity),
    };

    /// Estira el cuello hacia el cursor, se inclina hacia él y le orienta las orejas.
    public override PetPose Hover(PetPose pose, double hoverX, double hoverY, double seconds) => pose with
    {
        Stretch = Math.Max(pose.Stretch, 0.8 - 0.4 * hoverY), Lean = 0.7 * hoverX,
        EarL = 0.6 - 0.4 * hoverX, EarR = 0.6 + 0.4 * hoverX,
    };

    /// Trota con la cabeza alta.
    public override PetPose Walk(double distance, int facing, double lean, bool turning)
    {
        double phase = distance * Math.PI / 3;
        return Rest with
        {
            Feet = turning ? PetFeet.Stand : Math.Sin(phase) > 0 ? PetFeet.StepLeft : PetFeet.StepRight,
            Bob = turning ? 0 : -Math.Abs(Math.Sin(phase)) * 0.5, Lean = lean, LookX = facing * 0.7, Stretch = 0.6,
            EarL = Math.Sin(phase) * 0.3, EarR = -Math.Sin(phase) * 0.3,
        };
    }

    public override (double Dx, double Dy) Motion(PetActivity activity, double seconds) => activity switch
    {
        PetActivity.Downloading => (0, -Math.Abs(Math.Sin(Math.PI * seconds / 0.35)) * 0.8),
        PetActivity.Sweeping => (Math.Sin(Math.PI * seconds / 0.9) * 0.8, 0),
        _ => (0, 0),
    };

    public override (double Dx, double Dy) Motion(PetReaction reaction, double seconds)
    {
        double t = Math.Max(0, seconds);
        return reaction switch
        {
            PetReaction.Jump => (t is > 0.15 and < 0.6 ? -0.8 * Math.Sin(Math.PI * (t - 0.15) / 0.45) : 0, 0),   // da un respingo atrás
            PetReaction.Hearts => (0, -Math.Abs(Math.Sin(Math.PI * Math.Min(t, 1.6) / 0.4)) * 0.8),                // brinca contenta
            PetReaction.Sparkle => (0, -Math.Sin(Math.PI * Math.Min(t, 1.3) / 1.3) * 1.5),
            PetReaction.Dizzy => (Math.Sin(2 * Math.PI * t / 0.5) * Math.Max(0, 1 - t / 1.9), 0),
            _ => (0, 0),
        };
    }

    public override double Breath(PetActivity activity, double seconds) =>
        activity is PetActivity.Alert or PetActivity.Drowsy or PetActivity.Working or PetActivity.Eating
            ? 0.12 * Math.Sin(2 * Math.PI * seconds / 3) : 0;
}
