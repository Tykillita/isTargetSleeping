using System.Globalization;
using System.IO;
using System.Windows.Input;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// Un día de la cuadrícula del mes: el hoy va en rojo, los que tienen algo llevan un punto y el elegido un aro.
public sealed record CalendarDay(string Text, bool Today, bool InMonth, DateOnly Date, bool HasItems, bool Selected, ICommand Pick, string Spoken)
{
    public double Alpha => InMonth ? 1 : 0.35;
}

/// Un recordatorio o un evento en la lista de la agenda.
public sealed record AgendaRow(string Title, string TimeText, bool IsReminder, bool Done, ICommand Toggle, ICommand Remove, string RemoveHelp, string ToggleHelp)
{
    public bool IsEvent => !IsReminder;
    public double TitleAlpha => Done ? 0.5 : 1;
}

/// Los de un mismo día, con su cabecera («HOY (SEMANA 42)», «JUEVES, 27 OCT (SEMANA 43)»).
public sealed record AgendaGroupRow(string Header, List<AgendaRow> Items);

/// La hora, el mes con su navegación y la agenda, con el idioma de la app (no el de Windows). Los recordatorios son locales (se guardan en
/// los ajustes de la app) y los eventos pueden venir de un archivo .ics que elijas tú; no hay cuentas ni red.
public sealed class ClockWidget : WidgetModel
{
    private const int MaxLocal = 200, MaxRows = 12;
    private readonly CultureInfo culture = new(Effective);
    private readonly Prefs prefs;
    private DateTime now = DateTime.Now, nextIcsCheck;
    private bool dirty = true;
    private int monthOffset;
    private DateOnly? selected;
    private readonly List<AgendaItem> local = [];
    private List<AgendaItem> ics = [];
    private (string Path, DateTime Stamp) icsSource;
    private int newOffset;
    private string newTitle = "", newTime = "", hint = "";

    public ClockWidget(Prefs prefs) : base(WidgetCatalog.Find("clock")!)
    {
        this.prefs = prefs;
        foreach (var line in prefs.List(PrefKeys.CalendarItems)) if (Agenda.Parse(line) is { } item) local.Add(item);
        PrevMonth = new Command(() => Move(-1));
        NextMonth = new Command(() => Move(1));
        ThisMonth = new Command(() => { monthOffset = 0; selected = null; Rebuild(); });
        ToggleAdder = new Command(OpenAdder);
        Add = new Command(AddItem);
        PickIcs = new Command(ChooseIcs);
        ClearIcs = new Command(() => { prefs.SetList(PrefKeys.CalendarIcs, []); ics = []; icsSource = default; Rebuild(); });
    }

    public string Time { get; private set; } = "";
    public string DateText { get; private set; } = "";
    public string MonthTitle { get; private set; } = "";
    public bool NotThisMonth => monthOffset != 0 || selected is not null;
    public List<string> Weekdays { get; private set; } = [];
    public List<CalendarDay> Days { get; private set; } = [];
    public List<AgendaGroupRow> AgendaGroups { get; private set; } = [];
    public bool HasAgenda => AgendaGroups.Count > 0;
    public bool NoAgenda => !HasAgenda;
    /// Lo próximo, en una línea, para las vistas pequeñas.
    public string NextText { get; private set; } = "";
    public string NextTitle { get; private set; } = "";
    public string NextWhen { get; private set; } = "";
    public bool HasNext { get; private set; }
    public ICommand PrevMonth { get; }
    public ICommand NextMonth { get; }
    public ICommand ThisMonth { get; }
    public ICommand ToggleAdder { get; }
    public ICommand Add { get; }
    public ICommand PickIcs { get; }
    public ICommand ClearIcs { get; }
    public string PrevHelp => tr("Mes anterior");
    public string NextHelp => tr("Mes siguiente");
    public string TodayHelp => tr("Volver a hoy");
    public string AddHelp => tr("Añadir un recordatorio");
    public string AddSend => tr("Añadir");
    public string DoneLabel => tr("Hecho");
    public string TitleHint => tr("Nuevo recordatorio");
    public string TimeHelp => tr("Hora (por ejemplo, 21:30)");
    public string EmptyText => tr("Nada pendiente. Pulsa + para añadir un recordatorio.");
    public string RulerHelp => tr("Arrastra para elegir el día");

