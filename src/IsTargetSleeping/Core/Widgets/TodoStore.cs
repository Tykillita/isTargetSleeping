using System.Globalization;
using System.Text.Json.Nodes;

namespace IsTargetSleeping;

/// Una tarea: su título, la franja horaria («09:00 - 10:00»), si se repite cada día, el día en que se marcó hecha y el día al que pertenece (`Created`; null = de siempre, vale desde hoy).
public sealed record TodoItem(string Id, string Title, string Time, bool Daily, DateOnly? DoneOn, DateOnly? Created = null);

/// La lógica pura del widget Tareas: qué cuenta como hecho, qué lista se ve cada día (hoy o el que se elige en el calendario), el contador, la máscara de la hora y el archivo (tasks.json).
public static class TodoStore
{
    public const int MaxTasks = 100;
    public const int MaxTitle = 200;

    /// Hecha hoy; una tarea de un solo día marcada otro día sigue hecha, y una diaria marcada otro día vuelve a salir sin marcar.
    public static bool IsDone(TodoItem item, DateOnly today) => item.DoneOn is { } on && (item.Daily ? on == today : true);

    /// ¿Hecha en ese día? Hoy vale lo de siempre; en otro día, solo si se marcó justo entonces.
    public static bool IsDoneOn(TodoItem item, DateOnly day, DateOnly today) => day == today ? IsDone(item, today) : item.DoneOn == day;

    /// Lo que enseña la lista de un día. Hoy: lo de hoy, lo pendiente de antes y las diarias; una de un día cerrada antes de hoy ya no sale.
    /// Otro día: lo que se creó para él, lo que se cerró entonces y las diarias que ya existían.
    public static List<TodoItem> ForDay(IEnumerable<TodoItem> items, DateOnly day, DateOnly today) => items.Where(i =>
    {
        var created = i.Created ?? DateOnly.MinValue;
        if (day == today) return created <= today && (i.Daily || i.DoneOn is null || i.DoneOn == today);
        return i.Daily ? created <= day : i.Created == day || i.DoneOn == day;
    }).ToList();

    /// El contador «hechas / total» de la lista de ese día.
    public static (int Done, int Total) Count(IEnumerable<TodoItem> list, DateOnly day, DateOnly today)
    {
        var all = list.ToList();
        return (all.Count(i => IsDoneOn(i, day, today)), all.Count);
    }

    public static TodoItem Toggle(TodoItem item, DateOnly today) => item with { DoneOn = IsDone(item, today) ? null : today };

    public static string CleanTitle(string? title)
    {
        var t = (title ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return t.Length > MaxTitle ? t[..MaxTitle] : t;
    }

    /// La hora con la máscara «HH:MM - HH:MM»: solo cuenta lo que son cifras (hasta 8) y los separadores aparecen al escribir.
    public static string MaskTime(string? raw)
    {
        var d = new string((raw ?? "").Where(char.IsAsciiDigit).Take(8).ToArray());
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < d.Length; i++)
        {
            if (i == 2 || i == 6) sb.Append(':');
            if (i == 4) sb.Append(" - ");
            sb.Append(d[i]);
        }
        return sb.ToString();
    }

    public static string CleanTime(string? time) => MaskTime(time);

    /// Las 42 casillas (6 semanas) del mes de `month`, empezando por el primer día de la semana que lleve el día 1.
    public static List<DateOnly> MonthGrid(DateOnly month, DayOfWeek firstDay)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        int back = ((int)first.DayOfWeek - (int)firstDay + 7) % 7;
        var start = first.AddDays(-back);
        return Enumerable.Range(0, 42).Select(start.AddDays).ToList();
    }

    public static string Serialize(IEnumerable<TodoItem> items) => new JsonObject
    {
        ["tasks"] = new JsonArray([.. items.Select(i => (JsonNode)new JsonObject
        {
            ["id"] = i.Id, ["title"] = i.Title, ["time"] = i.Time, ["daily"] = i.Daily,
            ["doneOn"] = i.DoneOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["created"] = i.Created?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        })]),
    }.ToJsonString();

    public static bool TryParse(string json, out List<TodoItem> items)
    {
        items = [];
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["tasks"] is not JsonArray array) return false;
            foreach (var o in array.OfType<JsonObject>())
            {
                if (o["id"] is not JsonValue idv || !idv.TryGetValue<string>(out var id) || string.IsNullOrWhiteSpace(id) || items.Any(x => x.Id == id)) continue;
                string Text(string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var str) ? str : "";
                DateOnly? Day(string key) => DateOnly.TryParseExact(Text(key), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
                var title = CleanTitle(Text("title"));
                if (title.Length == 0) continue;
                items.Add(new(id, title, CleanTime(Text("time")), o["daily"] is JsonValue dv && dv.TryGetValue<bool>(out var daily) && daily, Day("doneOn"), Day("created")));
                if (items.Count >= MaxTasks) break;
            }
            return true;
        }
        catch (System.Text.Json.JsonException) { return false; }
    }

    /// Qué tareas se ven en la agenda: las de un día en su día (las pendientes de antes, hoy), las diarias hoy, y las hechas solo si se cerraron hoy.
    /// Sin hora, «todo el día»; con ella, la de inicio de la franja.
    public static List<AgendaItem> ToAgenda(IEnumerable<TodoItem> tasks, DateOnly today)
    {
        var result = new List<AgendaItem>();
        foreach (var task in tasks)
        {
            bool done = TodoStore.IsDone(task, today);
            var day = task.Daily ? today : task.Created ?? today;
            if (!task.Daily && day < today) { if (done) continue; day = today; }
            if (done && task.DoneOn != today) continue;
            bool timed = task.Time.Length >= 5 && TimeOnly.TryParseExact(task.Time[..5], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
            var start = day.ToDateTime(timed ? TimeOnly.ParseExact(task.Time[..5], "HH:mm", CultureInfo.InvariantCulture) : TimeOnly.MinValue);
            result.Add(new AgendaItem("todo:" + task.Id, task.Title, start, AgendaKind.Reminder, done, AllDay: !timed));
        }
        return result;
    }

    /// Lee tasks.json. Un archivo roto se copia aparte y, si no se puede, no se deja guardar encima (como las notas).
    public static (List<TodoItem> Items, bool SaveAllowed) Load(string file, DateTime now)
    {
        try
        {
            if (!File.Exists(file)) return ([], true);
            if (TryParse(File.ReadAllText(file), out var items)) return (items, true);
            try { File.Copy(file, file + ".corrupt-" + now.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture), overwrite: false); return ([], true); }
            catch { return ([], false); }
        }
        catch { return ([], false); }
    }
}
