using System.IO;
using System.IO.Compression;

namespace IsTargetSleeping;

/// Un archivo o carpeta que espera en la bandeja del notch. Solo guarda la ruta: nunca copia ni mueve nada por sí sola.
public sealed record TrayEntry(string Path, string Name, bool IsDirectory, long Size);

/// Qué hay dentro de una carpeta: archivos, subcarpetas y tamaño total. `Truncated` si se cortó por el tope de elementos.
public readonly record struct FolderStats(int Files, int Folders, long Bytes, bool Truncated);

/// La bandeja de archivos: una lista corta en memoria (se vacía al cerrar la app) con lo que se arrastró al notch.
public sealed class FileTray
{
    public const int Max = 50;
    private readonly List<TrayEntry> entries = [];

    public IReadOnlyList<TrayEntry> Entries => entries;
    public int Count => entries.Count;
    public long TotalBytes => entries.Sum(e => e.Size);

    /// Añade lo que exista, sin repetir y hasta `Max`. Devuelve cuántas entradas nuevas entraron.
    public int Add(IEnumerable<string> paths)
    {
        int added = 0;
        foreach (var raw in paths)
        {
            if (entries.Count >= Max) break;
            string path;
            try { path = System.IO.Path.GetFullPath(raw); } catch { continue; }
            if (entries.Any(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
            if (Directory.Exists(path)) entries.Add(new TrayEntry(path, System.IO.Path.GetFileName(path.TrimEnd('\\', '/')) is { Length: > 0 } dir ? dir : path, true, 0));
            else if (File.Exists(path)) entries.Add(new TrayEntry(path, System.IO.Path.GetFileName(path), false, new FileInfo(path).Length));
            else continue;
            added++;
        }
        return added;
    }

    /// Cuenta lo que hay dentro de `directory` (sin seguir enlaces, y parando en `cap` elementos para no atascarse con una carpeta enorme).
    public static FolderStats Measure(string directory, int cap = 20000)
    {
        int files = 0, folders = 0;
        long bytes = 0;
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.Count > 0)
        {
            try
            {
                foreach (var entry in new DirectoryInfo(pending.Pop()).EnumerateFileSystemInfos())
                {
                    if (files + folders >= cap) return new FolderStats(files, folders, bytes, true);
                    if (entry is DirectoryInfo dir)
                    {
                        if ((dir.Attributes & FileAttributes.ReparsePoint) != 0) continue;   // un enlace no es contenido de la carpeta
                        folders++;
                        pending.Push(dir.FullName);
                    }
                    else if (entry is FileInfo file) { files++; bytes += file.Length; }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // lo que no se puede leer no cuenta
        }
        return new FolderStats(files, folders, bytes, false);
    }

    public void Remove(string path) => entries.RemoveAll(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
    public void Clear() => entries.Clear();

    /// Las entradas que ya no existen en el disco (se borraron o movieron) se quitan.
    public int Prune() => entries.RemoveAll(e => e.IsDirectory ? !Directory.Exists(e.Path) : !File.Exists(e.Path));

    /// Comprime todo en un .zip nuevo dentro de `folder` (nunca sobrescribe: si el nombre existe, añade un número). Devuelve la ruta.
    public string ZipTo(string folder, string baseName)
    {
        Directory.CreateDirectory(folder);
        var target = System.IO.Path.Combine(folder, baseName + ".zip");
        for (int i = 2; File.Exists(target); i++) target = System.IO.Path.Combine(folder, $"{baseName} ({i}).zip");
        try
        {
            using var zip = ZipFile.Open(target, ZipArchiveMode.Create);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                var root = Unique(entry.Name, used);
                if (entry.IsDirectory) AddDirectory(zip, entry.Path, root);
                else zip.CreateEntryFromFile(entry.Path, root, CompressionLevel.Optimal);
            }
        }
        catch
        {
            try { File.Delete(target); } catch { }   // sin un .zip a medias
            throw;
        }
        return target;
    }

    private static void AddDirectory(ZipArchive zip, string directory, string root)
    {
        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToList();
        if (files.Count == 0) zip.CreateEntry(root + "/");
        foreach (var file in files)
            zip.CreateEntryFromFile(file, root + "/" + System.IO.Path.GetRelativePath(directory, file).Replace('\\', '/'), CompressionLevel.Optimal);
    }

    /// «a.txt» y otro «a.txt» de otra carpeta: el segundo pasa a «a (2).txt».
    private static string Unique(string name, HashSet<string> used)
    {
        var candidate = name;
        for (int i = 2; !used.Add(candidate); i++)
            candidate = System.IO.Path.GetFileNameWithoutExtension(name) + $" ({i})" + System.IO.Path.GetExtension(name);
        return candidate;
    }
}
