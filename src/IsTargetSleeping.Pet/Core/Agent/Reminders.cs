using System.Globalization;
using System.Text.RegularExpressions;

namespace IsTargetSleeping.Pet;

public sealed record Reminder(long Id, string Text, long Due, string Repeat, int EveryMin, bool Fired, long FiredTs);

public sealed record ParsedReminder(string Text, DateTimeOffset Due, string Repeat, int EveryMin);

/// Entiende recordatorios escritos en español sin ayuda del modelo: «recuérdame a las 5 pm llamar a mamá», «en 10 minutos revisar el horno»,
/// «mañana a las 8 tomar la pastilla», «todos los días a las 7 estirarme», «cada 2 horas beber agua». Devuelve null si no hay una hora clara
/// (entonces responde el modelo y pregunta). La hora se confirma siempre en la respuesta para que el usuario vea cómo se entendió.
public static class ReminderParser
{
    private const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly Regex Lead = new(@"^\s*(por favor[, ]*)?(recu[eé]rdame|recuerdame|av[ií]same|avisame|ponme un recordatorio|crea un recordatorio|nuevo recordatorio|recordatorio)\b[ :,]*(de |para |que )?", O);
    private static readonly Regex Relative = new(@"\ben\s+(?<n>\d+|un|una|media|medio|dos|tres|cinco|diez|quince|veinte|treinta)\s*(?<u>minutos?|min|horas?|h)\b(?<half>\s+y\s+media)?", O);
    private static readonly Regex Clock = new(@"\ba\s+las?\s+(?<h>\d{1,2})(?:[:.h](?<m>\d{2}))?\s*(?<ap>a\.?\s?m\.?|p\.?\s?m\.?|de la ma[nñ]ana|de la tarde|de la noche|de la madrugada)?", O);
    private static readonly Regex Every = new(@"\bcada\s+(?<n>\d+|una|un|dos|tres|media)?\s*(?<u>horas?|minutos?|d[ií]as?|semanas?)\b", O);
    private static readonly Regex Daily = new(@"\b(todos los d[ií]as|cada d[ií]a|a diario|diariamente)\b", O);
    private static readonly Regex Weekly = new(@"\b(todas las semanas|cada semana|semanalmente)\b", O);
    private static readonly Regex Tomorrow = new(@"\bma[nñ]ana\b", O);
    private static readonly Regex DayAfter = new(@"\bpasado ma[nñ]ana\b", O);
    private static readonly Regex Today = new(@"\b(hoy|esta (tarde|noche|ma[nñ]ana))\b", O);
    private static readonly Dictionary<string, int> Words = new() { ["un"] = 1, ["una"] = 1, ["dos"] = 2, ["tres"] = 3, ["cinco"] = 5, ["diez"] = 10, ["quince"] = 15, ["veinte"] = 20, ["treinta"] = 30, ["media"] = 30, ["medio"] = 30 };

    public static bool LooksLikeReminder(string text) => Lead.IsMatch(text);

