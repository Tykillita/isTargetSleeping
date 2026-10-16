using System.IO;

namespace IsTargetSleeping;

/// Una fila del árbol de archivos del widget Código, ya aplanada: el orden es el de pantalla y `Depth` da la sangría.
public sealed record TreeEntry(string Path, string Name, bool IsDirectory, int Depth, bool Expanded);

/// El árbol de archivos del widget Código. Solo lectura y sin seguir enlaces: nunca escribe ni sale de la carpeta elegida.
public static class CodeTree
{
    public const int MaxPerFolder = 400, MaxRows = 1500;

    /// Carpetas que un IDE esconde por defecto: son enormes o generadas y estorban más que ayudan.
    private static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase) { ".git", ".vs", "node_modules", "bin", "obj", "__pycache__" };

    public static readonly IReadOnlySet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico" };

    public static bool IsImage(string path) => ImageExtensions.Contains(System.IO.Path.GetExtension(path));

    /// Las filas visibles de `root` con las carpetas de `expanded` abiertas: carpetas antes que archivos y cada grupo por nombre.
    /// shortcut: una carpeta con más de MaxPerFolder entradas enseña solo las primeras, sin aviso; cuando haga falta, paginar o buscar.
    public static List<TreeEntry> Flatten(string root, ISet<string> expanded)
    {
        var rows = new List<TreeEntry>();
        void Walk(string dir, int depth)
        {
            foreach (var (path, name, isDir) in Children(dir))
            {
                if (rows.Count >= MaxRows) return;
                bool open = isDir && expanded.Contains(path);
                rows.Add(new TreeEntry(path, name, isDir, depth, open));
                if (open) Walk(path, depth + 1);
            }
        }
        Walk(root, 0);
        return rows;
    }

    private static List<(string Path, string Name, bool IsDir)> Children(string dir)
    {
        try
        {
            return new DirectoryInfo(dir).EnumerateFileSystemInfos()
                .Where(e => (e.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                .Where(e => e is not DirectoryInfo d || ((d.Attributes & FileAttributes.ReparsePoint) == 0 && !Hidden.Contains(d.Name)))   // sin enlaces: no hay ciclos ni salidas de la carpeta
                .Select(e => (e.FullName, e.Name, IsDir: e is DirectoryInfo))
                .OrderBy(e => e.IsDir ? 0 : 1).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxPerFolder)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
