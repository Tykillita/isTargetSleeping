using System.Runtime.InteropServices;

namespace IsTargetSleeping;

/// La GPU dedicada: nombre y memoria de DXGI, uso de los contadores de rendimiento.
public sealed record GpuAdapter(string Name, long Total, uint LuidLow, int LuidHigh)
{
    /// Como aparece en los contadores: «luid_0x00000000_0x00016062».
    public string Luid => $"luid_0x{LuidHigh:x8}_0x{LuidLow:x8}";

    /// «NVIDIA GeForce RTX 4060 Laptop GPU» → «RTX 4060 Laptop».
    public string ShortName
    {
        get
        {
            var n = Name;
            foreach (var prefix in new[] { "NVIDIA GeForce ", "NVIDIA ", "AMD Radeon(TM) ", "AMD Radeon ", "AMD ", "Intel(R) " })
                if (n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { n = n[prefix.Length..]; break; }
            if (n.EndsWith(" GPU", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
            return n.Trim();
        }
    }
}

/// VRAM en uso: toda la del adaptador y la parte de unos procesos (el modelo).
public readonly record struct GpuUsage(long Used, long Model);

/// VRAM de un adaptador. Un dato que Windows o el controlador no entregan es null («no disponible»), nunca una estimación.
public sealed record AdapterUsage(GpuAdapter Adapter, long? Used, long? Model);

/// Todo nativo: DXGI por su tabla de métodos COM (sin interop generado) y PDH para
/// los contadores `\GPU Adapter Memory(*)\Dedicated Usage` y
/// `\GPU Process Memory(*)\Dedicated Usage`, los mismos que usa el Administrador de tareas.
public static unsafe class Gpu
{
    private static readonly Lazy<IReadOnlyList<GpuAdapter>> adapters = new(FindAdapters);

    /// Todas las GPU dedicadas (≥ 1 GB de VRAM propia), sin la integrada ni la de software.
    public static IReadOnlyList<GpuAdapter> Adapters => adapters.Value;

    /// La de más memoria dedicada (la RTX, no la gráfica integrada), o null si no hay.
    public static GpuAdapter? Adapter => Adapters.MaxBy(a => a.Total);

    // MARK: DXGI

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(in Guid riid, out IntPtr factory);

    private static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        public fixed char Description[128];
        public uint VendorId, DeviceId, SubSysId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    private const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;

    private static void Release(IntPtr unknown) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, uint>)(*(IntPtr**)unknown)[2])(unknown);

    private static IReadOnlyList<GpuAdapter> FindAdapters()
    {
        var found = new List<GpuAdapter>();
        try
        {
            if (CreateDXGIFactory1(IID_IDXGIFactory1, out var factory) != 0 || factory == IntPtr.Zero) return found;
            try
            {
                // IDXGIFactory1::EnumAdapters1 es el método 12 de su tabla (IUnknown 3 + IDXGIObject 4 + IDXGIFactory 5).
                var enumAdapters1 = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)(*(IntPtr**)factory)[12];
                for (uint i = 0; i < 16; i++)
                {
                    IntPtr item;
                    if (enumAdapters1(factory, i, &item) != 0) break;   // DXGI_ERROR_NOT_FOUND
                    try
                    {
                        // IDXGIAdapter1::GetDesc1: método 10 (IUnknown 3 + IDXGIObject 4 + IDXGIAdapter 3).
                        var getDesc1 = (delegate* unmanaged[Stdcall]<IntPtr, DXGI_ADAPTER_DESC1*, int>)(*(IntPtr**)item)[10];
                        DXGI_ADAPTER_DESC1 desc;
                        if (getDesc1(item, &desc) != 0 || (desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0) continue;
                        long dedicated = (long)desc.DedicatedVideoMemory;
                        // Menos de 1 GB dedicado es una gráfica integrada con su reserva de arranque.
                        if (dedicated < (1L << 30)) continue;
                        var name = new string(desc.Description).TrimEnd('\0').Trim();
                        found.Add(new GpuAdapter(name, dedicated, desc.LuidLow, desc.LuidHigh));
                    }
                    finally { Release(item); }
                }
            }
            finally { Release(factory); }
            return found;
        }
        catch { return found; }
    }

    // MARK: PDH

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? source, IntPtr user, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint size, out uint count, IntPtr buffer);

    private const uint PDH_FMT_LARGE = 0x400, PDH_MORE_DATA = 0x800007D2;

    private static readonly object gate = new();
    private static IntPtr query, adapterCounter, processCounter;
    private static bool pdhFailed;

