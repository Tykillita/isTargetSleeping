using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IsTargetSleeping;

/// Lo que se apunta en el historial.
///  - Nap: un modelo se durmió (Detail: auto, manual o game; Bytes: lo que ocupaba).
///  - Wake: un modelo se cargó (Subject: la app que lo pidió; Detail: su .exe; Model: el modelo).
///  - Crash / Restart / GiveUp: el vigilante vio caer o colgarse Ollama (Detail: crash o hang).
///  - GameOn / GameOff: modo juego (Subject: el juego).
///  - Download / Delete: modelos descargados o borrados (Subject: el modelo).
///  - Clean: RAM liberada (Bytes: lo liberado; Detail: manual, threshold, interval, critical o game).
public enum StatKind { Nap, Wake, Crash, Restart, GiveUp, GameOn, GameOff, Download, Delete, Clean, ProcessTerminated }

public sealed record StatEvent(DateTime At, StatKind Kind, string? Subject = null, long Bytes = 0, string? Detail = null, string? Model = null,
    CleanResult? Clean = null, ProcessActionResult? ProcessAction = null);

public readonly record struct StatsSummary(long Recovered, int Naps, double LoadedHours, int Restarts, long Cleaned = 0, int Cleans = 0);

public readonly record struct ClientStat(string App, int Wakes, DateTime Last, string? Path);

/// Una muestra para la gráfica de 30 min (bytes).
public readonly record struct MemorySample(DateTime At, long Total, long Used, long Model);

/// Historial de 90 días en %LOCALAPPDATA%\isTargetSleeping\stats.json y los
/// agregados de la vista Actividad. Sin archivo (null) vive solo en memoria:
/// así lo usan las pruebas y la demo.
public sealed class StatsStore
{
    public const int KeepDays = 90;
    public static readonly TimeSpan SampleWindow = TimeSpan.FromMinutes(30);

    private readonly string? file;
    private readonly object gate = new();
    private List<StatEvent> events = [];
    /// Segundos con algún modelo cargado, por día (yyyy-MM-dd).
    private Dictionary<string, double> loaded = [];
    private readonly List<MemorySample> samples = [];
    private DateTime lastSave = DateTime.MinValue;
    private bool dirty;

    public event Action? Changed;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed class Data
    {
        public List<StatEvent> Events { get; set; } = [];
        public Dictionary<string, double> Loaded { get; set; } = [];
    }

    public StatsStore(string? file)
    {
        this.file = file;
        if (file is null) return;
        try
        {
            if (File.Exists(file) && JsonSerializer.Deserialize<Data>(File.ReadAllText(file), Json) is { } data)
            {
                events = data.Events ?? [];
                loaded = data.Loaded ?? [];
            }
        }
        catch { }
        Prune(DateTime.Now);
    }

    public void Add(StatEvent e)
    {
        lock (gate)
        {
            events.Add(e);
            Prune(e.At);
            dirty = true;
        }
        Save(force: true);
        Changed?.Invoke();
    }

    public void UpdateClean(CleanResult result)
    {
        lock (gate)
        {
            int index = events.FindIndex(e => e.Clean?.RequestId == result.RequestId);
            if (index < 0) return;
            events[index] = events[index] with { Bytes = result.Freed, Clean = result };
            dirty = true;
        }
        Save(force: true);
        Changed?.Invoke();
    }

    /// Suma el tiempo con modelo cargado (se llama en cada muestra). Guarda como
    /// mucho una vez por minuto.
    public void AddLoaded(double seconds, DateTime now)
    {
        if (seconds <= 0) return;
        lock (gate)
        {
            var day = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            loaded[day] = (loaded.TryGetValue(day, out var s) ? s : 0) + seconds;
            dirty = true;
        }
        Save(force: false, now);
    }

    public void Sample(MemorySample s)
    {
        lock (gate)
        {
            samples.Add(s);
            int drop = samples.FindIndex(x => s.At - x.At <= SampleWindow);
            if (drop > 0) samples.RemoveRange(0, drop);
        }
    }

    public List<MemorySample> Samples() { lock (gate) return [.. samples]; }

    public List<StatEvent> Recent(int count)
    {
        lock (gate) return events.OrderByDescending(e => e.At).Take(count).ToList();
    }

    public StatEvent? Last(StatKind kind)
    {
        lock (gate) return events.Where(e => e.Kind == kind).MaxBy(e => e.At);
    }

    /// Los últimos 7 días: memoria recuperada al dormir, siestas, horas con modelo
    /// cargado y reinicios del vigilante.
    public StatsSummary Week(DateTime now)
    {
        lock (gate)
        {
            var since = now.AddDays(-7);
            var week = events.Where(e => e.At > since && e.At <= now).ToList();
            var naps = week.Where(e => e.Kind == StatKind.Nap).ToList();
            double seconds = 0;
            for (int i = 0; i < 7; i++)
            {
                var day = now.Date.AddDays(-i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (loaded.TryGetValue(day, out var s)) seconds += s;
            }
            var cleans = week.Where(e => e.Kind == StatKind.Clean && (e.Clean is null
                || e.Clean.Outcome is CleanOutcome.Success or CleanOutcome.Partial)).ToList();
            long cleaned = cleans.Sum(e => e.Bytes);
            return new StatsSummary(naps.Sum(e => e.Bytes) + cleaned, naps.Count, seconds / 3600, week.Count(e => e.Kind == StatKind.Restart), cleaned, cleans.Count);
        }
    }

    /// Qué apps despiertan al modelo, las que más primero.
    public List<ClientStat> Clients()
    {
        lock (gate)
        {
            return events.Where(e => e.Kind == StatKind.Wake && !string.IsNullOrEmpty(e.Subject))
                .GroupBy(e => e.Subject!, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var last = g.MaxBy(e => e.At)!;
                    return new ClientStat(last.Subject!, g.Count(), last.At, last.Detail);
                })
                .OrderByDescending(c => c.Wakes).ThenByDescending(c => c.Last)
                .ToList();
        }
    }

    private void Prune(DateTime now)
    {
        var limit = now.AddDays(-KeepDays);
        events.RemoveAll(e => e.At < limit);
        foreach (var day in loaded.Keys.ToList())
        {
            if (DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d < limit.Date)
                loaded.Remove(day);
        }
    }

    public void Flush() => Save(force: true);

    private void Save(bool force, DateTime? now = null)
    {
        if (file is null) return;
        string text;
        lock (gate)
        {
            var t = now ?? DateTime.Now;
            if (!dirty || (!force && t - lastSave < TimeSpan.FromMinutes(1))) return;
            lastSave = t;
            dirty = false;
            text = JsonSerializer.Serialize(new Data { Events = events, Loaded = loaded }, Json);
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, text);
            File.Move(tmp, file, overwrite: true);
        }
        catch { }
    }
}
