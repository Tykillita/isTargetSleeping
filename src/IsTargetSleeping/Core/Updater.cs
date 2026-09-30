using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping;

public sealed record UpdateInfo(string Version, string ZipUrl, string ShaUrl, string Page);

/// Actualizaciones desde las versiones publicadas en GitHub (Releases). Es la
/// única conexión fuera del PC y solo existe si hay un repositorio configurado.
/// Usa los mismos archivos que genera package.ps1:
///   isTargetSleeping-&lt;v&gt;-win-&lt;arch&gt;.zip y su .zip.sha256
/// Instalar: descarga a %TEMP%, comprueba el SHA-256, extrae, renombra el .exe en
/// uso a .old (Windows deja renombrar un .exe abierto), pone el nuevo y relanza.
public static class Updater
{
    public static string Arch => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";

    private static readonly HttpClient http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppInfo.Name}/{(AppInfo.Version == "dev" ? "0.0.0" : AppInfo.Version)}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// Compara versiones semánticas («1.2.0», «v1.10.1», «2.0.0-beta.1»): una
    /// preliminar va antes que la final con el mismo número.
    public static int CompareVersions(string a, string b)
    {
        static (int[] Core, string? Pre) Split(string v)
        {
            v = v.Trim().TrimStart('v', 'V').Split('+')[0];
            int dash = v.IndexOf('-');
            var pre = dash >= 0 ? v[(dash + 1)..] : null;
            var core = (dash >= 0 ? v[..dash] : v).Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToList();
            while (core.Count < 3) core.Add(0);
            return (core.ToArray(), pre);
        }
        var (ca, pa) = Split(a);
        var (cb, pb) = Split(b);
        for (int i = 0; i < Math.Max(ca.Length, cb.Length); i++)
        {
            int x = i < ca.Length ? ca[i] : 0, y = i < cb.Length ? cb[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        if (pa == pb) return 0;
        if (pa is null) return 1;
        if (pb is null) return -1;
        return string.CompareOrdinal(pa, pb);
    }

    /// La respuesta de /releases/latest: la versión y los archivos de esta arquitectura.
    public static UpdateInfo? ParseRelease(string json, string arch)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject release) return null;
            var tag = release["tag_name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(tag)) return null;
            var version = tag.TrimStart('v', 'V');
            var zipName = $"{AppInfo.Name}-{version}-win-{arch}.zip";
            string? Url(string name) => (release["assets"] as JsonArray)?.OfType<JsonObject>()
                .FirstOrDefault(a => string.Equals(a["name"]?.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase))
                ?["browser_download_url"]?.GetValue<string>();
            if (Url(zipName) is not { } zip || Url(zipName + ".sha256") is not { } sha) return null;
            return new UpdateInfo(version, zip, sha, release["html_url"]?.GetValue<string>() ?? "");
        }
        catch { return null; }
    }

    /// Una versión más nueva que la actual, o null (sin conexión, sin versiones o ya al día).
    public static async Task<UpdateInfo?> Check(string apiBase, string repo, string current, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repo)) return null;
        try
        {
            using var resp = await http.GetAsync($"{apiBase.TrimEnd('/')}/repos/{repo.Trim('/')}/releases/latest", ct);
            if (!resp.IsSuccessStatusCode) return null;
            var info = ParseRelease(await resp.Content.ReadAsStringAsync(ct), Arch);
            return info is not null && CompareVersions(info.Version, current) > 0 ? info : null;
        }
        catch { return null; }
    }

    /// El archivo .sha256 lleva «hash  nombre»; se compara el primer campo.
    public static bool HashMatches(string file, string shaText)
    {
        var expected = shaText.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (expected is null || expected.Length != 64) return false;
        using var stream = File.OpenRead(file);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    /// Descarga, comprueba y extrae. Devuelve la ruta del .exe nuevo; lanza si algo falla.
    public static async Task<string> Download(UpdateInfo info, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"{AppInfo.Name}-update", info.Version);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var zip = Path.Combine(dir, $"{AppInfo.Name}-{info.Version}-win-{Arch}.zip");

        using (var resp = await http.GetAsync(info.ZipUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? 0;
            await using var input = await resp.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(zip);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0) progress?.Report((double)done / total);
            }
        }
        var sha = await http.GetStringAsync(info.ShaUrl, ct);
        if (!HashMatches(zip, sha)) throw new InvalidDataException(tr("La descarga no coincide con su SHA-256. No se instaló."));

        var extracted = Path.Combine(dir, "files");
        ZipFile.ExtractToDirectory(zip, extracted);
        var exe = Directory.GetFiles(extracted, $"{AppInfo.Name}.exe", SearchOption.AllDirectories).FirstOrDefault();
        return exe ?? throw new InvalidDataException(tr("El paquete no trae %@.exe.", AppInfo.Name));
    }

    /// Pone `next` en lugar de `current`: el .exe en uso pasa a .old.
    public static void Swap(string current, string next)
    {
        var old = current + ".old";
        try { if (File.Exists(old)) File.Delete(old); }
        catch { old = $"{current}.{DateTime.Now.Ticks}.old"; }
        File.Move(current, old);
        try { File.Copy(next, current); }
        catch
        {
            File.Move(old, current);   // se deja como estaba
            throw;
        }
    }

    /// Al arrancar: borra los .old que dejó una actualización. El .exe viejo puede
    /// seguir bloqueado unos segundos (su proceso acabando, el antivirus): se
    /// reintenta en segundo plano durante medio minuto.
    public static void CleanUpOld()
    {
        if (Environment.ProcessPath is not { } exe || Path.GetDirectoryName(exe) is not { } dir) return;
        bool Sweep()
        {
            bool left = false;
            try
            {
                foreach (var old in Directory.GetFiles(dir, Path.GetFileName(exe) + "*.old"))
                {
                    try { File.Delete(old); } catch { left = true; }
                }
            }
            catch { }
            return left;
        }
        if (!Sweep()) return;
        new Thread(() =>
        {
            for (int i = 0; i < 15; i++)
            {
                Thread.Sleep(2000);
                if (!Sweep()) return;
            }
        }) { IsBackground = true }.Start();
    }

    /// Instala el .exe descargado y lanza la versión nueva, que espera a que esta cierre.
    public static void InstallAndRelaunch(string newExe)
    {
        var current = Environment.ProcessPath ?? throw new InvalidOperationException();
        Swap(current, newExe);
        Process.Start(new ProcessStartInfo(current, $"--wait-pid {Environment.ProcessId}") { UseShellExecute = false })?.Dispose();
    }
}
