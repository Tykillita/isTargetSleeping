using static IsTargetSleeping.L10n;
using static IsTargetSleeping.UI.Pets.AnimalDrawing;

namespace IsTargetSleeping.UI.Pets;

public sealed class CapybaraPet : IPetSpecies
{
    public string Id => "capybara";
    public string Name => tr("Capibara");
    public IPetAnimationProfile Animations => PetProfiles.Capybara;
    public PetSize Size { get; } = new(32, 30, 18, Reach: 26, HideRight: 24, HideMiddle: 22);
    public PetAnchors Anchors { get; } = new(14.5, 12, 14.5, 22, 14.5, 18.5, 29.5);
    public void Draw(PixelCanvas c, PetPose p, int n)
    {
        uint fur = Tint(0xFFBB885A, p.Tint), shade = Tint(0xFF8D5F3E, p.Tint), snout = Tint(0xFFD2A779, p.Tint);
        double x = 14.5 + p.Lean * 0.5, s = Math.Clamp(p.Squash, -1.3, 1.3);
        double y = 19.5 + p.Bob + p.Sit * 2.3 - p.Stretch * 0.6;
        Bed(c, n, p.BedIn, false);
        Feet(c, n, p, x, fur, shade);
        Oval(c, n, x, 23.5 + p.Bob + p.Sit * 0.5, 8 + s * 0.4, 5 - s * 0.4, fur, shade, p.Lean);
        foreach (int side in new[] { -1, 1 })
        {
            double ear = side < 0 ? p.EarL : p.EarR;
            Oval(c, n, x + side * (4.8 + ear * 0.3), y - 4.6 + ear * 0.4, 1.7, 1.6, fur, shade);
            c.Ellipse((x + side * 4.8) * n, (y - 4.6 + ear * 0.4) * n, n * 0.75, n * 0.75, snout);
        }
        Oval(c, n, x, y, 7.4 + s * 0.2, 5.8 - s * 0.3, fur, shade, p.Lean);
        // El hocico ancho y casi rectangular es la silueta distintiva del capibara.
        Oval(c, n, x + 0.3, y + 2.1, 4.6, 2.8, snout, shade);
        Eyes(c, n, p, x, y - 1.2, 3.9, 0.9);
        c.Ellipse((x - 1.3) * n, (y + 1.5) * n, n * 0.5, n * 0.45, Ink);
        c.Ellipse((x + 1.3) * n, (y + 1.5) * n, n * 0.5, n * 0.45, Ink);
        Mouth(c, n, p, x, y + 3.2);
        Paw(c, n, x - 6.4, 23.3 + p.Bob, p.ArmL, -1, fur);
        var hand = Paw(c, n, x + 6.4, 23.3 + p.Bob, p.ArmR, 1, fur, p.OneArm);
        Props(c, n, p, x, y - 5.8, hand);
        Bed(c, n, p.BedIn, true);
    }
}
