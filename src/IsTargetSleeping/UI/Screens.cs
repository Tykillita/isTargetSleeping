namespace IsTargetSleeping.UI;

/// Los monitores conectados, con su nombre estable de Windows (`\\.\DISPLAY1`) y su número tal como lo ve el usuario (1, 2…, en el orden de Windows).
public static class Screens
{
    public static List<ScreenInfo> List()
    {
        var found = new List<ScreenInfo>();
        Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr hdc, ref Win32.RECT rect, IntPtr data) =>
        {
            var info = new Win32.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFOEX>() };
            if (Win32.GetMonitorInfoEx(monitor, ref info))
                found.Add(new ScreenInfo(info.szDevice ?? "", info.rcMonitor, (info.dwFlags & 1) != 0, monitor));
            return true;
        }, IntPtr.Zero);
        return found.OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// «1», «2»…: la posición en la lista de Windows (los nombres `\.\DISPLAY1`, `DISPLAY5` saltan números cuando se desconectan pantallas).
    public static string Number(ScreenInfo screen, IReadOnlyList<ScreenInfo> all) => (all.ToList().FindIndex(s => s.Id == screen.Id) + 1).ToString();
}
