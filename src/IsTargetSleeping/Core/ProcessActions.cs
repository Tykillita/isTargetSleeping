using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace IsTargetSleeping;

/// Forcible termination with creation-time authorization and a held process handle.
/// Both the normal UI and the elevated agent execute this same safety policy.
public static class ProcessActions
{
    public const int MaxTargets = 1024;
    public static ProcessActionPreview Preview(ProcessActionRequest request)
    {
        Validate(request);
        var sample = new ProcessMonitor().Capture();
        request = ExpandApplicationGroup(request, sample.Processes);
        var targets = ExpandTargets(request, sample.Processes);
        if (targets.Count > MaxTargets) throw new ArgumentException("The expanded action exceeds 1024 processes.", nameof(request));
        long? ram = targets.All(p => p.RamBytes(sample.UsesPrivateWorkingSet).HasValue)
            ? targets.Sum(p => p.RamBytes(sample.UsesPrivateWorkingSet)!.Value) : null;
        return new(request, targets, ram, sample.UsesPrivateWorkingSet);
    }

    public static Task<ProcessActionResult> ExecuteAsync(ProcessActionRequest request,
        CancellationToken cancellationToken = default) => Task.Run(() => Execute(request, cancellationToken));

    /// Normalize application membership at confirmation time, while at least one
    /// exact root identity still proves which executable/user/session was selected.
    /// Execution never silently adds independent processes after that confirmation.
    public static ProcessActionRequest ExpandApplicationGroup(ProcessActionRequest request,
        IReadOnlyList<ProcessSnapshot> processes)
    {
        Validate(request);
        if (request.Mode != ProcessActionMode.Application) return request;
        var roots = request.Targets.ToHashSet();
        var keys = processes.Where(p => p.Identity.IsValid && roots.Contains(p.Identity))
            .Select(ProcessGroup.GroupKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var process in processes)
            if (process.Identity.IsValid && keys.Contains(ProcessGroup.GroupKey(process))) roots.Add(process.Identity);
        if (roots.Count > MaxTargets)
            throw new ArgumentException("The application group exceeds 1024 processes.", nameof(request));
        return request with { Targets = roots.OrderBy(i => i.Pid).ThenBy(i => i.CreatedFileTime).ToArray() };
    }

