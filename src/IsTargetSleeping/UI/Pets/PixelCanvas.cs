namespace IsTargetSleeping.UI.Pets;

/// Un lienzo de píxeles físicos ARGB (0 = transparente) para dibujar mascotas: formas
/// continuas (elipses, trazos) sin suavizado —nítidas— y dibujos de pixel art ampliados
/// por un entero, con transparencia.
public sealed class PixelCanvas(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public uint[] Pixels { get; } = new uint[width * height];

    public void Clear() => Array.Clear(Pixels);

    public uint Get(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height ? Pixels[y * Width + x] : 0;

    /// Pinta encima (`argb` con su alfa, multiplicado por `alpha`).
    public void Set(int x, int y, uint argb, double alpha = 1)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return;
        double sa = (argb >> 24) / 255.0 * Math.Clamp(alpha, 0, 1);
        if (sa <= 0) return;
        int i = y * Width + x;
        if (sa >= 1) { Pixels[i] = argb | 0xFF000000; return; }
        uint dst = Pixels[i];
        double da = (dst >> 24) / 255.0, oa = sa + da * (1 - sa);
        byte Mix(int shift) => (byte)Math.Round(((argb >> shift & 0xFF) * sa + (dst >> shift & 0xFF) * da * (1 - sa)) / oa);
        Pixels[i] = (uint)Math.Round(oa * 255) << 24 | (uint)Mix(16) << 16 | (uint)Mix(8) << 8 | Mix(0);
    }

    public void FillRect(double x0, double y0, double x1, double y1, uint argb, double alpha = 1)
    {
        for (int y = (int)Math.Round(y0); y < (int)Math.Round(y1); y++)
            for (int x = (int)Math.Round(x0); x < (int)Math.Round(x1); x++)
                Set(x, y, argb, alpha);
    }

    /// Un trazo de `thickness` píxeles con extremos redondos (los píxeles cuyo centro cae
    /// a menos de medio grosor del segmento).
    public void Line(double x0, double y0, double x1, double y1, double thickness, uint argb, double alpha = 1)
    {
        double r = thickness / 2, dx = x1 - x0, dy = y1 - y0, length2 = dx * dx + dy * dy;
        int left = (int)Math.Floor(Math.Min(x0, x1) - r), right = (int)Math.Ceiling(Math.Max(x0, x1) + r);
        int top = (int)Math.Floor(Math.Min(y0, y1) - r), bottom = (int)Math.Ceiling(Math.Max(y0, y1) + r);
        for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
            {
                double px = x + 0.5, py = y + 0.5;
                double k = length2 <= 0 ? 0 : Math.Clamp(((px - x0) * dx + (py - y0) * dy) / length2, 0, 1);
                double ex = px - (x0 + k * dx), ey = py - (y0 + k * dy);
                if (ex * ex + ey * ey <= r * r) Set(x, y, argb, alpha);
            }
    }

    public void Ellipse(double cx, double cy, double rx, double ry, uint argb, double alpha = 1)
    {
        if (rx <= 0 || ry <= 0) return;
        for (int y = (int)Math.Floor(cy - ry); y <= (int)Math.Ceiling(cy + ry); y++)
            for (int x = (int)Math.Floor(cx - rx); x <= (int)Math.Ceiling(cx + rx); x++)
            {
                double u = (x + 0.5 - cx) / rx, v = (y + 0.5 - cy) / ry;
                if (u * u + v * v <= 1) Set(x, y, argb, alpha);
            }
    }

    /// Un dibujo en texto (cada carácter, un píxel de `scale`×`scale`; '.' y ' ' transparentes)
    /// con la esquina en (x, y) físicos.
    public void Stamp(double x, double y, string[] rows, Func<char, uint> color, int scale = 1, double alpha = 1)
    {
        if (alpha <= 0.01) return;
        int ox = (int)Math.Round(x), oy = (int)Math.Round(y);
        for (int row = 0; row < rows.Length; row++)
            for (int col = 0; col < rows[row].Length; col++)
            {
                if (rows[row][col] is '.' or ' ') continue;
                uint c = color(rows[row][col]);
                for (int dy = 0; dy < scale; dy++)
                    for (int dx = 0; dx < scale; dx++)
                        Set(ox + col * scale + dx, oy + row * scale + dy, c, alpha);
            }
    }

    /// BGRA premultiplicado, ampliado `scale` veces, de arriba abajo (para `UpdateLayeredWindow`).
    public byte[] ToBgra(int scale = 1)
    {
        int w = Width * scale, h = Height * scale;
        var bytes = new byte[w * h * 4];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                uint c = Pixels[y * Width + x];
                if (c == 0) continue;
                uint a = c >> 24;
                byte r = (byte)((c >> 16 & 0xFF) * a / 255), g = (byte)((c >> 8 & 0xFF) * a / 255), b = (byte)((c & 0xFF) * a / 255);
                for (int dy = 0; dy < scale; dy++)
                {
                    int i = ((y * scale + dy) * w + x * scale) * 4;
                    for (int dx = 0; dx < scale; dx++, i += 4)
                    {
                        bytes[i] = b; bytes[i + 1] = g; bytes[i + 2] = r; bytes[i + 3] = (byte)a;
                    }
                }
            }
        return bytes;
    }
}
