using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI.Widgets;

/// Detecta el uso de otras apps y solo captura vídeo tras abrir expresamente la página Cámara.
public sealed class CameraPreview : INotifyPropertyChanged, IDisposable
{
    private const string ConsentKey = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";
    private const string ConsentStoreKey = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";
    private const string CapabilityAccessKey = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager";
    private readonly Dispatcher dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    private readonly object frameGate = new();
    private CancellationTokenSource? usageMonitor;
    private MediaCapture? capture;
    private MediaFrameReader? reader;
    private ImageSource? frame;
    private WriteableBitmap? frameBitmap;
    private Windows.Storage.Streams.Buffer? frameBuffer;
    private byte[]? framePixels;
    private string? error;
    private bool privacyDenied;
    private bool systemInUse, previewing, starting, disposed;
    private bool appListAvailable;
    private CameraApplication[] activeApplications = [];
    private int generation, frameBusy;
    private long lastFrameAt;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? UsageChanged;

    public ImageSource? Frame { get => frame; private set { frame = value; Changed(); Changed(nameof(HasFrame)); Changed(nameof(HasNoFrame)); } }
    public bool HasFrame => Frame is not null;
    public bool HasNoFrame => Frame is null;
    public bool PreviewEnabled => starting || previewing;
    public bool IsInUse => systemInUse || previewing;
    public IReadOnlyList<CameraApplication> ActiveApplications => activeApplications;
    public bool HasActiveApplications => activeApplications.Length > 0;
    public bool NoActiveApplications => !HasActiveApplications;
    public string ActiveApplicationsText => !appListAvailable ? tr("Windows no pudo identificar las apps que usan la cámara.")
        : activeApplications.Length == 0 ? tr("Ninguna app está transmitiendo ahora.") : "";
    public bool IsPreviewing => previewing;
    public bool HasError => error is not null;
    public bool NeedsPrivacyHelp => privacyDenied;
    public string ErrorText => error ?? "";
    public string StatusText => error is not null ? tr("Sin acceso") : starting ? tr("Conectando…") : previewing ? tr("Vista previa activa") : IsInUse ? tr("Cámara en uso") : tr("Apagada");
    public string EmptyText => starting ? tr("Conectando con la cámara…") : tr("La vista previa aparece aquí mientras esta página esté abierta.");

    public void StartMonitoring()
    {
        if (disposed || usageMonitor is not null) return;
        var monitor = new CancellationTokenSource();
        usageMonitor = monitor;
        _ = MonitorSystemUsage(monitor);
    }

    public void StopMonitoring() => Interlocked.Exchange(ref usageMonitor, null)?.Cancel();

