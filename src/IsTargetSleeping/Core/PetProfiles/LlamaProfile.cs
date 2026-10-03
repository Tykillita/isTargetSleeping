namespace IsTargetSleeping;

/// La llama: curiosa, orgullosa y algo dramática. Va con la cabeza alta, estira el cuello
/// hacia todo (también hacia el cursor), rumia de lado, lleva la caja en el lomo como
/// animal de carga, tararea cuando está contenta y resopla «¡pff!» si la molestas.
/// No tiene brazos: se expresa con el cuello, las orejas, la cola y las pezuñas; lo que
/// come lo lleva en el hocico y teclea picoteando el portátil al ritmo de sus pezuñas.
/// Es alta: para esconderse asoma la cabeza por encima del logo, como un periscopio.
/// Para limpiar se sacude la lana soltando polvo; dormida, reacciona sin levantarse.
public sealed class LlamaProfile : ProfileKit
{
    protected override PetPose Rest => new(Stretch: 0.3, Tail: 0.1);
    protected override PetPose Asleep => Rest with
    {
        Eye = 0, Sit = 1, Squash = 0.5, BedIn = 1, Tint = PetTint.Sleepy, EarL = -0.6, EarR = -0.6, Stretch = -0.5, Tail = -0.6,
    };
    private PetPose Dozy => Rest with { Eye = 0.4, Sit = 0.15, Stretch = 0, Tail = -0.3 };
    public override double WalkSpeed => 14;
    public override double MinWait => 6;
    public override double MaxWait => 12;
    public override double LookSpeed => 12;