    public static ProcessActionResult Execute(ProcessActionRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        var started = DateTimeOffset.UtcNow;
        var timer = Stopwatch.StartNew();
        var outcomes = new Dictionary<ProcessIdentity, ProcessTargetResult>();
        var held = new Dictionary<ProcessIdentity, HeldTarget>();
        var authorized = new HashSet<ProcessIdentity>(request.Targets);
        var known = new Dictionary<ProcessIdentity, ProcessSnapshot>();
        bool targetLimit = false;
        try
        {
            // A blocked root never authorizes traversal through its descendants.
            // In particular, PID 0 must not expand to every system process.
            foreach (var root in authorized)
            {
                if (timer.Elapsed >= TimeSpan.FromSeconds(15) || cancellationToken.IsCancellationRequested) break;
                Pin(root, null, held, outcomes);
            }
            for (int scan = 0; scan < 3 && timer.Elapsed < TimeSpan.FromSeconds(15) && !cancellationToken.IsCancellationRequested; scan++)
            {
                var current = CaptureIdentities();
                var expandable = authorized.Where(id => !outcomes.TryGetValue(id, out var result)
                    || result.Outcome is not (ProcessTargetOutcome.Blocked or ProcessTargetOutcome.Changed or ProcessTargetOutcome.Failed)).ToArray();
                var expanded = ExpandTargets(request with { Targets = expandable }, current);
                if (authorized.Count + expanded.Count(p => !authorized.Contains(p.Identity)) > MaxTargets)
                {
                    targetLimit = true;
                    foreach (var identity in authorized)
                        if (!outcomes.TryGetValue(identity, out var existing) || !existing.Completed)
                            outcomes[identity] = new(identity, ProcessTargetOutcome.Blocked, "TargetLimit");
                    break;
                }
                foreach (var target in expanded)
                {
                    authorized.Add(target.Identity);
                    known[target.Identity] = target;
                }
                // Missing roots still receive a precise exited/reused/denied outcome.
                foreach (var identity in authorized.ToArray())
                {
                    if (outcomes.ContainsKey(identity) || held.ContainsKey(identity)) continue;
                    if (timer.Elapsed >= TimeSpan.FromSeconds(15) || cancellationToken.IsCancellationRequested) break;
                    Pin(identity, known.GetValueOrDefault(identity), held, outcomes);
                }
                foreach (var identity in LeafFirst(authorized, known.Values.ToArray()))
                {
                    if (outcomes.ContainsKey(identity) || !held.TryGetValue(identity, out var target)) continue;
                    if (timer.Elapsed >= TimeSpan.FromSeconds(15) || cancellationToken.IsCancellationRequested) break;
                    if (NativeProcess.WaitForSingleObject(target.Handle, 0) == NativeProcess.WaitObject)
                    {
                        outcomes[identity] = new(identity, target.TerminationRequested ? ProcessTargetOutcome.Terminated : ProcessTargetOutcome.AlreadyExited);
                        continue;
                    }
                    // Recheck critical status immediately before the destructive call.
                    if (NativeProcess.Critical(target.Handle) != false)
                    {
                        outcomes[identity] = new(identity, ProcessTargetOutcome.Blocked, "CriticalOrUnknown");
                        continue;
                    }
                    if (!NativeProcess.TerminateProcess(target.Handle, 1))
                    {
                        int error = Marshal.GetLastWin32Error();
                        // TerminateProcess also returns ERROR_ACCESS_DENIED for a process that is already exiting
                        // (e.g. a conhost.exe closing with its console): give it a moment before calling it denied.
                        uint grace = error == 5 ? (uint)Math.Clamp(15_000 - timer.ElapsedMilliseconds, 0, 250) : 0;
                        outcomes[identity] = NativeProcess.WaitForSingleObject(target.Handle, grace) == NativeProcess.WaitObject
                            ? new(identity, ProcessTargetOutcome.AlreadyExited)
                            : new(identity, error == 5 ? ProcessTargetOutcome.Denied : ProcessTargetOutcome.Failed,
                                "TerminateFailed", error);
                        continue;
                    }
                    target.TerminationRequested = true;
                    uint wait = (uint)Math.Clamp(15_000 - timer.ElapsedMilliseconds, 0, 100);
                    if (NativeProcess.WaitForSingleObject(target.Handle, wait) == NativeProcess.WaitObject)
                        outcomes[identity] = new(identity, ProcessTargetOutcome.Terminated);
                }
                if (request.Mode == ProcessActionMode.Single) break;
            }
            // Every pending handle is checked individually; parent exit is never enough.
            foreach (var (identity, target) in held)
            {
                if (outcomes.TryGetValue(identity, out var done) && done.Outcome != ProcessTargetOutcome.Pending) continue;
                uint wait = cancellationToken.IsCancellationRequested ? 0 : (uint)Math.Clamp(15_000 - timer.ElapsedMilliseconds, 0, 250);
                uint status = NativeProcess.WaitForSingleObject(target.Handle, wait);
                outcomes[identity] = status == NativeProcess.WaitObject
                    ? new(identity, target.TerminationRequested ? ProcessTargetOutcome.Terminated : ProcessTargetOutcome.AlreadyExited)
                    : new(identity, ProcessTargetOutcome.Pending, "ExitNotConfirmed");
            }
            foreach (var identity in authorized)
                if (!outcomes.ContainsKey(identity)) outcomes[identity] = new(identity, ProcessTargetOutcome.Pending,
                    cancellationToken.IsCancellationRequested ? "Cancelled" : "Deadline");
            var items = outcomes.Values.OrderBy(i => i.Identity.Pid).ToArray();
            var outcome = targetLimit ? items.Any(i => i.Completed) ? ProcessActionOutcome.Partial : ProcessActionOutcome.Failed
                : Summarize(items, cancellationToken.IsCancellationRequested);
            return new(request.Id, outcome, started, DateTimeOffset.UtcNow, items, targetLimit ? "TargetLimit" : null);
        }
        finally { foreach (var target in held.Values) target.Handle.Dispose(); }
    }

