using static IsTargetSleeping.L10n;

namespace IsTargetSleeping;

/// Otro motor de modelos locales además de Ollama (que sigue en OllamaController).
/// Lo mínimo para una tarjeta compacta en el panel: encender y apagar, qué modelo
/// tiene, cuánta memoria ocupa y cuánto lleva sin uso, y dormirlo solo.
public interface IEngine
{
    string Id { get; }
    string Name { get; }
    bool Experimental { get; }
    /// Se muestra: está instalado o se ha visto en marcha.
    bool Installed { get; }
    Power Power { get; }
    int Port { get; }
    string? Model { get; }
    /// RAM + VRAM que ocupa (0 si no se sabe).
    long Memory { get; }
    double? IdleSeconds { get; }
    /// Minutos sin uso tras los que se duerme; 0 = nunca.
    int IdleMinutes { get; set; }
    string? Error { get; set; }
    string? LogPath { get; }
    bool IsBusy { get; }
    /// «2,1 GB · sin uso 12 min · se duerme en 18 min».
    string IdleLine { get; }
    event Action? Changed;
    /// Se durmió solo por inactividad (con lo que ocupaba).
    event Action<IEngine, long>? AutoSlept;

    Task Refresh();
    Task SetOn(bool on);
    Task Sleep();
}

/// Lo común: el ajuste de inactividad, el contador de «sin uso» y la siesta automática.
public abstract class EngineBase : IEngine
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public virtual bool Experimental => false;
    public abstract bool Installed { get; }
    public Power Power { get; protected set; } = Power.Off;
    public int Port { get; protected set; }
    public string? Model { get; protected set; }
    public long Memory { get; protected set; }
    public double? IdleSeconds { get; protected set; }
    public string? Error { get; set; }
    public virtual string? LogPath => null;
    public bool IsBusy { get; protected set; }
    public bool IsDemo { get; protected set; }
    public event Action? Changed;
    public event Action<IEngine, long>? AutoSlept;

    protected readonly IdleTracker tracker = new();
    private bool refreshing;

    public int IdleMinutes
    {
        get => Defaults.GetInt($"{Id}IdleMinutes", 30);
        set
        {
            if (!IsDemo) Defaults.Set($"{Id}IdleMinutes", value);
            tracker.Touch();
            Notify();
        }
    }

    protected void Notify() => Changed?.Invoke();

    public async Task Refresh()
    {
        if (refreshing || IsDemo) return;
        refreshing = true;
        try
        {
            await Probe();
            if (Power != Power.On || Model is null)
            {
                IdleSeconds = null;
                tracker.Touch();
                return;
            }
            IdleSeconds = tracker.IdleSeconds();
            if (IdleMinutes > 0 && IdleSeconds >= IdleMinutes * 60 && !IsBusy)
            {
                long memory = Memory;
                await Sleep();
                AutoSlept?.Invoke(this, memory);
            }
        }
        catch (Exception e) { Error = e.Message; }
        finally
        {
            refreshing = false;
            Notify();
        }
    }

    /// Lee el estado (procesos, API) y alimenta el contador de inactividad.
    protected abstract Task Probe();

    public abstract Task SetOn(bool on);
    public abstract Task Sleep();

    /// Para las acciones del panel: marca ocupado, recoge el error y refresca.
    protected async Task Run(Func<Task<string?>> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        Error = null;
        Notify();
        try { Error = await action(); }
        catch (Exception e) { Error = e.Message; }
        finally
        {
            IsBusy = false;
            tracker.Touch();
        }
        await Refresh();
    }

    public string IdleLine
    {
        get
        {
            if (Power != Power.On) return "";
            var parts = new List<string>();
            if (Memory > 0) parts.Add(Memory.MemoryGB());
            if (IdleSeconds is { } idle && Model is not null)
            {
                if (idle >= 60) parts.Add(tr("sin uso %d min", (int)(idle / 60)));
                if (IdleMinutes > 0)
                {
                    double left = Math.Max(0, IdleMinutes * 60 - idle);
                    parts.Add(left < 60 ? tr("se duerme en <1 min") : tr("se duerme en %d min", (int)Math.Ceiling(left / 60)));
                }
            }
            return string.Join(" · ", parts);
        }
    }
}
