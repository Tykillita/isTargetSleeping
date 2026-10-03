namespace IsTargetSleeping;

/// Lo que la mascota está haciendo, de forma continua. `Sweeping`: liberando RAM, cada una
/// a su manera (Mira barre, la capibara se escurre el agua, el gato se acicala y la llama
/// se sacude la lana).
public enum PetActivity { Hidden, DeepSleep, WakingUp, Drowsy, Eating, Alert, Working, Downloading, Sweeping, Yawning }

public static class PetActivityExtensions
{
    /// Dormida en su cama (o bostezando camino de ella): ahí reacciona sin levantarse.
    public static bool InBed(this PetActivity activity) => activity is PetActivity.DeepSleep or PetActivity.Yawning;
}

/// Reacciones de un solo golpe: interrumpen la actividad y luego vuelven a ella.
public enum PetReaction { None, Jump, Hearts, Sparkle, Nap, Dizzy, Sad }

public enum PetPressure { Normal, High, Critical }

/// Lo que la mascota sabe del sistema (sin depender de los tipos de Ollama, para probarlo solo).
public readonly record struct PetSignals(
    bool Up = false, bool Starting = false, bool Stopping = false, bool ModelLoaded = false,
    bool Loading = false, bool Generating = false, bool Downloading = false, bool Cleaning = false,
    PetPressure Pressure = PetPressure.Normal, bool Game = false);

/// Decide la actividad según las señales, por prioridad, y lleva la cola de reacciones.
/// Una actividad puede alargarse lo justo para verse entera (`HoldFor` del perfil): la
/// limpieza (sale de la cama, limpia y, si liberó algo, su final) o el paseo de la capibara
/// en cocodrilo al encender Ollama. El tiempo, en segundos desde cualquier origen.
public sealed class PetBrain
{
    private IPetAnimationProfile animations;
    public PetBrain(IPetAnimationProfile? animations = null) => this.animations = animations ?? PetProfiles.Mira;

    /// Al elegir otra mascota conserva las señales del sistema y descarta reacciones antiguas.
    public void SetAnimations(IPetAnimationProfile profile)
    {
        animations = profile;
        Reaction = PetReaction.None;
        ReactionSince = 0;
        queue.Clear();
        finale = PetReaction.None;
        finaleShown = null;
    }
    /// «Trabajando» se mantiene un rato después de la última señal: el vigilante de
    /// actividad mira cada 2,5 s y la mascota no debe ir y venir entre frases.
    public const double WorkHold = 3;
    /// Lo más que se alarga la limpieza para que se vea.
    public const double CleanHoldMax = 6;

    public PetActivity Activity { get; private set; } = PetActivity.Hidden;
    /// Desde cuándo está en la actividad actual (para la animación).
    public double ActivitySince { get; private set; }
    public PetReaction Reaction { get; private set; }
    public double ReactionSince { get; private set; }
    /// De qué actividad venía la actual (la capibara limpia distinto si sale de la tina).
    public PetActivity ActivityFrom { get; private set; } = PetActivity.Hidden;
    /// En qué actividad empezó la reacción en curso: no cambia hasta que acaba.
    public PetActivity ReactionDuring { get; private set; }
    /// De dónde venía esa actividad cuando empezó la reacción.
    public PetActivity ReactionFrom { get; private set; } = PetActivity.Hidden;
    private PetContext ReactionContext => new(ReactionDuring, ReactionFrom);
    public PetPressure Pressure { get; private set; }

    private double lastGenerating = double.NegativeInfinity;
    private readonly List<PetReaction> queue = [];
    /// Lo que piden las señales (puede esperar a que acabe la limpieza).
    private PetActivity wanted = PetActivity.Hidden;
    /// Hasta cuándo se mantiene la actividad actual aunque las señales pidan otra.
    private double holdUntil = double.NegativeInfinity;
    /// El final de la limpieza: espera a que se vea la limpieza y la retiene mientras dura.
    private PetReaction finale;
    private (PetReaction Reaction, double Since)? finaleShown;

    public static double Duration(PetReaction reaction) => PetProfiles.Mira.ReactionDuration(reaction, PetActivity.Alert);

    /// Qué actividad toca, de más a menos importante.
    public static PetActivity Choose(PetSignals s, bool generating) =>
        s.Game ? PetActivity.Hidden
        : s.Cleaning ? PetActivity.Sweeping
        : s.Stopping ? PetActivity.Yawning
        : s.Starting ? PetActivity.WakingUp
        : !s.Up ? PetActivity.DeepSleep
        : s.Downloading ? PetActivity.Downloading
        : s.Loading ? PetActivity.Eating
        : generating && s.ModelLoaded ? PetActivity.Working
        : s.ModelLoaded ? PetActivity.Alert
        : PetActivity.Drowsy;

    public void Update(PetSignals s, double now)
    {
        if (s.Generating) lastGenerating = now;
        Pressure = s.Pressure;
        wanted = Choose(s, now - lastGenerating < WorkHold);
        Expire(now);
    }

    /// Una reacción: si ya hay otra más importante en curso, espera su turno (sin repetirse).
    /// `finale`: el final de la limpieza; espera a que la limpieza se haya visto y pasa por
    /// delante de lo que haya.
    public void React(PetReaction reaction, double now, bool finale = false)
    {
        if (reaction == PetReaction.None || Activity == PetActivity.Hidden) return;
        Expire(now);
        if (finale && Activity == PetActivity.Sweeping)
        {
            this.finale = reaction;
            Expire(now);
            return;
        }
        if (Reaction == PetReaction.None || reaction >= Reaction) Start(reaction, now);
        else if (!queue.Contains(reaction) && queue.Count < 3)
        {
            queue.Add(reaction);
            queue.Sort((a, b) => b.CompareTo(a));
        }
    }

    /// Termina la reacción en curso cuando le toca y pasa a la siguiente de la cola; lanza el
    /// final de la limpieza a su hora y, cuando nada la retiene, pasa a la actividad pedida.
    public void Expire(double now)
    {
        while (Reaction != PetReaction.None && now - ReactionSince >= animations.ReactionDuration(Reaction, ReactionContext))
        {
            double ended = ReactionSince + animations.ReactionDuration(Reaction, ReactionContext);
            if (queue.Count > 0)
            {
                Start(queue[0], ended);
                queue.RemoveAt(0);
            }
            else Reaction = PetReaction.None;
        }
        if (finale != PetReaction.None && now >= holdUntil)
        {
            Start(finale, now);
            finaleShown = (finale, now);
            finale = PetReaction.None;
        }
        bool held = wanted != PetActivity.Hidden && (now < holdUntil
            || Activity == PetActivity.Sweeping && (finale != PetReaction.None || finaleShown == (Reaction, ReactionSince)));
        if (wanted != Activity && !held) Change(wanted, now);
    }

    private void Start(PetReaction reaction, double now)
    {
        Reaction = reaction;
        ReactionSince = now;
        ReactionDuring = Activity;
        ReactionFrom = ActivityFrom;
    }

    private void Change(PetActivity next, double now)
    {
        var from = Activity;
        ActivityFrom = from;
        Activity = next;
        ActivitySince = now;
        finaleShown = null;
        holdUntil = now + animations.HoldFor(from, next);
        if (next == PetActivity.Hidden)
        {
            Reaction = PetReaction.None;
            queue.Clear();
            finale = PetReaction.None;
        }
    }
}
