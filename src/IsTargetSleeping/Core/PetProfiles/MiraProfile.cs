namespace IsTargetSleeping;

/// Mira, vigilante y algo hacker: patrulla con el ojo como un radar, fija la retícula
/// sobre el cursor, su antena emite señales y al trabajar le suben bits. Es un robot: al
/// arrancar calibra el ojo y, para limpiar, escanea la barra con un haz y compacta los
/// datos sueltos en un cubito que se deshace en un destello. Al clic hace «ping».
/// Todo con anticipación y rebote: se agacha antes de saltar y aterriza aplastándose.
/// Dormida, reacciona sin salir de su cápsula.
public sealed class MiraProfile : ProfileKit
{
    protected override PetPose Rest => new();
    /// En la cápsula, tapada (el último fotograma del bostezo y la base de dormir).
    protected override PetPose Asleep => new(Eye: 0, Sit: 1, Squash: 0.55, ArmL: PetArm.Down, ArmR: PetArm.Down, BedIn: 1, Tint: PetTint.Sleepy);
    private static readonly PetPose DrowsyPose = new(Eye: 0.45, Squash: 0.15, ArmL: PetArm.Down, ArmR: PetArm.Down);
    public override double WalkSpeed => 12;
    public override double MinWait => 6;
    public override double MaxWait => 15;
    public override double LookSpeed => 16;

