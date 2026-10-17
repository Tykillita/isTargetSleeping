namespace IsTargetSleeping;

/// El nombre legible de la app que suena, a partir del identificador que da Windows («SpotifyAB.SpotifyMusic_zpd…!Spotify», «chrome.exe»,
/// «Chrome._crx_h3nctbl7…» de una app web instalada desde el navegador). Un identificador interno nunca se enseña tal cual. Lógica pura.
public static class MediaSource
{
    // De lo más concreto a lo más general: «youtubemusic» antes que «youtube», y los navegadores al final.
    private static readonly (string Key, string Name)[] Known =
    [
        ("youtubemusic", "YouTube Music"), ("youtube-music", "YouTube Music"), ("ytmusic", "YouTube Music"), ("youtube", "YouTube"),
        ("spotify", "Spotify"), ("applemusic", "Apple Music"), ("itunes", "iTunes"), ("amazonmusic", "Amazon Music"), ("tidal", "Tidal"),
        ("deezer", "Deezer"), ("soundcloud", "SoundCloud"), ("vlc", "VLC"), ("zunemusic", "Media Player"), ("zunevideo", "Films & TV"),
        ("msedge", "Edge"), ("edge", "Edge"), ("chrome", "Chrome"), ("firefox", "Firefox"), ("brave", "Brave"), ("opera", "Opera"), ("vivaldi", "Vivaldi"),
    ];

    public static string Name(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "";
        var lower = id.ToLowerInvariant();
        foreach (var (key, name) in Known) if (lower.Contains(key)) return name;
        var raw = id.Contains('!') ? id[(id.LastIndexOf('!') + 1)..] : id;
        raw = raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? raw[..^4] : raw;
        if (raw.Contains('.')) raw = raw[(raw.LastIndexOf('.') + 1)..];
        // Un hash o un «_crx_…»: no es un nombre. Mejor no decir nada que enseñar letras raras.
        if (raw.Length == 0 || raw.StartsWith('_') || (raw.Length >= 14 && raw.Any(char.IsDigit))) return "";
        return char.ToUpperInvariant(raw[0]) + raw[1..];
    }
}
