namespace IsTargetSleeping.UI.Pets;

/// Cómo se ven las partículas (iguales para todas las mascotas): pequeños dibujos de pixel
/// art en su posición real, que crecen por pasos enteros y se desvanecen con transparencia.
public static class ParticleSprites
{
    private const uint ZColor = 0xFF8FB8E0, Pink = 0xFFFF6FA0, Yellow = 0xFFFFE58A, White = 0xFFF4F8FF;
    private const uint Dust = 0xFF9AA3AD, Water = 0xFF9FD8FF, Amber = 0xFFF5BD57, Gray = 0xFFC9D1DB;

    private static readonly string[] Z = ["oooo", "..o.", ".o..", "oooo"];
    private static readonly string[] Heart = [".X.X.", "XXXXX", ".XXX.", "..X.."];
    private static readonly string[] Sparkle = [".Y.", "YWY", ".Y."];
    private static readonly string[] Star = ["..Y..", ".YYY.", "YYWYY", ".YYY.", "..Y.."];
    private static readonly string[] Dot = ["d"];
    private static readonly string[] Drop = [".w", "ww", "ww"];

    public static void Draw(PixelCanvas canvas, IEnumerable<Particle> particles, int scale)
    {
        foreach (var p in particles)
        {
            double alpha = p.Alpha;
            if (alpha <= 0.02) continue;
            var (rows, color) = p.Kind switch
            {
                ParticleKind.Z => (Z, (Func<char, uint>)(_ => ZColor)),
                ParticleKind.Heart => (Heart, _ => Pink),
                ParticleKind.Sparkle => (Sparkle, k => k == 'W' ? White : Yellow),
                ParticleKind.Star => (Star, k => k == 'W' ? White : Yellow),
                ParticleKind.Dust => (Dot, _ => Dust),
                _ => (Drop, _ => Water),
            };
            int size = Math.Max(1, (int)Math.Round(scale * p.Scale));
            double w = rows[0].Length * size, h = rows.Length * size;
            canvas.Stamp(p.X * scale - w / 2, p.Y * scale - h / 2, rows, color, size, alpha);
        }
    }

    /// Tres puntos sobre la cabeza que laten uno detrás de otro («pensando»).
    public static void Thinking(PixelCanvas canvas, PetAnchors at, int scale, double seconds)
    {
        for (int i = 0; i < 3; i++)
        {
            double beat = 0.5 + 0.5 * Math.Sin(2 * Math.PI * (seconds * 1.5 - i / 3.0));
            double x = (at.HeadX + 4.5 + i * 2.6) * scale, y = (at.HeadY - 2 - beat * 0.8) * scale;
            canvas.FillRect(x, y, x + 2 * scale, y + 2 * scale, beat > 0.75 ? Amber : Gray, 0.35 + 0.65 * beat);
        }
    }
}
