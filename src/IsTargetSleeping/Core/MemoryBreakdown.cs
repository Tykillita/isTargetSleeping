using System.Runtime.InteropServices;

namespace IsTargetSleeping;

/// El desglose de memoria que también enseña Mem Reduct, más la memoria comprometida:
/// - física: la RAM (en uso y disponible, como el Administrador de tareas);
/// - comprometida: lo que los programas tienen reservado entre RAM y archivos de paginación;
///   si llega al límite, Windows no puede dar más memoria aunque quede RAM libre;
/// - archivos de paginación: lo que ocupan de verdad en disco (todos sumados);
/// - caché del sistema: la caché de archivos de Windows ahora y su pico reciente.
/// Las lecturas que fallan quedan a 0 (la vista las muestra como no disponibles).
public readonly record struct MemoryBreakdown(
    long PhysicalTotal, long PhysicalAvailable, int PhysicalLoad,
    long Committed, long CommitLimit,
    long PageFileUsed, long PageFileTotal,
    long CacheCurrent, long CachePeak)
{
    public double PhysicalFraction => Fraction(PhysicalTotal - PhysicalAvailable, PhysicalTotal);
    public double CommitFraction => Fraction(Committed, CommitLimit);
    public double PageFileFraction => Fraction(PageFileUsed, PageFileTotal);
    public double CacheFraction => Fraction(CacheCurrent, CachePeak);

    private static double Fraction(long part, long total) => total > 0 ? Math.Clamp((double)part / total, 0, 1) : 0;

    public static MemoryBreakdown Current()
    {
        var s = new Win32.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Win32.MEMORYSTATUSEX>() };
        bool ok = Win32.GlobalMemoryStatusEx(ref s);
        var (pageUsed, pageTotal) = PageFiles();
        var (cache, peak) = FileCache();
        return new MemoryBreakdown(
            ok ? (long)s.ullTotalPhys : 0, ok ? (long)s.ullAvailPhys : 0, ok ? (int)s.dwMemoryLoad : 0,
            ok ? (long)(s.ullTotalPageFile - Math.Min(s.ullTotalPageFile, s.ullAvailPageFile)) : 0, ok ? (long)s.ullTotalPageFile : 0,
            pageUsed, pageTotal, cache, peak);
    }

    private const int SystemPageFileInformation = 18, SystemFileCacheInformation = 21, InfoLengthMismatch = unchecked((int)0xC0000004);

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int infoClass, IntPtr buffer, int length, out int returned);

    /// SYSTEM_PAGEFILE_INFORMATION, una entrada por archivo: NextEntryOffset, TotalSize,
    /// TotalInUse y PeakUsage (en páginas), seguidos del nombre.
    private static (long Used, long Total) PageFiles()
    {
        int size = 4096;
        for (int attempt = 0; attempt < 4; attempt++, size *= 4)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                int status = NtQuerySystemInformation(SystemPageFileInformation, buffer, size, out int returned);
                if (status == InfoLengthMismatch) continue;
                if (status < 0 || returned == 0) return (0, 0);
                return ParsePageFiles(buffer, returned, Environment.SystemPageSize);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return (0, 0);
    }

    /// Recorre las entradas sin salirse de lo que devolvió el sistema.
    internal static (long Used, long Total) ParsePageFiles(IntPtr buffer, int length, long pageSize)
    {
        long used = 0, total = 0;
        int offset = 0;
        while (offset >= 0 && offset + 16 <= length)
        {
            int next = Marshal.ReadInt32(buffer, offset);
            total += (uint)Marshal.ReadInt32(buffer, offset + 4) * pageSize;
            used += (uint)Marshal.ReadInt32(buffer, offset + 8) * pageSize;
            if (next <= 0) break;
            offset += next;
        }
        return (used, total);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_FILECACHE_INFORMATION
    {
        public nuint CurrentSize, PeakSize;
        public uint PageFaultCount;
        public nuint MinimumWorkingSet, MaximumWorkingSet, CurrentSizeIncludingTransitionInPages, PeakSizeIncludingTransitionInPages;
        public uint TransitionRePurposeCount, Flags;
    }

    private static (long Current, long Peak) FileCache()
    {
        int size = Marshal.SizeOf<SYSTEM_FILECACHE_INFORMATION>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (NtQuerySystemInformation(SystemFileCacheInformation, buffer, size, out _) < 0) return (0, 0);
            var info = Marshal.PtrToStructure<SYSTEM_FILECACHE_INFORMATION>(buffer);
            return ((long)info.CurrentSize, (long)info.PeakSize);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
