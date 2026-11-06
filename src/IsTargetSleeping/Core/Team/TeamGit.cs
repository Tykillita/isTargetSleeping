namespace IsTargetSleeping;

public sealed record FileChange(string Path, int Added, int Removed);

/// Lo que el widget enseña de una rama del equipo sin abrir otra app: qué archivos cambió y el diff. Solo lectura.
public static class TeamGit
{
    private static async Task<(int Exit, string Out)> Run(string dir, CancellationToken ct, params string[] args)
    {
        var r = await TeamProcess.RunAsync(TeamProcess.Find("git") ?? "git", args, "", dir, TimeSpan.FromMinutes(1), ct);
        return (r.Exit, r.Out);
    }

    /// El commit actual de <paramref name="dir"/> (la base desde la que se cuentan los cambios del equipo).
    public static async Task<string> HeadAsync(string dir, CancellationToken ct = default)
    {
        var (exit, output) = await Run(dir, ct, "rev-parse", "HEAD");
        return exit == 0 ? output.Trim() : "";
    }

    /// `git diff --numstat`: «añadidas&lt;TAB&gt;quitadas&lt;TAB&gt;ruta», con «-» en los archivos binarios.
    public static List<FileChange> ParseNumstat(string output)
    {
        var result = new List<FileChange>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.TrimEnd('\r').Split('\t', 3);
            if (parts.Length < 3) continue;
            result.Add(new(parts[2], int.TryParse(parts[0], out var a) ? a : 0, int.TryParse(parts[1], out var r) ? r : 0));
        }
        return result;
    }

    public static async Task<List<FileChange>> ChangesAsync(string dir, string baseSha, CancellationToken ct = default)
    {
        if (baseSha.Length == 0) return [];
        var (exit, output) = await Run(dir, ct, "-c", "core.quotepath=false", "diff", "--numstat", baseSha, "HEAD");
        return exit == 0 ? ParseNumstat(output) : [];
    }

    /// El diff completo (o de un archivo), recortado para que quepa en el widget.
    public static async Task<string> DiffAsync(string dir, string baseSha, string? file = null, int maxChars = 20_000, CancellationToken ct = default)
    {
        if (baseSha.Length == 0) return "";
        var args = new List<string> { "-c", "core.quotepath=false", "diff", "--no-color", "--unified=3", baseSha, "HEAD" };
        if (file is { Length: > 0 }) { args.Add("--"); args.Add(file); }
        var (exit, output) = await Run(dir, ct, [.. args]);
        if (exit != 0) return "";
        return output.Length <= maxChars ? output : output[..maxChars] + "\n…";
    }
}
