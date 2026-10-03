namespace IsTargetSleeping;

/// El gatito naranja: juguetón y caprichoso. Persigue el cursor agachado y se lanza si
/// se queda quieto; al clic se eriza, da un zarpazo y queda satisfecho. Se sienta encima
/// del portátil y dentro de la caja, amasa, se acicala, agita la cola y duerme enroscado.
/// Un gato no barre: para limpiar se lava a lametones. Dormido, reacciona sin desenroscarse.
public sealed class CatProfile : ProfileKit
{
    protected override PetPose Rest => new(ArmL: PetArm.Down, ArmR: PetArm.Down, Pupil: 0.3, Tail: 0.3);
    protected override PetPose Asleep => Rest with
    {
        Eye = 0, Sit = 1, Squash = 0.5, Curl = 1, BedIn = 1, Tint = PetTint.Sleepy, EarL = -0.3, EarR = -0.3, Tail = -1,
    };
    /// Hecho un pan, con los ojos de rendija.
    private PetPose Loaf => Rest with { Eye = 0.3, Sit = 0.5, Squash = 0.2 };
    public override double WalkSpeed => 18;
    public override double MinWait => 4;
    public override double MaxWait => 9;
    public override double LookSpeed => 26;
    public override bool PouncesOnCursor => true;

