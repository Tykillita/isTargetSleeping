using System.Runtime.InteropServices;

namespace IsTargetSleeping.UI;

/// La ventana de la mascota: una ventana en capas propia, sin WPF, que se pinta con
/// `UpdateLayeredWindow` píxel a píxel (nítida a cualquier escala) y que nunca toma el
/// foco. Windows deja pasar los clics por los píxeles transparentes, así que solo la
/// silueta es clicable; sin `interactive`, deja pasar todos.
public sealed class PetSurface : IDisposable
{
    private const string ClassName = "isTargetSleepingPet";
    private static Win32.WndProc? proc;   // vivo mientras exista la clase
    private static readonly Dictionary<IntPtr, PetSurface> live = [];

    private readonly IntPtr hwnd;
    private IntPtr memDc, dib, bits, oldBitmap;
    private int dibWidth, dibHeight;
    private bool tracking;

    public bool Visible { get; private set; }
    public event Action<int, int>? MouseMoved;   // coordenadas dentro de la ventana
    public event Action? MouseLeft;
    public event Action? Clicked;
    public event Action? RightClicked;

    public PetSurface(bool interactive)
    {
        var instance = Win32.GetModuleHandle(null);
        if (proc is null)
        {
            proc = Dispatch;
            var wc = new Win32.WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<Win32.WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
                hInstance = instance,
                hCursor = Win32.LoadCursor(IntPtr.Zero, new IntPtr(32649)),   // IDC_HAND: se puede tocar
                lpszClassName = ClassName,
            };
            Win32.RegisterClassEx(ref wc);
        }
        uint style = Win32.WS_EX_LAYERED | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOPMOST | (uint)Win32.WS_EX_TOOLWINDOW;
        if (!interactive) style |= Win32.WS_EX_TRANSPARENT;
        hwnd = Win32.CreateWindowEx(style, ClassName, AppInfo.Name, Win32.WS_POPUP, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        live[hwnd] = this;
    }

    public IntPtr Handle => hwnd;

    /// Con la mascota interactiva, su silueta recibe el ratón; si no, todo pasa a lo de debajo.
    public bool Interactive
    {
        set
        {
            long ex = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
            ex = value ? ex & ~(long)Win32.WS_EX_TRANSPARENT : ex | Win32.WS_EX_TRANSPARENT;
            Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, new IntPtr(ex));
        }
    }

    /// Pinta `bgra` (premultiplicado, de arriba abajo) en (x, y) físicos y la muestra.
    public void Render(byte[] bgra, int width, int height, int x, int y)
    {
        if (width <= 0 || height <= 0) return;
        EnsureDib(width, height);
        Marshal.Copy(bgra, 0, bits, Math.Min(bgra.Length, width * height * 4));
        var destination = new Win32.POINT { X = x, Y = y };
        var size = new Win32.SIZE { cx = width, cy = height };
        var origin = new Win32.POINT();
        var blend = new Win32.BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 /* AC_SRC_ALPHA */ };
        Win32.UpdateLayeredWindow(hwnd, IntPtr.Zero, ref destination, ref size, memDc, ref origin, 0, ref blend, Win32.ULW_ALPHA);
        if (!Visible)
        {
            Win32.ShowWindow(hwnd, Win32.SW_SHOWNOACTIVATE);
            Visible = true;
            BringToTop();
        }
    }

    public void Hide()
    {
        if (!Visible) return;
        Win32.ShowWindow(hwnd, Win32.SW_HIDE);
        Visible = false;
    }

    /// La barra de tareas también es «siempre encima»: al tocarla puede quedar delante.
    public void BringToTop()
    {
        if (Visible) Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    private void EnsureDib(int width, int height)
    {
        if (dib != IntPtr.Zero && width == dibWidth && height == dibHeight) return;
        FreeDib();
        var header = new Win32.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height,   // de arriba abajo
            biPlanes = 1,
            biBitCount = 32,
        };
        memDc = Win32.CreateCompatibleDC(IntPtr.Zero);
        dib = Win32.CreateDIBSection(memDc, ref header, 0, out bits, IntPtr.Zero, 0);
        oldBitmap = Win32.SelectObject(memDc, dib);
        (dibWidth, dibHeight) = (width, height);
    }

    private void FreeDib()
    {
        if (memDc != IntPtr.Zero)
        {
            Win32.SelectObject(memDc, oldBitmap);
            Win32.DeleteDC(memDc);
        }
        if (dib != IntPtr.Zero) Win32.DeleteObject(dib);
        memDc = dib = bits = oldBitmap = IntPtr.Zero;
    }

    private static IntPtr Dispatch(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (live.TryGetValue(hwnd, out var surface) && surface.OnMessage(msg, lParam) is { } result) return result;
        return Win32.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private IntPtr? OnMessage(int msg, IntPtr lParam)
    {
        switch (msg)
        {
            case Win32.WM_MOUSEACTIVATE:
                return new IntPtr(Win32.MA_NOACTIVATE);   // un clic no le quita el foco a nadie
            case Win32.WM_MOUSEMOVE:
                if (!tracking)
                {
                    var track = new Win32.TRACKMOUSEEVENT
                    {
                        cbSize = Marshal.SizeOf<Win32.TRACKMOUSEEVENT>(), dwFlags = Win32.TME_LEAVE, hwndTrack = hwnd,
                    };
                    tracking = Win32.TrackMouseEvent(ref track);
                }
                int value = lParam.ToInt32();
                MouseMoved?.Invoke((short)(value & 0xFFFF), (short)(value >> 16 & 0xFFFF));
                return IntPtr.Zero;
            case Win32.WM_MOUSELEAVE:
                tracking = false;
                MouseLeft?.Invoke();
                return IntPtr.Zero;
            case Win32.WM_LBUTTONUP:
                Clicked?.Invoke();
                return IntPtr.Zero;
            case Win32.WM_RBUTTONUP:
                RightClicked?.Invoke();
                return IntPtr.Zero;
        }
        return null;
    }

    public void Dispose()
    {
        live.Remove(hwnd);
        Win32.DestroyWindow(hwnd);
        FreeDib();
    }
}
