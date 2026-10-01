using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace IsTargetSleeping.UI;

/// Ícono en el área de notificación con Shell_NotifyIcon, sin WinForms. Su
/// ventana oculta recibe también el atajo global y los cambios de tema.
public sealed class TrayIcon : IDisposable
{
    private const int Id = 1;
    private const int CallbackMessage = Win32.WM_APP + 1;
    private readonly int taskbarCreated = Win32.RegisterWindowMessage("TaskbarCreated");
    private readonly HwndSource source;
    private IntPtr icon;
    private bool iconOwned;
    private string tip = AppInfo.Name;
    private bool added;

    public event Action? Click;
    public event Action? RightClick;
    public event Action<HotKey>? HotKeyPressed;
    /// Clic en una notificación de la app.
    public event Action? NotificationClicked;
    /// Orden de otra instancia (`show`, `url:…`), por WM_COPYDATA.
    public event Action<string>? CommandReceived;
    /// Modo claro/oscuro, DPI o la barra de tareas cambiaron: hay que repintar.
    public event Action? AppearanceChanged;

    public IntPtr Handle => source.Handle;
    public bool EnsureReady()
    {
        if (!added && icon != IntPtr.Zero) Send(Win32.NIM_ADD);
        return added;
    }

    /// Título de la ventana oculta: la segunda instancia la busca por él.
    public const string WindowName = "isTargetSleepingTray";

