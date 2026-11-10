namespace IsTargetSleeping.Pet;

/// Persistencia del estado autoritativo (SQLite en produccion; memoria en pruebas).
public interface IPetStore
{
    PetSnapshot? LoadState();
    void SaveState(PetSnapshot s, long poseUntilMs);
    List<string> LoadSeenIds(int max);
    void AddSeenId(string id, long tsMs);
}

public enum ApplyOutcome { Applied, Duplicate, Ignored }

public readonly record struct ApplyResult(ApplyOutcome Outcome, long Rev);

/// Estado de la mascota en la PC: autoridad con revision, deduplicacion por id y cola de acciones ya vistas acotada.
/// Las acciones muy viejas (placa sin conexion mas de 8 h) se acusan pero no cambian las necesidades.
public sealed class PetService
{
    public const int MaxSeen = 256;
    public const int MaxAbsenceMinutes = 480;
    public const int NeedFloor = 10;
    public const int TapAffection = 6, PlayAffection = 4, PlayEnergy = -3, RestEnergy = 12, WakeAffection = 2;
    public const int PoseHoldMs = 1500;

    private readonly object gate = new();
    private readonly IPetStore store;
    private readonly Func<DateTimeOffset> clock;
    private readonly HashSet<string> seen = [];
    private readonly Queue<string> seenOrder = new();
    private long rev;
    private string name = "Nimbo";
    private int energy = 80, affection = 60;
    private bool asleep = true;
    private string lastPose = "sleep";
    private long poseUntilMs;
    private long lastElapseMs;

    public event Action<PetSnapshot>? Changed;

    public PetService(IPetStore store, Func<DateTimeOffset>? clock = null)
    {
        this.store = store;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        var saved = store.LoadState();
        if (saved is not null)
        {
            rev = saved.Rev; name = saved.Name; energy = saved.Energy; affection = saved.Affection; asleep = saved.Asleep;
            lastPose = asleep ? "sleep" : "open";
        }
        foreach (var id in store.LoadSeenIds(MaxSeen)) Remember(id);
        lastElapseMs = Now();
    }

    private long Now() => this.clock().ToUnixTimeMilliseconds();

    public PetSnapshot Snapshot
    {
        get { lock (gate) return Build(); }
    }

    private PetSnapshot Build()
    {
        string pose = Now() < poseUntilMs ? lastPose : (asleep ? "sleep" : "open");
        return new PetSnapshot(rev, name, energy, affection, pose, asleep);
    }

    private void Remember(string id)
    {
        if (!seen.Add(id)) return;
        seenOrder.Enqueue(id);
        while (seenOrder.Count > MaxSeen) seen.Remove(seenOrder.Dequeue());
    }

    private static int Clamp(int v) => Math.Clamp(v, 0, 100);
    private static int Floor(int v) => Math.Clamp(v, NeedFloor, 100);

    /// Aplica una accion de la placa (o de la web). Reenviar el mismo id no cambia nada.
    public ApplyResult Apply(ActionMsg a)
    {
        PetSnapshot snap;
        lock (gate)
        {
            if (seen.Contains(a.Id)) return new ApplyResult(ApplyOutcome.Duplicate, rev);
            Remember(a.Id);
            store.AddSeenId(a.Id, Now());

            bool tooOld = a.AgeS > MaxAbsenceMinutes * 60;  // tope: no cuenta para las necesidades
            bool fresh = a.AgeS < 5;
            long hold = fresh ? Now() + PoseHoldMs : 0;
            switch (a.Kind)
            {
                case "tap":
                    if (asleep) { asleep = false; if (!tooOld) affection = Clamp(affection + WakeAffection); SetPose("happy", hold); }
                    else { if (!tooOld) affection = Clamp(affection + TapAffection); SetPose("happy", hold); }
                    break;
                case "play":
                    if (!tooOld) { energy = Clamp(energy + PlayEnergy); affection = Clamp(affection + PlayAffection); }
                    asleep = false;
                    SetPose("smile", hold);
                    break;
                case "dizzy":
                    asleep = false;
                    SetPose("squint", hold);
                    break;
                case "rest":
                    if (!tooOld) energy = Clamp(energy + RestEnergy);
                    asleep = true;
                    SetPose("sleep", 0);
                    break;
                case "wake":
                    asleep = false;
                    if (!tooOld) affection = Clamp(affection + WakeAffection);
                    SetPose("open", hold);
                    break;
                case "rename":
                    if (!PetProtocol.IsValidName(a.Name)) return new ApplyResult(ApplyOutcome.Ignored, rev);
                    name = a.Name!;
                    break;
                default:
                    return new ApplyResult(ApplyOutcome.Ignored, rev);
            }
            rev++;
            snap = Build();
            store.SaveState(snap, poseUntilMs);
        }
        Changed?.Invoke(snap);
        return new ApplyResult(ApplyOutcome.Applied, snap.Rev);
    }

    private void SetPose(string pose, long untilMs) { lastPose = pose; poseUntilMs = untilMs; }

    /// Avanza el tiempo de las necesidades (cada minuto o al arrancar tras una ausencia). Nunca baja de NeedFloor.
    public void Elapse(int minutes)
    {
        if (minutes <= 0) return;
        minutes = Math.Min(minutes, MaxAbsenceMinutes);
        PetSnapshot snap;
        lock (gate)
        {
            int de = asleep ? minutes / 6 : -(minutes / 15);
            int da = -(minutes / 20);
            int e2 = Floor(energy + de), a2 = Floor(affection + da);
            if (e2 == energy && a2 == affection) return;
            energy = e2; affection = a2;
            rev++;
            snap = Build();
            store.SaveState(snap, poseUntilMs);
        }
        Changed?.Invoke(snap);
    }

    /// Llamar cada ~30 s: cuenta los minutos transcurridos desde la ultima llamada.
    public void Tick()
    {
        long now = Now();
        int minutes;
        lock (gate)
        {
            minutes = (int)((now - lastElapseMs) / 60000);
            if (minutes > 0) lastElapseMs += minutes * 60000L;
        }
        Elapse(minutes);
    }
}
