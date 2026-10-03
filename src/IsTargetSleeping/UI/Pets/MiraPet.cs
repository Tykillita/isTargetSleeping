using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI.Pets;

/// Mira, la mascota de la marca: el cuerpo es el anillo de la mira, las marcas de la
/// cruz son su antena, sus bracitos y sus patas, y el ojo es el del logo — cerrado con
/// pestañas mientras duerme y abierto, la diana, cuando hay un modelo despierto.
/// Dibujo híbrido: el cuerpo y el ojo a resolución real (se aplastan, respiran y
/// parpadean sin saltos) con un contorno de un píxel de rejilla, y las marcas y los
/// accesorios con aspecto de pixel art. Rejilla de 27×24: el cuerpo ocupa las 18 filas
/// de abajo y encima quedan las Z, los corazones y los destellos. Vigilante: un anillo de
/// radar sale de su ojo (`Scan`) y, con el ratón encima, fija una retícula (`Lock`). Para
/// limpiar, su ojo proyecta un haz de escáner (`Beam`) y compacta los datos en un cubito.
public sealed class MiraPet : IPetSpecies
{
    public string Id => "mira";
    public string Name => tr("Mira");
    public IPetAnimationProfile Animations => PetProfiles.Mira;
    public PetSize Size { get; } = new(27, 24, 18, Reach: 23, HideRight: 22, HideMiddle: 17);
    public PetAnchors Anchors { get; } = new(HeadX: Cx, HeadY: 6, CenterX: Cx, CenterY: BaseCy, EyeX: Cx, EyeY: BaseCy - 1, GroundY: 23.5,
        MouthX: Cx, MouthY: BaseCy + 3.4);

    private const double Cx = 13.5, BaseCy = 15.5;
    /// Las marcas de la cruz (antena, bracitos, patas): azul medio, que se ve sobre barra clara y oscura.
    private const uint Tick = 0xFF3A8EE8;
    private const uint Outline = 0xFF132C44, White = 0xFFF4F8FF, Pupil = 0xFF0B1A2A, Shine = 0xFFB9DCFF;
    private const uint Pink = 0xFFFF6FA0, MouthInside = 0xFFB23A5B, Blush = 0xFFFF8FB1;
    private const uint GrayLight = 0xFFC9D1DB, GrayDark = 0xFF4A5260, Cardboard = 0xFFC98A4B, Tape = 0xFFE8C170;
    private const uint Amber = 0xFFF5BD57, AmberDark = 0xFFC98A2E;
    private const uint Radar = 0xFF6CC0FF, Target = 0xFFFF4F5E;
    /// El escáner y lo que recoge: aguamarina, para que se distinga del cuerpo azul.
    private const uint Scanner = 0xFF7FF2E6, CubeTop = 0xFFB8FFF6, CubeSide = 0xFF3FBFB4, CubeData = 0xFF2F7FD6;

    private static (uint Fill, uint Shade) Body(PetTint tint) => tint switch
    {
        PetTint.Sleepy => (0xFF6E9CC8, 0xFF557FA8),
        PetTint.Warm => (0xFFF5BD57, 0xFFD9963A),
        PetTint.Danger => (0xFFFF7A70, 0xFFD9564E),
        _ => (0xFF4DA3FF, 0xFF2F7FD6),
    };

