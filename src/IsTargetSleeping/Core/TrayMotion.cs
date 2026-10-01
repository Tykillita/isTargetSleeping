namespace IsTargetSleeping;

/// Lo que el ícono de la bandeja cuenta de Ollama (Missing se pinta como Off).
public enum TrayPhase { Off, Starting, On, Stopping }

/// Animaciones del ícono: los bucles de radar mientras Ollama cambia de estado y
/// las transiciones cortas entre las poses fijas.
public enum TrayAnimation { None, Searching, LockOn, LettingGo, Off, EyeOpen, EyeClose }

/// Un fotograma del ícono como números, sin WPF (se prueba sin dibujar nada).
/// Ángulos en grados: 0 arriba, en el sentido del reloj.
/// - `Ring`: cuánto anillo sólido se dibuja (1 = entero), centrado abajo: al bajar de 1 se va ◠ → ◡ → ·.
/// - `RingFaint`: el anillo tenue bajo el radar (0 = no está).
/// - `ArcStart`, `ArcLength`, `ArcTint`: el arco del radar y su color (0 = tinta, 1 = ámbar).
/// - `TickShift`: las marcas hacia el centro, en píxeles de la rejilla de 16 (negativo = hacia fuera).
/// - `Eye`: 0 = párpado con pestañas (dormido) … 1 = centro de la diana (despierto).
/// - `Dot`, `DotTint`: escala de la insignia (pasa de 1 al saltar) y su color (0 = azul, 1 = ámbar).
/// - `Ink`: opacidad de la tinta (0,78 apagado … 1 encendido).
public readonly record struct TrayPose(
    double Ring, double RingFaint, double ArcStart, double ArcLength, double ArcTint,
    double TickShift, double Eye, double Dot, double DotTint, double Ink)
{
    public const double DimInk = 0.78;

    public static readonly TrayPose Off = new(1, 0, 0, 0, 0, 0, 0, 0, 0, DimInk);
    public static TrayPose On(bool awake) => new(1, 0, 0, 0, 0, 0, awake ? 1 : 0, 1, 0, 1);
    /// Sin animaciones: la insignia ámbar fija mientras cambia de estado (sin parpadeo).
    public static readonly TrayPose Busy = new(1, 0, 0, 0, 0, 0, 0, 1, 1, 1);

    public static TrayPose Lerp(TrayPose a, TrayPose b, double t)
    {
        // Un arco que no se ve no tiene ángulo: aparece donde está el otro, sin girar.
        double aStart = a.ArcLength <= 0 ? b.ArcStart : a.ArcStart;
        double bStart = b.ArcLength <= 0 ? aStart : b.ArcStart;
        return new(
            L(a.Ring, b.Ring), L(a.RingFaint, b.RingFaint), L(aStart, bStart), L(a.ArcLength, b.ArcLength),
            L(a.ArcTint, b.ArcTint), L(a.TickShift, b.TickShift), L(a.Eye, b.Eye), L(a.Dot, b.Dot),
            L(a.DotTint, b.DotTint), L(a.Ink, b.Ink));
        double L(double x, double y) => x + (y - x) * t;
    }

    /// Igual a efectos visuales (los ángulos, módulo 360).
    public bool Near(TrayPose o, double eps = 1e-6)
    {
        double da = Math.Abs(Mod(ArcStart) - Mod(o.ArcStart));
        return Math.Abs(Ring - o.Ring) < eps && Math.Abs(RingFaint - o.RingFaint) < eps
            && (Math.Min(da, 360 - da) < eps * 360 || ArcLength <= 0 && o.ArcLength <= 0)
            && Math.Abs(ArcLength - o.ArcLength) < eps * 360 && Math.Abs(ArcTint - o.ArcTint) < eps
            && Math.Abs(TickShift - o.TickShift) < eps && Math.Abs(Eye - o.Eye) < eps
            && Math.Abs(Dot - o.Dot) < eps && Math.Abs(DotTint - o.DotTint) < eps && Math.Abs(Ink - o.Ink) < eps;
    }

    public static double Mod(double angle) => (angle % 360 + 360) % 360;
}

/// Decide qué animación toca con cada cambio y calcula la pose en cada instante.
/// El tiempo va en segundos desde cualquier origen (lo pone quien llama). Si el
/// estado cambia a media animación, la siguiente arranca de la pose de ese momento.
public sealed class TrayMotion
{
    public const double Fps = 20;
    /// Una vuelta del radar; 24 fotogramas a 20 fps, que se dibujan una vez y se reutilizan.
    public const double Turn = 1.2;
    public const int LoopFrames = 24;
    public const double RadarArc = 100;

