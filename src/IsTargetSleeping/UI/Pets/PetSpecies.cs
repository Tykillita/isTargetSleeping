namespace IsTargetSleeping.UI.Pets;

/// Una mascota de la colección: cómo se dibuja cada pose (`PetPose`) y su personalidad.
/// Comparte las señales del sistema y las partículas; cada especie elige sus animaciones.
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
