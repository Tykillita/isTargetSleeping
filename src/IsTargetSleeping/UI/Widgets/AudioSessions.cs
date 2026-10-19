using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace IsTargetSleeping.UI;

public sealed class AudioSessionRow : System.ComponentModel.INotifyPropertyChanged
{
    private AudioSessions.ISimpleAudioVolume? control;
    private double level;
    private bool muted;
    private readonly DispatcherTimer ramp = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private double rampFrom, rampTo;
    private DateTime rampStarted;
    public string Id { get; }
    public string Name { get; }
    public string Executable { get; }
    public System.Windows.Media.ImageSource? Icon => AppIcons.For(Executable);
    public double Level { get => level; private set { level = value; Changed(nameof(Level)); Changed(nameof(Label)); } }
    public bool Muted { get => muted; private set { muted = value; Changed(nameof(Muted)); Changed(nameof(MuteHelp)); } }
    public string Label => $"{Math.Round(Level * 100):0}%";
    public string MuteHelp => Muted ? L10n.tr("Activar %@", Name) : L10n.tr("Silenciar %@", Name);
    public ICommand SetLevel { get; }
    public ICommand ToggleMute { get; }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    internal event Action<AudioSessionRow>? UserLevelChanged;

    internal AudioSessionRow(string id, string name, string executable, AudioSessions.ISimpleAudioVolume control, float level, bool muted)
    {
        Id = id; Name = name; Executable = executable; this.control = control;
        Level = Math.Clamp(level, 0, 1); Muted = muted;
        SetLevel = new ParamCommand(v => Set(v is double d ? d : Level));
        ToggleMute = new Command(() => SetMute(!Muted));
        ramp.Tick += (_, _) =>
        {
            var fraction = Math.Clamp((DateTime.UtcNow - rampStarted).TotalMilliseconds / 120, 0, 1);
            var eased = fraction * fraction * (3 - 2 * fraction);
            Level = rampFrom + (rampTo - rampFrom) * eased;
            try { control?.SetMasterVolume((float)Level, ref AudioSessions.Context); } catch { }
            if (fraction >= 1)
            {
                ramp.Stop();
                UserLevelChanged?.Invoke(this);
            }
        };
    }

    internal void Update(AudioSessions.ISimpleAudioVolume next, float value, bool isMuted)
    {
        if (control is not null && !ReferenceEquals(control, next) && Marshal.IsComObject(control)) Marshal.ReleaseComObject(control);
        control = next;
        Level = Math.Clamp(value, 0, 1); Muted = isMuted;
    }

    private void Set(double value)
    {
        rampFrom = Level;
        rampTo = Math.Clamp(value, 0, 1);
        if (Math.Abs(rampFrom - rampTo) < 0.002) return;
        rampStarted = DateTime.UtcNow;
        ramp.Stop(); ramp.Start();
    }
    private void SetMute(bool value)
    {
        Muted = value;
        try { control?.SetMute(value, ref AudioSessions.Context); } catch { }
    }
    private void Changed(string property) => PropertyChanged?.Invoke(this, new(property));
    internal void Close() { ramp.Stop(); if (control is not null && Marshal.IsComObject(control)) Marshal.ReleaseComObject(control); control = null; }
}

/// <summary>Sesiones activas del mezclador de Windows. Se enumera solo mientras el widget está visible.</summary>
public sealed class AudioSessions(Prefs prefs) : IDisposable
{
    internal static Guid Context = Guid.Empty;
    private readonly Dictionary<string, AudioSessionRow> rows = new(StringComparer.OrdinalIgnoreCase);
    private bool logged;
    public ObservableCollection<AudioSessionRow> Active { get; } = [];
    public string EmptyText => L10n.tr("Las apps aparecen aquí cuando reproducen audio.");

