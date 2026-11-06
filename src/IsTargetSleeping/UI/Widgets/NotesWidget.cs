using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// Una fila de la lista de notas: el título, y debajo la fecha y el principio del texto (como la lista de Notas de iOS). Tocarla abre la nota.
public sealed record NoteRow(string Id, string Title, string Preview, string Date, bool Selected, ICommand Open)
{
    public bool HasPreview => Preview.Length > 0;
    public string Meta => HasPreview ? $"{Date}  {Preview}" : $"{Date}  {tr("Sin texto adicional")}";
}

/// El widget Notas, a la manera de las Notas de iOS: varias notas (la primera línea es el título), una lista con título, fecha y vista previa, y un editor.
/// Se guardan en notes.json de la carpeta de datos (nunca salen del PC; el bloc único de antes, notes.txt, pasa a ser la primera nota). En las capturas no toca el disco.
public sealed class NotesWidget : WidgetModel
{
    private readonly bool persist;
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private List<NoteData> notes = [];
    private string current = "";
    private bool editing;
    private IReadOnlyList<NoteRow> rows = [];
    private bool saveAllowed = true;
    private NoteData? deleted;
    private string status = "";

    public NotesWidget(bool persist) : base(WidgetCatalog.Find("notes")!)
    {
        this.persist = persist;
        if (persist) Load();
        if (notes.Count == 0) notes.Add(Blank());
        if (notes.All(n => n.Id != current)) current = notes[0].Id;
        saveTimer.Tick += (_, _) => Save();
        NewNote = new Command(AddNote);
        StartEdit = new Command(() => { editing = true; Changed(); });
        Back = new Command(CloseEditor);
        Delete = new Command(DeleteCurrent);
        Next = new Command(() => Step(1));
        Previous = new Command(() => Step(-1));
        UndoDelete = new Command(RestoreDeleted);
        DismissStatus = new Command(ClearStatus);
        Rebuild();
    }

    public static string FilePath => Path.Combine(Paths.DataDir, "notes.json");
    private static string OldFilePath => Path.Combine(Paths.DataDir, "notes.txt");

    private static NoteData Blank() => new(Guid.NewGuid().ToString("N"), "", DateTime.Now);

    private void Load()
    {
        var loaded = NotesStore.Load(FilePath, OldFilePath, DateTime.Now);
        (notes, current) = (loaded.Notes, loaded.Current ?? "");
        saveAllowed = loaded.SaveAllowed;
        status = loaded.Issue switch
        {
            NotesLoadIssue.CorruptFileBackedUp => tr("No se pudieron leer las notas anteriores; conservé una copia en %@.", loaded.BackupPath ?? ""),
            NotesLoadIssue.CorruptFilePreserved => tr("No se pudieron leer las notas y no se guardarán cambios para proteger el archivo original."),
            NotesLoadIssue.ReadFailed => tr("No se pudieron leer las notas; no se guardarán cambios para proteger el archivo original."),
            _ => "",
        };
    }

    // MARK: lo que enseña

    public string Hint => tr("Escribe aquí: se guarda solo, en este PC.");
    public string HeaderTitle => tr("Notas");
    public IReadOnlyList<NoteRow> Rows => rows;
    public IReadOnlyList<NoteRow> Top3 => rows.Take(3).ToList();
    public IReadOnlyList<NoteRow> Top6 => rows.Take(6).ToList();
    public IReadOnlyList<NoteRow> Top9 => rows.Take(9).ToList();
    /// Hay algo escrito (una nota vacía no cuenta): para que la galería prefiera los datos de ejemplo mientras no.
    public bool HasContent => notes.Any(n => n.Text.Trim().Length > 0);
    public bool Editing => editing;
    public bool NotEditing => !editing;
    public string CountText => tr(notes.Count == 1 ? "1 nota" : "%d notas", notes.Count);
    public string StatusText => status;
    public bool HasStatus => status.Length > 0;
    public bool HasUndoDelete => deleted is not null;

    private NoteData Current => notes.First(n => n.Id == current);
    public string CurrentTitle => NotesStore.Title(Current.Text) is { Length: > 0 } t ? t : tr("Escribe aquí");
    public string CurrentPreview => NotesStore.Preview(Current.Text);
    public bool HasCurrentPreview => CurrentPreview.Length > 0;
    public string CurrentDate => NotesStore.DateLabel(Current.Updated, DateTime.Now);
    public bool CurrentEmpty => Current.Text.Trim().Length == 0;
    public bool CanCycle => notes.Count > 1;

    public string Text
    {
        get => Current.Text;
        set
        {
            var note = Current;
            if (value == note.Text) return;
            notes[notes.IndexOf(note)] = note with { Text = value, Updated = DateTime.Now };
            saveTimer.Stop();
            saveTimer.Start();
            Rebuild();
            foreach (var name in new[] { nameof(Rows), nameof(Top3), nameof(Top6), nameof(Top9), nameof(CurrentTitle), nameof(CurrentPreview), nameof(HasCurrentPreview), nameof(CurrentDate), nameof(CurrentEmpty), nameof(HasContent) })
                RaiseProperty(name);
        }
    }

    // MARK: acciones

