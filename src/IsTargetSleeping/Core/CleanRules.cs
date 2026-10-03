namespace IsTargetSleeping;

public enum CleanReason { Manual, Threshold, Interval, Critical, Game }
public enum GameCleanDecision { Wait, Ready, Cancelled }

public static class GameCleanGate
{
    public static GameCleanDecision Decide(bool gameActive, bool enabled, bool shutdownFailed,
        bool shutdownConfirmed, TimeSpan elapsed) => !gameActive || !enabled || shutdownFailed || elapsed >= TimeSpan.FromSeconds(30)
        ? GameCleanDecision.Cancelled : shutdownConfirmed ? GameCleanDecision.Ready : GameCleanDecision.Wait;
}

/// Las reglas automáticas de Liberar RAM, cada una con su reloj:
///  - **umbral**: con la RAM en el % elegido o por encima limpia aunque no toque el intervalo, y
///    mientras siga alta repite como mucho cada `ThresholdCooldownMinutes` (la «pausa mínima»);
///  - **intervalo**: cada `IntervalMinutes` desde la última limpieza completada, sea del tipo que sea;
///  - **presión crítica**: al empezar actúa enseguida; si sigue crítica, repite con la pausa mínima.
/// Ninguna depende de la presión que marque Windows: manda el % que se configura en la app.
/// Entre dos intentos pasa al menos `MinGap`, y un intento fallido espera 3, 6, 15 y luego 30 min.
public sealed class CleanRuleTracker
{
    public static readonly TimeSpan MinGap = TimeSpan.FromMinutes(1);
    public const int DefaultCooldownMinutes = 5;
    private static readonly int[] RetryMinutes = [3, 6, 15, 30];

    public int ThresholdPercent { get; set; }
    public int ThresholdCooldownMinutes { get; set; } = DefaultCooldownMinutes;
    public int IntervalMinutes { get; set; }
    public bool OnCritical { get; set; }

    /// El intervalo cuenta desde aquí (al arrancar, desde ahora).
    private DateTime lastClean;
    /// La última limpieza completada de verdad: la pausa mínima cuenta desde aquí.
    private DateTime? lastCompleted;
    private DateTime? lastAttempt, retryAt;
    private bool inFlight, criticalArmed = true;
    private int failures;

    public CleanRuleTracker(DateTime now) => lastClean = now;

    private TimeSpan Cooldown => TimeSpan.FromMinutes(Math.Max(1, ThresholdCooldownMinutes));

    /// Si ahora se puede lanzar una limpieza automática (también la del modo juego).
    public bool CanAttempt(DateTime now) => !inFlight && (lastAttempt is null || now - lastAttempt >= MinGap)
        && (retryAt is null || now >= retryAt);

    public CleanReason? Sample(double usedPercent, bool critical, DateTime now)
    {
        if (!critical) criticalArmed = true;
        if (!CanAttempt(now)) return null;
        bool cooled = lastCompleted is null || now - lastCompleted >= Cooldown;
        CleanReason? reason =
            OnCritical && critical && (criticalArmed || cooled) ? CleanReason.Critical
            : ThresholdPercent > 0 && usedPercent >= ThresholdPercent && cooled ? CleanReason.Threshold
            : IntervalMinutes > 0 && now - lastClean >= TimeSpan.FromMinutes(IntervalMinutes) ? CleanReason.Interval
            : null;
        if (reason == CleanReason.Critical) criticalArmed = false;
        return reason;
    }

    /// Empieza una limpieza (automática o a mano).
    public void Attempted(DateTime now)
    {
        inFlight = true;
        lastAttempt = now;
    }

    public void Completed(DateTime now, bool successful)
    {
        inFlight = false;
        if (successful)
        {
            lastClean = now;
            lastCompleted = now;
            failures = 0;
            retryAt = null;
        }
        else
        {
            retryAt = now.AddMinutes(RetryMinutes[Math.Min(failures, RetryMinutes.Length - 1)]);
            failures++;
        }
    }

    /// Una limpieza que ya terminó bien (pruebas y limpiezas de fuera de la app).
    public void Cleaned(DateTime now)
    {
        Attempted(now);
        Completed(now, true);
    }

    /// Al cambiar el intervalo, cuenta desde ahora.
    public void ResetInterval(DateTime now) => lastClean = now;
}
