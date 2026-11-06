using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace IsTargetSleeping;

/// Qué se hizo en cada paso de una ejecución (lo que enseña el historial; nunca el prompt ni el razonamiento del agente).
public sealed record TeamStepSummary(string Agent, string Role, string? Model, bool Ok, int Seconds, long Tokens);

/// Una ejecución terminada: lo que el historial del widget enseña y lo que permite fusionar, descartar o pedir cambios después.
public sealed record TeamHistoryEntry
{
    public string Id { get; init; } = "";
    /// La rama es `team/<BranchId>`. Un seguimiento reutiliza la rama y la copia de trabajo de la ejecución anterior.
    public string BranchId { get; init; } = "";
    public string? ParentId { get; init; }
    public string Root { get; init; } = "";
    public string TreeDir { get; init; } = "";
    public string BaseSha { get; init; } = "";
    public string Title { get; init; } = "";
    /// El texto completo de la tarea (para reintentarla): se guarda en local, en el historial, y se borra con él.
    public string Task { get; init; } = "";
    public string Preset { get; init; } = "";
    /// El veredicto de la última revisión (Approved, Warning, Blocked) o vacío si no hubo.
    public string Verdict { get; init; } = "";
    public DateTime Started { get; init; }
    public int Seconds { get; init; }
    /// Done, NeedsUser, Failed o Cancelled.
    public string Outcome { get; init; } = "";
    public long Tokens { get; init; }
    /// Solo lo que informa Claude (Codex no da coste).
    public double CostUsd { get; init; }
    public int Files { get; init; }
    public int Added { get; init; }
    public int Removed { get; init; }
    /// El informe del último paso, recortado.
    public string Report { get; init; } = "";
    public string? ScheduleId { get; init; }
    /// Open (la rama sigue ahí), Merged o Discarded.
    public string State { get; init; } = "Open";
    public List<TeamStepSummary> Steps { get; init; } = [];
    [JsonIgnore] public string? StoreError { get; init; }
}

public enum ScheduleKind { Once, Daily, Weekdays, Weekly, EveryHours }
public enum ScheduleCheck { NotYet, Due, Missed }
public enum ScheduleClaimResult { Claimed, Busy, NotDue, Missed, NotFound, StorageError }

