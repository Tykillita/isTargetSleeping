using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IsTargetSleeping.UI.Widgets;

internal static class WidgetLogoCatalog
{
    private static readonly Dictionary<string, ImageSource?> Logos = new(StringComparer.Ordinal);
    private static readonly HashSet<string> FolderLogos = new(StringComparer.Ordinal)
    {
        "activity", "bluetooth", "brightness", "camera", "clean", "cpu", "disk", "gpu", "locks", "meeting", "network", "pressure", "processes", "ram",
    };

    private static readonly IReadOnlyDictionary<string, string> FolderAliases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["volume"] = "audio", ["power"] = "ollama", ["state"] = "ollama", ["model"] = "launcher", ["notice"] = "activity",
        ["vram"] = "gpu", ["free"] = "launcher", ["teamcancel"] = "team",
    };

    public static ImageSource? For(string id)
    {
        if (Logos.TryGetValue(id, out var logo)) return logo;
        if (FolderAliases.TryGetValue(id, out var alias)) return Logos[id] = For(alias);
        logo = WidgetCatalog.Find(id) is { } widget ? Load(widget) : FolderLogos.Contains(id) ? LoadFolderLogo(id) : null;
        Logos[id] = logo;
        return logo;
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
