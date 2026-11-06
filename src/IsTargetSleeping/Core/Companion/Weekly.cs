using System.Text.Json;

namespace IsTargetSleeping;

public enum WeekEvent { Chat, AgentDone, Focus, Approved, Denied }

/// Lo que ha pasado esta semana con tu mascota, para el resumen de la página Chat: solo contadores, en local (week.json), que empiezan de cero cada lunes.
public sealed class WeekLog
{
    private sealed class State
    {
        public string Monday { get; set; } = "";
        public Dictionary<string, int> Counts { get; set; } = [];
    }

    private readonly string path;
    private readonly Func<DateTime> now;
    private State state;

    public WeekLog(string path, Func<DateTime>? now = null)
    {
        this.path = path;
        this.now = now ?? (() => DateTime.Now);
        try { state = File.Exists(path) ? JsonSerializer.Deserialize<State>(File.ReadAllText(path)) ?? new() : new(); }
        catch { state = new(); }
        Roll();
    }

    public static DateTime MondayOf(DateTime d) => d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    /// Si ya es otra semana, los contadores vuelven a cero.
    private void Roll()
    {
        var monday = MondayOf(now()).ToString("yyyy-MM-dd");
        if (state.Monday == monday) return;
        state = new State { Monday = monday };
    }

    public int Count(WeekEvent e) { Roll(); return state.Counts.GetValueOrDefault(e.ToString()); }

    public void Record(WeekEvent e)
    {
        Roll();
        state.Counts[e.ToString()] = state.Counts.GetValueOrDefault(e.ToString()) + 1;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state));
            File.Move(temp, path, overwrite: true);
        }
        catch { /* sin disco: el resumen de esta sesión sigue, solo no se guarda */ }
    }
}

public enum ChatterLevel { Off, Rare, Normal }

/// La charla ambiente: de vez en cuando la mascota dice una frase corta y amable según su humor. Es una función pura (qué decir y cuándo) para probarla;
/// no usa el modelo (así no gasta RAM ni carga nada) y calla en modo juego, con un permiso pendiente o mientras se escucha.
public static class Chatter
{
    public static ChatterLevel Parse(string? value) => value switch { "rare" => ChatterLevel.Rare, "normal" => ChatterLevel.Normal, _ => ChatterLevel.Off };

    /// Lo mínimo entre dos frases.
    public static TimeSpan Gap(ChatterLevel level) => level == ChatterLevel.Rare ? TimeSpan.FromHours(2) : TimeSpan.FromMinutes(50);

    private static readonly Dictionary<PetMood, string[]> Lines = new()
    {
        [PetMood.Calm] = ["Todo tranquilo por aquí.", "¿Has bebido agua hace poco?", "Estiro las patas un momento. ¿Y tú, estiras las tuyas?"],
        [PetMood.Sleepy] = ["Es tarde… ¿no deberías ir descansando?", "Se me cierran los ojitos. Mañana será otro día."],
        [PetMood.Worried] = ["La memoria anda justa. Si quieres, libera algo de RAM.", "Noto el PC apretado de memoria."],
        [PetMood.Happy] = ["¡Qué buen rato llevamos!", "Me alegra que todo vaya bien."],
    };
    private static readonly string[] Friendly = ["Me gusta pasar el rato contigo.", "Aquí estoy para lo que necesites."];

    /// Todas las frases posibles (para traducirlas y probarlas).
    public static IEnumerable<string> All => Lines.Values.SelectMany(x => x).Concat(Friendly);

    /// La frase (en español, la clave de traducción) que toca decir ahora, o null. Se llama cada minuto: solo habla con algo de probabilidad,
    /// pasado el mínimo entre frases, para que sea rara y nunca machacona.
    public static string? Pick(ChatterLevel level, PetMood mood, int bondLevel, DateTime now, DateTime lastSpoke, bool quiet, Random rnd)
    {
        if (level == ChatterLevel.Off || quiet || mood == PetMood.Alert || now - lastSpoke < Gap(level)) return null;
        if (rnd.NextDouble() > 0.15) return null;
        var pool = Lines[mood].AsEnumerable();
        if (bondLevel >= 2) pool = pool.Concat(Friendly);
        var list = pool.ToArray();
        return list[rnd.Next(list.Length)];
    }
}
