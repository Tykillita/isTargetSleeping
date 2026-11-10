namespace IsTargetSleeping;

/// <summary>Local copy of the last track seen in a Windows media session.</summary>
public sealed record MediaTrackSnapshot(string Title, string Artist, string ArtistLine, string SourceApp, string SourceAppId)
{
    public bool Matches(string title, string artist, string artistLine, string sourceApp, string sourceAppId)
    {
        var cachedAlbum = Album(ArtistLine, Artist);
        var currentAlbum = Album(artistLine, artist);
        return Same(Title, title) && Same(Artist, artist)
            && (cachedAlbum.Length == 0 || currentAlbum.Length == 0 || Same(cachedAlbum, currentAlbum))
            && (Same(SourceAppId, sourceAppId) || Same(SourceApp, sourceApp));
    }

    private static string Album(string artistLine, string artist) =>
        !string.IsNullOrEmpty(artist) && artistLine.StartsWith(artist + " - ", StringComparison.OrdinalIgnoreCase) ? artistLine[(artist.Length + 3)..] : "";

    private static bool Same(string a, string b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
