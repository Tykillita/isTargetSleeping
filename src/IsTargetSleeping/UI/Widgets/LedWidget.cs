using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

public sealed record LedDeviceRow(string Name, bool Selected, string Status, ICommand Select);
public sealed record LedChip(string Name, bool Selected, ICommand Pick, Brush? Dot = null);
public sealed record LedEffectGroup(string Title, IReadOnlyList<LedChip> Chips);
public sealed record LedSwatch(Brush Fill, bool Selected, ICommand Pick, string Name);
public sealed record LedTimerRow(string Title, string Time, string Days, bool Enabled, ICommand Toggle, ICommand Earlier, ICommand Later, ICommand CycleDays, ICommand Remove);

/// Luces LED: las tiras Bluetooth (ELK-BLEDOM, MELK…) como en la app Lotus Lamp X: encender y apagar, color, brillo, efectos con su velocidad, escenas,
/// luz al ritmo de la música y alarmas. Cada tira se recuerda; se conecta sola al mandarle algo.
public sealed class LedWidget : WidgetModel, IDisposable
{
    private readonly Prefs prefs;
    private readonly bool demo;
    private readonly List<LedDevice> devices = [];
    private readonly List<LedTimer> timers = [];
    private int selected;
    private int tab;
    private bool scanning, musicOn;
    private string scanText = "";
    private int musicMode;
    private double sensitivity = 0.5, hueDrift;
    private readonly DispatcherTimer musicTimer = new() { Interval = TimeSpan.FromMilliseconds(110) };
    private LoudnessMeter? meter;
    private double smoothPeak;
    private long lastMinute = -1;
    private IReadOnlyList<LedDeviceRow> deviceRows = [];
    private IReadOnlyList<LedChip> effectChips = [], sceneChips = [];
    private IReadOnlyList<LedEffectGroup> effectGroups = [];
    private Brush glow = Brushes.Transparent;
    private string glowKey = "";
    private IReadOnlyList<LedSwatch> swatches = [];
    private IReadOnlyList<LedTimerRow> timerRows = [];
    private IReadOnlyList<Segment> tabSegments = [], musicSegments = [];
    private string signature = "";

    public LedWidget(Prefs prefs, bool demo = false) : base(WidgetCatalog.Find("led")!)
    {
        this.prefs = prefs;
        this.demo = demo;
        if (!demo)
        {
            foreach (var line in prefs.List(PrefKeys.LedDevices)) if (LedDevice.Parse(line) is { } d) devices.Add(d);
            foreach (var line in prefs.List(PrefKeys.LedTimers)) if (LedTimer.Parse(line) is { } t) timers.Add(t);
            selected = Math.Clamp(prefs.List(PrefKeys.LedSelected).FirstOrDefault() is { } s && int.TryParse(s, out var i) ? i : 0, 0, Math.Max(0, devices.Count - 1));
        }
        Toggle = new Command(TogglePower);
        PickHue = new ParamCommand(p => { if (p is double h) SetColor(LedProtocol.FromHsv(h, 1, 1)); });
        SetBrightness = new ParamCommand(p => { if (p is double f) SetBrightnessPercent((int)Math.Round(f * 100)); });
        SetSpeed = new ParamCommand(p => { if (p is double f) SetSpeedPercent((int)Math.Round(f * 100)); });
        SetSensitivity = new ParamCommand(p => { if (p is double f) { sensitivity = Math.Clamp(f, 0, 1); Raise(); } });
        ToggleMusic = new Command(() => SetMusic(!musicOn));
        Scan = new Command(() => _ = ScanNow());
        AddOnTimer = new Command(() => AddTimer(true));
        AddOffTimer = new Command(() => AddTimer(false));
        Next = new Command(() => Select((selected + 1) % Math.Max(1, devices.Count)));
        musicTimer.Tick += (_, _) => MusicStep();
        Rebuild();
    }

    // ------------------------------------------------------------------ Estado
    private LedDevice? Current => devices.Count == 0 ? null : devices[Math.Clamp(selected, 0, devices.Count - 1)];
    public override bool Always => !demo && (musicOn || timers.Any(t => t.Enabled));
    public bool HasDevices => devices.Count > 0;
    public bool NoDevices => !HasDevices;
    public bool MoreThanOne => devices.Count > 1;
    public string Name => Current?.Name ?? "";
    public ICommand Next { get; }
    public bool IsOn => Current?.On == true;
    public string PowerGlyph => "\uE7E8";   // símbolo de encendido (Segoe Fluent Icons)
    public string PowerHelp => IsOn ? tr("Apagar las luces") : tr("Encender las luces");
    public ICommand Toggle { get; }

