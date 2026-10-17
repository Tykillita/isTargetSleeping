using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Windows.Input;
using System.Windows.Threading;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// Un widget colocado en un sitio: el Home o la vista al pasar el ratón, con una o dos celdas. Su plantilla elige la variante.
public sealed class WidgetSlot(WidgetModel model, int span, bool peek, bool alone = false)
{
    public WidgetModel Model { get; } = model;
    public string Id => Model.Id;
    public bool Wide => span == 2;
    public bool Compact => !Wide;
    /// Es el único widget de su lista: llena su celda con una vista grande en vez de dejar sitio vacío.
    public bool Alone => alone;
    public bool WideAlone => Wide && alone && !peek;   // en la vista corta se queda baja
    public bool WideCompanion => Wide && !WideAlone;
    /// En la vista al pasar el ratón los widgets van más densos (sin lo secundario).
    public bool Peek { get; } = peek;
    public bool Full => !Peek;
    /// Una celda mide 294 y dos 600: con 6 de margen a cada lado, dos de una celda caben en una fila de 640.
    public double CardWidth => Wide ? 600 : 294;
}

/// Elige la plantilla de cada widget por su tipo (App.xaml: WidgetOllamaWidget, WidgetSystemWidget…).
public sealed class WidgetTemplateSelector : System.Windows.Controls.DataTemplateSelector
{
    public override System.Windows.DataTemplate? SelectTemplate(object? item, System.Windows.DependencyObject container) =>
        item is WidgetSlot slot ? System.Windows.Application.Current.TryFindResource("Widget" + slot.Model.GetType().Name) as System.Windows.DataTemplate : null;
}

/// Un widget del Home: su modelo se pinta con la plantilla de su tipo (App.xaml).
public abstract class WidgetModel(WidgetInfo info) : INotifyPropertyChanged
{
    public string Id => info.Id;
    public string Title => info.Title;
    public string Glyph => info.Glyph;
    /// Los que siguen corriendo con el Home cerrado (el temporizador, mantener despierto).
    public virtual bool Always => false;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    protected void RaiseProperty(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public virtual void Tick(DateTime now) { }
}

public sealed class OllamaWidget(PanelViewModel panel) : WidgetModel(WidgetCatalog.Find("ollama")!)
{
    public PanelViewModel Panel { get; } = panel;
}

/// CPU, RAM, GPU, disco y red: la RAM y la GPU ya las muestrea el panel; el resto, aquí cada segundo.
public sealed class SystemWidget(PanelViewModel panel) : WidgetModel(WidgetCatalog.Find("system")!)
{
    public PanelViewModel Panel { get; } = panel;
    public string CpuTitle => tr("CPU");
    public string DiskTitle => tr("Disco");
    public string NetTitle => tr("Red");
    public double CpuFraction { get; private set; }
    public string CpuLabel => $"{CpuFraction * 100:0} %";
    public double DiskFraction { get; private set; }
    public string DiskLabel { get; private set; } = "";
    public string NetLabel { get; private set; } = "";

    private Win32.FILETIME idle, kernel, user;
    private bool hasCpu;
    private long netReceived, netSent;
    private DateTime netAt;
    private int ticks;

    public override void Tick(DateTime now)
    {
        if (Win32.GetSystemTimes(out var i, out var k, out var u))
        {
            if (hasCpu) CpuFraction = CpuMath.Percent(idle.Value, kernel.Value, user.Value, i.Value, k.Value, u.Value) / 100;
            (idle, kernel, user, hasCpu) = (i, k, u, true);
        }
        if (ticks++ % 10 == 0) ReadDisk();
        ReadNetwork(now);
        Raise();
    }

