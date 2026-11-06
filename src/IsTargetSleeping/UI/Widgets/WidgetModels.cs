using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Windows.Input;
using System.Windows.Threading;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// Un widget colocado en un sitio: el Home o la vista al pasar el ratón, con una o dos celdas. Su plantilla elige la variante.
public sealed class WidgetSlot(WidgetModel model, int span, int storedSpan, bool peek, bool alone = false, bool customizing = false, Action<string>? resize = null, int rows = 1, bool grid = false, bool gallery = false)
{
    public WidgetModel Model { get; } = model;
    public string Id => Model.Id;
    public ICommand Resize { get; } = new Command(() => resize?.Invoke(model.Id));
    public bool Wide => span == 2;
    public bool ResizeWide => storedSpan == 2;
    public int Span => span;
    public bool Compact => !Wide;
    public bool CompactPeek => Compact && Peek;
    // Agentes de IA: el vistazo (una celda, o dos en una fila, y siempre en la vista corta) y las vistas altas con lista y detalle.
    public bool GlanceCompact => Compact && (Peek || !Tall);
    public bool GlanceWide => Wide && (Peek || !Tall);
    public bool ListCompact => Compact && Full && Tall;
    public bool DetailWide => Wide && Full && Tall;
    public bool ListLayout => ListCompact || DetailWide;
    public double DetailMin => Math.Max(0, BodyMax - 18);
    /// El alto que le queda al contenido de una lista (agentes…) dentro de su hueco de la cuadrícula: lo que pase de ahí se desplaza.
    /// El alto del texto del teleprompter: lo que deja el hueco una vez quitados la cabecera y, en la vista completa, el editor y los deslizadores.
    public double TeleHeight => Math.Max(44, Rows * GridPanel.RowHeight - 64 - (WideFull ? 118 : 0));
    /// El alto mínimo de la tarjeta de una lista en una vista alta: que llene su hueco en vez de quedarse a media altura (lo que ocupan las pestañas queda fuera).
    public double FillMin => Tall ? Math.Max(0, BodyMax - 52) : 0;
    /// El alto mínimo de la tarjeta de la tarea del Equipo en reposo (en una vista alta): lo que sobra tras las pestañas, la línea de estado y, con una celda, los nodos.
    public double TeamTaskMin => !Tall ? 0 : Math.Max(0, BodyMax - (Wide ? 52 + 50 : 50 + 78));
    /// El alto mínimo de la tarjeta del plan (dos celdas): lo que sobra tras las pestañas.
    public double TeamPlanMin => Wide && Tall ? Math.Max(0, BodyMax - 52) : 0;
    public double BodyMax => Math.Max(60, Rows * GridPanel.RowHeight - 56);
    /// Dos celdas con sitio de sobra (solo en su lista o en dos filas): el diseño grande; en una fila, el estrecho.
    public bool WideBig => Wide && Full && (alone || Tall);
    public bool WideSlim => Wide && !WideBig;
    /// Una celda, o dos en una sola fila: el diseño de una celda, a lo ancho.
    public bool CompactOrPeek => Compact || Peek;
    public bool WideFullCompanion => WideCompanion && Full;
    public bool CompactFlat => Compact && !Tall;
    public bool WideFlat => Wide && !Tall;
    public bool CompactTall => Compact && Tall;
    public bool WideTall => Wide && Tall;
    public bool WidePeek => Wide && Peek;
    public bool CompactFull => Compact && Full;
    public bool WideFull => Wide && Full;
    /// Es el único widget de su lista: llena su celda con una vista grande en vez de dejar sitio vacío.
    public bool Alone => alone;
    public bool WideAlone => Wide && alone && !Peek;   // en la vista corta se queda baja
    public bool WideCompanion => Wide && !WideAlone;
    /// En la vista al pasar el ratón los widgets van más densos (sin lo secundario).
    // En la cuadrícula del Home una fila es la vista densa (la misma de la vista corta) y dos o más son la vista completa: cada tamaño tiene su diseño.
    public bool Peek { get; } = peek || grid && rows <= 1;
    public bool Full => !Peek;
    /// La vista al pasar el ratón con un solo widget: sin título ni nombre de la app, solo el contenido (como en Nusa).
    public bool Bare => Peek && alone;
    public bool NotBare => !Bare;
    /// La vista corta de verdad (no la fila del Home) con un solo widget: el Pomodoro usa ahí el diseño de Nusa, a 420 px.
    public bool NusaPeek => peek && alone;
    public bool NotNusaPeek => !NusaPeek;
    /// El lienzo de 420 px de esa vista sale del margen de la tarjeta (356) sin recortarse.
    public System.Windows.Thickness NusaInset => CardWidth < 420 ? new(-44, 0, -44, 0) : new(0);
    public bool PeekList => Peek && !Bare;
    public bool CompactNotBare => Compact && !Bare;
    /// Las filas que pidió (el panel puede darle más si su contenido no cabe).
    public int Rows => rows;
    public bool Tall => rows >= 2;
    public bool ResizeHandleVisible => !peek && customizing && WidgetLayout.CanResize(Id, home: grid);
    public string ResizeHelp => tr("Cambiar el tamaño de %@", Model.Title);
    /// En la galería la tarjeta llena la celda (menos el borde exterior); en Home conserva su margen.
    public System.Windows.Thickness CardMargin => gallery ? new(0) : new(6, 2, 6, 2);
    /// El Estante en la vista corta con él solo llena casi todo el ancho (título y chips a 14 px de los bordes, como en Nusa).
    public double ShelfWidth => Bare && !gallery ? 416 : CardWidth;
    public System.Windows.Thickness ShelfMargin => Bare && !gallery ? new(0, 2, 0, 2) : CardMargin;
    public double CardWidth => gallery ? span * GridPanel.CellWidth - 2 : Wide ? 600 : Bare ? (Id == "todo" ? 408 : 356) : 294;   // Tareas: la vista corta usa todo el ancho (el título sale a 15 px del borde, como en Nusa)
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
public sealed record SystemMetricCard(string Id, string Title, string Value, double Fraction, double[] Samples);

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
    public List<SystemMetricCard> Cards { get; private set; } = [];
    public List<SystemMetricCard> AllCards { get; private set; } = [];
    /// Las métricas que enseña la vista corta: las elegidas (CPU y RAM por defecto), como mucho tres.
    public List<SystemMetricCard> PeekCards
    {
        get
        {
            bool Usable(SystemMetricCard c) => c.Id != "battery" && (c.Id != "gpu" || Panel.ShowGpu);
            var list = Cards.Where(Usable).Take(4).ToList();
            foreach (var card in AllCards) if (list.Count < 4 && Usable(card) && list.All(x => x.Id != card.Id)) list.Add(card);
            return list;
        }
    }
    public string PeekSummary => $"CPU {CpuLabel} · RAM {Panel.UsedLabel}";
    private readonly Dictionary<string, SeriesBuffer> series = SystemMetricCatalog.All.ToDictionary(id => id, _ => new SeriesBuffer(60));

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
        SampleMetrics();
        Raise();
    }

    private void SampleMetrics()
    {
        var values = new Dictionary<string, (string Title, string Value, double Fraction)>
        {
            ["cpu"] = (tr("CPU"), CpuLabel, CpuFraction),
            ["ram"] = ("RAM", Panel.UsedLabel, Panel.UsedFraction),
            ["gpu"] = ("GPU", Panel.ShowGpu ? Panel.GpuUsedLabel : tr("No disponible"), Panel.ShowGpu ? Panel.GpuUsedFraction : 0),
            ["disk"] = (tr("Disco"), DiskLabel, DiskFraction),
            ["battery"] = (tr("Batería"), Panel.Hub.BatteryInfo.HasBattery ? Panel.Hub.BatteryInfo.Label : tr("No disponible"), Panel.Hub.BatteryInfo.HasBattery ? Panel.Hub.BatteryInfo.Fraction : 0),
            ["network"] = (tr("Red"), NetLabel, NetFraction),
        };
        foreach (var item in series) item.Value.Add(values[item.Key].Fraction);
        AllCards = SystemMetricCatalog.All.Select(id => Card(id, values[id])).ToList();
        var selected = SystemMetricCatalog.Normalize(Panel.Prefs.List(PrefKeys.SystemMetrics));
        Cards = selected.Select(id => Card(id, values[id])).ToList();
    }

    private SystemMetricCard Card(string id, (string Title, string Value, double Fraction) value) =>
        new(id, value.Title, value.Value, value.Fraction, series[id].Snapshot());

    private double NetFraction { get; set; }

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
            {
                var down = Math.Max(0, (received - netReceived) / seconds);
                var up = Math.Max(0, (sent - netSent) / seconds);
                NetFraction = Math.Clamp(Math.Max(down, up) / 10_485_760, 0, 1);
                NetLabel = $"↓ {Rate(down)}   ↑ {Rate(up)}";
            }
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

