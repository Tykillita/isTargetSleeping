namespace IsTargetSleeping;

/// Lo que la mascota está haciendo, de forma continua.
public enum PetActivity { Hidden, DeepSleep, WakingUp, Drowsy, Eating, Alert, Working, Downloading, Sweeping, Yawning }

/// Reacciones de un solo golpe: interrumpen la actividad y luego vuelven a ella.
public enum PetReaction { None, Jump, Hearts, Sparkle, Nap, Dizzy, Sad }

public enum PetPressure { Normal, High, Critical }

/// Lo que la mascota sabe del sistema (sin depender de los tipos de Ollama, para probarlo solo).
public readonly record struct PetSignals(
    bool Up = false, bool Starting = false, bool Stopping = false, bool ModelLoaded = false,
    bool Loading = false, bool Generating = false, bool Downloading = false, bool Cleaning = false,
    PetPressure Pressure = PetPressure.Normal, bool Game = false);

/// Decide la actividad según las señales, por prioridad, y lleva la cola de reacciones.
/// El tiempo, en segundos desde cualquier origen.
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
    }
    /// «Trabajando» se mantiene un rato después de la última señal: el vigilante de
    /// actividad mira cada 2,5 s y la mascota no debe ir y venir entre frases.
    public const double WorkHold = 3;

    public PetActivity Activity { get; private set; } = PetActivity.Hidden;
    /// Desde cuándo está en la actividad actual (para la animación).
    public double ActivitySince { get; private set; }
    public PetReaction Reaction { get; private set; }
    public double ReactionSince { get; private set; }
    public PetPressure Pressure { get; private set; }

    private double lastGenerating = double.NegativeInfinity;
    private readonly List<PetReaction> queue = [];

    public static double Duration(PetReaction reaction) => PetProfiles.Mira.ReactionDuration(reaction);

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
        var next = Choose(s, now - lastGenerating < WorkHold);
        if (next != Activity)
        {
            Activity = next;
            ActivitySince = now;
            if (next == PetActivity.Hidden) { Reaction = PetReaction.None; queue.Clear(); }
        }
        Expire(now);
    }

    /// Una reacción: si ya hay otra más importante en curso, espera su turno (sin repetirse).
    public void React(PetReaction reaction, double now)
    {
        if (reaction == PetReaction.None || Activity == PetActivity.Hidden) return;
        Expire(now);
        if (Reaction == PetReaction.None || reaction >= Reaction)
        {
            Reaction = reaction;
            ReactionSince = now;
        }
        else if (!queue.Contains(reaction) && queue.Count < 3)
        {
            queue.Add(reaction);
            queue.Sort((a, b) => b.CompareTo(a));
        }
    }

    /// Termina la reacción en curso cuando le toca y pasa a la siguiente de la cola.
    public void Expire(double now)
    {
        while (Reaction != PetReaction.None && now - ReactionSince >= animations.ReactionDuration(Reaction))
        {
            double ended = ReactionSince + animations.ReactionDuration(Reaction);
            if (queue.Count > 0)
            {
                Reaction = queue[0];
                queue.RemoveAt(0);
                ReactionSince = ended;
            }
            else Reaction = PetReaction.None;
        }
    }
}
