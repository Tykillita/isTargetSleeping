using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IsTargetSleeping;

/// Una app que habla con un motor: su nombre visible y su .exe (para el ícono).
public sealed record ClientApp(string Name, string? Path);

/// Qué procesos tienen una conexión abierta hacia un puerto (el de Ollama, el de
/// LM Studio…), con la tabla TCP de Windows que ya incluye el PID dueño.
public static class TcpClients
{
    /// PIDs con alguna conexión (IPv4 o IPv6) cuyo puerto remoto es `port`.
    public static HashSet<int> Connected(int port)
    {
        var pids = new HashSet<int>();
        Read(Win32.AF_INET, 24, 16, 20, port, pids);
        Read(Win32.AF_INET6, 56, 44, 52, port, pids);
        pids.Remove(0);
        pids.Remove(Environment.ProcessId);
        return pids;
    }

    /// MIB_TCPTABLE_OWNER_PID / MIB_TCP6TABLE_OWNER_PID: un DWORD con el número de
    /// filas y las filas, de `rowSize` bytes, con el puerto remoto y el PID en esos desplazamientos.
    private static void Read(int family, int rowSize, int remotePortOffset, int pidOffset, int port, HashSet<int> pids)
    {
        int size = 0;
        Win32.GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, Win32.TCP_TABLE_OWNER_PID_CONNECTIONS, 0);
        if (size <= 0) return;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            size += 4096;
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                uint result = Win32.GetExtendedTcpTable(buffer, ref size, false, family, Win32.TCP_TABLE_OWNER_PID_CONNECTIONS, 0);
                if (result == 122 /* ERROR_INSUFFICIENT_BUFFER */) continue;
                if (result != 0) return;
                int count = Marshal.ReadInt32(buffer);
                for (int i = 0; i < count; i++)
                {
                    var row = buffer + 4 + i * rowSize;
                    uint raw = (uint)Marshal.ReadInt32(row, remotePortOffset);
                    int remotePort = (int)(((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF));   // orden de red
                    if (remotePort == port) pids.Add(Marshal.ReadInt32(row, pidOffset));
                }
                return;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }
}

/// Nombre visible de una app a partir de su proceso: el producto del .exe
/// («Obsidian», «Cursor»); para los programas de Windows, su descripción
/// («Windows PowerShell»); si no, el nombre del .exe.
public static class AppIdentity
{
    private static readonly Dictionary<string, ClientApp> cache = new(StringComparer.OrdinalIgnoreCase);

    /// Procesos del propio motor, que también se conectan a su puerto.
    private static readonly string[] Engines = ["ollama", "ollama app", "llama-server", "ollama_llama_server", "lm studio", "lms"];

    public static ClientApp? Of(int pid)
    {
        var path = Procs.ImagePath(pid);
        if (path is null) return null;
        var exe = System.IO.Path.GetFileNameWithoutExtension(path);
        if (Engines.Contains(exe.ToLowerInvariant())) return null;
        lock (cache)
        {
            if (cache.TryGetValue(path, out var known)) return known;
            string name = exe;
            try
            {
                var v = FileVersionInfo.GetVersionInfo(path);
                var product = v.ProductName?.Trim();
                var description = v.FileDescription?.Trim();
                if (!string.IsNullOrEmpty(product) && !product.Contains("Operating System", StringComparison.OrdinalIgnoreCase)) name = product;
                else if (!string.IsNullOrEmpty(description)) name = description;
            }
            catch { }
            if (name.Length > 40) name = exe;
            return cache[path] = new ClientApp(name, path);
        }
    }
}

/// Lógica pura de «quién lo despertó»: se apunta qué apps estaban conectadas en
/// cada muestra y, cuando hay actividad o se carga un modelo, se atribuye a las
/// vistas en los últimos 10 s.
public sealed class ClientTracker
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);
    private readonly object gate = new();
    private readonly Dictionary<string, (DateTime At, ClientApp App)> seen = new(StringComparer.OrdinalIgnoreCase);

    public void Seen(IEnumerable<ClientApp> apps, DateTime now)
    {
        lock (gate)
        {
            foreach (var app in apps) seen[app.Name] = (now, app);
            foreach (var old in seen.Where(s => now - s.Value.At > TimeSpan.FromMinutes(5)).Select(s => s.Key).ToList())
                seen.Remove(old);
        }
    }

    /// Las apps vistas en la ventana, la más reciente primero.
    public List<ClientApp> Recent(DateTime now)
    {
        lock (gate)
        {
            return seen.Values.Where(s => now - s.At <= Window && s.At <= now)
                .OrderByDescending(s => s.At).Select(s => s.App).ToList();
        }
    }
}