/// Un punto del ciclo de cuatro sesiones: hecha, la de ahora o por hacer.
public sealed record PomodoroDot(bool Done, bool Current);

public sealed class PomodoroWidget : WidgetModel
{
    private readonly Pomodoro timer = new();
    private IReadOnlyList<PomodoroDot> dots = [];
    private string dotsSignature = "";
    public event Action<string, string>? Finished;
    /// Solo cuando arranca, se pausa, se reinicia o acaba una fase (no cada segundo).
    public event Action? StateChanged;

    public PomodoroWidget() : base(WidgetCatalog.Find("pomodoro")!)
    {
        Toggle = new Command(() => { timer.Toggle(DateTime.Now); Raise(); StateChanged?.Invoke(); });
        Reset = new Command(() => { timer.Reset(); Raise(); StateChanged?.Invoke(); });
        Skip = new Command(() => { timer.Skip(DateTime.Now); Raise(); StateChanged?.Invoke(); });
        Restart = new Command(() => { timer.Restart(); Raise(); StateChanged?.Invoke(); });
        FocusMode = new Command(() => { timer.Select(PomodoroPhase.Focus); Raise(); StateChanged?.Invoke(); });
        BreakMode = new Command(() => { timer.Select(PomodoroPhase.Break); Raise(); StateChanged?.Invoke(); });
    }