    public static double Duration(TrayAnimation kind) => kind switch
    {
        TrayAnimation.Searching => 0.3,    // entrada, antes del bucle
        TrayAnimation.LockOn => 0.6,
        TrayAnimation.LettingGo => 0.4,    // entrada, antes del bucle
        TrayAnimation.Off => 0.5,
        TrayAnimation.EyeOpen => 0.35,
        TrayAnimation.EyeClose => 0.45,
        _ => 0,
    };

    public static bool IsLoop(TrayAnimation kind) => kind is TrayAnimation.Searching or TrayAnimation.LettingGo;

    public bool Animate { get; private set; }
    public TrayPhase Phase { get; private set; }
    public bool Awake { get; private set; }
    public TrayAnimation Kind { get; private set; }
    private double start;
    private TrayPose from;

    public TrayMotion(TrayPhase phase, bool awake, bool animate)
    {
        Phase = phase;
        Awake = awake && phase == TrayPhase.On;
        Animate = animate;
        Kind = animate ? LoopFor(phase) : TrayAnimation.None;
        from = Rest(phase, Awake, animate);
    }

    private static TrayAnimation LoopFor(TrayPhase phase) => phase switch
    {
        TrayPhase.Starting => TrayAnimation.Searching,
        TrayPhase.Stopping => TrayAnimation.LettingGo,
        _ => TrayAnimation.None,
    };

    /// La pose de reposo de un estado (en los bucles, la de su entrada terminada).
    public static TrayPose Rest(TrayPhase phase, bool awake, bool animate) => phase switch
    {
        TrayPhase.On => TrayPose.On(awake),
        TrayPhase.Off => TrayPose.Off,
        TrayPhase.Starting => animate ? Searching(0) : TrayPose.Busy,
        _ => animate ? LettingGo(0) : TrayPose.Busy,
    };

    /// Cambia de estado en `now`. Devuelve la animación que empieza (None si no cambia nada).
    public TrayAnimation Set(TrayPhase phase, bool awake, double now)
    {
        awake = awake && phase == TrayPhase.On;
        if (phase == Phase && awake == Awake) return TrayAnimation.None;
        var current = PoseAt(now);
        var kind = !Animate ? TrayAnimation.None : phase switch
        {
            TrayPhase.Starting => TrayAnimation.Searching,
            TrayPhase.Stopping => TrayAnimation.LettingGo,
            TrayPhase.On when Phase == TrayPhase.On => awake ? TrayAnimation.EyeOpen : TrayAnimation.EyeClose,
            TrayPhase.On => TrayAnimation.LockOn,
            _ => Phase == TrayPhase.Off ? TrayAnimation.None : TrayAnimation.Off,
        };
        Phase = phase;
        Awake = awake;
        Kind = kind;
        start = now;
        from = current;
        return kind;
    }

    /// Activa o quita las animaciones (ajuste de la app o «Efectos de animación» de Windows).
    /// Sin ellas, el ícono salta a su pose final.
    public void SetAnimate(bool animate, double now)
    {
        if (animate == Animate) return;
        Animate = animate;
        // A media transición, el radar entra desde la insignia ámbar fija.
        Kind = animate ? LoopFor(Phase) : TrayAnimation.None;
        from = Rest(Phase, Awake, false);
        start = now;
    }

    /// Si hace falta seguir pintando: en un bucle siempre; en una transición, hasta que acaba.
    public bool IsAnimating(double now) => IsLoop(Kind) || Kind != TrayAnimation.None && now - start < Duration(Kind);

    /// El fotograma del bucle en `now` si ya pasó la entrada (se puede reutilizar dibujado), o null.
    public int? LoopFrame(double now)
    {
        if (!IsLoop(Kind)) return null;
        double t = now - start;
        return t < Duration(Kind) ? null : Frame(t / Turn);
    }

    public static int Frame(double turns) => (int)Math.Floor(turns * LoopFrames + 1e-9) % LoopFrames;

