using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace IsTargetSleeping.UI;

/// <summary>Lee cambios de brillo, teclas de bloqueo y aparatos Bluetooth; no oculta los avisos de Windows.</summary>
public sealed class SystemHuds : IDisposable
{
    private const int BrightnessEventWaitMs = 5000;
    private readonly KeyboardLocks locks;
    private int lastBrightness = -1;
    private DateTime brightnessAt;
    private readonly Dictionary<string, string> bluetoothDevices = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DeviceWatcher> bluetoothWatchers = [];
    private int bluetoothWatcherCount, bluetoothEnumerations, bluetoothGeneration;
    private bool bluetoothBaselineReady, disposed;
    private (string Name, bool Connected, int? Percent, DeviceKind Kind, DateTime At)? bluetoothInfo;
    private bool brightnessEnabled, bluetoothEnabled;
    private bool brightnessReading;
    private CancellationTokenSource? brightnessEvents;
    private readonly DispatcherTimer brightnessPoll = new() { Interval = TimeSpan.FromSeconds(1) };
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
            if (wantsBrightness) { PollBrightness(); brightnessPoll.Start(); } else brightnessPoll.Stop();
        }
        locks.BrightnessEnabled = wantsBrightness;
        var wantsBluetooth = ids.Contains("bluetooth");
        if (wantsBluetooth != bluetoothEnabled)
        {
            bluetoothEnabled = wantsBluetooth;
            if (wantsBluetooth) StartBluetoothWatchers();
            else StopBluetoothWatchers();
        }
        locks.Enabled = ids.Contains("locks");
        if (locks.Enabled && !languagePoll.IsEnabled) { lastLanguage = 0; languagePoll.Start(); }
        else if (!locks.Enabled) languagePoll.Stop();
    }

    public SystemHuds()
    {
        locks = new KeyboardLocks(() => Changed?.Invoke(), ProbeBrightnessKey);
        brightnessPoll.Tick += (_, _) => PollBrightness();
        languagePoll.Tick += (_, _) => PollLanguage();
    }

    /// El último cambio de brillo (porcentaje y cuándo), para el aviso del notch.
    public (int Percent, DateTime At)? BrightnessInfo => lastBrightness >= 0 && brightnessAt != default ? (lastBrightness, brightnessAt) : null;
    /// La última tecla de bloqueo pulsada (Mayús o Núm), si quedó encendida y cuándo.
    public (bool Caps, bool On, DateTime At)? LockInfo => locks.Last;
    /// El último cambio de idioma del teclado: nombre, etiqueta de dos letras y cuándo.
    public (string Name, string Chip, DateTime At)? LanguageInfo => languageName.Length > 0 ? (languageName, languageChip, languageAt) : null;
    private int lastLanguage;
    private string languageName = "", languageChip = "";
    private DateTime languageAt;
    private readonly DispatcherTimer languagePoll = new() { Interval = TimeSpan.FromMilliseconds(450) };

    /// Mira la distribución del teclado de la ventana de primer plano; avisa cuando cambia (no al empezar).
    private void PollLanguage()
    {
        var window = Win32.GetForegroundWindow();
        if (window == IntPtr.Zero) return;
        uint thread = (uint)Win32.GetWindowThreadProcessId(window, out _);
        int language = (int)((long)Win32.GetKeyboardLayout(thread) & 0xFFFF);
        if (language == 0 || language == lastLanguage) return;
        bool first = lastLanguage == 0;
        lastLanguage = language;
        if (first) return;
        try
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo(language);
            languageName = culture.DisplayName;
            languageChip = culture.TwoLetterISOLanguageName.ToUpperInvariant();
        }
        catch { return; }
        languageAt = DateTime.Now;
        Changed?.Invoke();
    }

    public string? Brightness => lastBrightness >= 0 && DateTime.Now - brightnessAt < TimeSpan.FromSeconds(2.2) ? $"{lastBrightness} %" : null;
    public string? LocksText => locks.Recent;
    /// El último aparato Bluetooth que se conectó o desconectó: nombre, si quedó conectado, su batería (si la dice) y cuándo.
    public (string Name, bool Connected, int? Percent, DeviceKind Kind, DateTime At)? BluetoothInfo => bluetoothInfo;

    private void PollBrightness()
    {
        if (!brightnessEnabled || brightnessReading) return;
        brightnessReading = true;
        _ = Task.Run(ReadBrightness).ContinueWith(task => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            brightnessReading = false;
            if (!brightnessEnabled || task.Status != TaskStatus.RanToCompletion || task.Result < 0) return;
            SetBrightness(task.Result, false);
        }));
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
            await Task.Delay(40);
            int value = ReadBrightness();
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (!brightnessEnabled || value < 0) return;
                SetBrightness(value, true);
            });
        });
    }

    private void WatchBrightnessEvents(CancellationToken cancellation)
    {
        // Si el vigilante se cae (WMI lo hace), se vuelve a conectar: antes terminaba tras el primer evento y el brillo dejaba de avisar.
        // No se llama a ReleaseComObject: WMI reparte el mismo objeto de servicio entre las lecturas y el vigilante, y soltarlo a la fuerza estropeaba a los demás.
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                var locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator")!)!;
                dynamic dynamicLocator = locator;
                dynamic service = dynamicLocator.ConnectServer(".", @"root\wmi");
                dynamic source = service.ExecNotificationQuery("SELECT * FROM WmiMonitorBrightnessEvent");
                while (!cancellation.IsCancellationRequested)
                {
                    try
                    {
                        dynamic item = source.NextEvent(BrightnessEventWaitMs);
                        if (item is null) continue;
                        int value = Convert.ToInt32(item.Properties_.Item("Brightness").Value);   // por Properties_: leer la propiedad directo falla a ratos con «Unspecified error»
                        Application.Current?.Dispatcher.BeginInvoke(() =>
                        {
                            if (brightnessEnabled) SetBrightness(value, true);
                        });
                    }
                    catch (COMException e) when (e.ErrorCode == unchecked((int)0x80043001)) { }   // sin evento en este segundo
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                AppLog.Write($"brillo: el vigilante de eventos se reconecta: {e.Message}");
                if (cancellation.WaitHandle.WaitOne(2000)) return;
            }
        }
    }

    private void StartBluetoothWatchers()
    {
        bluetoothDevices.Clear();
        bluetoothWatcherCount = bluetoothEnumerations = 0;
        bluetoothGeneration++;
        bluetoothBaselineReady = false;
        string[] selectors;
        try
        {
            selectors =
            [
                BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
                BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
            ];
        }
        catch (Exception e)
        {
            bluetoothBaselineReady = true;
            AppLog.Write($"bluetooth: no se pudieron obtener los selectores: {e.Message}");
            return;
        }
        foreach (var selector in selectors)
        {
            DeviceWatcher? watcher = null;
            try
            {
                watcher = DeviceInformation.CreateWatcher(selector);
                watcher.Added += BluetoothAdded;
                watcher.Removed += BluetoothRemoved;
                watcher.EnumerationCompleted += BluetoothEnumerationCompleted;
                watcher.Stopped += BluetoothStopped;
                bluetoothWatchers.Add(watcher);
                watcher.Start();
                bluetoothWatcherCount++;
            }
            catch (Exception e)
            {
                if (watcher is not null)
                {
                    bluetoothWatchers.Remove(watcher);
                    DetachBluetoothWatcher(watcher);
                }
                AppLog.Write($"bluetooth: no se pudo iniciar el vigilante: {e.Message}");
            }
        }
        if (bluetoothWatcherCount == 0) bluetoothBaselineReady = true;
    }

    private void BluetoothAdded(DeviceWatcher sender, DeviceInformation info) => Application.Current?.Dispatcher.BeginInvoke(() =>
    {
        if (!bluetoothEnabled || !bluetoothWatchers.Contains(sender)) return;
        var name = TrackBluetoothDevice(info.Id, info.Name, true);
        if (name is not null) PublishBluetooth(name, true);
    });

    private void BluetoothRemoved(DeviceWatcher sender, DeviceInformationUpdate info) => Application.Current?.Dispatcher.BeginInvoke(() =>
    {
        if (!bluetoothEnabled || !bluetoothWatchers.Contains(sender)) return;
        var name = TrackBluetoothDevice(info.Id, null, false);
        if (name is not null) PublishBluetooth(name, false);
    });

    private void BluetoothEnumerationCompleted(DeviceWatcher sender, object args) => Application.Current?.Dispatcher.BeginInvoke(() =>
    {
        if (!bluetoothEnabled || !bluetoothWatchers.Contains(sender)) return;
        if (++bluetoothEnumerations >= bluetoothWatcherCount) bluetoothBaselineReady = true;
    });

    private void BluetoothStopped(DeviceWatcher sender, object args) => Application.Current?.Dispatcher.BeginInvoke(async () =>
    {
        if (!bluetoothEnabled || !bluetoothWatchers.Contains(sender)) return;
        AppLog.Write($"bluetooth: un vigilante se detuvo inesperadamente ({sender.Status}) y se recuperará");
        StopBluetoothWatchers();
        int generation = bluetoothGeneration;
        await Task.Delay(2000);
        if (bluetoothEnabled && !disposed && generation == bluetoothGeneration) StartBluetoothWatchers();
    });

    private string? TrackBluetoothDevice(string id, string? name, bool connected)
    {
        if (connected)
        {
            if (string.IsNullOrWhiteSpace(name) || bluetoothDevices.ContainsKey(id)) return null;
            bluetoothDevices[id] = name;
            return bluetoothBaselineReady ? name : null;
        }
        if (!bluetoothDevices.Remove(id, out var previous)) return null;
        return bluetoothBaselineReady ? previous : null;
    }

    private void PublishBluetooth(string name, bool connected)
    {
        var at = DateTime.Now;
        var kind = DeviceKinds.Classify(name);
        void Publish(int? percent) => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (!bluetoothEnabled || disposed) return;
            bluetoothInfo = (name, connected, percent, kind, at);
            Changed?.Invoke();
        });
        Publish(null);
        if (!connected) return;
        _ = Task.Run(() => ReadBluetoothBattery(name, Publish));
    }

    private static async Task ReadBluetoothBattery(string name, Action<int?> publish)
    {
        foreach (var wait in new[] { 700, 1300 })
        {
            await Task.Delay(wait);
            try
            {
                var battery = (await DeviceBatteryReader.ReadAsync()).FirstOrDefault(b => name.Contains(b.Name, StringComparison.OrdinalIgnoreCase) || b.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
                if (battery is not null) { publish(battery.Percent); return; }
            }
            catch { }
        }
    }

    private void StopBluetoothWatchers()
    {
        bluetoothGeneration++;
        foreach (var watcher in bluetoothWatchers.ToArray())
        {
            DetachBluetoothWatcher(watcher);
            try
            {
                if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                    watcher.Stop();
            }
            catch { }
        }
        bluetoothWatchers.Clear();
        bluetoothDevices.Clear();
        bluetoothWatcherCount = bluetoothEnumerations = 0;
        bluetoothBaselineReady = false;
    }

    private void DetachBluetoothWatcher(DeviceWatcher watcher)
    {
        watcher.Added -= BluetoothAdded;
        watcher.Removed -= BluetoothRemoved;
        watcher.EnumerationCompleted -= BluetoothEnumerationCompleted;
        watcher.Stopped -= BluetoothStopped;
    }

    private static int ReadBrightness()
    {
        try
        {
            var locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator")!)!;
            dynamic dynamicLocator = locator;
            dynamic service = dynamicLocator.ConnectServer(".", @"root\wmi");
            dynamic items = service.ExecQuery("SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (dynamic item in items) return Convert.ToInt32(item.Properties_.Item("CurrentBrightness").Value);
        }
        catch (Exception e) { AppLog.Write($"brillo: lectura falló: {e.Message}"); }
        return -1;
    }

    public void Dispose()
    {
        disposed = true;
        bluetoothEnabled = false;
        StopBluetoothWatchers();
        brightnessPoll.Stop(); brightnessEvents?.Cancel(); brightnessEvents?.Dispose(); locks.Dispose();
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
    /// La última tecla de bloqueo pulsada: si fue Mayús, si quedó encendida y cuándo.
    public (bool Caps, bool On, DateTime At)? Last { get; private set; }
    private string? text;

    public KeyboardLocks(Action changed, Action brightnessKey)
    {
        this.changed = changed;
        this.brightnessKey = brightnessKey;
        callback = OnKey;
    }

    private Thread? hookThread;
    private uint hookThreadId;

    private void SyncHook()
    {
        bool wanted = enabled || brightnessEnabled;
        if (wanted && hookThread is null) StartHook();
        else if (!wanted && hookThread is not null) StopHook();
    }

    // El gancho de teclado global vive en un hilo propio con su bucle de mensajes. Windows espera la respuesta del gancho antes de
    // entregar cada tecla a todo el sistema: en el hilo de la interfaz, cualquier pausa (animaciones, memoria, un cuelgue)
    // retrasaba el teclado de todo el PC; así, la interfaz puede ir lenta sin tocar el teclado de nadie.
    private void StartHook()
    {
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            hookThreadId = GetCurrentThreadId();
            hook = SetWindowsHookEx(WhKeyboardLl, callback, GetModuleHandle(null), 0);
            var installed = hook;
            ready.Set();
            if (installed == IntPtr.Zero) return;
            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
            UnhookWindowsHookEx(installed);
        }) { IsBackground = true, Name = "isTargetSleeping keyboard hook", Priority = ThreadPriority.Highest };
        thread.Start();
        ready.Wait(TimeSpan.FromSeconds(2));
        hookThread = hook == IntPtr.Zero ? null : thread;
    }

    private void StopHook()
    {
        var thread = hookThread;
        hookThread = null;
        if (thread is null) return;
        PostThreadMessage(hookThreadId, 0x0012 /* WM_QUIT */, UIntPtr.Zero, IntPtr.Zero);
        thread.Join(TimeSpan.FromSeconds(1));
        hook = IntPtr.Zero;
    }

    /// Se ejecuta en el hilo del gancho: solo mira la tecla y avisa a la interfaz; nada de trabajo aquí.
    private IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            uint key = (uint)Marshal.ReadInt32(lParam);   // KBDLLHOOKSTRUCT.vkCode es el primer campo
            int message = wParam.ToInt32();
            bool brightness = brightnessEnabled && (message is WmKeyDown or WmSysKeyDown)
                && (key is 0x74 or 0x75 or 0x87 or 0x88 || key is >= 0x97 and <= 0x9F);
            bool lockKey = enabled && message is WmKeyUp or WmSysKeyUp && key is 0x14 or 0x90;
            if (brightness) Application.Current?.Dispatcher.BeginInvoke(brightnessKey);
            if (lockKey)
            {
                bool on = (GetKeyState((int)key) & 1) != 0;
                Application.Current?.Dispatcher.BeginInvoke(() => ShowLock(key == 0x14, on));
            }
        }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private void ShowLock(bool caps, bool on)
    {
        if (!enabled) return;
        text = L10n.tr(caps ? on ? "Bloq. Mayús activado" : "Bloq. Mayús desactivado" : on ? "Bloq. Núm activado" : "Bloq. Núm desactivado");
        changedAt = DateTime.Now;
        Last = (caps, on, changedAt);
        changed();
    }

    public void Dispose() { enabled = brightnessEnabled = false; SyncHook(); }
    [StructLayout(LayoutKind.Sequential)] private struct Msg { public IntPtr Hwnd; public uint Message; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X, Y; }
    [DllImport("user32.dll")] private static extern int GetMessage(out Msg msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    private delegate IntPtr Win32Hook(int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, Win32Hook callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? module);
}