    public void Draw(PixelCanvas c, PetPose p, int n)
    {
        // Medidas en píxeles de rejilla; × n, físicos.
        double squash = Math.Clamp(p.Squash, -1.3, 1.3);
        double rx = 7.4 + 0.8 * squash, ry = 7.4 - 0.8 * squash;
        double cx = Cx, cy = BaseCy + 0.8 * squash + p.Bob + p.Sit;
        var (fill, shade) = Body(p.Tint);

        PetBeds.Draw(c, n, PetBed.Mira, p.BedIn, false);
        Feet(c, n, p, cy + ry);
        BodyShape(c, n, p, cx, cy, rx, ry, fill, shade);
        Eye(c, n, p, cx + p.Lean * 0.4, cy - 1, fill);
        if (p.Mouth > 0.05)
        {
            double my = (cy + 3.4) * n, mh = (0.35 + 0.75 * p.Mouth) * n;
            c.Ellipse(cx * n, my, 1.4 * n, mh, Outline);
            c.Ellipse(cx * n, my, 0.8 * n, Math.Max(0.5, mh - 0.6 * n), MouthInside);
            if (p.Mouth > 0.5) c.Ellipse(cx * n, my + mh * 0.35, 0.6 * n, mh * 0.35, Pink);
        }
        if (p.Blush > 0.02)
        {
            c.Ellipse((cx - 4.3) * n, (cy + 1.6) * n, 1.1 * n, 0.5 * n, Blush, p.Blush * 0.85);
            c.Ellipse((cx + 4.3) * n, (cy + 1.6) * n, 1.1 * n, 0.5 * n, Blush, p.Blush * 0.85);
        }

        // Antena (la marca de arriba): se queda atrás al moverse, con su muelle. La baliza
        // de la punta se enciende mientras escanea, emite o fija la retícula.
        double top = cy - ry;
        if (p.Prop != PetProp.Box || p.PropIn < 0.5)
        {
            double tipX = cx + p.Antenna * 1.6, tipY = top - 2.0;
            c.Line(cx * n, (top + 0.4) * n, tipX * n, tipY * n, n, Tick);
            double beacon = Math.Clamp(Math.Max(Math.Max(p.Beam, p.Lock), PetPose.Ring(p.Scan) * 1.5), 0, 1);
            c.Ellipse(tipX * n, tipY * n, 0.75 * n, 0.75 * n, Outline);
            c.Ellipse(tipX * n, tipY * n, 0.45 * n, 0.45 * n, beacon > 0.05 ? Radar : Tick);
            if (beacon > 0.05) c.Ellipse(tipX * n, tipY * n, 1.3 * n, 1.3 * n, Radar, beacon * 0.35);
        }

        // Bracitos (las marcas de los lados), girando cada uno a su ángulo.
        var right = Arm(c, n, cx + rx - 0.3, cy + 0.2, p.ArmR, 1, p.OneArm);
        Arm(c, n, cx - rx + 0.3, cy + 0.2, p.ArmL, -1, false);

        if (p.Beam > 0.01) Beam(c, n, cx + p.Lean * 0.4, cy - 1, p);
        Props(c, n, p, cx, cy, top, right);
        PetBeds.Draw(c, n, PetBed.Mira, p.BedIn, true);
        double ring = PetPose.Ring(p.Scan);
        if (ring > 0.01) Ring(c, n, cx + p.Lean * 0.4, cy - 1, 3.8 + p.Scan * 8.5, ring);
        if (p.Lock > 0.01) Reticle(c, n, cx + p.Lean * 0.4, cy - 1, Math.Clamp(p.Lock, 0, 1));
    }

    /// El haz de escáner: un cono tenue desde la pupila hasta el suelo, hacia donde mira, con
    /// un rayo nítido en el centro y una franja brillante donde toca.
    private static void Beam(PixelCanvas c, int n, double ex, double ey, PetPose p)
    {
        double beam = Math.Clamp(p.Beam, 0, 1);
        double sx = ex + Math.Clamp(p.LookX, -1, 1) * 1.1, sy = ey + Math.Clamp(p.LookY, -1, 1) * 0.9;
        double fx = ex + Math.Clamp(p.LookX, -1, 1) * 10, fy = 23.2, half = 2.2;
        for (int y = (int)Math.Floor(sy * n); y <= (int)Math.Ceiling(fy * n); y++)
        {
            double k = (y + 0.5 - sy * n) / ((fy - sy) * n);
            if (k is < 0 or > 1) continue;
            double mid = (sx + (fx - sx) * k) * n, w = (0.3 + (half - 0.3) * k) * n;
            for (int x = (int)Math.Floor(mid - w); x <= (int)Math.Ceiling(mid + w); x++)
                if (Math.Abs(x + 0.5 - mid) <= w) c.Set(x, y, Scanner, beam * (0.1 + 0.2 * k));
        }
        c.Line(sx * n, sy * n, fx * n, fy * n, 0.5 * n, Scanner, beam * 0.8);
        c.Line((fx - half) * n, fy * n, (fx + half) * n, fy * n, 0.8 * n, Scanner, beam);
        c.Line((fx - half * 0.4) * n, fy * n, (fx + half * 0.4) * n, fy * n, 0.8 * n, White, beam);
    }

    /// El anillo de radar: un círculo fino que crece desde el ojo y se apaga.
    private static void Ring(PixelCanvas c, int n, double ex, double ey, double radius, double alpha)
    {
        double R = radius * n, X = ex * n, Y = ey * n, half = 0.4 * n;
        for (int y = (int)Math.Floor(Y - R - half); y <= (int)Math.Ceiling(Y + R + half); y++)
            for (int x = (int)Math.Floor(X - R - half); x <= (int)Math.Ceiling(X + R + half); x++)
            {
                double d = Math.Sqrt(Math.Pow(x + 0.5 - X, 2) + Math.Pow(y + 0.5 - Y, 2));
                if (Math.Abs(d - R) <= half) c.Set(x, y, Radar, alpha * 0.9);
            }
    }

