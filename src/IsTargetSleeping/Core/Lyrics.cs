using System.Globalization;
using System.Text.RegularExpressions;

namespace IsTargetSleeping;

/// Una línea de una letra sincronizada: desde qué momento de la canción se canta.
public sealed record LyricLine(TimeSpan Time, string Text);

/// Cómo está la letra de la canción que suena.
public enum LyricsStatus { Off, Loading, Ready, None }

public static partial class LyricsTimeline
{
    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]")]
    private static partial Regex Stamp();

    /// El formato LRC («[01:23.45] texto»; una línea puede llevar varias marcas): las líneas con texto, ordenadas por tiempo.
    /// Las marcas de metadatos («[ar:…]», «[ti:…]») y las líneas vacías se ignoran.
    public static List<LyricLine> ParseLrc(string? lrc)
    {
        var lines = new List<LyricLine>();
        foreach (var raw in (lrc ?? "").Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var stamps = Stamp().Matches(line);
            if (stamps.Count == 0) continue;
            var text = Stamp().Replace(line, "").Trim();
            if (text.Length == 0) continue;
            foreach (Match stamp in stamps)
            {
                double fraction = stamp.Groups[3].Success ? double.Parse("0." + stamp.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
                lines.Add(new LyricLine(TimeSpan.FromSeconds(int.Parse(stamp.Groups[1].Value) * 60 + int.Parse(stamp.Groups[2].Value) + fraction), text));
            }
        }
        return [.. lines.OrderBy(l => l.Time)];
    }

    /// El índice de la línea que se canta en `position` (la última cuyo momento ya pasó); -1 antes de la primera.
    public static int CurrentIndex(IReadOnlyList<LyricLine> lines, TimeSpan position)
    {
        int low = 0, high = lines.Count - 1, found = -1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (lines[mid].Time <= position) { found = mid; low = mid + 1; }
            else high = mid - 1;
        }
        return found;
    }

    /// La clave de una canción para la caché y para saber si cambió.
    public static string Key(string title, string artist, int seconds) => $"{title.Trim().ToLowerInvariant()}|{artist.Trim().ToLowerInvariant()}|{seconds}";
}
