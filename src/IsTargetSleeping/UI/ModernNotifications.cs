using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// Avisos nativos con una imagen de origen; el aviso de bandeja queda como respaldo.
internal sealed class ModernNotifications
{
    private const string AppId = "CodeSentry.isTargetSleeping";
    private const string DownloadTag = "model-pull", DownloadGroup = "downloads";
    private readonly ToastNotifier notifier;
    private readonly string image;
    private string? downloading;
    private bool progressVisible;
    private uint sequence;
    private DateTime lastProgress;

    private ModernNotifications(ToastNotifier notifier, string image) => (this.notifier, this.image) = (notifier, image);

    public static ModernNotifications Create()
    {
        NotificationShortcut.Ensure(AppId);
        using var source = typeof(Logo).Assembly.GetManifestResourceStream("IsTargetSleeping.BrandLogo.png")
            ?? throw new InvalidOperationException("Missing app logo resource.");
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();
        string hash = Convert.ToHexString(SHA256.HashData(bytes))[..16];
        string directory = Path.Combine(Paths.DataDir, "branding");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"notification-logo-{hash}.png");
        if (!File.Exists(path))
        {
            try { using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read); file.Write(bytes); }
            catch (IOException) when (File.Exists(path)) { }
        }
        if (!SHA256.HashData(File.ReadAllBytes(path)).AsSpan().SequenceEqual(SHA256.HashData(bytes)))
            throw new IOException("Notification logo does not match the app resource.");
        return new ModernNotifications(ToastNotificationManager.CreateToastNotifier(AppId), new Uri(path).AbsoluteUri);
    }

    internal static string Payload(string image, string title, string text, string? button, bool progress)
    {
        var binding = new XElement("binding", new XAttribute("template", "ToastGeneric"),
            new XElement("text", title), new XElement("text", text),
            new XElement("image", new XAttribute("placement", "appLogoOverride"), new XAttribute("src", image)));
        if (progress) binding.Add(new XElement("progress", new XAttribute("value", "{value}"),
            new XAttribute("valueStringOverride", "{percent}"), new XAttribute("status", "{status}")));
        var toast = new XElement("toast", new XAttribute("launch", Links.Url("show")), new XAttribute("activationType", "protocol"),
            new XElement("visual", binding));
        if (button is not null) toast.Add(new XElement("actions", new XElement("action", new XAttribute("content", button),
            new XAttribute("activationType", "protocol"), new XAttribute("arguments", Links.Url(progress ? "show" : "activity")))));
        return toast.ToString(SaveOptions.DisableFormatting);
    }

    private ToastNotification NewToast(string title, string text, string? button, bool progress = false, Action? onFailure = null)
    {
        var xml = new XmlDocument();
        xml.LoadXml(Payload(image, title, text, button, progress));
        var toast = new ToastNotification(xml);
        toast.Failed += (_, e) => { AppLog.Write($"notificación de Windows: {e.ErrorCode}"); onFailure?.Invoke(); };
        return toast;
    }

    public void Show(string title, string text, bool details, Action onFailure)
    {
        notifier.Show(NewToast(title, text, details ? tr("Ver detalles") : null, onFailure: onFailure));
    }

    public void Download(PullJob? job)
    {
        if (job is null) { EndDownload(); return; }
        if (downloading != job.Name)
        {
            EndDownload();
            downloading = job.Name;
            sequence = 1;
            var toast = NewToast(tr("Descargando modelo"), job.Name, tr("Abrir"), progress: true);
            toast.Tag = DownloadTag;
            toast.Group = DownloadGroup;
            toast.Data = Progress(job, sequence);
            notifier.Show(toast);
            progressVisible = true;
            lastProgress = DateTime.UtcNow;
        }
        else if (progressVisible && (DateTime.UtcNow - lastProgress).TotalSeconds >= 1)
        {
            progressVisible = notifier.Update(Progress(job, ++sequence), DownloadTag, DownloadGroup) == NotificationUpdateResult.Succeeded;
            lastProgress = DateTime.UtcNow;
        }
    }

    private static NotificationData Progress(PullJob job, uint sequence)
    {
        double? value = job.Fraction is { } f && double.IsFinite(f) ? Math.Clamp(f, 0, 1) : null;
        var data = new NotificationData { SequenceNumber = sequence };
        data.Values["value"] = value?.ToString("0.####", CultureInfo.InvariantCulture) ?? "indeterminate";
        data.Values["percent"] = value is { } v ? tr("%d %% de las capas detectadas", (int)(v * 100)) : "";
        data.Values["status"] = job.Status switch
        {
            "pulling manifest" => tr("Leyendo el manifiesto…"),
            "verifying sha256 digest" => tr("Comprobando…"),
            "writing manifest" or "removing any unused layers" => tr("Terminando…"),
            "success" => tr("Listo"),
            _ => tr("Descargando…"),
        };
        return data;
    }

    public void EndDownload()
    {
        if (downloading is null) return;
        ToastNotificationManager.History.Remove(DownloadTag, DownloadGroup, AppId);
        downloading = null;
        progressVisible = false;
    }
}

