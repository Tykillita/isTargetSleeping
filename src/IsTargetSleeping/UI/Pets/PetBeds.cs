namespace IsTargetSleeping.UI.Pets;

internal enum PetBed { Mira, Llama, Capybara, OrangeCat }

/// Camas dibujadas en dos capas: almohada detrás y cobija delante. La misma entrada
/// continua de la pose sirve para dormir, despertar y las reacciones de siesta.
internal static class PetBeds
{
    internal static void Draw(PixelCanvas c, int n, PetBed bed, double into, bool front)
    {
        double alpha = Math.Clamp(into, 0, 1);
        if (alpha <= 0) return;
        double drop = (1 - alpha) * 3;
        void Box(double l, double t, double r, double b, double radius, uint fill, uint ink) =>
            AnimalDrawing.RoundedBox(c, n, l, t + drop, r, b + drop, radius, fill, ink, alpha);
        void Oval(double x, double y, double rx, double ry, uint fill) =>
            c.Ellipse(x * n, (y + drop) * n, rx * n, ry * n, fill, alpha);
        void Line(double x0, double y0, double x1, double y1, double width, uint fill) =>
            c.Line(x0 * n, (y0 + drop) * n, x1 * n, (y1 + drop) * n, width * n, fill, alpha);
        void Stamp(double x, double y, string[] rows, Func<char, uint> palette) =>
            c.Stamp(x * n, (y + drop) * n, rows, palette, n, alpha);

        switch (bed)
        {
            case PetBed.Mira:
                const uint navy = 0xFF132C44, blue = 0xFF407AC4, ice = 0xFFB9DCFF;
                if (!front)
                {
                    Box(2, 17, 25, 24, 2.5, blue, navy);
                    Box(3, 17.5, 10, 21.5, 1.4, 0xFFF4F8FF, ice);
                    Line(3, 23, 24, 23, 0.7, ice);
                }
                else
                {
                    Box(5, 19.5, 24, 23.5, 1.3, 0xFF335DA3, navy);
                    Line(6, 20.2, 22.5, 20.2, 0.8, ice);
                    Line(8, 21.5, 9, 22.5, 0.5, blue);
                    Line(12, 21.5, 13, 22.5, 0.5, blue);
                    // Una diana bordada en el edredón de su cápsula.
                    Oval(18.5, 22, 1.2, 1.2, ice);
                    Oval(18.5, 22, 0.65, 0.65, navy);
                    Oval(18.5, 22, 0.25, 0.25, ice);
                }
                break;
            case PetBed.Llama:
                const uint teal = 0xFF477C80, darkTeal = 0xFF294C53, gold = 0xFFEFC47D, coral = 0xFFCE7665;
                if (!front)
                {
                    Box(2.5, 23, 29.5, 29.5, 1.7, teal, darkTeal);
                    Box(3, 21.5, 9, 26.5, 1.5, 0xFFEAE3CB, darkTeal);
                    Box(25, 23, 29, 28, 1.5, 0xFF77A2A0, darkTeal);
                }
                else
                {
                    Box(3, 25, 29, 29.5, 1, teal, darkTeal);
                    Line(4, 25.7, 28, 25.7, 0.9, gold);
                    Line(4, 28.7, 28, 28.7, 0.65, gold);
                    for (int x = 7; x <= 25; x += 6)
                        Stamp(x, 26, [".g.", "gcg", ".g."], k => k == 'c' ? coral : gold);
                    Line(1.5, 27.5, 2.5, 28.5, 0.8, gold);
                    Line(30, 27.5, 30, 29, 0.8, gold);
                }
                break;
            case PetBed.Capybara:
                // Un baño termal: tina de madera con aros, el agua caliente al ras y el vapor
                // (partículas). La capibara se mete dentro hasta el lomo.
                const uint wood = 0xFFB98351, woodInk = 0xFF6B4528, plank = 0xFF9A6A3E, hoop = 0xFF5D6670;
                const uint water = 0xFF86CDE2, waterInk = 0xFF4A98B4, foam = 0xFFD8F2F8;
                if (!front)
                {
                    Box(3, 19.5, 29, 26, 2, wood, woodInk);
                    Line(5, 21.2, 27, 21.2, 0.6, plank);
                    Oval(16, 23.4, 12, 1.4, water);
                }
                else
                {
                    Box(3.5, 22.4, 28.5, 25.2, 1.2, water, waterInk);
                    Line(5.5, 23.2, 13, 23.2, 0.6, foam);
                    Line(18, 23.4, 26.5, 23.4, 0.6, foam);
                    Box(2.5, 24.2, 29.5, 29.5, 1.8, wood, woodInk);
                    foreach (double x in new[] { 7.0, 11.5, 16, 20.5, 25 })
                        Line(x, 24.9, x, 28.8, 0.5, plank);
                    Line(3.2, 25.6, 28.8, 25.6, 0.75, hoop);
                    Line(3.2, 28.1, 28.8, 28.1, 0.75, hoop);
                }
                break;
            case PetBed.OrangeCat:
                const uint wicker = 0xFFB97F50, basketInk = 0xFF63442F, linen = 0xFFFFE4AF;
                if (!front)
                {
                    Box(2.5, 23.5, 29.5, 29.5, 2.5, wicker, basketInk);
                    Box(3, 22, 10, 26, 1.8, 0xFFCEBDD9, 0xFF766481);
                    Oval(16, 25.3, 12.5, 2.6, linen);
                }
                else
                {
                    Box(3, 25.5, 29, 29.5, 1.6, wicker, basketInk);
                    Line(4.5, 25.7, 27.5, 25.7, 1.3, linen);
                    for (int x = 6; x <= 26; x += 4)
                    {
                        Line(x, 27, x + 1, 28.5, 0.55, 0xFFE2B680);
                        Line(x, 28.5, x + 1, 27, 0.4, basketInk);
                    }
                    // Emblema de huella cosido a la cesta.
                    Oval(16, 28, 1.2, 0.8, linen);
                    Oval(14.5, 27, 0.45, 0.45, linen);
                    Oval(16, 26.6, 0.45, 0.45, linen);
                    Oval(17.5, 27, 0.45, 0.45, linen);
                }
                break;
        }
    }
}
