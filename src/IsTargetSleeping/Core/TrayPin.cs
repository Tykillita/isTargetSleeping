using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace IsTargetSleeping;

/// «Mostrar siempre en la barra de tareas»: el mismo interruptor que Windows 11
/// tiene en Configuración › Personalización › Barra de tareas › Otros iconos de la
/// bandeja del sistema. Windows guarda una entrada por ícono en
/// HKCU\Control Panel\NotifyIconSettings\&lt;id&gt; con la ruta del .exe y
/// IsPromoted (1 = en la barra, 0 = en el menú ^). La entrada la crea el Explorador
/// la primera vez que ve el ícono; en Windows 10 no existe y la opción no aparece.
public static class TrayPin
{
    private const string Root = @"Control Panel\NotifyIconSettings";
    private static List<string>? keys;

    /// Las entradas de este .exe (puede haber más de una si cambió el ícono).
    private static List<string> Keys()
    {
        if (keys is { Count: > 0 } known && known.All(Exists)) return known;
        var exe = Environment.ProcessPath;
        var found = new List<string>();
        if (exe is null) return found;
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(Root);
            foreach (var name in root?.GetSubKeyNames() ?? [])
            {
                using var key = root!.OpenSubKey(name);
                if (key?.GetValue("ExecutablePath") is string path && string.Equals(Resolve(path), exe, StringComparison.OrdinalIgnoreCase))
                    found.Add(name);
            }
        }
        catch { }
        return keys = found;
    }

    private static bool Exists(string name)
    {
        try { using var key = Registry.CurrentUser.OpenSubKey($@"{Root}\{name}"); return key is not null; }
        catch { return false; }
    }

    /// Windows abrevia las carpetas conocidas: «{6D809377-…}\isTargetSleeping\x.exe» es Program Files.
    private static string Resolve(string path)
    {
        if (path.Length < 39 || path[0] != '{' || path[37] != '}' || !Guid.TryParse(path[1..37], out var folder)) return path;
        if (SHGetKnownFolderPath(folder, 0, IntPtr.Zero, out var ptr) != 0) return path;
        try { return Marshal.PtrToStringUni(ptr) + path[38..]; }
        finally { Marshal.FreeCoTaskMem(ptr); }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

    /// Windows ya tiene la entrada del ícono (Windows 11): se puede ofrecer el interruptor.
    public static bool Available => Keys().Count > 0;

    public static bool Pinned
    {
        get
        {
            try
            {
                return Keys().Any(name =>
                {
                    using var key = Registry.CurrentUser.OpenSubKey($@"{Root}\{name}");
                    return key?.GetValue("IsPromoted") is int v && v == 1;
                });
            }
            catch { return false; }
        }
    }

    /// El Explorador vigila esta clave: el ícono se mueve al momento.
    public static void SetPinned(bool on)
    {
        foreach (var name in Keys())
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey($@"{Root}\{name}", writable: true);
                key?.SetValue("IsPromoted", on ? 1 : 0, RegistryValueKind.DWord);
            }
            catch { }
        }
    }
}
