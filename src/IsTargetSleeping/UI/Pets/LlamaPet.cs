using static IsTargetSleeping.L10n;
using static IsTargetSleeping.UI.Pets.AnimalDrawing;

namespace IsTargetSleeping.UI.Pets;

public sealed class LlamaPet : IPetSpecies
{
    public string Id => "llama";
    public string Name => tr("Llama");
    public IPetAnimationProfile Animations => PetProfiles.Llama;
    public PetSize Size { get; } = new(32, 30, 22, Reach: 24, HideRight: 21, HideMiddle: 21);
    public PetAnchors Anchors { get; } = new(14.5, 8, 14.5, 22, 14.5, 15.5, 29.5);
    public void Draw(PixelCanvas c, PetPose p, int n)
    {
        uint wool = Tint(0xFFF0EADB, p.Tint), shade = Tint(0xFFC6BBA9, p.Tint), muzzle = Tint(0xFFE0CDAE, p.Tint);
        double x = 14.5 + p.Lean * 0.6, s = Math.Clamp(p.Squash, -1.3, 1.3);
        double head = 15.5 + p.Bob + p.Sit * 4 - p.Stretch * 1.4 + s * 0.4;
        Bed(c, n, p.BedIn, false);
        Feet(c, n, p, x, wool, shade);
        Oval(c, n, x + 5.8, 23 + p.Bob, 2.2, 1.4, wool, shade);
        Oval(c, n, x, 23.4 + p.Bob + p.Sit, 6 + s * 0.5, 4.8 - s * 0.4, wool, shade, p.Lean);
        Oval(c, n, x, head + 5.3, 2.8, 5.2 + p.Stretch * 0.5, wool, shade);
        // Orejas largas, inclinadas de forma independiente; el muelle deja atrás las puntas.
        foreach (int side in new[] { -1, 1 })
        {
            double ear = side < 0 ? p.EarL : p.EarR;
            double ex = x + side * 2.7, ey = head - 3;
            double tx = ex + side * (0.4 + ear * 1.2) + p.Antenna * 0.7, ty = ey - 3.8 + Math.Abs(ear) * 0.6;
            c.Line(ex * n, ey * n, tx * n, ty * n, n * 2.8, Ink);
            c.Line(ex * n, ey * n, tx * n, ty * n, n * 1.5, wool);
            c.Line(ex * n, ey * n, tx * n, (ty + 0.5) * n, n * 0.65, Pink);
        }
        Oval(c, n, x, head, 4.8, 4.4, wool, shade, p.Lean);
        for (int i = -1; i <= 1; i++) c.Ellipse((x + i * 1.6) * n, (head - 3.5) * n, n * 1.2, n * 0.75, wool);
        Oval(c, n, x, head + 2.3, 2.9, 2.4, muzzle, shade);
        Eyes(c, n, p, x, head - 0.2, 2.6, 0.85);
        c.Ellipse(x * n, (head + 1.6) * n, n * 0.8, n * 0.5, Ink);
        Mouth(c, n, p, x, head + 3);
        Paw(c, n, x - 4.8, 22.5 + p.Bob, p.ArmL, -1, wool);
        var hand = Paw(c, n, x + 4.8, 22.5 + p.Bob, p.ArmR, 1, wool, p.OneArm);
        Props(c, n, p, x, head - 6.8, hand);
        Bed(c, n, p.BedIn, true);
    }
}
