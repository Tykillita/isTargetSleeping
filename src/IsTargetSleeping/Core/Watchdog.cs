namespace IsTargetSleeping;

public enum WatchdogAction { None, Restart, KillAndRestart, GiveUp }

/// Vigilante de Ollama, lógica pura (sin procesos ni red) para poder probarla.
///  - Caída: debería estar encendido, la API no responde y no queda ningún
///    `ollama serve` vivo en 2 muestras seguidas (~5 s; la app de Ollama a veces
///    relanza su servidor sola) → reiniciar.
///  - Cuelgue: el servidor sigue vivo pero la API no responde en 3 muestras
///    seguidas (~7,5 s) → matarlo y reiniciar.
/// Como mucho 3 reinicios en 10 min; después se rinde una vez y no insiste
/// hasta que Ollama vuelva a responder.
public sealed class Watchdog
{
    public const int CrashSamples = 2;
    public const int HangSamples = 3;
    public const int MaxRestarts = 3;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private readonly List<DateTime> restarts = [];
    private int hung, crashed;
    public bool GaveUp { get; private set; }

    /// `armed`: el usuario quiere Ollama encendido y no hay nada en marcha
    /// (ni un encendido o apagado pedido, ni modo juego, ni el vigilante desactivado).
    public WatchdogAction Sample(bool armed, bool apiUp, bool serverAlive, DateTime now)
    {
        if (apiUp)
        {
            hung = crashed = 0;
            GaveUp = false;
            return WatchdogAction.None;
        }
        if (!armed || GaveUp)
        {
            hung = crashed = 0;
            return WatchdogAction.None;
        }

        WatchdogAction wanted;
        if (!serverAlive)
        {
            hung = 0;
            if (++crashed < CrashSamples) return WatchdogAction.None;
            wanted = WatchdogAction.Restart;
        }
        else
        {
            crashed = 0;
            if (++hung < HangSamples) return WatchdogAction.None;
            wanted = WatchdogAction.KillAndRestart;
        }

        hung = crashed = 0;
        restarts.RemoveAll(t => now - t > Window);
        if (restarts.Count >= MaxRestarts)
        {
            GaveUp = true;
            return WatchdogAction.GiveUp;
        }
        restarts.Add(now);
        return wanted;
    }
}