    /// Reinicia la fase de ahora (25:00 o 05:00, en pausa); `Reset` vuelve a empezar todo el ciclo.
    public ICommand Restart { get; }
    public ICommand FocusMode { get; }
    public ICommand BreakMode { get; }
    public string FocusModeText => tr("Modo enfoque");
    public string BreakModeText => tr("Pausa corta");
    public bool IsFocus => timer.Phase == PomodoroPhase.Focus;
    // Los fondos de las píldoras de modo, de Nusa: el modo activo en gris medio y el otro más oscuro.
    public string FocusFill => IsFocus ? "#323232" : "#181818";
    public string BreakFill => IsFocus ? "#181818" : "#323232";

    public override bool Always => true;
    public bool Running => timer.Running;
    public ICommand Toggle { get; }
    public ICommand Reset { get; }
    public ICommand Skip { get; }
    public string SkipHelp => tr("Saltar a la fase siguiente");
    /// Los cuatro puntos del ciclo (la misma lista mientras no cambien: los dibujos no se rehacen cada segundo).
    public IReadOnlyList<PomodoroDot> Dots
    {
        get
        {
            var signature = $"{timer.Completed}/{timer.Phase}";
            if (signature != dotsSignature)
            {
                dotsSignature = signature;
                dots = Enumerable.Range(0, Pomodoro.SessionsPerCycle).Select(i => new PomodoroDot(i < timer.Completed, timer.Phase == PomodoroPhase.Focus && i == timer.Completed)).ToList();
            }
            return dots;
        }
    }
    public string SessionText => tr("Sesión %d de %d", timer.Session, Pomodoro.SessionsPerCycle);
    public string Remaining => Pomodoro.Format(timer.Left);
    public double Progress => timer.Progress;
    public string PhaseLabel => timer.Phase == PomodoroPhase.Focus ? tr("Tiempo de enfoque") : timer.LongBreak ? tr("Descanso largo") : tr("Descanso corto");
    /// La fase en una frase: lo que toca hacer y cuánto dura.
    public string PhaseHint => timer.Phase == PomodoroPhase.Focus ? tr("Concéntrate %d minutos", (int)timer.Length.TotalMinutes) : tr("Descansa %d minutos", (int)timer.Length.TotalMinutes);
    public bool IsBreak => timer.Phase == PomodoroPhase.Break;
    public string ToggleGlyph => timer.Running ? "" : "";   // pausa / reproducir
    // Dibujado a mano (triángulo y barras macizos) para que el icono se vea igual en cualquier fuente y quede centrado en el disco.
    public string ToggleShape => timer.Running ? "M0,0 H4 V16 H0 Z M8,0 H12 V16 H8 Z" : "M3,0 L15,8 L3,16 Z";
    // Iconos del estilo Lucide (24 × 24): triángulo y barras con las esquinas redondeadas por el trazo.
    public string ToggleIcon => timer.Running ? "M7,5.5 H10 V18.5 H7 Z M14,5.5 H17 V18.5 H14 Z" : "M7.5,5 L18.5,12 L7.5,19 Z";
    public string ToggleHelp => timer.Running ? tr("Pausar el temporizador") : tr("Iniciar el temporizador");
    public string ResetHelp => tr("Reiniciar el temporizador");

