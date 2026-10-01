using static IsTargetSleeping.L10n;
using static IsTargetSleeping.UI.Pets.AnimalDrawing;

namespace IsTargetSleeping.UI.Pets;

public sealed class OrangeCatPet : IPetSpecies
{
    public string Id => "orange-cat";
    public string Name => tr("Gatito naranja");
    public IPetAnimationProfile Animations => PetProfiles.OrangeCat;
    public PetSize Size { get; } = new(32, 30, 20, Reach: 26, HideRight: 22, HideMiddle: 22);
    public PetAnchors Anchors { get; } = new(14.5, 10, 14.5, 23, 14.5, 18, 29.5);
    public void Draw(PixelCanvas c, PetPose p, int n)
    {
        uint fur = Tint(0xFFF2A044, p.Tint), shade = Tint(0xFFC7692D, p.Tint), cream = Tint(0xFFFFE8BF, p.Tint), stripe = Tint(0xFF9F4925, p.Tint);
        double x = 14.5 + p.Lean * 0.6, s = Math.Clamp(p.Squash, -1.3, 1.3);
        double y = 18 + p.Bob + p.Sit * 3 - p.Stretch * 0.8;
        Bed(c, n, p.BedIn, false);
        // Cola articulada: se enrosca al dormir y oscila al jugar y caminar.
        double tx = x + 9 + p.Tail * 1.5, ty = 21 - p.Tail * 3 + p.Sit * 5;
        c.Line((x + 4.5) * n, 26 * n, (x + 9) * n, 25 * n, 3 * n, Ink);
        c.Line((x + 9) * n, 25 * n, tx * n, ty * n, 3 * n, Ink);
        c.Line((x + 4.5) * n, 26 * n, (x + 9) * n, 25 * n, 1.6 * n, fur);
        c.Line((x + 9) * n, 25 * n, tx * n, ty * n, 1.6 * n, fur);
        c.Line((tx - 0.2) * n, (ty + 0.8) * n, tx * n, ty * n, 1.5 * n, stripe);
        Feet(c, n, p, x, cream, shade);
        Oval(c, n, x, 24.5 + p.Bob + p.Sit * 0.4, 5.5 + s * 0.6 + p.Sit, 4 - s * 0.5, fur, shade, p.Lean);
        c.Ellipse(x * n, (25 + p.Bob) * n, 2.7 * n, 2.5 * n, cream);
        foreach (int side in new[] { -1, 1 })
        {
            double ear = side < 0 ? p.EarL : p.EarR;
            double tipX = x + side * (4.7 + ear), tipY = y - 6 + Math.Abs(ear) * 1.4;
            Triangle(c, n, (x + side * 1.7, y - 2.4), (x + side * 6, y - 2.4), (tipX, tipY), Ink);
            Triangle(c, n, (x + side * 2.5, y - 2.7), (x + side * 5.3, y - 2.7), (tipX, tipY + 1), fur);
            Triangle(c, n, (x + side * 3.1, y - 2.8), (x + side * 4.7, y - 2.8), (tipX, tipY + 1.7), Pink);
        }
        Oval(c, n, x, y, 6.2 + s * 0.2, 4.7 - s * 0.3, fur, shade, p.Lean);
        for (int i = -1; i <= 1; i++) c.Line((x + i * 1.8) * n, (y - 3.9) * n, (x + i * 1.4) * n, (y - 2.5) * n, n * 0.65, stripe);
        c.Ellipse((x - 1.3) * n, (y + 1.7) * n, 2 * n, 1.6 * n, cream);
        c.Ellipse((x + 1.3) * n, (y + 1.7) * n, 2 * n, 1.6 * n, cream);
        Eyes(c, n, p, x, y - 0.7, 2.8, 1.05);
        Triangle(c, n, (x - 0.7, y + 0.8), (x + 0.7, y + 0.8), (x, y + 1.6), Ink);
        Mouth(c, n, p, x, y + 2.4);
        foreach (int side in new[] { -1, 1 })
        {
            c.Line((x + side * 3.5) * n, (y + 1) * n, (x + side * 6.8) * n, (y + 0.4) * n, n * 0.4, Ink);
            c.Line((x + side * 3.5) * n, (y + 1.8) * n, (x + side * 6.8) * n, (y + 2.3) * n, n * 0.4, Ink);
        }
        Paw(c, n, x - 4.5, 23.5 + p.Bob, p.ArmL, -1, cream);
        var hand = Paw(c, n, x + 4.5, 23.5 + p.Bob, p.ArmR, 1, cream, p.OneArm);
        Props(c, n, p, x, y - 6, hand);
        Bed(c, n, p.BedIn, true);
    }
}