    public MiraProfile()
    {
        var r = Rest;
        var a = Asleep;
        var d = DrowsyPose;

        // Respira en su cápsula (inspira más rápido de lo que espira).
        Activities[PetActivity.DeepSleep] = Loop(3.6, K(0, a), K(1.3, a with { Squash = 0.95 }), K(2.2, a with { Squash = 0.85 }));
        // Arranca como un robot: al salir de la cápsula se encoge, se estira con un bostezo y se
        // frota el ojo (su transición, `wakeUp`); mientras Ollama arranca, ya despierta, la
        // retícula calibra, el ojo enfoca a un lado y al otro y a ratos se lo frota.
        PetPose waking(double eye) => Waking(eye);
        PetPose rub(double eye) => Rub(eye);
        wakeUp = [
            K(0, a), K(0.35, a with { Eye = 0.3 }), K(0.8, waking(0) with { Squash = 0.5, BedIn = 0.3 }),
            K(1.15, waking(0) with { Squash = 0.65, ArmL = 60, ArmR = 60 }),
            K(1.8, new(Eye: 0, ArmL: PetArm.Up, ArmR: PetArm.Up - 10, Squash: -0.85, Mouth: 1), Ease.OutBack),
            K(2.4, new(Eye: 0, ArmL: PetArm.Up - 5, ArmR: PetArm.Up, Squash: -0.8, Mouth: 0.7)),
            K(2.75, waking(0.1) with { Squash = 0.15 }),
            K(3.05, rub(0.35)), K(3.25, rub(0.05)), K(3.45, rub(0.45)), K(3.65, rub(0.1)), K(4.0, waking(0.6)),
        ];
        Activities[PetActivity.WakingUp] = Loop(4.0,
            K(0, waking(0.8)), K(0.3, waking(0.7) with { Lock = 0.8 }), K(0.55, waking(0.9) with { Lock = 1 }, Ease.OutBack),
            K(0.9, waking(0.9) with { Lock = 1, LookX = -0.7 }), K(1.3, waking(0.9) with { Lock = 0.6, LookX = 0.7 }),
            K(1.7, waking(0.8)), K(2.2, rub(0.45)), K(2.4, rub(0.15)), K(2.6, rub(0.5)), K(2.9, waking(0.7)), K(3.5, waking(0.8)));
        // Encendida sin modelo: sentada, cabecea hundiéndose, se sobresalta y mira alrededor.
        Activities[PetActivity.Drowsy] = Loop(7.0, K(0, d),
            K(1.6, d with { Bob = 0.5, Eye = 0.3, Squash = 0.3 }),
            K(2.3, d with { Bob = 1.0, Eye = 0, Squash = 0.4, ArmL = 65, ArmR = 65 }, Ease.In),
            K(2.45, d with { Bob = -0.4, Eye = 0.85, Squash = -0.3, ArmL = PetArm.Out, ArmR = PetArm.Out }, Ease.Out),   // ¡se despierta de golpe!
            K(2.9, d), K(4.0, d with { LookX = -0.7 }), K(4.6, d with { LookX = -0.7 }),
            K(5.2, d), K(5.6, d with { Eye = 0 }), K(5.9, d), K(6.4, d with { LookX = 0.6 }));
        // Patrulla: la pose del logo (bracitos en cruz), barridos de pupila con el cuerpo
        // detrás, parpadeos rápidos y, de vez en cuando, un barrido de radar.
        Activities[PetActivity.Alert] = Loop(7.2,
            K(0, r), K(1.8, r), K(1.95, r with { Eye = 0 }), K(2.1, r),
            K(2.6, r with { LookX = -0.85, Lean = -0.15 }), K(3.6, r with { LookX = -0.85, Lean = -0.15 }), K(4.0, r with { LookX = 0.1 }),
            K(4.5, r with { LookX = 0.85, Lean = 0.15 }), K(5.4, r with { LookX = 0.85, Lean = 0.15 }),
            K(5.55, r with { LookX = 0.85, Lean = 0.15, Eye = 0 }), K(5.7, r with { LookX = 0.85, Lean = 0.15 }),
            K(6.1, r), K(6.9, r with { Scan = 1 }, Ease.Linear), K(6.91, r, Ease.Step));
        // Se acerca el bocado de datos, le da un mordisco, lo mastica y lo saborea; la otra
        // mano, para equilibrarse.
        Activities[PetActivity.Eating] = Loop(1.2,
            K(0, Eating(0, 0, PetArm.Up)), K(0.15, Eating(0.2, 0.15, PetArm.Up + 15)), K(0.3, Eating(1, 0.35, PetArm.Up + 25)),
            K(0.42, Eating(0.1, -0.15, PetArm.Up + 15)), K(0.6, Eating(0.6, 0.2, PetArm.Up + 20)), K(0.72, Eating(0, -0.1, PetArm.Up + 10)),
            K(0.95, Eating(0, 0, PetArm.Up) with { Eye = 0.7, Blush = 0.3 }));
        // Teclea a seis teclas por segundo con las manos desfasadas, se para a leer la
        // pantalla y asiente.
        Activities[PetActivity.Working] = Loop(2.4, [
            .. Enumerable.Range(0, 9).Select(i => K(i / 6.0, Typing(i % 2 == 0) with { Squash = i % 2 == 0 ? 0.08 : 0 }, Ease.Out)),
            K(1.5, Typing(false) with { ArmL = 60, ArmR = 60, LookY = 0.3, Eye = 0.85 }),
            K(1.75, Typing(false) with { ArmL = 60, ArmR = 60, LookY = 0.6, Bob = 0.4, Eye = 0.85 }),
            K(1.95, Typing(false) with { ArmL = 60, ArmR = 60, LookY = 0.4 }),
            K(2.15, Typing(true)),
        ]);
        // La caja en la cabeza, sujeta con los dos brazos en alto, corriendo en el sitio.
        Activities[PetActivity.Downloading] = Loop(0.6,
            K(0, Box(PetFeet.StepLeft, 0.25, -70)), K(0.15, Box(PetFeet.Stand, -0.1, -78)),
            K(0.3, Box(PetFeet.StepRight, 0.25, -70)), K(0.45, Box(PetFeet.Stand, -0.1, -78)));
        // Escanea y compacta: el haz del ojo recorre la barra de un lado a otro (los
        // fragmentos vuelan hacia ella), aprieta lo recogido en un cubito y vuelve a escanear.
        Activities[PetActivity.Sweeping] = Loop(3.2,
            K(0, Scanning(-1)), K(0.7, Scanning(1)), K(1.4, Scanning(-0.2)),
            K(1.7, Compact(0.5, 0.2, 0.6, -20)), K(1.95, Compact(0.8, 0.45, 0.6, 20)),
            K(2.15, Compact(1, -0.2, 0.7, -25), Ease.OutBack), K(2.4, Compact(0.75, 0.45, 0.6, 20)),
            K(2.6, Compact(0.9, -0.1, 0.8, -20)), K(2.9, Scanning(-0.6) with { PropIn = 0.3 }));
        // Bosteza estirándose y se mete en la cápsula (se queda dentro).
        Activities[PetActivity.Yawning] = Once(
            K(0, waking(0.5)), K(0.3, waking(0) with { Mouth = 0.4, Squash = 0.2, ArmL = 60, ArmR = 60 }),
            K(0.95, new(Eye: 0, ArmL: PetArm.Up, ArmR: PetArm.Up - 10, Squash: -0.8, Mouth: 1), Ease.OutBack),
            K(1.35, new(Eye: 0, ArmL: PetArm.Up, ArmR: PetArm.Up - 10, Squash: -0.8, Mouth: 0.9)),
            K(1.65, waking(0) with { Squash = 0.3, Mouth = 0.2 }),
            K(2.0, a with { Sit = 0.6, BedIn = 0.6, Squash = 0.4 }), K(2.5, a));

        // Clic: «ping». Se agacha, salta (la parábola la hace `Motion`), estira los brazos en
        // el aire y amortigua al caer, mientras sale un anillo de radar del ojo.
        Reactions[PetReaction.Jump] = Once(K(0, r),
            K(0.12, new(Squash: 0.85, ArmL: 60, ArmR: 60, Eye: 0.7), Ease.Out),
            K(0.22, new(Squash: -0.7, ArmL: PetArm.Up, ArmR: PetArm.Up, Scan: 0.15), Ease.Linear),
            K(0.34, new(Squash: -0.2, ArmL: PetArm.Up - 15, ArmR: PetArm.Up - 15, Scan: 0.3, Mouth: 0.5)),
            K(0.45, new(Squash: 0.75, ArmL: PetArm.Down, ArmR: PetArm.Down, Scan: 0.45), Ease.Linear),
            K(0.58, new(Squash: -0.15, Scan: 0.6), Ease.Out), K(0.85, r with { Scan = 1 }, Ease.Linear),
            K(0.86, r, Ease.Step), K(0.9, r));
        // Feliz, con el ojo cerrado y sonrojada, se abraza y se mece.
        Reactions[PetReaction.Hearts] = Once(K(0, r),
            K(0.2, Happy(0, 0.3) with { ArmL = PetArm.Up, ArmR = PetArm.Up }), K(0.45, Happy(-0.6, -0.1)), K(0.75, Happy(0.6, 0.15)),
            K(1.05, Happy(-0.5, -0.1)), K(1.3, Happy(0.3, 0.2) with { ArmL = PetArm.Up, ArmR = PetArm.Up }), K(1.6, r));
        // Lo celebra: se agacha, salta con los brazos arriba girando el ojo y aterriza contenta.
        Reactions[PetReaction.Sparkle] = Once(K(0, r),
            K(0.12, new(Squash: 0.4, ArmL: PetArm.Down, ArmR: PetArm.Down), Ease.Out),
            K(0.3, Cheer(-0.5) with { LookX = -1, Lean = -0.4 }, Ease.Out), K(0.5, Cheer(-0.3) with { LookX = 1, Lean = 0.4, Eye = 0.9 }),
            K(0.7, Cheer(-0.4) with { LookX = -0.3, Eye = 0, Blush = 0.4 }), K(0.95, Cheer(0.15) with { Eye = 0, Blush = 0.4 }), K(1.4, r));
        // Mareada: la pupila da vueltas y el cuerpo se tambalea cada vez menos.
        Reactions[PetReaction.Dizzy] = Once([K(0, r), .. Enumerable.Range(1, 8).Select(i => K(i * 0.18, new PetPose(
            Eye: 0.55, LookX: Math.Cos(i * Math.PI / 2), LookY: Math.Sin(i * Math.PI / 2) * 0.8,
            Lean: 0.5 * (i % 2 == 0 ? 1 : -1) * (1 - i / 9.0), Squash: i % 2 == 0 ? 0.15 : -0.1,
            ArmL: i % 2 == 0 ? 20 : -20, ArmR: i % 2 == 0 ? -20 : 20))), K(1.6, r)]);
        // Se desinfla, con la mirada en el suelo.
        var deflated = new PetPose(Eye: 0.35, Squash: 0.75, ArmL: 75, ArmR: 75, LookY: 0.7);
        Reactions[PetReaction.Sad] = Once(K(0, r), K(0.5, deflated), K(0.8, deflated with { Squash = 0.7, LookX = -0.3 }),
            K(1.6, deflated with { Squash = 0.78, LookX = 0.2 }), K(1.85, deflated), K(2.2, r));
        Reactions[PetReaction.Nap] = Once(K(0, new(Eye: 0.5)),
            K(0.5, new(Eye: 0, ArmL: PetArm.Up, ArmR: PetArm.Up, Squash: -0.7, Mouth: 1), Ease.OutBack),
            K(0.95, new(Eye: 0, ArmL: PetArm.Down, ArmR: PetArm.Down, Mouth: 0.3, Squash: 0.25)), K(1.8, a));

        // El final de la limpieza: aprieta el cubo hasta que desaparece, salta con los
        // brazos arriba mientras sale un anillo de radar y vuelve a la diana.
        var held = Compact(0.9, 0, 0.7, -20);
        ContextReactions[(PetReaction.Sparkle, PetActivity.Sweeping)] = Once(K(0, held),
            K(0.3, held with { PropIn = 0.45, Squash = 0.45, Eye = 0.35, ArmL = 25, ArmR = 25 }),
            K(0.5, held with { PropIn = 0, Squash = 0.6, Eye = 0.2, Beam = 0, LookY = 0.3 }, Ease.In),
            K(0.75, Cheer(-0.55) with { Eye = 0, Blush = 0.5, Scan = 0.28 }, Ease.Out),
            K(1.0, Cheer(0.25) with { Eye = 0, Blush = 0.5, Scan = 0.55 }),
            K(1.2, r with { Blush = 0.3, Scan = 0.8, Squash = -0.1 }, Ease.Out),
            K(1.4, r with { Blush = 0.2, Scan = 1 }, Ease.Linear), K(1.41, r with { Blush = 0.2 }, Ease.Step), K(1.8, r));
        ContextDrips[(PetReaction.Sparkle, PetActivity.Sweeping)] = D((ParticleKind.Sparkle, 0.08));
        ContextBursts[(PetReaction.Sparkle, PetActivity.Sweeping)] = [(ParticleKind.Sparkle, 3), (ParticleKind.Bit, 2)];

        // Dormida en su cápsula: abre medio ojo y la antena emite, sin levantarse.
        InBed(PetReaction.Jump, Once(K(0, a), K(0.15, a with { Eye = 0.45, Squash = 0.4 }, Ease.Out), K(0.5, a with { Eye = 0.45, LookX = 0.5 }),
            K(0.8, a with { Eye = 0.45, LookX = -0.5 }), K(1.1, a with { Eye = 0.2 }), K(1.4, a)), burst: [(ParticleKind.Signal, 1)]);
        InBed(PetReaction.Hearts, Once(K(0, a), K(0.4, a with { Blush = 0.8, Squash = 0.7 }), K(0.9, a with { Blush = 0.8, Squash = 0.6, Lean = -0.3 }),
            K(1.4, a with { Blush = 0.8, Squash = 0.65, Lean = 0.3 }), K(1.8, a)), D((ParticleKind.Heart, 0.4)));
        InBed(PetReaction.Sparkle, Once(K(0, a), K(0.4, a with { Eye = 0.3, Blush = 0.3 }), K(1.0, a with { Eye = 0.3 }), K(1.4, a)),
            D((ParticleKind.Sparkle, 0.1)));
        InBed(PetReaction.Dizzy, Once([K(0, a), .. Enumerable.Range(1, 6).Select(i => K(i * 0.2, a with
            {
                Eye = 0.3, LookX = i % 2 == 0 ? 1 : -1, LookY = i % 2 == 0 ? 0.3 : -0.3, Squash = i % 2 == 0 ? 0.65 : 0.5,
            })), K(1.6, a)]), burst: [(ParticleKind.Star, 3)]);
        InBed(PetReaction.Sad, Once(K(0, a), K(0.4, a with { Eye = 0.2, LookY = 0.6, Squash = 0.75 }),
            K(1.7, a with { Eye = 0.2, LookY = 0.6, Squash = 0.75 }), K(2.0, a)), D((ParticleKind.Tear, 0.6)));

        // MARK: gestos al estar quieta
        Gestures[PetGesture.Stretch] = Once(K(0, r), K(0.25, new(Squash: 0.35, ArmL: PetArm.Down, ArmR: PetArm.Down, Eye: 0.6)),
            K(0.75, new(ArmL: PetArm.Up - 10, ArmR: PetArm.Up, Squash: -0.75, Eye: 0.2, Mouth: 0.3), Ease.Out),
            K(1.3, new(ArmL: PetArm.Up - 15, ArmR: PetArm.Up - 15, Squash: -0.85, Eye: 0, Mouth: 0.45)),
            K(1.6, new(Squash: 0.3, ArmL: PetArm.Down, ArmR: PetArm.Down)), K(1.95, r, Ease.OutBack));
        // Se rasca la antena con la mano derecha, mirando arriba.
        Gestures[PetGesture.Scratch] = Once(K(0, r), K(0.35, Scratch(-115)), K(0.5, Scratch(-95)), K(0.65, Scratch(-115)), K(0.8, Scratch(-95)),
            K(0.95, Scratch(-115)), K(1.1, Scratch(-95)), K(1.5, Scratch(-110) with { Eye = 0.8 }), K(2.0, r));
        Gestures[PetGesture.Yawn] = Once(K(0, r), K(0.2, new(Squash: 0.2, Eye: 0.5, ArmL: 60, ArmR: 60)),
            K(0.6, new(Eye: 0.1, Mouth: 1, ArmL: PetArm.Up + 20, ArmR: PetArm.Up + 10, Squash: -0.45), Ease.Out),
            K(1.15, new(Eye: 0, Mouth: 0.9, ArmL: PetArm.Up + 20, ArmR: PetArm.Up + 10, Squash: -0.4)),
            K(1.45, new(Eye: 0.4, Mouth: 0, Squash: 0.15)), K(1.8, r));
        Gestures[PetGesture.LookAround] = Once(K(0, r), K(0.5, r with { LookX = -1, LookY = -0.3, Lean = -0.25 }),
            K(1.1, r with { LookX = -1, LookY = -0.3, Lean = -0.25 }), K(1.5, r with { LookX = 1, LookY = -0.4, Lean = 0.25 }),
            K(2.1, r with { LookX = 1, LookY = -0.4, Lean = 0.25 }), K(2.6, r));
        // Saluda con la mano, inclinándose un poco.
        Gestures[PetGesture.Wave] = Once(K(0, r), K(0.3, Waving(-70)), K(0.5, Waving(-30)), K(0.7, Waving(-70)), K(0.9, Waving(-30)),
            K(1.1, Waving(-70)), K(1.7, r));
        // Un saltito en el sitio (el bote lo hace `Motion`).
        Gestures[PetGesture.Hop] = Once(K(0, r), K(0.1, new(Squash: 0.7, ArmL: 25, ArmR: 25), Ease.Out),
            K(0.25, new(Squash: -0.4, ArmL: PetArm.Up, ArmR: PetArm.Up), Ease.Out),
            K(0.48, new(Squash: 0.5, ArmL: PetArm.Down, ArmR: PetArm.Down), Ease.In), K(0.75, r, Ease.OutBack));
        Gestures[PetGesture.Nod] = Once(K(0, d), K(0.9, d with { Bob = 0.9, Eye = 0, Squash = 0.35 }, Ease.In),
            K(1.1, d with { Bob = -0.4, Eye = 0.9, Squash = -0.2 }, Ease.Out), K(1.4, d with { LookX = -0.5 }), K(2.2, d));
        // Barrido de radar: el ojo recorre la barra de un lado a otro y sale un anillo.
        Gestures[PetGesture.Scan] = Once(K(0, r), K(0.4, r with { LookX = -1, Eye = 0.85, Lean = -0.15 }),
            K(1.4, r with { LookX = 1, Eye = 0.85, Lean = 0.15, Scan = 1 }, Ease.Linear),
            K(1.41, r with { LookX = 1, Eye = 0.85, Lean = 0.15 }, Ease.Step),
            K(2.3, r with { LookX = -0.2, Eye = 0.85 }), K(2.8, r));
        // Señal de antena: se estira mirando arriba mientras la antena emite.
        Gestures[PetGesture.Ping] = Once(K(0, r), K(0.15, new(Squash: 0.3, ArmL: 30, ArmR: 30)),
            K(0.4, new(Squash: -0.45, LookY: -0.9, ArmL: -20, ArmR: -25), Ease.Out),
            K(1.0, new(Squash: -0.35, LookY: -0.9, ArmL: -25, ArmR: -20)), K(1.4, r));
        // Enfoca el ojo dos veces, con la retícula puesta.
        Gestures[PetGesture.Calibrate] = Once(K(0, r), K(0.4, r with { Eye = 0.5, Lock = 0.8 }), K(0.7, r with { Lock = 1 }, Ease.OutBack),
            K(1.0, r with { Eye = 0.5, Lock = 1 }), K(1.3, r with { Lock = 1 }, Ease.OutBack),
            K(1.8, r with { Lock = 0.2 }), K(2.2, r));
        AwakeGestures = [(PetGesture.Scan, 3), (PetGesture.Ping, 2), (PetGesture.Calibrate, 2), (PetGesture.LookAround, 2),
            (PetGesture.Wave, 1), (PetGesture.Hop, 1), (PetGesture.Stretch, 1), (PetGesture.Scratch, 1)];
        DrowsyGestures = [(PetGesture.Nod, 4), (PetGesture.Yawn, 3), (PetGesture.Stretch, 2), (PetGesture.Scratch, 1), (PetGesture.Calibrate, 1)];

        // Esperando un modelo: tras el logo, se asoma por su izquierda (`Peek`, en píxeles de
        // rejilla) curiosa; mira hacia el logo («¿ya viene?»), se esconde de golpe y vuelve
        // tímida, sonrojada y con el ojo entrecerrado.
        PeekStill = Hiding(2, 1, -1, 0);
        Peek = Loop(8.0,
            K(0, Hiding(0, 1, -1, 0)), K(1.0, Hiding(0, 1, -1, 0)), K(1.8, Hiding(3, 1, -1, 0), Ease.OutBack),
            K(2.35, Hiding(3, 1, -1, 0)), K(2.5, Hiding(3, 0, -1, 0)), K(2.65, Hiding(3, 1, -1, 0)),
            K(3.0, Hiding(3, 1, -1, 0)), K(3.25, Hiding(3, 1, 1, 0)), K(3.7, Hiding(3, 1, 1, 0)), K(3.85, Hiding(3, 1, -1, 0)),
            K(4.15, Hiding(0, 0.8, -1, 0.6), Ease.In),
            K(4.6, Hiding(0, 0.6, -1, 1)), K(5.2, Hiding(0, 0.5, -1, 1)), K(5.9, Hiding(2, 0.5, -1, 1)),
            K(7.0, Hiding(2, 0.55, -1, 0.8)), K(7.6, Hiding(0, 0.8, -1, 0.3)));

        ActivityDrips[PetActivity.DeepSleep] = D((ParticleKind.Z, 1.1));
        ActivityDrips[PetActivity.Eating] = D((ParticleKind.Sparkle, 0.35));
        ActivityDrips[PetActivity.Sweeping] = D((ParticleKind.Fragment, 0.1));
        ActivityDrips[PetActivity.Working] = D((ParticleKind.Bit, 0.22));
        ReactionDrips[PetReaction.Sad] = D((ParticleKind.Tear, 0.5));
        ReactionDrips[PetReaction.Hearts] = D((ParticleKind.Heart, 0.22));
        ReactionDrips[PetReaction.Sparkle] = D((ParticleKind.Sparkle, 0.07), (ParticleKind.Bit, 0.15));
        GestureDrips[PetGesture.Ping] = D((ParticleKind.Signal, 0.3));
        Bursts[PetReaction.Jump] = [(ParticleKind.Signal, 1)];
        Bursts[PetReaction.Dizzy] = [(ParticleKind.Star, 3)];
        Complete();
    }

