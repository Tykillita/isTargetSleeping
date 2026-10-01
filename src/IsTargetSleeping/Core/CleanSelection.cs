namespace IsTargetSleeping;

/// Automatic trimming policy, independent of native calls so its safeguards can
/// be validated without changing any process's working set.
public static class CleanSelection
{
    public const int MaximumProcesses = 10;
    public const long MinimumResidentBytes = 128L * 1024 * 1024;
    public const double MaximumCpuCoreFraction = 0.01;

    public static IReadOnlyList<ProcessSnapshot> Select(ProcessSample previous, ProcessSample current,
        string ownerSid, int sessionId, IReadOnlySet<int> protectedPids, double elapsedSeconds)
    {
        var baseline = previous.Processes.ToDictionary(p => p.Identity);
        return current.Processes.Where(p => p.Identity.IsValid && p.Pid > 4 && p.IsCritical == false
                && p.ProtectionReason == null && !protectedPids.Contains(p.Pid)
                && p.OwnerSid == ownerSid && p.SessionId == sessionId && p.WorkingSetBytes >= MinimumResidentBytes
                && baseline.TryGetValue(p.Identity, out var prior) && LowActivity(prior.CpuSeconds, p.CpuSeconds, elapsedSeconds))
            .OrderByDescending(p => p.WorkingSetBytes).ThenBy(p => p.Pid).Take(MaximumProcesses).ToArray();
    }

    public static bool LowActivity(double? beforeCpu, double? afterCpu, double elapsedSeconds) =>
        beforeCpu.HasValue && afterCpu.HasValue && double.IsFinite(beforeCpu.Value) && double.IsFinite(afterCpu.Value)
        && afterCpu >= beforeCpu && elapsedSeconds > 0 && double.IsFinite(elapsedSeconds)
        && afterCpu.Value - beforeCpu.Value <= elapsedSeconds * MaximumCpuCoreFraction + 1e-9;
    public static bool ShouldContinue(CleanMemorySample memory) => memory.LowMemory || memory.PhysicalLoad >= 85;
}