    /// Cómo va la conexión con la tira elegida, en una palabra.
    public string Status
    {
        get
        {
            if (Current is not { } d) return "";
            if (demo) return tr("Conectada");
            if (d.Link is not LedLink link) return tr("Sin conectar");
            return link.Connected ? tr("Conectada") : link.Problem.Length > 0 ? tr(link.Problem) : tr("Conectando…");
        }
    }
    public bool IsConnected => demo || (Current?.Link as LedLink)?.Connected == true;

    public double Hue => Current is { } d ? LedProtocol.ToHsv(d.R, d.G, d.B).Hue : 0;
    public Brush ColorBrush { get { var d = Current; return Theme.Brush(d is null ? Colors.White : Color.FromRgb(d.R, d.G, d.B)); } }
    /// El color de las luces para los halos (blanco si no hay tira).
    public Color AccentColor => Current is { } d ? Color.FromRgb(d.R, d.G, d.B) : Colors.White;
    /// El halo que rodea al botón de encendido: del color de las luces mientras estén encendidas.
    public double GlowOpacity => IsOn && IsConnected ? 0.85 : IsOn ? 0.45 : 0;
    /// El símbolo del botón de encendido: negro sobre colores claros y blanco sobre oscuros.
    public Brush PowerForeground { get { var c = AccentColor; return Theme.Brush(0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 150 ? Color.FromRgb(0x10, 0x10, 0x14) : Colors.White); } }
    /// Lo que valen el color de fondo y el halo con las luces apagadas.
    public Brush PowerFill => IsOn ? ColorBrush : Theme.Brush(Theme.WithOpacity(Theme.Current.Fg, 0.12));
    public double MusicLevel => smoothPeak;
    public ICommand PickHue { get; }
    public double Brightness => (Current?.Brightness ?? 100) / 100.0;
    public string BrightnessText => $"{Current?.Brightness ?? 100} %";
    public ICommand SetBrightness { get; }
    public double Speed => (Current?.Speed ?? 50) / 100.0;
    public string SpeedText => tr("Velocidad: %@", Current?.Speed ?? 50);
    public ICommand SetSpeed { get; }
    /// Lo que está haciendo ahora: el efecto o, si es un color fijo, su nombre en una palabra.
    public string ModeText => Current is { Effect: > 0 } d && LedEffects.Find(d.Effect) is { } e ? tr(e.Key) : tr("Color fijo");

