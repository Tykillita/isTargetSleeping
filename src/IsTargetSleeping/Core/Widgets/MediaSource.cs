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
        ("apple music", "Apple Music"), ("amazon music", "Amazon Music"),
        ("deezer", "Deezer"), ("soundcloud", "SoundCloud"), ("vlc", "VLC"), ("zunemusic", "Media Player"), ("zunevideo", "Films & TV"),
        ("musicbee", "MusicBee"), ("foobar2000", "foobar2000"), ("foobar 2000", "foobar2000"), ("plexamp", "Plexamp"), ("winamp", "Winamp"), ("aimp", "AIMP"),
        ("msedge", "Edge"), ("edge", "Edge"), ("chrome", "Chrome"), ("firefox", "Firefox"), ("brave", "Brave"), ("opera", "Opera"), ("vivaldi", "Vivaldi"),
    ];

    /// App launchers are limited to music apps; generic browsers and video apps are not useful shortcuts here.
    private static readonly (string Key, string Name)[] Launchable =
    [
        ("spotify", "Spotify"), ("apple music", "Apple Music"), ("applemusic", "Apple Music"), ("itunes", "iTunes"),
        ("amazon music", "Amazon Music"), ("amazonmusic", "Amazon Music"), ("youtube music", "YouTube Music"), ("youtubemusic", "YouTube Music"), ("youtube-music", "YouTube Music"),
        ("tidal", "Tidal"), ("deezer", "Deezer"), ("soundcloud", "SoundCloud"), ("vlc", "VLC"),
        ("windows media player", "Media Player"), ("media player", "Media Player"), ("zunemusic", "Media Player"), ("groove music", "Groove Music"),
        ("musicbee", "MusicBee"), ("foobar2000", "foobar2000"), ("foobar 2000", "foobar2000"), ("plexamp", "Plexamp"), ("winamp", "Winamp"), ("aimp", "AIMP"),
    ];

    public static string? LauncherName(string? appName)
    {
        if (string.IsNullOrWhiteSpace(appName)) return null;
        var lower = appName.ToLowerInvariant();
        foreach (var (key, name) in Launchable) if (lower.Contains(key)) return name;
        return null;
    }

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
