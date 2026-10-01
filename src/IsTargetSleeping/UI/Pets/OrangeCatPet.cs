using static IsTargetSleeping.L10n;
using static IsTargetSleeping.UI.Pets.AnimalDrawing;

namespace IsTargetSleeping.UI.Pets;

/// El gatito naranja, atigrado: pupilas rasgadas que se dilatan (`Pupil`), orejas que se
/// giran y se aplanan, una cola que habla (la punta se agita; erizada con `Arch`), el lomo
/// arqueado con el pelo de punta, enroscado al dormir con la cola alrededor (`Curl`), la
/// lengua al acicalarse, y se sienta encima del portátil o dentro de la caja.
public sealed class OrangeCatPet : IPetSpecies
{
    public string Id => "orange-cat";
    public string Name => tr("Gatito naranja");
    public IPetAnimationProfile Animations => PetProfiles.OrangeCat;
    public PetSize Size { get; } = new(32, 30, 20, Reach: 30, HideRight: 22, HideMiddle: 22, CenterX: 14.5);
    public PetAnchors Anchors { get; } = new(14.5, 10, 14.5, 23, 14.5, 17, 29.5, MouthX: 14.5, MouthY: 20.2);

    private const uint Iris = 0xFFB9CF4A, Tongue = 0xFFF07F8C, Screen = 0xFF8EC9FF;