    public CatProfile()
    {
        var r = Rest;
        var d = Loaf;
        Activities[PetActivity.DeepSleep] = Loop(3.2, K(0, Asleep), K(1.4, Asleep with { Squash = 0.7 }), K(2.2, Asleep with { Tail = -0.75 }));
        // Mientras Ollama arranca, ya despierto (el desperezo es su transición desde la cesta):
        // se lava la cara con la pata, mira a un lado y a otro y se le escapa un bostezo.
        var waking = r with { Eye = 0.45, Tail = 0.5 };
        Activities[PetActivity.WakingUp] = Loop(4.2, K(0, waking), K(0.5, waking with { ArmR = PetArm.Rub, Eye = 0.3, Mouth = 0.3 }),
            K(0.8, waking with { ArmR = -110, EarR = -0.7, Eye = 0.3 }), K(1.1, waking with { ArmR = PetArm.Rub, Eye = 0.3, Mouth = 0.2 }),
            K(1.4, waking with { ArmR = -110, EarR = -0.7, Eye = 0.3 }), K(1.8, waking),
            K(2.3, waking with { LookX = 0.7, Pupil = 0.6, EarL = 0.8, Tail = 0.9 }), K(2.9, waking with { LookX = -0.7, Pupil = 0.6, Tail = 0.2 }),
            K(3.3, waking with { Eye = 0.15, Mouth = 0.8, EarL = -0.4, EarR = -0.4 }), K(3.8, waking));
        Activities[PetActivity.Drowsy] = Loop(5.4, K(0, d), K(1.6, d with { Eye = 0, Bob = 0.4, EarL = -0.3 }),
            K(2.2, d with { Eye = 0.4, Tail = 0.6 }, Ease.Out), K(3.2, d with { Tail = -0.2 }), K(4.2, d with { LookX = -0.5, EarL = 0.6 }), K(4.8, d));
        // Despierta: la punta de la cola y las orejas no paran; mira rápido a un lado y al otro.
        Activities[PetActivity.Alert] = Loop(4.5, K(0, r), K(0.6, r with { Tail = 0.9, EarL = 0.8 }), K(0.9, r with { Tail = 0.1 }),
            K(1.4, r with { LookX = -0.9, EarL = 1, Pupil = 0.5 }), K(2.0, r with { LookX = -0.9, Eye = 0 }), K(2.15, r with { LookX = -0.9 }),
            K(2.8, r with { LookX = 0.8, EarR = 1, Tail = -0.4 }), K(3.4, r with { LookX = 0.8, Tail = 0.6 }), K(4.0, r));
        var eat = r with { Prop = PetProp.Crumb, ArmR = PetArm.Rub, LookY = 0.3, EarL = 0.8 };
        Activities[PetActivity.Eating] = Loop(0.7, K(0, eat with { Mouth = 0.1 }), K(0.17, eat with { Mouth = 0.7, Squash = 0.2 }),
            K(0.35, eat), K(0.52, eat with { Mouth = 0.5, Tail = 0.7 }));
        // Sentado encima del portátil, amasando el teclado.
        var work = r with { Prop = PetProp.Laptop, LookY = 0.5, Eye = 0.5, Sit = 0.2 };
        Activities[PetActivity.Working] = Loop(1.2, K(0, work with { ArmL = 70, ArmR = 35, Tail = 0.6 }),
            K(0.3, work with { ArmL = 35, ArmR = 70, Tail = 0.2 }), K(0.6, work with { ArmL = 70, ArmR = 35, Eye = 0.3, Tail = 0.7 }),
            K(0.9, work with { ArmL = 35, ArmR = 70, Tail = 0.3 }));
        // Dentro de la caja, con las pupilas grandes, vigilando.
        var box = r with { Prop = PetProp.Box, Sit = 0.6, Pupil = 1, EarL = 0.8, EarR = 0.8 };
        Activities[PetActivity.Downloading] = Loop(1.6, K(0, box with { LookX = -0.8 }), K(0.5, box with { LookX = -0.8, Tail = 0.9 }),
            K(0.8, box with { LookX = 0.8 }), K(1.3, box with { LookX = 0.8, EarL = -0.2 }));
        // Se lava: lame la pata, se la pasa por la cara y la oreja, y luego se lame el costado.
        var wash = Washing;
        var flank = r with { Sit = 0.4, Eye = 0.2, Lean = -0.4, LookX = -0.8, LookY = 0.5, Tail = 0.6 };
        Activities[PetActivity.Sweeping] = Loop(3.2, K(0, wash with { Mouth = 0.3 }), K(0.2, wash with { Mouth = 0.15 }),
            K(0.4, wash with { Mouth = 0.3, Tail = 0.5 }), K(0.6, wash with { Mouth = 0.15 }), K(0.8, wash with { Mouth = 0.3, Tail = 0.1 }),
            K(1.2, wash with { ArmR = -110, EarR = -0.7 }), K(1.45, wash with { EarR = -0.7 }), K(1.7, wash with { ArmR = -110, EarR = -0.7 }),
            K(2.1, flank with { Mouth = 0.3 }), K(2.35, flank with { Mouth = 0.1, Tail = 0.9 }), K(2.6, flank with { Mouth = 0.3 }),
            K(2.85, flank with { Mouth = 0.1, Tail = 0.4 }));
        Activities[PetActivity.Yawning] = Once(K(0, d),
            K(0.7, r with { Eye = 0, Mouth = 1, Stretch = 0.8, Tail = 0.8, EarL = -0.5, EarR = -0.5 }, Ease.Out),
            K(1.4, r with { Eye = 0, Mouth = 0.3, Sit = 0.6, Curl = 0.5, BedIn = 0.4 }), K(2.4, Asleep));

        // Clic: se eriza (lomo arqueado, pelo de punta, bufido), da un zarpazo y se queda tan satisfecho.
        var puffed = r with { Arch = 1, EarL = -0.9, EarR = -0.9, Pupil = 1, Squash = -0.2, Tail = 1, Mouth = 0.7 };
        Reactions[PetReaction.Jump] = Once(K(0, r), K(0.12, puffed, Ease.Out), K(0.4, puffed with { Arch = 0.9, Squash = 0 }),
            K(0.58, r with { Arch = 0.5, ArmR = -60, Pupil = 1, EarL = 0.2, EarR = 0.2, Mouth = 0.1 }),
            K(0.7, r with { Arch = 0.3, ArmR = -5, Pupil = 1, Lean = 0.4 }, Ease.Out), K(0.88, r with { Pupil = 0.6 }),
            K(1.1, r with { Eye = 0, Blush = 0.35, Tail = 0.7 }), K(1.4, r));
        Reactions[PetReaction.Hearts] = Once(K(0, r), K(0.3, r with { Eye = 0, Blush = 0.9, Tail = 1, EarL = 0.5, EarR = 0.5, Squash = 0.2 }),
            K(0.7, r with { Eye = 0, Blush = 0.8, ArmR = PetArm.Rub, Tail = -0.4 }), K(1.1, r with { Eye = 0, Blush = 0.7, Tail = 0.9 }), K(1.6, r));
        Reactions[PetReaction.Sparkle] = Once(K(0, r), K(0.25, r with { Squash = 0.5, Pupil = 1, Tail = 0.9 }),
            K(0.5, r with { Squash = -0.5, ArmL = PetArm.Up, ArmR = PetArm.Up, EarL = 0.8, EarR = 0.8, Pupil = 1 }, Ease.Out),
            K(0.85, r with { Squash = 0.3, Blush = 0.3 }), K(1.15, r));
        Reactions[PetReaction.Dizzy] = Once(K(0, r), K(0.3, r with { Eye = 0.4, LookX = -1, EarL = -0.8, EarR = 0.5, Tail = -1, Pupil = 1 }),
            K(0.6, r with { Eye = 0.4, LookX = 1, EarL = 0.5, EarR = -0.8, Tail = 1, Pupil = 1 }),
            K(0.9, r with { Eye = 0.4, LookX = -1, Tail = -1, Pupil = 1 }), K(1.2, r with { Eye = 0.5, LookX = 0.6 }), K(1.6, r));
        var sulk = r with { Eye = 0.4, LookY = 0.6, Sit = 0.6, EarL = -1, EarR = -1, Tail = -1, Pupil = 1 };
        Reactions[PetReaction.Sad] = Once(K(0, r), K(0.5, sulk), K(1.8, sulk with { Eye = 0.45 }), K(2.2, r));
        Reactions[PetReaction.Nap] = Once(K(0, r with { Eye = 0.5 }), K(0.5, r with { Eye = 0, Mouth = 0.9, Stretch = 0.7 }),
            K(1.2, Asleep with { BedIn = 0.6, Curl = 0.6 }), K(1.9, Asleep));

        // Dormido en la cesta: una oreja, la cola, un ojo de rendija… y sigue enroscado.
        var a = Asleep;
        InBed(PetReaction.Jump, Once(K(0, a), K(0.12, a with { EarL = 0.8 }, Ease.Out), K(0.3, a with { EarL = -0.3, Eye = 0.25, Pupil = 0 }),
            K(0.6, a with { Tail = -0.4, Eye = 0.25, Pupil = 0 }), K(0.9, a with { Eye = 0.1 }), K(1.2, a)));
        InBed(PetReaction.Hearts, Once(K(0, a), K(0.3, a with { Blush = 0.7, Squash = 0.65 }), K(1.3, a with { Blush = 0.7, Squash = 0.55, Tail = -0.6 }),
            K(1.7, a)), D((ParticleKind.Purr, 0.3), (ParticleKind.Heart, 0.5)));
        InBed(PetReaction.Sparkle, Once(K(0, a), K(0.4, a with { Eye = 0.2, Blush = 0.3 }), K(1.0, a with { Eye = 0.2 }), K(1.4, a)),
            D((ParticleKind.Sparkle, 0.1)));
        var tight = a with { EarL = -1, EarR = -1, Squash = 0.75 };
        InBed(PetReaction.Dizzy, Once(K(0, a), K(0.4, tight with { Eye = 0.2, Tail = -0.6 }), K(0.8, tight with { Tail = -1 }),
            K(1.2, tight with { Eye = 0.2, Tail = -0.6 }), K(1.6, a)), burst: [(ParticleKind.Star, 3)]);
        InBed(PetReaction.Sad, Once(K(0, a), K(0.5, tight), K(1.8, tight with { Squash = 0.7 }), K(2.2, a)), D((ParticleKind.Tear, 0.7)));

        // Se lame la pata (la lengua asoma) y se la pasa por la oreja.
        var groom = r with { ArmR = PetArm.Rub, Eye = 0.25, Sit = 0.4, LookY = 0.2 };
        Gestures[PetGesture.Groom] = Once(K(0, r), K(0.4, groom with { Mouth = 0.3 }), K(0.6, groom with { Mouth = 0.15 }),
            K(0.8, groom with { Mouth = 0.3 }), K(1.0, groom with { Mouth = 0.15 }), K(1.2, groom with { Mouth = 0.3 }),
            K(1.6, groom with { ArmR = -110, EarR = -0.7 }), K(1.9, groom with { EarR = -0.7 }), K(2.2, groom with { ArmR = -110, EarR = -0.7 }), K(2.6, r));
        // Amasa con los ojos cerrados de gusto (el ronroneo, en sus partículas).
        var knead = r with { Eye = 0.2, Blush = 0.3, Sit = 0.3, Squash = 0.2, Tail = 0.5 };
        Gestures[PetGesture.Knead] = Once(K(0, r), K(0.3, knead with { ArmL = 70, ArmR = 30 }), K(0.6, knead with { ArmL = 30, ArmR = 70 }),
            K(0.9, knead with { ArmL = 70, ArmR = 30 }), K(1.2, knead with { ArmL = 30, ArmR = 70 }), K(1.5, knead with { ArmL = 70, ArmR = 30 }),
            K(1.8, knead with { ArmL = 30, ArmR = 70 }), K(2.1, knead with { ArmL = 70, ArmR = 30 }), K(2.4, knead), K(2.8, r));
        Gestures[PetGesture.TailSwish] = Once(K(0, r), K(0.25, r with { Tail = -1, LookX = 0.4, EarL = 0.6 }), K(0.5, r with { Tail = 1, LookX = 0.4 }),
            K(0.75, r with { Tail = -1, LookX = 0.4 }), K(1.0, r with { Tail = 1, LookX = 0.4 }), K(1.25, r with { Tail = -0.6 }), K(1.6, r));
        // Salto de caza: se agacha, menea el trasero y se lanza (el salto lo hace `Motion`).
        var crouch = r with { Squash = 0.6, Pupil = 1, LookX = 0.9, EarL = 0.9, EarR = 0.9, Tail = 0.5 };
        Gestures[PetGesture.Hop] = Once(K(0, r), K(0.35, crouch), K(0.5, crouch with { Lean = 0.3, Tail = 0.2 }),
            K(0.65, crouch with { Lean = -0.3 }), K(0.8, crouch with { Lean = 0.3, Tail = 0.2 }), K(0.95, crouch with { Squash = 0.7 }),
            K(1.15, r with { Squash = -0.5, ArmL = PetArm.Up, ArmR = PetArm.Up, Pupil = 1 }, Ease.Out), K(1.4, r with { Squash = 0.4 }), K(1.7, r));
        var stretch = r with { Stretch = 1, ArmL = -10, ArmR = -10, Squash = 0.4, Tail = 1, Eye = 0.3, Mouth = 0.5 };
        Gestures[PetGesture.Stretch] = Once(K(0, r), K(0.6, stretch, Ease.Out), K(1.4, stretch with { Mouth = 0.2 }), K(2, r));
        Gestures[PetGesture.Nod] = Once(K(0, d), K(0.9, d with { Eye = 0, Bob = 0.6 }, Ease.In),
            K(1.1, d with { Eye = 0.6, Bob = -0.2, EarL = 0.6 }, Ease.Out), K(1.4, d with { LookX = -0.4 }), K(2, d));
        Gestures[PetGesture.Yawn] = Once(K(0, r), K(0.5, r with { Eye = 0, Mouth = 1, EarL = -0.6, EarR = -0.6, Squash = -0.2 }),
            K(1.1, r with { Eye = 0.2, Mouth = 0.4 }), K(1.6, r));
        AwakeGestures = [(PetGesture.Groom, 3), (PetGesture.TailSwish, 3), (PetGesture.Hop, 3), (PetGesture.Knead, 2), (PetGesture.Stretch, 2)];
        DrowsyGestures = [(PetGesture.Nod, 3), (PetGesture.Yawn, 3), (PetGesture.Groom, 2), (PetGesture.Knead, 2)];

        // Esperando un modelo: tras el logo asoman primero las orejas, luego los ojos por
        // la izquierda, mientras la cola se le escapa por el otro lado.
        PeekStill = r with { Behind = true, PeekY = -2.5, Sit = 1, Squash = 0.6, EarL = 0.2, EarR = 0.2, Tail = 0.8, OneArm = true, Pupil = 0.8 };
        var ears = PeekStill with { PeekY = 0, EarL = 0.9, EarR = 0.9 };
        var eyes = ears with { Peek = 3, LookX = -1, Pupil = 1 };
        Peek = Loop(8, K(0, PeekStill), K(1.0, PeekStill), K(1.8, ears), K(2.4, ears with { EarL = -0.2 }), K(2.8, ears),
            K(3.6, eyes), K(4.6, eyes with { Tail = -0.6 }), K(4.9, eyes), K(5.1, eyes with { Eye = 0 }), K(5.25, eyes),
            K(5.8, PeekStill with { Blush = 0.4 }, Ease.In), K(7.2, PeekStill with { Blush = 0.3 }), K(8, PeekStill));

        ActivityDrips[PetActivity.DeepSleep] = D((ParticleKind.Z, 1.8));
        ActivityDrips[PetActivity.Working] = D((ParticleKind.Purr, 0.7));
        ActivityDrips[PetActivity.Eating] = D((ParticleKind.Sparkle, 0.4));
        ActivityDrips[PetActivity.Sweeping] = D((ParticleKind.Sparkle, 0.8));
        ReactionDrips[PetReaction.Hearts] = D((ParticleKind.Heart, 0.25), (ParticleKind.Purr, 0.3));
        ReactionDrips[PetReaction.Sad] = D((ParticleKind.Tear, 0.6));
        ReactionDrips[PetReaction.Sparkle] = D((ParticleKind.Sparkle, 0.07));
        GestureDrips[PetGesture.Knead] = D((ParticleKind.Purr, 0.45));
        Bursts[PetReaction.Jump] = [(ParticleKind.Dust, 3)];
        Bursts[PetReaction.Dizzy] = [(ParticleKind.Star, 3)];
        Complete();
    }