/// Una tarea del equipo que se repite sola. Solo corre mientras la app está abierta (o por `--team-run`, que se puede lanzar desde
/// el Programador de tareas de Windows). Nunca fusiona: deja la rama y avisa.
public sealed record TeamSchedule
{
    /// Si la app estuvo cerrada más de esto a la hora de una tarea, no se ejecuta tarde (salvo que se pida recuperarla).
    public static readonly TimeSpan MissWindow = TimeSpan.FromHours(12);
    public const long DefaultMaxTokens = 300_000;

    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; init; } = "";
    public string Prompt { get; init; } = "";
    public string Root { get; init; } = "";
    public string Preset { get; init; } = nameof(TeamPreset.Auto);
    public ScheduleKind Kind { get; init; } = ScheduleKind.Daily;
    /// HH:mm local.
    public string Time { get; init; } = "09:00";
    /// Lunes = bit 0 … domingo = bit 6 (solo para Weekly).
    public int DaysMask { get; init; } = 0b0000001;
    public int EveryHours { get; init; } = 6;
    /// Para Once.
    public DateTime? At { get; init; }
    public bool Enabled { get; init; } = true;
    public bool CatchUp { get; init; }
    public long MaxTokens { get; init; } = DefaultMaxTokens;
    public DateTime Created { get; init; } = DateTime.Now;
    public DateTime? LastRun { get; init; }
    public string? LastOutcome { get; init; }
    /// Fallos seguidos de esta tarea. Al llegar a `FailureLimit` se apaga sola (disyuntor): una tarea que falla una y otra vez gasta tokens sin hacer nada.
    public int ConsecutiveFailures { get; init; }
    public const int FailureLimit = 10;
    public bool PausedByFailures => !Enabled && ConsecutiveFailures >= FailureLimit;

    /// Apunta cómo acabó una ejecución. Un fallo suma, un éxito pone la cuenta a cero y cancelar o perderse una ejecución no cuenta; al décimo fallo seguido se apaga.
    public TeamSchedule WithOutcome(string? outcome)
    {
        int failures = outcome == "Failed" ? ConsecutiveFailures + 1 : outcome == "Done" ? 0 : ConsecutiveFailures;
        return this with { LastOutcome = outcome, ConsecutiveFailures = failures, Enabled = failures >= FailureLimit ? false : Enabled };
    }

    /// El interruptor a mano: al encenderla se vuelve a empezar de cero.
    public TeamSchedule Toggled() => Enabled ? this with { Enabled = false } : this with { Enabled = true, ConsecutiveFailures = 0 };

    public TimeSpan TimeOfDay => TimeSpan.TryParse(Time, out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1) ? t : TimeSpan.FromHours(9);

    /// La primera ocasión posterior a <paramref name="from"/> (estrictamente), o null si ya no hay más.
    public DateTime? NextAfter(DateTime from)
    {
        switch (Kind)
        {
            case ScheduleKind.Once: return At is { } at && at > from ? at : null;
            case ScheduleKind.EveryHours: return from.AddHours(Math.Max(1, EveryHours));
        }
        for (int day = 0; day <= 8; day++)
        {
            var candidate = from.Date.AddDays(day) + TimeOfDay;
            if (candidate <= from) continue;
            bool ok = Kind switch
            {
                ScheduleKind.Weekdays => candidate.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday),
                ScheduleKind.Weekly => (DaysMask & (1 << (((int)candidate.DayOfWeek + 6) % 7))) != 0,
                _ => true,
            };
            if (ok) return candidate;
        }
        return null;
    }

    public DateTime? Next => NextAfter(LastRun ?? Created);

    public ScheduleCheck Check(DateTime now)
    {
        if (!Enabled || Prompt.Trim().Length == 0 || Root.Length == 0) return ScheduleCheck.NotYet;
        if (Next is not { } next || next > now) return ScheduleCheck.NotYet;
        return now - next > MissWindow && !CatchUp ? ScheduleCheck.Missed : ScheduleCheck.Due;
    }
}

/// Historial y tareas programadas, en JSON dentro de la carpeta de datos. Todo local; el historial guarda el título (el comienzo de la
/// tarea) y el informe final, y se puede borrar entero.
public sealed class TeamStore
{
    public const int MaxHistory = 100;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly object gate = new();
    private readonly string dir;
    private readonly string mutexName;

