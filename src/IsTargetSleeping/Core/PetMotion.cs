namespace IsTargetSleeping;

/// Las animaciones de cada actividad, reacción, gesto y transición, iguales para todas
/// las especies: claves que se interpolan (se muestrean a 60 fps sin saltos). Encima, el
/// movimiento de la ventana (`Motion`) para los saltos y botes que mueven a toda la mascota.
public static class PetAnimations
{
    private static PetKey K(double t, PetPose pose, Ease ease = Ease.InOut) => new(t, pose, ease);

    private static readonly PetPose Rest = new();
    /// En la cama, tapada (el último fotograma del bostezo y la base de dormir).
    public static readonly PetPose Asleep = new(Eye: 0, Sit: 1, Squash: 0.55, ArmL: PetArm.Down, ArmR: PetArm.Down, BedIn: 1, Tint: PetTint.Sleepy);

    public static PetClip For(PetActivity activity) => activity switch
    {
        // Respira despacio (inspira más rápido de lo que espira).
        PetActivity.DeepSleep => new([K(0, Asleep), K(1.4, Asleep with { Squash = 0.95 })], length: 3.4),
        // Se estira bostezando y se frota el ojo.
        PetActivity.WakingUp => new([
            K(0, new(Eye: 0, Squash: 0.5, ArmL: PetArm.Down, ArmR: PetArm.Down)),
            K(0.25, new(Eye: 0)),
            K(0.9, new(Eye: 0, ArmL: PetArm.Up, ArmR: PetArm.Up, Squash: -0.8, Mouth: 1), Ease.OutBack),
            K(1.6, new(Eye: 0, ArmL: PetArm.Up, ArmR: PetArm.Up, Squash: -0.8, Mouth: 0.8)),
            K(1.9, new(Eye: 0)),
            K(2.2, new(Eye: 0.4, ArmL: PetArm.Rub, ArmR: PetArm.Down)),
            K(2.45, new(Eye: 0.05, ArmL: PetArm.Rub, ArmR: PetArm.Down)),
            K(2.7, new(Eye: 0.5, ArmL: PetArm.Rub, ArmR: PetArm.Down)),
            K(2.95, new(Eye: 0.1, ArmL: PetArm.Rub, ArmR: PetArm.Down)),
            K(3.3, new(Eye: 0.5, ArmL: PetArm.Down, ArmR: PetArm.Down)),
            K(4.0, new(Eye: 0.5, ArmL: PetArm.Down, ArmR: PetArm.Down, LookX: 0.7)),
            K(4.6, new(Eye: 0.5, ArmL: PetArm.Down, ArmR: PetArm.Down)),
        ], length: 5.0),
        PetActivity.Drowsy => Drowsy,
        // Levanta un bocado de datos y mastica.
        PetActivity.Eating => new([
            K(0, Eating(0, 0)), K(0.17, Eating(1, 0.3)), K(0.35, Eating(0, 0)), K(0.52, Eating(0.9, 0.25)),
        ], length: 0.7),
        // La pose del logo (bracitos en cruz): mira a los lados y parpadea.
        PetActivity.Alert => new([
            K(0, Rest), K(1.8, Rest), K(1.95, Rest with { Eye = 0 }), K(2.1, Rest),
            K(2.6, Rest with { LookX = -0.85 }), K(3.6, Rest with { LookX = -0.85 }), K(4.0, Rest with { LookX = 0.1 }),
            K(4.5, Rest with { LookX = 0.85 }), K(5.4, Rest with { LookX = 0.85 }),
            K(5.55, Rest with { LookX = 0.85, Eye = 0 }), K(5.7, Rest with { LookX = 0.85 }),
            K(6.1, Rest),
        ], length: 7.2),
        // Teclea en un portátil diminuto, a seis teclas por segundo, mirando la pantalla.
        PetActivity.Working => new(Enumerable.Range(0, 12).Select(i => K(i / 6.0, Typing(i % 2 == 0) with
        {
            Eye = i == 9 ? 0.3 : 1,
        }, Ease.Out)).ToArray(), length: 2.0),
        // Una caja sobre la cabeza, corriendo en el sitio.
        PetActivity.Downloading => new([
            K(0, Box(PetFeet.StepLeft, 0.2)), K(0.125, Box(PetFeet.Stand, 0)),
            K(0.25, Box(PetFeet.StepRight, 0.2)), K(0.375, Box(PetFeet.Stand, 0)),
        ], length: 0.5),
        // Barre a un lado y al otro.
        PetActivity.Sweeping => new([
            K(0, Broom(-1, PetFeet.StepLeft)), K(0.7, Broom(1, PetFeet.StepRight)),
        ], length: 1.4),
        // Bosteza, se estira y se mete en la cama (se queda dentro).
        PetActivity.Yawning => new([
            K(0, new(Eye: 0.5, ArmL: PetArm.Down, ArmR: PetArm.Down)),
            K(0.25, new(Eye: 0, Mouth: 0.4)),
            K(0.9, new(Eye: 0, ArmL: PetArm.Up, ArmR: PetArm.Up, Squash: -0.8, Mouth: 1), Ease.OutBack),
            K(1.3, new(Eye: 0, ArmL: PetArm.Up, ArmR: PetArm.Up, Squash: -0.8, Mouth: 0.9)),
            K(1.6, new(Eye: 0)),
            K(1.9, Asleep with { Sit = 0.6, BedIn = 0.6, Squash = 0.4 }),
            K(2.4, Asleep),
        ], loop: false),
        _ => PetClip.Hold(Rest),
    };

