using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace IsTargetSleeping;

/// Sampling is serialized per monitor; all handles belong to a single capture.
public sealed class ProcessMonitor
{
    private readonly object gate = new();
    private readonly Dictionary<ProcessIdentity, ProcessMetadata> metadata = [];
    private Dictionary<ProcessIdentity, double?> previousCpu = [];
    private long previousTick;
    private readonly bool usesPrivate;
    private static int lastExternalForeground;
    public static int? LastExternalForegroundPid => Volatile.Read(ref lastExternalForeground) is > 0 and var pid ? pid : null;

    public ProcessMonitor() => usesPrivate = NativeProcess.SupportsPrivateWorkingSet();
    internal ProcessMonitor(bool usePrivateWorkingSet) => usesPrivate = usePrivateWorkingSet;

    public Task<ProcessSample> CaptureAsync(IReadOnlySet<int>? protectedPids = null, int? foregroundPid = null,
        CancellationToken cancellationToken = default) => Task.Run(() => Capture(protectedPids, foregroundPid), cancellationToken);

    public ProcessSample Capture(IReadOnlySet<int>? protectedPids = null, int? foregroundPid = null)
    {
        lock (gate)
        {
            var now = DateTimeOffset.UtcNow;
            long tick = Stopwatch.GetTimestamp();
            double elapsed = previousTick == 0 ? 0 : (tick - previousTick) / (double)Stopwatch.Frequency;
            var entries = NativeProcess.Enumerate();
            var windows = NativeProcess.WindowPids();
            var snapshots = new List<ProcessSnapshot>(entries.Count);
            var cpu = new Dictionary<ProcessIdentity, double?>();
            foreach (var entry in entries)
            {
                using var handle = NativeProcess.OpenProcess(NativeProcess.Query, false, entry.Pid);
                var identity = new ProcessIdentity(entry.Pid, 0);
                double? seconds = null;
                bool? critical = null;
                long? working = null, privateWorking = null, committed = null;
                ProcessMetadata? meta = null;
                if (!handle.IsInvalid)
                {
                    if (NativeProcess.GetProcessTimes(handle, out long creation, out _, out long kernel, out long user))
                    {
                        identity = new(entry.Pid, creation);
                        seconds = (kernel + (double)user) / 10_000_000;
                    }
                    critical = NativeProcess.Critical(handle);
                    if (identity.IsValid && !metadata.TryGetValue(identity, out meta))
                    {
                        meta = NativeProcess.Metadata(handle, entry.Pid, entry.Executable);
                        if (metadata.Count < 4096) metadata[identity] = meta;
                    }
                    NativeProcess.Memory(handle, usesPrivate, out working, out privateWorking, out committed);
                }
                meta ??= new(null, null, null, NativeProcess.Session(entry.Pid), System.IO.Path.GetFileNameWithoutExtension(entry.Executable));
                cpu[identity] = seconds;
                double? percent = previousCpu.TryGetValue(identity, out var prev)
                    ? CalculateCpuPercent(identity, prev, identity, seconds, elapsed, Environment.ProcessorCount) : null;
                snapshots.Add(new ProcessSnapshot
                {
                    Identity = identity, ParentPid = entry.ParentPid, Name = meta.Name, Executable = entry.Executable,
                    Path = meta.Path, OwnerSid = meta.Sid, User = meta.User, SessionId = meta.SessionId,
                    HasWindow = windows.Contains(entry.Pid), IsSystem = meta.SessionId == 0 || meta.Sid is "S-1-5-18" or "S-1-5-19" or "S-1-5-20" || entry.Pid is 0 or 4,
                    IsCritical = critical, WorkingSetBytes = working, PrivateWorkingSetBytes = privateWorking,
                    CommitBytes = committed, CpuSeconds = seconds, CpuPercent = percent,
                    ProtectionReason = entry.Pid is 0 or 4 || critical == true ? "Critical"
                        : entry.Pid == Environment.ProcessId || NativeProcess.OwnExecutable(entry.Executable) ? "OwnApplication"
                        : !identity.IsValid ? "UnavailableIdentity" : critical == null ? "CriticalUnknown" : null
                });
            }
            var roots = protectedPids != null ? new HashSet<int>(protectedPids) : [];
            int? foreground = foregroundPid ?? GetForegroundPid();
            if (foreground.HasValue) roots.Add(foreground.Value);
            if (LastExternalForegroundPid.HasValue) roots.Add(LastExternalForegroundPid.Value);
            // Critical and unreadable system processes are protected individually.
            // Expanding PID 0/4 or every system ancestor would hide valid candidates.
            foreach (var p in snapshots.Where(p => p.ProtectionReason == "OwnApplication")) roots.Add(p.Pid);
            var expanded = ExpandProtection(roots, snapshots);
            for (int i = 0; i < snapshots.Count; i++)
                if (snapshots[i].ProtectionReason == null && expanded.Contains(snapshots[i].Pid))
                    snapshots[i] = snapshots[i] with { ProtectionReason = snapshots[i].Pid == foreground || snapshots[i].Pid == LastExternalForegroundPid
                        ? "Foreground" : "Excluded" };
            foreach (var obsolete in metadata.Keys.Where(id => !cpu.ContainsKey(id)).ToArray()) metadata.Remove(obsolete);
            previousCpu = cpu;
            previousTick = tick;
            return new ProcessSample(now, NativeProcess.TotalRam(), usesPrivate, snapshots);
        }
    }