    public override PetPose Still(PetActivity activity) => activity switch
    {
        PetActivity.WakingUp => Rest with { Eye = 0.4, Stretch = 0.6, Mouth = 0.5 },
        PetActivity.Drowsy => Loaf,
        PetActivity.Eating => Rest with { Prop = PetProp.Crumb, ArmR = PetArm.Rub, Mouth = 0.5 },
        PetActivity.Working => Rest with { Prop = PetProp.Laptop, ArmL = 70, ArmR = 35, LookY = 0.5, Eye = 0.5, Sit = 0.2 },
        PetActivity.Downloading => Rest with { Prop = PetProp.Box, Sit = 0.6, Pupil = 1, EarL = 0.8, EarR = 0.8 },
        PetActivity.Sweeping => Washing with { Mouth = 0.3 },
        _ => base.Still(activity),
    };

    /// Lavándose: sentado, con la pata en la boca y los ojos entornados.
    private PetPose Washing => Rest with { ArmR = PetArm.Rub, Eye = 0.25, Sit = 0.4, LookY = 0.2 };

    /// Si dormía, se despereza saliendo de la cesta (patas delante, bostezo) antes de lavarse
    /// o mientras Ollama arranca; y al despertar, además se frota la cara una vez.
    public override PetClip? Transition(PetActivity from, PetActivity to, bool fromHiding, bool toHiding)
    {
        if (fromHiding || toHiding || to is not (PetActivity.Sweeping or PetActivity.WakingUp) || !from.InBed())
            return base.Transition(from, to, fromHiding, toHiding);
        var r = Rest;
        var after = For(new PetContext(to, from)).At(0);
        var stretch = new List<PetKey>
        {
            K(0, Asleep), K(0.4, Asleep with { Eye = 0.2 }),
            K(1.0, r with { Stretch = 1, ArmL = -10, ArmR = -10, Squash = 0.3, Mouth = 1, Tail = 1, BedIn = 0.4, Eye = 0.1 }, Ease.Out),
            K(1.6, r with { Stretch = 0.5, Mouth = 0.2, Eye = 0.3 }),
        };
        if (to == PetActivity.WakingUp)
            stretch.AddRange([K(2.0, r with { Eye = 0.3, ArmR = PetArm.Rub, Mouth = 0.3 }), K(2.4, r with { Eye = 0.3, ArmR = -110 }),
                K(2.8, r with { Eye = 0.5, LookX = 0.6, Pupil = 0.6 }), K(3.3, after)]);
        else stretch.Add(K(2.0, after));
        return Once([.. stretch]);
    }

