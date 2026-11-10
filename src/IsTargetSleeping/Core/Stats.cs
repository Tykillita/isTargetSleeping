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
    private const long JournalCompactionBytes = 1_048_576;

    private readonly string? file;
    private readonly string? eventsFile;
    private readonly string? sequenceFile;
    private readonly string? loadedFile;
    private readonly object gate = new();
    private readonly object saveGate = new();
    private List<StatEvent> events = [];
    private Dictionary<string, double> loaded = [];
    private Dictionary<string, double> loadedDelta = [];
    private readonly Dictionary<Guid, StatEvent> pendingClean = [];
    private readonly List<JournalEntry> pendingJournal = [];
    private volatile bool historyLoaded;
    private readonly List<MemorySample> samples = [];
    private DateTime lastLoadedSave = DateTime.MinValue;
    private long journalSequence;
    private bool loadedDirty;

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
        public long JournalSequence { get; set; }
    }

    private sealed record JournalEntry(long Sequence, StatEvent Event, Guid? UpdatesClean = null);

    public StatsStore(string? file)
    {
        this.file = file;
        eventsFile = file is null ? null : file + ".events.jsonl";
        sequenceFile = file is null ? null : file + ".events.seq";
        loadedFile = file is null ? null : file + ".loaded.json";
        historyLoaded = file is null;
        if (loadedFile is null) return;
        bool hasSequence = false;
        try
        {
            if (File.Exists(sequenceFile) && long.TryParse(File.ReadAllText(sequenceFile), NumberStyles.None, CultureInfo.InvariantCulture, out var sequence))
            {
                journalSequence = Math.Max(0, sequence);
                hasSequence = true;
            }
        }
        catch { }
        if (!hasSequence)
        {
            try
            {
                if (File.Exists(eventsFile))
                {
                    foreach (var line in File.ReadLines(eventsFile))
                        try { if (JsonSerializer.Deserialize<JournalEntry>(line, Json) is { } entry) journalSequence = Math.Max(journalSequence, entry.Sequence); } catch { }
                }
            }
            catch { }
        }
        try
        {
            if (File.Exists(loadedFile))
            {
                using var stream = File.OpenRead(loadedFile);
                loadedDelta = JsonSerializer.Deserialize<Dictionary<string, double>>(stream, Json) ?? [];
                int count = loadedDelta.Count;
                PruneLoadedDelta(DateTime.Now);
                loadedDirty = count != loadedDelta.Count;
            }
        }
        catch { }
    }

    private void EnsureHistoryLoaded()
    {
        if (historyLoaded) return;
        lock (saveGate)
        {
            if (historyLoaded) return;
            PersistPendingJournal();
            long appliedSequence = 0;
            bool baseLoaded = file is not null && !File.Exists(file);
            try
            {
                if (file is not null && File.Exists(file))
                {
                    using var stream = File.OpenRead(file);
                    if (JsonSerializer.Deserialize<Data>(stream, Json) is { } data)
                    {
                        lock (gate)
                        {
                            events = data.Events ?? [];
                            loaded = data.Loaded ?? [];
                            appliedSequence = Math.Max(0, data.JournalSequence);
                            journalSequence = Math.Max(journalSequence, appliedSequence);
                            baseLoaded = true;
                        }
                    }
                }
            }
            catch { }

            if (eventsFile is not null)
            {
                try
                {
                    foreach (var line in File.ReadLines(eventsFile))
                    {
                        try
                        {
                            if (JsonSerializer.Deserialize<JournalEntry>(line, Json) is not { } entry || entry.Sequence <= appliedSequence) continue;
                            lock (gate) ApplyJournalEntry(entry);
                            appliedSequence = entry.Sequence;
                        }
                        catch { }
                    }
                }
                catch { }
            }
            lock (gate)
            {
                foreach (var entry in pendingJournal.OrderBy(e => e.Sequence))
                {
                    if (entry.Sequence <= appliedSequence) continue;
                    ApplyJournalEntry(entry);
                    appliedSequence = entry.Sequence;
                }
                Prune(DateTime.Now);
                historyLoaded = true;
            }

            // shortcut: compacts after a history read passes 1 MiB; schedule compaction if unopened histories grow materially.
            if (baseLoaded && eventsFile is not null && File.Exists(eventsFile))
            {
                try { if (new FileInfo(eventsFile).Length >= JournalCompactionBytes) CompactJournal(appliedSequence); }
                catch { }
            }
        }
    }

    // Called under gate; updates replay after their original event in the append-only journal.
    private void ApplyJournalEntry(JournalEntry entry)
    {
        if (entry.UpdatesClean is { } requestId)
        {
            int index = events.FindIndex(e => e.Clean?.RequestId == requestId);
            if (index >= 0) events[index] = entry.Event;
            return;
        }
        events.Add(entry.Event);
    }

    public void Add(StatEvent e)
    {
        if (file is null)
        {
            lock (gate)
            {
                events.Add(e);
                Prune(e.At);
            }
        }
        else
        {
            lock (saveGate)
            {
                lock (gate)
                {
                    var entry = new JournalEntry(++journalSequence, e);
                    pendingJournal.Add(entry);
                    if (historyLoaded)
                    {
                        ApplyJournalEntry(entry);
                        Prune(e.At);
                    }
                    if (e.Clean is { RequestId: var id } && id != Guid.Empty) pendingClean[id] = e;
                }
                PersistPendingJournal();
            }
        }
        Changed?.Invoke();
    }

    public void UpdateClean(CleanResult result)
    {
        if (file is null)
        {
            lock (gate)
            {
                int index = events.FindIndex(e => e.Clean?.RequestId == result.RequestId);
                if (index < 0) return;
                events[index] = events[index] with { Bytes = result.Freed, Clean = result };
            }
        }
        else
        {
            lock (saveGate)
            {
                StatEvent? original;
                lock (gate)
                {
                    if (historyLoaded)
                        original = events.FirstOrDefault(e => e.Clean?.RequestId == result.RequestId);
                    else
                        pendingClean.TryGetValue(result.RequestId, out original);
                    if (original is null) return;
                    var updated = original with { Bytes = result.Freed, Clean = result };
                    pendingClean.Remove(result.RequestId);
                    var entry = new JournalEntry(++journalSequence, updated, result.RequestId);
                    pendingJournal.Add(entry);
                    if (historyLoaded) ApplyJournalEntry(entry);
                }
                PersistPendingJournal();
            }
        }
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
            loadedDelta[day] = (loadedDelta.TryGetValue(day, out var s) ? s : 0) + seconds;
            PruneLoadedDelta(now);
            loadedDirty = true;
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
        EnsureHistoryLoaded();
        lock (gate) return events.OrderByDescending(e => e.At).Take(count).ToList();
    }

    public StatEvent? Last(StatKind kind)
    {
        EnsureHistoryLoaded();
        lock (gate) return events.Where(e => e.Kind == kind).MaxBy(e => e.At);
    }

    /// Los últimos 7 días: memoria recuperada al dormir, siestas, horas con modelo
    /// cargado y reinicios del vigilante.
    public StatsSummary Week(DateTime now)
    {
        EnsureHistoryLoaded();
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
                if (loadedDelta.TryGetValue(day, out var delta)) seconds += delta;
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
        EnsureHistoryLoaded();
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

    private void PruneLoadedDelta(DateTime now)
    {
        var limit = now.AddDays(-KeepDays).Date;
        foreach (var day in loadedDelta.Keys.ToList())
        {
            if (DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d < limit)
                loadedDelta.Remove(day);
        }
    }

    public void Flush() => Save(force: true);

    private void Save(bool force, DateTime? now = null)
    {
        if (file is null) return;
        lock (saveGate)
        {
            PersistPendingJournal();
            Dictionary<string, double>? loadedSnapshot = null;
            lock (gate)
            {
                var t = now ?? DateTime.Now;
                if (!loadedDirty || (!force && t - lastLoadedSave < TimeSpan.FromMinutes(1))) return;
                lastLoadedSave = t;
                loadedDirty = false;
                loadedSnapshot = new(loadedDelta);
            }
            if (loadedSnapshot is not null)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(loadedFile!))!);
                    var tmp = loadedFile + ".tmp";
                    using (var stream = File.Create(tmp)) JsonSerializer.Serialize(stream, loadedSnapshot, Json);
                    File.Move(tmp, loadedFile!, overwrite: true);
                }
                catch { lock (gate) loadedDirty = true; }
            }
        }
    }

    // Sequence is committed before append, so a crash can leave gaps but cannot reuse an ID.
    private void PersistPendingJournal()
    {
        if (file is null || eventsFile is null || sequenceFile is null) return;
        JournalEntry[] pending;
        lock (gate) pending = [.. pendingJournal];
        if (pending.Length == 0) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(eventsFile))!);
            var sequenceTmp = sequenceFile + ".tmp";
            File.WriteAllText(sequenceTmp, journalSequence.ToString(CultureInfo.InvariantCulture));
            File.Move(sequenceTmp, sequenceFile, overwrite: true);
            using (var stream = new FileStream(eventsFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
            {
                if (stream.Length > 0)
                {
                    stream.Position = stream.Length - 1;
                    if (stream.ReadByte() != '\n')
                    {
                        stream.Position = stream.Length;
                        stream.WriteByte((byte)'\n');
                    }
                }
                stream.Position = stream.Length;
                foreach (var entry in pending)
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry, Json) + "\n");
                    stream.Write(bytes);
                }
                stream.Flush(flushToDisk: true);
            }
            lock (gate) pendingJournal.RemoveAll(e => e.Sequence <= pending[^1].Sequence);
        }
        catch { }
    }

    private void CompactJournal(long throughSequence)
    {
        if (file is null || eventsFile is null) return;
        try
        {
            var tmp = file + ".tmp";
            lock (gate)
            {
                using var stream = File.Create(tmp);
                JsonSerializer.Serialize(stream, new Data
                {
                    Events = events,
                    Loaded = loaded,
                    JournalSequence = throughSequence,
                }, Json);
            }
            File.Move(tmp, file, overwrite: true);
            File.WriteAllText(eventsFile, string.Empty);
            lock (gate) pendingJournal.RemoveAll(e => e.Sequence <= throughSequence);
        }
        catch { }
    }
}
