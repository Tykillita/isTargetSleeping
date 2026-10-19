using System.Globalization;

namespace IsTargetSleeping;

public enum AgendaKind { Reminder, Event }

/// Una cosa de la agenda del widget de calendario: un recordatorio local (se marca hecho) o un evento de un archivo .ics (solo se lee).
public sealed record AgendaItem(string Id, string Title, DateTime Start, AgendaKind Kind, bool Done = false, bool AllDay = false,
    DateTime? End = null, string? Url = null, string? Location = null, string? Description = null);

public sealed record AgendaDay(DateOnly Day, List<AgendaItem> Items);

/// La agenda del calendario: guardado de los recordatorios locales (una línea de texto cada uno, en los ajustes), agrupado por días y
/// lector de archivos .ics (el formato de Google Calendar, Outlook y Apple). Lógica pura, sin WPF, para poder probarla. Nada sale del PC.
public static class Agenda
{
    /// `id|aaaammddThhmm|tipo+hecho|título`: el título va el último, así que puede llevar «|».
    public static string Format(AgendaItem item) =>
        $"{item.Id}|{item.Start:yyyyMMdd'T'HHmm}|{(item.Kind == AgendaKind.Event ? 'e' : 'r')}{(item.Done ? '1' : '0')}|{item.Title}";