    private void ReadDisk()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            var used = drive.TotalSize - drive.AvailableFreeSpace;
            DiskFraction = (double)used / drive.TotalSize;
            DiskLabel = $"{used / 1_073_741_824.0:0} / {drive.TotalSize / 1_073_741_824.0:0} GB";
        }
        catch { DiskLabel = tr("No disponible"); }
    }

    private void ReadNetwork(DateTime now)
    {
        try
        {
            long received = 0, sent = 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var stats = nic.GetIPStatistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }
            var seconds = (now - netAt).TotalSeconds;
            if (netAt != default && seconds > 0)
                NetLabel = $"↓ {Rate((received - netReceived) / seconds)}   ↑ {Rate((sent - netSent) / seconds)}";
            (netReceived, netSent, netAt) = (received, sent, now);
        }
        catch { NetLabel = tr("No disponible"); }
    }

    public static string Rate(double bytesPerSecond) => bytesPerSecond switch
    {
        < 1024 => $"{Math.Max(0, bytesPerSecond):0} B/s",
        < 1_048_576 => $"{bytesPerSecond / 1024:0} KB/s",
        _ => $"{bytesPerSecond / 1_048_576:0.0} MB/s",
    };
}

public sealed class PomodoroWidget : WidgetModel
{
    private readonly Pomodoro timer = new();
    public event Action<string, string>? Finished;
    /// Solo cuando arranca, se pausa, se reinicia o acaba una fase (no cada segundo).
    public event Action? StateChanged;

    public PomodoroWidget() : base(WidgetCatalog.Find("pomodoro")!)
    {
        Toggle = new Command(() => { timer.Toggle(DateTime.Now); Raise(); StateChanged?.Invoke(); });
        Reset = new Command(() => { timer.Reset(); Raise(); StateChanged?.Invoke(); });
    }

    public override bool Always => true;
    public bool Running => timer.Running;
    public ICommand Toggle { get; }
    public ICommand Reset { get; }
    public string Remaining => Pomodoro.Format(timer.Left);
    public double Progress => timer.Progress;
    public string PhaseLabel => timer.Phase == PomodoroPhase.Focus ? tr("Enfoque") : tr("Descanso");
    public string ToggleGlyph => timer.Running ? "" : "";   // pausa / reproducir
    public string ToggleHelp => timer.Running ? tr("Pausar el temporizador") : tr("Iniciar el temporizador");
    public string ResetHelp => tr("Reiniciar el temporizador");

    public override void Tick(DateTime now)
    {
        if (timer.Tick(now))
        {
            // La fase que acaba de empezar es la siguiente: si ahora toca descanso, acabó el enfoque.
            Finished?.Invoke(tr("Pomodoro"), timer.Phase == PomodoroPhase.Break
                ? tr("Enfoque terminado: toca descansar 5 minutos.") : tr("Descanso terminado: a trabajar."));
            StateChanged?.Invoke();
        }
        Raise();
    }
}

/// Impide que Windows suspenda el PC o apague la pantalla mientras está activo (no sobrevive a reiniciar la app).
public sealed class StayAwakeWidget : WidgetModel
{
    public StayAwakeWidget() : base(WidgetCatalog.Find("awake")!) => Toggle = new Command(Set);

    public bool On { get; private set; }
    public ICommand Toggle { get; }
    public string State => On ? tr("Activado: el PC y la pantalla no se suspenden.") : tr("Desactivado: Windows decide cuándo suspender.");
    public string ToggleHelp => On ? tr("Dejar que Windows suspenda") : tr("Mantener el PC despierto");

    private void Set()
    {
        On = !On;
        Win32.SetThreadExecutionState(On
            ? Win32.ES_CONTINUOUS | Win32.ES_SYSTEM_REQUIRED | Win32.ES_DISPLAY_REQUIRED
            : Win32.ES_CONTINUOUS);
        Raise();
    }
}

/// Un bloc de texto local: se guarda en notes.txt de la carpeta de datos (nunca sale del PC). En las capturas no toca el disco.
public sealed class NotesWidget : WidgetModel
{
    private readonly bool persist;
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private string text = "";

    public NotesWidget(bool persist) : base(WidgetCatalog.Find("notes")!)
    {
        this.persist = persist;
        if (persist)
        {
            try { if (File.Exists(FilePath)) text = File.ReadAllText(FilePath); }
            catch (Exception e) { AppLog.Write($"notas: {e.Message}"); }
        }
        saveTimer.Tick += (_, _) => Save();
    }

