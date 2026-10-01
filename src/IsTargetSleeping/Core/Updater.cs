using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping;

public sealed record UpdateInfo(string Version, string ZipUrl, string ShaUrl, string Page);
public enum UpdateStatus { Idle, Checking, UpToDate, Available, NoRelease, Incompatible, Error, RateLimited }
public sealed record UpdateCheckResult(UpdateStatus Status, UpdateInfo? Info = null, string? Error = null, DateTimeOffset? RetryAt = null);
public enum UpdateStage { Idle, Downloading, Verifying, Applying }
public sealed record UpdateTransfer(UpdateStage Stage, double? Fraction = null);
public sealed record PreparedUpdate(string Exe, string Version, string Sha256, string Directory);

/// GitHub público, sin tokens. Loopback se inyecta solo desde las pruebas.
public static class Updater
{
    public static string Arch => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
    public const string Api = "https://api.github.com";
    private const long MaxZip = 256L * 1024 * 1024, MaxExtracted = 512L * 1024 * 1024;
    private static readonly Regex stable = new(@"^[vV]?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", RegexOptions.CultureInvariant);
    private static readonly Regex semantic = new(@"^[vV]?(0|[1-9]\d*)\.(0|[1-9]\d*)(?:\.(0|[1-9]\d*))?(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$", RegexOptions.CultureInvariant);
    private static readonly HttpClient http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppInfo.Name}/{(AppInfo.Version == "dev" ? "0.0.0" : AppInfo.Version)}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public static bool StableVersion(string version) => stable.IsMatch(version)
        && stable.Match(version).Groups.Cast<Group>().Skip(1).All(g => int.TryParse(g.Value, out _));

    public static int CompareVersions(string a, string b)
    {
        static (int[] Core, string[] Pre) Parse(string value)
        {
            var match = semantic.Match(value.Trim());
            if (!match.Success) throw new ArgumentException("Invalid semantic version.");
            var core = new[] { match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Success ? match.Groups[3].Value : "0" };
            if (core.Any(p => !int.TryParse(p, out _))) throw new ArgumentException("Invalid semantic version.");
            string[] pre = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : [];
            if (pre.Any(p => p.All(char.IsAsciiDigit) && p.Length > 1 && p[0] == '0')) throw new ArgumentException("Invalid prerelease.");
            return (core.Select(int.Parse).ToArray(), pre);
        }
        var (ca, pa) = Parse(a);
        var (cb, pb) = Parse(b);
        for (int i = 0; i < 3; i++) if (ca[i] != cb[i]) return ca[i].CompareTo(cb[i]);
        if (pa.Length == 0 || pb.Length == 0) return pa.Length == pb.Length ? 0 : pa.Length == 0 ? 1 : -1;
        for (int i = 0; i < Math.Min(pa.Length, pb.Length); i++)
        {
            bool na = pa[i].All(char.IsAsciiDigit), nb = pb[i].All(char.IsAsciiDigit);
            int c = na && nb ? pa[i].Length.CompareTo(pb[i].Length) : na != nb ? (na ? -1 : 1) : 0;
            if (c == 0) c = string.CompareOrdinal(pa[i], pb[i]);
            if (c != 0) return c;
        }
        return pa.Length.CompareTo(pb.Length);
    }

    public static bool ValidRepo(string repo) => Regex.IsMatch(repo, @"^[A-Za-z0-9][A-Za-z0-9_.-]*/[A-Za-z0-9][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant);

    private static bool TestOrigin(string? origin, Uri uri) => origin is not null
        && Uri.TryCreate(origin, UriKind.Absolute, out var root) && root.IsLoopback && uri.IsLoopback
        && root.Scheme == uri.Scheme && root.Port == uri.Port && uri.UserInfo.Length == 0;

    private static bool AssetUrl(string url, string repo, string tag, string name, string? testOrigin)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (TestOrigin(testOrigin, uri)) return true;
        return uri.Scheme == "https" && uri.Host == "github.com" && uri.UserInfo.Length == 0 && uri.IsDefaultPort
            && uri.AbsolutePath == $"/{repo}/releases/download/{tag}/{name}" && uri.Query.Length == 0 && uri.Fragment.Length == 0;
    }

    public static UpdateInfo? ParseRelease(string json, string arch, string? repo = null, string? testOrigin = null)
    {
        try
        {
            repo ??= AppInfo.UpdateRepo;
            if (!ValidRepo(repo) || arch is not ("x64" or "arm64") || JsonNode.Parse(json) is not JsonObject release
                || release["draft"]?.GetValue<bool>() == true || release["prerelease"]?.GetValue<bool>() == true) return null;
            var tag = release["tag_name"]?.GetValue<string>();
            if (tag is null || !StableVersion(tag)) return null;
            var version = tag.TrimStart('v', 'V');
            var zipName = $"{AppInfo.Name}-{version}-win-{arch}.zip";
            string? Url(string name)
            {
                var matches = (release["assets"] as JsonArray)?.OfType<JsonObject>()
                    .Where(a => a["name"]?.GetValue<string>() == name).ToArray();
                return matches is { Length: 1 } ? matches[0]["browser_download_url"]?.GetValue<string>() : null;
            }
            if (Url(zipName) is not { } zip || Url(zipName + ".sha256") is not { } sha
                || !AssetUrl(zip, repo, tag, zipName, testOrigin) || !AssetUrl(sha, repo, tag, zipName + ".sha256", testOrigin)) return null;
            var page = release["html_url"]?.GetValue<string>() ?? "";
            if (!Uri.TryCreate(page, UriKind.Absolute, out var pageUri) || (!TestOrigin(testOrigin, pageUri)
                && (pageUri.Scheme != "https" || pageUri.Host != "github.com" || pageUri.UserInfo.Length > 0
                    || !pageUri.IsDefaultPort || pageUri.AbsolutePath != $"/{repo}/releases/tag/{tag}"))) return null;
            return new(version, zip, sha, page);
        }
        catch { return null; }
    }

    public static async Task<UpdateCheckResult> Check(string apiBase, string repo, string current, CancellationToken ct = default)
    {
        if (!ValidRepo(repo) || !Uri.TryCreate(apiBase, UriKind.Absolute, out var origin)
            || !(apiBase.TrimEnd('/') == Api || origin.IsLoopback))
            return new(UpdateStatus.Error, Error: tr("El repositorio de actualizaciones no es válido."));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using var response = await http.GetAsync($"{apiBase.TrimEnd('/')}/repos/{repo}/releases/latest", timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) return new(UpdateStatus.NoRelease);
            if ((int)response.StatusCode == 429 || !response.IsSuccessStatusCode && response.Headers.RetryAfter is not null
                || response.StatusCode == HttpStatusCode.Forbidden
                && response.Headers.TryGetValues("X-RateLimit-Remaining", out var left) && left.FirstOrDefault() == "0")
            {
                var retry = response.Headers.RetryAfter?.Date
                    ?? (response.Headers.RetryAfter?.Delta is { } delta ? DateTimeOffset.UtcNow + delta : (DateTimeOffset?)null);
                if (retry is null && response.Headers.TryGetValues("X-RateLimit-Reset", out var reset)
                    && long.TryParse(reset.FirstOrDefault(), out var seconds)) retry = DateTimeOffset.FromUnixTimeSeconds(seconds);
                return new(UpdateStatus.RateLimited, Error: tr("GitHub pide esperar antes de volver a buscar."), RetryAt: retry ?? DateTimeOffset.UtcNow.AddMinutes(15));
            }
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            var release = JsonNode.Parse(json);
            var tag = release?["tag_name"]?.GetValue<string>();
            if (release?["draft"]?.GetValue<bool>() == true || release?["prerelease"]?.GetValue<bool>() == true)
                return new(UpdateStatus.NoRelease);
            if (tag is null || !StableVersion(tag)) return new(UpdateStatus.Incompatible);
            if (CompareVersions(tag, current) <= 0) return new(UpdateStatus.UpToDate);
            var info = ParseRelease(json, Arch, repo, origin.IsLoopback ? apiBase : null);
            return info is null ? new(UpdateStatus.Incompatible) : new(UpdateStatus.Available, info);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            AppLog.Write($"update check: {e.GetType().Name}");
            return new(UpdateStatus.Error, Error: tr("No se pudo conectar con GitHub. Inténtalo más tarde."));
        }
    }

    public static bool HashMatches(string file, string shaText)
    {
        var expected = shaText.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return expected is { Length: 64 } && expected.All(char.IsAsciiHexDigit)
            && string.Equals(FileHash(file), expected, StringComparison.OrdinalIgnoreCase);
    }

    public static string FileHash(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static async Task<HttpResponseMessage> GetDownload(string url, string? testOrigin, CancellationToken ct)
    {
        var uri = new Uri(url);
        for (int i = 0; i < 6; i++)
        {
            if (!TestOrigin(testOrigin, uri) && (uri.Scheme != "https" || uri.UserInfo.Length > 0 || !uri.IsDefaultPort
                || uri.Host is not ("github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com")))
                throw new InvalidDataException(tr("La dirección de descarga no es válida."));
            var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                response.Dispose();
                continue;
            }
            try { response.EnsureSuccessStatusCode(); return response; }
            catch { response.Dispose(); throw; }
        }
        throw new InvalidDataException(tr("La dirección de descarga no es válida."));
    }

    public static async Task<PreparedUpdate> Download(UpdateInfo info, IProgress<UpdateTransfer>? progress = null,
        CancellationToken ct = default, string? testOrigin = null)
    {
        if (!StableVersion(info.Version)) throw new InvalidDataException(tr("El paquete de actualización no es válido."));
        var dir = Path.Combine(Path.GetTempPath(), $"{AppInfo.Name}-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var zip = Path.Combine(dir, "package.zip");
            progress?.Report(new(UpdateStage.Downloading));
            using (var response = await GetDownload(info.ZipUrl, testOrigin, ct))
            {
                long total = response.Content.Headers.ContentLength ?? 0;
                if (total > MaxZip) throw new InvalidDataException(tr("El paquete de actualización es demasiado grande."));
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = File.Create(zip);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    done += read;
                    if (done > MaxZip) throw new InvalidDataException(tr("El paquete de actualización es demasiado grande."));
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    progress?.Report(new(UpdateStage.Downloading, total > 0 ? Math.Clamp((double)done / total, 0, 1) : null));
                }
            }
            progress?.Report(new(UpdateStage.Verifying));
            using var shaResponse = await GetDownload(info.ShaUrl, testOrigin, ct);
            await using var shaStream = await shaResponse.Content.ReadAsStreamAsync(ct);
            var shaBytes = new byte[4097];
            int shaCount = await shaStream.ReadAtLeastAsync(shaBytes, 4097, throwOnEndOfStream: false, ct);
            if (shaCount > 4096 || !HashMatches(zip, System.Text.Encoding.UTF8.GetString(shaBytes, 0, shaCount)))
                throw new InvalidDataException(tr("La descarga no coincide con su SHA-256. No se instaló."));
            ct.ThrowIfCancellationRequested();
            var extracted = Path.Combine(dir, "files");
            Directory.CreateDirectory(extracted);
            using (var archive = ZipFile.OpenRead(zip))
            {
                long expanded = 0;
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in archive.Entries)
                {
                    string name = entry.FullName;
                    if (name is not ("isTargetSleeping.exe" or "LICENSE") || !names.Add(name)
                        || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000 || entry.Length > MaxExtracted
                        || (expanded += entry.Length) > MaxExtracted)
                        throw new InvalidDataException(tr("El paquete de actualización no es válido."));
                }
                if (!names.Contains($"{AppInfo.Name}.exe")) throw new InvalidDataException(tr("El paquete no trae %@.exe.", AppInfo.Name));
                foreach (var entry in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    await using var input = entry.Open();
                    await using var output = File.Create(Path.Combine(extracted, entry.FullName));
                    var buffer = new byte[81920];
                    long written = 0;
                    int count;
                    while ((count = await input.ReadAsync(buffer, ct)) > 0)
                    {
                        written += count;
                        if (written > entry.Length || written > MaxExtracted)
                            throw new InvalidDataException(tr("El paquete de actualización no es válido."));
                        await output.WriteAsync(buffer.AsMemory(0, count), ct);
                    }
                    if (written != entry.Length) throw new InvalidDataException(tr("El paquete de actualización no es válido."));
                }
            }
            var exe = Path.Combine(extracted, $"{AppInfo.Name}.exe");
            ValidateExecutable(exe, info.Version, Arch);
            ct.ThrowIfCancellationRequested();
            return new(exe, info.Version, FileHash(exe), dir);
        }
        catch { DeleteStaging(dir); throw; }
    }

    public static void ValidateExecutable(string exe, string version, string arch)
    {
        using var stream = File.OpenRead(exe);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 64 || reader.ReadUInt16() != 0x5A4D) throw new InvalidDataException(tr("El ejecutable descargado no es válido."));
        stream.Position = 0x3C;
        int pe = reader.ReadInt32();
        if (pe < 64 || pe > stream.Length - 6) throw new InvalidDataException(tr("El ejecutable descargado no es válido."));
        stream.Position = pe;
        if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != (arch == "arm64" ? 0xAA64 : 0x8664))
            throw new InvalidDataException(tr("El ejecutable descargado no corresponde a este Windows."));
        var actual = FileVersionInfo.GetVersionInfo(exe).ProductVersion?.Split('+')[0];
        if (actual is null || CompareVersions(actual, version) != 0)
            throw new InvalidDataException(tr("La versión del ejecutable no coincide con la publicación."));
    }

    public static bool IsStaging(string dir)
    {
        var full = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
        return string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            && Regex.IsMatch(Path.GetFileName(full), $"^{AppInfo.Name}-update-[0-9a-f]{{32}}$")
            && (!Directory.Exists(full) || (File.GetAttributes(full) & FileAttributes.ReparsePoint) == 0);
    }

    public static void DeleteStaging(string dir)
    {
        if (!IsStaging(dir)) return;
        try { Directory.Delete(dir, recursive: true); } catch { }
    }
}
