using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace IsTargetSleeping.UI;

/// Notifica movimientos globales sin sondear el cursor mientras el notch está cerrado.
internal sealed class NotchMouseMonitor : IDisposable
{
    private const int WhMouseLl = 14, WmMouseMove = 0x0200, WmLButtonDown = 0x0201, WmQuit = 0x0012;
    private readonly Dispatcher dispatcher;
    private readonly Action<Win32.POINT> moved;
    private readonly Action<Win32.POINT>? clicked;
    private readonly HookProc callback;
    private Thread? thread;
    private IntPtr hook;
    private uint threadId;
    private int x, y, sequence, queued, disposed;

    public NotchMouseMonitor(Dispatcher dispatcher, Action<Win32.POINT> moved, Action<Win32.POINT>? clicked = null)
    {
        this.dispatcher = dispatcher;
        this.moved = moved;
        this.clicked = clicked;
        callback = OnMouse;
    }

    public bool Start()
    {
        if (thread is not null) return hook != IntPtr.Zero;
        using var ready = new ManualResetEventSlim();
        var worker = new Thread(() =>
        {
            threadId = GetCurrentThreadId();
            hook = SetWindowsHookEx(WhMouseLl, callback, GetModuleHandle(null), 0);
            if (hook != IntPtr.Zero) PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
            ready.Set();
            if (hook == IntPtr.Zero) return;
            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
            UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }) { IsBackground = true, Name = "isTargetSleeping notch mouse hook" };
        thread = worker;
        worker.Start();
        ready.Wait();
        if (hook != IntPtr.Zero) return true;
        worker.Join(TimeSpan.FromSeconds(1));
        thread = null;
        return false;
    }

    private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && message.ToInt32() == WmMouseMove && Volatile.Read(ref disposed) == 0)
        {
            var point = Marshal.PtrToStructure<MouseHookData>(data).Point;
            Volatile.Write(ref x, point.X);
            Volatile.Write(ref y, point.Y);
            Interlocked.Increment(ref sequence);
            QueueDispatch();
        }
        else if (code >= 0 && message.ToInt32() == WmLButtonDown && clicked is not null && Volatile.Read(ref disposed) == 0)
        {
            var point = Marshal.PtrToStructure<MouseHookData>(data).Point;
            try { dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { try { clicked(point); } catch (Exception e) { AppLog.Write($"clic del notch: {e.Message}"); } })); }
            catch (InvalidOperationException) { }
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    private void QueueDispatch()
    {
        if (Interlocked.Exchange(ref queued, 1) != 0) return;
        try { dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(Dispatch)); }
        catch (InvalidOperationException) { Interlocked.Exchange(ref queued, 0); }
    }

    private void Dispatch()
    {
        int seen = Volatile.Read(ref sequence);
        try
        {
            if (Volatile.Read(ref disposed) == 0) moved(new Win32.POINT { X = Volatile.Read(ref x), Y = Volatile.Read(ref y) });
        }
        catch (Exception e) { AppLog.Write($"ratón del notch: {e.Message}"); }
        finally { Interlocked.Exchange(ref queued, 0); }
        if (seen != Volatile.Read(ref sequence)) QueueDispatch();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        var worker = thread;
        thread = null;
        if (worker is null || hook == IntPtr.Zero) return;
        PostThreadMessage(threadId, WmQuit, UIntPtr.Zero, IntPtr.Zero);
        worker.Join(TimeSpan.FromSeconds(1));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData { public Win32.POINT Point; public uint MouseData, Flags, Time; public UIntPtr ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message { public IntPtr Window; public uint Id; public UIntPtr WParam; public IntPtr LParam; public uint Time; public Win32.POINT Point; public uint Private; }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", EntryPoint = "GetMessageW")] private static extern int GetMessage(out Message message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll", EntryPoint = "PeekMessageW")] private static extern bool PeekMessage(out Message message, IntPtr window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? module);
}
