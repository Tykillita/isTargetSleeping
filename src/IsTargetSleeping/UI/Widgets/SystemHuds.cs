using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace IsTargetSleeping.UI;

/// <summary>Lee cambios de brillo, teclas de bloqueo y salidas Bluetooth; no oculta los avisos de Windows.</summary>
public sealed class SystemHuds : IDisposable
{
    private readonly KeyboardLocks locks;
    private int lastBrightness = -1, ticks;
    private DateTime brightnessAt, bluetoothAt;
    private HashSet<string>? bluetoothDevices;
    private string bluetoothText = "";
    private bool brightnessEnabled, bluetoothEnabled;
    private bool brightnessReading;
    private CancellationTokenSource? brightnessEvents;
    public long BrightnessRevision { get; private set; }
    public event Action? Changed;

    public void Configure(IEnumerable<string> enabled)
    {
        var ids = enabled.ToHashSet(StringComparer.Ordinal);
        var wantsBrightness = ids.Contains("brightness");
        if (wantsBrightness != brightnessEnabled)
        {
            brightnessEnabled = wantsBrightness;
            brightnessEvents?.Cancel();
            brightnessEvents?.Dispose();
            brightnessEvents = wantsBrightness ? new CancellationTokenSource() : null;
            if (brightnessEvents is { } watcher) _ = Task.Run(() => WatchBrightnessEvents(watcher.Token));
        }
        locks.BrightnessEnabled = wantsBrightness;
        bluetoothEnabled = ids.Contains("bluetooth");
        locks.Enabled = ids.Contains("locks");
    }

    public SystemHuds()
    {
        locks = new KeyboardLocks(() => Changed?.Invoke(), ProbeBrightnessKey);
    }

    public string? Brightness => lastBrightness >= 0 && DateTime.Now - brightnessAt < TimeSpan.FromSeconds(2.2) ? $"{lastBrightness} %" : null;
    public string? LocksText => locks.Recent;
    public string? Bluetooth => DateTime.Now - bluetoothAt < TimeSpan.FromSeconds(3) ? bluetoothText : null;

    public void Refresh()
    {
        ticks++;
        if (brightnessEnabled && !brightnessReading)
        {
            brightnessReading = true;
            _ = Task.Run(ReadBrightness).ContinueWith(task => Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                brightnessReading = false;
                if (!brightnessEnabled || task.Status != TaskStatus.RanToCompletion || task.Result < 0) return;
                SetBrightness(task.Result, false);
            }));
        }
        if (bluetoothEnabled && ticks % 3 == 0) ReadBluetooth();
    }

    private void SetBrightness(int value, bool isEvent)
    {
        if (lastBrightness == value && !isEvent) return;
        bool changed = lastBrightness >= 0 || isEvent;
        lastBrightness = value;
        if (changed) { brightnessAt = DateTime.Now; BrightnessRevision++; Changed?.Invoke(); }
    }

    private void ProbeBrightnessKey()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(80);
            int value = ReadBrightness();
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (!brightnessEnabled || value < 0) return;
                if (lastBrightness < 0) SetBrightness(value, false);
                else if (lastBrightness != value) SetBrightness(value, true);
            });
        });
    }

    private void WatchBrightnessEvents(CancellationToken cancellation)
    {
        object? locator = null, service = null, source = null, current = null;
        try
        {
            locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator")!)!;
            dynamic dynamicLocator = locator;
            service = dynamicLocator.ConnectServer(".", @"root\wmi");
            dynamic dynamicService = service;
            source = dynamicService.ExecNotificationQuery("SELECT * FROM WmiMonitorBrightnessEvent");
            dynamic dynamicSource = source;
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    current = dynamicSource.NextEvent(1000);
                    if (current is not null)
                    {
                        dynamic item = current;
                        int value = (int)item.Brightness;
                        Application.Current?.Dispatcher.BeginInvoke(() =>
                        {
                            if (brightnessEnabled) SetBrightness(value, true);
                        });
                    }
                }
                catch (COMException e) when (e.ErrorCode == unchecked((int)0x80043001)) { }
                finally { if (current is not null) { Release(current); current = null; } }
            }
        }
        catch { }
        finally
        {
            if (current is not null) Release(current);
            if (source is not null) Release(source);
            if (service is not null) Release(service);
            if (locator is not null) Release(locator);
        }
    }

    private void ReadBluetooth()
    {
        // shortcut: polls audio endpoint IDs every 3 seconds; replace with DeviceWatcher when Windows exposes reliable Bluetooth endpoint changes.
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(0, 1, out collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out var count));
            var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out var device) < 0) continue;
                if (device.GetId(out var id) >= 0 && (id.Contains("BTH", StringComparison.OrdinalIgnoreCase) || id.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase))) current.Add(id);
                Release(device);
            }
            if (bluetoothDevices is not null && !current.SetEquals(bluetoothDevices))
            {
                bluetoothText = current.Count > bluetoothDevices.Count ? L10n.tr("Auricular conectado") : L10n.tr("Auricular desconectado");
                bluetoothAt = DateTime.Now; Changed?.Invoke();
            }
            bluetoothDevices = current;
        }
        catch { }
        finally { if (collection is not null) Release(collection); if (enumerator is not null) Release(enumerator); }
    }

    private static int ReadBrightness()
    {
        object? locator = null, service = null, items = null, currentItem = null;
        try
        {
            locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator")!)!;
            dynamic dynamicLocator = locator;
            service = dynamicLocator.ConnectServer(".", @"root\wmi");
            dynamic dynamicService = service;
            items = dynamicService.ExecQuery("SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (dynamic item in (dynamic)items)
            {
                currentItem = item;
                var value = (int)item.CurrentBrightness;
                Release(currentItem); currentItem = null;
                return value;
            }
        }
        catch { }
        finally { if (currentItem is not null) Release(currentItem); if (items is not null) Release(items); if (service is not null) Release(service); if (locator is not null) Release(locator); }
        return -1;
    }

    public void Dispose() { brightnessEvents?.Cancel(); brightnessEvents?.Dispose(); locks.Dispose(); }
    private static void Release(object value) { if (Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int state, out IMMDeviceCollection devices);
        void __GetDefaultAudioEndpoint();
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-C0A2A1A5C3B9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        void __Activate(); void __OpenPropertyStore();
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }
}

