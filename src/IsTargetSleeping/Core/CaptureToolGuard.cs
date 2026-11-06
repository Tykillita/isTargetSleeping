using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IsTargetSleeping;

/// Decide qué procesos de la herramienta de recortes se dan por colgados. Es lógica pura (sin Windows) para poder probarla: la lleva `CaptureToolGuard`.
/// Dos señales: una ventana que Windows marca como «no responde» más de `HungLimit`, o una superposición a pantalla completa que sigue ahí pasados
/// `OverlayLimit` (30 s; una captura normal dura segundos; la herramienta colgada se queda con la pantalla congelada y sigue «respondiendo» para Windows).
public sealed class CaptureToolWatch
{
    public static readonly TimeSpan OverlayLimit = TimeSpan.FromSeconds(30), HungLimit = TimeSpan.FromSeconds(8);
    private readonly Dictionary<int, DateTime> overlaySince = [], hungSince = [];

    /// Los procesos que hay que cerrar ahora (cada uno sale una sola vez). `overlay` y `hung` son los que cumplen cada señal en este instante.
    public List<int> Tick(DateTime now, IReadOnlyCollection<int> overlay, IReadOnlyCollection<int> hung)
    {
        Track(overlaySince, overlay, now);
        Track(hungSince, hung, now);
        var stuck = overlaySince.Where(p => now - p.Value >= OverlayLimit).Select(p => p.Key)
            .Union(hungSince.Where(p => now - p.Value >= HungLimit).Select(p => p.Key)).ToList();
        foreach (var pid in stuck) { overlaySince.Remove(pid); hungSince.Remove(pid); }
        return stuck;
    }

    /// Lo que deja de cumplir la señal vuelve a empezar de cero; lo nuevo empieza a contar ahora.
    private static void Track(Dictionary<int, DateTime> since, IReadOnlyCollection<int> present, DateTime now)
    {
        foreach (var pid in since.Keys.Where(k => !present.Contains(k)).ToList()) since.Remove(pid);
        foreach (var pid in present) since.TryAdd(pid, now);
    }
}

/// Cierra sola la herramienta de recortes cuando se cuelga: su superposición congela la pantalla y no deja usar ninguna otra app, ni siquiera el
/// Administrador de tareas. Corre en un hilo propio (la interfaz de esta app puede ir lenta sin afectarle) y solo toca `SnippingTool` y
/// `ScreenClippingHost`; la barra de juegos queda fuera porque su superposición dura mientras se usa.
public sealed class CaptureToolGuard : IDisposable
{
    private static readonly string[] Names = ["SnippingTool", "ScreenClippingHost"];
    private readonly CaptureToolWatch watch = new();
    private readonly Timer timer;
    private int checking;

    public CaptureToolGuard() => timer = new Timer(_ => Check(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));

    private void Check()
    {
        if (Interlocked.Exchange(ref checking, 1) == 1) return;
        try
        {
            var (overlay, hung) = Probe();
            foreach (var pid in watch.Tick(DateTime.UtcNow, overlay, hung))
            {
                bool wasHung = hung.Contains(pid);
                try
                {
                    using var process = Process.GetProcessById(pid);
                    if (!Names.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase)) continue;
                    process.Kill();
                    AppLog.Write(wasHung ? $"herramienta de recortes cerrada: no respondía ({process.ProcessName}, {pid})"
                        : $"herramienta de recortes cerrada: su superposición llevaba más de {CaptureToolWatch.OverlayLimit.TotalSeconds:0} s a pantalla completa ({process.ProcessName}, {pid})");
                }
                catch (Exception e) { AppLog.Write($"herramienta de recortes: no se pudo cerrar {pid}: {e.Message}"); }
            }
        }
        catch (Exception e) { AppLog.Write($"vigilante de la herramienta de recortes: {e.Message}"); }
        finally { Interlocked.Exchange(ref checking, 0); }
    }

    /// Los procesos de la herramienta con una ventana a pantalla completa a la vista, y los que tienen una ventana que no responde.
    private static (HashSet<int> Overlay, HashSet<int> Hung) Probe()
    {
        var overlay = new HashSet<int>();
        var hung = new HashSet<int>();
        var captureTool = new Dictionary<int, bool>();
        Win32.EnumWindows((window, _) =>
        {
            if (!Win32.IsWindowVisible(window) || Win32.GetWindowThreadProcessId(window, out int pid) == 0) return true;
            if (!captureTool.TryGetValue(pid, out bool isTool))
            {
                try { using var process = Process.GetProcessById(pid); isTool = Names.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase); }
                catch { isTool = false; }
                captureTool[pid] = isTool;
            }
            if (!isTool) return true;
            if (IsHungAppWindow(window)) hung.Add(pid);
            if (Win32.GetWindowRect(window, out var rect) && IsCaptureOverlay(window, rect)) overlay.Add(pid);
            return true;
        }, IntPtr.Zero);
        return (overlay, hung);
    }

    private const int GwlExStyle = -20;
    private const long WsExTopmost = 0x00000008, WsExTransparent = 0x00000020;

    /// La capa de recorte: una ventana siempre encima que recibe el ratón y cubre el monitor entero («XamlWindow» de 1920×1080 en un monitor así; tiene
    /// el estilo de barra de título aunque no se vea, así que eso no sirve para distinguirla). No cuentan el editor maximizado (no es siempre-encima y no
    /// tapa la barra de tareas) ni el marco de una grabación (deja pasar el ratón), que pueden durar más.
    private static bool IsCaptureOverlay(IntPtr window, Win32.RECT rect)
    {
        long exStyle = GetWindowLongPtr(window, GwlExStyle).ToInt64();
        if ((exStyle & WsExTopmost) == 0 || (exStyle & WsExTransparent) != 0) return false;
        return CoversMonitor(rect);
    }

    /// La ventana cubre todo el monitor donde está.
    private static bool CoversMonitor(Win32.RECT rect)
    {
        var monitor = Win32.MonitorFromPoint(new Win32.POINT { X = rect.Left + rect.Width / 2, Y = rect.Top + rect.Height / 2 }, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        if (monitor == IntPtr.Zero || !Win32.GetMonitorInfo(monitor, ref info)) return false;
        return rect.Width >= info.rcMonitor.Width * 0.98 && rect.Height >= info.rcMonitor.Height * 0.98;
    }

    [DllImport("user32.dll")] private static extern bool IsHungAppWindow(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    public void Dispose() => timer.Dispose();
}
