using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace IsTargetSleeping;

/// Una forma de un icono: un trazado SVG con su relleno o contorno y su transformación. Los colores son «#RRGGBB».
public sealed record IconShape(string D, string? Fill, double FillAlpha, string? Stroke, double StrokeWidth, double StrokeAlpha, bool EvenOdd, double[]? Matrix);

public sealed record IconDef(double[] ViewBox, IReadOnlyList<IconShape> Shapes);

/// Los iconos de archivo y carpeta del árbol del widget Código: qué icono toca a cada nombre y su dibujo.
/// Los datos salen de Material Icon Theme (MIT) convertido por tools/build-file-icons.py; aquí no hay nada de WPF.
public sealed class FileIconMap
{
    private readonly Dictionary<string, IconDef> icons = [];
    private readonly Dictionary<string, string> extensions = [], files = [], dirs = [], dirsOpen = [];
    private string defaultFile = "file", defaultDir = "folder", defaultOpen = "folder-open";

    public int Count => icons.Count;
    public IEnumerable<string> Keys => icons.Keys;

    public static FileIconMap Load(Stream gzip)
    {
        using var zip = new GZipStream(gzip, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(zip);
        var root = doc.RootElement;
        var map = new FileIconMap
        {
            defaultFile = root.GetProperty("file").GetString()!,
            defaultDir = root.GetProperty("folder").GetString()!,
            defaultOpen = root.GetProperty("open").GetString()!,
        };
        foreach (var icon in root.GetProperty("icons").EnumerateObject())
        {
            var shapes = icon.Value.GetProperty("s").EnumerateArray().Select(s => new IconShape(
                s.GetProperty("d").GetString()!,
                s.TryGetProperty("f", out var f) ? f.GetString() : null,
                s.TryGetProperty("fa", out var fa) ? fa.GetDouble() : 1,
                s.TryGetProperty("s", out var st) ? st.GetString() : null,
                s.TryGetProperty("sw", out var sw) ? sw.GetDouble() : 1,
                s.TryGetProperty("sa", out var sa) ? sa.GetDouble() : 1,
                s.TryGetProperty("r", out _),
                s.TryGetProperty("m", out var m) ? m.EnumerateArray().Select(x => x.GetDouble()).ToArray() : null)).ToList();
            map.icons[icon.Name] = new IconDef(icon.Value.GetProperty("vb").EnumerateArray().Select(x => x.GetDouble()).ToArray(), shapes);
        }
        void Fill(string key, Dictionary<string, string> target)
        {
            foreach (var entry in root.GetProperty(key).EnumerateObject()) target[entry.Name] = entry.Value.GetString()!;
        }
        Fill("ext", map.extensions);
        Fill("files", map.files);
        Fill("dirs", map.dirs);
        Fill("dirsOpen", map.dirsOpen);
        return map;
    }

    public IconDef? Get(string key) => icons.GetValueOrDefault(key);

    /// El nombre del icono de un archivo o carpeta: primero el nombre entero (`package.json`, `src`), luego la extensión más
    /// larga (`spec.ts` antes que `ts`) y, si nada encaja, el icono genérico. Sin distinguir mayúsculas, como VS Code.
    public string Resolve(string name, bool isDirectory, bool expanded)
    {
        var lower = name.ToLowerInvariant();
        if (isDirectory)
        {
            if (expanded) return dirsOpen.GetValueOrDefault(lower) ?? defaultOpen;
            return dirs.GetValueOrDefault(lower) ?? defaultDir;
        }
        if (files.TryGetValue(lower, out var byName)) return byName;
        for (int dot = lower.IndexOf('.'); dot >= 0 && dot < lower.Length - 1; dot = lower.IndexOf('.', dot + 1))
            if (extensions.TryGetValue(lower[(dot + 1)..], out var byExtension)) return byExtension;
        return defaultFile;
    }
}
