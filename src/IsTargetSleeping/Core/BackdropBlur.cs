namespace IsTargetSleeping;

/// El desenfoque del vidrio del notch, barato: una ventana con transparencia por píxel no puede pedirle a Windows que desenfoque lo de
/// detrás, así que se copia la zona de pantalla que queda tras el notch, se reduce promediando celdas (cada celda es un punto de color) y,
/// al ampliarla con suavizado, parece un desenfoque grande. Lógica pura, sin WPF, para poder probarla.
public static class BackdropBlur
{
    /// `bgra` son píxeles de 32 bits, de arriba abajo. Devuelve la imagen reducida (también BGRA, opaca) con el color realzado
    /// (`saturation` 1 = igual) y aclarado (`lift`) y su tamaño en `outWidth` y `outHeight`.
    public static byte[] Reduce(byte[] bgra, int width, int height, int cell, out int outWidth, out int outHeight, double saturation = 1.35, double lift = 0)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4) throw new ArgumentException("la imagen no cabe en el tamaño dado");
        cell = Math.Max(1, cell);
        outWidth = (width + cell - 1) / cell;
        outHeight = (height + cell - 1) / cell;
        var result = new byte[outWidth * outHeight * 4];
        for (int oy = 0; oy < outHeight; oy++)
            for (int ox = 0; ox < outWidth; ox++)
            {
                long b = 0, g = 0, r = 0;
                int x0 = ox * cell, y0 = oy * cell, x1 = Math.Min(x0 + cell, width), y1 = Math.Min(y0 + cell, height);
                for (int y = y0; y < y1; y++)
                    for (int x = x0, i = (y * width + x0) * 4; x < x1; x++, i += 4)
                    {
                        b += bgra[i];
                        g += bgra[i + 1];
                        r += bgra[i + 2];
                    }
                double count = (x1 - x0) * (y1 - y0);
                double mb = b / count, mg = g / count, mr = r / count, gray = 0.114 * mb + 0.587 * mg + 0.299 * mr;
                int o = (oy * outWidth + ox) * 4;
                // `lift`: hacia el blanco (0 = igual, 1 = blanco), porque el vidrio deja pasar más luz de la que tiene el fondo.
                result[o] = Clamp(Lift(gray + (mb - gray) * saturation, lift));
                result[o + 1] = Clamp(Lift(gray + (mg - gray) * saturation, lift));
                result[o + 2] = Clamp(Lift(gray + (mr - gray) * saturation, lift));
                result[o + 3] = 255;
            }
        return result;
    }

    /// Claridad media (0 negro … 1 blanco) de una imagen BGRA, para saber cuánto oscurecer el vidrio y que el texto se lea.
    public static double Luminance(byte[] bgra, int width, int height)
    {
        int pixels = Math.Min(width * height, bgra.Length / 4);
        if (pixels == 0) return 0;
        double sum = 0;
        for (int i = 0; i < pixels; i++) sum += 0.114 * bgra[i * 4] + 0.587 * bgra[i * 4 + 1] + 0.299 * bgra[i * 4 + 2];
        return sum / pixels / 255.0;
    }

    private static double Lift(double v, double lift) => v + (255 - v) * lift;

    private static byte Clamp(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
}
