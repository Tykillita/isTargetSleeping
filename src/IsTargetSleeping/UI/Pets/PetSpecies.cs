namespace IsTargetSleeping.UI.Pets;

/// Una mascota de la colección: cómo se dibuja cada pose (`PetPose`). Las poses, las
/// animaciones y las partículas son las mismas para todas, así que una especie nueva
/// solo dibuja.
public interface IPetSpecies
{
    /// Se guarda en los ajustes (`taskbarPetSpecies`): no cambiarlo nunca.
    string Id { get; }
    string Name { get; }
    IPetAnimationProfile Animations { get; }
    /// La rejilla de diseño y las filas de abajo que ocupa el cuerpo (encima van los efectos).
    PetSize Size { get; }
    /// Dónde salen las partículas, en la rejilla.
    PetAnchors Anchors { get; }
    /// Dibuja la pose en un lienzo de `Size` × `scale` píxeles físicos.
    void Draw(PixelCanvas canvas, PetPose pose, int scale);
}

/// Las mascotas disponibles. Para añadir una: una clase con `IPetSpecies` y una línea
/// aquí; el selector de Ajustes y del menú aparece en cuanto hay más de una.
public static class PetCatalog
{
    public static IReadOnlyList<IPetSpecies> All { get; } = [new MiraPet(), new LlamaPet(), new CapybaraPet(), new OrangeCatPet()];

    public static IPetSpecies Find(string? id) => All.FirstOrDefault(s => s.Id == id) ?? All[0];
}