/// Identidad para las notificaciones de aplicaciones Win32 sin paquete MSIX.
internal static class NotificationShortcut
{
    private static readonly PropertyKey AppIdKey = new() { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 5 };

    public static void Ensure(string appId)
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Application path unavailable.");
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        if (string.IsNullOrEmpty(programs)) throw new IOException("Start menu unavailable.");
        string primary = Path.Combine(programs, "isTargetSleeping.lnk");
        string portable = Path.Combine(programs, "isTargetSleeping (portable).lnk");
        if (File.Exists(primary) && !Matches(primary, exe, appId, allowOwned: false))
        {
            if (File.Exists(portable) && !Matches(portable, exe, appId, allowOwned: true)) throw new IOException("Notification shortcut already belongs to another application.");
            primary = portable;
        }
        SetIdentity(primary, exe, appId);
    }

    private static bool Matches(string path, string exe, string appId, bool allowOwned)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(path, 0);
            var target = new StringBuilder(32768);
            Marshal.ThrowExceptionForHR(link.GetPath(target, target.Capacity, IntPtr.Zero, 0));
            if (string.Equals(target.ToString(), exe, StringComparison.OrdinalIgnoreCase)) return true;
            if (!allowOwned) return false;
            var key = AppIdKey;
            Marshal.ThrowExceptionForHR(((IPropertyStore)link).GetValue(ref key, out var value));
            try { return value.Type == 31 && Marshal.PtrToStringUni(value.Pointer) == appId; }
            finally { PropVariantClear(ref value); }
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    private static void SetIdentity(string path, string exe, string appId)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            if (File.Exists(path)) ((IPersistFile)link).Load(path, 0);
            Marshal.ThrowExceptionForHR(link.SetPath(exe));
            var store = (IPropertyStore)link;
            var key = AppIdKey;
            var value = new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(appId) };
            try { Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value)); Marshal.ThrowExceptionForHR(store.Commit()); }
            finally { PropVariantClear(ref value); }
            ((IPersistFile)link).Save(path, true);
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] private class ShellLink { }
    [StructLayout(LayoutKind.Sequential, Pack = 4)] private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        [PreserveSig] int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int length, IntPtr data, uint flags);
        [PreserveSig] int GetIDList(out IntPtr id);
        [PreserveSig] int SetIDList(IntPtr id);
        [PreserveSig] int GetDescription(IntPtr text, int length);
        [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        [PreserveSig] int GetWorkingDirectory(IntPtr text, int length);
        [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string path);
        [PreserveSig] int GetArguments(IntPtr text, int length);
        [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        [PreserveSig] int GetHotkey(out ushort key);
        [PreserveSig] int SetHotkey(ushort key);
        [PreserveSig] int GetShowCmd(out int command);
        [PreserveSig] int SetShowCmd(int command);
        [PreserveSig] int GetIconLocation(IntPtr text, int length, out int index);
        [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        [PreserveSig] int Resolve(IntPtr window, uint flags);
        [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
