using static IsTargetSleeping.L10n;
using static IsTargetSleeping.UI.Pets.AnimalDrawing;

namespace IsTargetSleeping.UI.Pets;

public sealed class CapybaraPet : IPetSpecies
{
    public string Id => "capybara";
    public string Name => tr("Capibara");
    public IPetAnimationProfile Animations => PetProfiles.Capybara;
    public PetSize Size { get; } = new(32, 30, 22, Reach: 29, HideRight: 29, HideMiddle: 21, CenterX: 16);
    public PetAnchors Anchors { get; } = new(11, 7.5, 17, 23, 11, 15, 29.5, MouthX: 7.5, MouthY: 19.5);

    private const uint Outline = 0xFF785433;
    private const uint Feather = 0xFF6F8FB8, FeatherDark = 0xFF46607F, Belly = 0xFFF4EEE2, Beak = 0xFFF2A23A;
    private const int BodyHeight = 17;
    // Perfil erguido de la referencia: cabeza alta, morro romo, lomo más bajo
    // y pecho continuo hasta las patas. Contorno cálido y sombras en bloques.
    private static readonly string[] Body =
    [
        ".........oo..............",
        ".....ooooffo.............",
        "...oohffffffo............",
        "..ofhffffffffo...........",
        ".offfffffffffo...........",
        "offffffffffffo...........",
        "offfffffffffffo..........",
        "offffffffffffffooooooo...",
        ".offffffffffffffffffffoo.",
        "..ooffffffffffffffffhfffo",
        "....offffffffffffffffhffo",
        ".....osssfffffffffffffffo",
        "......ossfffffffffffffffo",
        "......ossfffffffffffffffo",
        ".......osffffffffffffffo.",
        "........ossssssssssssso..",
        ".........ooooooooooooo...",
    ];