    public static double? CalculateCpuPercent(ProcessIdentity previousIdentity, double? previous,
        ProcessIdentity identity, double? current, double elapsedSeconds, int logicalProcessors)
    {
        if (previousIdentity != identity || !identity.IsValid || !previous.HasValue || !current.HasValue
            || elapsedSeconds <= 0 || logicalProcessors <= 0 || current < previous) return null;
        return Math.Clamp((current.Value - previous.Value) / elapsedSeconds / logicalProcessors * 100, 0, 100);
    }

    public static ProcessIdentity? QueryIdentity(int pid)
    {
        using var handle = NativeProcess.OpenProcess(NativeProcess.Query, false, pid);
        return !handle.IsInvalid && NativeProcess.GetProcessTimes(handle, out long creation, out _, out _, out _)
            ? new ProcessIdentity(pid, creation) : null;
    }

    public static int? GetForegroundPid()
    {
        var window = NativeProcess.GetForegroundWindow();
        if (window == IntPtr.Zero || NativeProcess.GetWindowThreadProcessId(window, out int pid) == 0) return null;
        // Opening the tray can foreground Explorer's notification surface. Keep
        // the application active just before that interaction, rather than the tray.
        if (pid != Environment.ProcessId && !IsShellSurface(NativeProcess.WindowClass(window)))
        {
            using var handle = NativeProcess.OpenProcess(NativeProcess.Query, false, pid);
            var path = !handle.IsInvalid ? NativeProcess.ImagePath(handle) : null;
            if (path == null || !NativeProcess.OwnExecutable(System.IO.Path.GetFileName(path)))
                Volatile.Write(ref lastExternalForeground, pid);
        }
        return pid;
    }

    internal static bool IsShellSurface(string className) => className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
        or "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland" or "Xaml_WindowedPopupClass";

    public static HashSet<int> ExpandProtection(IReadOnlySet<int> roots, IReadOnlyList<ProcessSnapshot> processes)
    {
        var result = new HashSet<int>(roots);
        var identities = processes.GroupBy(p => p.Pid).ToDictionary(g => g.Key, g => g.First().Identity);
        bool changed;
        do
        {
            changed = false;
            foreach (var p in processes)
            {
                if (p.ParentPid is not int parent || !result.Contains(parent) || result.Contains(p.Pid)) continue;
                // The current PID holder cannot be the parent of an older child.
                if (identities.TryGetValue(parent, out var id) && id.IsValid && p.Identity.IsValid
                    && id.CreatedFileTime > p.Identity.CreatedFileTime) continue;
                changed |= result.Add(p.Pid);
            }
        } while (changed);
        return result;
    }
}

internal sealed record ProcessMetadata(string? Path, string? Sid, string? User, int? SessionId, string Name);
internal readonly record struct NativeProcessEntry(int Pid, int ParentPid, string Executable);