    public void Draw(PixelCanvas c, PetPose p, int n)
    {
        uint fur = Tint(0xFFF2A34E, p.Tint), shade = Tint(0xFFCE7939, p.Tint), cream = Tint(0xFFFFE9C5, p.Tint), stripe = Tint(0xFFA75930, p.Tint);
        double s = Math.Clamp(p.Squash, -1.3, 1.3), curl = Math.Clamp(p.Curl, 0, 1), arch = Math.Clamp(p.Arch, 0, 1);
        double prop = Math.Clamp(p.PropIn, 0, 1);
        double onLaptop = p.Prop == PetProp.Laptop ? prop : 0, inBox = p.Prop == PetProp.Box ? prop : 0;
        double x = 14.5 + p.Lean * 0.6;
        // Encima del portátil va más alta; dentro de la caja, más hundida; enroscada en la
        // cesta, asoma por encima del borde.
        double lift = -onLaptop * 1.6 + inBox * 1.2 - curl * 2;
        double bodyY = 24.5 + p.Bob + p.Sit * 0.4 - arch * 1.2 + lift;
        double rx = 5.2 + s * 0.5 + p.Sit * 0.8 + curl * 1.8, ry = 4 - s * 0.4 - curl * 0.3;
        // Enroscada, la cabeza descansa sobre el cuerpo, a un lado.
        // Arqueada, agacha la cabeza y el lomo erizado asoma por encima.
        double hx = x - curl * 3.6, y = 17.6 + p.Bob + p.Sit * 3 - p.Stretch * 0.8 + arch * 2.2 + lift + curl * 1.2;

        PetBeds.Draw(c, n, PetBed.OrangeCat, p.BedIn, false);
        if (onLaptop > 0.01) LaptopBack(onLaptop);
        if (inBox > 0.01) BoxBack(inBox);
        if (curl < 0.5) Tail();
        if (onLaptop > 0.01) LaptopBase(onLaptop);
        if (onLaptop < 0.5 && inBox < 0.5) Feet(c, n, p, x, cream, shade);

        // El cuerpo; erizada, el contorno se llena de puntas y el lomo sube en joroba.
        double top = bodyY - ry;
        bool Fluffy(double px, double py, double cx, double cy, double ax, double ay)
        {
            double dx = (px - cx) / ax, dy = (py - cy) / ay, d = Math.Sqrt(dx * dx + dy * dy);
            if (arch < 0.05) return d <= 1;
            double w = Math.Atan2(dy, dx) * 13 / (2 * Math.PI), f = Math.Abs(w - Math.Round(w));
            return d <= 1 + arch * 0.4 * (0.5 - f);
        }
        bool Body(double px, double py) => Fluffy(px, py, x, bodyY, rx, ry)
            || arch > 0.02 && Fluffy(px, py, x, bodyY - 3.5 * arch, 3.2 + 4 * arch, 3.8 * arch);
        Silhouette(c, n, x - rx * 1.25 - 1, top - 6, x + rx * 1.25 + 1, bodyY + ry * 1.25 + 1, Body,
            (px, py) => py - bodyY + (px - x) * 0.35 > ry * 0.5 ? shade : fur);
        c.Ellipse(x * n, (bodyY + 0.5) * n, 2.5 * n, 2.4 * n, cream);
        foreach (double sx in new[] { -1.6, 0.0, 1.6 })
            c.Line((x + sx) * n, (top + 0.7) * n, (x + sx + 0.4) * n, (top + 1.9) * n, 0.6 * n, stripe);
        if (curl >= 0.5) Tail();

        Ears();
        // Mejillas anchas y barbilla crema; rayas de tabby en M, mejillas y cola.
        Oval(c, n, hx, y, 6.4 + s * 0.2, 4.6 - s * 0.3, fur, shade, p.Lean);
        c.Ellipse(hx * n, (y + 2.3) * n, 3.2 * n, 1.65 * n, cream);
        c.Line((hx - 2.5) * n, (y - 3.6) * n, (hx - 1.5) * n, (y - 2.2) * n, 0.65 * n, stripe);
        c.Line((hx - 1.5) * n, (y - 2.2) * n, hx * n, (y - 3.4) * n, 0.65 * n, stripe);
        c.Line(hx * n, (y - 3.4) * n, (hx + 1.5) * n, (y - 2.2) * n, 0.65 * n, stripe);
        c.Line((hx + 1.5) * n, (y - 2.2) * n, (hx + 2.5) * n, (y - 3.6) * n, 0.65 * n, stripe);
        CatEyes(hx, y - 0.4);
        Triangle(c, n, (hx - 0.7, y + 1), (hx + 0.7, y + 1), (hx, y + 1.7), Ink);
        if (p.Mouth >= 0.5) Mouth(c, n, p, hx, y + 2.6);
        else
        {
            c.Line(hx * n, (y + 1.7) * n, hx * n, (y + 2.3) * n, 0.45 * n, Ink);
            c.Line(hx * n, (y + 2.3) * n, (hx - 0.9) * n, (y + 2.7) * n, 0.45 * n, Ink);
            c.Line(hx * n, (y + 2.3) * n, (hx + 0.9) * n, (y + 2.7) * n, 0.45 * n, Ink);
            // Acicalándose: asoma la lengua.
            if (p.Mouth > 0.1) c.Ellipse(hx * n, (y + 2.9 + p.Mouth) * n, 0.6 * n, (0.35 + p.Mouth) * n, Tongue);
            if (p.Blush > 0)
            {
                c.Ellipse((hx - 4) * n, (y + 1.5) * n, 1 * n, 0.5 * n, Pink, p.Blush);
                c.Ellipse((hx + 4) * n, (y + 1.5) * n, 1 * n, 0.5 * n, Pink, p.Blush);
            }
        }
        foreach (int side in new[] { -1, 1 })
        {
            c.Line((hx + side * 5.4) * n, (y - 0.3) * n, (hx + side * 4.2) * n, (y + 0.1) * n, 0.6 * n, stripe);
            c.Line((hx + side * 5.1) * n, (y + 1) * n, (hx + side * 4.2) * n, (y + 1.1) * n, 0.6 * n, stripe);
            c.Line((hx + side * 3.5) * n, (y + 1.4) * n, (hx + side * 7.4) * n, (y + 0.9) * n, 0.4 * n, Ink);
            c.Line((hx + side * 3.7) * n, (y + 2.1) * n, (hx + side * 7) * n, (y + 2.9) * n, 0.4 * n, Ink);
        }
        if (inBox > 0.01) BoxFront(inBox);
        // Enroscada, las patas quedan recogidas.
        (double X, double Y) hand = (x + 6, bodyY);
        if (curl < 0.5)
        {
            Paw(c, n, x - 4.4, bodyY - 1, p.ArmL, -1, cream);
            hand = Paw(c, n, x + 4.4, bodyY - 1, p.ArmR, 1, cream, p.OneArm);
        }
        if (p.Prop is not (PetProp.Laptop or PetProp.Box)) Props(c, n, p, x, y - 6.8, hand);
        PetBeds.Draw(c, n, PetBed.OrangeCat, p.BedIn, true);

        void Ears()
        {
            foreach (int side in new[] { -1, 1 })
            {
                // Erguidas hacia delante, o aplanadas hacia fuera («en avión»).
                double ear = Math.Clamp(side < 0 ? p.EarL : p.EarR, -1, 1);
                double tipX = hx + side * (4.8 + (ear < 0 ? -ear * 1.8 : ear * 0.4)), tipY = y - 6.5 + (ear < 0 ? -ear * 2.8 : -ear * 0.4);
                Triangle(c, n, (hx + side * 1.8, y - 2.1), (hx + side * 6.1, y - 1.9), (tipX, tipY), Ink);
                Triangle(c, n, (hx + side * 2.5, y - 2.2), (hx + side * 5.4, y - 2.2), (tipX, tipY + 1), fur);
                Triangle(c, n, (hx + side * 3.2, y - 2.6), (hx + side * 4.9, y - 2.5), (tipX, tipY + 1.9), 0xFFD98F85);
            }
        }

        // Iris verde amarillento con la pupila de rendija a redonda; cerrados, dos arcos.
        void CatEyes(double ex0, double ey)
        {
            double open = Math.Clamp(p.Eye, 0, 1), pupil = Math.Clamp(p.Pupil, 0, 1);
            foreach (int side in new[] { -1, 1 })
            {
                double ex = ex0 + side * 2.7;
                if (open < 0.2)
                {
                    c.Line((ex - 1.2) * n, ey * n, ex * n, (ey + 0.5) * n, n * 0.65, Ink);
                    c.Line(ex * n, (ey + 0.5) * n, (ex + 1.2) * n, ey * n, n * 0.65, Ink);
                    continue;
                }
                double ry = 0.3 + open * 1.05, lx = Math.Clamp(p.LookX, -1, 1) * 0.35, ly = Math.Clamp(p.LookY, -1, 1) * 0.3;
                c.Ellipse(ex * n, ey * n, 1.3 * n, ry * n, Ink);
                if (ry > 0.75) c.Ellipse(ex * n, ey * n, 0.85 * n, (ry - 0.45) * n, Iris);
                c.Ellipse((ex + lx) * n, (ey + ly) * n, (0.22 + pupil * 0.6) * n, Math.Max(0.3, ry - 0.3) * n, Ink);
                if (open > 0.65) c.Ellipse((ex + lx - 0.35) * n, (ey + ly - 0.35) * n, 0.3 * n, 0.3 * n, 0xFFFFF4E7);
            }
        }

        // La cola: normal se curva hacia arriba; erizada, tiesa y gorda; enroscada, rodea el cuerpo.
        void Tail()
        {
            double wave = Math.Clamp(p.Tail, -1.2, 1.2);
            (double X, double Y) Point(double t)
            {
                double xx = x + 4.4 + 8 * Math.Sin(t * Math.PI / 2) + wave * 0.7 * t + wave * 1.6 * Math.Max(0, t - 0.6);
                double yy = bodyY + 1.9 - 8 * t + 1.9 * t * t;
                xx -= Math.Max(0, t - 0.7) * 7;
                (double X, double Y) up = (x + 5.2 + wave * 1.2 * t * t, bodyY + 1 - 10.5 * t);
                (double X, double Y) wrap = (x + rx * Math.Cos(0.1 * Math.PI + t * 0.85 * Math.PI) * 1.02,
                    bodyY + 0.4 + Math.Sin(0.1 * Math.PI + t * 0.85 * Math.PI) * (ry * 0.55) - t * 0.8);
                xx += (up.X - xx) * arch;
                yy += (up.Y - yy) * arch;
                return (xx + (wrap.X - xx) * curl, yy + (wrap.Y - yy) * curl);
            }
            double fat = 1 + arch * 0.9;
            for (int layer = 0; layer < 2; layer++)
            for (int i = 0; i < 12; i++)
            {
                var a = Point(i / 12.0); var b = Point((i + 1) / 12.0);
                c.Line(a.X * n, a.Y * n, b.X * n, b.Y * n, (layer == 0 ? 3.1 : 1.8) * fat * n, layer == 0 ? Ink : fur);
            }
            foreach (double t in new[] { 0.45, 0.7, 0.94 })
            {
                var a = Point(t - 0.025); var b = Point(t + 0.025);
                c.Line(a.X * n, a.Y * n, b.X * n, b.Y * n, 1.9 * fat * n, stripe);
            }
        }

        // El portátil abierto detrás (la pantalla asoma por los lados) y el teclado debajo.
        void LaptopBack(double a)
        {
            double drop = (1 - a) * 4;
            RoundedBox(c, n, x - 7.5, 17.5 + drop, x + 7.5, 26 + drop, 0.8, 0xFF596574, Ink, a);
            RoundedBox(c, n, x - 6.5, 18.5 + drop, x + 6.5, 25 + drop, 0.4, Screen, 0xFF596574, a);
        }
        void LaptopBase(double a)
        {
            double drop = (1 - a) * 4;
            RoundedBox(c, n, x - 8.5, 26 + drop, x + 8.5, 29 + drop, 0.8, 0xFFBFC9D6, Ink, a);
            c.Line((x - 7) * n, (27.4 + drop) * n, (x + 7) * n, (27.4 + drop) * n, 0.6 * n, 0xFF8C97A6, a);
        }
        // Dentro de la caja: el fondo detrás y la pared delantera con las solapas abiertas.
        void BoxBack(double a)
        {
            double drop = (1 - a) * 5;
            RoundedBox(c, n, x - 7, 21.5 + drop, x + 7, 29.5 + drop, 0.5, 0xFF8E5C2E, Ink, a);
        }
        void BoxFront(double a)
        {
            double drop = (1 - a) * 5;
            foreach (int side in new[] { -1, 1 })
            {
                Triangle(c, n, (x + side * 7, 23.2 + drop), (x + side * 10.2, 20.3 + drop), (x + side * 7.3, 19.6 + drop), Ink, a);
                Triangle(c, n, (x + side * 7.4, 22.6 + drop), (x + side * 9.4, 20.6 + drop), (x + side * 7.6, 20.3 + drop), 0xFFB57A40, a);
            }
            RoundedBox(c, n, x - 7.5, 23 + drop, x + 7.5, 29.5 + drop, 0.5, 0xFFC98A4B, Ink, a);
            c.Line(x * n, (23.5 + drop) * n, x * n, (26 + drop) * n, 1.2 * n, 0xFFE8C170, a);
        }
    }
}