    private readonly PetKey[] wakeUp;

    private static PetPose Waking(double eye) => new(Eye: eye, ArmL: PetArm.Down, ArmR: PetArm.Down);
    private static PetPose Rub(double eye) => new(Eye: eye, ArmL: PetArm.Rub, ArmR: PetArm.Down, Squash: 0.1);

    private static PetPose Eating(double mouth, double squash, double arm) =>
        new(Mouth: mouth, Squash: squash, ArmL: -15, ArmR: arm, Prop: PetProp.Crumb, LookX: 0.4, LookY: -0.3);

    private static PetPose Typing(bool left) => new(Prop: PetProp.Laptop, LookY: 0.55,
        ArmL: left ? PetArm.Type : 52, ArmR: left ? 52 : PetArm.Type);

    private static PetPose Box(PetFeet feet, double squash, double arms) =>
        new(Prop: PetProp.Box, ArmL: arms, ArmR: arms, Feet: feet, Squash: squash);

    /// Escaneando: el haz apunta al suelo hacia `look` (−1 … 1) y el cuerpo se inclina detrás.
    private static PetPose Scanning(double look) => new(Prop: PetProp.Cube, PropIn: 0, Beam: 1, LookX: look, LookY: 0.8,
        Lean: 0.3 * look, Eye: 0.9, ArmL: 30 - 15 * look, ArmR: 30 + 15 * look);