    // MARK: archivo .ics (Ajustes > Widgets)
    public string IcsText => icsSource.Path.Length > 0 ? icsSource.Path : tr("Ninguno");
    public bool HasIcs => icsSource.Path.Length > 0;
    public string IcsTitle => tr("Calendario (archivo .ics)");
    public string IcsDetail => tr("Muestra los eventos de un archivo .ics (Google Calendar, Outlook, Apple…) junto a tus recordatorios. Se lee en este PC: no se conecta a ninguna cuenta ni sube nada.");
    public string IcsChoose => tr("Elegir archivo…");
    public string IcsRemove => tr("Quitar");

    // MARK: añadir un recordatorio
    public bool AdderOpen { get; private set; }
    public string NewTitle { get => newTitle; set { newTitle = value; hint = ""; RaiseProperty(nameof(Hint)); } }
    public string NewTime { get => newTime; set { newTime = value; hint = ""; RaiseProperty(nameof(Hint)); } }
    public int NewOffset { get => newOffset; set { if (newOffset == value) return; newOffset = value; RaiseProperty(nameof(NewOffset)); RaiseProperty(nameof(NewDayText)); } }
    public string Hint => hint;
    public bool HasHint => hint.Length > 0;
    public string NewDayText => NewOffset switch
    {
        0 => tr("Hoy"), 1 => tr("Mañana"),
        _ => DateTime.Today.AddDays(NewOffset).ToString("dddd, d MMM", culture),
    };

    public override void Tick(DateTime at)
    {
        now = at;
        Time = at.ToString("t", culture);
        var today = DateOnly.FromDateTime(at);
        if (dirty || today != DateOnly.FromDateTime(lastBuilt)) Rebuild();
        else if (at >= nextIcsCheck) { LoadIcs(); if (dirty) Rebuild(); }
        Raise();
    }

    private DateTime lastBuilt = DateTime.MinValue;

    private void Move(int by) { monthOffset += by; selected = null; Rebuild(); }

    private DateOnly Today => DateOnly.FromDateTime(now);

    /// Vuelve a leer el archivo .ics si cambió (o si cambió el día: la ventana de eventos se desplaza con él).
    private void LoadIcs()
    {
        nextIcsCheck = now.AddSeconds(30);
        var path = prefs.StoredList(PrefKeys.CalendarIcs)?.FirstOrDefault() ?? "";
        DateTime stamp = default;
        try { if (path.Length > 0 && File.Exists(path)) stamp = File.GetLastWriteTime(path); } catch (IOException) { }
        if (path == icsSource.Path && stamp == icsSource.Stamp && !dirty) return;
        icsSource = (path, stamp);
        try
        {
            // Hasta 1 MB: un calendario normal cabe de sobra, y un archivo enorme no debe congelar el notch.
            ics = stamp != default && new FileInfo(path).Length <= 1_048_576
                ? Agenda.ParseIcs(File.ReadAllText(path), Today.AddDays(-1), Today.AddDays(400)) : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ics = []; }
        dirty = true;
    }