    public TrayIcon()
    {
        source = new HwndSource(new HwndSourceParameters(WindowName)
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000),   // WS_POPUP, invisible
            ExtendedWindowStyle = (int)Win32.WS_EX_TOOLWINDOW,
        });
        source.AddHook(WndProc);
    }

    /// Píxeles del ícono pequeño del sistema con el DPI actual (16 a 100 %, 24 a 150 %…).
    public int IconSize
    {
        get
        {
            uint dpi = Win32.GetDpiForWindow(Handle);
            int size = Win32.GetSystemMetricsForDpi(49 /* SM_CXSMICON */, dpi == 0 ? 96 : dpi);
            return size > 0 ? size : 16;
        }
    }

    public void Update(BitmapSource image, string tooltip) => SetIcon(Mark.ToHIcon(image), tooltip, owned: true);

    /// Pone un HICON ya hecho. Con `owned` el ícono pasa a ser suyo y lo destruye al
    /// cambiarlo; sin él (los fotogramas guardados de la animación) no lo toca.
    public void SetIcon(IntPtr hicon, string tooltip, bool owned)
    {
        var (old, oldOwned) = (icon, iconOwned);
        icon = hicon;
        iconOwned = owned;
        tip = tooltip;
        Send(added ? Win32.NIM_MODIFY : Win32.NIM_ADD);
        if (oldOwned && old != IntPtr.Zero && old != hicon) Win32.DestroyIcon(old);
    }

    private void Send(int message)
    {
        var data = Data();
        data.uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP | Win32.NIF_SHOWTIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = icon;
        data.szTip = tip.Length > 127 ? tip[..127] : tip;
        bool ok = Win32.Shell_NotifyIcon(message, ref data);
        if (message == Win32.NIM_ADD && ok)
        {
            added = true;
            data.uVersion = Win32.NOTIFYICON_VERSION_4;
            Win32.Shell_NotifyIcon(Win32.NIM_SETVERSION, ref data);
        }
        else if (message == Win32.NIM_MODIFY && !ok)
        {
            // El ícono se perdió (Explorer aún arrancando): se da de alta de nuevo.
            added = false;
            Send(Win32.NIM_ADD);
        }
    }

    /// Notificación nativa: el globo de la bandeja, que Windows 10/11 muestra como
    /// notificación de la app, con el logo grande.
    public bool Notify(string title, string text)
    {
        if (!added) return false;
        if (balloonIcon == IntPtr.Zero)
        {
            uint dpi = Win32.GetDpiForWindow(Handle);
            int px = Win32.GetSystemMetricsForDpi(11 /* SM_CXICON */, dpi == 0 ? 96 : dpi);
            balloonIcon = Mark.ToHIcon(Mark.AppIcon(px > 0 ? px : 32));
        }
        var data = Data();
        data.uFlags = Win32.NIF_INFO;
        data.szInfoTitle = title.Length > 63 ? title[..63] : title;
        data.szInfo = text.Length > 255 ? text[..255] : text;
        data.dwInfoFlags = Win32.NIIF_USER | Win32.NIIF_LARGE_ICON;
        data.hBalloonIcon = balloonIcon;
        return Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, ref data);
    }

    private IntPtr balloonIcon;

    /// Manda una orden a la instancia que ya corre. Devuelve si la encontró.
    public static bool SendToRunning(string command)
    {
        var hwnd = Win32.FindWindow(null, WindowName);
        if (hwnd == IntPtr.Zero) return false;
        // Le deja traer su panel al frente: esta instancia la lanzó el usuario y puede cederlo.
        Win32.GetWindowThreadProcessId(hwnd, out int pid);
        Win32.AllowSetForegroundWindow(pid);
        var text = Marshal.StringToHGlobalUni(command);
        try
        {
            var data = new Win32.COPYDATASTRUCT { dwData = new IntPtr(0x4953), cbData = (command.Length + 1) * 2, lpData = text };
            return Win32.SendMessageTimeout(hwnd, Win32.WM_COPYDATA, IntPtr.Zero, ref data, 0x2 /* SMTO_ABORTIFHUNG */, 5000, out _) != IntPtr.Zero;
        }
        finally { Marshal.FreeHGlobal(text); }
    }

    private Win32.NOTIFYICONDATA Data() => new()
    {
        cbSize = Marshal.SizeOf<Win32.NOTIFYICONDATA>(),
        hWnd = Handle,
        uID = Id,
        szTip = "",
        szInfo = "",
        szInfoTitle = "",
    };

    /// Dónde está el ícono en pantalla (null si Windows no lo sabe, p. ej. oculto).
    public Win32.RECT? Rect()
    {
        var id = new Win32.NOTIFYICONIDENTIFIER { cbSize = Marshal.SizeOf<Win32.NOTIFYICONIDENTIFIER>(), hWnd = Handle, uID = Id };
        return Win32.Shell_NotifyIconGetRect(ref id, out var rect) == 0 && rect.Width > 0 ? rect : null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            switch (lParam.ToInt32() & 0xFFFF)
            {
                case Win32.NIN_SELECT:
                case Win32.NIN_KEYSELECT:
                    Click?.Invoke();
                    break;
                case Win32.WM_CONTEXTMENU:
                    RightClick?.Invoke();
                    break;
                case Win32.NIN_BALLOONUSERCLICK:
                    NotificationClicked?.Invoke();
                    break;
            }
            handled = true;
        }
        else if (msg == Win32.WM_COPYDATA)
        {
            var data = Marshal.PtrToStructure<Win32.COPYDATASTRUCT>(lParam);
            if (data.dwData == new IntPtr(0x4953) && data.lpData != IntPtr.Zero && data.cbData is > 0 and < 8192)
            {
                var command = Marshal.PtrToStringUni(data.lpData, data.cbData / 2).TrimEnd('\0');
                // Se atiende después: SendMessage espera a que esto vuelva.
                source.Dispatcher.BeginInvoke(() => CommandReceived?.Invoke(command));
            }
            handled = true;
            return new IntPtr(1);
        }
        else if (msg == taskbarCreated)
        {
            // Explorer se reinició: el ícono hay que volver a darlo de alta.
            added = false;
            Send(Win32.NIM_ADD);
        }
        else if (msg == Win32.WM_HOTKEY && new[] { HotKey.Power, HotKey.Sleep, HotKey.Clean }.FirstOrDefault(k => k.Matches(wParam)) is { } key)
        {
            HotKeyPressed?.Invoke(key);
            handled = true;
        }
        else if (msg == Win32.WM_SETTINGCHANGE || msg == Win32.WM_DPICHANGED || msg == 0x007E /* WM_DISPLAYCHANGE */)
        {
            AppearanceChanged?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (added)
        {
            var data = Data();
            Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref data);
            added = false;
        }
        if (iconOwned && icon != IntPtr.Zero) Win32.DestroyIcon(icon);
        icon = IntPtr.Zero;
        if (balloonIcon != IntPtr.Zero) Win32.DestroyIcon(balloonIcon);
        balloonIcon = IntPtr.Zero;
        source.Dispose();
    }
}
