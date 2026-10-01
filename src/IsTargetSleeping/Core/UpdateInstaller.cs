using System.Diagnostics;
using System.Text.Json;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping;

public sealed record UpdateSession(string Id, string CurrentExe, string NewExe, int PreviousPid,
    long PreviousStarted, string Version, string Sha256)
{
    public string Backup => CurrentExe + "." + Id + ".old";
    public string Pending => CurrentExe + "." + Id + ".next";
}

/// El mismo EXE, copiado a la carpeta de descarga, actúa como auxiliar sin WPF.
/// La app cierra normalmente; el auxiliar espera, reemplaza y conserva el respaldo
/// hasta que la nueva bandeja confirma que arrancó. Nunca toca ajustes ni modelos.
public static class UpdateInstaller
{
    public static string Start(PreparedUpdate update)
    {
        string current = Environment.ProcessPath ?? throw new InvalidOperationException();
        if (!Updater.IsStaging(update.Directory) || !Updater.HashMatches(update.Exe, update.Sha256))
            throw new InvalidDataException(tr("El paquete de actualización no es válido."));
        Updater.ValidateExecutable(update.Exe, update.Version, Updater.Arch);
        string probe = current + "." + Guid.NewGuid().ToString("N") + ".probe";
        try { using (File.Create(probe)) { } }
        finally { if (File.Exists(probe)) File.Delete(probe); }
        using var own = Process.GetCurrentProcess();
        var session = new UpdateSession(Guid.NewGuid().ToString("N"), Path.GetFullPath(current),
            Path.GetFullPath(update.Exe), own.Id, own.StartTime.ToUniversalTime().Ticks, update.Version, update.Sha256);
        string path = Path.Combine(update.Directory, "session.json");
        Validate(session, path);
        File.WriteAllText(path, JsonSerializer.Serialize(session));
        string worker = Path.Combine(update.Directory, "update-worker.exe");
        File.Copy(current, worker);
        using var process = Launch(worker, "--apply-update", path);
        // Antes de cerrar, comprobar que el auxiliar pudo leer la sesión.
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!File.Exists(path + ".worker-ready"))
        {
            if (process.HasExited || DateTime.UtcNow >= until)
            {
                if (!process.HasExited) { process.Kill(); process.WaitForExit(10_000); }
                throw new IOException(tr("No se pudo iniciar el auxiliar de actualización."));
            }
            Thread.Sleep(50);
        }
        return path;
    }

    private static Process Launch(string exe, string flag, string session)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetDirectoryName(exe)! };
        info.ArgumentList.Add(flag);
        info.ArgumentList.Add(session);
        return Process.Start(info) ?? throw new IOException(tr("No se pudo iniciar el auxiliar de actualización."));
    }

    public static void Validate(UpdateSession session, string path)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        if (!Updater.IsStaging(directory) || Path.GetFileName(path) != "session.json"
            || !Guid.TryParseExact(session.Id, "N", out _) || !Updater.StableVersion(session.Version)
            || session.Sha256.Length != 64 || !session.Sha256.All(char.IsAsciiHexDigit)
            || session.PreviousPid <= 0 || session.PreviousStarted <= 0
            || !Path.IsPathFullyQualified(session.CurrentExe) || Path.GetFileName(session.CurrentExe) != AppInfo.Name + ".exe"
            || !Path.IsPathFullyQualified(session.NewExe)
            || !string.Equals(Path.GetFullPath(session.NewExe), Path.Combine(directory, "files", AppInfo.Name + ".exe"), StringComparison.OrdinalIgnoreCase)
            || string.Equals(session.CurrentExe, session.NewExe, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid update session.");
    }

    private static UpdateSession Read(string path)
    {
        var session = JsonSerializer.Deserialize<UpdateSession>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid update session.");
        Validate(session, path);
        return session;
    }

    private static void Retry(Action action)
    {
        for (int i = 0; ; i++)
        {
            try { action(); return; }
            catch (IOException) when (i < 11) { Thread.Sleep(500); }
        }
    }

    /// Separado del manejo de procesos para probar bloqueos y rollback sin cerrar
    /// ninguna app real. El respaldo solo se borra al confirmar el arranque.
    public static void ReplaceAndConfirm(UpdateSession session, Func<bool> confirm)
    {
        if (!Updater.HashMatches(session.NewExe, session.Sha256)) throw new InvalidDataException("Staged executable changed.");
        bool backedUp = false;
        try
        {
            Retry(() => File.Copy(session.NewExe, session.Pending, overwrite: true));
            if (!Updater.HashMatches(session.Pending, session.Sha256)) throw new InvalidDataException("Copied executable changed.");
            Retry(() => File.Move(session.CurrentExe, session.Backup));
            backedUp = true;
            Retry(() => File.Move(session.Pending, session.CurrentExe));
            if (!confirm()) throw new IOException(tr("La nueva versión no pudo arrancar. Se restauró la anterior."));
            // Un bloqueo del antivirus al borrar el respaldo no invalida un arranque correcto.
            try { Retry(() => File.Delete(session.Backup)); } catch { }
        }
        catch
        {
            if (backedUp)
            {
                Retry(() => { if (File.Exists(session.CurrentExe)) File.Delete(session.CurrentExe); });
                Retry(() => File.Move(session.Backup, session.CurrentExe));
            }
            throw;
        }
        finally { try { File.Delete(session.Pending); } catch { } }
    }

    public static int Run(string path)
    {
        UpdateSession? session = null;
        bool previousClosed = false;
        try
        {
            session = Read(path);
            Updater.ValidateExecutable(session.NewExe, session.Version, Updater.Arch);
            if (!Updater.HashMatches(session.NewExe, session.Sha256)) throw new InvalidDataException("Staged executable changed.");
            File.WriteAllText(path + ".worker-ready", session.Id);
            Process? previous = null;
            try { previous = Process.GetProcessById(session.PreviousPid); } catch (ArgumentException) { }
            using (previous)
            {
                if (previous is not null && previous.StartTime.ToUniversalTime().Ticks == session.PreviousStarted
                    && !previous.WaitForExit(30_000)) throw new IOException(tr("La app no se cerró a tiempo. Inténtalo de nuevo."));
            }
            previousClosed = true;
            AppLog.Write($"update applying {session.Version}");
            ReplaceAndConfirm(session, () =>
            {
                using var next = Launch(session.CurrentExe, "--update-session", path);
                var until = DateTime.UtcNow.AddSeconds(30);
                while (DateTime.UtcNow < until && !next.HasExited)
                {
                    try
                    {
                        if (File.ReadAllText(path + ".ready") == $"{session.Id}:{next.Id}:{session.Version}") return true;
                    }
                    catch (IOException) { }
                    Thread.Sleep(100);
                }
                // Solo el proceso que acaba de lanzar este auxiliar, nunca Ollama.
                if (!next.HasExited) { next.Kill(); next.WaitForExit(10_000); }
                return false;
            });
            try { File.WriteAllText(path + ".completed", session.Id); } catch (IOException) { }
            AppLog.Write($"update ready {session.Version}");
            return 0;
        }
        catch (Exception e)
        {
            AppLog.Write($"update rollback: {e.GetType().Name}: {e.Message}");
            if (session is not null)
            {
                // Si restaurar falla también, conservar el respaldo y explicar dónde está.
                bool restored = !File.Exists(session.Backup);
                string error = restored ? tr("No se pudo actualizar. Se conservó la versión anterior.")
                    : tr("No se pudo restaurar automáticamente. El respaldo está en %@.", session.Backup);
                try { File.WriteAllText(path + ".error", error); } catch { }
                if (previousClosed && restored && File.Exists(session.CurrentExe))
                    try { using var recovery = Launch(session.CurrentExe, "--update-recovered", path); } catch { }
            }
            return 1;
        }
    }

    public static void Confirm(string path)
    {
        var session = Read(path);
        if (!string.Equals(Path.GetFullPath(Environment.ProcessPath!), session.CurrentExe, StringComparison.OrdinalIgnoreCase)
            || session.Version != AppInfo.Version) throw new InvalidDataException("Update confirmation does not match this app.");
        File.WriteAllText(path + ".ready", $"{session.Id}:{Environment.ProcessId}:{AppInfo.Version}");
        _ = Task.Run(async () =>
        {
            for (int i = 0; i < 120; i++)
            {
                await Task.Delay(500);
                if (File.Exists(path + ".completed"))
                {
                    // El archivo del auxiliar puede estar bloqueado unos instantes al salir.
                    string dir = Path.GetDirectoryName(path)!;
                    for (int retry = 0; retry < 12 && Directory.Exists(dir); retry++)
                    { Updater.DeleteStaging(dir); await Task.Delay(500); }
                    return;
                }
            }
        });
    }

    public static string? RecoveryMessage(string path)
    {
        try
        {
            var session = Read(path);
            if (!string.Equals(Environment.ProcessPath, session.CurrentExe, StringComparison.OrdinalIgnoreCase)) return null;
            return File.Exists(path + ".error") ? tr("No se pudo actualizar. Se conservó la versión anterior.") : null;
        }
        catch { return null; }
    }
}