    /// Compactando: el cubo (`size`, su `PropIn`) delante, el haz más tenue y los brazos que aprietan.
    private static PetPose Compact(double size, double squash, double eye, double arms) => new(Prop: PetProp.Cube, PropIn: size,
        Beam: 0.6, LookY: 1, Squash: squash, Eye: eye, ArmL: arms, ArmR: arms);

    private static PetPose Cheer(double squash) => new(ArmL: PetArm.Up, ArmR: PetArm.Up, Squash: squash);
    private static PetPose Happy(double lean, double squash) => new(Eye: 0, ArmL: 70, ArmR: 70, Lean: lean, Squash: squash, Blush: 0.9);
    private static PetPose Scratch(double arm) => new(ArmR: arm, Eye: 0.5, LookX: 0.5, LookY: -0.5, Squash: 0.15);
    private static PetPose Waving(double arm) => new(ArmR: arm, Blush: 0.3, Lean: 0.15, LookX: 0.3);

    private static PetPose Hiding(double peek, double eye, double look, double blush) =>
        new(Eye: eye, LookX: look, Squash: 0.9, Sit: 1, ArmL: PetArm.Down, OneArm: true, Behind: true, Peek: peek, Blush: blush);

    /// Fija la retícula sobre el cursor y, al llegar, un barrido de radar.
    public override PetPose Hover(PetPose pose, double hoverX, double hoverY, double seconds) =>
        pose with { Lock = 1, Scan = seconds < 0.7 ? seconds / 0.7 : 0 };

