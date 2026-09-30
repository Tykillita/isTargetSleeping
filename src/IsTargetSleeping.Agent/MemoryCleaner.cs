using System.Diagnostics;
using System.Runtime.InteropServices;

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

    [DllImport("kernel32.dll", EntryPoint = "K32EmptyWorkingSet")]
    private static extern bool EmptyWorkingSet(IntPtr process);

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

    private static void EnablePrivilege(string name)
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008 /* ADJUST_PRIVILEGES | QUERY */, out var token)) return;
        try
        {
            if (!LookupPrivilegeValue(null, name, out var luid)) return;
            var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attributes = 0x2 /* SE_PRIVILEGE_ENABLED */ };
            AdjustTokenPrivileges(token, false, ref tp, Marshal.SizeOf<TOKEN_PRIVILEGES>(), IntPtr.Zero, IntPtr.Zero);
        }
        finally { CloseHandle(token); }
    }

    /// Libera las zonas pedidas. Devuelve las que fallaron.
    public static CleanAreas Clean(CleanAreas areas, IReadOnlyCollection<int> keep)
    {
        EnablePrivilege("SeProfileSingleProcessPrivilege");
        EnablePrivilege("SeIncreaseQuotaPrivilege");
        EnablePrivilege("SeDebugPrivilege");
        var failed = CleanAreas.None;
        void Try(CleanAreas area, Func<bool> action)
        {
            if (!areas.HasFlag(area)) return;
            bool ok;
            try { ok = action(); } catch { ok = false; }
            if (!ok) failed |= area;
        }

        // Mismo orden que Mem Reduct: primero lo que genera páginas en las listas, luego las listas.
        Try(CleanAreas.WorkingSets, () => EmptyWorkingSets(keep));
        Try(CleanAreas.SystemFileCache, FlushSystemFileCache);
        Try(CleanAreas.ModifiedFileCache, FlushVolumes);
        Try(CleanAreas.ModifiedList, () => MemoryList(MemoryFlushModifiedList));
        Try(CleanAreas.Standby, () => MemoryList(MemoryPurgeStandbyList));
        Try(CleanAreas.StandbyLowPriority, () => MemoryList(MemoryPurgeLowPriorityStandbyList));
        Try(CleanAreas.RegistryCache, () => NtSetSystemInformation(SystemRegistryReconciliationInformation, IntPtr.Zero, 0) >= 0);
        Try(CleanAreas.CombineLists, CombineLists);
        return failed;
    }

    /// Vacía la memoria de trabajo de cada proceso salvo los protegidos. Cuenta
    /// como bien aunque algunos (protegidos por Windows) no se dejen abrir.
    private static bool EmptyWorkingSets(IReadOnlyCollection<int> keep)
    {
        int emptied = 0;
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (p.Id <= 4 || keep.Contains(p.Id) || p.Id == Environment.ProcessId) continue;
                var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_SET_QUOTA, false, p.Id);
                if (h == IntPtr.Zero) continue;
                try { if (EmptyWorkingSet(h)) emptied++; }
                finally { CloseHandle(h); }
            }
        }
        return emptied > 0;
    }

    private static bool FlushSystemFileCache()
    {
        var info = new SYSTEM_FILECACHE_INFORMATION { MinimumWorkingSet = nuint.MaxValue, MaximumWorkingSet = nuint.MaxValue };
        int size = Marshal.SizeOf<SYSTEM_FILECACHE_INFORMATION>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, buffer, false);
            return NtSetSystemInformation(SystemFileCacheInformationEx, buffer, size) >= 0;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static bool MemoryList(int command)
    {
        var buffer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(buffer, command);
            return NtSetSystemInformation(SystemMemoryListInformation, buffer, sizeof(int)) >= 0;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static bool CombineLists()
    {
        int size = Marshal.SizeOf<MEMORY_COMBINE_INFORMATION_EX>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(new MEMORY_COMBINE_INFORMATION_EX(), buffer, false);
            return NtSetSystemInformation(SystemCombinePhysicalMemoryInformation, buffer, size) >= 0;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    /// Escribe a disco la caché de archivos modificados de cada disco fijo.
    private static bool FlushVolumes()
    {
        bool any = false;
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            var h = CreateFile($@"\\.\{drive.Name.TrimEnd('\\')}", 0x80000000 | 0x40000000 /* GENERIC_READ | WRITE */,
                0x1 | 0x2 /* FILE_SHARE_READ | WRITE */, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero);
            if (h == new IntPtr(-1)) continue;
            try { any |= FlushFileBuffers(h); }
            finally { CloseHandle(h); }
        }
        return any;
    }
}
