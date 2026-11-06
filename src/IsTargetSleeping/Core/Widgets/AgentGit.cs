namespace IsTargetSleeping;

/// La rama de git de la carpeta de un proyecto, leída del archivo `.git/HEAD` (sin ejecutar git, así que es instantáneo y no puede colgarse).
/// Entiende también los worktrees (donde `.git` es un archivo que apunta a la carpeta real). Con HEAD suelto (un commit) devuelve el hash corto.
public static class AgentGit
{
    public static string? Branch(string? project)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(project) || !Path.IsPathRooted(project)) return null;
            var dot = Path.Combine(project, ".git");
            string? gitDir = Directory.Exists(dot) ? dot : null;
            if (gitDir is null && File.Exists(dot))
            {
                // Un worktree: `gitdir: <ruta>` (absoluta o relativa a la carpeta).
                var line = File.ReadLines(dot).FirstOrDefault(l => l.StartsWith("gitdir:", StringComparison.Ordinal));
                if (line is null) return null;
                var target = line["gitdir:".Length..].Trim();
                gitDir = Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(project, target));
            }
            if (gitDir is null) return null;
            var head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
            const string prefix = "ref: refs/heads/";
            if (head.StartsWith(prefix, StringComparison.Ordinal)) return head[prefix.Length..];
            return head.Length >= 7 && head.All(Uri.IsHexDigit) ? head[..7] : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }
}