    /// Uso total del adaptador y la parte de `pids`; null si no hay GPU dedicada o no se puede leer.
    public static GpuUsage? Read(IEnumerable<int> pids)
    {
        if (Adapter is not { } gpu) return null;
        lock (gate)
        {
            if (!Collect()) return null;
            long used = Values(adapterCounter).Where(v => v.Name.StartsWith(gpu.Luid, StringComparison.OrdinalIgnoreCase)).Sum(v => v.Value);
            var wanted = pids.ToHashSet();
            long model = wanted.Count == 0 ? 0 : ProcessValues(gpu).Where(p => wanted.Contains(p.Pid)).Sum(p => p.Value);
            return new GpuUsage(Math.Min(used, gpu.Total), Math.Min(model, used));
        }
    }

    /// Uso de cada GPU dedicada y la parte de `pids` (los runners): lo que el sistema no entrega sale null.
    public static List<AdapterUsage> ReadAll(IEnumerable<int> pids)
    {
        if (Adapters.Count == 0) return [];
        lock (gate)
        {
            bool ok = Collect();
            return Summarize(Adapters, ok ? Values(adapterCounter) : null, ok ? Values(processCounter) : null, pids.ToHashSet());
        }
    }

    /// Reparte los contadores por adaptador (por su LUID). `null` = el contador no existe;
    /// un adaptador sin instancia en él queda «no disponible» en vez de 0.
    internal static List<AdapterUsage> Summarize(IEnumerable<GpuAdapter> adapters, IReadOnlyList<(string Name, long Value)>? adapterCounters,
        IReadOnlyList<(string Name, long Value)>? processCounters, ISet<int> pids)
    {
        var list = new List<AdapterUsage>();
        foreach (var gpu in adapters)
        {
            var own = adapterCounters?.Where(v => v.Name.StartsWith(gpu.Luid, StringComparison.OrdinalIgnoreCase)).ToList();
            long? used = own is { Count: > 0 } ? Math.Min(own.Sum(v => v.Value), gpu.Total) : null;
            long? model = processCounters is null || used is null ? null
                : Math.Min(PidValues(processCounters, gpu).Where(p => pids.Contains(p.Pid)).Sum(p => p.Value), used.Value);
            list.Add(new AdapterUsage(gpu, used, model));
        }
        return list;
    }

    /// VRAM dedicada de un proceso (para los otros motores).
    public static long ProcessUsage(int pid)
    {
        if (Adapter is not { } gpu) return 0;
        lock (gate)
        {
            if (!Collect()) return 0;
            return ProcessValues(gpu).Where(p => p.Pid == pid).Sum(p => p.Value);
        }
    }

    private static bool Collect()
    {
        if (pdhFailed) return false;
        if (query == IntPtr.Zero)
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out query) != 0
                || PdhAddEnglishCounter(query, @"\GPU Adapter Memory(*)\Dedicated Usage", IntPtr.Zero, out adapterCounter) != 0
                || PdhAddEnglishCounter(query, @"\GPU Process Memory(*)\Dedicated Usage", IntPtr.Zero, out processCounter) != 0)
            {
                pdhFailed = true;
                return false;
            }
        }
        return PdhCollectQueryData(query) == 0;
    }

    private static IEnumerable<(int Pid, long Value)> ProcessValues(GpuAdapter gpu) => PidValues(Values(processCounter), gpu);

    private static IEnumerable<(int Pid, long Value)> PidValues(IEnumerable<(string Name, long Value)> counters, GpuAdapter gpu)
    {
        foreach (var (name, value) in counters)
        {
            // «pid_1234_luid_0x…_phys_0»
            if (!name.StartsWith("pid_", StringComparison.OrdinalIgnoreCase) || !name.Contains(gpu.Luid, StringComparison.OrdinalIgnoreCase)) continue;
            int end = name.IndexOf('_', 4);
            if (end > 4 && int.TryParse(name.AsSpan(4, end - 4), out var pid)) yield return (pid, value);
        }
    }

    /// Instancias de un contador con comodín. PDH_FMT_COUNTERVALUE_ITEM: el nombre y,
    /// tras el estado (4 bytes y relleno), el valor de 64 bits.
    private static List<(string Name, long Value)> Values(IntPtr counter)
    {
        var list = new List<(string, long)>();
        uint size = 0;
        uint status = PdhGetFormattedCounterArray(counter, PDH_FMT_LARGE, ref size, out _, IntPtr.Zero);
        if (status != PDH_MORE_DATA || size == 0) return list;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArray(counter, PDH_FMT_LARGE, ref size, out uint count, buffer) != 0) return list;
            int stride = IntPtr.Size + 16;
            for (int i = 0; i < count; i++)
            {
                var item = buffer + i * stride;
                var name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)) ?? "";
                uint cstatus = (uint)Marshal.ReadInt32(item, IntPtr.Size);
                if (cstatus is 0 or 1) list.Add((name, Marshal.ReadInt64(item, IntPtr.Size + 8)));
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return list;
    }
}