    public void Draw(PixelCanvas c, PetPose p, int n)
    {
        uint fur = Tint(0xFFDCA76D, p.Tint), shade = Tint(0xFFBD874F, p.Tint);
        uint light = Tint(0xFFEFC68F, p.Tint), deep = Tint(0xFF956333, p.Tint);
        double s = Math.Clamp(p.Squash, -1.3, 1.3), sit = Math.Clamp(p.Sit, 0, 1);
        // Su cama es un baño termal; el gesto `Bath` usa la misma tina despierta.
        double tub = Math.Clamp(Math.Max(p.BedIn, p.Bath), 0, 1);
        double cx = 16 + p.Lean * 0.35, bottom = 27.5 + p.Bob + sit * 0.65 + tub * 1.6;
        double sx = 1 + s * 0.03 + sit * 0.06, sy = 1 - s * 0.04 - sit * 0.16;
        double lift = p.Feet == PetFeet.Stand ? 0 : Math.Clamp(Math.Abs(p.EarL) / 0.3, 0, 1) * 0.8;

        (double X, double Y) Point(double column, double row) =>
            (cx + (column - 12.5) * sx + p.Lean * (row - 8.5) * 0.1, bottom + (row - BodyHeight) * sy);
        PetBeds.Draw(c, n, PetBed.Capybara, tub, false);

        // Cuatro apoyos pequeños. Los lejanos quedan más altos y oscuros; los
        // cercanos terminan en un pie de dos píxeles, sin botas ni barras internas.
        if (tub < 0.6)
        {
            Foot(10, 28.6, p.Feet == PetFeet.StepLeft, true);
            Foot(20, 28.6, p.Feet == PetFeet.StepRight, true);
            Foot(7.5, 29.2, p.Feet == PetFeet.StepRight, false);
            Foot(22, 29.2, p.Feet == PetFeet.StepLeft, false);
        }

        // Muestreo inverso: el dibujo de rejilla se deforma a resolución física,
        // conservando la respiración, la inclinación y la mezcla de poses.
        for (int py = (int)Math.Floor((bottom - BodyHeight * sy) * n); py < (int)Math.Ceiling(bottom * n); py++)
        for (int px = (int)Math.Floor((cx - 14 * sx) * n); px < (int)Math.Ceiling((cx + 14 * sx) * n); px++)
        {
            double row = ((py + 0.5) / n - bottom) / sy + BodyHeight;
            double column = ((px + 0.5) / n - cx - p.Lean * (row - 8.5) * 0.1) / sx + 12.5;
            int iy = (int)Math.Floor(row), ix = (int)Math.Floor(column);
            if (iy < 0 || iy >= Body.Length || ix < 0 || ix >= Body[iy].Length) continue;
            uint color = Body[iy][ix] switch { 'o' => Outline, 'f' => fur, 's' => shade, 'h' => light, _ => 0u };
            if (color != 0) c.Set(px, py, color);
        }

        // La caja, en equilibrio sobre la cabeza; la mandarina sube encima de ella. La
        // mandarina va con un muelle (`Antenna`): se tambalea al moverse.
        var orange = Point(6, 1);
        double boxIn = p.Prop == PetProp.Box ? Math.Clamp(p.PropIn, 0, 1) : 0;
        if (boxIn > 0.01)
            c.Stamp((orange.X - 3.5) * n, (orange.Y - 6 - (1 - boxIn) * 3) * n, ["oooooooo", "obbbybbo", "obbbybbo", "obbbybbo", "obbbbbbo", "oooooooo"],
                k => k switch { 'b' => 0xFFC98A4B, 'y' => 0xFFE8C170, _ => Ink }, n, boxIn);
        double wobble = Math.Clamp(p.Antenna, -1.5, 1.5);
        c.Stamp((orange.X - 1 + wobble * 0.9) * n, (orange.Y - 3 - boxIn * 6 + Math.Abs(wobble) * 0.3) * n, [".l.", "abo", "aaa"],
            k => k switch { 'l' => 0xFF6F8745, 'o' => 0xFFD47628, 'b' => 0xFFFFBD5B, _ => 0xFFEF862C }, n);

        var eye = Point(7.5, 4.5);
        double ear = p.EarR * 0.3;
        var earPoint = Point(10, 1.1);
        c.FillRect((earPoint.X - 0.5) * n, (earPoint.Y + ear) * n,
            (earPoint.X + 0.5) * n, (earPoint.Y + 1 + ear) * n, deep);
        if (p.Eye < 0.2)
            c.Line((eye.X - 0.6) * n, eye.Y * n, (eye.X + 0.6) * n, (eye.Y + 0.2) * n, 0.55 * n, Outline);
        else
        {
            // Ojos entornados: un párpado pesado encima, salvo bien despierta.
            double ry = 0.2 + p.Eye * 0.4;
            c.Ellipse((eye.X + p.LookX * 0.22) * n, (eye.Y + p.LookY * 0.2) * n, 0.65 * n, ry * n, 0xFF241B12);
            if (p.Eye < 0.8)
                c.Line((eye.X - 0.8) * n, (eye.Y - ry + 0.1) * n, (eye.X + 0.8) * n, (eye.Y - ry + 0.1) * n, 0.5 * n, deep);
        }
        var nose = Point(1, 5.5);
        c.FillRect((nose.X - 0.6) * n, (nose.Y - 0.8) * n, (nose.X + 1.7) * n, (nose.Y + 1.5) * n, deep);
        var mouth = Point(4, 9);
        if (p.Mouth > 0.1)
        {
            c.Ellipse(mouth.X * n, mouth.Y * n, 0.65 * n, (0.25 + p.Mouth * 0.6) * n, Outline);
            if (p.Mouth > 0.5) c.Ellipse(mouth.X * n, (mouth.Y + 0.2) * n, 0.35 * n, 0.25 * n, Pink);
        }
        else c.Line((mouth.X - 1) * n, mouth.Y * n, (mouth.X + 0.5) * n, mouth.Y * n, 0.45 * n, deep);
        if (p.Blush > 0) c.Ellipse((eye.X - 0.5) * n, (eye.Y + 1.8) * n, 1 * n, 0.45 * n, Pink, p.Blush * 0.7);

        // Los accesorios se acercan al hocico o se apoyan delante. Esta especie
        // conserva sus cuatro patas y nunca dibuja brazos, incluso al reaccionar.
        if (p.Prop != PetProp.Box)
            Props(c, n, p, p.Prop == PetProp.Laptop ? 18 : 16, orange.Y - 3, (mouth.X + 0.6, mouth.Y + 0.8));
        PetBeds.Draw(c, n, PetBed.Capybara, tub, true);
        if (p.Bird > 0.02) Bird(Point(16, 6.6));

        // El pajarito: llega volando desde arriba a la derecha (aleteando) y se posa en el lomo.
        void Bird((double X, double Y) perch)
        {
            double k = Math.Clamp(p.Bird, 0, 1), alpha = Math.Min(1, k * 5);
            double bx = 28 + (perch.X - 28) * k, by = 3 + (perch.Y - 3) * k - Math.Sin(Math.PI * k) * 3;
            bool flying = k < 0.97;
            c.Ellipse(bx * n, by * n, 1.45 * n, 1.05 * n, FeatherDark, alpha);
            c.Ellipse(bx * n, by * n, 1.05 * n, 0.7 * n, Feather, alpha);
            c.Ellipse((bx - 0.2) * n, (by + 0.35) * n, 0.7 * n, 0.4 * n, Belly, alpha);
            c.Ellipse((bx - 1.1) * n, (by - 0.7) * n, 0.85 * n, 0.8 * n, Feather, alpha);
            c.FillRect((bx - 1.4) * n, (by - 0.95) * n, (bx - 0.9) * n, (by - 0.45) * n, 0xFF1C1C1C, alpha);
            c.Line((bx - 1.9) * n, (by - 0.6) * n, (bx - 2.6) * n, (by - 0.4) * n, 0.55 * n, Beak, alpha);
            c.Line((bx + 1.2) * n, (by - 0.1) * n, (bx + 2.2) * n, (by - 0.5) * n, 0.6 * n, FeatherDark, alpha);
            double wing = flying ? Math.Sin(k * 70) : 0;
            c.Line((bx + 0.1) * n, (by - 0.2) * n, (bx + 0.9) * n, (by - 0.4 - wing * 1.4) * n, 0.75 * n, FeatherDark, alpha);
        }

        void Foot(double col, double ground, bool step, bool far)
        {
            double fx = Point(col, 15).X, fy = ground - sit * 2.1 - (step ? lift : 0);
            c.FillRect((fx - 0.9) * n, (fy - 4.2) * n, (fx + 1.4) * n, fy * n, far ? deep : shade);
            if (!far) c.FillRect((fx + 0.1) * n, (fy - 3.2) * n, (fx + 1.1) * n, (fy - 0.75) * n, fur);
            c.FillRect((fx - 1.2) * n, (fy - 0.75) * n, (fx + 1.4) * n, fy * n, Outline);
        }
    }
}
