using Microsoft.Data.Sqlite;

namespace IsTargetSleeping.Pet;

/// SQLite del modulo: estado, ids de acciones vistas, historial de mensajes y recuerdos (solo los que el usuario
/// pide guardar de forma visible). Una sola conexion protegida por un candado: el volumen es minimo.
public sealed class SqlitePetStore : IPetStore, IDisposable
{
    private readonly SqliteConnection db;
    private readonly object gate = new();

    public SqlitePetStore(string path)
    {
        db = new SqliteConnection($"Data Source={path}");
        db.Open();
        Exec("PRAGMA journal_mode=WAL;");
        Exec("CREATE TABLE IF NOT EXISTS state(k TEXT PRIMARY KEY, v TEXT NOT NULL);");
        Exec("CREATE TABLE IF NOT EXISTS seen(id TEXT PRIMARY KEY, ts INTEGER NOT NULL);");
        Exec("CREATE TABLE IF NOT EXISTS messages(id INTEGER PRIMARY KEY AUTOINCREMENT, chat_id TEXT NOT NULL, role TEXT NOT NULL, text TEXT NOT NULL, expression TEXT, ts INTEGER NOT NULL);");
        Exec("CREATE TABLE IF NOT EXISTS activity(id INTEGER PRIMARY KEY AUTOINCREMENT, ts INTEGER NOT NULL, channel TEXT NOT NULL, tool TEXT NOT NULL, summary TEXT NOT NULL, outcome TEXT NOT NULL);");
        Exec("CREATE TABLE IF NOT EXISTS reminders(id INTEGER PRIMARY KEY AUTOINCREMENT, text TEXT NOT NULL, due INTEGER NOT NULL, repeat TEXT NOT NULL, every_min INTEGER NOT NULL, state INTEGER NOT NULL DEFAULT 0, fired_ts INTEGER NOT NULL DEFAULT 0);");
        Exec("CREATE TABLE IF NOT EXISTS reminder_log(seq INTEGER PRIMARY KEY AUTOINCREMENT, rid INTEGER NOT NULL, text TEXT NOT NULL, due INTEGER NOT NULL, ts INTEGER NOT NULL);");
        Exec("CREATE TABLE IF NOT EXISTS memories(id INTEGER PRIMARY KEY AUTOINCREMENT, text TEXT NOT NULL, ts INTEGER NOT NULL);");
    }

    public const int MaxReminders = 100;

    public long AddReminder(string text, long due, string repeat, int everyMin)
    {
        lock (gate)
        {
            Exec("DELETE FROM reminders WHERE state=1 AND fired_ts < $old", ("$old", DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeSeconds()));
            Exec("INSERT INTO reminders(text,due,repeat,every_min) VALUES($t,$d,$r,$e)", ("$t", text), ("$d", due), ("$r", repeat), ("$e", everyMin));
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT last_insert_rowid()";
            long id = (long)cmd.ExecuteScalar()!;
            Exec("DELETE FROM reminders WHERE state=0 AND id <= $x", ("$x", id - MaxReminders));
            return id;
        }
    }

    private static Reminder ReadReminder(SqliteDataReader r) => new(r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.GetString(3), r.GetInt32(4), r.GetInt32(5) == 1, r.GetInt64(6));

