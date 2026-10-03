using static IsTargetSleeping.L10n;
using static IsTargetSleeping.UI.Pets.AnimalDrawing;

namespace IsTargetSleeping.UI.Pets;

/// La llama, orgullosa: pestañas largas y copete, un cuello que se estira e inclina hacia
/// lo que le interesa (`Stretch`, `Lean`), la mandíbula que rumia de lado (`Jaw`), una cola
/// lanuda que se levanta y se agita (`Tail`), las patas recogidas al sentarse y la caja con
/// su manta tejida en el lomo (`Saddle`). Sin brazos: lo que coge, lo lleva en el hocico.
public sealed class LlamaPet : IPetSpecies
{
    public string Id => "llama";
    public string Name => tr("Llama");
    public IPetAnimationProfile Animations => PetProfiles.Llama;
    public PetSize Size { get; } = new(32, 30, 22, Reach: 29, HideRight: 21, HideMiddle: 21, CenterX: 14.5);
    public PetAnchors Anchors { get; } = new(14.5, 8, 14.5, 22, 14.5, 14.5, 29.5, MouthX: 14.5, MouthY: 18.5);

    private const uint Lash = 0xFF2A2124;

    public void Draw(PixelCanvas c, PetPose p, int n)
    {
        uint wool = Tint(0xFFF8EEDB, p.Tint), shade = Tint(0xFFD5C5AA, p.Tint), muzzle = Tint(0xFFE9D1AA, p.Tint);
        uint hoof = Tint(0xFF8B6B55, p.Tint);
        double x = 14.5 + p.Lean * 0.6, s = Math.Clamp(p.Squash, -1.3, 1.3);
        double head = 14.8 + p.Bob + p.Sit * 4.2 - p.Stretch * 2 + s * 0.4;
        double body = 23.5 + p.Bob + p.Sit * 0.6;
        // El cuello se inclina hacia donde mira: la cabeza va más lejos que el cuerpo.
        double hx = x + p.Lean * 1.4 - p.Shake * 0.5, jaw = Math.Clamp(p.Jaw, -1, 1) * 0.7;
        PetBeds.Draw(c, n, PetBed.Llama, p.BedIn, false);
        Feet(c, n, p, x, hoof, hoof);
        // Una cola lanuda corta (se levanta con `Tail` > 0 y cae con < 0), un cuello largo y
        // un cuerpo de bordes esponjosos.
        double tail = Math.Clamp(p.Tail, -1, 1), angle = (-47 - 38 * tail) * Math.PI / 180, reach = 3.4 + 0.3 * Math.Abs(tail);
        double tipX = x + 5 + Math.Cos(angle) * reach, tipY = body + Math.Sin(angle) * reach;
        c.Line((x + 5) * n, body * n, tipX * n, tipY * n, 2.4 * n, Ink);
        c.Line((x + 5) * n, body * n, tipX * n, tipY * n, 1.2 * n, wool);
        double Neck(double py) => hx + (x - hx) * Math.Clamp((py - head) / Math.Max(1, body - head), 0, 1);
        // Al sacudirse, la lana del cuerpo ondula de lado a lado.
        double Wave(double py) => p.Shake * 0.9 * Math.Sin(py * 1.4);
        bool Shape(double px, double py) =>
            InOval(px - (py > head + 3 ? Wave(py) : 0), py, x, body, 6.4 + s * 0.4, 4.7 - s * 0.3)
            || InOval(px, py, x - 4.8, body - 1.8, 2.1, 2.5)
            || InOval(px, py, x + 4.7, body - 1.7, 2.1, 2.6)
            || py >= head + 1 && py <= body + 3.1 && Math.Abs(px - Neck(py)) <= 3.2;
        Silhouette(c, n, x - 9, head - 1, x + 9, body + 5.5, Shape,
            (px, py) => px > Neck(py) + 2.2 && py > head + 4 || py > body + 2.6 ? shade : wool);
        // Sentada (kush), las patas delanteras asoman recogidas bajo el pecho.
        if (p.Sit > 0.6)
            foreach (int side in new[] { -1, 1 })
                Oval(c, n, x + side * 2.6, body + 4.1, 1.7, 0.9, hoof, hoof);
        if (p.Prop == PetProp.Saddle) Saddle(c, n, p, x, body);
        foreach (int side in new[] { -1, 1 })
        {
            double ear = side < 0 ? p.EarL : p.EarR;
            double ex = hx + side * 2.8, ey = head - 2.7;
            double tx = ex + side * (0.7 + ear * 1.1) + p.Antenna * 0.7, ty = ey - 4.8 + Math.Abs(ear) * 0.7;
            c.Line(ex * n, ey * n, tx * n, ty * n, 2.7 * n, Ink);
            c.Line(ex * n, ey * n, tx * n, (ty + 0.5) * n, 1.5 * n, wool);
            c.Line(ex * n, (ey - 0.5) * n, tx * n, (ty + 1) * n, 0.6 * n, 0xFFC98D88);
        }
        // Copete de lana y mejillas: una sola curva, sin círculos apilados en la cara.
        bool Face(double px, double py) => InOval(px, py, hx, head, 4.9 + s * 0.2, 4.2 - s * 0.2)
            || InOval(px, py, hx - 2.1, head - 3.3, 1.8, 1.4)
            || InOval(px, py, hx + 0.2, head - 3.9, 1.9, 1.6)
            || InOval(px, py, hx + 2.4, head - 3.2, 1.7, 1.4)
            || InOval(px, py, hx - 0.8, head - 4.8, 1.3, 1.1);
        Silhouette(c, n, hx - 6, head - 7, hx + 6, head + 5, Face,
            (px, py) => py > head + 2 || px > hx + 3.2 ? shade : wool);
        c.Line((hx - 1.8) * n, (head - 3.1) * n, (hx - 0.8) * n, (head - 3.6) * n, 0.5 * n, shade);
        c.Line((hx + 0.2) * n, (head - 4.6) * n, (hx + 1.2) * n, (head - 4.1) * n, 0.5 * n, shade);
        // Hocico largo y suave, rasgo que la separa de un conejo; rumia moviéndolo de lado.
        RoundedBox(c, n, hx - 2.4 + jaw * 0.5, head + 0.5, hx + 2.4 + jaw * 0.5, head + 5.4, 1.8, muzzle, shade);
        Eyes(c, n, p, hx, head - 0.1, 2.8, 0.8);
        Lashes(c, n, p, hx, head - 0.1);
        c.Ellipse((hx + jaw * 0.3) * n, (head + 2.3) * n, 0.85 * n, 0.55 * n, Ink);
        c.Line((hx + jaw * 0.3) * n, (head + 2.6) * n, (hx + jaw) * n, (head + 3.3) * n, 0.45 * n, Ink);
        Mouth(c, n, p, hx + jaw, head + 3.7);
        // Mechones y pezuñas aportan textura sin llenar de ruido el dibujo pequeño.
        c.Line((x - 4.6) * n, (body - 0.6) * n, (x - 3.7) * n, body * n, 0.5 * n, shade);
        c.Line((x + 3.4) * n, (body + 0.2) * n, (x + 4.3) * n, (body - 0.3) * n, 0.5 * n, shade);
        // Sin brazos: el bocado va en el hocico y el portátil, en el suelo delante de las pezuñas.
        Props(c, n, p, x, head - 7.6, (hx + jaw + 0.3, head + 4.9));
        PetBeds.Draw(c, n, PetBed.Llama, p.BedIn, true);
    }