    public override void Tick(DateTime now)
    {
        if (timer.Tick(now))
        {
            // La fase que acaba de empezar es la siguiente: si ahora toca descanso, acabó el enfoque.
            Finished?.Invoke(tr("Pomodoro"), timer.Phase == PomodoroPhase.Break
                ? timer.LongBreak ? tr("Enfoque terminado: toca un descanso largo de 15 minutos.") : tr("Enfoque terminado: toca descansar 5 minutos.")
                : tr("Descanso terminado: a trabajar."));
            StateChanged?.Invoke();
        }
        Raise();
    }
}

/// Impide que Windows suspenda el PC o apague la pantalla mientras está activo (no sobrevive a reiniciar la app).
public sealed class StayAwakeWidget : WidgetModel
{
    public StayAwakeWidget() : base(WidgetCatalog.Find("awake")!) => Toggle = new Command(Set);

    private bool failed;
    public bool On { get; private set; }
    public bool Failed => failed;
    public ICommand Toggle { get; }
    public string StateTitle => failed ? tr("No se pudo cambiar") : On ? tr("PC despierto") : tr("Modo automático");
    public string State => failed ? tr("Windows no pudo cambiar el estado de suspensión.")
        : On ? tr("El PC y la pantalla se mantienen activos.") : tr("Windows decide cuándo suspender el equipo.");
    public string ToggleHelp => On ? tr("Dejar que Windows suspenda") : tr("Mantener el PC despierto");

    private void Set()
    {
        bool next = !On;
        uint result = Win32.SetThreadExecutionState(next
            ? Win32.ES_CONTINUOUS | Win32.ES_SYSTEM_REQUIRED | Win32.ES_DISPLAY_REQUIRED
            : Win32.ES_CONTINUOUS);
        failed = result == 0;
        if (!failed) On = next;
        Raise();
    }
}

/// Una fila de la batería: un aparato con su icono, su nombre y su nivel.
public sealed class DeviceBatteryRow(DeviceBattery device)
{
    public string Name => device.Name;
    public DeviceKind Kind => device.Kind;
    public double Fraction => device.Percent / 100.0;
    public string Label => $"{device.Percent} %";
    public bool Charging => device.Charging;
    /// Casi vacía y sin cargar: el anillo y la barra se ponen en el color de aviso.
    public bool Low => device.Percent <= 20 && !device.Charging;
    public string Help => $"{device.Name}: {device.Percent} %" + (device.Charging ? " · " + tr("Cargando") : "");
}

/// La batería del equipo y la de los accesorios Bluetooth que informan de la suya (ratón, teclado, auriculares, mando…), como el widget de baterías de iOS.
public sealed class BatteryWidget() : WidgetModel(WidgetCatalog.Find("battery")!)
{
    private IReadOnlyList<DeviceBattery> accessories = [];
    private IReadOnlyList<DeviceBatteryRow> devices = [], top4 = [], top6 = [], top10 = [];
    private string signature = "";
    private DateTime nextAccessories;
    private bool reading, demo;
    /// La batería del propio equipo (la usan también la pastilla del notch y las actividades).
    public bool HasBattery { get; private set; }
    public double Fraction { get; private set; }
    public bool Charging { get; private set; }
    public string Label { get; private set; } = "";
    public string State { get; private set; } = "";
    public string Missing => tr("Este equipo no tiene batería.");
    // Las listas se rehacen solo cuando cambian los datos: si no, los anillos se redibujarían (y volverían a animarse) cada segundo.
    public IReadOnlyList<DeviceBatteryRow> Devices => devices;
    /// Una celda (cuatro anillos), dos celdas en una fila (seis) y las vistas altas (lista de seis o de diez).
    public IReadOnlyList<DeviceBatteryRow> Top4 => top4;
    public IReadOnlyList<DeviceBatteryRow> Top6 => top6;
    public IReadOnlyList<DeviceBatteryRow> Top10 => top10;
    public bool HasDevices => devices.Count > 0;
    public bool NoDevices => !HasDevices;
    private DeviceBattery? laptop;

