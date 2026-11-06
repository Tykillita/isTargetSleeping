using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IsTargetSleeping.UI.Widgets;

internal static class WidgetLogoCatalog
{
    private static readonly IReadOnlyDictionary<string, ImageSource> Logos = WidgetCatalog.All.ToDictionary(
        widget => widget.Id, Load, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, ImageSource> FolderLogos = new[]
    {
        "activity", "bluetooth", "brightness", "camera", "clean", "cpu", "disk", "gpu", "locks", "meeting", "network", "pressure", "processes", "ram",
    }.ToDictionary(id => id, LoadFolderLogo, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, string> FolderAliases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["volume"] = "audio", ["power"] = "ollama", ["state"] = "ollama", ["model"] = "launcher",
        ["vram"] = "gpu", ["free"] = "launcher", ["teamcancel"] = "team",
    };

    public static ImageSource? For(string id)
    {
        if (Logos.TryGetValue(id, out var logo) || FolderLogos.TryGetValue(id, out logo)) return logo;
        return FolderAliases.TryGetValue(id, out var alias)
            ? Logos.GetValueOrDefault(alias) ?? FolderLogos.GetValueOrDefault(alias)
            : null;
    }

    private static ImageSource Load(WidgetInfo widget)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri($"pack://application:,,,/isTargetSleeping;component/Resources/WidgetLogos/{widget.Id}.png");
        image.DecodePixelWidth = 256;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static ImageSource LoadFolderLogo(string id)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri($"pack://application:,,,/isTargetSleeping;component/Resources/FolderLogos/{id}.png");
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