    /// «Objetivo fijado»: cuatro esquinas rojas que se cierran sobre el ojo.
    private static void Reticle(PixelCanvas c, int n, double ex, double ey, double lockIn)
    {
        double r = 6.2 - 1.6 * lockIn, arm = 1.6, w = 0.7 * n;
        foreach (int sx in new[] { -1, 1 })
        foreach (int sy in new[] { -1, 1 })
        {
            double x = ex + sx * r, y = ey + sy * r;
            c.Line(x * n, y * n, (x - sx * arm) * n, y * n, w, Target, lockIn);
            c.Line(x * n, y * n, x * n, (y - sy * arm) * n, w, Target, lockIn);
        }
    }

    private static void BodyShape(PixelCanvas c, int n, PetPose p, double cx, double cy, double rx, double ry, uint fill, uint shade)
    {
        double ox = cx * n, oy = cy * n, RX = rx * n, RY = ry * n, inner = 1.0 * n;
        for (int y = (int)Math.Floor(oy - RY) - 1; y <= (int)Math.Ceiling(oy + RY) + 1; y++)
            for (int x = (int)Math.Floor(ox - RX) - n; x <= (int)Math.Ceiling(ox + RX) + n; x++)
            {
                double py = y + 0.5, px = x + 0.5 - p.Lean * (py - oy) * 0.18;   // inclinada al caminar
                double u = (px - ox) / RX, v = (py - oy) / RY;
                if (u * u + v * v > 1) continue;
                double ui = (px - ox) / (RX - inner), vi = (py - oy) / (RY - inner);
                bool edge = ui * ui + vi * vi > 1;
                bool lower = (px - ox) / n * 0.55 + (py - oy) / n > ry * 0.62;
                c.Set(x, y, edge ? Outline : lower ? shade : fill);
            }
        // Brillo arriba a la izquierda.
        c.Ellipse((cx - 4.1) * n, (cy - 4.3) * n, 1.0 * n, 0.65 * n, Shine);
    }

