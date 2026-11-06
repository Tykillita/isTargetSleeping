using System.Globalization;
using System.IO;
using System.Windows.Input;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// Una fila de la lista de tareas: el círculo (marcar), el título (en el modo editar abre el formulario) y, en el modo editar, la ✕ roja que la borra.
public sealed record TodoRow(string Id, string Title, string Time, bool Done, bool Daily, bool EditMode, ICommand Toggle, ICommand Open, ICommand Remove)
{
    public bool HasTime => Time.Length > 0;
    public string Meta => Daily && HasTime ? $"{Time} · {tr("Diaria")}" : Daily ? tr("Diaria") : Time;
    public bool HasMeta => Meta.Length > 0;
    public string RemoveHelp => $"{tr("Borrar la tarea")}: {Title}";
}

/// Una casilla del calendario del widget Tareas: el día, si es del mes que se ve, el de hoy, el que se está mirando y si tiene tareas.
public sealed record TodoDay(DateOnly Date, bool InMonth, bool IsToday, bool Selected, bool HasTasks, ICommand Select)
{
    public string Text => Date.Day.ToString(CultureInfo.InvariantCulture);
    public string Help => Date.ToString("D", Culture());
    private static CultureInfo Culture() => Effective == "es" ? new CultureInfo("es") : new CultureInfo("en");
}

/// El widget Tareas (TodoList): una lista con contador de «hechas hoy», formulario para añadir o editar, repetición diaria y un calendario del mes:
/// elegir un día enseña sus tareas y, si es futuro, lo que se añade queda para ese día. Se guarda en tasks.json de la carpeta de datos; en las capturas y la galería no toca el disco.
public sealed class TodoWidget : WidgetModel
{
    private readonly bool persist;
    private List<TodoItem> items = [];
    private IReadOnlyList<TodoRow> rows = [];
    private IReadOnlyList<TodoDay> days = [];
    private DateOnly day = DateOnly.FromDateTime(DateTime.Now);
    private DateOnly viewDay = DateOnly.FromDateTime(DateTime.Now);
    private DateOnly month = new(DateTime.Now.Year, DateTime.Now.Month, 1);
    private bool editMode, formOpen, calendarOpen, saveAllowed = true;
    private string? editingId;
    private string formTitle = "", formTime = "";
    private bool formDaily, wideHover;

    public TodoWidget(bool persist) : base(WidgetCatalog.Find("todo")!)
    {
        this.persist = persist;
        if (persist) (items, saveAllowed) = TodoStore.Load(FilePath, DateTime.Now);
        AddTask = new Command(() => { if (formOpen && editingId is null) CloseForm(); else OpenForm(null); });
        EditTasks = new Command(() => { if (!HasTasks && !editMode) return; editMode = !editMode; formOpen = false; calendarOpen = false; Changed(); });
        ToggleCalendar = new Command(() => { calendarOpen = !calendarOpen; formOpen = false; editMode = false; if (calendarOpen) month = new(viewDay.Year, viewDay.Month, 1); Changed(); });
        PrevMonth = new Command(() => { month = month.AddMonths(-1); Changed(); });
        NextMonth = new Command(() => { month = month.AddMonths(1); Changed(); });
        GoToday = new Command(() => SelectDay(day));
        ToggleWide = new Command(() => WideHover = !WideHover);
        Cancel = new Command(CloseForm);
        Save = new Command(SaveForm);
        ClearTitle = new Command(() => { FormTitle = ""; });
        ToggleDaily = new Command(() => { formDaily = !formDaily; Raise(); });
        Rebuild();
    }

    public static string FilePath => Path.Combine(Paths.DataDir, "tasks.json");
    private static CultureInfo Culture => Effective == "es" ? new CultureInfo("es") : new CultureInfo("en");

    // MARK: lo que enseña

    public IReadOnlyList<TodoRow> Rows => rows;
    public IReadOnlyList<TodoDay> Days => days;
    public int DoneCount { get; private set; }
    public int TotalCount { get; private set; }
    public string CountText => $"{DoneCount}/{TotalCount}";
    /// «Tareas hechas hoy» o, mirando otro día del calendario, ese día.
    public string DoneLabel => viewDay == day ? tr("Tareas hechas hoy") : viewDay.ToString("ddd d MMM", Culture);
    public string MonthLabel => month.ToString("MMMM yyyy", Culture);
    public bool HasTasks => TotalCount > 0;
    public bool NoTasks => !HasTasks;
    public bool HasContent => items.Count > 0;
    public bool ShowList => !formOpen && !calendarOpen;
    public bool ShowForm => formOpen;
    public bool ShowCalendar => calendarOpen && !formOpen;
    public bool EditMode => editMode;
    public bool AddActive => formOpen && editingId is null;
    public bool EditActive => editMode;
    public bool EditEnabled => HasTasks || editMode;
    public bool CalendarActive => calendarOpen || viewDay != day;
    public bool FormDaily => formDaily;
    public bool TitleEmpty => formTitle.Length == 0;
    public bool TitleFilled => formTitle.Length > 0;
    public bool TimeEmpty => formTime.Length == 0;
    public bool CanSave => TodoStore.CleanTitle(formTitle).Length > 0;

    /// La vista corta se ensancha sobre la columna de la izquierda (lo pone el clic en la tarjeta; aquí se puede forzar, p. ej. para las capturas).
    public bool WideHover { get => wideHover; set { if (value == wideHover) return; wideHover = value; RaiseProperty(nameof(WideHover)); } }

    public string FormTitle
    {
        get => formTitle;
        set { if (value == formTitle) return; formTitle = value; Raise(); }
    }

