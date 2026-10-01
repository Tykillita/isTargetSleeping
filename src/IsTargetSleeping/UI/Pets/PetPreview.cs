using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IsTargetSleeping.UI.Pets;

/// Miniaturas estáticas: el mismo dibujo que la mascota real, sin temporizadores.
public static class PetPreview
{
    private static readonly Dictionary<string, BitmapSource> cache = [];
    public static BitmapSource For(IPetSpecies pet)
    {
        if (cache.TryGetValue(pet.Id, out var found)) return found;
        const int scale = 3;
        var c = new PixelCanvas(pet.Size.Width * scale, pet.Size.Height * scale);
        pet.Draw(c, pet.Animations.Still(PetActivity.Alert), scale);
        var image = BitmapSource.Create(c.Width, c.Height, 96, 96, PixelFormats.Pbgra32, null, c.ToBgra(), c.Width * 4);
        image.Freeze();
        cache[pet.Id] = image;
        return image;
    }
}