    /// Sale de detrás del logo de un saltito y se mete agachándose; sale de la cápsula
    /// estirándose y entra con la cama; saca un accesorio deslizándolo y lo guarda (si
    /// comía, se relame y da un saltito). Si no, basta con la mezcla corta.
    public override PetClip? Transition(PetActivity from, PetActivity to, bool fromHiding, bool toHiding)
    {
        if (fromHiding && !toHiding)
            return Once(K(0, Hiding(0, 1, 0, 0)), K(0.15, new(Squash: 0.7, Sit: 0.3), Ease.Out),
                K(0.32, new(Squash: -0.5, Bob: -1.2, ArmL: PetArm.Up, ArmR: PetArm.Up), Ease.Out), K(0.55, Rest, Ease.OutBack),
                K(0.85, For(to).At(0)));
        if (!fromHiding && toHiding)
            return Once(K(0, Still(from)), K(0.2, new(Squash: 0.9, Sit: 0.5)), K(0.45, Hiding(0, 1, -1, 0)));
        if (fromHiding || from == to) return null;
        var before = Still(from);
        var after = For(new PetContext(to, from)).At(0);
        if (from == PetActivity.DeepSleep && to == PetActivity.WakingUp)
            return Once([.. wakeUp, K(4.4, after)]);
        if (from == PetActivity.DeepSleep && to != PetActivity.Hidden)
            return Once(K(0, Asleep), K(0.35, Asleep with { Eye = 0.3 }),
                K(0.7, Asleep with { Eye = 0.2, Sit = 0.5, Squash = 0.2, Tint = PetTint.Normal }),
                K(1.05, new(Eye: 0.5, Squash: -0.5, ArmL: PetArm.Up, ArmR: PetArm.Up, BedIn: 0.3), Ease.Out), K(1.45, after));
        if (to == PetActivity.DeepSleep && from != PetActivity.Yawning)
            return Once(K(0, before), K(0.35, new(Squash: 0.5, Eye: 0.4, BedIn: 0.5, ArmL: PetArm.Down, ArmR: PetArm.Down)), K(1.0, Asleep));
        if (after.Prop != PetProp.None && after.Prop != before.Prop)
            return before.Prop == PetProp.None
                ? Once(K(0, before with { Prop = after.Prop, PropIn = 0 }), K(0.5, after, Ease.Out))
                : Once(K(0, before), K(0.35, PetPose.Lerp(before, after, 0.4) with { Prop = before.Prop, PropIn = 0 }),
                    K(0.45, PetPose.Lerp(before, after, 0.6) with { Prop = after.Prop, PropIn = 0 }), K(0.8, after));
        if (before.Prop != PetProp.None && after.Prop != before.Prop)
            return from == PetActivity.Eating
                ? Once(K(0, before), K(0.2, before with { Mouth = 1 }), K(0.4, before with { Mouth = 0, PropIn = 0 }),
                    K(0.6, new(Bob: -1, Squash: -0.3), Ease.Out), K(0.85, after, Ease.OutBack))
                : Once(K(0, before), K(0.4, PetPose.Lerp(before, after, 0.5) with { Prop = before.Prop, PropIn = 0 }, Ease.In), K(0.7, after));
        return null;
    }