    /// La hora se enmascara al escribir («0900» → «09:00»).
    public string FormTime
    {
        get => formTime;
        set { var masked = TodoStore.MaskTime(value); if (masked == formTime) { if (value != masked) Raise(); return; } formTime = masked; Raise(); }
    }

    // MARK: acciones y textos

    public ICommand AddTask { get; }
    public ICommand EditTasks { get; }
    /// El botón del calendario: abre o cierra el mes en la tarjeta.
    public ICommand ToggleCalendar { get; }
    public ICommand PrevMonth { get; }
    public ICommand NextMonth { get; }
    public ICommand GoToday { get; }
    /// Un clic en la tarjeta de tareas la ensancha (los botones se hacen redondos) y otro la devuelve; también se recoge al sacar el ratón del widget.
    public ICommand ToggleWide { get; }
    public ICommand Cancel { get; }
    public ICommand Save { get; }
    public ICommand ClearTitle { get; }
    public ICommand ToggleDaily { get; }

    public string AddLabel => tr("Añadir");
    public string EditLabel => tr("Editar");
    public string CancelLabel => tr("Cancelar");
    public string SaveLabel => tr("Guardar");
    public string DailyLabel => tr("Diaria");
    public string DailyHelp => tr("Repetir cada día");
    public string TitlePlaceholder => tr("Título de la tarea");
    public string TitleName => tr("Título de la tarea");
    public string TimePlaceholder => "__:__ - __:__";
    public string TimeName => tr("Hora de la tarea");
    public string ClearHelp => tr("Borrar el texto");
    public string EmptyText => tr("Sin tareas.");
    public string CalendarHelp => tr("Calendario de tareas");
    public string PrevMonthHelp => tr("Mes anterior");
    public string NextMonthHelp => tr("Mes siguiente");
    public string TodayHelp => tr("Volver a hoy");

    private void SelectDay(DateOnly date)
    {
        viewDay = date;
        month = new(date.Year, date.Month, 1);
        calendarOpen = false;
        editMode = false;
        Changed();
    }

    private void OpenForm(string? id)
    {
        editingId = id;
        var item = id is null ? null : items.FirstOrDefault(i => i.Id == id);
        (formTitle, formTime, formDaily) = item is null ? ("", "", false) : (item.Title, item.Time, item.Daily);
        formOpen = true;
        calendarOpen = false;
        editMode = false;   // como en Nusa: abrir el formulario (añadir o editar una fila) sale del modo editar
        Changed();
    }

    private void CloseForm() { formOpen = false; editingId = null; Changed(); }

    private void SaveForm()
    {
        var title = TodoStore.CleanTitle(formTitle);
        if (title.Length == 0) return;
        var time = TodoStore.CleanTime(formTime);
        if (editingId is { } id && items.FindIndex(i => i.Id == id) is var at and >= 0) items[at] = items[at] with { Title = title, Time = time, Daily = formDaily };
        else if (items.Count < TodoStore.MaxTasks) items.Add(new(Guid.NewGuid().ToString("N"), title, time, formDaily, null, viewDay > day ? viewDay : day));
        CloseForm();
    }

    private void Remove(string id)
    {
        items.RemoveAll(i => i.Id == id);
        Changed();
    }

    /// Solo se marca lo de hoy: los demás días del calendario son para mirar (o para preparar los futuros).
    private void Toggle(string id)
    {
        if (viewDay != day) return;
        if (items.FindIndex(i => i.Id == id) is var at and >= 0) items[at] = TodoStore.Toggle(items[at], day);
        Changed();
    }

    private void Changed() { Rebuild(); Persist(); Raise(); }

    private void Rebuild()
    {
        var list = TodoStore.ForDay(items, viewDay, day);
        (DoneCount, TotalCount) = TodoStore.Count(list, viewDay, day);
        rows = list.Select(i =>
        {
            var id = i.Id;
            return new TodoRow(id, i.Title, i.Time, TodoStore.IsDoneOn(i, viewDay, day), i.Daily, editMode, new Command(() => Toggle(id)),
                new Command(() => { if (editMode) OpenForm(id); }), new Command(() => Remove(id)));
        }).ToList();
        if (rows.Count == 0) editMode = false;
        days = TodoStore.MonthGrid(month, Culture.DateTimeFormat.FirstDayOfWeek).Select(d => new TodoDay(d, d.Month == month.Month, d == day, d == viewDay,
            TodoStore.ForDay(items, d, day).Count > 0, new Command(() => SelectDay(d)))).ToList();
    }

    private void Persist()
    {
        if (!persist || !saveAllowed) return;
        try { NotesStore.SaveAtomic(FilePath, TodoStore.Serialize(items)); }
        catch (Exception e) { AppLog.Write($"tareas: {e.Message}"); }
    }

    /// Con el reloj del Home: al cambiar de día las diarias reaparecen sin marcar y el contador de hoy vuelve a cero.
    public override void Tick(DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        if (today == day) return;
        if (viewDay == day) viewDay = today;
        day = today;
        Rebuild();
        Raise();
    }

    /// Solo para la galería y las capturas: tareas de ejemplo, sin tocar el disco.
    public void LoadDemo()
    {
        items =
        [
            new("d1", tr("Revisar el diseño de los widgets"), "", false, null),
            new("d2", tr("Responder los correos pendientes"), "09:00 - 10:00", true, null),
            new("d3", tr("Preparar la demo del viernes"), "", false, null),
            new("d4", tr("Salir a caminar"), "", true, day),
        ];
        Changed();
    }
}
