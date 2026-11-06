using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Animation;

namespace IsTargetSleeping.UI;

/// WPF anima a 60 fotogramas por segundo por defecto, aunque el monitor sea de 100 o 144 Hz: el notch se veía menos fluido que las apps que van a la frecuencia de la pantalla.
/// Al arrancar se pide la frecuencia del monitor más rápido (hasta 240). Las animaciones que piden la suya (la barra del día, a 20) la conservan.
internal static class FrameRate
{
    public static void MatchDisplays()
    {
        int hz = 60;
        try { hz = Math.Clamp(FastestDisplay(), 60, 240); }
        catch (Exception e) { AppLog.Write($"frecuencia de pantalla: {e.Message}"); }
        if (hz > 60) Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata(hz));
    }

    private static int FastestDisplay()
    {
        int best = 0;
        for (uint i = 0; i < 16; i++)
        {
            var device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, i, ref device, 0)) break;
            if ((device.StateFlags & 1) == 0) continue;   // DISPLAY_DEVICE_ATTACHED_TO_DESKTOP
            var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (EnumDisplaySettings(device.DeviceName, -1, ref mode)) best = Math.Max(best, (int)mode.dmDisplayFrequency);
        }
        return best;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency, dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string? device, uint index, ref DISPLAY_DEVICE displayDevice, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettings(string? device, int mode, ref DEVMODE devMode);
}
