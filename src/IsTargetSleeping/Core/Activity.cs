namespace IsTargetSleeping;

/// Ollama no expone cuándo fue la última petición, pero su runner (un proceso por
/// modelo cargado) solo gasta CPU mientras procesa. Medido con Ollama 0.34,
/// una RTX 4060 y gemma-4 E2B: 0,008 s cada 2,5 s en reposo frente a
/// decenas de segundos al generar, aunque el modelo esté entero en la GPU. Con
/// esa diferencia basta mirar cuánto CPU sumó desde la última muestra.
public static class ProcessCpu
{
    /// Runners de Ollama. Según la versión: `llama-server.exe` dentro de
    /// …\Ollama\lib\ollama (0.34+), `ollama.exe runner …` o el antiguo
    /// `ollama_llama_server.exe`. Un llama-server propio (fuera de Ollama) no cuenta,
    /// aunque sea el mismo .exe de la carpeta de Ollama: lo que decide es que lo
    /// haya lanzado `ollama.exe`.
    public static List<int> RunnerPids()
    {
        var pids = new List<int>();
        foreach (var p in Procs.ByName("ollama", "llama-server", "ollama_llama_server"))
        {
            bool runner = p.Name switch
            {
                "ollama" => Procs.Args(p.CommandLine).Contains("runner"),
                "llama-server" => IsOllamaChild(p.Pid, p.CommandLine),
                _ => true,
            };
            if (runner) pids.Add(p.Pid);
        }
        return pids;
    }

    public static bool IsOllamaChild(int pid, string commandLine)
    {
        if (Procs.ParentPid(pid) is { } parent && Procs.Name(parent) is { } name)
            return name.Equals("ollama", StringComparison.OrdinalIgnoreCase);
        // Sin padre vivo (lo lanzó una terminal que ya se cerró): es de Ollama si está
        // en su carpeta y lleva las opciones con que Ollama lanza sus runners (0.35:
        // `--no-webui --offline --port <aleatorio>`).
        var args = Procs.Args(commandLine);
        return (Procs.ImagePath(pid) ?? commandLine).Contains("ollama", StringComparison.OrdinalIgnoreCase)
               && (args.Contains("--no-webui") || args.Contains("--offline"));
    }

    public static double? Seconds(int pid) => Procs.CpuSeconds(pid);

    /// RAM privada de los runners: lo que el modelo ocupa de verdad en la memoria
    /// del sistema. Con GPU dedicada Ollama dice que todo está en VRAM, pero el
    /// runner reserva igualmente gigas de RAM (buffers del host de CUDA, contexto…).
    public static long RunnerMemory() => RunnerPids().Sum(pid => Procs.PrivateBytes(pid) ?? 0);
}

/// Lógica pura del «sin uso», separada de los procesos para poder probarla.
public sealed class IdleTracker
{
    /// CPU por encima de la cual una muestra cuenta como uso. El reposo medido
    /// ronda 0,005 s cada 2,5 s; generar unos pocos tokens ya supera 0,1 s.
    public const double ActivityThreshold = 0.08;

    public DateTime LastActivity { get; private set; }
    private Dictionary<int, double> lastCpu = new();
    private HashSet<string> lastModels = new();

    public IdleTracker(DateTime? now = null) { LastActivity = now ?? DateTime.Now; }

    /// `cpu` son los segundos acumulados por cada runner vivo en este instante.
    public void Sample(Dictionary<int, double> cpu, HashSet<string> models, DateTime? now = null)
    {
        var t = now ?? DateTime.Now;
        try
        {
            // Un modelo nuevo o un runner que cambia (recarga) es uso aunque aún no gaste CPU.
            if (!models.SetEquals(lastModels) || !cpu.Keys.ToHashSet().SetEquals(lastCpu.Keys))
            {
                LastActivity = t;
                return;
            }
            double delta = 0;
            foreach (var (pid, seconds) in cpu)
            {
                delta += Math.Max(0, seconds - (lastCpu.TryGetValue(pid, out var before) ? before : seconds));
            }
            if (delta >= ActivityThreshold) LastActivity = t;
        }
        finally
        {
            lastCpu = new Dictionary<int, double>(cpu);
            lastModels = new HashSet<string>(models);
        }
    }

    /// Algo que el usuario hizo a mano (cargar desde el panel) también cuenta.
    public void Touch(DateTime? now = null) => LastActivity = now ?? DateTime.Now;

    public double IdleSeconds(DateTime? now = null) => ((now ?? DateTime.Now) - LastActivity).TotalSeconds;
}