    /// Se agacha, dilata las pupilas, levanta las orejas y la punta de la cola no para.
    public override PetPose Hover(PetPose pose, double hoverX, double hoverY, double seconds) => pose with
    {
        Squash = Math.Max(pose.Squash, 0.45), Pupil = 1, EarL = 0.9, EarR = 0.9, Tail = 0.4 + 0.35 * Math.Sin(seconds * 14),
    };

    /// Al acecho: bajito, con la cola en alto.
    public override PetPose Walk(double distance, int facing, double lean, bool turning)
    {
        double phase = distance * Math.PI / 2.5;
        return Rest with
        {
            Feet = turning ? PetFeet.Stand : Math.Sin(phase) > 0 ? PetFeet.StepLeft : PetFeet.StepRight,
            Bob = turning ? 0 : -Math.Abs(Math.Sin(phase)) * 0.35, Lean = lean, LookX = facing * 0.7, Squash = 0.15,
            EarL = 0.5, EarR = 0.5, Tail = 0.6 + 0.3 * Math.Sin(phase * 0.5),
        };
    }

    public override (double Dx, double Dy) Motion(PetActivity activity, double seconds) => activity switch
    {
        PetActivity.Downloading => (Math.Sin(2 * Math.PI * seconds / 0.8) * 0.4, 0),   // la caja se tambalea
        _ => (0, 0),
    };