    private static readonly PetPose DrowsyPose = new(Eye: 0.45, ArmL: PetArm.Down, ArmR: PetArm.Down);

    /// Encendido sin modelo: sentada, cabecea de sueño, se sobresalta y mira alrededor.
    private static readonly PetClip Drowsy = new([
        K(0, DrowsyPose),
        K(1.6, DrowsyPose with { Bob = 0.5, Eye = 0.3 }),
        K(2.3, DrowsyPose with { Bob = 0.9, Eye = 0 }, Ease.In),
        K(2.45, DrowsyPose with { Bob = -0.3, Eye = 0.75 }, Ease.Out),   // ¡se despierta de golpe!
        K(2.9, DrowsyPose),
        K(4.0, DrowsyPose with { LookX = -0.7 }), K(4.6, DrowsyPose with { LookX = -0.7 }),
        K(5.2, DrowsyPose), K(5.6, DrowsyPose with { Eye = 0 }), K(5.9, DrowsyPose),
        K(6.4, DrowsyPose with { LookX = 0.6 }),
    ], length: 7.0);

    private static PetPose Eating(double mouth, double squash) =>
        new(Mouth: mouth, Squash: squash, ArmL: PetArm.Out, ArmR: PetArm.Up, Prop: PetProp.Crumb);

    private static PetPose Typing(bool left) => new(Prop: PetProp.Laptop, LookY: 0.55,
        ArmL: left ? PetArm.Type : 52, ArmR: left ? 52 : PetArm.Type);

    private static PetPose Box(PetFeet feet, double squash) =>
        new(Prop: PetProp.Box, ArmL: PetArm.Up, ArmR: PetArm.Up, Feet: feet, Squash: squash);

    private static PetPose Broom(double sway, PetFeet feet) =>
        new(Prop: PetProp.Broom, Sway: sway, LookX: 0.6 * sway, ArmL: PetArm.Down, ArmR: PetArm.Down, Feet: feet);