internal sealed class KeyboardLocks : IDisposable
{
    private const int WhKeyboardLl = 13, WmKeyDown = 0x100, WmSysKeyDown = 0x104, WmKeyUp = 0x101, WmSysKeyUp = 0x105;
    private readonly Win32Hook callback;
    private readonly Action changed, brightnessKey;
    private IntPtr hook;
    private bool enabled, brightnessEnabled;
    private DateTime changedAt;
    public bool Enabled
    {
        get => enabled;
        set
        {
            if (enabled == value) return;
            enabled = value;
            SyncHook();
        }
    }
    public bool BrightnessEnabled
    {
        get => brightnessEnabled;
        set { if (brightnessEnabled == value) return; brightnessEnabled = value; SyncHook(); }
    }
    public string? Recent => Enabled && DateTime.Now - changedAt < TimeSpan.FromSeconds(2.2) ? text : null;
    private string? text;

    public KeyboardLocks(Action changed, Action brightnessKey)
    {
        this.changed = changed;
        this.brightnessKey = brightnessKey;
        callback = OnKey;
    }

    private void SyncHook()
    {
        if ((enabled || brightnessEnabled) && hook == IntPtr.Zero) hook = SetWindowsHookEx(WhKeyboardLl, callback, GetModuleHandle(null), 0);
        else if (!enabled && !brightnessEnabled && hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
    }

    private IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var key = Marshal.PtrToStructure<KbdLlHookStruct>(lParam).VkCode;
            int message = wParam.ToInt32();
            if (brightnessEnabled && (message is WmKeyDown or WmSysKeyDown) && (key is 0x74 or 0x75 || key is >= 0x97 and <= 0x9F))
                brightnessKey();
            if (enabled && (message is WmKeyUp or WmSysKeyUp) && (key is 0x14 or 0x90))
            {
                text = L10n.tr(key == 0x14
                    ? (GetKeyState((int)key) & 1) != 0 ? "Bloq. Mayús activado" : "Bloq. Mayús desactivado"
                    : (GetKeyState((int)key) & 1) != 0 ? "Bloq. Núm activado" : "Bloq. Núm desactivado");
                changedAt = DateTime.Now;
                changed();
            }
        }
        return CallNextHookEx(hook, code, wParam, lParam);
    }

    public void Dispose() { enabled = brightnessEnabled = false; SyncHook(); }
    [StructLayout(LayoutKind.Sequential)] private struct KbdLlHookStruct { public uint VkCode, ScanCode, Flags, Time; public IntPtr ExtraInfo; }
    private delegate IntPtr Win32Hook(int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, Win32Hook callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? module);
}