    public override (double Dx, double Dy) Motion(PetReaction reaction, double seconds)
    {
        double t = Math.Max(0, seconds);
        return reaction switch
        {
            // Bote al erizarse y un pasito adelante con el zarpazo.
            PetReaction.Jump => (t is > 0.58 and < 0.88 ? 0.8 * Math.Sin(Math.PI * (t - 0.58) / 0.3) : 0, Arc(t, 0.05, 0.4, 2.5)),
            PetReaction.Sparkle => (0, Arc(t, 0.3, 0.8, 3)),
            PetReaction.Hearts => (Math.Sin(2 * Math.PI * t / 0.8) * 0.4, 0),
            PetReaction.Dizzy => (Math.Sin(2 * Math.PI * t / 0.4) * Math.Max(0, 1 - t / 1.6), 0),
            _ => (0, 0),
        };
    }

    public override (double Dx, double Dy) Motion(PetGesture gesture, double seconds) => gesture == PetGesture.Hop
        ? (seconds is > 1.0 and < 1.7 ? 1.5 * Math.Sin(Math.PI * (seconds - 1.0) / 0.7) : 0, Arc(seconds, 1.0, 1.45, 4))
        : (0, 0);

    public override double Breath(PetActivity activity, double seconds) =>
        activity is PetActivity.Alert or PetActivity.Drowsy or PetActivity.Working or PetActivity.Eating
            ? 0.12 * Math.Sin(2 * Math.PI * seconds / 2.2) : 0;
}
