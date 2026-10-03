namespace IsTargetSleeping;

/// La capibara: zen e imperturbable. Lenta, con los ojos entornados; casi nada la
/// altera (al clic, un parpadeo lento y una burbuja). Duerme en un baño termal con
/// vapor, a veces se le posa un pajarito y la mandarina de la cabeza se tambalea. No se
/// esconde tras el logo: espera apoyada en él, tranquila. Para limpiar no usa escoba: despierta se
/// frota el trasero por el suelo (como hacen los animales para limpiarse) y acaba de pie,
/// brillante; si dormía, sale de la tina, se escurre el agua del pelo y acaba seca.
/// Dormida, reacciona sin salir de la tina. Sin nada que hacer está sentada (de pie solo
/// camina, carga la caja, se escurre y sube o baja del cocodrilo), y mientras Ollama
/// arranca llega su cocodrilo: se pone de pie, salta a su lomo, se sienta y pasean; ya
/// encendido, se levanta, baja de un salto y el cocodrilo se va.
public sealed class CapybaraProfile : ProfileKit
{
    protected override PetPose Rest => new(ArmL: PetArm.Down, ArmR: PetArm.Down, Eye: 0.55);
    protected override PetPose Asleep => Rest with { Eye = 0, Sit = 1, Squash = 0.4, BedIn = 1, Tint = PetTint.Sleepy };
    /// Sentada, pasando el rato: su postura sin nada que hacer.
    private PetPose Seated => Rest with { Sit = 0.75 };
    private PetPose Dozy => Seated with { Eye = 0.3 };
    /// Montada en el lomo del cocodrilo, que ya está en su sitio.
    private PetPose Riding => Seated with { Ride = 1, Croc = 1, Eye = 0.45 };
    public override double WalkSpeed => 7;
    public override double MinWait => 12;
    public override double MaxWait => 22;
    public override double LookSpeed => 4;
    public override bool HidesBehindLogo => false;