    // ------------------------------------------------------------------ Listas (las mismas instancias mientras nada cambie: los controles no se rehacen cada segundo)
    public IReadOnlyList<LedDeviceRow> Devices => deviceRows;
    public IReadOnlyList<LedChip> EffectChips => effectChips;
    public IReadOnlyList<LedEffectGroup> EffectGroups => effectGroups;
    /// El efecto que corre (0 si es un color fijo) y el volumen de la música (negativo si no manda): lo que necesita la tira de muestra.
    public int EffectCode => Current?.Effect ?? 0;
    public double LiveLevel => musicOn ? smoothPeak : -1;
    /// Un resplandor suave del color de las luces, para el fondo del widget (nada con las luces apagadas).
    public Brush GlowBrush
    {
        get
        {
            var c = AccentColor;
            var key = $"{IsOn}{c.R:X2}{c.G:X2}{c.B:X2}{Current?.Brightness}";
            if (key == glowKey) return glow;
            glowKey = key;
            // Un resplandor elíptico, inscrito en la tarjeta y con caída de campana: vale cero en los cuatro bordes, así que no hay corte visible donde acaba.
            double strength = IsOn ? 0.12 + 0.22 * Brightness : 0;
            var brush = new RadialGradientBrush { Center = new System.Windows.Point(0.5, 0.5), GradientOrigin = new System.Windows.Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
            foreach (var (offset, factor) in new[] { (0.0, 1.0), (0.15, 0.90), (0.30, 0.65), (0.45, 0.375), (0.60, 0.175), (0.75, 0.066), (0.90, 0.02), (1.0, 0.0) })
                brush.GradientStops.Add(new GradientStop(Theme.WithOpacity(c, strength * factor), offset));
            brush.Freeze();
            return glow = brush;
        }
    }
    public IReadOnlyList<LedChip> SceneChips => sceneChips;
    public IReadOnlyList<LedSwatch> Swatches => swatches;
    public IReadOnlyList<LedTimerRow> Timers => timerRows;
    public IReadOnlyList<Segment> Tabs => tabSegments;
    public IReadOnlyList<Segment> MusicModes => musicSegments;
    public bool IsColorTab => tab == 0;
    public bool IsEffectsTab => tab == 1;
    public bool IsScenesTab => tab == 2;
    public bool IsMusicTab => tab == 3;
    public bool IsTimersTab => tab == 4;
    public int TabIndex => tab;

    // ------------------------------------------------------------------ Música y alarmas
    public bool MusicOn => musicOn;
    public ICommand ToggleMusic { get; }
    public string MusicLabel => musicOn ? tr("Parar la música") : tr("Luces con la música");
    public string MusicHint => tr("Las luces laten con el volumen de lo que suena en este equipo.");
    public double Sensitivity => sensitivity;
    public string SensitivityText => tr("Sensibilidad: %@", (int)Math.Round(sensitivity * 100));
    public ICommand SetSensitivity { get; }
    public ICommand AddOnTimer { get; }
    public ICommand AddOffTimer { get; }
    public string AddOnLabel => tr("Alarma de encendido");
    public string AddOffLabel => tr("Alarma de apagado");
    public string TimersHint => tr("Encienden o apagan todas las luces a su hora mientras la app está abierta.");
    public bool NoTimers => timers.Count == 0;

    // ------------------------------------------------------------------ Búsqueda
    public ICommand Scan { get; }
    public bool Scanning => scanning;
    public string ScanLabel => scanning ? tr("Buscando…") : tr("Buscar tiras");
    public string ScanText => scanText;
    public string EmptyTitle => tr("Sin tiras LED");
    public string EmptyText => tr("Enciende tu tira y pulsa Buscar: se añaden solas las que se llaman ELK-BLEDOM o MELK.");

    // ------------------------------------------------------------------ Acciones
    private LedLink LinkOf(LedDevice d)
    {
        if (d.Link is not LedLink link)
        {
            link = new LedLink(d.Address, d.Family);
            link.StateChanged += () => System.Windows.Application.Current?.Dispatcher.BeginInvoke(Raise);
            d.Link = link;
        }
        return link;
    }

    private void Post(LedDevice d, string key, byte[] frame) { if (!demo) LinkOf(d).Post(key, frame); }

    private void Save()
    {
        if (demo) return;
        prefs.SetList(PrefKeys.LedDevices, devices.Select(d => d.Serialize()));
        prefs.SetList(PrefKeys.LedTimers, timers.Select(t => t.Serialize()));
        prefs.SetList(PrefKeys.LedSelected, [selected.ToString()]);
    }

    private void Changed() { Save(); Rebuild(); Raise(); }

    public void TogglePower()
    {
        if (Current is not { } d) return;
        d.On = !d.On;
        Post(d, "encendido", LedProtocol.Power(d.On, d.Family));
        Changed();
    }

    private void TurnOnIfNeeded(LedDevice d)
    {
        if (d.On) return;
        d.On = true;
        Post(d, "encendido", LedProtocol.Power(true, d.Family));
    }

    public void SetColor((byte R, byte G, byte B) rgb)
    {
        if (Current is not { } d) return;
        (d.R, d.G, d.B, d.Effect) = (rgb.R, rgb.G, rgb.B, 0);
        TurnOnIfNeeded(d);
        Post(d, "color", LedProtocol.Color(rgb.R, rgb.G, rgb.B, d.Family));
        Changed();
    }

    public void SetBrightnessPercent(int percent)
    {
        if (Current is not { } d) return;
        d.Brightness = Math.Clamp(percent, 0, 100);
        TurnOnIfNeeded(d);
        Post(d, "brillo", LedProtocol.Brightness(d.Brightness, d.Family));
        Changed();
    }

    public void SetSpeedPercent(int percent)
    {
        if (Current is not { } d) return;
        d.Speed = Math.Clamp(percent, 0, 100);
        Post(d, "velocidad", LedProtocol.Speed(d.Speed, d.Family));
        Changed();
    }

    public void SetEffect(int code)
    {
        if (Current is not { } d) return;
        d.Effect = code;
        TurnOnIfNeeded(d);
        Post(d, "efecto", LedProtocol.Effect(code, d.Family));
        Post(d, "velocidad", LedProtocol.Speed(d.Speed, d.Family));
        Changed();
    }

    public void ApplyScene(LedScene scene)
    {
        if (Current is not { } d) return;
        d.Brightness = scene.Brightness;
        if (scene.IsEffect) { d.Effect = scene.EffectCode; d.Speed = scene.Speed; }
        else (d.R, d.G, d.B, d.Effect) = (scene.R, scene.G, scene.B, 0);
        TurnOnIfNeeded(d);
        if (scene.IsEffect) { Post(d, "efecto", LedProtocol.Effect(d.Effect, d.Family)); Post(d, "velocidad", LedProtocol.Speed(d.Speed, d.Family)); }
        else Post(d, "color", LedProtocol.Color(d.R, d.G, d.B, d.Family));
        Post(d, "brillo", LedProtocol.Brightness(d.Brightness, d.Family));
        Changed();
    }

    public void Select(int index)
    {
        if (devices.Count == 0) return;
        selected = Math.Clamp(index, 0, devices.Count - 1);
        if (!demo && Current is { } d) _ = LinkOf(d).ConnectAsync();   // se conecta al elegirla: lista cuando se toque un control
        Changed();
    }

    private void SelectTab(int index) { tab = index; Rebuild(); Raise(); }

    // ------------------------------------------------------------------ Búsqueda de tiras
    private async Task ScanNow()
    {
        if (scanning || demo) return;
        scanning = true; scanText = tr("Escuchando los anuncios Bluetooth…"); Raise();
        try
        {
            var found = await LedScanner.ScanAsync();
            int added = 0;
            foreach (var (address, name, _) in found)
            {
                if (devices.Any(d => d.Address == address)) continue;
                devices.Add(new LedDevice(address, name, LedProtocol.FamilyFor(name)));
                added++;
            }
            scanText = found.Count == 0 ? tr("Windows no detectó tiras en este escaneo.") : added == 0 ? tr("No hay tiras nuevas.") : tr("Añadidas: %d", added);
            if (added > 0 && devices.Count == added) selected = 0;
        }
        catch (Exception e) { AppLog.Write($"luces: buscar: {e.Message}"); scanText = tr("No se pudo buscar: ¿está el Bluetooth encendido?"); }
        finally { scanning = false; Changed(); }
    }

    // ------------------------------------------------------------------ Luces con la música
    private void SetMusic(bool on)
    {
        musicOn = on;
        if (on) { meter = new LoudnessMeter(); musicTimer.Start(); }
        else { musicTimer.Stop(); meter?.Dispose(); meter = null; if (Current is { } d) { Post(d, "brillo", LedProtocol.Brightness(d.Brightness, d.Family)); Post(d, "color", LedProtocol.Color(d.R, d.G, d.B, d.Family)); } }
        Changed();
    }

    private void MusicStep()
    {
        if (Current is not { } d || meter is null) return;
        double gain = 0.6 + sensitivity * 3.4;
        double raw = Math.Clamp(meter.Peak() * gain, 0, 1);
        smoothPeak = raw > smoothPeak ? raw : smoothPeak * 0.8 + raw * 0.2;   // sube de golpe y baja suave
        if (musicMode == 0)
        {
            // Energía: el color elegido, con el brillo que sigue al volumen.
            int level = (int)Math.Round(8 + 92 * Math.Pow(smoothPeak, 0.7));
            Post(d, "brillo", LedProtocol.Brightness(level, d.Family));
        }
        else
        {
            // Ritmo: el tono avanza con cada golpe de volumen y el brillo lo acompaña.
            hueDrift = (hueDrift + 4 + smoothPeak * 46) % 360;
            var (r, g, b) = LedProtocol.FromHsv(hueDrift, 1, 1);
            Post(d, "color", LedProtocol.Color(r, g, b, d.Family));
            Post(d, "brillo", LedProtocol.Brightness((int)Math.Round(20 + 80 * smoothPeak), d.Family));
        }
    }

    // ------------------------------------------------------------------ Alarmas
    private void AddTimer(bool on)
    {
        timers.Add(new LedTimer { TurnOn = on, Minute = LedTimer.DefaultMinute(DateTime.Now) });
        Changed();
    }

    public override void Tick(DateTime now)
    {
        if (demo) return;
        var minute = LedTimer.MinuteStamp(now);
        if (minute != lastMinute)
        {
            lastMinute = minute;
            var fired = false;
            foreach (var t in timers.Where(t => t.TakeIfDue(now)).ToList())
            {
                foreach (var d in devices) { d.On = t.TurnOn; Post(d, "encendido", LedProtocol.Power(t.TurnOn, d.Family)); }
                fired = true;
            }
            if (fired) Changed();
        }
        Raise();
    }

    // ------------------------------------------------------------------ Puntos de color de las pastillas
    private static Color Rgb((byte R, byte G, byte B) c) => Color.FromRgb(c.R, c.G, c.B);

    /// El punto de color de un efecto: los colores que se ven en él (ver `LedEffects.PaletteOf`).
    private static Brush EffectDot(LedEffect e) => GradientOf(LedEffects.PaletteOf(e).Select(Rgb).ToList());

    private static Brush SceneDot(LedScene s) => s.IsEffect ? GradientOf(LedEffects.Rainbow.Select(Rgb).ToList()) : Theme.Brush(Color.FromRgb(s.R, s.G, s.B));

    private static Brush GradientOf(List<Color> colors)
    {
        if (colors.Count == 1) return Theme.Brush(colors[0]);
        var brush = new LinearGradientBrush(new GradientStopCollection(colors.Select((c, i) => new GradientStop(c, i / (double)(colors.Count - 1)))), 0);
        brush.Freeze();
        return brush;
    }

    // ------------------------------------------------------------------ Listas de la interfaz
    private static string Time(int minute) => $"{minute / 60:00}:{minute % 60:00}";
    private string DaysText(int days) => days switch { 1 => tr("Lun–Vie"), 2 => tr("Sáb–Dom"), 3 => tr("Una vez"), _ => tr("Cada día") };

    private void Rebuild()
    {
        var d = Current;
        var sig = $"{selected}|{tab}|{musicMode}|{musicOn}|{string.Join(";", devices.Select(x => x.Serialize()))}|{string.Join(";", timers.Select(t => t.Serialize()))}|{Status}";
        if (sig == signature) return;
        signature = sig;
        deviceRows = devices.Select((x, i) => new LedDeviceRow(x.Name, i == selected, i == selected ? Status : "", new Command(() => Select(i)))).ToList();
        effectChips = LedEffects.All.Select(e => new LedChip(tr(e.Key), d is { Effect: var code } && code == e.Code, new Command(() => SetEffect(e.Code)), EffectDot(e))).ToList();
        effectGroups = LedEffects.Groups.Select(g => new LedEffectGroup(tr(g), effectChips.Where((_, i) => LedEffects.All[i].Group == g).ToList())).ToList();
        sceneChips = LedEffects.Scenes.Select(s => new LedChip(tr(s.Key), false, new Command(() => ApplyScene(s)), SceneDot(s))).ToList();
        swatches = LedEffects.Swatches.Select(s => new LedSwatch(Theme.Brush(Color.FromRgb(s.R, s.G, s.B)),
            d is { Effect: 0 } && d.R == s.R && d.G == s.G && d.B == s.B, new Command(() => SetColor(s)), $"#{s.R:X2}{s.G:X2}{s.B:X2}")).ToList();
        timerRows = timers.Select(t => new LedTimerRow(t.TurnOn ? tr("Enciende") : tr("Apaga"), Time(t.Minute), DaysText(t.Days), t.Enabled,
            new Command(() => { t.Enabled = !t.Enabled; Changed(); }),
            new Command(() => { t.Minute = (t.Minute + 1440 - 15) % 1440; Changed(); }),
            new Command(() => { t.Minute = (t.Minute + 15) % 1440; Changed(); }),
            new Command(() => { t.Days = (t.Days + 1) % 4; Changed(); }),
            new Command(() => { timers.Remove(t); Changed(); }))).ToList();
        var tabNames = new[] { tr("Color"), tr("Efectos"), tr("Escenas"), tr("Música"), tr("Alarmas") };
        tabSegments = tabNames.Select((n, i) => new Segment(n, i == tab, new Command(() => SelectTab(i)))).ToList();
        musicSegments = new[] { tr("Energía"), tr("Ritmo") }.Select((n, i) => new Segment(n, i == musicMode, new Command(() => { musicMode = i; Changed(); }))).ToList();
    }

    public void Dispose()
    {
        musicTimer.Stop();
        meter?.Dispose();
        foreach (var d in devices) (d.Link as IDisposable)?.Dispose();
    }

    /// Datos de muestra para la galería de widgets: dos tiras, la primera encendida en violeta.
    public static LedWidget Demo()
    {
        var widget = new LedWidget(new Prefs(), demo: true);
        widget.devices.Add(new LedDevice(1, tr("Escritorio"), LedFamily.Elk) { R = 0x8A, G = 0x4D, B = 0xFF, Brightness = 70 });
        widget.devices.Add(new LedDevice(2, tr("Lámpara"), LedFamily.ElkAlt) { R = 0xFF, G = 0x9A, B = 0x3C, Brightness = 45 });
        widget.timers.Add(new LedTimer { TurnOn = true, Minute = 19 * 60 + 30, Days = 0 });
        widget.timers.Add(new LedTimer { TurnOn = false, Minute = 1 * 60 + 30, Days = 1 });
        widget.signature = "";
        widget.Rebuild();
        return widget;
    }
}