    public static AgendaItem? Parse(string? line)
    {
        var parts = line?.Split('|', 4);
        if (parts is not { Length: 4 } || parts[0].Length == 0 || parts[3].Trim().Length == 0 || parts[2].Length != 2
            || !DateTime.TryParseExact(parts[1], "yyyyMMdd'T'HHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            || parts[2][0] is not ('e' or 'r') || parts[2][1] is not ('0' or '1')) return null;
        return new AgendaItem(parts[0], parts[3], start, parts[2][0] == 'e' ? AgendaKind.Event : AgendaKind.Reminder, parts[2][1] == '1');
    }

    public static int IsoWeek(DateOnly day) => ISOWeek.GetWeekOfYear(day.ToDateTime(TimeOnly.MinValue));

    /// Lo que viene desde `from`, en orden y agrupado por días (solo los días que tienen algo), hasta `max` cosas en total.
    public static List<AgendaDay> Group(IEnumerable<AgendaItem> items, DateOnly from, int max)
    {
        var days = new List<AgendaDay>();
        foreach (var item in items.Where(i => DateOnly.FromDateTime(i.Start) >= from).OrderBy(i => i.Start).ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase).Take(max))
        {
            var day = DateOnly.FromDateTime(item.Start);
            if (days.Count == 0 || days[^1].Day != day) days.Add(new AgendaDay(day, []));
            days[^1].Items.Add(item);
        }
        return days;
    }

    // MARK: archivos .ics

    /// Los eventos del archivo que caen entre `from` y `to` (incluidos). Entiende lo habitual (SUMMARY, DTSTART, todo el día, hora UTC y
    /// repeticiones diarias, semanales con días, mensuales y anuales con INTERVAL, COUNT y UNTIL).
    /// shortcut: ignora la zona horaria de una hora con TZID (se toma como hora local) y las excepciones de las repeticiones (EXDATE); revisar si hace falta.
    public static List<AgendaItem> ParseIcs(string text, DateOnly from, DateOnly to, int max = 400)
    {
        var result = new List<AgendaItem>();
        var lines = text.Replace("\r\n", "\n").Replace("\n ", "").Replace("\n\t", "").Split('\n');
        Dictionary<string, (string Params, string Value)>? ev = null;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase)) { ev = new(StringComparer.OrdinalIgnoreCase); continue; }
            if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (ev is not null) Expand(ev, from, to, result);
                ev = null;
                if (result.Count >= max) break;
                continue;
            }
            if (ev is null) continue;
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var head = line[..colon];
            int semi = head.IndexOf(';');
            ev[semi < 0 ? head : head[..semi]] = (semi < 0 ? "" : head[(semi + 1)..], line[(colon + 1)..]);
        }
        return result.OrderBy(i => i.Start).Take(max).ToList();
    }

    private static string Unescape(string v) => v.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\").Trim();

    private static bool TryDate(string value, out DateTime start, out bool allDay)
    {
        allDay = value.Length == 8;
        start = default;
        if (value.Length == 8) return DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start);
        bool utc = value.EndsWith('Z');
        if (!DateTime.TryParseExact(value.TrimEnd('Z'), ["yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out start)) return false;
        if (utc) start = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(start, DateTimeKind.Utc), TimeZoneInfo.Local);
        return true;
    }

    private static void Expand(Dictionary<string, (string Params, string Value)> ev, DateOnly from, DateOnly to, List<AgendaItem> result)
    {
        if (!ev.TryGetValue("DTSTART", out var s) || !ev.TryGetValue("SUMMARY", out var summary) || !TryDate(s.Value, out var start, out bool allDay)) return;
        var title = Unescape(summary.Value);
        if (title.Length == 0) return;
        TimeSpan duration = TimeSpan.Zero;
        if (ev.TryGetValue("DTEND", out var dtEnd) && TryDate(dtEnd.Value, out var end, out _) && end > start) duration = end - start;
        else if (allDay) duration = TimeSpan.FromDays(1);
        else duration = TimeSpan.FromHours(1);
        string? url = ev.TryGetValue("URL", out var urlValue) ? Unescape(urlValue.Value)[..Math.Min(Unescape(urlValue.Value).Length, 2048)] : null;
        string? location = ev.TryGetValue("LOCATION", out var locValue) ? Unescape(locValue.Value)[..Math.Min(Unescape(locValue.Value).Length, 512)] : null;
        string? description = ev.TryGetValue("DESCRIPTION", out var descValue) ? Unescape(descValue.Value)[..Math.Min(Unescape(descValue.Value).Length, 4096)] : null;
        string uid = ev.TryGetValue("UID", out var u) ? u.Value : title;
        void Add(DateTime when)
        {
            var day = DateOnly.FromDateTime(when);
            if (day >= from && day <= to) result.Add(new AgendaItem("ics:" + uid.GetHashCode().ToString("x") + when.ToString("yyyyMMddHHmm"), title, when, AgendaKind.Event, false, allDay,
                when + duration, url, location, description));
        }
        if (!ev.TryGetValue("RRULE", out var rule)) { Add(start); return; }
        var r = rule.Value.Split(';').Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0].ToUpperInvariant(), p => p[1]);
        r.TryGetValue("FREQ", out var freq);
        int interval = r.TryGetValue("INTERVAL", out var iv) && int.TryParse(iv, out var n) && n > 0 ? n : 1;
        int count = r.TryGetValue("COUNT", out var c) && int.TryParse(c, out var cn) ? cn : int.MaxValue;
        DateTime until = r.TryGetValue("UNTIL", out var un) && TryDate(un, out var ud, out _) ? ud.Date.AddDays(1) : DateTime.MaxValue;
        var limit = to.ToDateTime(TimeOnly.MaxValue);
        int produced = 0;
        if (freq == "WEEKLY" && r.TryGetValue("BYDAY", out var by))
        {
            var wanted = by.Split(',').Select(d => d.Trim().ToUpperInvariant() switch { "MO" => 1, "TU" => 2, "WE" => 3, "TH" => 4, "FR" => 5, "SA" => 6, "SU" => 0, _ => -1 }).Where(d => d >= 0).ToList();
            var monday = start.Date.AddDays(-(((int)start.DayOfWeek + 6) % 7));
            bool stop = false;
            for (int week = 0; week < 600 && !stop; week += interval)
            {
                var weekStart = monday.AddDays(week * 7);
                if (weekStart > limit) break;
                foreach (var d in wanted.OrderBy(d => (d + 6) % 7))
                {
                    var when = weekStart.AddDays((d + 6) % 7) + start.TimeOfDay;
                    if (when < start) continue;
                    if (when >= until || when > limit || produced >= count) { stop = true; break; }
                    Add(when);
                    produced++;
                }
            }
            return;
        }
        for (int i = 0; i < 4000 && produced < count; i++)
        {
            var when = freq switch
            {
                "DAILY" => start.AddDays((long)i * interval),
                "WEEKLY" => start.AddDays((long)i * interval * 7),
                "MONTHLY" => start.AddMonths(i * interval),
                "YEARLY" => start.AddYears(i * interval),
                _ => DateTime.MaxValue,
            };
            if (when >= until || when > limit) break;
            Add(when);
            produced++;
        }
    }
}