    private async Task MonitorSystemUsage(CancellationTokenSource monitor)
    {
        try
        {
            await Task.Run(() => WatchSystemUsage(monitor.Token), monitor.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { AppLog.Write($"Cámara: estado: {e.Message}"); }
        finally
        {
            Interlocked.CompareExchange(ref usageMonitor, null, monitor);
            monitor.Dispose();
        }
    }

    private void WatchSystemUsage(CancellationToken cancellation)
    {
        PublishSystemUsage(ReadSystemUsage(), [], appListAvailable: false, cancellation);
        using var sensorMonitor = CameraSensorActivityMonitor.TryStart((inUse, processIds) =>
        {
            PublishSystemUsage(inUse, ResolveApplications(processIds), appListAvailable: true, cancellation);
        });

        if (sensorMonitor is not null)
        {
            PublishApplicationListAvailable(cancellation);
            cancellation.WaitHandle.WaitOne();
            return;
        }

        using var userKey = OpenUsageWatchKey(Registry.CurrentUser);
        using var machineKey = OpenUsageWatchKey(Registry.LocalMachine);
        using var userChanged = new ManualResetEvent(false);
        using var machineChanged = new ManualResetEvent(false);
        ArmUsageWatch(userKey, userChanged);
        ArmUsageWatch(machineKey, machineChanged);
        var waits = new WaitHandle[] { userChanged, machineChanged, cancellation.WaitHandle };

        while (!cancellation.IsCancellationRequested)
        {
            // Keep registry events fast, with a periodic recovery read if Windows misses an event.
            int signaled = WaitHandle.WaitAny(waits, 350);
            if (signaled == 2 || cancellation.IsCancellationRequested) break;
            if (signaled == 0) ArmUsageWatch(userKey, userChanged);
            else if (signaled == 1) ArmUsageWatch(machineKey, machineChanged);
            PublishSystemUsage(ReadSystemUsage(), [], appListAvailable: false, cancellation);
        }
    }

    private static RegistryKey? OpenUsageWatchKey(RegistryKey hive)
    {
        try
        {
            return hive.OpenSubKey(ConsentStoreKey, RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.ReadKey)
                ?? hive.OpenSubKey(CapabilityAccessKey, RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.ReadKey);
        }
        catch { return null; }
    }

    private static void ArmUsageWatch(RegistryKey? key, EventWaitHandle changed)
    {
        if (key is null) return;
        changed.Reset();
        RegNotifyChangeKeyValue(key.Handle, true, 0x00000001 | 0x00000004 | 0x10000000, changed.SafeWaitHandle, true);
    }

    private void PublishSystemUsage(bool inUse, CameraApplication[] applications, bool appListAvailable, CancellationToken cancellation)
    {
        if (cancellation.IsCancellationRequested || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
        try
        {
            dispatcher.BeginInvoke(() =>
            {
                if (!cancellation.IsCancellationRequested && Volatile.Read(ref usageMonitor) is { } monitor && monitor.Token == cancellation)
                    SetSystemUsage(inUse, applications, appListAvailable);
            }, DispatcherPriority.DataBind);
        }
        catch (InvalidOperationException) { }
    }

    private void PublishApplicationListAvailable(CancellationToken cancellation)
    {
        if (cancellation.IsCancellationRequested || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
        try
        {
            dispatcher.BeginInvoke(() =>
            {
                if (!cancellation.IsCancellationRequested && Volatile.Read(ref usageMonitor) is { } monitor && monitor.Token == cancellation
                    && !appListAvailable)
                {
                    appListAvailable = true;
                    Changed(nameof(ActiveApplicationsText));
                }
            }, DispatcherPriority.DataBind);
        }
        catch (InvalidOperationException) { }
    }

    public void StartPreview()
    {
        if (disposed || previewing || starting) return;
        starting = true;
        error = null;
        privacyDenied = false;
        Changed(nameof(PreviewEnabled)); Changed(nameof(StatusText)); Changed(nameof(EmptyText)); Changed(nameof(HasError)); Changed(nameof(ErrorText)); Changed(nameof(NeedsPrivacyHelp));
        var run = Interlocked.Increment(ref generation);
        _ = StartPreviewAsync(run);
    }

    public void StopPreview()
    {
        if (!starting && !previewing && capture is null && reader is null) return;
        bool wasInUse = IsInUse;
        Interlocked.Increment(ref generation);
        starting = previewing = false;
        Frame = null;
        frameBitmap = null;
        lock (frameGate) { frameBuffer = null; framePixels = null; }
        Changed(nameof(PreviewEnabled)); Changed(nameof(StatusText)); Changed(nameof(EmptyText)); Changed(nameof(IsPreviewing));
        if (wasInUse != IsInUse) UsageChanged?.Invoke();
        _ = ReleaseAsync();
    }

    private async Task StartPreviewAsync(int run)
    {
        MediaCapture? nextCapture = null;
        MediaFrameReader? nextReader = null;
        try
        {
            var groups = await MediaFrameSourceGroup.FindAllAsync();
            var selected = groups.Select(group => (Group: group, Source: group.SourceInfos.FirstOrDefault(info => info.SourceKind == MediaFrameSourceKind.Color)))
                .FirstOrDefault(item => item.Source is not null);
            if (selected.Source is null) throw new InvalidOperationException("No camera source found.");

            nextCapture = new MediaCapture();
            await nextCapture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                SourceGroup = selected.Group,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly,
            });
            if (run != Volatile.Read(ref generation)) return;

            nextReader = await nextCapture.CreateFrameReaderAsync(nextCapture.FrameSources[selected.Source.Id], MediaEncodingSubtypes.Bgra8,
                new BitmapSize { Width = 480, Height = 360 });
            if (run != Volatile.Read(ref generation)) return;
            nextReader.FrameArrived += OnFrameArrived;
            capture = nextCapture;
            reader = nextReader;
            var activeReader = nextReader;
            nextCapture = null;
            nextReader = null;
            var result = await activeReader.StartAsync();
            if (run != Volatile.Read(ref generation)) return;
            if (result != MediaFrameReaderStartStatus.Success) throw new InvalidOperationException($"Frame reader status: {result}");
            bool wasInUse = IsInUse;
            previewing = true;
            error = null;
            Changed(nameof(StatusText)); Changed(nameof(EmptyText)); Changed(nameof(HasError)); Changed(nameof(ErrorText)); Changed(nameof(IsPreviewing));
            Changed(nameof(PreviewEnabled)); Changed(nameof(IsInUse));
            if (wasInUse != IsInUse) UsageChanged?.Invoke();
        }
        catch (Exception e)
        {
            if (run == Volatile.Read(ref generation))
            {
                error = e is UnauthorizedAccessException || e.HResult == unchecked((int)0x80070005)
                    ? tr("Windows bloqueó el acceso. Revisa la privacidad de la cámara.")
                    : e is InvalidOperationException && e.Message == "No camera source found."
                        ? tr("No se encontró una cámara disponible.") : tr("No se pudo iniciar la vista previa.");
                privacyDenied = e is UnauthorizedAccessException || e.HResult == unchecked((int)0x80070005);
                AppLog.Write($"Cámara: {e.Message}");
                Changed(nameof(StatusText)); Changed(nameof(EmptyText)); Changed(nameof(HasError)); Changed(nameof(ErrorText)); Changed(nameof(NeedsPrivacyHelp));
                await ReleaseAsync();
            }
        }
        finally
        {
            if (run == Volatile.Read(ref generation)) { starting = false; Changed(nameof(PreviewEnabled)); Changed(nameof(StatusText)); }
            if (!ReferenceEquals(nextCapture, capture)) nextCapture?.Dispose();
            if (!ReferenceEquals(nextReader, reader)) nextReader?.Dispose();
        }
    }

    private async void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        long now = Environment.TickCount64;
        if (now - Interlocked.Read(ref lastFrameAt) < 125 || Interlocked.CompareExchange(ref frameBusy, 1, 0) != 0) return;
        Interlocked.Exchange(ref lastFrameAt, now);
        try
        {
            using var reference = sender.TryAcquireLatestFrame();
            using var bitmap = reference?.VideoMediaFrame?.SoftwareBitmap;
            if (bitmap is null) return;
            var (pixels, width, height) = CopyFramePixels(bitmap);
            await dispatcher.InvokeAsync(() => { if (previewing && !disposed) UpdateFrame(pixels, width, height); });
        }
        catch (Exception e) { AppLog.Write($"Cámara: frame: {e.Message}"); }
        finally { Interlocked.Exchange(ref frameBusy, 0); }
    }

    private (byte[] Pixels, int Width, int Height) CopyFramePixels(SoftwareBitmap bitmap)
    {
        SoftwareBitmap? converted = null;
        try
        {
            var pixelsSource = bitmap;
            if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 || bitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
                pixelsSource = converted = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            int width = pixelsSource.PixelWidth, height = pixelsSource.PixelHeight;
            if (width <= 0 || height <= 0) throw new InvalidOperationException("La cámara entregó un fotograma vacío.");
            int size = checked(width * height * 4);
            lock (frameGate)
            {
                if (frameBuffer?.Capacity != (uint)size)
                {
                    frameBuffer = new Windows.Storage.Streams.Buffer((uint)size) { Length = (uint)size };
                    framePixels = new byte[size];
                }
                var buffer = frameBuffer!;
                var pixels = framePixels!;
                pixelsSource.CopyToBuffer(buffer);
                using var data = DataReader.FromBuffer(buffer);
                data.ReadBytes(pixels);
                return (pixels, width, height);
            }
        }
        finally { converted?.Dispose(); }
    }

    private void UpdateFrame(byte[] pixels, int width, int height)
    {
        if (frameBitmap is null || frameBitmap.PixelWidth != width || frameBitmap.PixelHeight != height)
        {
            frameBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
            Frame = frameBitmap;
        }
        frameBitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, checked(width * 4), 0);
    }