    private void Rebuild()
    {
        LoadIcs();
        dirty = false;
        lastBuilt = now;
        var today = Today;
        DateText = now.ToString("dddd, d MMMM", culture);
        var shown = new DateOnly(today.Year, today.Month, 1).AddMonths(monthOffset);
        MonthTitle = culture.DateTimeFormat.GetMonthName(shown.Month).ToUpperInvariant() + (shown.Year != today.Year ? " " + shown.Year : "");
        var first = culture.DateTimeFormat.FirstDayOfWeek;
        Weekdays = Enumerable.Range(0, 7)
            .Select(i => culture.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)(((int)first + i) % 7))[..1].ToUpperInvariant()).ToList();
        var all = local.Concat(ics).ToList();
        var busy = all.Select(i => DateOnly.FromDateTime(i.Start)).ToHashSet();
        var start = shown.AddDays(-(((int)shown.DayOfWeek - (int)first + 7) % 7));
        Days = Enumerable.Range(0, 42).Select(i =>
        {
            var day = start.AddDays(i);
            return new CalendarDay(day.Day.ToString(culture), day == today, day.Month == shown.Month, day, busy.Contains(day), day == selected, new Command(() => Pick(day)), day.ToDateTime(TimeOnly.MinValue).ToString("D", culture));
        }).ToList();
        AgendaGroups = Agenda.Group(all, selected ?? today, MaxRows).Select(d => new AgendaGroupRow(Header(d.Day), d.Items.Select(Row).ToList())).ToList();
        var next = all.Where(i => !i.Done && (i.AllDay ? DateOnly.FromDateTime(i.Start) >= today : i.Start >= now)).OrderBy(i => i.Start).FirstOrDefault();
        NextText = next is null ? tr("Nada pendiente") : $"{next.Title} · {When(next)}";
        (HasNext, NextTitle, NextWhen) = next is null ? (false, tr("Nada pendiente"), "") : (true, next.Title, When(next));
        Raise();
    }

    private void Pick(DateOnly day)
    {
        selected = selected == day ? null : day;
        Rebuild();
    }

    private string When(AgendaItem item)
    {
        var day = DateOnly.FromDateTime(item.Start);
        var time = item.AllDay ? tr("Todo el día") : item.Start.ToString("t", culture);
        return day == Today ? time : day == Today.AddDays(1) ? $"{tr("Mañana")}, {time}" : $"{item.Start.ToString("ddd d MMM", culture)}, {time}";
    }

    private string Header(DateOnly day)
    {
        string label = day == Today ? tr("HOY") : day == Today.AddDays(1) ? tr("MAÑANA") : day.ToDateTime(TimeOnly.MinValue).ToString("dddd, d MMM", culture).ToUpperInvariant();
        return $"{label} ({tr("SEMANA %@", Agenda.IsoWeek(day))})";
    }

    private AgendaRow Row(AgendaItem item) => new(item.Title, item.Kind == AgendaKind.Event ? (item.AllDay ? tr("Todo el día") : item.Start.ToString("t", culture)) : item.Start.ToString("t", culture),
        item.Kind == AgendaKind.Reminder, item.Done, new Command(() => Complete(item)), new Command(() => Remove(item)), tr("Quitar"), item.Done ? tr("Marcar como pendiente") : tr("Marcar como hecho"));

    private void Complete(AgendaItem item)
    {
        int i = local.FindIndex(x => x.Id == item.Id);
        if (i < 0) return;
        local[i] = item with { Done = !item.Done };
        Save();
        Rebuild();
    }

    private void Remove(AgendaItem item)
    {
        if (local.RemoveAll(x => x.Id == item.Id) > 0) { Save(); Rebuild(); }
    }

    private void Save() => prefs.SetList(PrefKeys.CalendarItems, local.Select(Agenda.Format));

    private void OpenAdder()
    {
        AdderOpen = !AdderOpen;
        if (AdderOpen) { NewTitle = ""; NewOffset = 0; NewTime = now.AddHours(1).ToString("HH:00", CultureInfo.InvariantCulture); }
        Raise();
    }

    private void AddItem()
    {
        var title = NewTitle.Trim();
        if (title.Length == 0) { hint = tr("Escribe qué recordar."); Raise(); return; }
        if (!TimeSpan.TryParseExact(NewTime.Trim(), ["h\\:mm", "hh\\:mm", "h", "hh"], CultureInfo.InvariantCulture, out var time) || time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
        {
            hint = tr("Hora no válida: usa 21:30.");
            Raise();
            return;
        }
        if (local.Count >= MaxLocal) local.RemoveAt(local.FindIndex(x => x.Done) is var done and >= 0 ? done : 0);
        local.Add(new AgendaItem(Guid.NewGuid().ToString("N")[..8], title, now.Date.AddDays(NewOffset) + time, AgendaKind.Reminder));
        Save();
        AdderOpen = false;
        NewTitle = "";
        selected = null;
        Rebuild();
    }

    private void ChooseIcs()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Calendario (*.ics)|*.ics", CheckFileExists = true, Title = tr("Elegir archivo…") };
        if (dialog.ShowDialog() != true) return;
        prefs.SetList(PrefKeys.CalendarIcs, [dialog.FileName]);
        dirty = true;
        Rebuild();
    }
}
