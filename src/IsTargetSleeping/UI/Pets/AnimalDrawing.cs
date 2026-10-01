namespace IsTargetSleeping.UI.Pets;

/// Formas continuas y accesorios de rejilla. Las especies conservan su propia anatomía.
internal static class AnimalDrawing
{
    internal const uint Ink = 0xFF362D30, Pink = 0xFFF3A3A0;
    internal static uint Tint(uint color, PetTint tint)
    {
        var (target, weight) = tint switch
        {
            PetTint.Sleepy => (0xFF87A4C8u, 0.18), PetTint.Warm => (0xFFF5BD57u, 0.16),
            PetTint.Danger => (0xFFFF7A70u, 0.35), _ => (color, 0.0),
        };
        uint C(int shift) => (uint)Math.Round((color >> shift & 255) * (1 - weight) + (target >> shift & 255) * weight) << shift;
        return 0xFF000000 | C(16) | C(8) | C(0);
    }
    internal static void Oval(PixelCanvas c, int n, double x, double y, double rx, double ry, uint fill, uint shade, double lean = 0)
    {
        for (int py = (int)Math.Floor((y - ry) * n); py <= (int)Math.Ceiling((y + ry) * n); py++)
        for (int px = (int)Math.Floor((x - rx - 1) * n); px <= (int)Math.Ceiling((x + rx + 1) * n); px++)
        {
            double dy = (py + 0.5) / n - y, dx = (px + 0.5) / n - x - lean * dy * 0.15;
            if (dx * dx / (rx * rx) + dy * dy / (ry * ry) > 1) continue;
            bool edge = dx * dx / Math.Pow(Math.Max(0.1, rx - 0.65), 2) + dy * dy / Math.Pow(Math.Max(0.1, ry - 0.65), 2) > 1;
            c.Set(px, py, edge ? Ink : dy + dx * 0.35 > ry * 0.5 ? shade : fill);
        }
    }
    // Una sola silueta evita las costuras negras entre el cuello, el hocico y el cuerpo.
    internal static void Silhouette(PixelCanvas c, int n, double left, double top, double right, double bottom,
        Func<double, double, bool> contains, Func<double, double, uint> color, double alpha = 1)
    {
        const double edge = 0.65;
        for (int py = (int)Math.Floor(top * n); py < (int)Math.Ceiling(bottom * n); py++)
        for (int px = (int)Math.Floor(left * n); px < (int)Math.Ceiling(right * n); px++)
        {
            double x = (px + 0.5) / n, y = (py + 0.5) / n;
            if (!contains(x, y)) continue;
            bool border = !contains(x - edge, y) || !contains(x + edge, y)
                || !contains(x, y - edge) || !contains(x, y + edge);
            c.Set(px, py, border ? Ink : color(x, y), alpha);
        }
    }
    internal static bool InOval(double x, double y, double cx, double cy, double rx, double ry) =>
        Math.Pow((x - cx) / rx, 2) + Math.Pow((y - cy) / ry, 2) <= 1;