    /// Pestañas largas: dos por ojo hacia fuera, también con el ojo cerrado.
    private static void Lashes(PixelCanvas c, int n, PetPose p, double x, double y)
    {
        double lift = p.Eye < 0.2 ? 0.2 : -0.35 - Math.Clamp(p.Eye, 0, 1) * 0.6;
        foreach (int side in new[] { -1, 1 })
        {
            double ex = x + side * 2.8;
            c.Line((ex + side * 0.6) * n, (y + lift) * n, (ex + side * 1.6) * n, (y + lift - 0.7) * n, 0.45 * n, Lash);
            c.Line((ex + side * 0.1) * n, (y + lift - 0.2) * n, (ex + side * 0.6) * n, (y + lift - 1.0) * n, 0.45 * n, Lash);
        }
    }

    /// La carga: una manta tejida sobre la cruz y la caja atada en el lomo (baja al cargarla).
    private static void Saddle(PixelCanvas c, int n, PetPose p, double x, double body)
    {
        double a = Math.Clamp(p.PropIn, 0, 1);
        if (a <= 0.01) return;
        const uint red = 0xFFC2463E, teal = 0xFF2F8C8A, gold = 0xFFEFC47D;
        RoundedBox(c, n, x - 6.2, body - 3.6, x + 6.2, body - 0.4, 1.2, red, Ink, a);
        c.Line((x - 5.4) * n, (body - 2.4) * n, (x + 5.4) * n, (body - 2.4) * n, 0.6 * n, gold, a);
        for (double tx = x - 4.5; tx <= x + 4.6; tx += 1.8)
            c.Stamp(tx * n, (body - 1.6) * n, ["t"], _ => teal, n, a);
        foreach (double tx in new[] { x - 6, x - 3.8, x + 3.8, x + 6 })
            c.Line(tx * n, (body - 0.6) * n, tx * n, (body + 0.6) * n, 0.5 * n, gold, a);
        double top = body - 9 - (1 - a) * 4;
        c.Stamp((x + 2.5) * n, top * n, ["oooooooo", "obbbybbo", "obbbybbo", "obbbybbo", "obbbbbbo", "oooooooo"],
            k => k switch { 'b' => 0xFFC98A4B, 'y' => 0xFFE8C170, _ => Ink }, n, a);
    }
}
