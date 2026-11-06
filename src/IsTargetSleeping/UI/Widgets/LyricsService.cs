using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IsTargetSleeping.UI;

/// Busca la letra sincronizada de la canción en lrclib.net (por título, artista y duración) y la guarda en disco. Es lo único de la app que manda el título
/// y el artista de lo que suena a internet, y solo cuando el usuario activa las letras. Sin letra sincronizada, devuelve null.
internal sealed class LyricsService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly string CacheDir = Path.Combine(Paths.DataDir, "lyrics");
    private static readonly TimeSpan NegativeFor = TimeSpan.FromMinutes(20);   // «sin letra» caduca pronto: a veces la letra llega después o los metadatos venían a medias

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"isTargetSleeping/{AppInfo.Version} (https://github.com/Tykillita/isTargetSleeping)");
        return http;
    }

    private static string CacheFile(string key, string extension)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(CacheDir, hash + extension);
    }

    public async Task<List<LyricLine>?> GetAsync(string title, string artist, string album, TimeSpan duration, bool ignoreNone, CancellationToken cancel)
    {
        var key = LyricsTimeline.Key(title, artist, (int)Math.Round(duration.TotalSeconds));
        try
        {
            var cached = CacheFile(key, ".lrc");
            if (File.Exists(cached)) return LyricsTimeline.ParseLrc(await File.ReadAllTextAsync(cached, cancel));
            var none = CacheFile(key, ".none");
            if (!ignoreNone && File.Exists(none) && DateTime.UtcNow - File.GetLastWriteTimeUtc(none) < NegativeFor) return null;

            await Task.Delay(150, cancel);   // al cambiar de canción, el título, el artista y la duración no llegan a la vez: se espera a que se asienten
            var synced = await Query(title, artist, album, duration, cancel);
            Directory.CreateDirectory(CacheDir);
            if (synced is { Length: > 0 }) { await File.WriteAllTextAsync(cached, synced, cancel); return LyricsTimeline.ParseLrc(synced); }
            await File.WriteAllTextAsync(none, "", cancel);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { AppLog.Write($"letras: {e.Message}"); throw; }   // un fallo (red, servidor ocupado) no se guarda como «sin letra»: se reintenta
        return null;
    }

    /// Las tres búsquedas a la vez (la exacta y dos de búsqueda): gana la primera que trae letra sincronizada, en vez de probarlas una tras otra (cada una podía tardar
    /// segundos con el servidor lento y la letra salía tarde). Si todas fallan por red o servidor, lanza el error (no es «sin letra»); si responden y ninguna tiene, null.
    private static async Task<string?> Query(string title, string artist, string album, TimeSpan duration, CancellationToken cancel)
    {
        var exact = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}"
            + (album.Length > 0 ? $"&album_name={Uri.EscapeDataString(album)}" : "") + (duration.TotalSeconds > 1 ? $"&duration={(int)Math.Round(duration.TotalSeconds)}" : "");
        using var race = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        var pending = new List<Task<(string? Lyrics, bool Answered)>>
        {
            Fetch(exact, json => Synced(json.RootElement), race.Token, notFoundIsAnswer: true),
            Fetch($"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}", json => Best(json.RootElement, duration), race.Token),
            Fetch($"https://lrclib.net/api/search?q={Uri.EscapeDataString(CleanTitle(title) + " " + FirstArtist(artist))}", json => Best(json.RootElement, duration), race.Token),
        };
        Exception? failure = null;
        bool answered = false;
        while (pending.Count > 0)
        {
            var done = await Task.WhenAny(pending);
            pending.Remove(done);
            try
            {
                var (lyrics, ok) = await done;
                if (lyrics is not null) { race.Cancel(); return lyrics; }
                answered |= ok;
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
            catch (Exception e) { failure = e; }
        }
        if (!answered && failure is not null) throw failure;   // todo falló: no se puede decir que no hay letra
        return null;
    }

    /// Una petición a lrclib: si el servidor falla (5xx) o se corta, un segundo intento enseguida. `Answered` es que el servidor respondió (con o sin resultado).
    private static async Task<(string? Lyrics, bool Answered)> Fetch(string url, Func<JsonDocument, string?> parse, CancellationToken cancel, bool notFoundIsAnswer = false)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var response = await Http.GetAsync(url, cancel);
                if (response.IsSuccessStatusCode)
                {
                    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
                    return (parse(json), true);
                }
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound && notFoundIsAnswer) return (null, true);
                if (attempt == 0 && (int)response.StatusCode >= 500) { await Task.Delay(300, cancel); continue; }
                throw new HttpRequestException($"lrclib respondió {(int)response.StatusCode}");
            }
            catch (Exception e) when (attempt == 0 && e is HttpRequestException or TaskCanceledException && !cancel.IsCancellationRequested) { await Task.Delay(300, cancel); }
        }
    }

    /// De los resultados con letra sincronizada, el de duración más cercana (hasta 10 s de diferencia); sin duración conocida, el primero.
    private static string? Best(JsonElement items, TimeSpan duration)
    {
        if (items.ValueKind != JsonValueKind.Array) return null;
        string? best = null;
        double bestGap = double.MaxValue;
        foreach (var item in items.EnumerateArray())
        {
            if (Synced(item) is not { } synced) continue;
            double length = item.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0;
            double gap = duration.TotalSeconds > 1 && length > 0 ? Math.Abs(length - duration.TotalSeconds) : 5;
            if (gap > 10 || gap >= bestGap) continue;
            (best, bestGap) = (synced, gap);
        }
        return best;
    }

    /// El título sin lo que va entre paréntesis o corchetes ni lo que sigue a « - » («Remastered», «feat. …»).
    internal static string CleanTitle(string title)
    {
        var clean = System.Text.RegularExpressions.Regex.Replace(title, @"\s*[\(\[][^)\]]*[\)\]]", "");
        int dash = clean.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0) clean = clean[..dash];
        return clean.Trim() is { Length: > 0 } text ? text : title.Trim();
    }

    /// El primer artista de «A, B», «A & B» o «A x B».
    internal static string FirstArtist(string artist) => artist.Split([',', '&', ';'], 2)[0].Split(" x ", 2, StringSplitOptions.TrimEntries)[0].Trim() is { Length: > 0 } first ? first : artist.Trim();

    private static string? Synced(JsonElement item) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty("syncedLyrics", out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;
}
