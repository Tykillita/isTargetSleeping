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
    // Sentada: la misma cabeza, el pecho erguido sobre la pata delantera y el muslo trasero
    // doblado en el suelo. Es su postura sin nada que hacer (y la de ir montada).
    private static readonly string[] Seated =
    [
        ".........oo..........",
        ".....ooooffo.........",
        "...oohffffffo........",
        "..ofhffffffffo.......",
        ".offfffffffffo.......",
        "offffffffffffo.......",
        "offfffffffffffo......",
        "offffffffffffffoo....",
        ".offfffffffffffffo...",
        "..ooffffffffffffhfo..",
        "....offffffffffffhfo.",
        "....offfffffffffffffo",
        "....osffffffffffffffo",
        "....osfffffffssffffffo",
        "....osffffffsffsfffffo",
        "....osfffffsffffhsfffo",
        "....osfffffsfffffsfffo",
        "....osfffffosfffffsffo",
        "....osfffo.ossssssssso",
        "....ooooo...ooooooooo.",
    ];

    public void Draw(PixelCanvas c, PetPose p, int n)
    {
        // Mojada, el pelo se oscurece y se le pega al cuerpo.
        double wet = Math.Clamp(p.Wet, 0, 1) * 0.45;
        uint fur = Damp(Tint(0xFFDCA76D, p.Tint)), shade = Damp(Tint(0xFFBD874F, p.Tint));
        uint light = Damp(Tint(0xFFEFC68F, p.Tint)), deep = Tint(0xFF956333, p.Tint);
        double s = Math.Clamp(p.Squash, -1.3, 1.3), sit = Math.Clamp(p.Sit, 0, 1);
        // Su cama es un baño termal; el gesto `Bath` usa la misma tina despierta.
        double tub = Math.Clamp(Math.Max(p.BedIn, p.Bath), 0, 1);
        // Sentada fuera de la tina usa su dibujo sentado (con su pata delantera). En el cocodrilo
        // (`Ride`) sube a su lomo: de pie al subir y bajar, con las patas sobre él; sentada al
        // pasear, un poco más atrás para que se le vea la cara.
        double ride = Math.Clamp(p.Ride, 0, 1);
        bool seated = sit * (1 - tub) >= 0.5;
        var body = seated ? Seated : Body;
        int height = body.Length;
        double cx = 16 + p.Lean * 0.35 + ride * (seated ? 5.5 : 3.5);
        // Sentada ya toca el suelo: al cabecear baja menos, para no salirse por abajo.
        const double seatedGround = 29.4, standGround = 29.2;
        double standLift = ride * (standGround - (Crocodile.Top + 3.9));
        double bottom = seated
            ? Math.Min(seatedGround + p.Bob * 0.5, 29.9) - ride * (seatedGround - (Crocodile.Top + 5.3))
            : 27.5 + p.Bob + sit * 0.65 + tub * 1.6 - standLift;
        double sx = 1 + s * 0.03 + (seated ? 0 : sit * 0.06), sy = 1 - s * 0.04 - (seated ? 0 : sit * 0.16);
        double lift = p.Feet == PetFeet.Stand ? 0 : Math.Clamp(Math.Abs(p.EarL) / 0.3, 0, 1) * 0.8;

        // Al frotarse el trasero, el cuerpo se echa hacia delante con el trasero quieto en el suelo
        // (`Drag`) y el trasero se arrastra (`Rump`): solo se desplazan las filas de abajo.
        double drag = Math.Clamp(p.Drag, 0, 1.2), rump = Math.Clamp(p.Rump, -1.2, 1.2);
        double Shift(double row) => p.Lean * (row - 8.5) * 0.1 - drag * (height - row) * 0.145
            + rump * 1.5 * Math.Clamp((row - (height - 8)) / 8, 0, 1);
        (double X, double Y) Point(double column, double row) =>
            (cx + (column - 12.5) * sx + Shift(row), bottom + (row - height) * sy);
        PetBeds.Draw(c, n, PetBed.Capybara, tub, false);
        Crocodile.Draw(c, n, p.Croc, p.CrocStep);
        // La marca de polvo que deja el trasero: se queda atrás (a la derecha) y se borra.
        double skid = PetPose.SkidAlpha(p.Skid);
        if (skid > 0.02 && seated)
        {
            double sx0 = Point(19.5, height).X + p.Skid * 3, sy0 = bottom - 0.4;
            c.Line(sx0 * n, sy0 * n, (sx0 + 4) * n, sy0 * n, 0.8 * n, 0xFF9C7A52, skid * 0.9);
            c.Line((sx0 + 1) * n, (sy0 - 0.8) * n, (sx0 + 3.5) * n, (sy0 - 0.8) * n, 0.6 * n, 0xFFC9A77A, skid * 0.7);
        }

        // Cuatro apoyos pequeños. Los lejanos quedan más altos y oscuros; los
        // cercanos terminan en un pie de dos píxeles, sin botas ni barras internas.
        // Sentada, sus patas ya están en el dibujo. De pie en el cocodrilo, pisan su lomo.
        if (tub < 0.6 && !seated)
        {
            Foot(10, 28.6 - standLift, p.Feet == PetFeet.StepLeft, true);
            Foot(20, 28.6 - standLift, p.Feet == PetFeet.StepRight, true);
            Foot(7.5, standGround - standLift, p.Feet == PetFeet.StepRight, false);
            Foot(22, standGround - standLift, p.Feet == PetFeet.StepLeft, false);
        }

        // Muestreo inverso: el dibujo de rejilla se deforma a resolución física,
        // conservando la respiración, la inclinación y la mezcla de poses.
        for (int py = (int)Math.Floor((bottom - height * sy) * n); py < (int)Math.Ceiling(bottom * n); py++)
        for (int px = (int)Math.Floor((cx - 16 * sx) * n); px < (int)Math.Ceiling((cx + 15 * sx) * n); px++)
        {
            double row = ((py + 0.5) / n - bottom) / sy + height;
            // Al sacudirse, el pelo ondula de lado a lado a lo largo del cuerpo.
            double column = ((px + 0.5) / n - cx - Shift(row)) / sx + 12.5 - p.Shake * 0.8 * Math.Sin(row * 1.1);
            int iy = (int)Math.Floor(row), ix = (int)Math.Floor(column);
            if (iy < 0 || iy >= body.Length || ix < 0 || ix >= body[iy].Length) continue;
            uint color = body[iy][ix] switch { 'o' => Outline, 'f' => fur, 's' => shade, 'h' => light, _ => 0u };
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
        if (p.Bird > 0.02) Bird(seated ? Point(14.5, 7.6) : Point(16, 6.6));
        // Gotas que cuelgan de la barriga mientras sigue mojada.
        if (p.Wet > 0.05 && tub < 0.6)
            foreach (var (col, len) in new[] { (8.5, 0.9), (14.0, 1.3), (19.5, 1.0) })
            {
                var drip = Point(col, 16.6);
                c.Ellipse(drip.X * n, (drip.Y + len * p.Wet) * n, 0.35 * n, 0.5 * n, 0xFF86CDE2, Math.Clamp(p.Wet, 0, 1));
            }

        uint Damp(uint color)
        {
            uint C(int shift) => (uint)Math.Round((color >> shift & 255) * (1 - wet) + (0x5A3A22u >> shift & 255) * wet) << shift;
            return 0xFF000000 | C(16) | C(8) | C(0);
        }

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