    public override PetPose Still(PetActivity activity) => activity switch
    {
        PetActivity.WakingUp => new(Eye: 0.5, ArmL: PetArm.Rub, ArmR: PetArm.Down),
        PetActivity.Drowsy => DrowsyPose,
        PetActivity.Eating => Eating(1, 0, PetArm.Up + 20),
        PetActivity.Working => Typing(true),
        PetActivity.Downloading => Box(PetFeet.Stand, 0, -75),
        PetActivity.Sweeping => Scanning(0.5) with { PropIn = 0.6 },
        _ => base.Still(activity),
    };

    /// Un paso con cada pie según la distancia recorrida (para que no patinen), con un
    /// bote por paso, los brazos balanceándose y mirando hacia donde va.
    public override PetPose Walk(double distance, int facing, double lean, bool turning)
    {
        double phase = distance / 6.0;   // un ciclo (dos pasos) cada 6 píxeles de rejilla
        int q = (int)Math.Floor(phase * 4) & 3;
        double swing = 25 * Math.Sin(2 * Math.PI * phase);
        return new(
            Feet: q == 0 ? PetFeet.StepLeft : q == 2 ? PetFeet.StepRight : PetFeet.Stand,
            Bob: -0.8 * Math.Abs(Math.Sin(2 * Math.PI * phase)), ArmL: 20 + swing, ArmR: 20 - swing,
            LookX: 0.7 * facing, Lean: lean, Squash: turning ? 0.4 : 0.1 * Math.Cos(4 * Math.PI * phase));
    }

