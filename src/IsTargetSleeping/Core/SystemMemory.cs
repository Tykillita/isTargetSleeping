using System.Globalization;

namespace IsTargetSleeping;

/// Memoria del PC con los criterios del Administrador de tareas: «en uso» es
/// lo que no está disponible (ni libre ni en espera). La presión sale de la
/// notificación de memoria baja del kernel y de la carga de memoria.
public readonly record struct SystemMemory(long Total, long Installed, long Used, SystemMemory.Level Pressure)
{
    public enum Level { Normal, Warning, Critical }

    private static readonly IntPtr lowMemory = Win32.CreateMemoryResourceNotification(0 /* LowMemoryResourceNotification */);

    public static SystemMemory Current()
    {
        var s = new Win32.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32.MEMORYSTATUSEX>() };
        if (!Win32.GlobalMemoryStatusEx(ref s)) return new SystemMemory(1, 1, 0, Level.Normal);

        long total = (long)s.ullTotalPhys;
        long used = Math.Clamp(total - (long)s.ullAvailPhys, 0, total);
        // El Administrador de tareas muestra la memoria instalada (16 GB), no la
        // utilizable (15,7 GB): el total de la barra se rotula igual.
        long installed = Win32.GetPhysicallyInstalledSystemMemory(out var kb) ? (long)kb * 1024 : total;

        bool low = lowMemory != IntPtr.Zero && Win32.QueryMemoryResourceNotification(lowMemory, out var state) && state;
        double commit = s.ullTotalPageFile > 0 ? 1 - (double)s.ullAvailPageFile / s.ullTotalPageFile : 0;
        // La RAM física disponible manda. El commit solo cuenta cerca del límite:
        // CUDA reserva mucho sin usarlo y el archivo de paginación puede crecer.
        var pressure = low || s.dwMemoryLoad >= 95 || commit >= 0.97 ? Level.Critical
            : s.dwMemoryLoad >= 85 ? Level.Warning
            : Level.Normal;

        return new SystemMemory(total, Math.Max(installed, total), used, pressure);
    }
}

public static class Bytes
{
    /// En GiB, como el Administrador de tareas (un PC de 32 GB muestra «32 GB»).
    public static string MemoryGB(this long bytes) =>
        string.Format(CultureInfo.InvariantCulture, "{0:0.0} GB", bytes / 1_073_741_824.0);

    /// En GB decimales, como Ollama cuenta el tamaño de los modelos.
    public static string Gigabytes(this long bytes) =>
        string.Format(CultureInfo.InvariantCulture, "{0:0.0} GB", bytes / 1_000_000_000.0);
}