    public static PetClip For(PetReaction reaction) => reaction switch
    {
        // Se agacha, salta (la parábola la hace `Motion`) y amortigua al caer.
        PetReaction.Jump => new([
            K(0, Rest), K(0.09, new(Squash: 0.8, ArmL: PetArm.Down, ArmR: PetArm.Down), Ease.Out),
            K(0.22, new(Squash: -0.6, ArmL: PetArm.Up, ArmR: PetArm.Up), Ease.Linear),
            K(0.32, Rest), K(0.4, new(Squash: 0.7, ArmL: PetArm.Down, ArmR: PetArm.Down), Ease.Linear),
            K(0.5, Rest, Ease.Out),
        ], loop: false),
        PetReaction.Sparkle => new([
            K(0, Rest), K(0.12, Cheer(-0.3)), K(0.3, Rest with { Squash = 0.1 }), K(0.45, Cheer(-0.3)),
            K(0.65, Rest), K(0.8, Cheer(-0.2)), K(1.0, Rest),
        ], loop: false),
        // Mareada: el ojo da vueltas de un lado a otro.
        PetReaction.Dizzy => new(Enumerable.Range(0, 9).Select(i => K(i * 0.18, new PetPose(
            Eye: 0.5, LookX: i == 0 || i == 8 ? 0 : i % 2 == 0 ? 1 : -1, LookY: i % 2 == 0 ? 0.3 : -0.3,
            Squash: i % 2 == 0 ? 0.15 : -0.1))).Append(K(1.5, Rest)).ToArray(), loop: false),
        PetReaction.Sad => new([
            K(0, Rest), K(0.4, new(Eye: 0.35, Squash: 0.6, ArmL: PetArm.Down, ArmR: PetArm.Down, LookY: 0.6)),
            K(1.7, new(Eye: 0.35, Squash: 0.6, ArmL: PetArm.Down, ArmR: PetArm.Down, LookY: 0.6)), K(2.0, Rest),
        ], loop: false),
        PetReaction.Nap => new([
            K(0, new(Eye: 0.5)),
            K(0.45, new(Eye: 0, ArmL: PetArm.Up, ArmR: PetArm.Up, Squash: -0.7, Mouth: 1), Ease.OutBack),
            K(0.9, new(Eye: 0, ArmL: PetArm.Down, ArmR: PetArm.Down, Mouth: 0.3, Squash: 0.2)),
            K(1.6, Asleep),
        ], loop: false),
        // Feliz (ojo cerrado y sonrojada), bracitos arriba y abajo.
        PetReaction.Hearts => new([
            K(0, Rest), K(0.15, Happy(PetArm.Up, 0.3)), K(0.35, Happy(PetArm.Out, -0.2)), K(0.55, Happy(PetArm.Up, 0.3)),
            K(0.75, Happy(PetArm.Out, -0.2)), K(0.95, Happy(PetArm.Up, 0.2)), K(1.3, Rest),
        ], loop: false),
        _ => PetClip.Hold(Rest),
    };

    private static PetPose Cheer(double squash) => new(ArmL: PetArm.Up, ArmR: PetArm.Up, Squash: squash);
    private static PetPose Happy(double arms, double squash) => new(Eye: 0, ArmL: arms, ArmR: arms, Squash: squash, Blush: 0.8);

    // MARK: gestos al estar quieta (para que no se repita en bucle)

    public static PetClip Gesture(PetGesture gesture) => gesture switch
    {
        PetGesture.Stretch => new([
            K(0, Rest), K(0.5, new(ArmL: PetArm.Up, ArmR: PetArm.Up, Squash: -0.7, Eye: 0.3, Mouth: 0.3), Ease.Out),
            K(1.2, new(ArmL: PetArm.Up - 10, ArmR: PetArm.Up - 10, Squash: -0.8, Eye: 0, Mouth: 0.4)),
            K(1.5, new(Squash: 0.3), Ease.InOut), K(1.8, Rest, Ease.OutBack),
        ], loop: false),
        // Se rasca la antena con la mano derecha.
        PetGesture.Scratch => new([
            K(0, Rest), K(0.35, Scratch(-115)), K(0.5, Scratch(-95)), K(0.65, Scratch(-115)), K(0.8, Scratch(-95)),
            K(0.95, Scratch(-115)), K(1.1, Scratch(-95)), K(1.5, Scratch(-110)), K(2.0, Rest),
        ], loop: false),
        PetGesture.Yawn => new([
            K(0, Rest), K(0.5, new(Eye: 0.1, Mouth: 1, ArmL: PetArm.Up + 20, ArmR: PetArm.Up + 20, Squash: -0.4), Ease.Out),
            K(1.1, new(Eye: 0, Mouth: 0.9, ArmL: PetArm.Up + 20, ArmR: PetArm.Up + 20, Squash: -0.4)),
            K(1.4, new(Eye: 0.4, Mouth: 0)), K(1.7, Rest),
        ], loop: false),
        PetGesture.LookAround => new([
            K(0, Rest), K(0.5, Rest with { LookX = -1, LookY = -0.3 }), K(1.1, Rest with { LookX = -1, LookY = -0.3 }),
            K(1.5, Rest with { LookX = 1, LookY = -0.4 }), K(2.1, Rest with { LookX = 1, LookY = -0.4 }), K(2.6, Rest),
        ], loop: false),
        // Saluda con la mano.
        PetGesture.Wave => new([
            K(0, Rest), K(0.3, Waving(-70)), K(0.5, Waving(-30)), K(0.7, Waving(-70)), K(0.9, Waving(-30)),
            K(1.1, Waving(-70)), K(1.7, Rest),
        ], loop: false),
        // Un saltito en el sitio (el bote lo hace `Motion`).
        PetGesture.Hop => new([
            K(0, Rest), K(0.1, new(Squash: 0.7), Ease.Out), K(0.25, new(Squash: -0.4, ArmL: PetArm.Up, ArmR: PetArm.Up), Ease.Out),
            K(0.48, new(Squash: 0.5), Ease.In), K(0.7, Rest, Ease.OutBack),
        ], loop: false),
        // Cabecea de sueño y se despierta de golpe.
        PetGesture.Nod => new([
            K(0, DrowsyPose), K(0.9, DrowsyPose with { Bob = 0.9, Eye = 0 }, Ease.In),
            K(1.1, DrowsyPose with { Bob = -0.4, Eye = 0.9 }, Ease.Out), K(1.4, DrowsyPose with { LookX = -0.5 }),
            K(2.2, DrowsyPose),
        ], loop: false),
        _ => PetClip.Hold(Rest),
    };

