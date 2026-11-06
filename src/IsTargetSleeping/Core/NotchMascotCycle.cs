namespace IsTargetSleeping;

/// La presencia de la mascota en el notch cerrado, ligada a la inactividad real del usuario (sin teclado ni ratón): tras un rato aparece dormida
/// (ojos cerrados); si llega algo se despierta unos segundos y se vuelve a dormir; cuando el usuario vuelve, abre los ojos, saluda un instante y se retira
/// (la ventana la funde y reaparece el notch normal). Con el usuario activo no hay mascota. Lógica pura, sin WPF.
public sealed class NotchMascotCycle
{
    public const double IdleSeconds = 40;    // sin entrada del usuario antes de aparecer
    public const double ReturnSeconds = 1;   // por debajo de esto, el usuario ha vuelto
    public const double AwakeSeconds = 14;   // lo que se queda despierta tras una novedad estando dormida
    public const double GreetSeconds = 1.8;  // el saludo al volver el usuario (abre los ojos, se pone feliz y sonrojada), antes de retirarse

    private enum Phase { Away, Asleep, Greeting }
    private Phase phase = Phase.Away;
    private DateTime? lastStimulus, greetingSince;

    public bool Visible { get; private set; }
    public bool Awake { get; private set; }
    public bool Greeting { get; private set; }

    /// Puede mostrarse con la mascota activada, el notch cerrado y a la vista, y sin música (la portada y el ecualizador ocupan la pastilla).
    public static bool IsVisible(bool enabled, bool closedAndShown, bool musicPlaying) => enabled && closedAndShown && !musicPlaying;

    /// Avanza el reloj. `idleSeconds`: lo que lleva el usuario sin teclado ni ratón. `stimulus`: algo nuevo en el notch (una actividad que llega o se va).
    public void Update(bool allowed, bool stimulus, double idleSeconds, DateTime now)
    {
        if (!allowed) { Reset(); return; }
        switch (phase)
        {
            case Phase.Away:
                if (idleSeconds < IdleSeconds) { Visible = Awake = false; return; }
                phase = Phase.Asleep;
                Visible = true;
                lastStimulus = null;
                break;
            case Phase.Greeting:
                if ((now - greetingSince!.Value).TotalSeconds < GreetSeconds) return;
                Reset();
                return;
        }
        if (idleSeconds < ReturnSeconds)
        {
            phase = Phase.Greeting;
            greetingSince = now;
            Visible = Awake = Greeting = true;
            return;
        }
        if (stimulus) lastStimulus = now;
        Awake = lastStimulus is { } at && (now - at).TotalSeconds < AwakeSeconds;
    }

    private void Reset()
    {
        phase = Phase.Away;
        Visible = Awake = Greeting = false;
        lastStimulus = greetingSince = null;
    }
}