    public CapybaraProfile()
    {
        var r = Rest;
        var st = Seated;
        var d = Dozy;
        Activities[PetActivity.DeepSleep] = Loop(5.5, K(0, Asleep), K(2.4, Asleep with { Squash = 0.6 }));
        // Ollama arrancando: a lomos del cocodrilo, que camina en el sitio; ella se mece y mira el paisaje.
        var ride = Riding;
        Activities[PetActivity.WakingUp] = Loop(2.4, [.. Enumerable.Range(0, 8).Select(i => K(i * 0.3, ride with
        {
            CrocStep = i % 2 == 0 ? 1 : -1, Bob = i % 2 == 0 ? 0 : -0.3, Lean = i % 2 == 0 ? 0.1 : -0.1,
            LookX = i < 4 ? -0.4 : 0.3, EarR = i == 3 ? 0.5 : 0, Blush = i is >= 4 and <= 6 ? 0.2 : 0,
        }))]);
        Activities[PetActivity.Drowsy] = Loop(9, K(0, d), K(3, d with { Eye = 0, Bob = 0.4 }), K(4.2, d with { Eye = 0.35 }),
            K(6.5, d with { LookX = -0.4 }), K(8, d));
        // Sentada, casi inmóvil: un parpadeo lento, una oreja y una mirada de reojo de vez en cuando.
        Activities[PetActivity.Alert] = Loop(11, K(0, st), K(2.5, st), K(2.8, st with { Eye = 0 }), K(3.4, st),
            K(5, st with { LookX = 0.4, EarR = 0.5 }), K(7, st with { LookX = 0.4 }), K(8, st),
            K(9, st with { Eye = 0.3, Squash = 0.15 }), K(10, st));
        var eat = st with { Prop = PetProp.Crumb, LookY = 0.3 };
        Activities[PetActivity.Eating] = Loop(1.6, K(0, eat with { Mouth = 0.5 }), K(0.8, eat with { Blush = 0.2 }));
        var work = st with { Prop = PetProp.Laptop, LookY = 0.55, Eye = 0.5 };
        Activities[PetActivity.Working] = Loop(2.4, K(0, work), K(1.2, work with { Bob = 0.3, LookX = 0.3 }), K(1.8, work with { LookX = -0.2 }));
        // La caja en equilibrio sobre la cabeza, con la mandarina encima; pasitos lentos.
        var box = r with { Prop = PetProp.Box };
        Activities[PetActivity.Downloading] = Loop(1.4, K(0, box with { Feet = PetFeet.StepLeft, EarL = 0.3 }), K(0.35, box),
            K(0.7, box with { Feet = PetFeet.StepRight, EarL = -0.3, Squash = 0.1 }), K(1.05, box));
        // Despierta: se frota el trasero por el suelo, sin moverse de su sitio. Cada arrastre: se
        // echa hacia delante sobre la pata con el trasero quieto, tira y el trasero se arrastra
        // hacia delante (aplastándose, con los ojos cerrados de gusto) dejando una marca de polvo
        // que se queda atrás; luego se endereza y menea el trasero.
        var scoot = Scoot;
        Activities[PetActivity.Sweeping] = Loop(2.4, [
            .. new[] { 0.0, 1.2 }.SelectMany(at => new[]
            {
                K(at, scoot, Ease.Step),   // la marca anterior ya se borró: la nueva empieza sin saltos
                K(at + 0.35, scoot with { Drag = 1, Rump = 0.4, Squash = -0.1, Eye = 0.3 }, Ease.Out),
                K(at + 0.8, scoot with { Rump = -0.7, Squash = 0.35, Eye = 0.05, Blush = 0.35, Skid = 0.75, EarR = at > 0 ? 0.5 : 0 }, Ease.InOut),
                K(at + 0.95, scoot with { Rump = 0.3, Squash = -0.05, Skid = 0.92 }, Ease.Out),
                K(at + 1.08, scoot with { Rump = -0.15, Skid = 0.99 }),
                K(at + 1.18, scoot with { Skid = 1 }),
            }),
        ]);
        // Recién salida de la tina se escurre: ráfagas de sacudidas con los ojos cerrados y,
        // entre una y otra, se estruja.
        var wet = Wet;
        AfterBed(PetActivity.Sweeping, Loop(2.8, [
            K(0, wet), K(0.2, wet with { Eye = 0 }),
            .. Enumerable.Range(0, 8).Select(i => K(0.3 + i * 0.1, wet with { Eye = 0, Shake = i % 2 == 0 ? 1 : -1, EarR = i % 2 == 0 ? 0.4 : -0.4, Squash = -0.1 }, Ease.Linear)),
            K(1.2, wet with { Eye = 0 }), K(1.7, wet with { Eye = 0, Squash = 0.35, Blush = 0.15 }), K(2.2, wet with { Squash = -0.1, Eye = 0.2 }),
        ]), D((ParticleKind.Drop, 0.12)));
        // Bosteza sentada (sin levantarse) y se mete en la tina.
        Activities[PetActivity.Yawning] = Once(K(0, d), K(1.2, st with { Eye = 0, Mouth = 1, Stretch = 0.3 }),
            K(2.2, st with { Eye = 0, Mouth = 0.3, Sit = 0.85, BedIn = 0.4 }), K(3.4, Asleep));

        // Clic: un parpadeo lento, nada más (y una burbuja).
        Reactions[PetReaction.Jump] = Once(K(0, st), K(0.45, st with { Eye = 0 }), K(1.0, st with { Eye = 0, Blush = 0.2 }), K(1.45, st));
        Reactions[PetReaction.Hearts] = Once(K(0, st), K(0.6, st with { Eye = 0, Blush = 0.6, Lean = 0.2 }),
            K(1.6, st with { Eye = 0, Blush = 0.6, Lean = -0.2 }), K(2.2, st));
        Reactions[PetReaction.Sparkle] = Once(K(0, st), K(0.5, st with { Eye = 0.8, Squash = -0.1, Blush = 0.2 }), K(1.2, st with { Eye = 0.8 }), K(1.7, st));
        Reactions[PetReaction.Dizzy] = Once(K(0, st), K(0.6, st with { Eye = 0.3, LookX = -0.6 }), K(1.2, st with { Eye = 0.3, LookX = 0.6 }),
            K(1.8, st with { Eye = 0.4 }), K(2.3, st));
        // Ni la tristeza la saca de quicio: baja la mirada mientras cae una hoja.
        Reactions[PetReaction.Sad] = Once(K(0, st), K(0.8, st with { Eye = 0.3, LookY = 0.4, Sit = 0.9 }),
            K(2.0, st with { Eye = 0.3, LookY = 0.4, Sit = 0.9 }), K(2.6, st));
        Reactions[PetReaction.Nap] = Once(K(0, st with { Eye = 0.4 }), K(0.9, st with { Eye = 0, Mouth = 0.6 }),
            K(1.8, Asleep with { BedIn = 0.6 }), K(2.6, Asleep));
        // El final de la limpieza despierta: deja de frotarse y, sentada, brilla satisfecha.
        ContextReactions[(PetReaction.Sparkle, PetActivity.Sweeping)] = Once(K(0, scoot), K(0.3, st with { Eye = 0.2, Blush = 0.3 }),
            K(0.7, st with { Eye = 0.8, Squash = -0.12, Blush = 0.35 }, Ease.Out), K(1.6, st with { Eye = 0.8, Blush = 0.3 }),
            K(2.0, st with { Eye = 0.6, Blush = 0.2 }));
        ContextDrips[(PetReaction.Sparkle, PetActivity.Sweeping)] = D((ParticleKind.Sparkle, 0.1));
        ContextBursts[(PetReaction.Sparkle, PetActivity.Sweeping)] = [(ParticleKind.Dust, 4)];
        // Y tras la tina: una última sacudida, ya seca, mullida y brillante.
        AfterBed(PetReaction.Sparkle, PetActivity.Sweeping, Once(K(0, wet), K(0.3, wet with { Eye = 0 }),
            K(0.4, wet with { Eye = 0, Shake = 1, Wet = 0.5 }, Ease.Linear), K(0.5, wet with { Eye = 0, Shake = -1, Wet = 0.35 }, Ease.Linear),
            K(0.6, wet with { Eye = 0, Shake = 1, Wet = 0.2 }, Ease.Linear), K(0.7, r with { Eye = 0 }, Ease.Linear),
            K(1.1, r with { Eye = 0.8, Squash = -0.15, Blush = 0.3 }, Ease.Out), K(1.8, r with { Eye = 0.8, Blush = 0.2 }), K(2.2, r)),
            D((ParticleKind.Sparkle, 0.1)), [(ParticleKind.Drop, 4)]);

        // Dormida en la tina: ni un clic la saca del agua.
        var a = Asleep;
        InBed(PetReaction.Jump, Once(K(0, a), K(0.6, a with { Eye = 0.3 }), K(1.2, a with { Eye = 0.3 }), K(1.7, a)),
            burst: [(ParticleKind.Bubble, 1)]);
        InBed(PetReaction.Hearts, Once(K(0, a), K(0.6, a with { Blush = 0.6, Lean = 0.2 }), K(1.6, a with { Blush = 0.6, Lean = -0.2 }), K(2.2, a)),
            D((ParticleKind.Heart, 0.5), (ParticleKind.Bubble, 0.8)));
        InBed(PetReaction.Sparkle, Once(K(0, a), K(0.5, a with { Eye = 0.3, Blush = 0.2 }), K(1.2, a with { Eye = 0.3 }), K(1.7, a)),
            D((ParticleKind.Sparkle, 0.12)));
        InBed(PetReaction.Dizzy, Once(K(0, a), K(0.6, a with { Eye = 0.3, LookX = -0.6 }), K(1.2, a with { Eye = 0.3, LookX = 0.6 }),
            K(1.8, a with { Eye = 0.2 }), K(2.3, a)), D((ParticleKind.Bubble, 0.25)), [(ParticleKind.Bubble, 3)]);
        // Triste, se hunde un poco más en el agua mientras cae una hoja.
        InBed(PetReaction.Sad, Once(K(0, a), K(0.8, a with { Squash = 0.8, Eye = 0.2, LookY = 0.5 }),
            K(2.0, a with { Squash = 0.8, Eye = 0.2, LookY = 0.5 }), K(2.6, a)), D((ParticleKind.Leaf, 1.0)));

        // Un pajarito llega volando, se posa en su lomo un rato y se va.
        Gestures[PetGesture.Bird] = Once(K(0, st), K(2.4, st with { Bird = 1, Eye = 0.45 }, Ease.Out),
            K(3.2, st with { Bird = 1, Eye = 0.25, Blush = 0.2 }), K(6.6, st with { Bird = 1, Eye = 0.25, Blush = 0.2 }),
            K(8, st with { Bird = 0 }, Ease.In), K(8.6, st));
        // Se mete un rato en su baño termal.
        Gestures[PetGesture.Bath] = Once(K(0, st), K(1.6, st with { Bath = 1, Sit = 0.5, Eye = 0.25 }),
            K(8.4, st with { Bath = 1, Sit = 0.5, Eye = 0.2, Blush = 0.2 }), K(10, st));
        Gestures[PetGesture.Yawn] = Once(K(0, st), K(1, st with { Eye = 0, Mouth = 1, Stretch = 0.3 }), K(2.2, st with { Eye = 0.2, Mouth = 0.3 }), K(3.2, st));
        Gestures[PetGesture.Nod] = Once(K(0, d), K(1.6, d with { Eye = 0, Bob = 0.6 }, Ease.In), K(2.3, d with { Eye = 0.4, Bob = -0.1 }), K(3.6, d));
        AwakeGestures = [(PetGesture.Bird, 3), (PetGesture.Bath, 2), (PetGesture.Yawn, 2)];
        DrowsyGestures = [(PetGesture.Nod, 4), (PetGesture.Yawn, 2), (PetGesture.Bath, 2), (PetGesture.Bird, 1)];

        // Esperando un modelo: sentada junto al logo, apoyada en él, sin esconderse.
        PeekStill = st with { Lean = 0.5, Eye = 0.35 };
        Peek = Loop(9, K(0, PeekStill), K(3, PeekStill with { Eye = 0.25 }), K(3.4, PeekStill with { Eye = 0 }),
            K(4.6, PeekStill with { Eye = 0.3, Blush = 0.15 }), K(7, PeekStill with { LookX = 0.4 }), K(8.2, PeekStill));

        ActivityDrips[PetActivity.DeepSleep] = D((ParticleKind.Steam, 0.8), (ParticleKind.Bubble, 1.7));
        ActivityDrips[PetActivity.Alert] = D((ParticleKind.Leaf, 7));
        ActivityDrips[PetActivity.WakingUp] = D((ParticleKind.Dust, 0.6));   // los pasos del cocodrilo
        ActivityDrips[PetActivity.Working] = D((ParticleKind.Leaf, 4));
        ActivityDrips[PetActivity.Eating] = D((ParticleKind.Sparkle, 0.6));
        ActivityDrips[PetActivity.Sweeping] = D((ParticleKind.Dust, 0.1));   // el polvo que levanta al frotarse
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
        PetActivity.WakingUp => Riding,
        PetActivity.Drowsy => Dozy,
        PetActivity.Eating => Seated with { Prop = PetProp.Crumb, Mouth = 0.5 },
        PetActivity.Working => Seated with { Prop = PetProp.Laptop, LookY = 0.55, Eye = 0.5 },
        PetActivity.Sweeping => Scoot with { Drag = 0.6, Rump = -0.3, Squash = 0.2, Skid = 0.5 },
        _ => base.Still(activity),
    };