    public static ParsedReminder? Parse(string input, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        string text = input.Trim();
        if (!Lead.IsMatch(text)) return null;
        text = Lead.Replace(text, "");
        var local = TimeZoneInfo.ConvertTime(now, zone);
        string repeat = "none";
        int every = 0;
        DateTimeOffset? due = null;

        string Cut(Regex r, ref string t, out Match m) { m = r.Match(t); if (m.Success) t = (t[..m.Index] + " " + t[(m.Index + m.Length)..]); return t; }

        var em = Every.Match(text);
        if (em.Success)
        {
            string u = em.Groups["u"].Value.ToLowerInvariant();
            int n = em.Groups["n"].Success ? Num(em.Groups["n"].Value) : 1;
            if (n <= 0) return null;
            if (u.StartsWith("min")) { repeat = "every"; every = n; }
            else if (u.StartsWith("hora")) { repeat = "every"; every = n * 60; }
            else if (u.StartsWith("d")) { repeat = n == 1 ? "daily" : "every"; every = n * 1440; }
            else { repeat = n == 1 ? "weekly" : "every"; every = n * 10080; }
            if (every < 5) return null;   // nunca cada pocos segundos
            text = text.Remove(em.Index, em.Length);
        }
        else if (Daily.IsMatch(text)) { repeat = "daily"; text = Daily.Replace(text, " ", 1); }
        else if (Weekly.IsMatch(text)) { repeat = "weekly"; text = Weekly.Replace(text, " ", 1); }

        var rm = Relative.Match(text);
        if (rm.Success)
        {
            string ns = rm.Groups["n"].Value.ToLowerInvariant();
            int n = Num(ns);
            string u = rm.Groups["u"].Value.ToLowerInvariant();
            double minutes = u.StartsWith("h") ? n * 60.0 : n;
            if (ns is "media" or "medio") minutes = u.StartsWith("h") ? 30 : n;
            if (rm.Groups["half"].Success) minutes += 30;
            if (minutes < 1 || minutes > 60 * 24 * 30) return null;
            due = now.AddMinutes(minutes);
            text = text.Remove(rm.Index, rm.Length);
        }
        else
        {
            var cm = Clock.Match(text);
            bool tomorrow = Tomorrow.IsMatch(text) && !DayAfter.IsMatch(text), after = DayAfter.IsMatch(text);
            if (cm.Success)
            {
                int h = int.Parse(cm.Groups["h"].Value, CultureInfo.InvariantCulture), m = cm.Groups["m"].Success ? int.Parse(cm.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
                if (h > 23 || m > 59) return null;
                string ap = cm.Groups["ap"].Value.ToLowerInvariant().Replace(".", "").Replace(" ", "");
                bool pm = ap.StartsWith("pm") || ap.Contains("tarde") || ap.Contains("noche"), am = ap.StartsWith("am") || ap.Contains("mañana") || ap.Contains("madrugada");
                if (pm && h < 12) h += 12;
                if (am && h == 12) h = 0;
                var day = local.Date + TimeSpan.Zero;
                int add = after ? 2 : tomorrow ? 1 : 0;
                DateTimeOffset At(int hh, int off) => new(DateTime.SpecifyKind(day.AddDays(off).AddHours(hh).AddMinutes(m), DateTimeKind.Unspecified), zone.GetUtcOffset(day.AddDays(off).AddHours(hh)));
                var cand = At(h, add);
                if (!pm && !am && h is >= 1 and <= 11)   // «a las 5» sin am/pm: la proxima ocurrencia (5:00 o 17:00)
                {
                    var alt = At(h + 12, add);
                    var options = new[] { cand, alt }.Where(c => c > now).ToList();
                    cand = options.Count > 0 ? options.Min() : At(h, add + 1);
                }
                else if (cand <= now && add == 0 && repeat == "none") cand = At(h, 1);
                else if (cand <= now) cand = cand.AddDays(1);
                due = cand;
                text = text.Remove(cm.Index, cm.Length);
            }
            else if (repeat == "every") due = now.AddMinutes(every);
            else if (tomorrow || after || Today.IsMatch(text)) return null;   // un dia sin hora: mejor preguntar
        }
        if (due is null) return null;
        if (due < now.AddSeconds(30) && repeat == "none") return null;

        text = Tomorrow.Replace(DayAfter.Replace(Today.Replace(text, " "), " "), " ");
        text = Regex.Replace(text, @"\s+", " ").Trim(' ', ',', '.', ':', ';');
        text = Regex.Replace(text, @"^(de|que|para|a)\s+", "", O).Trim();
        if (text.Length == 0) text = "Recordatorio";
        if (text.Length > 140) text = text[..140];
        text = char.ToUpperInvariant(text[0]) + text[1..];
        return new ParsedReminder(text, due.Value, repeat, every);
    }

    private static int Num(string s) => int.TryParse(s, out var n) ? n : Words.GetValueOrDefault(s.ToLowerInvariant(), 1);

    /// «hoy a las 17:00», «mañana a las 8:00», «el mié 6 nov a las 9:30»: como se lo confirma la mascota al usuario.
    public static string Describe(DateTimeOffset due, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var l = TimeZoneInfo.ConvertTime(due, zone);
        var n = TimeZoneInfo.ConvertTime(now, zone);
        string day = l.Date == n.Date ? "hoy" : l.Date == n.Date.AddDays(1) ? "mañana" : l.ToString("ddd d MMM", new CultureInfo("es"));
        return $"{day} a las {l:HH:mm}";
    }

    public static DateTimeOffset NextDue(string repeat, int everyMin, DateTimeOffset due, DateTimeOffset now)
    {
        var step = repeat switch { "daily" => TimeSpan.FromDays(1), "weekly" => TimeSpan.FromDays(7), "every" => TimeSpan.FromMinutes(Math.Max(5, everyMin)), _ => TimeSpan.Zero };
        if (step == TimeSpan.Zero) return due;
        var next = due + step;
        while (next <= now) next += step;   // si la PC estaba apagada no se disparan 50 avisos seguidos: se salta al siguiente futuro
        return next;
    }
}

/// Dispara los recordatorios vencidos. Un recordatorio que venció con la PC apagada se entrega al volver como «tardío» (hasta 24 h).
public sealed class ReminderScheduler(SqlitePetStore store, IEventSink sink, IAppBridge? bridge, Func<DateTimeOffset>? clock = null, PushService? push = null)
{
    public static readonly TimeSpan LateWindow = TimeSpan.FromHours(24);
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly object gate = new();

    public event Action<Reminder, bool>? Fired;

    public async Task TickAsync(CancellationToken ct = default)
    {
        var t = now();
        List<Reminder> due;
        lock (gate) due = store.DueReminders(t.ToUnixTimeSeconds());
        foreach (var r in due)
        {
            bool late = t.ToUnixTimeSeconds() - r.Due > 120;
            if (late && t - DateTimeOffset.FromUnixTimeSeconds(r.Due) > LateWindow && r.Repeat == "none") { store.MarkFired(r.Id, t.ToUnixTimeSeconds(), null); continue; }   // demasiado viejo: se descarta en silencio
            DateTimeOffset? next = r.Repeat == "none" ? null : ReminderParser.NextDue(r.Repeat, r.EveryMin, DateTimeOffset.FromUnixTimeSeconds(r.Due), t);
            store.MarkFired(r.Id, t.ToUnixTimeSeconds(), next?.ToUnixTimeSeconds());
            long seq = store.LogFired(r, t.ToUnixTimeSeconds());
            sink.Publish(PetProtocol.Reminder(r, late, seq));
            if (push is not null) _ = push.SendAsync("Nimbo", (late ? "Te lo debía: " : "") + r.Text, "/#agenda", "rem" + r.Id);
            Fired?.Invoke(r, late);
            if (bridge is not null)
                try { await bridge.CallAsync("notify.show", new System.Text.Json.Nodes.JsonObject { ["title"] = "Nimbo", ["text"] = r.Text }, ct); }
                catch (Exception e) when (e is IOException or OperationCanceledException) { }
        }
    }
}
