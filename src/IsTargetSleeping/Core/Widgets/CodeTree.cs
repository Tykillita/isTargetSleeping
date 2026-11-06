using System.IO;

namespace IsTargetSleeping;

/// Una fila del árbol de archivos del widget Código, ya aplanada: el orden es el de pantalla y `Depth` da la sangría.
public sealed record TreeEntry(string Path, string Name, bool IsDirectory, int Depth, bool Expanded);
public sealed record CodeTreeResult(List<TreeEntry> Rows, bool IsPartial);

/// El árbol de archivos del widget Código. Solo lectura y sin seguir enlaces: nunca escribe ni sale de la carpeta elegida.
public static class CodeTree
{
    public const int MaxPerFolder = 400, MaxRows = 1500;

    /// Carpetas que un IDE esconde por defecto: son enormes o generadas y estorban más que ayudan.
    private static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase) { ".git", ".vs", "node_modules", "bin", "obj", "__pycache__" };

    public static readonly IReadOnlySet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico" };

    public static bool IsImage(string path) => ImageExtensions.Contains(System.IO.Path.GetExtension(path));

    /// Las filas visibles de `root` con las carpetas de `expanded` abiertas: carpetas antes que archivos y cada grupo por nombre.
    public static CodeTreeResult Flatten(string root, ISet<string> expanded)
    {
        var rows = new List<TreeEntry>();
        if (!Directory.Exists(root)) return new(rows, true);
        bool partial = false;
        var fullRoot = Path.GetFullPath(root);
        void Walk(string dir, int depth)
        {
            if (!IsSafePath(fullRoot, dir)) { partial = true; return; }
            var children = Children(dir, out bool folderPartial);
            partial |= folderPartial;
            foreach (var (path, name, isDir) in children)
            {
                if (rows.Count >= MaxRows) { partial = true; return; }
                bool open = isDir && expanded.Contains(path);
                rows.Add(new TreeEntry(path, name, isDir, depth, open));
                if (open) Walk(path, depth + 1);
            }
        }
        Walk(fullRoot, 0);
        return new(rows, partial);
    }

    /// Comprueba que una ruta siga dentro de la carpeta elegida y que ningún componente hijo sea un punto de reanálisis.
    /// La carpeta raíz se considera elegida de forma explícita y puede ser un alias; sus descendientes no pueden redirigir fuera.
    public static bool IsSafePath(string root, string path)
    {
        try
        {
            var fullRoot = Path.GetFullPath(root);
            if (!Directory.Exists(fullRoot)) return false;
            var relative = Path.GetRelativePath(fullRoot, Path.GetFullPath(path));
            if (relative == ".") return true;
            if (Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part == "..")) return false;

            var current = fullRoot;
            var parts = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                current = Path.Combine(current, parts[i]);
                if (!File.Exists(current) && !Directory.Exists(current))
                {
                    if (new FileInfo(current).LinkTarget is not null || new DirectoryInfo(current).LinkTarget is not null) return false;
                    return i == parts.Length - 1;
                }
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
            }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static List<(string Path, string Name, bool IsDir)> Children(string dir, out bool partial)
    {
        partial = false;
        try
        {
            var entries = new DirectoryInfo(dir).EnumerateFileSystemInfos()
                .Where(e => (e.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                .Where(e => (e.Attributes & FileAttributes.ReparsePoint) == 0 && (e is not DirectoryInfo d || !Hidden.Contains(d.Name)))   // sin enlaces: no hay ciclos ni salidas de la carpeta
                .Select(e => (e.FullName, e.Name, IsDir: e is DirectoryInfo))
                .OrderBy(e => e.IsDir ? 0 : 1).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            partial = entries.Count > MaxPerFolder;
            return entries.Take(MaxPerFolder).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            partial = true;
            return [];
        }
    }
}
