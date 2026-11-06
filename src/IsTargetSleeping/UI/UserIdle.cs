using System.Runtime.InteropServices;

namespace IsTargetSleeping.UI;

/// Cuánto lleva el usuario sin tocar el teclado ni el ratón (de todo Windows, no solo de la app).
internal static class UserIdle
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { public uint Size; public uint Time; }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    public static double Seconds()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        // Si Windows no responde, se cuenta como usuario activo: la mascota no sale.
        return GetLastInputInfo(ref info) ? unchecked((uint)Environment.TickCount - info.Time) / 1000.0 : 0;
    }
}
