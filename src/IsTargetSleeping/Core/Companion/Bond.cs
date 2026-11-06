using System.Text.Json;

namespace IsTargetSleeping;

public enum BondEvent { Chat, AgentDone, Focus }
public enum PetMood { Calm, Sleepy, Alert, Worried, Happy }

/// El vínculo con la mascota: sube con el uso sano (charlar, que un agente termine, acabar un Pomodoro) y nunca baja, así que ausentarse no
/// se penaliza. Cada tipo de evento tiene un tope diario para que no se «farmee». No premia la RAM liberada (sería incentivar recortes innecesarios).
/// Se guarda en local (bond.json), con el mismo reemplazo atómico que el resto de datos.
public sealed class Bond
{
    /// XP que da cada evento y lo más que cuenta por día.
    private static (int Xp, int Cap) Rule(BondEvent e) => e switch
    {
        BondEvent.Chat => (2, 20),
        BondEvent.AgentDone => (3, 15),
        _ => (5, 15),
    };

    /// XP necesaria para cada nivel (el 0 es «recién conocidos»).
    public static readonly int[] Thresholds = [0, 30, 100, 250, 500];
    public static int MaxLevel => Thresholds.Length - 1;

    private sealed class State
    {
        public int Xp { get; set; }
        public string Day { get; set; } = "";
        public Dictionary<string, int> Today { get; set; } = [];
    }

    private readonly string path;
    private readonly Func<DateTime> now;
    private State state;

    public Bond(string path, Func<DateTime>? now = null)
    {
        this.path = path;
        this.now = now ?? (() => DateTime.Now);
        try { state = File.Exists(path) ? JsonSerializer.Deserialize<State>(File.ReadAllText(path)) ?? new() : new(); }
        catch { state = new(); }   // un archivo roto no debe impedir arrancar: se empieza de cero
    }

    public int Xp => state.Xp;
    public int Level => Thresholds.Count(t => state.Xp >= t) - 1;
    /// Cuánto falta para el siguiente nivel, de 0 a 1 (1 si ya está en el último).
    public double Progress => Level >= MaxLevel ? 1 : (double)(state.Xp - Thresholds[Level]) / (Thresholds[Level + 1] - Thresholds[Level]);

    /// Apunta un evento. Devuelve true si con él sube de nivel.
    public bool Record(BondEvent e)
    {
        var day = now().ToString("yyyy-MM-dd");
        if (state.Day != day) { state.Day = day; state.Today = []; }
        var (xp, cap) = Rule(e);
        var earned = state.Today.GetValueOrDefault(e.ToString());
        var gain = Math.Min(xp, cap - earned);
        if (gain <= 0) return false;
        int before = Level;
        state.Today[e.ToString()] = earned + gain;
        state.Xp += gain;
        Save();
        return Level > before;
    }

    public void Reset()
    {
        state = new();
        try { File.Delete(path); } catch { }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state));
            File.Move(temp, path, overwrite: true);
        }
        catch { /* sin disco: el vínculo de esta sesión sigue, solo no se guarda */ }
    }
}

public static class PetMoods
{
    /// El humor según el contexto, por prioridad: preocupada si la memoria está crítica, alerta si un agente espera tu permiso,
    /// contenta un rato tras algo bueno, con sueño de noche y tranquila el resto del tiempo.
    public static PetMood Of(int hour, bool approvalWaiting, bool memoryCritical, bool recentlyHappy) =>
        memoryCritical ? PetMood.Worried
        : approvalWaiting ? PetMood.Alert
        : recentlyHappy ? PetMood.Happy
        : hour is >= 23 or < 6 ? PetMood.Sleepy
        : PetMood.Calm;

    /// Para el prompt del chat (en inglés): cómo se siente ahora y por qué.
    public static string PromptLine(PetMood mood) => mood switch
    {
        PetMood.Worried => "Right now you feel worried because the computer's memory is critically low.",
        PetMood.Alert => "Right now you feel alert because an AI agent is waiting for the user's permission.",
        PetMood.Happy => "Right now you feel happy because something good just happened.",
        PetMood.Sleepy => "Right now you feel sleepy because it is late at night.",
        _ => "Right now you feel calm.",
    };

    private static readonly string[] LevelNames = ["just met", "companions", "friends", "good friends", "inseparable"];
    public static string BondPromptLine(int level) => $"You and the user are {LevelNames[Math.Clamp(level, 0, LevelNames.Length - 1)]}.";
}