    public TeamStore(string dir)
    {
        this.dir = Path.GetFullPath(dir);
        mutexName = "Local\\isTargetSleeping.TeamStore." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(this.dir.ToUpperInvariant())))[..24];
    }

    /// La carpeta de datos del equipo (se llama así para no tapar a System.IO.Directory dentro de la clase).
    public string Folder => dir;

    public static TeamStore Default => new(Path.Combine(Paths.DataDir, "team"));

    /// Error de la última escritura que no se pudo completar; se limpia con la siguiente escritura correcta.
    public string? LastError { get; private set; }

    private string HistoryFile => Path.Combine(dir, "history.json");
    private string SchedulesFile => Path.Combine(dir, "schedules.json");

    private List<T> Load<T>(string file)
    {
        lock (gate)
        using (ProcessLock())
        {
            try
            {
                if (!File.Exists(file)) { LastError = null; return []; }
                var items = JsonSerializer.Deserialize<List<T>>(File.ReadAllText(file));
                if (items is null) throw new InvalidDataException("El archivo no contiene una lista válida.");
                LastError = null;
                return items;
            }
            catch (Exception e) { LastError = e.Message; return []; }
        }
    }

    private bool Save<T>(string file, List<T> items)
    {
        lock (gate)
        using (ProcessLock())
        {
            var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(dir);
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(items, Json));
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temp, file, overwrite: true);
                LastError = null;
                return true;
            }
            catch (Exception e) { LastError = e.Message; return false; }
            finally { try { File.Delete(temp); } catch { } }
        }
    }

    private IDisposable ProcessLock()
    {
        var mutex = new Mutex(false, mutexName);
        try { mutex.WaitOne(); } catch (AbandonedMutexException) { }
        return new MutexLease(mutex);
    }

    private sealed class MutexLease(Mutex mutex) : IDisposable
    {
        public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
    }

    /// Lo más reciente primero.
    public List<TeamHistoryEntry> LoadHistory() => [.. Load<TeamHistoryEntry>(HistoryFile).OrderByDescending(e => e.Started)];

    public bool SaveHistory(TeamHistoryEntry entry)
    {
        lock (gate)
        using (ProcessLock())
        {
            var all = Load<TeamHistoryEntry>(HistoryFile);
            if (LastError is not null) return false;
            all.RemoveAll(e => e.Id == entry.Id);
            all.Add(entry);
            return Save(HistoryFile, [.. all.OrderByDescending(e => e.Started).Take(MaxHistory)]);
        }
    }

    public bool SetState(string id, string state)
    {
        lock (gate)
        using (ProcessLock())
        {
            var all = Load<TeamHistoryEntry>(HistoryFile);
            if (LastError is not null) return false;
            for (int i = 0; i < all.Count; i++)
                if (all[i].Id == id || all[i].BranchId == id && state != "Open") all[i] = all[i] with { State = state };
            return Save(HistoryFile, all);
        }
    }

    public bool ClearHistory() => Save(HistoryFile, new List<TeamHistoryEntry>());

    public List<TeamSchedule> LoadSchedules() => Load<TeamSchedule>(SchedulesFile);
    public bool SaveSchedules(List<TeamSchedule> schedules) => Save(SchedulesFile, schedules);

    public bool UpsertSchedule(TeamSchedule schedule)
    {
        lock (gate)
        using (ProcessLock())
        {
            var all = LoadSchedules();
            if (LastError is not null) return false;
            int i = all.FindIndex(s => s.Id == schedule.Id);
            if (i >= 0) all[i] = schedule; else all.Add(schedule);
            return SaveSchedules(all);
        }
    }

    public bool DeleteSchedule(string id)
    {
        lock (gate)
        using (ProcessLock()) return SaveSchedules([.. LoadSchedules().Where(s => s.Id != id)]);
    }

    /// Reserva la ejecución de una programación usando un archivo compartido cuyo bloqueo se libera también si el proceso termina.
    public ScheduleClaimResult ClaimSchedule(string id, DateTime now, bool runNow, out TeamSchedule? claimed, out IDisposable? lease)
    {
        claimed = null;
        lease = null;
        lock (gate)
        using (ProcessLock())
        {
            var all = LoadSchedules();
            var index = all.FindIndex(s => s.Id == id);
            if (index < 0) return ScheduleClaimResult.NotFound;
            var schedule = all[index];
            var due = schedule.Check(now);
            if (!runNow && due == ScheduleCheck.NotYet) return ScheduleClaimResult.NotDue;

            FileStream reservation;
            try
            {
                Directory.CreateDirectory(Path.Combine(dir, "schedule-leases"));
                var leaseName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + ".lock";
                reservation = new FileStream(Path.Combine(dir, "schedule-leases", leaseName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) { return ScheduleClaimResult.Busy; }
            catch (Exception e) { LastError = e.Message; return ScheduleClaimResult.StorageError; }

            var keepLease = false;
            try
            {
                if (!runNow && due == ScheduleCheck.Missed)
                {
                    all[index] = schedule with { LastRun = now, LastOutcome = "Missed" };
                    return Save(SchedulesFile, all) ? ScheduleClaimResult.Missed : ScheduleClaimResult.StorageError;
                }

                var started = schedule with { LastRun = now, LastOutcome = "Running", Enabled = schedule.Kind != ScheduleKind.Once && schedule.Enabled };
                all[index] = started;
                if (!Save(SchedulesFile, all)) return ScheduleClaimResult.StorageError;
                claimed = started;
                lease = reservation;
                keepLease = true;
                return ScheduleClaimResult.Claimed;
            }
            finally { if (!keepLease) reservation.Dispose(); }
        }
    }
}
