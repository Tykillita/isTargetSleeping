using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace IsTargetSleeping.Agent;

/// Las mismas operaciones que Mem Reduct, con una diferencia: la memoria de
/// trabajo se vacía proceso a proceso y los protegidos (modelos, juego) no se
/// tocan; Mem Reduct usa MemoryEmptyWorkingSets, que vacía todos.
internal static class MemoryCleaner
{
    // MARK: NtSetSystemInformation

    private const int SystemFileCacheInformationEx = 81;
    private const int SystemMemoryListInformation = 80;
    private const int SystemCombinePhysicalMemoryInformation = 130;
    private const int SystemRegistryReconciliationInformation = 155;

    private const int MemoryFlushModifiedList = 3;
    private const int MemoryPurgeStandbyList = 4;
    private const int MemoryPurgeLowPriorityStandbyList = 5;

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int infoClass, IntPtr info, int length);

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_FILECACHE_INFORMATION
    {
        public nuint CurrentSize, PeakSize;
        public uint PageFaultCount;
        public nuint MinimumWorkingSet, MaximumWorkingSet, CurrentSizeIncludingTransitionInPages, PeakSizeIncludingTransitionInPages;
        public uint TransitionRePurposeCount, Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORY_COMBINE_INFORMATION_EX
    {
        public IntPtr Handle;
        public nuint PagesCombined;
        public uint Flags;
    }

    // MARK: procesos, volúmenes y privilegios

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, PROCESS_SET_QUOTA = 0x0100;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", EntryPoint = "K32EmptyWorkingSet", SetLastError = true)]
    private static extern bool EmptyWorkingSet(SafeProcessHandle process);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FlushFileBuffers(IntPtr file);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TOKEN_PRIVILEGES
    {
        public uint Count;
        public long Luid;
        public uint Attributes;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES state, int length, IntPtr previous, IntPtr returned);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    private static CleanError? EnablePrivilege(string name)
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008 /* ADJUST_PRIVILEGES | QUERY */, out var token))
            return new("Privilege", Target: name, Win32Error: Marshal.GetLastWin32Error());
        try
        {
            if (!LookupPrivilegeValue(null, name, out var luid)) return new("Privilege", Target: name, Win32Error: Marshal.GetLastWin32Error());
            var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attributes = 0x2 /* SE_PRIVILEGE_ENABLED */ };
            Marshal.SetLastPInvokeError(0);
            bool success = AdjustTokenPrivileges(token, false, ref tp, Marshal.SizeOf<TOKEN_PRIVILEGES>(), IntPtr.Zero, IntPtr.Zero);
            int error = Marshal.GetLastWin32Error();
            // AdjustTokenPrivileges can return true while ERROR_NOT_ALL_ASSIGNED is set.
            return !success || error != 0 ? new("Privilege", Target: name, Win32Error: error) : null;
        }
        finally { CloseHandle(token); }
    }

    public static CleanResult Clean(CleanSpec spec)
    {
        var clock = Stopwatch.StartNew();
        var before = CurrentMemory();
        var errors = new List<CleanError>();
        foreach (string privilege in new[] { "SeProfileSingleProcessPrivilege", "SeIncreaseQuotaPrivilege", "SeDebugPrivilege" })
            if (EnablePrivilege(privilege) is { } error) errors.Add(error);
        var areas = spec.Selective ? spec.Areas & CleanAreas.WorkingSets : spec.Areas;
        var failed = CleanAreas.None;
        int succeeded = 0, treated = 0, skipped = 0, processFailed = 0;
        void TryNt(CleanAreas area, Func<int> action)
        {
            if (!areas.HasFlag(area)) return;
            try
            {
                int status = action();
                if (status >= 0) succeeded++;
                else { failed |= area; errors.Add(new(area.ToString(), NtStatus: status)); }
            }
            catch (Exception e) { failed |= area; errors.Add(new(area.ToString(), Message: e.Message)); }
        }

        if (areas.HasFlag(CleanAreas.WorkingSets))
        {
            try { (treated, skipped, processFailed) = EmptyWorkingSets(spec, errors); }
            catch (Exception e) { processFailed++; errors.Add(new("WorkingSets", Message: e.Message)); }
            if (treated > 0) succeeded++;
            if (processFailed > 0) failed |= CleanAreas.WorkingSets;
        }
        TryNt(CleanAreas.SystemFileCache, FlushSystemFileCache);
        if (areas.HasFlag(CleanAreas.ModifiedFileCache))
        {
            if (FlushVolumes(errors)) succeeded++;
            if (errors.Any(e => e.Operation == "ModifiedFileCache")) failed |= CleanAreas.ModifiedFileCache;
        }
        TryNt(CleanAreas.ModifiedList, () => MemoryList(MemoryFlushModifiedList));
        TryNt(CleanAreas.Standby, () => MemoryList(MemoryPurgeStandbyList));
        TryNt(CleanAreas.StandbyLowPriority, () => MemoryList(MemoryPurgeLowPriorityStandbyList));
        TryNt(CleanAreas.RegistryCache, () => NtSetSystemInformation(SystemRegistryReconciliationInformation, IntPtr.Zero, 0));
        TryNt(CleanAreas.CombineLists, CombineLists);
        var immediate = CurrentMemory();
        bool operationError = errors.Any(e => e.Operation != "Privilege");
        var outcome = succeeded > 0 ? operationError ? CleanOutcome.Partial : CleanOutcome.Success
            : operationError ? CleanOutcome.Failed : CleanOutcome.NoWork;
        return new(before.Used - immediate.Used, failed, null)
        { RequestId = spec.RequestId, Requested = areas, Outcome = outcome, DurationMilliseconds = clock.ElapsedMilliseconds,
            ProcessesTreated = treated, ProcessesSkipped = skipped, ProcessesFailed = processFailed,
            OperationsSucceeded = succeeded, Errors = errors.Take(4096).ToArray(), Before = before, Immediate = immediate };
    }

    private static (int Treated, int Skipped, int Failed) EmptyWorkingSets(CleanSpec spec, List<CleanError> errors)
    {
        var monitor = new ProcessMonitor();
        var roots = spec.Keep.ToHashSet();
        var first = monitor.Capture(roots);
        foreach (var id in spec.KeepIdentities)
            if (first.Processes.Any(p => p.Identity == id)) roots.Add(id.Pid);
        foreach (var p in first.Processes.Where(IsModel)) roots.Add(p.Pid);
        var sample = first;
        long baseline = Stopwatch.GetTimestamp();
        if (spec.Selective)
        {
            Thread.Sleep(2000);
            sample = monitor.Capture(roots);
        }
        // Critical and inaccessible processes are individually excluded. Their
        // system ancestry must not cause unrelated user applications to be excluded.
        foreach (var protectedProcess in sample.Processes.Where(p => p.ProtectionReason == "OwnApplication")) roots.Add(protectedProcess.Pid);
        double elapsed = (Stopwatch.GetTimestamp() - baseline) / (double)Stopwatch.Frequency;
        var protectedPids = ProcessMonitor.ExpandProtection(roots, sample.Processes);
        var candidates = sample.Processes.Where(p => p.Identity.IsValid && p.Pid > 4
            && p.ProtectionReason == null && !protectedPids.Contains(p.Pid) && !IsModel(p) && p.IsCritical == false);
        if (spec.Selective)
            candidates = CleanSelection.Select(first, sample, spec.OwnerSid!, spec.SessionId, protectedPids, elapsed).Where(p => !IsModel(p));
        var list = candidates.OrderByDescending(p => p.WorkingSetBytes).ToArray();
        int treated = 0, skipped = sample.Processes.Count - list.Length, failed = 0, examined = 0;
        foreach (var p in list)
        {
            if (spec.Selective && !CleanSelection.ShouldContinue(CurrentMemory()))
            { skipped += list.Length - examined; break; }
            examined++;
            // Automatic selection needs fresh CPU counters. Manual cleanup uses
            // a cheap fresh process forest for exclusions instead of sampling
            // every process's memory/user/version hundreds of times.
            var current = spec.Selective ? monitor.Capture(roots) : sample;
            var live = current.Processes.FirstOrDefault(q => q.Identity == p.Identity);
            var protectedNow = spec.Selective ? ProcessMonitor.ExpandProtection(roots, current.Processes) : LiveProtection(roots);
            if (live is null || protectedNow is null || live.ProtectionReason != null || protectedNow.Contains(p.Pid) || IsModel(live)
                || live.IsCritical != false || spec.Selective && (live.OwnerSid != spec.OwnerSid || live.SessionId != spec.SessionId
                    || !CleanSelection.LowActivity(p.CpuSeconds, live.CpuSeconds, (current.CapturedAt - sample.CapturedAt).TotalSeconds)))
            { skipped++; continue; }
            using var handle = NativeProcess.OpenProcess(NativeProcess.Query | PROCESS_SET_QUOTA, false, p.Pid);
            if (handle.IsInvalid)
            { failed++; errors.Add(new("WorkingSets", p.Pid, Win32Error: Marshal.GetLastWin32Error())); continue; }
            if (!NativeProcess.GetProcessTimes(handle, out long created, out _, out _, out _)
                || created != p.Identity.CreatedFileTime || NativeProcess.Critical(handle) != false)
            { skipped++; continue; }
            if (spec.Selective)
            {
                // Same held handle revalidates activity and owner immediately before trimming.
                var owner = NativeProcess.Owner(handle).Sid;
                if (owner != spec.OwnerSid || NativeProcess.Session(p.Pid) != spec.SessionId
                    || !NativeProcess.GetProcessTimes(handle, out _, out _, out long kernel, out long user)
                    || !CleanSelection.LowActivity(p.CpuSeconds, (kernel + (double)user) / 10_000_000,
                        (DateTimeOffset.UtcNow - sample.CapturedAt).TotalSeconds)) { skipped++; continue; }
            }
            if (EmptyWorkingSet(handle)) treated++;
            else { failed++; errors.Add(new("WorkingSets", p.Pid, Win32Error: Marshal.GetLastWin32Error())); }
        }
        return (treated, skipped, failed);
    }

    private static bool IsModel(ProcessSnapshot p) => Path.GetFileNameWithoutExtension(p.Executable).ToLowerInvariant()
        is "ollama" or "ollama app" or "ollama_llama_server" or "llama-server" or "lm studio" or "lms"
        || p.Executable.StartsWith("ollama", StringComparison.OrdinalIgnoreCase);
    private static HashSet<int>? LiveProtection(IReadOnlySet<int> roots)
    {
        var entries = NativeProcess.Enumerate();
        if (entries.Count == 0) return null;
        var protectedPids = new HashSet<int>(roots);
        if (ProcessMonitor.GetForegroundPid() is { } foreground) protectedPids.Add(foreground);
        if (ProcessMonitor.LastExternalForegroundPid is { } previous) protectedPids.Add(previous);
        foreach (var entry in entries)
        {
            string name = Path.GetFileNameWithoutExtension(entry.Executable).ToLowerInvariant();
            if (NativeProcess.OwnExecutable(entry.Executable) || name is "llama-server" or "lm studio" or "lms"
                || name.StartsWith("ollama", StringComparison.Ordinal)) protectedPids.Add(entry.Pid);
        }
        bool changed;
        do
        {
            changed = false;
            foreach (var entry in entries)
                if (protectedPids.Contains(entry.ParentPid)) changed |= protectedPids.Add(entry.Pid);
        } while (changed);
        return protectedPids;
    }

    private static int FlushSystemFileCache()
    {
        var info = new SYSTEM_FILECACHE_INFORMATION { MinimumWorkingSet = nuint.MaxValue, MaximumWorkingSet = nuint.MaxValue };
        int size = Marshal.SizeOf<SYSTEM_FILECACHE_INFORMATION>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, buffer, false);
            return NtSetSystemInformation(SystemFileCacheInformationEx, buffer, size);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static int MemoryList(int command)
    {
        var buffer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(buffer, command);
            return NtSetSystemInformation(SystemMemoryListInformation, buffer, sizeof(int));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static int CombineLists()
    {
        int size = Marshal.SizeOf<MEMORY_COMBINE_INFORMATION_EX>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(new MEMORY_COMBINE_INFORMATION_EX(), buffer, false);
            return NtSetSystemInformation(SystemCombinePhysicalMemoryInformation, buffer, size);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    /// Escribe a disco la caché de archivos modificados de cada disco fijo.
    private static bool FlushVolumes(List<CleanError> errors)
    {
        bool any = false;
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            var h = CreateFile($@"\\.\{drive.Name.TrimEnd('\\')}", 0x80000000 | 0x40000000 /* GENERIC_READ | WRITE */,
                0x1 | 0x2 /* FILE_SHARE_READ | WRITE */, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero);
            if (h == new IntPtr(-1))
            { errors.Add(new("ModifiedFileCache", Target: drive.Name, Win32Error: Marshal.GetLastWin32Error())); continue; }
            try
            {
                if (FlushFileBuffers(h)) any = true;
                else errors.Add(new("ModifiedFileCache", Target: drive.Name, Win32Error: Marshal.GetLastWin32Error()));
            }
            finally { CloseHandle(h); }
        }
        return any;
    }

    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    {
        public uint Size, Load;
        public ulong TotalPhys, AvailPhys, TotalPagefile, AvailPagefile, TotalVirtual, AvailVirtual, ExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll")] private static extern IntPtr CreateMemoryResourceNotification(int type);
    [DllImport("kernel32.dll")] private static extern bool QueryMemoryResourceNotification(IntPtr notification, out bool state);
    private static readonly IntPtr lowMemory = CreateMemoryResourceNotification(0);
    internal static CleanMemorySample CurrentMemory()
    {
        var status = new MemoryStatus { Size = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        bool low = lowMemory != IntPtr.Zero && QueryMemoryResourceNotification(lowMemory, out bool state) && state;
        return new(DateTimeOffset.UtcNow, (long)(status.TotalPhys - status.AvailPhys), (long)status.AvailPhys,
            (long)(status.TotalPagefile - status.AvailPagefile), (long)status.TotalPagefile, (int)status.Load, low);
    }
}