    private async Task ReleaseAsync()
    {
        var oldReader = Interlocked.Exchange(ref reader, null);
        var oldCapture = Interlocked.Exchange(ref capture, null);
        if (oldReader is not null)
        {
            oldReader.FrameArrived -= OnFrameArrived;
            try { await oldReader.StopAsync(); } catch (Exception e) { AppLog.Write($"Cámara: detener lector: {e.Message}"); }
            oldReader.Dispose();
        }
        oldCapture?.Dispose();
    }

    private void SetSystemUsage(bool inUse, CameraApplication[] applications, bool appListAvailable)
    {
        bool wasInUse = IsInUse;
        bool appListChanged = this.appListAvailable != appListAvailable
            || !activeApplications.Select(app => app.Key).SequenceEqual(applications.Select(app => app.Key));
        if (systemInUse == inUse && !appListChanged) return;
        systemInUse = inUse;
        this.appListAvailable = appListAvailable;
        activeApplications = applications;
        Changed(nameof(IsInUse)); Changed(nameof(StatusText));
        Changed(nameof(ActiveApplications)); Changed(nameof(HasActiveApplications)); Changed(nameof(NoActiveApplications)); Changed(nameof(ActiveApplicationsText));
        if (wasInUse != IsInUse || appListChanged) UsageChanged?.Invoke();
    }