    private static PetPose Scratch(double arm) => new(ArmR: arm, Eye: 0.5, LookX: 0.5, Squash: 0.15);
    private static PetPose Waving(double arm) => new(ArmR: arm, Blush: 0.3);

    // MARK: transiciones entre actividades

    /// Lo que hace al pasar de una actividad a otra (antes de su bucle), o null si basta
    /// con mezclar las dos poses. `fromHiding`/`toHiding`: escondida tras el logo.
    public static PetClip? Transition(PetActivity from, PetActivity to, bool fromHiding, bool toHiding)
    {
        if (fromHiding && !toHiding)
            // Sale de detrás del logo de un saltito.
            return new([
                K(0, Hiding(0, 1, 0, 0)), K(0.15, new(Squash: 0.7, Sit: 0.3), Ease.Out),
                K(0.32, new(Squash: -0.5, Bob: -1.2, ArmL: PetArm.Up, ArmR: PetArm.Up), Ease.Out),
                K(0.5, Rest, Ease.OutBack),
            ], loop: false);
        if (!fromHiding && toHiding)
            // Se agacha y se mete detrás.
            return new([K(0, Rest), K(0.2, new(Squash: 0.9, Sit: 0.5)), K(0.45, Hiding(0, 1, -1, 0))], loop: false);
        if (from == PetActivity.DeepSleep && to != PetActivity.Hidden)
            // Se destapa, se sienta y la cama se va.
            return new([
                K(0, Asleep), K(0.35, Asleep with { Eye = 0.3 }), K(0.7, Asleep with { Eye = 0.2, Sit = 0.5, Squash = 0.2, Tint = PetTint.Normal }),
                K(1.1, new(Eye: 0.5, Squash: -0.3, ArmL: PetArm.Up, ArmR: PetArm.Up, BedIn: 0.3)),
                K(1.4, new(Eye: 0.6, ArmL: PetArm.Down, ArmR: PetArm.Down)),
            ], loop: false);
        if (to == PetActivity.DeepSleep && from != PetActivity.Yawning)
            // Aparece la cama y se mete dentro.
            return new([K(0, Rest), K(0.35, new(Squash: 0.5, Eye: 0.4, BedIn: 0.5)), K(1.0, Asleep)], loop: false);
        var prop = PropOf(to);
        var old = PropOf(from);
        if (prop != PetProp.None && prop != old)
        {
            // Saca el accesorio: entra deslizándose.
            var first = For(to).At(0);
            return new([K(0, first with { PropIn = 0 }), K(0.5, first, Ease.Out)], loop: false);
        }
        if (old != PetProp.None && prop != old)
        {
            // Lo guarda (y si venía de comer, se relame y da un saltito).
            var last = For(from).At(0);
            return from == PetActivity.Eating
                ? new([K(0, last), K(0.2, last with { Mouth = 1 }), K(0.4, last with { Mouth = 0, PropIn = 0 }),
                    K(0.6, new(Bob: -1, Squash: -0.3), Ease.Out), K(0.8, Rest, Ease.OutBack)], loop: false)
                : new([K(0, last), K(0.45, last with { PropIn = 0 }, Ease.In)], loop: false);
        }
        return null;
    }