    public TrayPose PoseAt(double now)
    {
        double t = Math.Max(0, now - start);
        double d = Duration(Kind);
        switch (Kind)
        {
            case TrayAnimation.None:
                return Rest(Phase, Awake, Animate);
            case TrayAnimation.Searching:
            case TrayAnimation.LettingGo:
                // Entrada desde donde estaba (el radar ya girando) y luego el bucle, a
                // fotogramas fijos para poder guardarlos.
                bool searching = Kind == TrayAnimation.Searching;
                if (t >= d) return Loop(searching, Frame(t / Turn));
                var target = searching ? Searching(t / Turn) : LettingGo(t / Turn);
                return TrayPose.Lerp(from, target, EaseOutCubic(t / d));
        }
        double p = Math.Min(1, t / d);
        var rest = Rest(Phase, Awake, Animate);
        if (p >= 1) return rest;
        var pose = TrayPose.Lerp(from, rest, EaseOutCubic(p));
        return Kind switch
        {
            TrayAnimation.LockOn => LockOn(from, pose, p),
            TrayAnimation.Off => GoingOff(from, pose, p),
            TrayAnimation.EyeClose => pose with { Eye = from.Eye * (1 - EaseInOutCubic(p)) },
            _ => pose,
        };
    }

    /// Un fotograma del bucle (0…23).
    public static TrayPose Loop(bool searching, int frame)
    {
        double f = (double)frame / LoopFrames;
        return searching ? Searching(f) : LettingGo(f);
    }

    /// Buscando: anillo tenue y un arco ámbar que gira en el sentido del reloj; ojo cerrado, sin insignia.
    public static TrayPose Searching(double turn) =>
        new(0, 1, TrayPose.Mod(360 * turn), RadarArc, 1, 0, 0, 0, 1, TrayPose.DimInk);

    /// Soltando: el mismo radar al revés y con la insignia ámbar.
    public static TrayPose LettingGo(double turn) =>
        new(0, 1, TrayPose.Mod(-360 * turn), RadarArc, 1, 0, 0, 1, 1, 1);

    /// Fijado: el arco se cierra en el anillo, las marcas salen 1 px y vuelven con rebote
    /// y la insignia azul salta (0 → 1,25 → 1).
    private static TrayPose LockOn(TrayPose from, TrayPose pose, double p)
    {
        const double close = 0.6;
        double k = EaseOutCubic(Math.Min(1, p / close));
        if (k < 1)
        {
            // El arco llega a la vuelta entera y se vuelve tinta; entonces es el anillo.
            pose = pose with
            {
                Ring = from.Ring,
                RingFaint = from.RingFaint * (1 - k),
                ArcStart = from.ArcLength > 0 ? from.ArcStart : 0,
                ArcLength = from.ArcLength + (360 - from.ArcLength) * k,
                ArcTint = from.ArcTint * (1 - k),
            };
        }
        else pose = pose with { Ring = 1, RingFaint = 0, ArcLength = 0, ArcTint = 0 };

        const double outEnd = 0.35;
        double shift = p < outEnd
            ? from.TickShift + (-1 - from.TickShift) * EaseOutCubic(p / outEnd)
            : -1 + EaseOutBack((p - outEnd) / (1 - outEnd));

        const double popStart = 0.3;
        double dot;
        if (p < popStart) dot = from.Dot * (1 - p / popStart);
        else
        {
            double q = (p - popStart) / (1 - popStart);
            dot = q < 0.5 ? 1.25 * EaseOutCubic(q / 0.5) : 1.25 - 0.25 * EaseOutCubic((q - 0.5) / 0.5);
        }
        return pose with { TickShift = shift, Dot = dot, DotTint = p < popStart ? from.DotTint : 0 };
    }

    /// Apagándose: lo que haya (anillo o radar) se deshace ◠ → ◡ → · y el anillo vuelve
    /// tenue; la insignia encoge y se apaga, la tinta baja a 0,78.
    private static TrayPose GoingOff(TrayPose from, TrayPose pose, double p)
    {
        if (p < 0.5)
        {
            double k = EaseInOutCubic(p / 0.5);
            return pose with
            {
                Ring = from.Ring * (1 - k),
                RingFaint = from.RingFaint * (1 - k),
                ArcStart = from.ArcStart,
                ArcLength = from.ArcLength * (1 - k),
                ArcTint = from.ArcTint,
                Dot = from.Dot * (1 - k),
                DotTint = from.DotTint,
            };
        }
        return pose with { Ring = EaseOutCubic((p - 0.5) / 0.5), RingFaint = 0, ArcLength = 0, Dot = 0 };
    }

    public static double EaseOutCubic(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);

    public static double EaseInOutCubic(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }

    /// Pasa del final y vuelve (≈10 %): el rebote de las marcas.
    public static double EaseOutBack(double t)
    {
        t = Math.Clamp(t, 0, 1);
        const double c1 = 1.70158, c3 = c1 + 1;
        return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }
}