    public static string FilePath => Path.Combine(Paths.DataDir, "notes.txt");
    public string Hint => tr("Escribe aquí: se guarda solo, en este PC.");
    public string Text
    {
        get => text;
        set
        {
            if (value == text) return;
            text = value;
            saveTimer.Stop();
            saveTimer.Start();
        }
    }

    public void Save()
    {
        saveTimer.Stop();
        if (!persist) return;
        try
        {
            Directory.CreateDirectory(Paths.DataDir);
            File.WriteAllText(FilePath, text);
        }
        catch (Exception e) { AppLog.Write($"notas: {e.Message}"); }
    }
}

public sealed class BatteryWidget() : WidgetModel(WidgetCatalog.Find("battery")!)
{
    public bool HasBattery { get; private set; }
    public double Fraction { get; private set; }
    public string Label { get; private set; } = "";
    public string State { get; private set; } = "";
    public string Missing => tr("Este equipo no tiene batería.");

    public override void Tick(DateTime now)
    {
        if (Win32.GetSystemPowerStatus(out var s) && s.BatteryFlag != 128 && s.BatteryLifePercent <= 100)
        {
            HasBattery = true;
            Fraction = s.BatteryLifePercent / 100.0;
            Label = $"{s.BatteryLifePercent} %";
            State = s.ACLineStatus == 1 ? tr("Enchufado") : tr("Con batería");
        }
        else HasBattery = false;
        Raise();
    }
}

/// Los widgets del notch y su reloj de un segundo: solo corre con el Home a la vista o con algo en marcha (temporizador).
public sealed class WidgetHub
{
    private readonly Dictionary<string, WidgetModel> all;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool visible;

    public PomodoroWidget Pomodoro { get; } = new();
    public StayAwakeWidget Awake { get; } = new();
    public NotesWidget Notes { get; }
    public SystemWidget SystemStats { get; }
    public CodeWidget Code { get; }
    public ClockWidget ClockInfo { get; }
    public BatteryWidget BatteryInfo { get; } = new();
    public MediaWidget Media { get; } = new();
    public AudioVolume Volume { get; } = new();
    private HashSet<string> pillNeeds = [];
    public event Action<string, string>? Finished;
    /// Cada segundo, tras leer los datos: la pastilla cerrada se repinta con ellos.
    public event Action? Ticked;

    public WidgetHub(PanelViewModel panel)
    {
        Notes = new NotesWidget(persist: !panel.Ollama.IsDemo);
        SystemStats = new SystemWidget(panel);
        Code = new CodeWidget(panel.Prefs);
        ClockInfo = new ClockWidget(panel.Prefs);
        all = new WidgetModel[]
        {
            new OllamaWidget(panel), SystemStats, ClockInfo, Pomodoro, Awake, Notes, BatteryInfo, Media, Code,
        }.ToDictionary(w => w.Id);
        Pomodoro.Finished += (title, text) => Finished?.Invoke(title, text);
        Pomodoro.PropertyChanged += (_, _) => Sync();
        timer.Tick += (_, _) => Tick();
    }

    public WidgetModel? Find(string id) => all.GetValueOrDefault(id);
    public List<WidgetSlot> Slots(IReadOnlyList<string> ids, bool peek) =>
        WidgetLayout.Arrange(ids).Select(p => Find(p.Id) is { } w ? new WidgetSlot(w, p.Span, peek, ids.Count == 1) : null).OfType<WidgetSlot>().ToList();

    public void SetVisible(bool value)
    {
        visible = value;
        Sync();
        if (value) Tick();
    }

    /// Los widgets cuyos datos enseña la pastilla cerrada (CPU, hora, batería): siguen leyendo con el notch cerrado.
    public void SetPillNeeds(IEnumerable<string> widgetIds)
    {
        pillNeeds = [.. widgetIds];
        Sync();
        if (pillNeeds.Count > 0) Tick();
    }

    private void Sync()
    {
        if (visible || Pomodoro.Running || pillNeeds.Count > 0) timer.Start();
        else timer.Stop();
    }

    private void Tick()
    {
        var now = DateTime.Now;
        foreach (var widget in all.Values)
            if (visible || widget.Always || pillNeeds.Contains(widget.Id)) widget.Tick(now);
        Ticked?.Invoke();
    }
}