    private static PetProp PropOf(PetActivity activity) => activity switch
    {
        PetActivity.Working => PetProp.Laptop,
        PetActivity.Downloading => PetProp.Box,
        PetActivity.Sweeping => PetProp.Broom,
        PetActivity.Eating => PetProp.Crumb,
        _ => PetProp.None,
    };

    // MARK: escondida detrás del logo

    /// Esperando a que arranque un modelo: tras el logo, se asoma por su izquierda (`Peek`,
    /// en píxeles de rejilla) curiosa; mira hacia el logo («¿ya viene?»), se esconde de
    /// golpe y vuelve tímida, sonrojada y con el ojo entrecerrado.
    public static readonly PetClip Peek = new([
        K(0, Hiding(0, 1, -1, 0)), K(1.0, Hiding(0, 1, -1, 0)), K(1.8, Hiding(3, 1, -1, 0)),
        K(2.35, Hiding(3, 1, -1, 0)), K(2.5, Hiding(3, 0, -1, 0)), K(2.65, Hiding(3, 1, -1, 0)),
        K(3.0, Hiding(3, 1, -1, 0)), K(3.25, Hiding(3, 1, 1, 0)), K(3.7, Hiding(3, 1, 1, 0)), K(3.85, Hiding(3, 1, -1, 0)),
        K(4.15, Hiding(0, 0.8, -1, 0.6), Ease.In),
        K(4.6, Hiding(0, 0.6, -1, 1)), K(5.2, Hiding(0, 0.5, -1, 1)), K(5.9, Hiding(2, 0.5, -1, 1)),
        K(7.0, Hiding(2, 0.55, -1, 0.8)), K(7.6, Hiding(0, 0.8, -1, 0.3)),
    ], length: 8.0);

    public static PetPose PeekStill => Hiding(2, 1, -1, 0);

    private static PetPose Hiding(double peek, double eye, double look, double blush) =>
        new(Eye: eye, LookX: look, Squash: 0.9, Sit: 1, ArmL: PetArm.Down, OneArm: true, Behind: true, Peek: peek, Blush: blush);

    // MARK: caminar

    /// Un paso con cada pie según la distancia recorrida (para que no patinen), con un
    /// bote por paso, los brazos balanceándose y mirando hacia donde va.
    public static PetPose Walk(double distance, int facing, double lean, bool turning)
    {
        double phase = distance / 6.0;   // un ciclo (dos pasos) cada 6 píxeles de rejilla
        int q = (int)Math.Floor(phase * 4) & 3;
        double swing = 25 * Math.Sin(2 * Math.PI * phase);
        return new(
            Feet: q == 0 ? PetFeet.StepLeft : q == 2 ? PetFeet.StepRight : PetFeet.Stand,
            Bob: -0.8 * Math.Abs(Math.Sin(2 * Math.PI * phase)), ArmL: 20 + swing, ArmR: 20 - swing,
            LookX: 0.7 * facing, Lean: lean, Squash: turning ? 0.4 : 0);
    }

    // MARK: movimiento de la ventana y respiración

    /// Desplazamiento de toda la mascota (en píxeles de rejilla, con decimales).
    public static (double Dx, double Dy) Motion(PetActivity activity, double seconds)
    {
        double t = Math.Max(0, seconds);
        return activity switch
        {
            PetActivity.Downloading => (0, -1.5 * Math.Abs(Math.Sin(Math.PI * t / 0.25))),   // botes al correr
            PetActivity.Sweeping => (Math.Sin(2 * Math.PI * t / 1.4), 0),                    // va y viene con la escoba
            _ => (0, 0),
        };
    }

    public static (double Dx, double Dy) Motion(PetReaction reaction, double seconds)
    {
        double t = Math.Max(0, seconds);
        switch (reaction)
        {
            case PetReaction.Jump: return (0, Arc(t, 0.09, 0.4, 5));
            case PetReaction.Sparkle: return (0, -1.5 * Math.Abs(Math.Sin(Math.PI * t / 0.5)));
            case PetReaction.Hearts: return (0.75 * Math.Sin(2 * Math.PI * t / 0.6), 0);
            case PetReaction.Dizzy: return (Math.Sin(2 * Math.PI * t / 0.5) * Math.Max(0, 1 - t / 1.5), 0);
            default: return (0, 0);
        }
    }

