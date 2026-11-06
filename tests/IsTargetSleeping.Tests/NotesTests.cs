using IsTargetSleeping;

internal static class NotesTests
{
    public static void Run(Action<string, bool> check)
    {
        var now = new DateTime(2026, 10, 21, 17, 0, 0);
        check("título: la primera línea con texto, recortada", NotesStore.Title("\n\n  Compra  \nleche\npan") == "Compra" && NotesStore.Title("   ") == "" && NotesStore.Title(new string('a', 100)).Length == 81);
        check("vista previa: lo que sigue al título en una línea", NotesStore.Preview("Compra\nleche\n\n  pan ") == "leche pan" && NotesStore.Preview("Solo título") == "");
        check("fecha: hoy la hora, ayer, el día de la semana y luego la fecha",
            NotesStore.DateLabel(now.AddHours(-3), now) == "14:00" && NotesStore.DateLabel(now.AddDays(-1), now) == "Ayer"
            && NotesStore.DateLabel(now.AddDays(-3), now) is { Length: > 0 } week && !week.Any(char.IsDigit) && NotesStore.DateLabel(now.AddDays(-30), now) is var old && old.StartsWith("21 "));
        var notes = new List<NoteData> { new("a", "Uno\ncon \"comillas\" y\nsaltos", now), new("b", "", now.AddDays(-2)) };
        var (back, current) = NotesStore.Parse(NotesStore.Serialize(notes, "b"));
        check("guardar y leer: las notas, su texto y la elegida vuelven iguales", back.Select(n => (n.Id, n.Text)).SequenceEqual(notes.Select(n => (n.Id, n.Text))) && current == "b" && back[0].Updated == now);
        check("un archivo roto o vacío no da notas, sin fallar", NotesStore.Parse("{ roto").Notes.Count == 0 && NotesStore.Parse("{}").Notes.Count == 0);
        check("ids repetidos o vacíos se descartan", NotesStore.Parse("{\"notes\":[{\"id\":\"x\",\"text\":\"1\"},{\"id\":\"x\",\"text\":\"2\"},{\"id\":\"\",\"text\":\"3\"}]}").Notes.Count == 1);
        check("el bloc único de antes pasa a ser la primera nota; vacío no crea ninguna",
            NotesStore.Migrate("Ideas\nuna", now) is [{ Text: "Ideas\nuna" }] && NotesStore.Migrate("  ", now).Count == 0 && NotesStore.Migrate(null, now).Count == 0);
        var folder = Path.Combine(Path.GetTempPath(), "its-notes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "notes.json");
            var oldFile = Path.Combine(folder, "notes.txt");
            const string corrupt = "{ notas truncadas";
            File.WriteAllText(file, corrupt);
            var recovered = NotesStore.Load(file, oldFile, now);
            var backup = Directory.GetFiles(folder, "notes.json.corrupt-*").SingleOrDefault();
            check("notas: un JSON dañado se respalda intacto antes de permitir guardar una lista nueva",
                recovered.Issue == NotesLoadIssue.CorruptFileBackedUp && recovered.SaveAllowed && File.ReadAllText(file) == corrupt
                && backup is not null && recovered.BackupPath == backup && File.ReadAllText(backup) == corrupt);
            File.WriteAllText(file, corrupt);
            File.WriteAllText(file + ".corrupt-" + now.ToString("yyyyMMdd-HHmmss-fffffff", System.Globalization.CultureInfo.InvariantCulture), "backup ya existe");
            var protectedFile = NotesStore.Load(file, oldFile, now);
            check("notas: si no puede respaldar el JSON dañado, bloquea el guardado y conserva el original",
                protectedFile.Issue == NotesLoadIssue.CorruptFilePreserved && !protectedFile.SaveAllowed && File.ReadAllText(file) == corrupt);
            var json = NotesStore.Serialize(notes, "b");
            NotesStore.SaveAtomic(file, json);
            NotesStore.SaveAtomic(file, NotesStore.Serialize(notes, "a"));
            check("notas: el guardado atómico reemplaza el archivo completo y limpia su temporal",
                NotesStore.Parse(File.ReadAllText(file)).Current == "a" && Directory.GetFiles(folder, "*.tmp").Length == 0);
            check("notas: un esquema JSON inválido se distingue de una lista vacía válida",
                !NotesStore.TryParse("{}", out _, out _) && NotesStore.TryParse("{\"notes\":[]}", out var empty, out _) && empty.Count == 0);
        }
        finally { try { Directory.Delete(folder, true); } catch { } }
        check("las notas caben en una, dos y cuatro celdas y siguen siendo de dos por defecto",
            WidgetLayout.Sizes("notes").SequenceEqual([(1, 1), (2, 1), (2, 2), (1, 2)]) && WidgetLayout.Span("notes") == 2 && WidgetLayout.Span("notes@1") == 1
            && WidgetLayout.Normalize(["notes@1"], max: 12, home: true).SequenceEqual(["notes@1"]));
    }
}
