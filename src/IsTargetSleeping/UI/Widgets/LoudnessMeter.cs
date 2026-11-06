using System.Runtime.InteropServices;

namespace IsTargetSleeping.UI;

/// El volumen de lo que suena en el equipo (el pico de la salida de audio por defecto, de 0 a 1), para que las luces latan con la música.
/// Lee el medidor del propio Windows: no graba nada ni abre el micrófono.
internal sealed class LoudnessMeter : IDisposable
{
    private IAudioMeterInformation? meter;

    public float Peak()
    {
        try
        {
            meter ??= Open();
            if (meter is null) return 0;
            return meter.GetPeakValue(out var peak) >= 0 ? Math.Clamp(peak, 0f, 1f) : 0f;
        }
        catch { Dispose(); return 0; }
    }

    private static IAudioMeterInformation? Open()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        if (enumerator.GetDefaultAudioEndpoint(0, 1, out var device) < 0) return null;
        var iid = typeof(IAudioMeterInformation).GUID;
        return device.Activate(ref iid, 23, IntPtr.Zero, out var meterObject) >= 0 ? (IAudioMeterInformation)meterObject : null;
    }

    public void Dispose()
    {
        if (meter is not null && Marshal.IsComObject(meter)) Marshal.ReleaseComObject(meter);
        meter = null;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int state, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }
    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }
}