    public static (double Dx, double Dy) Motion(PetGesture gesture, double seconds) =>
        gesture == PetGesture.Hop ? (0, Arc(seconds, 0.1, 0.48, 2.5)) : (0, 0);

    /// En el aire entre `start` y `end`: una parábola de `height` píxeles de rejilla.
    private static double Arc(double t, double start, double end, double height)
    {
        if (t <= start || t >= end) return 0;
        double p = (t - start) / (end - start);
        return -height * 4 * p * (1 - p);
    }

    /// Respira (se hincha y deshincha un poco) mientras está despierta.
    public static double Breath(PetActivity activity, double seconds) =>
        activity is PetActivity.Alert or PetActivity.Drowsy or PetActivity.Working or PetActivity.Eating or PetActivity.WakingUp
            ? 0.12 * Math.Sin(2 * Math.PI * seconds / 2.8) : 0;

    // MARK: «Movimiento reducido»: una pose fija que se entiende sola

    public static PetPose Still(PetActivity activity) => activity switch
    {
        PetActivity.DeepSleep or PetActivity.Yawning => Asleep,
        PetActivity.WakingUp => new(Eye: 0.5, ArmL: PetArm.Rub, ArmR: PetArm.Down),
        PetActivity.Drowsy => DrowsyPose,
        PetActivity.Eating => Eating(1, 0),
        PetActivity.Working => Typing(true),
        PetActivity.Downloading => Box(PetFeet.Stand, 0),
        PetActivity.Sweeping => Broom(-1, PetFeet.Stand),
        _ => Rest,
    };

    public static PetPose Still(PetReaction reaction) => reaction switch
    {
        PetReaction.Sparkle => Cheer(0),
        PetReaction.Dizzy => new(Eye: 0.5),
        PetReaction.Sad => new(Eye: 0.35, Squash: 0.6, ArmL: PetArm.Down, ArmR: PetArm.Down),
        PetReaction.Nap => Asleep,
        PetReaction.Hearts => Happy(PetArm.Up, 0),
        _ => Rest,
    };
}

/// Las «luces» del logo de Windows, panel a panel (0 arriba a la izquierda, 1 arriba a la
/// derecha, 2 abajo a la izquierda, 3 abajo a la derecha): color, opacidad de cada panel y
/// un destello en diagonal (`Shine` 0 … 1, o −1 si no hay). Continuas en el tiempo.
public readonly record struct PetGlow(uint Rgb = 0xFFFFFF, double A0 = 0, double A1 = 0, double A2 = 0, double A3 = 0, double Shine = -1)
{
    public PetGlow() : this(Rgb: 0xFFFFFF) { }

    public double this[int pane] => pane switch { 0 => A0, 1 => A1, 2 => A2, _ => A3 };
    public bool Dark => A0 <= 0 && A1 <= 0 && A2 <= 0 && A3 <= 0 && Shine < 0;

    public static PetGlow All(double alpha, uint rgb = 0xFFFFFF, double shine = -1) => new(rgb, alpha, alpha, alpha, alpha, shine);

    public static PetGlow Lerp(PetGlow a, PetGlow b, double k)
    {
        k = Math.Clamp(k, 0, 1);
        double L(double x, double y) => x + (y - x) * k;
        uint C(int shift) => (uint)Math.Round(L(a.Rgb >> shift & 0xFF, b.Rgb >> shift & 0xFF)) << shift;
        return new(C(16) | C(8) | C(0), L(a.A0, b.A0), L(a.A1, b.A1), L(a.A2, b.A2), L(a.A3, b.A3), k < 0.5 ? a.Shine : b.Shine);
    }

    /// Redondeada a lo que se nota (para no repintar si no cambia).
    public PetGlow Quantized() => new(Rgb, Q(A0), Q(A1), Q(A2), Q(A3), Shine < 0 ? -1 : Math.Round(Shine * 48) / 48);
    private static double Q(double a) => Math.Round(Math.Clamp(a, 0, 1) * 64) / 64;
}

