namespace IsTargetSleeping;

/// El color de una portada: el dominante (los píxeles vivos y claros pesan más que los grises) y su versión viva para teñir lo que suena
/// (ecualizador, barra de progreso, botones activos), como hace Droppy. Lógica pura, sin WPF, para poder probarla.
public static class ArtColor
{
    /// `bgra`: píxeles de 32 bits. Sin píxeles, gris medio.
    public static (byte R, byte G, byte B) Dominant(byte[] bgra)
    {
        double r = 0, g = 0, b = 0, total = 0;
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            double pb = bgra[i], pg = bgra[i + 1], pr = bgra[i + 2];
            double max = Math.Max(pr, Math.Max(pg, pb)), min = Math.Min(pr, Math.Min(pg, pb));
            double weight = 0.1 + (max - min) / 255.0 * (max / 255.0);
            (r, g, b, total) = (r + pr * weight, g + pg * weight, b + pb * weight, total + weight);
        }
        return total <= 0 ? ((byte)128, (byte)128, (byte)128) : ((byte)(r / total), (byte)(g / total), (byte)(b / total));
    }

    /// El mismo tono, con color y claridad de sobra para verse sobre el cristal oscuro. Una portada gris da un blanco suave.
    public static (byte R, byte G, byte B) Vivid((byte R, byte G, byte B) c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min, l = (max + min) / 2;
        double s = d == 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));
        if (s < 0.12) return (0xE6, 0xE6, 0xEB);
        double h = d == 0 ? 0 : max == r ? ((g - b) / d + (g < b ? 6 : 0)) : max == g ? ((b - r) / d + 2) : ((r - g) / d + 4);
        return FromHsl(h / 6, Math.Max(s, 0.6), Math.Clamp(l, 0.6, 0.72));
    }

    private static (byte R, byte G, byte B) FromHsl(double h, double s, double l)
    {
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        double Channel(double t)
        {
            t = (t % 1 + 1) % 1;
            return (t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p) * 255;
        }
        return ((byte)Math.Round(Channel(h + 1.0 / 3)), (byte)Math.Round(Channel(h)), (byte)Math.Round(Channel(h - 1.0 / 3)));
    }
}
