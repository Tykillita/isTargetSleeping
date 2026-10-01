using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using Microsoft.Win32;

namespace IsTargetSleeping.UI;

/// Dónde están la barra de tareas principal, su botón Inicio y su bandeja. El botón se
/// busca con UI Automation una sola vez y después solo se lee su rectángulo (una llamada
/// barata): así se sigue a Inicio cuando se mueve en la barra centrada de Windows 11 sin
/// recorrer el árbol de Explorer cada segundo. Llamar fuera del hilo de la interfaz.
public sealed class TaskbarLocator
{
    public readonly record struct Snapshot(
        Win32.RECT Bar, Win32.RECT Monitor, uint Dpi, Win32.RECT? Start, Win32.RECT? Tray,
        bool Visible, bool StartMissing, bool Suppressed);

    private AutomationElement? start;
    private Win32.RECT lastBar;

    /// Olvida el botón (Explorer reiniciado, otra pantalla): se vuelve a buscar.
    public void Forget() => start = null;

    public Snapshot Locate()
    {
        var taskbar = Win32.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !Win32.IsWindowVisible(taskbar)
            || !Win32.GetWindowRect(taskbar, out var bar) || bar.Width <= 0 || bar.Height <= 0) return default;
        var monitor = Win32.MonitorFromPoint(new Win32.POINT { X = (bar.Left + bar.Right) / 2, Y = (bar.Top + bar.Bottom) / 2 }, 2);
        var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(monitor, ref info)) return default;
        if (Win32.GetDpiForMonitor(monitor, 0, out uint dpi, out _) != 0) dpi = 96;
        bool visible = TaskbarPetLayout.BarVisible(bar, info.rcMonitor, dpi);
        bool suppressed = Suppressed(info.rcMonitor);

        Win32.RECT? tray = null;
        var notify = Win32.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (notify != IntPtr.Zero && Win32.GetWindowRect(notify, out var t) && t.Width > 0) tray = t;

        if (!bar.Equals(lastBar)) start = null;   // la barra se movió o cambió de tamaño
        lastBar = bar;
        var button = StartButton(taskbar, bar);
        bool missing = button is null;
        // Sin UI Automation, con la barra alineada a la izquierda Inicio es la primera casilla.
        if (button is null && AlignedLeft && bar.Width >= bar.Height)
            button = new Win32.RECT { Left = bar.Left, Top = bar.Top, Right = bar.Left + bar.Height, Bottom = bar.Bottom };
        return new(bar, info.rcMonitor, dpi, button, tray, visible, missing && button is null, suppressed);
    }

    private Win32.RECT? StartButton(IntPtr taskbar, Win32.RECT bar)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                // La identidad del botón depende de Explorer; no se adivina su posición.
                start ??= Find(taskbar);
                if (start is null) return null;
                var current = start.Current;   // UI Automation devuelve píxeles físicos
                var rect = current.BoundingRectangle;
                if (current.IsOffscreen || rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0) return null;
                if (rect.Right <= bar.Left || rect.Left >= bar.Right || rect.Bottom <= bar.Top || rect.Top >= bar.Bottom) return null;
                return new Win32.RECT
                {
                    Left = (int)Math.Round(rect.Left), Top = (int)Math.Round(rect.Top),
                    Right = (int)Math.Round(rect.Right), Bottom = (int)Math.Round(rect.Bottom),
                };
            }
            catch
            {
                start = null;   // el elemento ya no existe (Explorer se reinició): se busca otra vez
            }
        }
        return null;
    }

    private static AutomationElement? Find(IntPtr taskbar)
    {
        var root = AutomationElement.FromHandle(taskbar);
        return root?.FindFirst(TreeScope.Descendants,
                   new PropertyCondition(AutomationElement.AutomationIdProperty, "StartButton"))
               ?? root?.FindFirst(TreeScope.Descendants, new AndCondition(
                   new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                   new OrCondition(new PropertyCondition(AutomationElement.NameProperty, "Start"),
                       new PropertyCondition(AutomationElement.NameProperty, "Inicio"))));
    }

    /// Windows 10, o Windows 11 con la barra alineada a la izquierda (`TaskbarAl` = 0).
    private static bool AlignedLeft
    {
        get
        {
            if (Environment.OSVersion.Version.Build < 22000) return true;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                return key?.GetValue("TaskbarAl") is int al && al == 0;
            }
            catch { return false; }
        }
    }

    /// Presentaciones, pantalla completa, sesión bloqueada, Inicio o Búsqueda abiertos, y
    /// juegos o vídeos sin bordes que cubren la pantalla.
    public static bool Suppressed(Win32.RECT monitor)
    {
        if (Win32.SHQueryUserNotificationState(out int state) == 0 && state is 2 or 3 or 4) return true;
        var foreground = Win32.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        Win32.GetWindowThreadProcessId(foreground, out int pid);
        string? name = Procs.Name(pid);
        if (name is "StartMenuExperienceHost" or "SearchHost" or "SearchApp") return true;
        var className = new StringBuilder(128);
        Win32.GetClassName(foreground, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        return pid != Environment.ProcessId && Win32.GetWindowRect(foreground, out var rect)
            && rect.Left <= monitor.Left && rect.Top <= monitor.Top
            && rect.Right >= monitor.Right && rect.Bottom >= monitor.Bottom;
    }

    /// La ventana de primer plano es la barra de tareas (al tocarla puede quedar delante de la mascota).
    public static bool TaskbarInFront()
    {
        var foreground = Win32.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        var className = new StringBuilder(64);
        Win32.GetClassName(foreground, className, className.Capacity);
        return className.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }
}