public static class PetLights
{
    /// El brillo de «Ollama encendido»: un poco más suave que el de liberar RAM.
    public const double Lit = 0.22;
    private const uint White = 0xFFFFFF, Amber = 0xF5BD57, Pink = 0xFF6FA0;

    /// Con Ollama apagado el logo se queda como lo dibuja Windows; encendido, brilla un
    /// poco y los efectos van encima.
    public static PetGlow For(PetActivity activity, double seconds, bool still)
    {
        double t = Math.Max(0, seconds);
        switch (activity)
        {
            case PetActivity.Drowsy:
                return PetGlow.All(Lit);
            case PetActivity.WakingUp:
            {
                // El brillo llega panel a panel; al final del ciclo se funde y vuelve a empezar.
                if (still) return PetGlow.All(Lit);
                double p = t % 2.0, fade = p < 1.6 ? 1 : 1 - (p - 1.6) / 0.4;
                double Pane(int i) => Lit * Smooth((p - i * 0.3) / 0.3) * fade;
                return new(White, Pane(0), Pane(1), Pane(2), Pane(3));
            }
            case PetActivity.Eating:
                return PetGlow.All(still ? 0.4 : 0.3 + 0.2 * Wave(t, 0.35), Amber);
            case PetActivity.Alert:
            {
                double p = t % 6.0;   // un destello en diagonal cada 6 s
                return PetGlow.All(Lit, White, still || p > 1.2 ? -1 : p / 1.2);
            }
            case PetActivity.Working:
            {
                if (still) return PetGlow.All(Lit);
                // Un panel destella con cada tecla (seis por segundo) y se apaga enseguida.
                int key = (int)Math.Floor(t * 6);
                int pane = (key * 3 + 1) % 4;
                double spike = 0.28 * Math.Exp(-(t - key / 6.0) / 0.09);
                return new(White, Lit + (pane == 0 ? spike : 0), Lit + (pane == 1 ? spike : 0), Lit + (pane == 2 ? spike : 0), Lit + (pane == 3 ? spike : 0));
            }
            case PetActivity.Downloading:
            {
                // Se llena como una barra y se vacía suavemente.
                double p = still ? 1.2 : t % 1.5, level = Math.Min(p, 1.2) / 1.2 * 4, fade = p < 1.2 ? 1 : 1 - (p - 1.2) / 0.3;
                double Pane(int i) => Lit + 0.23 * Math.Clamp(level - i, 0, 1) * fade;
                return new(White, Pane(0), Pane(1), Pane(2), Pane(3));
            }
            case PetActivity.Sweeping:
            {
                double p = t % 1.5;
                return PetGlow.All(Lit, White, still ? -1 : p / 1.5);
            }
            case PetActivity.Yawning:
            {
                // El brillo se va panel a panel y queda el logo de siempre.
                if (still) return new();
                double Pane(int i) => Lit * (1 - Smooth((t - i * 0.4) / 0.4));
                return new(White, Pane(0), Pane(1), Pane(2), Pane(3));
            }
            default:
                return new();
        }
    }

    /// Las luces propias de una reacción, o null si se quedan las de la actividad.
    public static PetGlow? For(PetReaction reaction, double seconds, bool still)
    {
        double t = Math.Max(0, seconds);
        return reaction switch
        {
            PetReaction.Sparkle => PetGlow.All(still ? 0.38 : 0.3 + 0.15 * Wave(t, 0.25)),
            PetReaction.Hearts => PetGlow.All(still ? 0.38 : 0.3 + 0.15 * Wave(t, 0.3), Pink),
            PetReaction.Dizzy => still ? PetGlow.All(Lit) : new(White,
                Lit + 0.2 * Math.Max(0, Math.Sin(2 * Math.PI * (t * 5))), Lit + 0.2 * Math.Max(0, Math.Sin(2 * Math.PI * (t * 5 + 0.37))),
                Lit + 0.2 * Math.Max(0, Math.Sin(2 * Math.PI * (t * 5 + 0.61))), Lit + 0.2 * Math.Max(0, Math.Sin(2 * Math.PI * (t * 5 + 0.83)))),
            PetReaction.Nap => still ? new() : new(White,
                Lit * (1 - Smooth(t / 0.4)), Lit * (1 - Smooth((t - 0.3) / 0.4)), Lit * (1 - Smooth((t - 0.6) / 0.4)), Lit * (1 - Smooth((t - 0.9) / 0.4))),
            _ => null,
        };
    }

