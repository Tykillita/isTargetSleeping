namespace IsTargetSleeping;

/// El color de una portada: el dominante (los píxeles vivos y claros pesan más que los grises) y su versión viva para teñir lo que suena
/// (ecualizador, barra de progreso, botones activos), como hace Droppy. Lógica pura, sin WPF, para poder probarla.
public static class ArtColor
{
    /// `bgra`: píxeles de 32 bits. Gana el tono con más color: se agrupan los píxeles vivos por tono y se queda el grupo que más pesa (un corazón rojo en una portada
    /// plateada da rojo, no el azul grisáceo del promedio). Una portada sin color se queda con el promedio. Sin píxeles, gris medio.
    public static (byte R, byte G, byte B) Dominant(byte[] bgra)
    {
        const int Bins = 12;
        var (wr, wg, wb, wt) = (new double[Bins], new double[Bins], new double[Bins], new double[Bins]);
        double r = 0, g = 0, b = 0, total = 0;
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            double pb = bgra[i], pg = bgra[i + 1], pr = bgra[i + 2];
            double max = Math.Max(pr, Math.Max(pg, pb)), min = Math.Min(pr, Math.Min(pg, pb));
            double weight = 0.1 + (max - min) / 255.0 * (max / 255.0);
            (r, g, b, total) = (r + pr * weight, g + pg * weight, b + pb * weight, total + weight);
            double sat = max == 0 ? 0 : (max - min) / max, value = max / 255.0;
            if (sat < 0.3 || value < 0.2) continue;   // el plateado, el gris y el negro no cuentan como color
            double h = max == pr ? ((pg - pb) / (max - min) + 6) % 6 : max == pg ? (pb - pr) / (max - min) + 2 : (pr - pg) / (max - min) + 4;
            int bin = Math.Min(Bins - 1, (int)(h / 6 * Bins));
            double w = sat * sat * value;
            (wr[bin], wg[bin], wb[bin], wt[bin]) = (wr[bin] + pr * w, wg[bin] + pg * w, wb[bin] + pb * w, wt[bin] + w);
        }
        int best = Array.IndexOf(wt, wt.Max());
        if (wt[best] > 0) return ((byte)(wr[best] / wt[best]), (byte)(wg[best] / wt[best]), (byte)(wb[best] / wt[best]));
        return total <= 0 ? ((byte)128, (byte)128, (byte)128) : ((byte)(r / total), (byte)(g / total), (byte)(b / total));
    }

    /// El tono oscuro y hondo con el que se tiñe el fondo de las letras (como en Nusa: un rojo de unos 137,53,53 para una portada roja). Una portada gris da un azul apagado.
    public static (byte R, byte G, byte B) Deep((byte R, byte G, byte B) c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0, max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d < 0.06 ? 3.5 : max == r ? ((g - b) / d + 6) % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        double s = d < 0.06 ? 0.35 : Math.Clamp(d / max, 0.5, 0.7);
        return FromHsv(h, s, 0.55);
    }

    private static (byte R, byte G, byte B) FromHsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h % 2 - 1)), m = v - c;
        var (r, g, b) = h < 1 ? (c, x, 0.0) : h < 2 ? (x, c, 0.0) : h < 3 ? (0.0, c, x) : h < 4 ? (0.0, x, c) : h < 5 ? (x, 0.0, c) : (c, 0.0, x);
        return ((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
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