    public static ProcessActionOutcome Summarize(IReadOnlyList<ProcessTargetResult> items, bool cancelled = false)
    {
        if (cancelled) return ProcessActionOutcome.Cancelled;
        if (items.Count == 0 || items.All(i => i.Outcome == ProcessTargetOutcome.AlreadyExited)) return ProcessActionOutcome.NoWork;
        if (items.All(i => i.Completed)) return ProcessActionOutcome.Success;
        if (items.Any(i => i.Completed)) return ProcessActionOutcome.Partial;
        if (items.Any(i => i.Outcome == ProcessTargetOutcome.Pending)) return ProcessActionOutcome.Pending;
        return ProcessActionOutcome.Failed;
    }

    /// Pure expansion also used by confirmations/tests. Creation-time ordering rejects reused parents.
    public static IReadOnlyList<ProcessSnapshot> ExpandTargets(ProcessActionRequest request, IReadOnlyList<ProcessSnapshot> processes)
    {
        var roots = request.Targets.ToHashSet();
        var selected = processes.Where(p => roots.Contains(p.Identity)).ToDictionary(p => p.Identity);
        if (request.Mode == ProcessActionMode.Single) return selected.Values.OrderBy(p => p.Pid).ToArray();
        var parentIdentities = processes.GroupBy(p => p.Pid).ToDictionary(g => g.Key, g => g.First().Identity);
        foreach (var root in roots)
            if (!parentIdentities.ContainsKey(root.Pid)) parentIdentities[root.Pid] = root;
        var blockedPids = processes.Where(p => p.Pid is 0 or 4 || p.Pid == Environment.ProcessId || p.IsCritical == true
            || NativeProcess.OwnExecutable(p.Executable) || NativeProcess.OwnExecutable(p.Path)
            || p.ProtectionReason is "CriticalUnknown" or "UnavailableIdentity").Select(p => p.Pid).ToHashSet();
        var parentRoots = roots.Where(root => !blockedPids.Contains(root.Pid) && root.Pid is not (0 or 4)
            && root.Pid != Environment.ProcessId && parentIdentities.GetValueOrDefault(root.Pid) == root).ToDictionary(root => root.Pid);
        bool changed;
        do
        {
            changed = false;
            foreach (var p in processes)
            {
                if (selected.ContainsKey(p.Identity) || p.ParentPid is not int parent || !parentRoots.TryGetValue(parent, out var parentId)) continue;
                if (parentId.IsValid && p.Identity.IsValid && parentId.CreatedFileTime > p.Identity.CreatedFileTime) continue;
                selected[p.Identity] = p;
                if (!blockedPids.Contains(p.Pid)) parentRoots[p.Pid] = p.Identity;
                changed = true;
            }
        } while (changed);
        return selected.Values.OrderBy(p => p.Pid).ToArray();
    }

    public static IReadOnlyList<ProcessIdentity> LeafFirst(IEnumerable<ProcessIdentity> identities, IReadOnlyList<ProcessSnapshot> processes)
    {
        var parents = processes.GroupBy(p => p.Pid).ToDictionary(g => g.Key, g => g.First().ParentPid);
        int Depth(ProcessIdentity identity)
        {
            int depth = 0, pid = identity.Pid;
            var visited = new HashSet<int>();
            while (visited.Add(pid) && parents.TryGetValue(pid, out var parent) && parent.HasValue)
            { depth++; pid = parent.Value; }
            return depth;
        }
        return identities.Distinct().OrderByDescending(Depth).ThenBy(i => i.Pid).ToArray();
    }

    private static void Validate(ProcessActionRequest request)
    {
        if (request.Id == Guid.Empty || !Enum.IsDefined(request.Mode) || request.Targets == null || request.Targets.Count > MaxTargets)
            throw new ArgumentException("Invalid process action request.", nameof(request));
    }