    public override PetPose Still(PetContext activity) => activity is { Activity: PetActivity.Sweeping, AfterBed: true }
        ? Wet with { Shake = 0.6, Eye = 0 } : Still(activity.Activity);

    /// Recién salida del agua, con el pelo empapado.
    private PetPose Wet => Rest with { Wet = 0.7, Eye = 0.3 };
    /// Frotándose el trasero: sentada al ras del suelo, echada hacia delante y con los ojos casi cerrados.
    private PetPose Scoot => Seated with { Sit = 0.95, Eye = 0.15, Blush = 0.2 };

    /// El paseo en cocodrilo se ve entero: su llegada y al menos una vuelta, aunque Ollama ya
    /// haya arrancado; después se baja (la transición de salida).
    public override double HoldFor(PetActivity from, PetActivity to) => to == PetActivity.WakingUp && from != to
        ? (Transition(from, to, false, false)?.Length ?? 0) + For(to).Length : base.HoldFor(from, to);

    /// Llega el cocodrilo caminando desde la derecha (con pasos) entre los segundos `start` y `end`.
    private static IEnumerable<PetKey> CrocWalk(PetPose pose, double start, double end, bool arriving)
    {
        const int steps = 5;
        for (int i = 0; i <= steps; i++)
        {
            double k = (double)i / steps;
            yield return K(start + (end - start) * k, pose with { Croc = arriving ? k : 1 - k, CrocStep = i % 2 == 0 ? 1 : -1 }, Ease.Linear);
        }
    }