    public LlamaProfile()
    {
        var r = Rest;
        var d = Dozy;
        Activities[PetActivity.DeepSleep] = Loop(4.0, K(0, Asleep), K(1.7, Asleep with { Squash = 0.75, EarL = -0.45 }),
            K(2.6, Asleep with { Squash = 0.65, Tail = -0.4 }));
        // Mientras Ollama arranca, ya despierta (el estirón y el bostezo son su transición desde
        // el cojín): rumia, mira a un lado y a otro meneando la cola y estira el cuello.
        var waking = r with { Eye = 0.45, Stretch = 0.5 };
        Activities[PetActivity.WakingUp] = Loop(4.4, K(0, waking), K(0.4, waking with { Jaw = 0.8 }), K(0.8, waking with { Jaw = -0.8 }),
            K(1.2, waking with { Jaw = 0.8 }), K(1.6, waking), K(2.2, waking with { LookX = 0.6, EarR = 0.8, Tail = 0.4 }),
            K(2.4, waking with { LookX = 0.6, EarR = 0.8, Tail = -0.2 }), K(2.6, waking with { LookX = 0.6, EarR = 0.8, Tail = 0.4 }),
            K(3.1, waking with { LookX = -0.6, EarL = 0.8 }), K(3.6, waking with { Stretch = 0.9, LookY = -0.4, Eye = 0.4, Tail = 0.3 }), K(4.1, waking));
        // Cabecea, se despierta de golpe ofendida (la cola se dispara); luego rumia.
        Activities[PetActivity.Drowsy] = Loop(6.4, K(0, d),
            K(1.8, d with { Eye = 0, Bob = 0.6, Stretch = -0.4, EarL = -0.4, EarR = -0.4, Tail = -0.5 }, Ease.In),
            K(2.5, d with { Eye = 0.8, Bob = -0.2, Stretch = 0.2, Tail = 0.6 }, Ease.Out), K(3.2, d),
            K(4.0, d with { Jaw = 0.7 }), K(4.4, d with { Jaw = -0.7 }), K(4.8, d), K(5.6, d with { LookX = -0.5 }));
        // Orgullosa: cuello alto, mira a un lado, rumia, mira al otro, parpadea y sacude la cola.
        var proud = r with { Stretch = 0.6, Tail = 0.3 };
        Activities[PetActivity.Alert] = Loop(5.4, K(0, r),
            K(0.8, proud with { LookX = -0.8, EarL = 0.8, EarR = 0.3 }), K(1.6, proud with { LookX = -0.8, EarL = 0.8, Jaw = 0.9 }),
            K(1.9, proud with { LookX = -0.8, EarL = 0.8, Jaw = -0.9 }), K(2.2, proud with { LookX = -0.8, EarL = 0.8, Jaw = 0.9 }),
            K(2.5, proud with { LookX = -0.6, EarL = 0.6 }), K(3.3, proud with { LookX = 0.8, EarR = 0.8, EarL = 0.3 }),
            K(3.45, proud with { LookX = 0.8, EarR = 0.8, Eye = 0 }), K(3.6, proud with { LookX = 0.8, EarR = 0.8 }),
            K(3.8, proud with { LookX = 0.8, EarR = 0.8, Tail = 0.8 }), K(4.0, proud with { LookX = 0.8, EarR = 0.8, Tail = 0 }),
            K(4.2, proud with { LookX = 0.6, EarR = 0.6, Tail = 0.6 }), K(4.6, r));
        // Rumia el bocado de lado a lado y, entre bocados, levanta la cabeza satisfecha.
        var eat = r with { Prop = PetProp.Crumb, LookY = 0.2, Stretch = 0.1 };
        Activities[PetActivity.Eating] = Loop(2.4, K(0, eat with { Jaw = -1, Mouth = 0.3 }), K(0.3, eat with { Jaw = 1, Mouth = 0.1 }),
            K(0.6, eat with { Jaw = -1, Mouth = 0.3, EarL = 0.4 }), K(0.9, eat with { Jaw = 1, Mouth = 0.1 }),
            K(1.2, eat with { Stretch = 0.6, Eye = 0.3, Blush = 0.3, Tail = 0.5, LookY = -0.2 }),
            K(1.7, eat with { Stretch = 0.6, Eye = 0.4, Blush = 0.3, Tail = 0.3, LookY = -0.2 }), K(2.1, eat with { Jaw = -0.5, Mouth = 0.2 }));
        // Teclea picoteando el portátil con el hocico al ritmo de sus pezuñas, y a ratos
        // levanta la cabeza a leer lo que ha escrito.
        var work = r with { Prop = PetProp.Laptop, LookY = 0.6, Stretch = 0.1 };
        Activities[PetActivity.Working] = Loop(1.6,
            K(0, work with { Stretch = -0.35, Feet = PetFeet.StepLeft, Jaw = 0.3 }), K(0.2, work),
            K(0.4, work with { Stretch = -0.35, Feet = PetFeet.StepRight, Jaw = -0.3 }), K(0.6, work with { EarR = 0.4 }),
            K(0.8, work with { Stretch = -0.35, Feet = PetFeet.StepLeft }), K(1.0, work),
            K(1.2, work with { LookY = 0.3, Stretch = 0.4, Eye = 0.8, Tail = 0.4, EarL = 0.4 }), K(1.45, work with { Stretch = 0.2, Tail = -0.1 }));
        // La caja va en el lomo, con su manta; trota en el sitio, la cabeza a contratiempo y la cola al compás.
        var load = r with { Prop = PetProp.Saddle, Stretch = 0.7 };
        Activities[PetActivity.Downloading] = Loop(0.7, K(0, load with { Feet = PetFeet.StepLeft, EarL = -0.3, EarR = 0.3, Tail = 0.5 }),
            K(0.175, load with { Tail = 0.2 }),
            K(0.35, load with { Feet = PetFeet.StepRight, EarL = 0.3, EarR = -0.3, Squash = 0.15, Stretch = 0.55, Tail = 0.5 }),
            K(0.525, load with { Tail = 0.2 }));
        // Se sacude la lana de arriba abajo (orejas y cola van y vienen) y se yergue de un
        // golpe de cabeza, orgullosa.
        var shake = r with { Stretch = 0.2, EarL = -0.5, EarR = -0.5, Eye = 0.3 };
        Activities[PetActivity.Sweeping] = Loop(2.4, [
            K(0, r), K(0.3, shake),
            .. Enumerable.Range(0, 8).Select(i => K(0.4 + i * 0.1, shake with { Eye = 0, Shake = i % 2 == 0 ? 1 : -1,
                EarL = i % 2 == 0 ? 0.6 : -0.6, EarR = i % 2 == 0 ? -0.6 : 0.6, Tail = i % 2 == 0 ? 0.6 : -0.2 }, Ease.Linear)),
            K(1.4, shake with { Eye = 0.4, Mouth = 0.2 }),
            K(1.65, proud with { Stretch = 0.9, LookY = -0.5, Eye = 0.3, EarL = 0.9, EarR = 0.9, Tail = 0.8 }, Ease.OutBack),
            K(2.0, proud with { LookY = -0.3, Eye = 0.6, EarL = 0.8, EarR = 0.8, Tail = 0.4 }),
        ]);
        Activities[PetActivity.Yawning] = Once(K(0, d),
            K(0.8, r with { Eye = 0, Mouth = 1, Stretch = 1.2, LookY = -0.7, EarL = -0.5, EarR = -0.5, Tail = 0.3 }, Ease.Out),
            K(1.5, r with { Eye = 0, Mouth = 0.4, Sit = 0.5, BedIn = 0.4, Stretch = 0, Tail = -0.3 }), K(2.6, Asleep));

        // Clic: se echa atrás con las orejas pegadas y resopla (la nubecita, en la ráfaga).
        Reactions[PetReaction.Jump] = Once(K(0, r),
            K(0.12, r with { Stretch = -0.3, EarL = -1, EarR = -1, Eye = 0.4, Squash = 0.25, Tail = 0.8 }, Ease.Out),
            K(0.28, r with { Stretch = 0.5, Mouth = 0.45, EarL = -1, EarR = -1, Eye = 0.3, LookX = -0.4, Tail = 0.9 }, Ease.Out),
            K(0.65, r with { Stretch = 0.5, Mouth = 0.1, EarL = -0.8, EarR = -0.8, Eye = 0.5, LookX = -0.4, Tail = 0.5 }), K(1.0, r));
        // Una cabriola: pezuñas alternas, la cola meneándose y ruborizada.
        var happy = r with { Eye = 0, Blush = 0.9, EarL = 0.6, EarR = 0.6, Stretch = 0.8 };
        Reactions[PetReaction.Hearts] = Once(K(0, r),
            K(0.25, happy with { Tail = 1, Feet = PetFeet.StepLeft, LookY = -0.3 }), K(0.5, happy with { Tail = 0.3, Feet = PetFeet.StepRight, Lean = 0.3 }),
            K(0.75, happy with { Tail = 1, Feet = PetFeet.StepLeft, Lean = -0.3 }), K(1.0, happy with { Tail = 0.3, Feet = PetFeet.StepRight, Lean = 0.3 }),
            K(1.25, happy with { Tail = 1, Lean = -0.2 }), K(1.55, happy with { Eye = 0.1, Blush = 0.6, Tail = 0.5 }), K(1.9, r));
        // Orgullosísima: el cuello estirado, la cola alta y los ojos entornados de satisfacción.
        var vain = r with { Stretch = 1.1, Eye = 0.2, EarL = 0.9, EarR = 0.9, LookY = -0.5, Blush = 0.3, Tail = 1 };
        Reactions[PetReaction.Sparkle] = Once(K(0, r), K(0.35, vain, Ease.Out), K(0.9, vain with { Tail = 0.7 }), K(1.3, r));
        // Mareada: el cuello oscila cada vez menos, con las orejas descompuestas.
        Reactions[PetReaction.Dizzy] = Once(K(0, r),
            K(0.35, r with { Eye = 0.4, Lean = -0.6, LookX = -1, EarL = -0.8, EarR = 0.6, Stretch = -0.2, Tail = -0.4 }),
            K(0.7, r with { Eye = 0.4, Lean = 0.5, LookX = 1, EarL = 0.6, EarR = -0.8, Tail = 0.3 }),
            K(1.05, r with { Eye = 0.4, Lean = -0.35, LookX = -0.8, Tail = -0.2 }),
            K(1.4, r with { Eye = 0.5, Lean = 0.2 }), K(1.9, r));
        // Dramática: se tumba en el suelo con el cuello por los suelos y la cola caída.
        var drama = r with { Eye = 0.3, LookY = 0.7, Sit = 0.5, Stretch = -1, EarL = -1, EarR = -1, Tail = -1 };
        Reactions[PetReaction.Sad] = Once(K(0, r), K(0.5, drama), K(1.9, drama with { Eye = 0.35, EarL = -0.9, EarR = -0.9 }), K(2.4, r));
        Reactions[PetReaction.Nap] = Once(K(0, r with { Eye = 0.5 }), K(0.6, r with { Eye = 0, Mouth = 0.8, Stretch = 1 }),
            K(1.3, Asleep with { BedIn = 0.6 }), K(2, Asleep));
        // El final de la limpieza: una última sacudida y queda la lana esponjosa, cola en alto.
        ContextReactions[(PetReaction.Sparkle, PetActivity.Sweeping)] = Once(K(0, shake with { Eye = 0.4 }),
            K(0.25, shake with { Eye = 0, Shake = 0.7, Tail = 0.6 }, Ease.Linear), K(0.35, shake with { Eye = 0, Shake = -0.6, Tail = -0.2 }, Ease.Linear),
            K(0.45, shake with { Eye = 0, Shake = 0.4, Tail = 0.5 }, Ease.Linear), K(0.55, shake with { Eye = 0 }, Ease.Linear),
            K(0.9, vain, Ease.Out), K(1.5, vain with { Tail = 0.8 }), K(1.9, r));
        ContextDrips[(PetReaction.Sparkle, PetActivity.Sweeping)] = D((ParticleKind.Sparkle, 0.1));
        ContextBursts[(PetReaction.Sparkle, PetActivity.Sweeping)] = [(ParticleKind.Dust, 4)];

        // Dormida en su cojín: mueve una oreja, resopla bajito, tararea en sueños.
        var a = Asleep;
        InBed(PetReaction.Jump, Once(K(0, a), K(0.15, a with { EarL = 0.6 }, Ease.Out), K(0.35, a with { EarL = -0.6, Mouth = 0.3, Eye = 0.3 }),
            K(0.7, a with { Eye = 0.3, Tail = -0.2 }), K(1.0, a)), burst: [(ParticleKind.Puff, 1)]);
        InBed(PetReaction.Hearts, Once(K(0, a), K(0.4, a with { Blush = 0.7, Mouth = 0.2, Lean = -0.3 }), K(1.0, a with { Blush = 0.7, Mouth = 0.2, Lean = 0.3, Tail = 0 }),
            K(1.6, a with { Blush = 0.6, Lean = -0.2 }), K(2.0, a)), D((ParticleKind.Note, 0.5), (ParticleKind.Heart, 0.5)));
        InBed(PetReaction.Sparkle, Once(K(0, a), K(0.4, a with { Eye = 0.2, Blush = 0.3, EarL = -0.2, EarR = -0.2 }), K(1.0, a with { Eye = 0.2 }), K(1.4, a)),
            D((ParticleKind.Sparkle, 0.1)));
        InBed(PetReaction.Dizzy, Once(K(0, a), K(0.35, a with { Eye = 0.3, EarL = -1, EarR = 0.3, Lean = -0.3 }),
            K(0.7, a with { Eye = 0.3, EarL = 0.3, EarR = -1, Lean = 0.3 }), K(1.05, a with { Eye = 0.3, EarL = -1, EarR = 0.3 }), K(1.5, a)),
            burst: [(ParticleKind.Star, 3)]);
        InBed(PetReaction.Sad, Once(K(0, a), K(0.5, a with { EarL = -1, EarR = -1, Stretch = -0.8, Eye = 0.2, Tail = -1 }),
            K(1.9, a with { EarL = -1, EarR = -1, Stretch = -0.8, Eye = 0.2, Tail = -1 }), K(2.4, a)), D((ParticleKind.Tear, 0.5)));

        Gestures[PetGesture.Chew] = Once(K(0, r), K(0.3, r with { Jaw = 1, Eye = 0.7, LookX = 0.3 }),
            K(0.6, r with { Jaw = -1, Eye = 0.7, LookX = 0.3 }), K(0.9, r with { Jaw = 1, Eye = 0.7, LookX = 0.3 }),
            K(1.2, r with { Jaw = -1, Eye = 0.7, LookX = 0.3, Tail = 0.4 }), K(1.5, r with { Jaw = 1, Eye = 0.7, LookX = 0.3 }),
            K(1.8, r with { Jaw = -1, Eye = 0.7, LookX = 0.3 }), K(2.1, r with { Eye = 0.7 }), K(2.5, r));
        // Una oreja, la otra y un latigazo de cola.
        Gestures[PetGesture.EarFlick] = Once(K(0, r), K(0.15, r with { EarL = -0.9 }), K(0.3, r with { EarL = 0.7 }), K(0.45, r),
            K(0.8, r with { EarR = -0.9 }), K(0.95, r with { EarR = 0.7 }), K(1.1, r with { Tail = 0.9 }), K(1.25, r with { Tail = -0.3 }), K(1.5, r));
        // Tararea con los ojos cerrados, meciendo la cabeza y la cola (las notas, en sus partículas).
        var hum = r with { Eye = 0, Mouth = 0.2, Blush = 0.3 };
        Gestures[PetGesture.Hum] = Once(K(0, r), K(0.4, hum with { Lean = -0.4, Tail = 0.5 }), K(1.0, hum with { Lean = 0.4, Tail = -0.1 }),
            K(1.6, hum with { Lean = -0.4, Tail = 0.5 }), K(2.2, hum with { Lean = 0.4, Tail = -0.1 }), K(2.8, r with { Eye = 0.2 }), K(3.2, r));
        var tall = r with { Stretch = 1.3, LookY = -0.8, EarL = 0.9, EarR = 0.9, Eye = 0.6, Tail = 0.9 };
        Gestures[PetGesture.Stretch] = Once(K(0, r), K(0.3, r with { Stretch = 0, Squash = 0.2, Tail = -0.2 }), K(0.9, tall, Ease.Out),
            K(1.6, tall with { Stretch = 1.2 }), K(2.3, r));
        Gestures[PetGesture.LookAround] = Once(K(0, r), K(0.6, r with { LookX = -1, EarL = 1, Stretch = 0.7, Lean = -0.3 }),
            K(1.5, r with { LookX = 1, EarR = 1, Stretch = 0.7, Lean = 0.3 }), K(2.4, r with { LookY = -0.6, EarL = 0.5, EarR = 0.5, Stretch = 0.9, Tail = 0.5 }), K(3, r));
        Gestures[PetGesture.Nod] = Once(K(0, d), K(1, d with { Eye = 0, Bob = 0.8, Stretch = -0.5, EarL = -0.5, EarR = -0.5 }, Ease.In),
            K(1.5, d with { Eye = 0.9, Bob = -0.3, Stretch = 0.3, Tail = 0.5 }, Ease.Out), K(2.6, d));
        Gestures[PetGesture.Yawn] = Once(K(0, r), K(0.6, r with { Eye = 0, Mouth = 1, Stretch = 1, EarL = -0.5, EarR = -0.5 }),
            K(1.3, r with { Eye = 0.3, Mouth = 0.5 }), K(2, r));
        AwakeGestures = [(PetGesture.Chew, 4), (PetGesture.LookAround, 3), (PetGesture.Stretch, 3), (PetGesture.EarFlick, 3), (PetGesture.Hum, 2)];
        DrowsyGestures = [(PetGesture.Nod, 4), (PetGesture.Yawn, 3), (PetGesture.Chew, 2), (PetGesture.EarFlick, 1)];

        // Esperando un modelo: agachada tras el logo, sube el cuello como un periscopio,
        // mira a los lados, se agacha de golpe y vuelve a asomar, ruborizada.
        PeekStill = r with { Behind = true, PeekY = -4, Sit = 1, Squash = 0.4, Stretch = -0.6 };
        var up = PeekStill with { PeekY = 3, Stretch = 1, EarL = 0.8, EarR = 0.8 };
        Peek = Loop(8, K(0, PeekStill), K(1.0, PeekStill), K(2.0, up, Ease.Out), K(2.6, up with { LookX = -1 }),
            K(3.4, up with { LookX = 1 }), K(3.55, up with { LookX = 1, Eye = 0 }), K(3.7, up with { LookX = 1 }), K(4.0, up with { LookX = 1 }),
            K(4.5, PeekStill with { Stretch = 0.2, Blush = 0.5 }, Ease.In), K(5.6, PeekStill with { Blush = 0.4 }),
            K(6.6, PeekStill with { PeekY = 1.5, Stretch = 0.8, LookX = -0.6, Blush = 0.3 }), K(7.4, PeekStill));

        ActivityDrips[PetActivity.DeepSleep] = D((ParticleKind.Z, 1.5), (ParticleKind.Note, 3.2));
        ActivityDrips[PetActivity.Working] = D((ParticleKind.Note, 1.6));   // tararea mientras trabaja
        ActivityDrips[PetActivity.Eating] = D((ParticleKind.Sparkle, 0.5));
        ActivityDrips[PetActivity.Sweeping] = D((ParticleKind.Dust, 0.08));
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
        PetActivity.WakingUp => Rest with { Eye = 0.5, Stretch = 1, Tail = 0.4 },
        PetActivity.Drowsy => Dozy,
        PetActivity.Alert => Rest with { Stretch = 0.6, Tail = 0.3 },
        PetActivity.Eating => Rest with { Prop = PetProp.Crumb, Jaw = 0.8, Mouth = 0.3, LookY = 0.2, Stretch = 0.1 },
        PetActivity.Working => Rest with { Prop = PetProp.Laptop, LookY = 0.6, Stretch = -0.35, Feet = PetFeet.StepLeft },
        PetActivity.Downloading => Rest with { Prop = PetProp.Saddle, Stretch = 0.7, Tail = 0.4 },
        PetActivity.Sweeping => Rest with { Stretch = 0.2, Shake = 0.6, EarL = 0.6, EarR = -0.6, Eye = 0, Tail = 0.6 },
        _ => base.Still(activity),
    };