    private static void Pin(ProcessIdentity identity, ProcessSnapshot? snapshot,
        Dictionary<ProcessIdentity, HeldTarget> held, Dictionary<ProcessIdentity, ProcessTargetResult> results)
    {
        if (identity.Pid is 0 or 4 || identity.Pid == Environment.ProcessId)
        { results[identity] = new(identity, ProcessTargetOutcome.Blocked, "OwnOrSystemProcess"); return; }
        if (!identity.IsValid)
        { results[identity] = new(identity, ProcessTargetOutcome.Blocked, "UnavailableIdentity"); return; }
        var handle = NativeProcess.OpenProcess(NativeProcess.Query | NativeProcess.Terminate | NativeProcess.Synchronize, false, identity.Pid);
        int openError = handle.IsInvalid ? Marshal.GetLastWin32Error() : 0;
        bool canTerminate = !handle.IsInvalid;
        if (!canTerminate)
        {
            handle.Dispose();
            handle = NativeProcess.OpenProcess(NativeProcess.Query | NativeProcess.Synchronize, false, identity.Pid);
        }
        try
        {
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                results[identity] = new(identity, error == 87 ? ProcessTargetOutcome.AlreadyExited
                    : error == 5 ? ProcessTargetOutcome.Denied : ProcessTargetOutcome.Failed, "OpenFailed", error);
                return;
            }
            if (!NativeProcess.GetProcessTimes(handle, out long created, out _, out _, out _))
            { results[identity] = new(identity, ProcessTargetOutcome.Blocked, "UnavailableIdentity", Marshal.GetLastWin32Error()); return; }
            if (created != identity.CreatedFileTime)
            { results[identity] = new(identity, ProcessTargetOutcome.Changed, "PidReused"); return; }
            if (NativeProcess.WaitForSingleObject(handle, 0) == NativeProcess.WaitObject)
            { results[identity] = new(identity, ProcessTargetOutcome.AlreadyExited); return; }
            string? path = NativeProcess.ImagePath(handle);
            if (path == null || NativeProcess.OwnExecutable(path) || NativeProcess.OwnExecutable(snapshot?.Executable))
            { results[identity] = new(identity, ProcessTargetOutcome.Blocked, path == null ? "UnavailablePath" : "OwnApplication"); return; }
            bool? critical = NativeProcess.Critical(handle);
            if (critical != false)
            { results[identity] = new(identity, ProcessTargetOutcome.Blocked, critical == true ? "Critical" : "CriticalUnknown"); return; }
            if (!canTerminate)
            { results[identity] = new(identity, openError == 5 ? ProcessTargetOutcome.Denied : ProcessTargetOutcome.Failed, "OpenFailed", openError); return; }
            held[identity] = new(handle);
            handle = null!; // ownership transferred; retained until all rescans and waits finish
        }
        finally { handle?.Dispose(); }
    }

    private static IReadOnlyList<ProcessSnapshot> CaptureIdentities()
    {
        var processes = new List<ProcessSnapshot>();
        foreach (var entry in NativeProcess.Enumerate())
        {
            using var handle = NativeProcess.OpenProcess(NativeProcess.Query, false, entry.Pid);
            var identity = !handle.IsInvalid && NativeProcess.GetProcessTimes(handle, out long creation, out _, out _, out _)
                ? new ProcessIdentity(entry.Pid, creation) : new ProcessIdentity(entry.Pid, 0);
            bool? critical = !handle.IsInvalid ? NativeProcess.Critical(handle) : null;
            processes.Add(new ProcessSnapshot { Identity = identity, ParentPid = entry.ParentPid,
                Executable = entry.Executable, Name = System.IO.Path.GetFileNameWithoutExtension(entry.Executable), IsCritical = critical,
                ProtectionReason = !identity.IsValid ? "UnavailableIdentity" : critical == null ? "CriticalUnknown" : null });
        }
        return processes;
    }

    private sealed class HeldTarget(SafeProcessHandle handle)
    {
        public SafeProcessHandle Handle { get; } = handle;
        public bool TerminationRequested { get; set; }
    }
}
