using IsTargetSleeping;

internal static class ShelfTests
{
    public static void Run(Action<string, bool> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "its-shelf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string a = Path.Combine(folder, "a.txt"), b = Path.Combine(folder, "b.txt"), dir = Path.Combine(folder, "carpeta");
            File.WriteAllText(a, "uno");
            File.WriteAllText(b, "dos");
            Directory.CreateDirectory(dir);

            var shelf = ShelfStore.Add([], [a, a.ToUpperInvariant(), dir, Path.Combine(folder, "no-existe.txt")]);
            check("estante: añade archivos y carpetas, sin repetir (ni por mayúsculas) y sin lo que no existe", shelf.SequenceEqual([a, dir]));
            shelf = ShelfStore.Add(shelf, [b]);
            check("estante: lo nuevo va al final", shelf.SequenceEqual([a, dir, b]));
            check("estante: quitar uno o varios deja el resto en su orden", ShelfStore.Remove(shelf, [dir]).SequenceEqual([a, b]) && ShelfStore.Remove(shelf, [a, b]).SequenceEqual([dir]));

            var file = Path.Combine(folder, "shelf.json");
            NotesStore.SaveAtomic(file, ShelfStore.Serialize(shelf));
            check("estante: guardar y leer devuelve las mismas rutas", ShelfStore.Parse(File.ReadAllText(file)).SequenceEqual(shelf));
            check("estante: un archivo roto, vacío o ajeno no da nada, sin fallar",
                ShelfStore.Parse("{ roto").Count == 0 && ShelfStore.Parse("").Count == 0 && ShelfStore.Parse("{}").Count == 0 && ShelfStore.Parse("{\"files\":[1,null,\"\"]}").Count == 0);

            File.Delete(b);
            check("estante: lo que desaparece se quita al revisar", ShelfStore.Prune(shelf).SequenceEqual([a, dir]));
            File.WriteAllText(b, "dos");

            check("estante: el nombre se corta con «…» a los 14 caracteres y el tamaño se escribe corto",
                ShelfStore.ShortName("isTargetSleeping.exe") == "isTargetSleepi…" && ShelfStore.ShortName("corto.txt") == "corto.txt"
                && ShelfStore.Size(2048) == "2 KB" && ShelfStore.Size(95_420_000) == "91 MB" && ShelfStore.Size(12) == "12 B");

            // El estado: seleccionar, quitar los marcados, vaciar y quitar uno.
            var list = new ShelfList();
            check("estante: empieza vacío", list.Files.Count == 0 && !list.Selecting);
            list.Add([a, dir, b]);
            list.ToggleSelect();
            list.Mark(a);
            list.Mark(b);
            check("estante: en «Elegir», pulsar un mosaico lo marca", list.Selecting && list.IsMarked(a) && !list.IsMarked(dir) && list.IsMarked(b));
            list.Mark(b);
            check("estante: pulsarlo otra vez lo desmarca", !list.IsMarked(b));
            list.Mark(b);
            list.RemoveMarked();
            check("estante: «Quitar» solo quita los marcados, sigue en «Elegir» y no borra los archivos",
                list.Files.SequenceEqual([dir]) && list.Selecting && File.Exists(a) && File.Exists(b));
            list.ToggleSelect();
            check("estante: «Listo» sale de la selección y no deja marcas", !list.Selecting && !list.IsMarked(a));
            list.Add([a, b]);
            list.Remove(a);
            check("estante: la ✕ de un mosaico quita solo ese", list.Files.SequenceEqual([dir, b]));
            File.Delete(b);
            check("estante: el archivo que desaparece se quita solo (y nada más se toca)", list.Prune() && list.Files.SequenceEqual([dir]) && !list.Prune());
            list.ToggleSelect();
            list.Clear();
            check("estante: «Vaciar todo» deja el estante vacío y sale de «Elegir»", list.Files.Count == 0 && !list.Selecting && Directory.Exists(dir));
            list.Load(shelf);
            check("estante: al cargar se descartan las rutas que ya no existen", list.Files.SequenceEqual([a, dir]));
        }
        finally { try { Directory.Delete(folder, true); } catch { } }
    }
}
