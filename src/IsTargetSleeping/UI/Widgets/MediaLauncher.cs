using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace IsTargetSleeping.UI;

public sealed record MediaLauncher(string Name, ICommand Open);

/// <summary>Finds known music apps in Windows' Apps folder and opens them through their registered shell identity.</summary>
internal static class MediaLaunchers
{
    public static IReadOnlyList<MediaLauncher> Find()
    {
        object? shell = null, folder = null, items = null;
        var apps = new Dictionary<string, MediaLauncher>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null) return [];
            shell = Activator.CreateInstance(type);
            if (shell is null) return [];
            dynamic dispatch = shell;
            folder = dispatch.Namespace("shell:AppsFolder");
            if (folder is null) return [];
            dynamic appFolder = folder;
            items = appFolder.Items();
            if (items is null) return [];
            dynamic entries = items;
            int count = entries.Count;
            for (int i = 0; i < count; i++)
            {
                object? item = null;
                try
                {
                    item = entries.Item(i);
                    dynamic entry = item!;
                    string name = entry.Name as string ?? "";
                    string id = entry.Path as string ?? "";
                    if (id.Length == 0 || MediaSource.LauncherName(name) is not { } known || apps.ContainsKey(known)) continue;
                    var launcher = new MediaLauncher(known, new Command(() => Launch(id)));
                    apps.Add(known, launcher);
                }
                finally { Release(item); }
            }
        }
        catch (Exception e) { AppLog.Write($"multimedia: no se pudieron buscar reproductores: {e.Message}"); }
        finally { Release(items); Release(folder); Release(shell); }

        return apps.Values.OrderBy(app => app.Name == "Spotify" ? 0 : 1)
            .ThenBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static void Launch(string id)
    {
        try
        {
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            start.ArgumentList.Add($"shell:AppsFolder\\{id}");
            Process.Start(start);
        }
        catch (Exception e) { AppLog.Write($"multimedia: no se pudo abrir el reproductor: {e.Message}"); }
    }

    private static void Release(object? value)
    {
        try { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        catch { }
    }
}