    public void Refresh()
    {
        var found = new Dictionary<string, AudioSessionRow>(StringComparer.OrdinalIgnoreCase);
        IMMDeviceEnumerator? devices = null;
        IMMDevice? device = null;
        object? managerObject = null;
        IAudioSessionEnumerator? sessions = null;
        try
        {
            devices = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            Marshal.ThrowExceptionForHR(devices.GetDefaultAudioEndpoint(0, 1, out device));
            var iid = typeof(IAudioSessionManager2).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out managerObject));
            var manager = (IAudioSessionManager2)managerObject;
            Marshal.ThrowExceptionForHR(manager.GetSessionEnumerator(out sessions));
            Marshal.ThrowExceptionForHR(sessions.GetCount(out var count));
            for (int n = 0; n < count; n++)
            {
                if (sessions.GetSession(n, out var session) < 0) continue;
                if (session.GetState(out var state) < 0 || state != 1 || session.GetProcessId(out var pid) < 0 || pid == 0) { Release(session); continue; }
                if (session.GetSessionInstanceIdentifier(out var id) < 0 || string.IsNullOrWhiteSpace(id)) { Release(session); continue; }
                string name = $"PID {pid}", executable = "";
                try
                {
                    using var process = Process.GetProcessById((int)pid);
                    name = process.ProcessName;
                    executable = process.MainModule?.FileName ?? "";
                }
                catch { }
                var control = (ISimpleAudioVolume)session;
                if (control.GetMasterVolume(out var volume) < 0 || control.GetMute(out var mute) < 0) { Release(session); continue; }
                if (rows.TryGetValue(id, out var existing))
                {
                    double? remembered = AudioMixer.Remembered(prefs.List(PrefKeys.AudioLevels), executable);
                    if (remembered is { } level && Math.Abs(level - volume) > 0.01)
                    {
                        control.SetMasterVolume((float)level, ref Context);
                        volume = (float)level;
                    }
                    existing.Update(control, volume, mute);
                    found[id] = existing;
                }
                else
                {
                    var row = new AudioSessionRow(id, name, executable, control, volume, mute);
                    if (AudioMixer.Remembered(prefs.List(PrefKeys.AudioLevels), executable) is { } remembered)
                    {
                        row.SetLevel.Execute(remembered);
                    }
                    row.UserLevelChanged += changed => prefs.SetList(PrefKeys.AudioLevels, AudioMixer.Remember(prefs.List(PrefKeys.AudioLevels), changed.Executable, changed.Level));
                    found[id] = row;
                }
                // El control permanece vivo en la fila; se libera cuando la sesión desaparece.
            }
            logged = false;
        }
        catch (Exception e)
        {
            if (!logged) { AppLog.Write($"mezclador por app: {e.Message}"); logged = true; }
            foreach (var row in found.Values) row.Close();
            foreach (var row in rows.Values) row.Close();
            rows.Clear(); Active.Clear();
            return;
        }
        finally
        {
            if (sessions is not null) Release(sessions);
            if (managerObject is not null) Release(managerObject);
            if (device is not null) Release(device);
            if (devices is not null) Release(devices);
        }

        foreach (var old in rows.Where(p => !found.ContainsKey(p.Key)).ToArray()) old.Value.Close();
        rows.Clear();
        foreach (var item in found) rows[item.Key] = item.Value;
        var ordered = rows.Values.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        if (!Active.Select(x => x.Id).SequenceEqual(ordered.Select(x => x.Id)))
        {
            Active.Clear(); foreach (var row in ordered) Active.Add(row);
        }
    }

    public void Dispose() { foreach (var row in rows.Values) row.Close(); rows.Clear(); Active.Clear(); }
    private static void Release(object value) { if (Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }

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
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        void __GetAudioSessionControl(); void __GetSimpleAudioVolume();
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
    }
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        void __GetDisplayName(); void __SetDisplayName(); void __GetIconPath(); void __SetIconPath();
        void __GetGroupingParam(); void __SetGroupingParam(); void __RegisterAudioSessionNotification(); void __UnregisterAudioSessionNotification();
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }
    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid context);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
