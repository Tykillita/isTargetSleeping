using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace IsTargetSleeping.UI;

/// Avisa de cada texto nuevo del portapapeles. Solo existe mientras el usuario tiene el historial activado: una ventana
/// sin interfaz que Windows avisa con WM_CLIPBOARDUPDATE. Respeta lo que los gestores de contraseñas marcan como privado.
public sealed class ClipboardWatcher : IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private static readonly IntPtr HWND_MESSAGE = new(-3);
    private readonly HwndSource source;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    public event Action<string>? TextCopied;

    public ClipboardWatcher()
    {
        source = new HwndSource(new HwndSourceParameters("isTargetSleeping.Clipboard") { ParentWindow = HWND_MESSAGE, Width = 0, Height = 0 });
        source.AddHook(OnMessage);
        AddClipboardFormatListener(source.Handle);
    }

    private IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE) Dispatcher.CurrentDispatcher.BeginInvoke(() => Read(2), DispatcherPriority.Background);
        return IntPtr.Zero;
    }

    /// El portapapeles puede estar ocupado un instante por quien acaba de copiar: se reintenta una vez.
    private void Read(int attempts)
    {
        try
        {
            var data = Clipboard.GetDataObject();
            if (data is null || !data.GetDataPresent(DataFormats.UnicodeText)) return;
            int? canInclude = data.GetData("CanIncludeInClipboardHistory") is MemoryStream s && s.Length >= 4 ? new BinaryReader(s).ReadInt32() : null;
            if (!ClipboardHistory.ShouldRecord(data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing"), canInclude)) return;
            if (data.GetData(DataFormats.UnicodeText) is string text) TextCopied?.Invoke(text);
        }
        catch (Exception e) when (e is COMException or ExternalException)
        {
            if (attempts > 1) Dispatcher.CurrentDispatcher.BeginInvoke(() => Read(attempts - 1), DispatcherPriority.ApplicationIdle);
            else AppLog.Write($"portapapeles: {e.Message}");
        }
    }

    public void Dispose()
    {
        RemoveClipboardFormatListener(source.Handle);
        source.Dispose();
    }
}