    public ICommand NewNote { get; }
    public ICommand StartEdit { get; }
    public ICommand Back { get; }
    public ICommand Delete { get; }
    public ICommand Next { get; }
    public ICommand Previous { get; }
    public ICommand UndoDelete { get; }
    public ICommand DismissStatus { get; }
    public string NewHelp => tr("Nueva nota");
    public string EditHelp => CurrentEmpty ? Hint : tr("Editar la nota");
    public string BackHelp => tr("Volver a la lista");
    public string DeleteHelp => tr("Borrar la nota");
    public string NextHelp => tr("Nota siguiente");
    public string PreviousHelp => tr("Nota anterior");
    public string UndoDeleteLabel => tr("Deshacer");
    public string UndoDeleteHelp => tr("Recuperar la nota borrada");
    public string DismissStatusHelp => tr("Cerrar el aviso");
    public string EditorName => tr("Texto de la nota");

    private void AddNote()
    {
        // Una nota vacía ya a mano se reutiliza: no se apilan notas en blanco.
        if (notes.FirstOrDefault(n => n.Text.Trim().Length == 0) is { } blank) current = blank.Id;
        else
        {
            if (notes.Count >= NotesStore.MaxNotes)
            {
                ShowStatus(tr("Has llegado al máximo de %d notas.", NotesStore.MaxNotes));
                return;
            }
            var note = Blank();
            notes.Add(note);
            current = note.Id;
        }
        editing = true;
        Changed();
    }

    private void Open(string id) { current = id; editing = true; Changed(); }

    private void Step(int delta)
    {
        if (rows.Count < 2) return;
        var at = rows.ToList().FindIndex(r => r.Id == current);
        current = rows[((at < 0 ? 0 : at) + delta + rows.Count) % rows.Count].Id;
        Changed();
    }

    /// Al salir del editor, una nota que se quedó en blanco se tira (si hay otras).
    private void CloseEditor()
    {
        editing = false;
        DropIfBlank();
        Changed();
    }

    private void DropIfBlank()
    {
        if (notes.Count > 1 && Current.Text.Trim().Length == 0)
        {
            notes.Remove(Current);
            current = notes.OrderByDescending(n => n.Updated).First().Id;
        }
    }

    private void DeleteCurrent()
    {
        deleted = Current;
        notes.Remove(deleted);
        if (notes.Count == 0) notes.Add(Blank());
        current = notes.OrderByDescending(n => n.Updated).First().Id;
        editing = false;
        status = tr("Nota borrada.");
        Changed();
    }

    private void RestoreDeleted()
    {
        if (deleted is not { } note) return;
        if (notes.Count == 1 && notes[0].Text.Trim().Length == 0) notes.Clear();
        notes.Add(note);
        current = note.Id;
        deleted = null;
        editing = true;
        status = "";
        Changed();
    }

    private void ClearStatus()
    {
        status = "";
        deleted = null;
        Raise();
    }

    private void ShowStatus(string message)
    {
        status = message;
        Raise();
    }

    /// Cambió la selección, el modo o la lista: se rehace todo y se guarda.
    private void Changed()
    {
        Rebuild();
        saveTimer.Stop();
        Save();
        Raise();
    }

    private void Rebuild()
    {
        var now = DateTime.Now;
        rows = notes.OrderByDescending(n => n.Updated).Select(n =>
        {
            var id = n.Id;
            return new NoteRow(id, NotesStore.Title(n.Text) is { Length: > 0 } t ? t : tr("Escribe aquí"), NotesStore.Preview(n.Text), NotesStore.DateLabel(n.Updated, now), id == current, new Command(() => Open(id)));
        }).ToList();
    }

    public void Save()
    {
        saveTimer.Stop();
        if (!persist) return;
        if (!saveAllowed) return;
        try
        {
            NotesStore.SaveAtomic(FilePath, NotesStore.Serialize(notes, current));
        }
        catch (Exception e)
        {
            AppLog.Write($"notas: {e.Message}");
            status = tr("No se pudieron guardar las notas: %@", e.Message);
            Raise();
        }
    }

    /// Solo para la galería y las capturas: notas de ejemplo, sin tocar el disco.
    public void LoadDemo()
    {
        var now = DateTime.Now;
        string Note(string title, params string[] lines) => string.Join("\n", new[] { title }.Concat(lines));
        notes =
        [
            new("d1", Note(tr("Ideas para el viernes"), tr("Probar la galería de widgets"), tr("Revisar el notch a 125 %")), now.AddMinutes(-12)),
            new("d2", Note(tr("Lista de la compra"), tr("Leche, pan, huevos y café")), now.AddHours(-5)),
            new("d3", Note(tr("Reunión con el equipo"), tr("Repasar el diseño de los widgets y acordar la fecha de la próxima versión")), now.AddDays(-1)),
            new("d4", Note(tr("Libros por leer")), now.AddDays(-3)),
            new("d5", Note(tr("Contraseña del wifi de casa"), tr("Está en el cajón del escritorio")), now.AddDays(-9)),
            new("d6", Note(tr("Atajos útiles"), tr("Ctrl+Alt+I copia el texto de la pantalla")), now.AddDays(-20)),
        ];
        current = "d1";
        editing = false;
        Rebuild();
        Raise();
    }
}
