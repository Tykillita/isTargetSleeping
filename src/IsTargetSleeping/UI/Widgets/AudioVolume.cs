using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace IsTargetSleeping.UI;

/// El volumen general de Windows (el del altavoz por defecto), para enseñarlo en el notch cuando cambia. Solo lee: nunca lo toca,
/// y no sustituye al indicador de Windows (que no se puede quitar de forma soportada): se enseña además de él.
public sealed class AudioVolume
{
    private IAudioEndpointVolume? endpoint;
    private VolumeCallback? callback;
    private DispatcherTimer? expire;
    private Dispatcher? ui;

    public float Level { get; private set; }
    public bool Muted { get; private set; }
    public DateTime ChangedAt { get; private set; }
    /// Cierto durante un par de segundos tras cada cambio: lo que dura la actividad en el notch.
    public bool Recent => ChangedAt != default && DateTime.Now - ChangedAt < Visible;
    public static readonly TimeSpan Visible = TimeSpan.FromSeconds(1.8);
    public event Action? Changed;

    public string Text => Muted ? L10n.tr("Silencio") : $"{(int)Math.Round(Level * 100)} %";

    /// Empieza a escuchar los cambios (una sola vez).
    public void Start()
    {
        if (endpoint is not null) return;
        try
        {
            ui = Application.Current.Dispatcher;
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0 /* render */, 1 /* multimedia */, out var device));
            var iid = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var volume));
            var control = (IAudioEndpointVolume)volume;
            control.GetMasterVolumeLevelScalar(out var level);
            control.GetMute(out var muted);
            (Level, Muted) = (level, muted);
            callback = new VolumeCallback(this);
            Marshal.ThrowExceptionForHR(control.RegisterControlChangeNotify(callback));
            endpoint = control;
        }
        catch (Exception e)
        {
            AppLog.Write($"volumen: {e.Message}");
            endpoint = null;
        }
    }

    private void OnNotify(float level, bool muted) => ui?.BeginInvoke(() =>
    {
        if (level == Level && muted == Muted) return;
        (Level, Muted, ChangedAt) = (level, muted, DateTime.Now);
        Changed?.Invoke();
        // Al pasar el tiempo la actividad se retira: hay que avisar una vez más.
        expire ??= new DispatcherTimer { Interval = Visible + TimeSpan.FromMilliseconds(100) };
        expire.Stop();
        expire.Tick -= OnExpire;
        expire.Tick += OnExpire;
        expire.Start();
    });

    private void OnExpire(object? sender, EventArgs e)
    {
        expire?.Stop();
        Changed?.Invoke();
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    /// El orden de los métodos es el de la tabla del objeto COM: no se puede cambiar ni quitar ninguno.
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr notifyData);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotificationData
    {
        public Guid EventContext;
        [MarshalAs(UnmanagedType.Bool)] public bool Muted;
        public float MasterVolume;
        public uint Channels;
    }

    [ClassInterface(ClassInterfaceType.None)]
    private sealed class VolumeCallback(AudioVolume owner) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr notifyData)
        {
            var data = Marshal.PtrToStructure<NotificationData>(notifyData);
            owner.OnNotify(data.MasterVolume, data.Muted);
            return 0;
        }
    }
}