    /// Desplazamiento de toda la mascota (en píxeles de rejilla, con decimales).
    public override (double Dx, double Dy) Motion(PetActivity activity, double seconds)
    {
        double t = Math.Max(0, seconds);
        return activity switch
        {
            PetActivity.Downloading => (0, -1.6 * Math.Abs(Math.Sin(Math.PI * t / 0.3))),   // botes al correr
            PetActivity.Sweeping => (0, -0.35 * (1 - Math.Cos(2 * Math.PI * t / 1.6))),     // flota mientras escanea
            _ => (0, 0),
        };
    }

    public override (double Dx, double Dy) Motion(PetReaction reaction, double seconds)
    {
        double t = Math.Max(0, seconds);
        return reaction switch
        {
            PetReaction.Jump => (0, Arc(t, 0.12, 0.45, 5.5)),
            PetReaction.Sparkle => (0, Arc(t, 0.12, 0.62, 3)),
            PetReaction.Hearts => (0, -0.8 * Math.Abs(Math.Sin(Math.PI * t / 0.3)) * Math.Max(0, 1 - t / 1.6)),
            PetReaction.Dizzy => (Math.Sin(2 * Math.PI * t / 0.5) * Math.Max(0, 1 - t / 1.6), 0),
            _ => (0, 0),
        };
    }

    /// El final de la limpieza: el saltito de celebración, sin moverse de donde escanea.
    protected override (double Dx, double Dy) ContextMotion(PetReaction reaction, double seconds, PetContext during) =>
        reaction == PetReaction.Sparkle && during.Activity == PetActivity.Sweeping ? (0, Arc(seconds, 0.55, 0.95, 3)) : Motion(reaction, seconds);

    public override (double Dx, double Dy) Motion(PetGesture gesture, double seconds) =>
        gesture == PetGesture.Hop ? (0, Arc(seconds, 0.1, 0.48, 2.5)) : (0, 0);

    /// Respira (se hincha y deshincha un poco) mientras está despierta.
    public override double Breath(PetActivity activity, double seconds) =>
        activity is PetActivity.Alert or PetActivity.Drowsy or PetActivity.Working or PetActivity.Eating or PetActivity.WakingUp
            ? 0.12 * Math.Sin(2 * Math.PI * seconds / 2.8) : 0;
}
