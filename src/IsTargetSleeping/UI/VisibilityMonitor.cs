using System.Text;
using System.Windows.Threading;
using Microsoft.Win32;

namespace IsTargetSleeping.UI;

/// Recoge lo que decide si el notch se enseña (ver `NotchVisibility`) y avisa cuando algo de eso puede haber cambiado. Sin sondeo caliente:
/// eventos del sistema (primer plano; la ventana de primer plano que se mueve, cambia de tamaño o se minimiza, que es lo que pasa con F11 o con un
/// vídeo que entra o sale de pantalla completa sin cambiar de ventana; sesión bloqueada o desconectada; monitores) y un temporizador de seguridad de 2 s
/// que además mira el escritorio seguro (UAC, bloqueo), del que no llega ningún evento.
internal sealed class VisibilityMonitor : IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly Action changed;
    private readonly Win32.WinEventProc callback;
    private readonly DispatcherTimer safety = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly Dictionary<int, string?> names = [];
    private IntPtr hookForeground, hookMoves, lastForeground;
    private volatile bool sessionLocked;

    public VisibilityMonitor(Dispatcher dispatcher, Action changed)
    {
        this.dispatcher = dispatcher;
        this.changed = changed;
        callback = OnEvent;
        safety.Tick += (_, _) => changed();
        debounce.Tick += (_, _) => { debounce.Stop(); changed(); };
    }

    /// Los monitores cambiaron (se conectó o quitó uno, resolución o disposición).
    public event Action? DisplaysChanged;

    public bool SessionLocked => sessionLocked;

    public void Start()
    {
        hookForeground = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, callback, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
        // Mover o cambiar de tamaño una ventana lanza muchos eventos: se filtran en la devolución (solo la de primer plano) y se agrupan.
        hookMoves = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_MINIMIZESTART, Win32.EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, callback, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
        if (hookMoves == IntPtr.Zero) return;
        locationHook = Win32.SetWinEventHook(Win32.EVENT_OBJECT_LOCATIONCHANGE, Win32.EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, callback, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
        safety.Start();
    }

    private IntPtr locationHook;

    public void Dispose()
    {
        safety.Stop();
        debounce.Stop();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
        foreach (var hook in new[] { hookForeground, hookMoves, locationHook })
            if (hook != IntPtr.Zero) Win32.UnhookWinEvent(hook);
        hookForeground = hookMoves = locationHook = IntPtr.Zero;
    }

    private void OnEvent(IntPtr hook, uint eventType, IntPtr window, int idObject, int idChild, uint thread, uint time)
    {
        if (eventType == Win32.EVENT_SYSTEM_FOREGROUND) { lastForeground = window; dispatcher.BeginInvoke(changed); return; }
        // Solo cuenta la ventana de primer plano (idObject 0 = la ventana misma): lo demás es ruido.
        if (idObject != 0 || window == IntPtr.Zero || window != Win32.GetForegroundWindow()) return;
        dispatcher.BeginInvoke(() => { if (!debounce.IsEnabled) debounce.Start(); });
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        sessionLocked = e.Reason switch
        {
            SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect => true,
            SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect or SessionSwitchReason.SessionLogon => false,
            _ => sessionLocked,
        };
        dispatcher.BeginInvoke(changed);
    }

    private void OnDisplaysChanged(object? sender, EventArgs e) => dispatcher.BeginInvoke(() => { DisplaysChanged?.Invoke(); changed(); });

    /// El escritorio de entrada no es el normal (UAC, pantalla de bloqueo): no se puede abrir.
    private static bool SecureDesktop()
    {
        var desktop = Win32.OpenInputDesktop(0, false, 0x0100 /* DESKTOP_SWITCHDESKTOP */);
        if (desktop == IntPtr.Zero) return true;
        Win32.CloseDesktop(desktop);
        return false;
    }

    /// Instantánea para el notch de `monitorHandle` (con su rectángulo `monitor`).
    public VisibilitySnapshot Capture(IntPtr monitorHandle, Win32.RECT monitor, bool hideInFullscreen, IReadOnlyCollection<string> hiddenApps, bool pinned)
    {
        var window = Win32.GetForegroundWindow();
        string? process = null;
        bool self = false, onThisMonitor = false, fullscreen = false;
        if (window != IntPtr.Zero && Win32.GetWindowThreadProcessId(window, out int pid) != 0)
        {
            self = pid == Environment.ProcessId;
            if (!self)
            {
                process = ProcessName(pid);
                onThisMonitor = Win32.MonitorFromWindow(window, 2 /* MONITOR_DEFAULTTONEAREST */) == monitorHandle;
                if (onThisMonitor && Win32.GetWindowRect(window, out var rect))
                {
                    var className = new StringBuilder(128);
                    Win32.GetClassName(window, className, className.Capacity);
                    bool cloaked = Win32.DwmGetWindowAttribute(window, Win32.DWMWA_CLOAKED, out int cloak, sizeof(int)) == 0 && cloak != 0;
                    fullscreen = TaskbarPetLayout.FullscreenApp(new ForegroundWindow(process, className.ToString(), rect,
                        Win32.GetWindowLongPtr(window, Win32.GWL_EXSTYLE).ToInt64(), Win32.IsWindowVisible(window), cloaked, Win32.IsIconic(window)), monitor);
                }
            }
        }
        int notification = Win32.SHQueryUserNotificationState(out int state) == 0 ? state : 0;
        return new VisibilitySnapshot(hideInFullscreen, fullscreen, onThisMonitor, notification, process, self, hiddenApps, pinned, sessionLocked, SecureDesktop());
    }

    /// El nombre del proceso por pid, recordado: abrir un `Process` en cada evento sería caro. Los elevados no se pueden leer: sin nombre, no se ocultan.
    private string? ProcessName(int pid)
    {
        if (names.TryGetValue(pid, out var known)) return known;
        string? name = null;
        try { name = Procs.Name(pid); } catch { }
        if (names.Count > 256) names.Clear();
        return names[pid] = name;
    }
}
