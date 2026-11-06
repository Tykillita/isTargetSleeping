namespace IsTargetSleeping;

public enum PomodoroPhase { Focus, Break }

/// El temporizador Pomodoro sin reloj propio: todo se calcula con el instante que le pasan, así se prueba sin esperar.
/// Enfoque 25 min, descanso 5; al acabar una fase empieza la otra (pausada) y `Tick` avisa de que acabó.
public sealed class Pomodoro
{
    public static readonly TimeSpan FocusLength = TimeSpan.FromMinutes(25);
    public static readonly TimeSpan BreakLength = TimeSpan.FromMinutes(5);
    /// Tras cuatro enfoques, un descanso largo; luego el ciclo empieza otra vez.
    public static readonly TimeSpan LongBreakLength = TimeSpan.FromMinutes(15);
    public const int SessionsPerCycle = 4;

    public PomodoroPhase Phase { get; private set; } = PomodoroPhase.Focus;
    /// Enfoques terminados en el ciclo actual (0 a 4).
    public int Completed { get; private set; }
    /// El descanso que toca (o que corre) es el largo.
    public bool LongBreak { get; private set; }
    /// La sesión del ciclo en la que se está, de 1 a 4 (en un descanso, la que acaba de terminar).
    public int Session => Math.Clamp(Phase == PomodoroPhase.Focus ? Completed + 1 : Completed, 1, SessionsPerCycle);
    public bool Running { get; private set; }
    private TimeSpan left = FocusLength;
    private DateTime lastTick;

    public TimeSpan Left => left;
    public TimeSpan Length => Phase == PomodoroPhase.Focus ? FocusLength : LongBreak ? LongBreakLength : BreakLength;
    /// 0 al empezar la fase y 1 al acabar.
    public double Progress => 1 - left.TotalSeconds / Length.TotalSeconds;

    public void Start(DateTime now)
    {
        if (Running) return;
        Running = true;
        lastTick = now;
    }

    public void Pause(DateTime now)
    {
        if (!Running) return;
        Tick(now);
        Running = false;
    }

    public void Toggle(DateTime now)
    {
        if (Running) Pause(now);
        else Start(now);
    }

    public void Reset()
    {
        Running = false;
        Phase = PomodoroPhase.Focus;
        Completed = 0;
        LongBreak = false;
        left = FocusLength;
    }

    /// Modo manual: pasa a enfoque o a descanso corto, parado y con la duración completa. No cuenta como terminada la fase que se deja.
    public void Select(PomodoroPhase phase)
    {
        Phase = phase;
        LongBreak = false;
        left = Length;
        Running = false;
    }

    /// Vuelve la fase actual a su duración completa, parada (el ciclo y los enfoques hechos no cambian).
    public void Restart()
    {
        left = Length;
        Running = false;
    }

    /// Salta a la fase siguiente sin contar la actual como terminada (un enfoque saltado no suma; saltar el descanso largo cierra el ciclo).
    public void Skip(DateTime now)
    {
        Tick(now);
        if (Phase == PomodoroPhase.Break && LongBreak) Completed = 0;
        Phase = Phase == PomodoroPhase.Focus ? PomodoroPhase.Break : PomodoroPhase.Focus;
        LongBreak = false;
        left = Length;
        Running = false;
    }

    /// Descuenta el tiempo pasado. Devuelve true si acaba de terminar una fase (la siguiente queda lista y en pausa).
    public bool Tick(DateTime now)
    {
        if (!Running) return false;
        left -= now - lastTick;
        lastTick = now;
        if (left > TimeSpan.Zero) return false;
        if (Phase == PomodoroPhase.Focus) { Completed++; LongBreak = Completed >= SessionsPerCycle; }
        else { if (LongBreak) Completed = 0; LongBreak = false; }
        Phase = Phase == PomodoroPhase.Focus ? PomodoroPhase.Break : PomodoroPhase.Focus;
        left = Length;
        Running = false;
        return true;
    }

    public static string Format(TimeSpan span)
    {
        var total = (int)Math.Ceiling(Math.Max(0, span.TotalSeconds));
        return $"{total / 60:00}:{total % 60:00}";
    }
}

/// Porcentaje de uso de CPU a partir de dos lecturas de `GetSystemTimes` (idle, kernel y user acumulados;
/// el kernel ya incluye el tiempo en reposo).
public static class CpuMath
{
    public static double Percent(ulong idle0, ulong kernel0, ulong user0, ulong idle1, ulong kernel1, ulong user1)
    {
        var total = (kernel1 - kernel0) + (user1 - user0);
        if (total == 0) return 0;
        var busy = total - (idle1 - idle0);
        return Math.Clamp(busy * 100.0 / total, 0, 100);
    }
}
