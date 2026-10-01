namespace IsTargetSleeping;

public enum CleanReason { Manual, Threshold, Interval, Critical, Game }
public enum GameCleanDecision { Wait, Ready, Cancelled }

public static class GameCleanGate
{
    public static GameCleanDecision Decide(bool gameActive, bool enabled, bool shutdownFailed,
        bool shutdownConfirmed, TimeSpan elapsed) => !gameActive || !enabled || shutdownFailed || elapsed >= TimeSpan.FromSeconds(30)
        ? GameCleanDecision.Cancelled : shutdownConfirmed ? GameCleanDecision.Ready : GameCleanDecision.Wait;
}

/// One decision per memory episode. Failed attempts retry after 3/6 minutes,
/// then wait for physical-pressure recovery. Successful work consumes triggers.
public sealed class CleanRuleTracker
{
    public const int Hysteresis = 5;
    public static readonly TimeSpan MinGap = TimeSpan.FromMinutes(3);
    public int ThresholdPercent { get; set; }
    public int IntervalMinutes { get; set; }
    public bool OnCritical { get; set; }
    private DateTime lastClean;
    private DateTime? lastAttempt, retryAt;
    private bool thresholdArmed = true, criticalArmed = true, inFlight;
    private bool pendingThreshold, pendingCritical;
    private CleanReason? retryReason;
    private int failures;

    public CleanRuleTracker(DateTime now) => lastClean = now;
    public bool CanAttempt(DateTime now) => !inFlight && (lastAttempt is null || now - lastAttempt >= MinGap);

    public CleanReason? Sample(double usedPercent, bool critical, DateTime now, bool physicalHigh = true)
    {
        if (ThresholdPercent <= 0 || usedPercent < ThresholdPercent - Hysteresis) thresholdArmed = true;
        if (!critical) criticalArmed = true;
        if (!physicalHigh)
        {
            failures = 0;
            retryAt = null;
            retryReason = null;
            return null;
        }
        if (!CanAttempt(now) || failures >= 3) return null;
        if (retryReason is CleanReason.Threshold && ThresholdPercent <= 0
            || retryReason is CleanReason.Interval && IntervalMinutes <= 0
            || retryReason is CleanReason.Critical && !OnCritical)
        {
            retryReason = null;
            retryAt = null;
        }
        CleanReason? reason;
        if (OnCritical && critical && criticalArmed && retryReason != CleanReason.Critical)
            reason = CleanReason.Critical;
        else if (retryReason is { } retry)
            reason = retryAt <= now ? retry : null;
        else if (now - lastClean < MinGap)
            reason = null;
        else if (ThresholdPercent > 0 && thresholdArmed && usedPercent >= ThresholdPercent)
            reason = CleanReason.Threshold;
        else if (IntervalMinutes > 0 && now - lastClean >= TimeSpan.FromMinutes(IntervalMinutes))
            reason = CleanReason.Interval;
        else
            reason = null;
        if (reason is null) return null;
        pendingThreshold = ThresholdPercent > 0 && usedPercent >= ThresholdPercent;
        pendingCritical = critical;
        Attempted(now, usedPercent, critical);
        retryReason = reason;
        return reason;
    }

    public void Attempted(DateTime now, double? usedPercent = null, bool critical = false)
    {
        inFlight = true;
        lastAttempt = now;
        if (usedPercent is { } used) pendingThreshold = ThresholdPercent > 0 && used >= ThresholdPercent;
        pendingCritical |= critical;
    }

    public void Completed(DateTime now, bool successful)
    {
        inFlight = false;
        if (successful)
        {
            lastClean = now;
            if (pendingThreshold) thresholdArmed = false;
            if (pendingCritical) criticalArmed = false;
            failures = 0;
            retryAt = null;
            retryReason = null;
        }
        else
        {
            failures++;
            retryAt = now.AddMinutes(failures == 1 ? 3 : 6);
        }
        pendingThreshold = pendingCritical = false;
    }

    public void Cleaned(DateTime now)
    {
        lastAttempt = now;
        Completed(now, true);
    }

    public void ResetInterval(DateTime now) => lastClean = now;
}