    /// Pendientes (por hora) y los que ya sonaron en las ultimas 24 h (para poder verlos o descartarlos).
    public List<Reminder> ListReminders()
    {
        lock (gate)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT id,text,due,repeat,every_min,state,fired_ts FROM reminders WHERE state=0 OR fired_ts > $since ORDER BY state, CASE WHEN state=0 THEN due END, fired_ts DESC";
            cmd.Parameters.AddWithValue("$since", DateTimeOffset.UtcNow.AddHours(-24).ToUnixTimeSeconds());
            var list = new List<Reminder>();
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadReminder(r));
            return list;
        }
    }

    public List<Reminder> DueReminders(long nowTs)
    {
        lock (gate)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT id,text,due,repeat,every_min,state,fired_ts FROM reminders WHERE state=0 AND due <= $n ORDER BY due LIMIT 20";
            cmd.Parameters.AddWithValue("$n", nowTs);
            var list = new List<Reminder>();
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadReminder(r));
            return list;
        }
    }

    /// Ya sono: si se repite, pasa a su siguiente hora; si no, queda como «sonado».
    public void MarkFired(long id, long firedTs, long? nextDue)
    {
        lock (gate)
        {
            if (nextDue is { } n) Exec("UPDATE reminders SET due=$d, fired_ts=$f WHERE id=$i", ("$d", n), ("$f", firedTs), ("$i", id));
            else Exec("UPDATE reminders SET state=1, fired_ts=$f WHERE id=$i", ("$f", firedTs), ("$i", id));
        }
    }

    /// Cada aviso que suena queda en una bitacora numerada: quien estaba desconectado lo recibe al volver (hasta 24 h; se guardan 200).
    public long LogFired(Reminder r, long ts)
    {
        lock (gate)
        {
            Exec("INSERT INTO reminder_log(rid,text,due,ts) VALUES($r,$t,$d,$s)", ("$r", r.Id), ("$t", r.Text), ("$d", r.Due), ("$s", ts));
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT last_insert_rowid()";
            long seq = (long)cmd.ExecuteScalar()!;
            Exec("DELETE FROM reminder_log WHERE seq <= $x", ("$x", seq - 200));
            return seq;
        }
    }

    public long MaxFiredSeq() { lock (gate) { using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT COALESCE(MAX(seq),0) FROM reminder_log"; return (long)cmd.ExecuteScalar()!; } }

    public List<(long Seq, Reminder R)> FiredSince(long seq, long windowSec, long nowTs)
    {
        lock (gate)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT seq,rid,text,due FROM reminder_log WHERE seq > $q AND ts >= $w ORDER BY seq LIMIT 10";
            cmd.Parameters.AddWithValue("$q", seq);
            cmd.Parameters.AddWithValue("$w", nowTs - windowSec);
            var list = new List<(long, Reminder)>();
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add((r.GetInt64(0), new Reminder(r.GetInt64(1), r.GetString(2), r.GetInt64(3), "none", 0, true, 0)));
            return list;
        }
    }

    public void DeleteReminder(long id) { lock (gate) Exec("DELETE FROM reminders WHERE id=$i", ("$i", id)); }

    public const int MaxActivity = 500;

    /// Registro de lo que hizo (o se nego a hacer) la mascota: quien, que y con que resultado. Se recorta solo.
    public void AddActivity(ActivityEntry e)
    {
        lock (gate)
        {
            Exec("INSERT INTO activity(ts,channel,tool,summary,outcome) VALUES($t,$c,$o,$s,$r)",
                ("$t", e.Ts), ("$c", e.Channel), ("$o", e.Tool), ("$s", e.Summary), ("$r", e.Outcome));
            Exec("DELETE FROM activity WHERE id <= (SELECT MAX(id) FROM activity) - $n", ("$n", MaxActivity));
        }
    }

    public List<ActivityEntry> RecentActivity(int count)
    {
        lock (gate)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT ts,channel,tool,summary,outcome FROM activity ORDER BY id DESC LIMIT $n";
            cmd.Parameters.AddWithValue("$n", Math.Clamp(count, 1, MaxActivity));
            var list = new List<ActivityEntry>();
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(new ActivityEntry(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4)));
            return list;
        }
    }

    public void ClearActivity() { lock (gate) Exec("DELETE FROM activity"); }

    private void Exec(string sql, params (string, object)[] ps)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    private string? Get(string key)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT v FROM state WHERE k=$k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    private void Put(string key, string value) =>
        Exec("INSERT INTO state(k,v) VALUES($k,$v) ON CONFLICT(k) DO UPDATE SET v=$v", ("$k", key), ("$v", value));

    public PetSnapshot? LoadState()
    {
        lock (gate)
        {
            if (Get("rev") is not { } rev || !long.TryParse(rev, out long r)) return null;
            string name = Get("name") ?? "Mira";
            int.TryParse(Get("energy"), out int e);
            int.TryParse(Get("affection"), out int a);
            bool asleep = Get("asleep") != "0";
            if (!PetProtocol.IsValidName(name)) name = "Mira";
            return new PetSnapshot(r, name, Math.Clamp(e, 0, 100), Math.Clamp(a, 0, 100), asleep ? "sleep" : "open", asleep);
        }
    }

    public void SaveState(PetSnapshot s, long poseUntilMs)
    {
        lock (gate)
        {
            using var tx = db.BeginTransaction();
            Put("rev", s.Rev.ToString());
            Put("name", s.Name);
            Put("energy", s.Energy.ToString());
            Put("affection", s.Affection.ToString());
            Put("asleep", s.Asleep ? "1" : "0");
            tx.Commit();
        }
    }

    public List<string> LoadSeenIds(int max)
    {
        lock (gate)
        {
            var ids = new List<string>();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT id FROM (SELECT id, ts FROM seen ORDER BY ts DESC LIMIT $n) ORDER BY ts ASC";
            cmd.Parameters.AddWithValue("$n", max);
            using var r = cmd.ExecuteReader();
            while (r.Read()) ids.Add(r.GetString(0));
            return ids;
        }
    }

    public void AddSeenId(string id, long tsMs)
    {
        lock (gate)
        {
            Exec("INSERT OR IGNORE INTO seen(id,ts) VALUES($i,$t)", ("$i", id), ("$t", tsMs));
            // Solo se conservan los 256 mas recientes (mismo tope que la memoria).
            Exec("DELETE FROM seen WHERE id NOT IN (SELECT id FROM seen ORDER BY ts DESC LIMIT 256)");
        }
    }

    // ------------------------------------------------------------ mensajes y recuerdos

    public void AddMessage(string chatId, string role, string text, string? expression, long tsMs)
    {
        lock (gate)
            Exec("INSERT INTO messages(chat_id,role,text,expression,ts) VALUES($c,$r,$t,$e,$ts)",
                ("$c", chatId), ("$r", role), ("$t", text), ("$e", (object?)expression ?? DBNull.Value), ("$ts", tsMs));
    }

    public List<(string ChatId, string Role, string Text, string? Expression, long Ts)> RecentMessages(int max)
    {
        lock (gate)
        {
            var list = new List<(string, string, string, string?, long)>();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT chat_id, role, text, expression, ts FROM (SELECT * FROM messages ORDER BY id DESC LIMIT $n) ORDER BY id ASC";
            cmd.Parameters.AddWithValue("$n", max);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetInt64(4)));
            return list;
        }
    }

    /// Los recuerdos solo se crean por una accion visible del usuario ("recuerda que..."): nunca de forma automatica.
    public long AddMemory(string text, long tsMs)
    {
        lock (gate)
        {
            Exec("INSERT INTO memories(text,ts) VALUES($t,$ts)", ("$t", text), ("$ts", tsMs));
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT last_insert_rowid()";
            return (long)cmd.ExecuteScalar()!;
        }
    }

    public List<(long Id, string Text, long Ts)> Memories()
    {
        lock (gate)
        {
            var list = new List<(long, string, long)>();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT id, text, ts FROM memories ORDER BY id";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add((r.GetInt64(0), r.GetString(1), r.GetInt64(2)));
            return list;
        }
    }

    public void EditMemory(long id, string text) { lock (gate) Exec("UPDATE memories SET text=$t WHERE id=$i", ("$t", text), ("$i", id)); }
    public void DeleteMemory(long id) { lock (gate) Exec("DELETE FROM memories WHERE id=$i", ("$i", id)); }

    public void Dispose() { lock (gate) db.Dispose(); }
}
