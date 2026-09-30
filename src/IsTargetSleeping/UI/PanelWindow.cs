using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace IsTargetSleeping.UI;

/// El panel que sale de la bandeja, como los flyouts de Windows 11: sin barra
/// de título, con las esquinas y la sombra que pone el propio DWM, y se cierra
/// al hacer clic fuera.
public sealed class PanelWindow : Window
{
    private readonly FrameworkElement view;
    private Win32.RECT? anchor;
    private IntPtr hwnd;

    /// Cuándo se ocultó por perder el foco: si el clic que lo cerró fue en el
    /// propio ícono, no hay que volver a abrirlo.
    public DateTime LastHidden { get; private set; }

    /// Dónde lo dejaste al arrastrarlo por la cabecera (esquina superior izquierda, en
    /// píxeles físicos). null: junto a la bandeja, como un flyout de Windows.
    private Win32.POINT? moved = Defaults.Has("panelX") && Defaults.Has("panelY")
        ? new Win32.POINT { X = Defaults.GetInt("panelX"), Y = Defaults.GetInt("panelY") }
        : null;

    public PanelWindow(FrameworkElement content)
    {
        view = content;
        Content = content;
        Title = AppInfo.Name;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = true;
        UseLayoutRounding = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -10000;
        Top = -10000;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            // -1: el marco de DWM cubre toda la ventana y el acrílico se ve detrás del contenido.
            GlassFrameThickness = Theme.BackdropSupported ? new Thickness(-1) : new Thickness(0),
            ResizeBorderThickness = new Thickness(0),
            UseAeroCaptionButtons = false,
            NonClientFrameEdges = NonClientFrameEdges.None,
        });
        Background = Brushes.Black;   // Glass.Apply lo vuelve transparente si hay acrílico

        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            // Ventana de herramienta: no sale en Alt+Tab ni en la barra de tareas.
            long ex = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
            Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, new IntPtr((ex | Win32.WS_EX_TOOLWINDOW) & ~Win32.WS_EX_APPWINDOW));
            // Sin menú de sistema: con el marco extendido, DWM pintaría su botón de cerrar encima del vidrio.
            long style = Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE).ToInt64();
            Win32.SetWindowLongPtr(hwnd, Win32.GWL_STYLE, new IntPtr(style & ~Win32.WS_SYSMENU));
            Glass.Apply(this, extendFrame: false);
        };
        Deactivated += (_, _) => HidePanel();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) HidePanel(); };
        // Al crecer o encoger (Ajustes, lista de modelos), se reancla sobre la bandeja (o
        // se mantiene donde lo dejaste, dentro de la pantalla) cuando ya tiene su tamaño nuevo.
        SizeChanged += (_, _) => Dispatcher.BeginInvoke(Place, System.Windows.Threading.DispatcherPriority.Background);
    }

    public void ShowAt(Win32.RECT? iconRect)
    {
        anchor = iconRect;
        new WindowInteropHelper(this).EnsureHandle();
        Place();
        Show();
        Activate();
        Place();
        if (hwnd != IntPtr.Zero) Win32.SetForegroundWindow(hwnd);
    }

    public void HidePanel()
    {
        if (!IsVisible) return;
        LastHidden = DateTime.Now;
        Hide();
    }

    // MARK: arrastrar desde la cabecera
    // A mano (captura del ratón y SetWindowPos) en vez de DragMove: el bucle modal de
    // DragMove se come el doble clic y a veces no sigue al ratón hasta el final.

    private (Win32.POINT Cursor, Win32.POINT Window)? drag;
    private bool dragged;

    public void BeginDrag()
    {
        if (hwnd == IntPtr.Zero || !Win32.GetWindowRect(hwnd, out var rect) || !Win32.GetCursorPos(out var cursor)) return;
        drag = (cursor, new Win32.POINT { X = rect.Left, Y = rect.Top });
        dragged = false;
    }

    public void DragTo()
    {
        if (drag is not { } d || !Win32.GetCursorPos(out var cursor)) return;
        int dx = cursor.X - d.Cursor.X, dy = cursor.Y - d.Cursor.Y;
        if (!dragged && Math.Abs(dx) < 4 && Math.Abs(dy) < 4) return;   // un clic no es un arrastre
        dragged = true;
        Win32.SetWindowPos(hwnd, IntPtr.Zero, d.Window.X + dx, d.Window.Y + dy, 0, 0,
            Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    /// Al soltar: el panel se queda ahí (también la próxima vez que se abra).
    public void EndDrag()
    {
        bool wasDragged = dragged;
        drag = null;
        dragged = false;
        if (!wasDragged || !Win32.GetWindowRect(hwnd, out var rect)) return;
        moved = new Win32.POINT { X = rect.Left, Y = rect.Top };
        Place();   // si quedó medio fuera de la pantalla, vuelve dentro
        // Se guarda donde quedó de verdad, ya dentro de la pantalla.
        if (Win32.GetWindowRect(hwnd, out rect)) moved = new Win32.POINT { X = rect.Left, Y = rect.Top };
        Defaults.Set("panelX", moved.Value.X);
        Defaults.Set("panelY", moved.Value.Y);
    }

    /// Doble clic en la cabecera: vuelve junto a la bandeja.
    public void ResetPosition()
    {
        moved = null;
        Defaults.Remove("panelX");
        Defaults.Remove("panelY");
        Place();
    }

    /// Encima del ícono y dentro del área de trabajo del monitor donde está la
    /// barra de tareas, sea cual sea su borde; o donde lo dejaste al arrastrarlo.
    /// Todo en píxeles físicos.
    private void Place()
    {
        if (hwnd == IntPtr.Zero) hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || drag is not null) return;   // mientras lo arrastras, manda el ratón

        Win32.POINT point;
        if (moved is { } m) point = new Win32.POINT { X = m.X + 20, Y = m.Y + 20 };
        else if (anchor is { } r) point = new Win32.POINT { X = r.Left + r.Width / 2, Y = r.Top + r.Height / 2 };
        else Win32.GetCursorPos(out point);

        var monitor = Win32.MonitorFromPoint(point, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(monitor, ref info)) return;
        Win32.GetDpiForMonitor(monitor, 0, out uint dpi, out _);
        double scale = (dpi == 0 ? 96 : dpi) / 96.0;

        // El cuerpo (Ajustes, Actividad, muchos modelos) se desplaza en vez de salirse
        // de la pantalla: como mucho ~75 % del área de trabajo.
        if (view.DataContext is PanelViewModel model)
        {
            double workDip = info.rcWork.Height / scale;
            model.BodyMaxHeight = Math.Max(240, Math.Min(workDip * 0.75, workDip - 150));
        }

        // El tamaño sale del contenido, no de GetWindowRect: al pasar a Ajustes
        // Windows aún no ha redimensionado la ventana cuando llega SizeChanged.
        view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        int w = (int)Math.Ceiling(view.DesiredSize.Width * scale);
        int h = (int)Math.Ceiling(view.DesiredSize.Height * scale);

        var work = info.rcWork;
        var mon = info.rcMonitor;
        int margin = (int)Math.Round(12 * scale);
        int x, y;
        if (moved is { } p)                   // donde lo dejaste, sin salirse de la pantalla
        {
            x = Math.Clamp(p.X, work.Left, Math.Max(work.Left, work.Right - w));
            y = Math.Clamp(p.Y, work.Top, Math.Max(work.Top, work.Bottom - h));
        }
        else if (work.Left > mon.Left)        // barra a la izquierda
        {
            x = work.Left + margin;
            y = Math.Clamp(point.Y - h / 2, work.Top + margin, work.Bottom - h - margin);
        }
        else if (work.Right < mon.Right)      // a la derecha
        {
            x = work.Right - w - margin;
            y = Math.Clamp(point.Y - h / 2, work.Top + margin, work.Bottom - h - margin);
        }
        else if (work.Top > mon.Top)          // arriba
        {
            y = work.Top + margin;
            x = Math.Clamp(point.X - w / 2, work.Left + margin, work.Right - w - margin);
        }
        else                                  // abajo (lo normal en Windows 11)
        {
            y = work.Bottom - h - margin;
            x = Math.Clamp(point.X - w / 2, work.Left + margin, work.Right - w - margin);
        }
        Win32.SetWindowPos(hwnd, IntPtr.Zero, x, Math.Max(work.Top, y), 0, 0,
            Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }
}