    private static double Smooth(double k)
    {
        k = Math.Clamp(k, 0, 1);
        return k * k * (3 - 2 * k);
    }

    private static double Wave(double t, double period) => 0.5 - 0.5 * Math.Cos(2 * Math.PI * t / period);
}

/// Dónde está la mascota en modo paseo, en píxeles físicos. Despierta elige destinos al
/// azar dentro del recorrido: acelera, frena al llegar, y para cambiar de sentido se para
/// y se da la vuelta. Cuando se duerme vuelve a casa (Inicio); si trabaja o la miras, se
/// queda quieta. El azar tiene semilla: con la misma, el mismo camino.
public sealed class PetWalker(int seed = 7)
{
    public const double TurnTime = 0.25;
    private readonly Random random = new(seed);
    private double? target;
    private double restUntil, turnUntil = double.NegativeInfinity;

    public double X { get; private set; } = double.NaN;
    public double Speed { get; private set; }
    /// Lo recorrido en total: el paso de los pies va con esto, no con el tiempo.
    public double Distance { get; private set; }
    public bool Walking => Speed > 0.5;
    public bool Turning { get; private set; }
    /// Hacia dónde mira o camina: −1 izquierda, 1 derecha.
    public int Facing { get; private set; } = 1;
    /// Se inclina al acelerar (hacia delante) y al frenar (hacia atrás): −1 … 1.
    public double Lean { get; private set; }

    public enum Mode { Roam, GoHome, Stay }

    /// `speed` en píxeles por segundo; `min`/`max`: el recorrido; `home`: junto a Inicio.
    public void Update(double now, double dt, int min, int max, int home, Mode mode, double speed)
    {
        if (max < min) max = min;
        home = Math.Clamp(home, min, max);
        if (double.IsNaN(X)) X = home;
        X = Math.Clamp(X, min, max);
        dt = Math.Max(0, dt);
        switch (mode)
        {
            case Mode.Stay: target = null; break;
            case Mode.GoHome: target = home; break;
            default:
                if (target is null && now >= restUntil && Speed == 0)
                {
                    // Un destino a una distancia apreciable, dentro del recorrido.
                    double span = max - min;
                    if (span >= 8)
                    {
                        double next = min + random.NextDouble() * span;
                        if (Math.Abs(next - X) < span * 0.15) next = X > (min + max) / 2.0 ? min + span * 0.1 : max - span * 0.1;
                        target = next;
                    }
                }
                break;
        }

        double accel = speed / 0.35;   // de parada a toda velocidad en 0,35 s
        double before = Speed;
        if (Turning && now >= turnUntil) { Turning = false; Facing = -Facing; }
        if (target is not { } goal || Turning)
        {
            Speed = Math.Max(0, Speed - accel * dt);   // frena
        }
        else
        {
            double gap = goal - X;
            int direction = Math.Sign(gap);
            if (direction != 0 && direction != Facing)
            {
                // Para dar la vuelta: frena, y ya parada se gira.
                Speed = Math.Max(0, Speed - accel * dt);
                if (Speed == 0) { Turning = true; turnUntil = now + TurnTime; }
            }
            else
            {
                double room = Math.Sqrt(2 * accel * Math.Abs(gap));   // frena a tiempo para llegar parada
                Speed = Math.Min(Math.Min(speed, room), Speed + accel * dt);
                if (Math.Abs(gap) <= Math.Max(0.05, Speed * dt))   // solo si el paso siguiente se pasaría
                {
                    Distance += Math.Abs(gap);
                    X = goal;
                    Speed = 0;
                    target = null;
                    if (mode == Mode.Roam) restUntil = now + 2 + random.NextDouble() * 5;   // se para a mirar
                    Lean = 0;
                    return;
                }
            }
        }
        double step = Speed * dt;
        X = Math.Clamp(X + Facing * step, min, max);
        Distance += step;
        double change = dt > 0 ? (Speed - before) / dt / accel : 0;
        Lean = Math.Clamp(change, -1, 1) * 0.6 * Facing;
    }
}