    private CameraApplication[] ResolveApplications(IEnumerable<int> processIds)
    {
        var apps = new Dictionary<string, CameraApplication>(StringComparer.OrdinalIgnoreCase);
        foreach (int processId in processIds)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                string processName = process.ProcessName;
                string name = processName.Length == 0 ? processName : char.ToUpperInvariant(processName[0]) + processName[1..];
                string? path = null;
                if (string.Equals(processName, "comet", StringComparison.OrdinalIgnoreCase))
                {
                    try { path = process.MainModule?.FileName; } catch { }
                }
                string key = path ?? name;
                apps.TryAdd(key, new CameraApplication(name, path, OpenCameraPermissions));
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
        return apps.Values.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public void OpenCameraPermissions(CameraApplication app)
    {
        try
        {
            if (app.IsComet && app.ExecutablePath is { Length: > 0 } path)
                Process.Start(new ProcessStartInfo(path, app.PermissionSettingsUri) { UseShellExecute = true });
            else
                Paths.Open(app.PermissionSettingsUri);
        }
        catch (Exception e) { AppLog.Write($"Cámara: abrir permisos: {e.Message}"); }
    }

    private static bool ReadSystemUsage() => ReadInUse(Registry.CurrentUser) || ReadInUse(Registry.LocalMachine);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegNotifyChangeKeyValue(SafeRegistryHandle key, bool watchSubtree, int filter, SafeWaitHandle changed, bool asynchronous);

    private static bool ReadInUse(RegistryKey hive)
    {
        try
        {
            using var key = hive.OpenSubKey(ConsentKey);
            return key is not null && KeyIsInUse(key);
        }
        catch { return false; }
    }

    private static bool KeyIsInUse(RegistryKey key)
    {
        // This Windows privacy history is only a fallback; its timestamps can remain stale.
        try
        {
            long start = key.GetValue("LastUsedTimeStart") is long s ? s : 0;
            long stop = key.GetValue("LastUsedTimeStop") is long e ? e : 0;
            if (CameraUsage.IsActive(start, stop)) return true;
            foreach (var name in key.GetSubKeyNames())
                using (var child = key.OpenSubKey(name))
                    if (child is not null && KeyIsInUse(child)) return true;
        }
        catch { }
        return false;
    }

    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        StopMonitoring();
        StopPreview();
    }
}

public sealed class CameraApplication
{
    public string Name { get; }
    public string? ExecutablePath { get; }
    public string PermissionActionLabel => tr("Administrar");
    public string PermissionSettingsUri => IsComet
        ? "chrome://settings/content/siteDetails?site=https%3A%2F%2Fdiscord.com"
        : "ms-settings:privacy-webcam";
    public string PermissionHelp => IsComet
        ? tr("Abre el permiso de cámara de Discord Web en Comet; allí puedes bloquearlo o volver a permitirlo.")
        : tr("Windows solo ofrece un control global para todas las apps de escritorio; este botón abre ese ajuste.");
    public ICommand OpenPermissions { get; }
    public string Key => ExecutablePath ?? Name;
    public CameraApplication(string name, string? executablePath, Action<CameraApplication> openPermissions)
    {
        Name = name;
        ExecutablePath = executablePath;
        OpenPermissions = new Command(() => openPermissions(this));
    }

    public bool IsComet
    {
        get
        {
            if (ExecutablePath is null) return false;
            try
            {
                string expected = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Perplexity", "Comet", "Application", "comet.exe"));
                return string.Equals(Path.GetFullPath(ExecutablePath), expected, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
