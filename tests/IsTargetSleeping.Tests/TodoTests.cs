using IsTargetSleeping;

internal static class TodoTests
{
    public static void Run(Action<string, bool> check)
    {
        var today = new DateOnly(2026, 10, 29);
        var yesterday = today.AddDays(-1);
        var tomorrow = today.AddDays(1);
        TodoItem Item(string id, bool daily = false, DateOnly? done = null, DateOnly? created = null) => new(id, "Tarea " + id, "", daily, done, created);

        var items = new List<TodoItem> { Item("a"), Item("b", done: today), Item("c", daily: true, done: yesterday), Item("d", done: yesterday), Item("e", daily: true, done: today) };
        check("tareas: una diaria marcada ayer reaparece sin marcar y una de un día cerrada ayer sigue hecha",
            !TodoStore.IsDone(items[2], today) && TodoStore.IsDone(items[3], today) && TodoStore.IsDone(items[1], today) && TodoStore.IsDone(items[4], today));
        var visible = TodoStore.ForDay(items, today, today);
        check("tareas: hoy se ocultan las de un día cerradas antes de hoy", visible.Select(i => i.Id).SequenceEqual(["a", "b", "c", "e"]));
        check("tareas: el contador cuenta las hechas hoy sobre el total visible", TodoStore.Count(visible, today, today) == (2, 4) && TodoStore.Count([], today, today) == (0, 0));
        var done = TodoStore.Toggle(Item("a"), today);
        check("tareas: marcar sube el contador y desmarcar lo baja", done.DoneOn == today && TodoStore.Toggle(done, today).DoneOn is null
            && TodoStore.Count(TodoStore.ForDay([done], today, today), today, today) == (1, 1));
        check("tareas: el día siguiente el contador vuelve a 0 y las diarias salen sin marcar",
            TodoStore.Count(TodoStore.ForDay([Item("x", daily: true, done: today), Item("y", done: today)], tomorrow, tomorrow), tomorrow, tomorrow) == (0, 1));
        check("tareas: una diaria hecha ayer se marca hoy al tocarla", TodoStore.Toggle(items[2], today).DoneOn == today);

        // Calendario: cada día enseña lo suyo.
        var plan = new List<TodoItem> { Item("futura", created: tomorrow), Item("hoy", created: today), Item("vieja", created: yesterday), Item("diaria", daily: true, created: yesterday), Item("cerrada", done: yesterday, created: yesterday) };
        check("calendario: una tarea de mañana no sale hoy y una pendiente de ayer sí",
            TodoStore.ForDay(plan, today, today).Select(i => i.Id).SequenceEqual(["hoy", "vieja", "diaria"]));
        check("calendario: mañana sale lo suyo y las diarias; ayer lo creado o cerrado entonces",
            TodoStore.ForDay(plan, tomorrow, today).Select(i => i.Id).SequenceEqual(["futura", "diaria"])
            && TodoStore.ForDay(plan, yesterday, today).Select(i => i.Id).SequenceEqual(["vieja", "diaria", "cerrada"]));
        check("calendario: en otro día solo cuenta como hecha la marcada justo entonces", TodoStore.IsDoneOn(plan[4], yesterday, today) && !TodoStore.IsDoneOn(plan[4], tomorrow, today));
        var grid = TodoStore.MonthGrid(new DateOnly(2026, 10, 1), DayOfWeek.Monday);
        check("calendario: la cuadrícula tiene 42 casillas y empieza en el lunes de la semana del día 1 (1 oct 2026 es jueves)",
            grid.Count == 42 && grid[0] == new DateOnly(2026, 9, 28) && grid[3] == new DateOnly(2026, 10, 1)
            && TodoStore.MonthGrid(new DateOnly(2026, 10, 1), DayOfWeek.Sunday)[0] == new DateOnly(2026, 9, 27));

        check("tareas: la hora se enmascara como «HH:MM - HH:MM» mientras se escribe y solo admite cifras",
            TodoStore.MaskTime("0900") == "09:00" && TodoStore.MaskTime("09001") == "09:00 - 1" && TodoStore.MaskTime("09001030") == "09:00 - 10:30"
            && TodoStore.MaskTime("ab1x2") == "12" && TodoStore.MaskTime("1234567890") == "12:34 - 56:78" && TodoStore.MaskTime("") == "" && TodoStore.MaskTime(null) == "");

        var back = TodoStore.TryParse(TodoStore.Serialize(plan), out var parsed);
        check("tareas: guardar y leer devuelve las mismas tareas, su repetición, su día y el día al que pertenecen", back && parsed.SequenceEqual(plan));
        check("tareas: un archivo roto no da tareas y un esquema ajeno no se toma por una lista vacía",
            !TodoStore.TryParse("{ roto", out _) && !TodoStore.TryParse("{}", out _) && TodoStore.TryParse("{\"tasks\":[]}", out var none) && none.Count == 0);
        check("tareas: ids repetidos, títulos vacíos y fechas inválidas se descartan o se ignoran",
            TodoStore.TryParse("{\"tasks\":[{\"id\":\"x\",\"title\":\"1\"},{\"id\":\"x\",\"title\":\"2\"},{\"id\":\"y\",\"title\":\"  \"},{\"id\":\"z\",\"title\":\"3\",\"doneOn\":\"mal\"}]}", out var odd)
            && odd.Select(i => i.Id).SequenceEqual(["x", "z"]) && odd[1].DoneOn is null && odd[1].Created is null);
        check("tareas: el título pierde saltos de línea y se recorta",
            TodoStore.CleanTitle(" uno\ndos ") == "uno dos" && TodoStore.CleanTitle(new string('a', 300)).Length == TodoStore.MaxTitle);

        var folder = Path.Combine(Path.GetTempPath(), "its-todo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "tasks.json");
            NotesStore.SaveAtomic(file, TodoStore.Serialize(plan));
            var loaded = TodoStore.Load(file, DateTime.Now);
            check("tareas: se guardan en disco y se recuperan al reiniciar", loaded.SaveAllowed && loaded.Items.SequenceEqual(plan));
            File.WriteAllText(file, "{ truncado");
            var corrupt = TodoStore.Load(file, DateTime.Now);
            check("tareas: un archivo dañado se respalda antes de volver a guardar",
                corrupt.Items.Count == 0 && corrupt.SaveAllowed && Directory.GetFiles(folder, "tasks.json.corrupt-*").Length == 1 && File.ReadAllText(file) == "{ truncado");
            check("tareas: sin archivo la lista empieza vacía", TodoStore.Load(Path.Combine(folder, "nada.json"), DateTime.Now) is { Items.Count: 0, SaveAllowed: true });
        }
        finally { try { Directory.Delete(folder, true); } catch { } }

        check("tareas: el widget cabe en una, dos y cuatro celdas y es de dos por defecto",
            WidgetLayout.Sizes("todo").SequenceEqual([(1, 1), (2, 1), (2, 2), (1, 2)]) && WidgetLayout.Span("todo") == 2 && WidgetLayout.Span("todo@1") == 1
            && WidgetCatalog.Folders.Any(f => f.Id == "create" && f.Widgets.Contains("todo")));
    }
}