    /// Al arrancar Ollama llega el cocodrilo: se pone de pie (si dormía, antes sale de la tina),
    /// lo espera de pie, salta a su lomo, aterriza de pie y ahí se sienta.
    private PetClip Mount(PetPose start)
    {
        var r = Rest;
        var first = For(PetActivity.WakingUp).At(0);
        var keys = new List<PetKey> { K(0, start) };
        double t = 0;
        if (start.BedIn > 0.5)
        {
            keys.Add(K(t += 0.6, start with { Eye = 0.3 }));
            keys.Add(K(t += 0.6, r with { BedIn = 0.6, Sit = 0.4, Eye = 0.45 }));
            keys.Add(K(t += 0.5, r with { Eye = 0.5 }));
        }
        else keys.Add(K(t += 0.5, r with { Eye = 0.5 }));
        var waiting = r with { Eye = 0.6, LookX = 0.7 };
        keys.AddRange(CrocWalk(waiting, t, t += 1.2, arriving: true));
        keys.Add(K(t += 0.3, waiting with { Croc = 1, Squash = 0.5, LookX = 0.3 }));
        keys.Add(K(t += 0.3, waiting with { Croc = 1, Ride = 0.6, Bob = -1.8, Squash = -0.3, LookX = 0 }, Ease.Out));
        keys.Add(K(t += 0.25, r with { Croc = 1, Ride = 1, Squash = 0.35, Eye = 0.5 }, Ease.In));
        keys.Add(K(t += 0.35, r with { Croc = 1, Ride = 1, Eye = 0.5 }));
        keys.Add(K(t += 0.45, Riding with { Squash = 0.2 }));
        keys.Add(K(t += 0.3, first));
        return Once([.. keys]);
    }

