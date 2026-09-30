namespace IsTargetSleeping;

public enum CleanReason { Manual, Threshold, Interval, Critical, Game }

/// Cuándo liberar RAM solo, lógica pura para probarla:
///  - uso ≥ X %: una vez, y no se rearma hasta que el uso baja 5 puntos del umbral
///    (con un modelo cargado el uso puede quedarse alto: no se limpia en bucle);
///  - cada N minutos desde la última limpieza (manual o automática);
///  - presión crítica: una vez por episodio.
/// Entre dos limpiezas automáticas pasan al menos 3 minutos.
public sealed class CleanRuleTracker
{
    public const int Hysteresis = 5;
    public static readonly TimeSpan MinGap = TimeSpan.FromMinutes(3);

    /// 0 = apagado.
    public int ThresholdPercent { get; set; }
    public int IntervalMinutes { get; set; }
    public bool OnCritical { get; set; }

    private DateTime lastClean;
    private bool thresholdArmed = true, criticalArmed = true;

    public CleanRuleTracker(DateTime now) { lastClean = now; }

    public CleanReason? Sample(double usedPercent, bool critical, DateTime now)
    {
        if (ThresholdPercent <= 0 || usedPercent < ThresholdPercent - Hysteresis) thresholdArmed = true;
        if (!critical) criticalArmed = true;
        if (now - lastClean < MinGap) return null;

        if (ThresholdPercent > 0 && thresholdArmed && usedPercent >= ThresholdPercent)
        {
            thresholdArmed = false;
            return CleanReason.Threshold;
        }
        if (OnCritical && critical && criticalArmed)
        {
            criticalArmed = false;
            return CleanReason.Critical;
        }
        if (IntervalMinutes > 0 && now - lastClean >= TimeSpan.FromMinutes(IntervalMinutes)) return CleanReason.Interval;
        return null;
    }

    /// Cualquier limpieza (también la manual) reinicia el intervalo y la pausa mínima.
    public void Cleaned(DateTime now) => lastClean = now;
}