    /// Al despertar en el cojín: estira el cuello todo lo que da, bosteza y rumia un poco.
    public override PetClip? Transition(PetActivity from, PetActivity to, bool fromHiding, bool toHiding)
    {
        if (fromHiding || toHiding || to != PetActivity.WakingUp || !from.InBed()) return base.Transition(from, to, fromHiding, toHiding);
        var r = Rest;
        return Once(K(0, Asleep), K(0.4, Asleep with { Eye = 0.3 }), K(0.9, Dozy with { Eye = 0.1 }),
            K(1.7, r with { Eye = 0.1, Stretch = 1.1, Mouth = 0.9, EarL = 0.8, EarR = 0.8, LookY = -0.6, Tail = 0.6 }, Ease.Out),
            K(2.4, r with { Eye = 0.2, Stretch = 1, Mouth = 0.5, Tail = 0.3 }), K(2.9, r with { Eye = 0.35, Jaw = 0.8 }),
            K(3.3, r with { Eye = 0.35, Jaw = -0.8 }), K(3.8, For(to).At(0)));
    }

    /// Estira el cuello hacia el cursor, se inclina hacia él, le orienta las orejas y mueve la cola.
    public override PetPose Hover(PetPose pose, double hoverX, double hoverY, double seconds) => pose with
    {
        Stretch = Math.Max(pose.Stretch, 0.8 - 0.4 * hoverY), Lean = 0.7 * hoverX,
        EarL = 0.6 - 0.4 * hoverX, EarR = 0.6 + 0.4 * hoverX, Tail = 0.5 + 0.3 * Math.Sin(seconds * 5),
    };