/// Shared by the UI and the signed/elevated agent; deliberately independent of WPF.
internal static class NativeProcess
{
    internal const uint Query = 0x1000, Terminate = 1, Synchronize = 0x100000;
    internal const uint WaitObject = 0, WaitTimeout = 258;
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetProcessTimes(SafeProcessHandle h, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool IsProcessCritical(SafeProcessHandle h, out bool critical);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool TerminateProcess(SafeProcessHandle h, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint WaitForSingleObject(SafeProcessHandle h, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32FirstW(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32NextW(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(SafeProcessHandle h, int flags, StringBuilder path, ref int size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ProcessIdToSessionId(int pid, out int session);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("psapi.dll", EntryPoint = "GetProcessMemoryInfo", SetLastError = true)] private static extern bool GetMemoryEx2(SafeProcessHandle h, ref MemoryCountersEx2 counters, uint size);
    [DllImport("psapi.dll", EntryPoint = "GetProcessMemoryInfo", SetLastError = true)] private static extern bool GetMemoryEx(SafeProcessHandle h, ref MemoryCountersEx counters, uint size);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int type, IntPtr buffer, int length, out int needed);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool LookupAccountSid(string? system, IntPtr sid, StringBuilder name, ref uint nameLength, StringBuilder domain, ref uint domainLength, out int use);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out int pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int length);
    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, Pid;
        public UIntPtr DefaultHeap;
        public uint Module, Threads, ParentPid;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Executable;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryCountersEx
    {
        public uint Size, PageFaultCount;
        public UIntPtr PeakWorking, Working, PeakPaged, Paged, PeakNonPaged, NonPaged, Pagefile, PeakPagefile, PrivateUsage;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryCountersEx2
    {
        public uint Size, PageFaultCount;
        public UIntPtr PeakWorking, Working, PeakPaged, Paged, PeakNonPaged, NonPaged, Pagefile, PeakPagefile, PrivateUsage, PrivateWorking;
        public ulong SharedCommit;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    {
        public uint Size, Load;
        public ulong TotalPhys, AvailPhys, TotalPagefile, AvailPagefile, TotalVirtual, AvailVirtual, ExtendedVirtual;
    }

    internal static List<NativeProcessEntry> Enumerate()
    {
        var result = new List<NativeProcessEntry>();
        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) return result;
        var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>(), Executable = "" };
        if (!Process32FirstW(snapshot, ref entry)) return result;
        do { result.Add(new((int)entry.Pid, (int)entry.ParentPid, entry.Executable)); }
        while (Process32NextW(snapshot, ref entry));
        return result;
    }
    internal static HashSet<int> WindowPids()
    {
        var result = new HashSet<int>();
        EnumWindows((window, _) => { if (IsWindowVisible(window)) { GetWindowThreadProcessId(window, out int pid); result.Add(pid); } return true; }, IntPtr.Zero);
        return result;
    }
    internal static string WindowClass(IntPtr window)
    {
        var buffer = new StringBuilder(256);
        return GetClassName(window, buffer, buffer.Capacity) > 0 ? buffer.ToString() : "";
    }
    internal static string? ImagePath(SafeProcessHandle h)
    {
        var buffer = new StringBuilder(32768);
        int length = buffer.Capacity;
        return QueryFullProcessImageName(h, 0, buffer, ref length) ? buffer.ToString() : null;
    }
    internal static int? Session(int pid) => ProcessIdToSessionId(pid, out int session) ? session : null;
    internal static bool? Critical(SafeProcessHandle h) => IsProcessCritical(h, out bool critical) ? critical : null;
    internal static bool OwnExecutable(string? name) => name != null && System.IO.Path.GetFileNameWithoutExtension(name)
        is var stem && (stem.Equals("isTargetSleeping", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("isTargetSleeping.MemoryAgent", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("isTargetSleeping.Agent", StringComparison.OrdinalIgnoreCase));
    internal static ProcessMetadata Metadata(SafeProcessHandle h, int pid, string executable)
    {
        string? path = ImagePath(h);
        var (sid, user) = Owner(h);
        string name = System.IO.Path.GetFileNameWithoutExtension(executable);
        try
        {
            if (path != null && !path.StartsWith(@"\\", StringComparison.Ordinal))
            {
                string? description = FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (!string.IsNullOrWhiteSpace(description)) name = description.Trim();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        return new(path, sid, user, Session(pid), name);
    }
    internal static (string? Sid, string? User) Owner(SafeProcessHandle h)
    {
        if (!OpenProcessToken(h, 8, out var token)) return (null, null);
        using (token)
        {
            GetTokenInformation(token, 1, IntPtr.Zero, 0, out int needed);
            if (needed <= 0 || needed > 65536) return (null, null);
            var buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (!GetTokenInformation(token, 1, buffer, needed, out _)) return (null, null);
                var sidPtr = Marshal.ReadIntPtr(buffer);
                string sid = new SecurityIdentifier(sidPtr).Value;
                var name = new StringBuilder(256); var domain = new StringBuilder(256);
                uint nl = 256, dl = 256;
                string user = LookupAccountSid(null, sidPtr, name, ref nl, domain, ref dl, out _)
                    ? (domain.Length > 0 ? $"{domain}\\{name}" : name.ToString()) : sid;
                return (sid, user);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }
    internal static bool SupportsPrivateWorkingSet()
    {
        using var h = OpenProcess(Query, false, Environment.ProcessId);
        if (h.IsInvalid) return false;
        var counters = new MemoryCountersEx2 { Size = (uint)Marshal.SizeOf<MemoryCountersEx2>(), PrivateWorking = UIntPtr.MaxValue };
        // Older implementations can accept a larger buffer without populating EX2.
        return GetMemoryEx2(h, ref counters, counters.Size) && counters.PrivateWorking != UIntPtr.MaxValue;
    }
    internal static void Memory(SafeProcessHandle h, bool usePrivate, out long? working, out long? privateWorking, out long? committed)
    {
        working = privateWorking = committed = null;
        if (usePrivate)
        {
            var counters = new MemoryCountersEx2 { Size = (uint)Marshal.SizeOf<MemoryCountersEx2>(), PrivateWorking = UIntPtr.MaxValue };
            if (!GetMemoryEx2(h, ref counters, counters.Size)) return;
            working = (long)counters.Working; committed = (long)counters.PrivateUsage;
            if (counters.PrivateWorking != UIntPtr.MaxValue) privateWorking = (long)counters.PrivateWorking;
        }
        else
        {
            var counters = new MemoryCountersEx { Size = (uint)Marshal.SizeOf<MemoryCountersEx>() };
            if (!GetMemoryEx(h, ref counters, counters.Size)) return;
            working = (long)counters.Working; committed = (long)counters.PrivateUsage;
        }
    }
    internal static long TotalRam()
    {
        var status = new MemoryStatus { Size = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref status) ? (long)status.TotalPhys : 0;
    }
}
