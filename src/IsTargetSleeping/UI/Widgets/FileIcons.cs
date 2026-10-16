using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace IsTargetSleeping.UI;

/// Dibuja los iconos de archivo y carpeta del árbol del widget Código (Material Icon Theme, MIT: ver THIRD-PARTY-NOTICES.md).
/// Cada icono se construye una vez a partir de sus trazados SVG y se reutiliza ya congelado.
public static class FileIcons
{
    private static readonly Lazy<FileIconMap?> map = new(Load);
    private static readonly Dictionary<string, ImageSource?> cache = [];

    private static FileIconMap? Load()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("IsTargetSleeping.FileIcons.json.gz");
            return stream is null ? null : FileIconMap.Load(stream);
        }
        catch (Exception e)
        {
            AppLog.Write($"iconos de archivo: {e.Message}");
            return null;
        }
    }

    public static FileIconMap? Map => map.Value;

    /// null si los datos no están (el árbol enseña entonces un glifo genérico).
    public static ImageSource? For(string name, bool isDirectory, bool expanded)
    {
        if (Map is not { } icons) return null;
        var key = icons.Resolve(name, isDirectory, expanded);
        if (cache.TryGetValue(key, out var hit)) return hit;
        return cache[key] = icons.Get(key) is { } def ? Build(def) : null;
    }

    /// Cuántas formas de un icono no se pudieron leer (la prueba exige 0 en todos).
    public static int BrokenShapes(IconDef def)
    {
        int broken = 0;
        foreach (var shape in def.Shapes)
        {
            try { Geometry.Parse(shape.D); }
            catch (FormatException) { broken++; }
        }
        return broken;
    }

    public static ImageSource Build(IconDef def)
    {
        var group = new DrawingGroup();
        var (x, y, w, h) = (def.ViewBox[0], def.ViewBox[1], def.ViewBox[2], def.ViewBox[3]);
        foreach (var shape in def.Shapes)
        {
            Geometry geometry;
            try { geometry = Geometry.Parse((shape.EvenOdd ? "F0" : "F1") + shape.D); }
            catch (FormatException) { continue; }
            if (shape.Matrix is { Length: 6 } m)
            {
                geometry = geometry.Clone();   // Parse devuelve la geometría congelada
                geometry.Transform = new MatrixTransform(m[0], m[1], m[2], m[3], m[4], m[5]);
            }
            Brush? fill = shape.Fill is null ? null : Solid(shape.Fill, shape.FillAlpha);
            Pen? pen = shape.Stroke is null ? null : new Pen(Solid(shape.Stroke, shape.StrokeAlpha), shape.StrokeWidth) { LineJoin = PenLineJoin.Round };
            group.Children.Add(new GeometryDrawing(fill, pen, geometry));
        }
        // El área del icono es su viewBox: así todos escalan igual dentro de su casilla.
        group.Children.Insert(0, new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(x, y, w, h))));
        group.ClipGeometry = new RectangleGeometry(new Rect(x, y, w, h));
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    private static SolidColorBrush Solid(string hex, double alpha)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * alpha), c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }
}