    /// Trota con la cabeza alta, que bota a contratiempo, y la cola que se mece.
    public override PetPose Walk(double distance, int facing, double lean, bool turning)
    {
        double phase = distance * Math.PI / 3;
        return Rest with
        {
            Feet = turning ? PetFeet.Stand : Math.Sin(phase) > 0 ? PetFeet.StepLeft : PetFeet.StepRight,
            Bob = turning ? 0 : -Math.Abs(Math.Sin(phase)) * 0.5, Lean = lean, LookX = facing * 0.7,
            Stretch = 0.6 + (turning ? 0 : 0.12 * Math.Cos(2 * phase)),
            EarL = Math.Sin(phase) * 0.3, EarR = -Math.Sin(phase) * 0.3, Tail = 0.3 + 0.25 * Math.Sin(phase),
        };
    }

    public override (double Dx, double Dy) Motion(PetActivity activity, double seconds) => activity switch
    {
        PetActivity.Downloading => (0, -Math.Abs(Math.Sin(Math.PI * seconds / 0.35)) * 0.8),
        PetActivity.Sweeping => (For(activity).At(seconds).Shake * 0.4, 0),   // el cuerpo va con la sacudida
        _ => (0, 0),
    };

    public override (double Dx, double Dy) Motion(PetReaction reaction, double seconds)
    {
        double t = Math.Max(0, seconds);
        return reaction switch
        {
            PetReaction.Jump => (t is > 0.12 and < 0.6 ? -0.8 * Math.Sin(Math.PI * (t - 0.12) / 0.48) : 0, 0),   // da un respingo atrás
            PetReaction.Hearts => (0, -Math.Abs(Math.Sin(Math.PI * Math.Min(t, 1.5) / 0.25)) * 0.8),            // cabriolas
            PetReaction.Sparkle => (0, -Math.Sin(Math.PI * Math.Min(t, 1.3) / 1.3) * 1.5),
            PetReaction.Dizzy => (Math.Sin(2 * Math.PI * t / 0.5) * Math.Max(0, 1 - t / 1.9), 0),
            _ => (0, 0),
        };
    }

    /// El final de la limpieza: el cuerpo va con la última sacudida.
    protected override (double Dx, double Dy) ContextMotion(PetReaction reaction, double seconds, PetContext during) =>
        reaction == PetReaction.Sparkle && during.Activity == PetActivity.Sweeping
            ? (For(reaction, during).At(seconds).Shake * 0.4, 0) : Motion(reaction, seconds);

    public override double Breath(PetActivity activity, double seconds) =>
        activity is PetActivity.Alert or PetActivity.Drowsy or PetActivity.Working or PetActivity.Eating
            ? 0.12 * Math.Sin(2 * Math.PI * seconds / 3) : 0;
}
