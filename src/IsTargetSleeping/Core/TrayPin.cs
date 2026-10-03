using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace IsTargetSleeping;

/// «Mostrar siempre en la barra de tareas»: el mismo interruptor que Windows 11
/// tiene en Configuración › Personalización › Barra de tareas › Otros iconos de la
/// bandeja del sistema. Windows guarda una entrada por ícono en
/// HKCU\Control Panel\NotifyIconSettings\&lt;id&gt; con la ruta del .exe y
/// IsPromoted (1 = en la barra, 0 = en el menú ^). La entrada la crea el Explorador
/// la primera vez que ve el ícono; en Windows 10 no existe y la opción no aparece.
/// Se lee el registro en cada acceso, sin caché: el Explorador crea entradas nuevas
/// en cualquier momento (por ejemplo al cambiar el ícono) y son pocas claves.
public static class TrayPin
{
    private const string Root = @"Control Panel\NotifyIconSettings";

    /// Las entradas de este .exe (puede haber más de una si cambió el ícono).
    /// `root` y `exe` son parámetros para poder probarlo con una rama temporal.
    public static List<string> KeysFor(string root, string? exe)
    {
        var found = new List<string>();
        if (exe is null) return found;
        try
        {
            using var rootKey = Registry.CurrentUser.OpenSubKey(root);
            foreach (var name in rootKey?.GetSubKeyNames() ?? [])
            {
                using var key = rootKey!.OpenSubKey(name);
                if (key?.GetValue("ExecutablePath") is string path && string.Equals(Resolve(path), exe, StringComparison.OrdinalIgnoreCase))
                    found.Add(name);
            }
        }
        catch { }
        return found;
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

    private static bool Promoted(string root, string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{root}\{name}");
            return key?.GetValue("IsPromoted") is int v && v == 1;
        }
        catch { return false; }
    }

    /// Windows ya tiene la entrada del ícono (Windows 11): se puede ofrecer el interruptor.
    public static bool Available => KeysFor(Root, Environment.ProcessPath).Count > 0;

    /// Todas las entradas promovidas: solo entonces el ícono está seguro fuera del
    /// menú ^. Basta una entrada nueva sin promover (tras cambiar el ícono, por
    /// ejemplo) para que el interruptor marque apagado hasta reactivarlo.
    public static bool Pinned
    {
        get
        {
            var names = KeysFor(Root, Environment.ProcessPath);
            return names.Count > 0 && names.All(name => Promoted(Root, name));
        }
    }

    public static bool PinnedFor(string root, string? exe)
    {
        var names = KeysFor(root, exe);
        return names.Count > 0 && names.All(name => Promoted(root, name));
    }

    /// El Explorador vigila esta clave: el ícono se mueve al momento.
    public static void SetPinned(bool on) => SetPinnedFor(Root, Environment.ProcessPath, on);

    public static void SetPinnedFor(string root, string? exe, bool on)
    {
        foreach (var name in KeysFor(root, exe))
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey($@"{root}\{name}", writable: true);
                key?.SetValue("IsPromoted", on ? 1 : 0, RegistryValueKind.DWord);
            }
            catch { }
        }
    }
}