    public override void Tick(DateTime now)
    {
        if (!demo)
        {
            if (Win32.GetSystemPowerStatus(out var s) && s.BatteryFlag != 128 && s.BatteryLifePercent <= 100)
            {
                HasBattery = true;
                Fraction = s.BatteryLifePercent / 100.0;
                Label = $"{s.BatteryLifePercent} %";
                Charging = s.ACLineStatus == 1;
                State = s.ACLineStatus == 1 ? tr("Enchufado") : tr("Con batería");
                laptop = new DeviceBattery("laptop", tr("Este equipo"), DeviceKind.Laptop, s.BatteryLifePercent, s.ACLineStatus == 1);
            }
            else { HasBattery = false; Charging = false; laptop = null; }
            if (!reading && now >= nextAccessories)
            {
                reading = true;
                nextAccessories = now.AddSeconds(60);
                _ = ReadAccessories();
            }
            Rebuild();
        }
        Raise();
    }

    private async Task ReadAccessories()
    {
        try
        {
            var list = await Task.Run(DeviceBatteryReader.ReadAsync);
            AppLog.Write($"baterías: {list.Count} accesorios leídos");
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { accessories = list; Rebuild(); Raise(); });
        }
        catch (Exception e) { AppLog.Write($"baterías: {e.Message}"); }
        finally { reading = false; }
    }

    private void Rebuild()
    {
        var all = DeviceKinds.Merge((laptop is null ? [] : new[] { laptop }).Concat(accessories));
        var next = string.Join("|", all.Select(d => $"{d.Id}:{d.Percent}:{d.Charging}"));
        if (next == signature) return;
        signature = next;
        devices = all.Select(d => new DeviceBatteryRow(d)).ToList();
        top4 = devices.Take(4).ToList(); top6 = devices.Take(6).ToList(); top10 = devices.Take(10).ToList();
    }

    /// Datos de muestra para la galería de widgets.
    public void LoadDemo()
    {
        demo = true;
        HasBattery = true; Fraction = 0.72; Label = "72 %"; State = tr("Con batería");
        laptop = new DeviceBattery("laptop", tr("Este equipo"), DeviceKind.Laptop, 72);
        accessories =
        [
            new DeviceBattery("d1", tr("Auriculares"), DeviceKind.Headphones, 41, Charging: true),
            new DeviceBattery("d2", tr("Ratón"), DeviceKind.Mouse, 85),
            new DeviceBattery("d3", tr("Teclado"), DeviceKind.Keyboard, 64),
            new DeviceBattery("d4", tr("Mando"), DeviceKind.Gamepad, 18),
            new DeviceBattery("d5", tr("Altavoz"), DeviceKind.Speaker, 93),
        ];
        Rebuild();
        Raise();
    }
}

/// Sesiones activas del mezclador de Windows. El usuario mueve los controles del sistema, no un volumen separado.
public sealed class AudioWidget(Prefs prefs) : WidgetModel(WidgetCatalog.Find("audio")!)
{
    public AudioSessions Sessions { get; } = new(prefs);
    public string EmptyText => Sessions.EmptyText;
    public string ActiveText => tr("%@ apps con audio", Sessions.Active.Count);
    /// Las dos (una celda) o tres (dos celdas) apps con más volumen, para la vista de una fila.
    public IReadOnlyList<AudioSessionRow> TopTwo => Sessions.Active.OrderByDescending(r => r.Level).Take(2).ToList();
    public IReadOnlyList<AudioSessionRow> TopThree => Sessions.Active.OrderByDescending(r => r.Level).Take(3).ToList();
    public bool HasSessions => Sessions.Active.Count > 0;
    public bool NoSessions => !HasSessions;
    public override void Tick(DateTime now) { Sessions.Refresh(); Raise(); }
    /// Datos de muestra para la galería de widgets.
    public void LoadDemo() { Sessions.LoadDemo(); Raise(); }
}

