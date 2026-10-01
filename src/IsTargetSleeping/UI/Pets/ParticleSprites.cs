namespace IsTargetSleeping.UI.Pets;

/// Cómo se ven las partículas: pequeños dibujos de pixel art en su posición real, que
/// crecen por pasos enteros y se desvanecen con transparencia. Las de cada carácter
/// (vapor, burbujas, hojas, notas, ronroneo, resoplido, bits, señales) tienen su dibujo.
public static class ParticleSprites
{
    private const uint ZColor = 0xFF8FB8E0, Pink = 0xFFFF6FA0, Yellow = 0xFFFFE58A, White = 0xFFF4F8FF;
    private const uint Dust = 0xFF9AA3AD, Water = 0xFF9FD8FF, Amber = 0xFFF5BD57, Gray = 0xFFC9D1DB;
    private const uint Steam = 0xFFEEF3F7, SteamShade = 0xFFC9D6E0, BubbleRim = 0xFF6FBFE0, Leaf = 0xFF86A95C, LeafDark = 0xFF587A3A;
    private const uint NoteColor = 0xFFE77FA8, PurrColor = 0xFFFFA94D, Cloud = 0xFFF4F1EA, CloudShade = 0xFF9E9384;
    private const uint BitColor = 0xFF35C977, SignalColor = 0xFF6CC0FF;

    private static readonly string[] Z = ["oooo", "..o.", ".o..", "oooo"];
    private static readonly string[] Heart = [".X.X.", "XXXXX", ".XXX.", "..X.."];
    private static readonly string[] Sparkle = [".Y.", "YWY", ".Y."];
    private static readonly string[] Star = ["..Y..", ".YYY.", "YYWYY", ".YYY.", "..Y.."];
    private static readonly string[] Dot = ["d"];
    private static readonly string[] Drop = [".w", "ww", "ww"];
    private static readonly string[] Wisp = [".ss.", "sSSs", ".ss."];
    private static readonly string[] Bubble = [".bb.", "b.wb", "b..b", ".bb."];
    private static readonly string[] Pop = ["b..b", "....", "....", "b..b"];
    private static readonly string[] LeafShape = ["..gg", ".gGg", "gGg.", "g..."];
    private static readonly string[] Note = ["..nn", "..n.", "..n.", "nnn.", "nn.."];
    private static readonly string[] Tilde = [".o...", "o.o.o", "...o."];
    private static readonly string[] Puff = [".CC.", "CccC", "CcCC", ".CC."];
    private static readonly string[] Zero = [".g.", "g.g", "g.g", ".g."];
    private static readonly string[] One = [".g", "gg", ".g", ".g"];

    public static void Draw(PixelCanvas canvas, IEnumerable<Particle> particles, int scale)
    {
        foreach (var p in particles)
        {
            double alpha = p.Alpha;
            if (alpha <= 0.02) continue;
            if (p.Kind == ParticleKind.Signal)
            {
                Signal(canvas, p, scale, alpha);
                continue;
            }
            var (rows, color) = p.Kind switch
            {
                ParticleKind.Z => (Z, (Func<char, uint>)(_ => ZColor)),
                ParticleKind.Heart => (Heart, _ => Pink),
                ParticleKind.Sparkle => (Sparkle, k => k == 'W' ? White : Yellow),
                ParticleKind.Star => (Star, k => k == 'W' ? White : Yellow),
                ParticleKind.Dust => (Dot, _ => Dust),
                ParticleKind.Steam => (Wisp, k => k == 'S' ? SteamShade : Steam),
                ParticleKind.Bubble => (p.Scale > 1.5 ? Pop : Bubble, k => k == 'w' ? White : BubbleRim),
                ParticleKind.Leaf => (LeafShape, k => k == 'G' ? LeafDark : Leaf),
                ParticleKind.Note => (Note, _ => NoteColor),
                ParticleKind.Purr => (Tilde, _ => PurrColor),
                ParticleKind.Puff => (Puff, k => k == 'C' ? CloudShade : Cloud),
                ParticleKind.Bit => (p.Phase > 0.5 ? One : Zero, _ => BitColor),
                _ => (Drop, _ => Water),
            };
            // El reventón de la burbuja no se amplía: se separa.
            double grow = p.Kind == ParticleKind.Bubble ? 1 : p.Scale;
            int size = Math.Max(1, (int)Math.Round(scale * grow));
            double w = rows[0].Length * size, h = rows.Length * size;
            canvas.Stamp(p.X * scale - w / 2, p.Y * scale - h / 2, rows, color, size, alpha);
        }
    }

    /// Las ondas de la antena: dos arcos, a izquierda y derecha, que se abren y se apagan.
    private static void Signal(PixelCanvas canvas, Particle p, int scale, double alpha)
    {
        double radius = (1.2 + 2.6 * p.Scale) * scale, cx = p.X * scale, cy = p.Y * scale;
        for (int i = -3; i <= 3; i++)
        {
            double a = i * 0.28;
            foreach (int side in new[] { -1, 1 })
            {
                double x = cx + side * Math.Cos(a) * radius, y = cy + Math.Sin(a) * radius;
                canvas.FillRect(x - scale * 0.5, y - scale * 0.5, x + scale * 0.5, y + scale * 0.5, SignalColor, alpha);
            }
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