    /// Ya encendido: se levanta en el lomo, baja de un salto, mira de pie cómo se va el
    /// cocodrilo y pasa a lo que toque (se sienta o vuelve a la tina).
    private PetClip Dismount(PetActivity to, PetPose end)
    {
        var r = Rest;
        var ride = For(PetActivity.WakingUp).At(0);
        var up = r with { Croc = 1, Ride = 1, Eye = 0.5 };
        var watching = r with { Croc = 1, LookX = 0.6, Eye = 0.55 };
        var keys = new List<PetKey>
        {
            K(0, ride), K(0.45, up), K(0.7, up with { Squash = 0.45 }),
            K(1.0, r with { Croc = 1, Ride = 0.4, Bob = -1.8, Squash = -0.3 }, Ease.Out),
            K(1.25, watching with { Squash = 0.35 }, Ease.In), K(1.55, watching),
        };
        double t = 1.55;
        keys.AddRange(CrocWalk(watching, t, t += 1.2, arriving: false));
        if (to.InBed() && end.BedIn > 0.5)
        {
            keys.Add(K(t += 0.5, PetPose.Lerp(r, Asleep, 0.5) with { Eye = 0.4, Tint = PetTint.Normal }));
            keys.Add(K(t += 0.7, end));
        }
        else keys.Add(K(t += 0.5, end));
        return Once([.. keys]);
    }