    /// El ojo del logo a resolución real: un párpado que baja como una curva desde arriba
    /// hasta quedar en el arco de abajo (la U del ojo cerrado), la pupila que se desliza
    /// y, casi cerrado, las pestañas.
    private static void Eye(PixelCanvas c, int n, PetPose p, double ex, double ey, uint lid)
    {
        double R = 3.5 * n, X = ex * n, Y = ey * n, line = n * 0.5;
        double open = Math.Clamp(p.Eye, 0, 1);
        // Al bajar el párpado la pupila baja con él y asoma por debajo: así un ojo
        // entrecerrado se lee como ojo con sueño (y no como una sonrisa blanca).
        double pupilX = X + Math.Clamp(p.LookX, -1, 1) * 1.1 * n;
        double pupilY = Y + Math.Clamp(p.LookY, -1, 1) * 0.9 * n + (1 - open) * 0.6 * R;
        for (int y = (int)Math.Floor(Y - R); y <= (int)Math.Ceiling(Y + R); y++)
            for (int x = (int)Math.Floor(X - R); x <= (int)Math.Ceiling(X + R); x++)
            {
                double dx = x + 0.5 - X, dy = y + 0.5 - Y;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d > R) continue;
                double half = Math.Sqrt(Math.Max(0, R * R - dx * dx));
                double edge = (Y - half) + (1 - open) * 2 * half;   // el borde del párpado en esta columna
                double py = y + 0.5;
                if (py < edge - line) { c.Set(x, y, lid); continue; }                 // piel del párpado
                if (py <= edge + line || d > R - n) { c.Set(x, y, Outline); continue; } // borde del párpado y del ojo
                double qx = x + 0.5 - pupilX, qy = y + 0.5 - pupilY;
                double hx = qx + 0.45 * n, hy = qy + 0.45 * n;
                c.Set(x, y, hx * hx + hy * hy < 0.2 * n * n ? Shine : qx * qx + qy * qy < 1.6 * n * n ? Pupil : White);
            }
        if (open < 0.35)
        {
            // Las pestañas, debajo del arco (sin ellas, el ojo cerrado parece una sonrisa).
            double alpha = 1 - open / 0.35;
            foreach (var angle in new[] { -42.0, 0, 42 })
            {
                double a = angle * Math.PI / 180, sx = X + R * Math.Sin(a), sy = Y + R * Math.Cos(a);
                c.Line(sx, sy, sx + 1.3 * n * Math.Sin(a), sy + 1.3 * n * Math.Cos(a), n * 0.9, Outline, alpha);
            }
        }
    }

    /// Un bracito desde el hombro, la marca de la cruz con contorno y la mano redonda:
    /// `side` −1 izquierda, 1 derecha. Devuelve dónde acaba la mano.
    private static (double X, double Y) Arm(PixelCanvas c, int n, double sx, double sy, double angle, int side, bool hidden)
    {
        double a = angle * Math.PI / 180, length = Math.Abs(angle) > 100 ? 3.6 : angle < -60 ? 3.0 : 2.6;
        double hx = sx + side * Math.Cos(a) * length, hy = sy + Math.Sin(a) * length;
        if (!hidden)
        {
            c.Line(sx * n, sy * n, hx * n, hy * n, 2.2 * n, Outline);
            c.Ellipse(hx * n, hy * n, 1.15 * n, 1.15 * n, Outline);
            c.Line(sx * n, sy * n, hx * n, hy * n, 1.1 * n, Tick);
            c.Ellipse(hx * n, hy * n, 0.6 * n, 0.6 * n, Tick);
        }
        return (hx, hy);
    }

    private static void Feet(PixelCanvas c, int n, PetPose p, double bottom)
    {
        if (p.Sit >= 0.95) return;
        // Se recogen bajo el cuerpo al sentarse (y el cuerpo las tapa); el que da el paso se levanta.
        double y = 23 - p.Sit * 2;
        double left = p.Feet == PetFeet.StepLeft ? y - 1 : y, right = p.Feet == PetFeet.StepRight ? y - 1 : y;
        foreach (var (fx, fy) in new[] { (Cx - 2.6, left), (Cx + 2.6, right) })
        {
            c.Ellipse(fx * n, fy * n, 1.7 * n, 1.05 * n, Outline);
            c.Ellipse(fx * n, (fy - 0.1) * n, 1.05 * n, 0.5 * n, Tick);
        }
    }

    private static void Props(PixelCanvas c, int n, PetPose p, double cx, double cy, double top, (double X, double Y) hand)
    {
        double into = Math.Clamp(p.PropIn, 0, 1);
        if (into <= 0.01) return;
        switch (p.Prop)
        {
            case PetProp.Laptop:
                // Un portátil diminuto delante, visto por detrás (con el punto azul de la marca); sube desde abajo.
                c.Stamp((cx - 4.5) * n, (18.4 + (1 - into) * 6) * n, [
                    ".ooooooo.",
                    ".ogggggo.",
                    ".oggbggo.",
                    ".ogggggo.",
                    "okkkkkkko",
                ], k => k switch { 'g' => GrayLight, 'b' => 0xFF4DA3FF, 'k' => GrayDark, _ => Outline }, n, into);
                break;
            case PetProp.Box:
            {
                // La caja baja hasta la cabeza y se apoya en ella (los brazos en alto la sujetan).
                c.Stamp((cx - 3.5) * n, (top - 5.0 - (1 - into) * 4) * n, [
                    "ooooooo",
                    "obbybbo",
                    "obbybbo",
                    "obbbbbo",
                    "ooooooo",
                ], k => k switch { 'b' => Cardboard, 'y' => Tape, _ => Outline }, n, into);
                break;
            }
            case PetProp.Crumb:
                // Un bocado de datos, ámbar como lo que cambia, en la mano levantada.
                c.Stamp((hand.X - 2) * n, (hand.Y - 3.2) * n, [
                    ".ooo.",
                    "oyAyo",
                    "oAyyo",
                    ".ooo.",
                ], k => k switch { 'y' => Amber, 'A' => AmberDark, _ => Outline }, n, into);
                break;
            case PetProp.Cube:
            {
                // El cubito de datos compactados, flotando delante con su halo: crece con
                // `PropIn` (de un punto al cubo entero) y al compactarse encoge hasta desaparecer.
                string[] cube = into switch
                {
                    < 0.4 => ["ooo", "owo", "ooo"],
                    < 0.75 => ["ooooo", "ottto", "ofbso", "offso", "ooooo"],
                    _ => ["ooooooo", "ottttto", "offffso", "ofbbfso", "ofbbfso", "offffso", "ooooooo"],
                };
                double side = cube.Length, bottom = 22.4;
                c.Ellipse(cx * n, (bottom - side / 2) * n, (side * 0.75 + 0.8) * n, (side * 0.75 + 0.8) * n, Scanner, 0.3 * into);
                c.Stamp((cx - side / 2) * n, (bottom - side) * n, cube, k => k switch
                {
                    't' => CubeTop, 'f' or 'w' => White, 's' => CubeSide, 'b' => CubeData, _ => Outline,
                }, n);
                break;
            }
        }
    }
}