/// Los widgets del notch y su reloj de un segundo: solo corre con el Home a la vista o con algo en marcha (temporizador).
public sealed class WidgetHub : IDisposable
{
    private readonly Dictionary<string, WidgetModel> all;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer motionResume = new() { Interval = TimeSpan.FromMilliseconds(80) };   // vuelve a leer en cuanto acaba la animación del notch
    private bool homeVisible, peekVisible;
    private HashSet<string> homeNeeds = [], peekNeeds = [];

    public PomodoroWidget Pomodoro { get; } = new();
    public StayAwakeWidget Awake { get; } = new();
    public NotesWidget Notes { get; }
    public TodoWidget Todo { get; }
    public ShelfWidget Shelf { get; }
    public SystemWidget SystemStats { get; }
    public CodeWidget Code { get; }
    public ClockWidget ClockInfo { get; }
    public BatteryWidget BatteryInfo { get; } = new();
    public MediaWidget Media { get; } = new();
    public AudioVolume Volume { get; } = new();
    public AudioWidget Audio { get; }
    public SystemHuds Huds { get; }
    public AgentsWidget Agents { get; }
    public UsageWidget Usage { get; }
    public LmStudioWidget LmStudio { get; }
    public LauncherWidget Launcher { get; }
    public WeatherWidget Weather { get; }
    public TeleprompterWidget Teleprompter { get; }
    public TeamWidget Team { get; }
    public LedWidget Leds { get; }
    private bool activityNeeds;
    private bool agentActivityNeeds;
    private bool meetingActivityNeeds;
    private HashSet<string> pillNeeds = [];
    public event Action<string, string>? Finished;
    /// El equipo terminó una tarea o una respuesta (aviso de Windows propio, con su ajuste).
    public event Action<string, string>? TeamFinished;
    /// Cada segundo, tras leer los datos: la pastilla cerrada se repinta con ellos.
    public event Action? Ticked;

