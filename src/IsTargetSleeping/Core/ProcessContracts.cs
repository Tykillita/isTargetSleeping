using System.Text.Json.Serialization;

namespace IsTargetSleeping;

/// A PID alone is never an authorization to act: Windows can reuse it.
public readonly record struct ProcessIdentity(int Pid, long CreatedFileTime)
{
    public bool IsValid => Pid > 0 && CreatedFileTime > 0;
}

public sealed record ProcessSnapshot
{
    public required ProcessIdentity Identity { get; init; }
    public int Pid => Identity.Pid;
    public int? ParentPid { get; init; }
    public required string Name { get; init; }
    public required string Executable { get; init; }
    public string? Path { get; init; }
    public string? OwnerSid { get; init; }
    public string? User { get; init; }
    public int? SessionId { get; init; }
    public bool HasWindow { get; init; }
    public bool IsSystem { get; init; }
    public bool? IsCritical { get; init; }
    public long? WorkingSetBytes { get; init; }
    public long? PrivateWorkingSetBytes { get; init; }
    public long? CommitBytes { get; init; }
    public double? CpuSeconds { get; init; }
    /// Percentage of all logical processors; the first sample is unavailable.
    public double? CpuPercent { get; init; }
    public string? ProtectionReason { get; init; }
    public long? RamBytes(bool usePrivate) => usePrivate ? PrivateWorkingSetBytes : WorkingSetBytes;
}

public sealed record ProcessSample(DateTimeOffset CapturedAt, long TotalRamBytes,
    bool UsesPrivateWorkingSet, IReadOnlyList<ProcessSnapshot> Processes)
{
    public IReadOnlyList<ProcessGroup> Groups => ProcessGroup.Create(this);
}

public sealed record ProcessGroup(string Id, string Name, string Executable, string? Path,
    string? OwnerSid, string? User, int? SessionId, IReadOnlyList<ProcessSnapshot> Processes,
    long? RamBytes, double? CpuPercent)
{
    public int Count => Processes.Count;
    public bool HasWindow => Processes.Any(p => p.HasWindow);
    public bool IsSystem => Processes.All(p => p.IsSystem);
    public bool IsProtected => Processes.Any(p => p.ProtectionReason != null);
    public static IReadOnlyList<ProcessGroup> Create(ProcessSample sample) => sample.Processes
        .GroupBy(GroupKey, StringComparer.OrdinalIgnoreCase)
        .Select(g =>
        {
            var entries = g.OrderBy(p => p.Pid).ToArray();
            var first = entries[0];
            return new ProcessGroup(g.Key, first.Name, first.Executable, first.Path,
                first.OwnerSid, first.User, first.SessionId, entries,
                entries.All(p => p.RamBytes(sample.UsesPrivateWorkingSet).HasValue)
                    ? entries.Sum(p => p.RamBytes(sample.UsesPrivateWorkingSet)!.Value) : null,
                entries.All(p => p.CpuPercent.HasValue) ? entries.Sum(p => p.CpuPercent!.Value) : null);
        }).OrderByDescending(g => g.RamBytes ?? -1).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    public static string GroupKey(ProcessSnapshot p) => p.Path != null && p.OwnerSid != null && p.SessionId.HasValue
        ? $"{p.Path}\0{p.OwnerSid}\0{p.SessionId}"
        : $"pid:{p.Identity.Pid}:{p.Identity.CreatedFileTime}";
}

public enum ProcessActionMode { Single, Tree, Application }
public enum ProcessActionOutcome { Success, Partial, Failed, NoWork, Pending, Cancelled }
public enum ProcessTargetOutcome { Terminated, AlreadyExited, Denied, Blocked, Changed, Pending, Failed }

public sealed record ProcessActionRequest(Guid Id, ProcessActionMode Mode, IReadOnlyList<ProcessIdentity> Targets);
public sealed record ProcessActionPreview(ProcessActionRequest Request,
    IReadOnlyList<ProcessSnapshot> Processes, long? RamBytes, bool UsesPrivateWorkingSet);
public sealed record ProcessTargetResult(ProcessIdentity Identity, ProcessTargetOutcome Outcome,
    string? Reason = null, int? Win32Error = null)
{
    public bool Completed => Outcome is ProcessTargetOutcome.Terminated or ProcessTargetOutcome.AlreadyExited;
}
public sealed record ProcessActionResult(Guid Id, ProcessActionOutcome Outcome,
    DateTimeOffset StartedAt, DateTimeOffset FinishedAt, IReadOnlyList<ProcessTargetResult> Items, string? Error = null)
{
    public IReadOnlyList<ProcessIdentity> Remaining => Items.Where(i => !i.Completed).Select(i => i.Identity).ToArray();
    public IReadOnlyList<ProcessIdentity> RetryTargets => Items.Where(i => i.Outcome == ProcessTargetOutcome.Denied)
        .Select(i => i.Identity).ToArray();
    /// Local-only continuation when an elevated instance outlives the initial wait.
    [JsonIgnore] public Task<ProcessActionResult>? Completion { get; init; }
}