    /// A limpiar: si dormía, sale de la tina mojada (y se escurre); si no, se sienta al ras
    /// del suelo a frotarse (la transición genérica). Y el cocodrilo al arrancar Ollama
    /// (también si espera junto al logo).
    public override PetClip? Transition(PetActivity from, PetActivity to, bool fromHiding, bool toHiding)
    {
        // Va montada mientras Ollama arranca (salvo si espera junto al logo).
        bool ridingBefore = from == PetActivity.WakingUp && !fromHiding, ridingAfter = to == PetActivity.WakingUp && !toHiding;
        if (ridingAfter && !ridingBefore) return Mount(fromHiding ? PeekStill : Still(from));
        if (ridingBefore && !ridingAfter) return Dismount(to, toHiding ? PeekStill : to.InBed() ? Asleep : For(to).At(0));
        if (fromHiding || toHiding || to != PetActivity.Sweeping || from == to || !from.InBed())
            return base.Transition(from, to, fromHiding, toHiding);
        var r = Rest;
        return Once(K(0, Asleep), K(0.6, Asleep with { Eye = 0.3 }),
            K(1.2, r with { BedIn = 0.85, Sit = 0.6, Eye = 0.35, Wet = 1, Stretch = 0.3 }),
            K(1.8, r with { BedIn = 0.3, Wet = 1, Squash = 0.3, Bob = -0.8 }), K(2.2, r with { Wet = 1, Squash = -0.2 }, Ease.OutBack),
            K(2.5, For(new PetContext(to, from)).At(0)));
    }

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
        PetActivity.WakingUp => (Math.Sin(2 * Math.PI * seconds / 4.8) * 1.0, 0),  // el paseo avanza y vuelve
        _ => (0, 0),
    };

    /// Recién salida de la tina, el cuerpo va con la sacudida al escurrirse.
    public override (double Dx, double Dy) Motion(PetContext activity, double seconds) =>
        activity is { Activity: PetActivity.Sweeping, AfterBed: true }
            ? (For(activity).At(seconds).Shake * 0.35, 0) : Motion(activity.Activity, seconds);

    /// Los finales de la limpieza: el cuerpo va con su sacudida.
    protected override (double Dx, double Dy) ContextMotion(PetReaction reaction, double seconds, PetContext during) =>
        reaction == PetReaction.Sparkle && during.Activity == PetActivity.Sweeping
            ? (For(reaction, during).At(seconds).Shake * 0.35, 0) : Motion(reaction, seconds);

    public override (double Dx, double Dy) Motion(PetReaction reaction, double seconds) =>
        reaction == PetReaction.Hearts ? (Math.Sin(Math.PI * Math.Min(seconds, 2.2) / 1.1) * 0.2, 0) : (0, 0);

    public override double Breath(PetActivity activity, double seconds) =>
        activity is PetActivity.Alert or PetActivity.Drowsy or PetActivity.Working or PetActivity.Eating
            ? 0.08 * Math.Sin(2 * Math.PI * seconds / 4.5) : 0;
}