    public WidgetHub(PanelViewModel panel)
    {
        Notes = new NotesWidget(persist: !panel.Ollama.IsDemo);
        Todo = new TodoWidget(persist: !panel.Ollama.IsDemo);
        Shelf = new ShelfWidget(persist: !panel.Ollama.IsDemo, prefs: panel.Prefs);
        SystemStats = new SystemWidget(panel);
        Code = new CodeWidget(panel.Prefs);
        ClockInfo = new ClockWidget(panel.Prefs);
        Audio = new AudioWidget(panel.Prefs);
        Agents = new AgentsWidget(panel.Prefs);
        Usage = new UsageWidget(panel.Prefs);
        LmStudio = new LmStudioWidget(panel);
        Launcher = new LauncherWidget(panel);
        Weather = new WeatherWidget(panel.Prefs);
        Teleprompter = new TeleprompterWidget(panel.Prefs, persist: !panel.Ollama.IsDemo);
        // En demostración (capturas) el equipo no toca la carpeta de datos del usuario.
        var teamService = new TeamService(panel.Ollama.IsDemo ? new TeamStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "its-team-demo")) : TeamStore.Default);
        Team = new TeamWidget(panel.Prefs, demo: panel.Ollama.IsDemo, teamService, Agents);
        Team.Finished += (title, text) => TeamFinished?.Invoke(title, text);
        Leds = new LedWidget(panel.Prefs, demo: panel.Ollama.IsDemo);
        Huds = new SystemHuds();
        Huds.Changed += () => Ticked?.Invoke();
        all = new WidgetModel[]
        {
            new OllamaWidget(panel), SystemStats, ClockInfo, Pomodoro, Awake, Notes, Todo, Shelf, BatteryInfo, Media, Code, Audio, Agents, Usage, LmStudio, Launcher, Weather, Teleprompter, Team, Leds,
        }.ToDictionary(w => w.Id);
        Pomodoro.Finished += (title, text) => Finished?.Invoke(title, text);
        Pomodoro.PropertyChanged += (_, _) => Sync();
        Team.PropertyChanged += (_, _) => Sync();
        Leds.PropertyChanged += (_, _) => Sync();
        timer.Tick += (_, _) => Tick();
        motionResume.Tick += (_, _) => { if (Motion.IsAnimating) return; motionResume.Stop(); Tick(); };
        Sync();
    }

    public WidgetModel? Find(string id) => all.GetValueOrDefault(id);
    public List<WidgetSlot> Slots(IReadOnlyList<string> ids, bool peek, bool customizing = false, Action<string>? resize = null, bool compact = false) =>
        peek
            ? WidgetLayout.Arrange(ids).Select(p => Find(p.Id) is { } w
                ? new WidgetSlot(w, compact ? 1 : p.Span, WidgetLayout.Span(ids.FirstOrDefault(id => WidgetLayout.BaseId(id) == p.Id) ?? p.Id), peek, ids.Count == 1, customizing, resize)
                : null).OfType<WidgetSlot>().ToList()
            : WidgetLayout.ArrangeHome(ids).Select(p => Find(p.Id) is { } w
                ? new WidgetSlot(w, p.Cols, WidgetLayout.Span(ids.FirstOrDefault(id => WidgetLayout.BaseId(id) == p.Id) ?? p.Id), false, ids.Count == 1, customizing, resize, p.Rows, grid: true)
                : null).OfType<WidgetSlot>().ToList();

    public void SetVisible(bool value, IEnumerable<string>? widgetIds = null)
    {
        homeVisible = value;
        homeNeeds = [.. widgetIds ?? []];
        Sync();
        if (value) Tick();
    }

    public void SetPeekVisible(bool value, IEnumerable<string>? widgetIds = null)
    {
        peekVisible = value;
        peekNeeds = [.. widgetIds ?? []];
        Sync();
        // La primera lectura va después de empezar la animación de apertura: hacerla ya la haría entrecortada.
        if (value) System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { if (peekVisible) Tick(); }, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// Los widgets cuyos datos enseña la pastilla cerrada (CPU, hora, batería): siguen leyendo con el notch cerrado.
    public void SetPillNeeds(IEnumerable<string> widgetIds)
    {
        pillNeeds = [.. widgetIds];
        Sync();
        if (pillNeeds.Count > 0) Tick();
    }

    public void SetActivityNeeds(IEnumerable<string> enabled)
    {
        var ids = enabled.ToArray();
        agentActivityNeeds = ids.Contains("agents");
        meetingActivityNeeds = ids.Contains("meeting");
        activityNeeds = agentActivityNeeds || meetingActivityNeeds || ids.Any(id => id is "brightness" or "locks" or "bluetooth");
        Huds.Configure(ids);
        if (!activityNeeds) Huds.Refresh();
        Sync();
    }

    private void Sync()
    {
        if (homeVisible || peekVisible || Pomodoro.Running || pillNeeds.Count > 0 || activityNeeds || Team.NeedsTick || Leds.Always) timer.Start();
        else timer.Stop();
    }

    private void Tick()
    {
        // Con el notch moviéndose, la lectura espera: se hace nada más acabar (ver `Motion.Busy`).
        if (Motion.IsAnimating) { motionResume.Start(); return; }
        var now = DateTime.Now;
        foreach (var widget in all.Values)
            if (homeVisible && homeNeeds.Contains(widget.Id) || peekVisible && peekNeeds.Contains(widget.Id)
                || widget.Always || pillNeeds.Contains(widget.Id) || widget.Id == "agents" && agentActivityNeeds) widget.Tick(now);
        if (meetingActivityNeeds && !(homeVisible && homeNeeds.Contains("clock")) && !(peekVisible && peekNeeds.Contains("clock"))) ClockInfo.Tick(now);
        if (activityNeeds) Huds.Refresh();
        Ticked?.Invoke();
    }

    /// El Estante está en el Home o en la vista corta que se ve ahora: ahí van los archivos que se arrastran al notch.
    public bool ShelfShown => homeVisible && homeNeeds.Any(id => WidgetLayout.BaseId(id) == "shelf") || peekVisible && peekNeeds.Any(id => WidgetLayout.BaseId(id) == "shelf");

    public void Dispose() { Notes.Save(); Huds.Dispose(); Audio.Sessions.Dispose(); Leds.Dispose(); }
}
