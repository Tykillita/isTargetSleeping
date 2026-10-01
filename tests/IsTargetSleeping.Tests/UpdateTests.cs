using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IsTargetSleeping;

internal static class UpdateTests
{
    public static async Task Run(Action<string, bool> check)
    {
        check("1.1.0 > 1.0.9", Updater.CompareVersions("1.1.0", "1.0.9") > 0);
        check("v1.10.0 > 1.9.9", Updater.CompareVersions("v1.10.0", "1.9.9") > 0);
        check("1.1 == 1.1.0", Updater.CompareVersions("1.1", "1.1.0") == 0);
        check("beta.2 < beta.10", Updater.CompareVersions("2.0.0-beta.2", "2.0.0-beta.10") < 0);
        check("beta < final", Updater.CompareVersions("2.0.0-beta.1", "2.0.0") < 0);
        check("metadatos no alteran la versión", Updater.CompareVersions("1.0.0+build.1", "1.0.0+build.2") == 0);
        check("rechaza versiones/rutas inválidas", !Updater.StableVersion("../9.9.9") && !Updater.StableVersion("01.2.0") && !Updater.StableVersion("1.0.0-beta"));
        check("repositorio oficial por defecto", AppInfo.UpdateRepo == AppInfo.OfficialUpdateRepo);
        // Inyectar solo en memoria: nunca escribir preferencias del usuario en estas pruebas.
        var defaultsField = typeof(Defaults).GetField("data", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var previousDefaults = defaultsField.GetValue(null);
        try
        {
            defaultsField.SetValue(null, System.Text.Json.Nodes.JsonNode.Parse("{}"));
            check("búsqueda activada sin una preferencia guardada", Defaults.GetBool("updateCheck", true));
            defaultsField.SetValue(null, System.Text.Json.Nodes.JsonNode.Parse("{\"updateCheck\":false,\"updateRepo\":\"custom/repo\"}"));
            check("conserva búsqueda desactivada y repositorio personalizado", !Defaults.GetBool("updateCheck", true) && AppInfo.UpdateRepo == "custom/repo");
        }
        finally { defaultsField.SetValue(null, previousDefaults); }

        byte[] executable = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "probe", "UpdateProbe.exe"));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        string api = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        string zipName = $"isTargetSleeping-1.0.0-win-{Updater.Arch}.zip";
        byte[] package = Zip(("isTargetSleeping.exe", executable), ("LICENSE", "MIT"u8.ToArray()));
        string sha = Hash(package) + "  " + zipName;
        string release = Release(api, Updater.Arch);
        int releaseCode = 200;
        string extraHeaders = "";
        var server = Task.Run(async () =>
        {
            while (true)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(); } catch { return; }
                using (client)
                {
                    try
                    {
                        var stream = client.GetStream();
                        var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                        var request = await reader.ReadLineAsync() ?? "";
                        while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                        string path = request.Split(' ').ElementAtOrDefault(1) ?? "";
                        byte[] body; int code;
                        if (path == "/repos/demo/its/releases/latest") { body = Encoding.UTF8.GetBytes(release); code = releaseCode; }
                        else if (path == "/dl/" + zipName) { body = package; code = 200; }
                        else if (path == "/dl/" + zipName + ".sha256") { body = Encoding.UTF8.GetBytes(sha); code = 200; }
                        else { body = []; code = 404; }
                        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {code} X\r\nContent-Length: {body.Length}\r\nConnection: close\r\n{extraHeaders}\r\n"));
                        await stream.WriteAsync(body);
                    }
                    catch (IOException) { }
                }
            }
        });
        PreparedUpdate? downloaded = null;
        try
        {
            var info = Updater.ParseRelease(release, Updater.Arch, "demo/its", api);
            check("elige ZIP y SHA de la arquitectura", info is { Version: "1.0.0" });
            check("sin SHA no se ofrece", Updater.ParseRelease(release.Replace(".sha256\"", ".txt\""), Updater.Arch, "demo/its", api) is null);
            check("ARM64 tiene su propio paquete", Updater.ParseRelease(Release(api, "arm64"), "arm64", "demo/its", api) is not null);
            check("no elige un ZIP de otra arquitectura", Updater.ParseRelease(Release(api, "arm64"), "x64", "demo/its", api) is null);
            check("ignora borradores", Updater.ParseRelease(Release(api, Updater.Arch, draft: true), Updater.Arch, "demo/its", api) is null);
            check("ignora versiones preliminares", Updater.ParseRelease(Release(api, Updater.Arch, prerelease: true), Updater.Arch, "demo/its", api) is null);
            check("loopback requiere inyección explícita", Updater.ParseRelease(release, Updater.Arch, "demo/its") is null);
            check("acepta solo descargas del repositorio", Updater.ParseRelease(Release("https://github.com", Updater.Arch, official: true), Updater.Arch, "demo/its") is not null);
            check("rechaza descargas ajenas", Updater.ParseRelease(Release("https://evil.example", Updater.Arch, official: true), Updater.Arch, "demo/its") is null);
            var found = await Updater.Check(api, "demo/its", "0.9.0");
            check("detecta actualización disponible", found.Status == UpdateStatus.Available && found.Info is not null);
            check("misma versión: al día", (await Updater.Check(api, "demo/its", "1.0.0")).Status == UpdateStatus.UpToDate);
            check("versión anterior: al día", (await Updater.Check(api, "demo/its", "2.0.0")).Status == UpdateStatus.UpToDate);
            check("sin releases: estado distinto", (await Updater.Check(api, "other/repo", "0.9.0")).Status == UpdateStatus.NoRelease);
            release = Release(api, "arm64");
            if (Updater.Arch == "x64") check("paquete incompatible: estado distinto", (await Updater.Check(api, "demo/its", "0.9.0")).Status == UpdateStatus.Incompatible);
            release = Release(api, Updater.Arch);
            release = release.Replace("v1.0.0", "v1.0.0-beta.1");
            check("consulta rechaza etiquetas preliminares", (await Updater.Check(api, "demo/its", "0.9.0")).Status == UpdateStatus.Incompatible);
            release = Release(api, Updater.Arch, draft: true);
            check("consulta nunca ofrece un borrador", (await Updater.Check(api, "demo/its", "0.9.0")).Status == UpdateStatus.NoRelease);
            release = Release(api, Updater.Arch);
            releaseCode = 429; extraHeaders = "Retry-After: 120\r\n";
            var rate = await Updater.Check(api, "demo/its", "0.9.0");
            check("respeta espera de GitHub", rate.Status == UpdateStatus.RateLimited && rate.RetryAt > DateTimeOffset.UtcNow.AddSeconds(110));
            releaseCode = 403; extraHeaders = "X-RateLimit-Remaining: 0\r\nX-RateLimit-Reset: " + DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds() + "\r\n";
            check("respeta límite de API y fecha de reinicio", (await Updater.Check(api, "demo/its", "0.9.0")).RetryAt > DateTimeOffset.UtcNow.AddMinutes(4));
            releaseCode = 500; extraHeaders = "";
            check("error de servidor no significa al día", (await Updater.Check(api, "demo/its", "0.9.0")).Status == UpdateStatus.Error);
            releaseCode = 200;
            downloaded = await Updater.Download(found.Info!, testOrigin: api);
            check("descarga y valida un EXE real", File.ReadAllBytes(downloaded.Exe).SequenceEqual(executable));
            check("conserva el hash del EXE preparado", downloaded.Sha256 == Hash(executable));
            using var midDownload = new CancellationTokenSource();
            bool midCancelled = false;
            try
            {
                await Updater.Download(found.Info!, new TransferProgress(p =>
                { if (p.Stage == UpdateStage.Downloading && p.Fraction > 0) midDownload.Cancel(); }), midDownload.Token, api);
            }
            catch (OperationCanceledException) { midCancelled = true; }
            check("cancela después de descargar bytes", midCancelled);
            bool wrongVersion = false, wrongArch = false;
            try { Updater.ValidateExecutable(downloaded.Exe, "9.0.0", Updater.Arch); } catch (InvalidDataException) { wrongVersion = true; }
            try { Updater.ValidateExecutable(downloaded.Exe, "1.0.0", Updater.Arch == "x64" ? "arm64" : "x64"); } catch (InvalidDataException) { wrongArch = true; }
            check("rechaza versión y arquitectura del EXE incorrectas", wrongVersion && wrongArch);
            sha = new string('0', 64);
            check("rechaza SHA distinto", await Reject(() => Updater.Download(found.Info!, testOrigin: api)));
            package = Zip(("../isTargetSleeping.exe", executable)); sha = Hash(package);
            check("rechaza rutas fuera del ZIP", await Reject(() => Updater.Download(found.Info!, testOrigin: api)));
            package = Zip(("isTargetSleeping.exe", executable), ("isTargetSleeping.exe", executable)); sha = Hash(package);
            check("rechaza EXE duplicado", await Reject(() => Updater.Download(found.Info!, testOrigin: api)));
            package = Zip(("isTargetSleeping.exe", "MZ fake"u8.ToArray())); sha = Hash(package);
            check("rechaza EXE inválido aun con SHA correcto", await Reject(() => Updater.Download(found.Info!, testOrigin: api)));
            package = "not a zip"u8.ToArray(); sha = Hash(package);
            check("rechaza ZIP corrupto", await Reject(() => Updater.Download(found.Info!, testOrigin: api)));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            bool cancel = false;
            try { await Updater.Download(found.Info!, ct: cancelled.Token, testOrigin: api); } catch (OperationCanceledException) { cancel = true; }
            check("cancelar no instala nada", cancel);
            listener.Stop();
            check("sin conexión no significa al día", (await Updater.Check(api, "demo/its", "0.9.0")).Status == UpdateStatus.Error);
        }
        finally { listener.Stop(); await server; if (downloaded is not null) Updater.DeleteStaging(downloaded.Directory); }

        // Carpetas propias, con un auxiliar real de prueba que nunca usa la bandeja.
        string target = Directory.CreateTempSubdirectory("its-update-target-").FullName;
        string staging = Path.Combine(Path.GetTempPath(), $"isTargetSleeping-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(staging, "files"));
        string current = Path.Combine(target, "isTargetSleeping.exe"), next = Path.Combine(staging, "files", "isTargetSleeping.exe");
        File.WriteAllBytes(next, executable);
        File.WriteAllText(Path.Combine(target, "settings.json"), "preserve settings");
        File.WriteAllText(Path.Combine(target, "stats.json"), "preserve history");
        using var previous = Process.GetCurrentProcess();
        var session = new UpdateSession(Guid.NewGuid().ToString("N"), current, next, previous.Id,
            previous.StartTime.ToUniversalTime().Ticks, "1.0.0", Hash(executable));
        string sessionPath = Path.Combine(staging, "session.json");
        try
        {
            UpdateInstaller.Validate(session, sessionPath);
            File.WriteAllText(current, "previous");
            bool kept = false;
            UpdateInstaller.ReplaceAndConfirm(session, () => { kept = File.ReadAllText(session.Backup) == "previous"; return true; });
            check("respaldo dura hasta confirmar arranque", kept && !File.Exists(session.Backup));
            check("instala el ejecutable completo", File.ReadAllBytes(current).SequenceEqual(executable));
            File.WriteAllText(current, "previous");
            bool rolledBack = false;
            try { UpdateInstaller.ReplaceAndConfirm(session, () => false); } catch (IOException) { rolledBack = true; }
            check("fallo de arranque restaura la anterior", rolledBack && File.ReadAllText(current) == "previous");
            bool denied = false;
            try { UpdateInstaller.ReplaceAndConfirm(session, () => throw new UnauthorizedAccessException()); } catch (UnauthorizedAccessException) { denied = true; }
            check("error de permisos conserva la anterior", denied && File.ReadAllText(current) == "previous");
            using (var locked = File.Open(current, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var unlock = Task.Run(async () => { await Task.Delay(200); locked.Dispose(); });
                UpdateInstaller.ReplaceAndConfirm(session, () => true);
                await unlock;
            }
            check("reintenta un archivo bloqueado", File.ReadAllBytes(current).SequenceEqual(executable));
            check("conserva ajustes e historial", File.ReadAllText(Path.Combine(target, "settings.json")) == "preserve settings"
                && File.ReadAllText(Path.Combine(target, "stats.json")) == "preserve history");
            bool unsafeSession = false;
            try { UpdateInstaller.Validate(session with { NewExe = Path.Combine(target, "outside.exe") }, sessionPath); } catch (InvalidDataException) { unsafeSession = true; }
            check("rechaza sesiones fuera de la descarga", unsafeSession);
            // PID propio ya terminado: el auxiliar no espera ni cierra procesos ajenos.
            var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "probe", "UpdateProbe.exe")) { UseShellExecute = false, CreateNoWindow = true };
            using var exited = Process.Start(info)!;
            long started = exited.StartTime.ToUniversalTime().Ticks;
            exited.WaitForExit();
            session = session with { PreviousPid = exited.Id, PreviousStarted = started };
            File.WriteAllText(sessionPath, JsonSerializer.Serialize(session));
            check("auxiliar real espera y confirma la nueva instancia", UpdateInstaller.Run(sessionPath) == 0 && File.Exists(sessionPath + ".completed"));
            await Task.Delay(1200);
            File.Delete(sessionPath + ".ready"); File.Delete(sessionPath + ".completed");
            File.WriteAllText(Path.Combine(target, "fail-update"), "fail");
            check("auxiliar real recupera un arranque fallido", UpdateInstaller.Run(sessionPath) == 1 && File.ReadAllBytes(current).SequenceEqual(executable));
            for (int i = 0; i < 30 && !File.Exists(sessionPath + ".recovered"); i++) await Task.Delay(100);
            check("vuelve a abrir la versión restaurada", File.Exists(sessionPath + ".recovered"));
        }
        finally
        {
            Updater.DeleteStaging(staging);
            // target procede de CreateTempSubdirectory: no contiene datos del usuario.
            if (Path.GetDirectoryName(target) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)
                && Path.GetFileName(target).StartsWith("its-update-target-")) Directory.Delete(target, recursive: true);
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private sealed class TransferProgress(Action<UpdateTransfer> report) : IProgress<UpdateTransfer>
    { public void Report(UpdateTransfer value) => report(value); }
    private static byte[] Zip(params (string Name, byte[] Bytes)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var entry in entries) using (var output = zip.CreateEntry(entry.Name).Open()) output.Write(entry.Bytes);
        return stream.ToArray();
    }
    private static string Release(string api, string arch, bool draft = false, bool prerelease = false, bool official = false)
    {
        string name = $"isTargetSleeping-1.0.0-win-{arch}.zip";
        string downloads = official ? api + "/demo/its/releases/download/v1.0.0/" : api + "/dl/";
        return JsonSerializer.Serialize(new { tag_name = "v1.0.0", draft, prerelease,
            html_url = official ? api + "/demo/its/releases/tag/v1.0.0" : api + "/release",
            assets = new[] { new { name, browser_download_url = downloads + name }, new { name = name + ".sha256", browser_download_url = downloads + name + ".sha256" } } });
    }
    private static async Task<bool> Reject(Func<Task<PreparedUpdate>> action)
    {
        try { var prepared = await action(); Updater.DeleteStaging(prepared.Directory); return false; }
        catch (InvalidDataException) { return true; }
    }
}