    internal static bool InBox(double x, double y, double left, double top, double right, double bottom, double radius)
    {
        if (x < left || x > right || y < top || y > bottom) return false;
        double cx = Math.Clamp(x, left + radius, right - radius), cy = Math.Clamp(y, top + radius, bottom - radius);
        return (x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius;
    }
    internal static void RoundedBox(PixelCanvas c, int n, double left, double top, double right, double bottom,
        double radius, uint fill, uint outline, double alpha = 1)
    {
        for (int py = (int)Math.Floor(top * n); py < (int)Math.Ceiling(bottom * n); py++)
        for (int px = (int)Math.Floor(left * n); px < (int)Math.Ceiling(right * n); px++)
        {
            double x = (px + 0.5) / n, y = (py + 0.5) / n;
            if (!InBox(x, y, left, top, right, bottom, radius)) continue;
            bool inside = InBox(x, y, left + 0.65, top + 0.65, right - 0.65, bottom - 0.65, Math.Max(0.1, radius - 0.65));
            c.Set(px, py, inside ? fill : outline, alpha);
        }
    }
    internal static void Triangle(PixelCanvas c, int n, (double X, double Y) a, (double X, double Y) b, (double X, double Y) d, uint color, double alpha = 1)
    {
        static double Cross(double ax, double ay, double bx, double by, double px, double py) => (bx - ax) * (py - ay) - (by - ay) * (px - ax);
        for (int y = (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, d.Y)) * n); y <= (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, d.Y)) * n); y++)
        for (int x = (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, d.X)) * n); x <= (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, d.X)) * n); x++)
        {
            double px = (x + 0.5) / n, py = (y + 0.5) / n;
            double q = Cross(a.X, a.Y, b.X, b.Y, px, py), r = Cross(b.X, b.Y, d.X, d.Y, px, py), s = Cross(d.X, d.Y, a.X, a.Y, px, py);
            if (q >= 0 && r >= 0 && s >= 0 || q <= 0 && r <= 0 && s <= 0) c.Set(x, y, color, alpha);
        }
    }
    internal static void Eyes(PixelCanvas c, int n, PetPose p, double x, double y, double apart, double radius = 1.1)
    {
        double open = Math.Clamp(p.Eye, 0, 1);
        foreach (int side in new[] { -1, 1 })
        {
            double ex = x + side * apart;
            if (open < 0.2)
            {
                c.Line((ex - radius) * n, y * n, ex * n, (y + 0.5) * n, n * 0.65, Ink);
                c.Line(ex * n, (y + 0.5) * n, (ex + radius) * n, y * n, n * 0.65, Ink);
            }
            else
            {
                c.Ellipse((ex + p.LookX * 0.35) * n, (y + p.LookY * 0.3) * n, radius * n, (0.25 + open * radius) * n, Ink);
                if (open > 0.65) c.Ellipse((ex - 0.25 + p.LookX * 0.35) * n, (y - 0.3 + p.LookY * 0.3) * n, 0.3 * n, 0.3 * n, 0xFFFFF4E7);
            }
        }
    }
    internal static void Mouth(PixelCanvas c, int n, PetPose p, double x, double y)
    {
        if (p.Mouth > 0.1)
        {
            c.Ellipse(x * n, y * n, 1 * n, (0.25 + p.Mouth * 0.8) * n, Ink);
            c.Ellipse(x * n, (y + 0.35) * n, 0.6 * n, p.Mouth * 0.35 * n, Pink);
        }
        else c.Line((x - 0.8) * n, y * n, (x + 0.8) * n, y * n, n * 0.5, Ink);
        if (p.Blush > 0.01)
        {
            c.Ellipse((x - 3.3) * n, (y - 0.7) * n, n, n * 0.45, Pink, p.Blush);
            c.Ellipse((x + 3.3) * n, (y - 0.7) * n, n, n * 0.45, Pink, p.Blush);
        }
    }
    internal static void Feet(PixelCanvas c, int n, PetPose p, double cx, uint fill, uint shade)
    {
        if (p.Sit > 0.8) return;
        Oval(c, n, cx - 3.4, 28 - (p.Feet == PetFeet.StepLeft ? 1 : 0), 1.8, 1.3, fill, shade);
        Oval(c, n, cx + 3.4, 28 - (p.Feet == PetFeet.StepRight ? 1 : 0), 1.8, 1.3, fill, shade);
    }
    internal static (double X, double Y) Paw(PixelCanvas c, int n, double x, double y, double angle, int side, uint fill, bool hidden = false)
    {
        double a = angle * Math.PI / 180, length = angle < -100 ? 4.5 : 2.5;
        double hx = x + side * Math.Cos(a) * length, hy = y + Math.Sin(a) * length;
        if (!hidden)
        {
            c.Line(x * n, y * n, hx * n, hy * n, n * 2.3, Ink);
            c.Line(x * n, y * n, hx * n, hy * n, n * 1.1, fill);
            c.Ellipse(hx * n, hy * n, n * 0.8, n * 0.8, fill);
        }
        return (hx, hy);
    }
    internal static void Props(PixelCanvas c, int n, PetPose p, double x, double top, (double X, double Y) hand)
    {
        double a = Math.Clamp(p.PropIn, 0, 1);
        if (a <= 0.01) return;
        switch (p.Prop)
        {
            case PetProp.Laptop:
                c.Stamp((x - 4.5) * n, (23 + (1 - a) * 5) * n,
                    [".ooooooo.", ".ogggggo.", ".oggbggo.", ".ogggggo.", "okkkkkkko"],
                    k => k switch { 'g' => 0xFFBFC9D6, 'b' => 0xFF4DA3FF, 'k' => 0xFF596574, _ => Ink }, n, a);
                break;
            case PetProp.Box:
                c.Stamp((x - 3.5) * n, Math.Max(2, top - 4.5 - (1 - a) * 3) * n,
                    ["ooooooo", "obbybbo", "obbybbo", "obbbbbo", "ooooooo"],
                    k => k switch { 'b' => 0xFFC98A4B, 'y' => 0xFFE8C170, _ => Ink }, n, a);
                break;
            case PetProp.Crumb:
                c.Stamp((hand.X - 1.5) * n, (hand.Y - 2.2) * n,
                    [".oo.", "oyyo", "oyyo", ".oo."], k => k == 'y' ? 0xFFF5BD57 : Ink, n, a);
                break;
            case PetProp.Broom:
                double x1 = x + 10 * p.Sway;
                c.Line(hand.X * n, hand.Y * n, x1 * n, 27 * n, n * 0.9, 0xFFA5703A, a);
                c.Stamp((x1 - 2) * n, 26 * n, [".ooo.", "oyyyo", "oyoyo"], k => k == 'y' ? 0xFFE8C170 : Ink, n, a);
                break;
        }
    }
}
