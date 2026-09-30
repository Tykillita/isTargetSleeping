using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IsTargetSleeping.UI;

namespace IsTargetSleeping;

/// Uso por terminal, con el mismo camino que el interruptor del panel:
///   isTargetSleeping --status | --on | --off | --sleep | --clean
///   isTargetSleeping --url istargetsleeping://sleep
///   isTargetSleeping --memory
///   isTargetSleeping --idle-test 20
///   isTargetSleeping --snapshot salida.png [settings|activity] [demo] [game] [--lang en]
///   isTargetSleeping --export-logo carpeta
/// Es una app de ventana: en PowerShell, canaliza la salida (`| Write-Output`)
/// para que la terminal espere a que termine.
public static class Cli
{
    private static readonly string[] Flags = ["--status", "--on", "--off", "--sleep", "--clean", "--memory", "--idle-test", "--snapshot", "--export-logo", "--help"];

    public static bool Wants(string[] args) => args.Any(Flags.Contains);

    public static int Run(string[] args)
    {
        AttachConsole();
        try
        {
            if (args.Contains("--memory")) return Memory();
            if (Value(args, "--idle-test") is { } s && double.TryParse(s, CultureInfo.InvariantCulture, out var limit)) return IdleTest(limit);
            if (Value(args, "--snapshot") is { } output) return Snapshot(output, args);
            if (Value(args, "--export-logo") is { } dir) return ExportLogo(dir);
            if (args.Contains("--on")) return Power(on: true);
            if (args.Contains("--off")) return Power(on: false);
            if (args.Contains("--sleep")) return SleepNow();
            if (args.Contains("--clean")) return CleanNow();
            if (args.Contains("--status")) return Status();
            Console.WriteLine($"{AppInfo.Name} --status | --on | --off | --sleep | --clean | --url link | --memory | --idle-test N | --snapshot out.png [settings|activity] [demo] [game] [--lang en] | --export-logo dir");
            return 0;
        }
        finally { Console.Out.Flush(); }
    }

    private static string? Value(string[] args, string flag)
    {
        int i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// Una app de ventana no tiene consola: se engancha a la de quien la lanzó,
    /// salvo que la salida ya vaya a una tubería o a un archivo.
    private static void AttachConsole()
    {
        var handle = Win32.GetStdHandle(-11);
        bool redirected = handle != IntPtr.Zero && handle != new IntPtr(-1) && Win32.GetFileType(handle) is 1 or 3;
        if (redirected) return;
        if (Win32.AttachConsole(-1))
        {
            var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Console.SetOut(stdout);
            Console.WriteLine();
        }
    }

    private static bool ApiUp() => OllamaApi.Version().GetAwaiter().GetResult() is not null;

    private static int Status()
    {
        var backend = Detector.Detect();
        Console.WriteLine($"mecanismo: {backend.Summary} · API {OllamaApi.HostLabel}: {(ApiUp() ? "responde" : "no responde")}");
        foreach (var b in Detector.Available()) Console.WriteLine($"  instalado: {b.KindLabel} ({b.Summary}) · log: {b.LogPath ?? "-"}");
        foreach (var p in Procs.ByName("llama-server"))
        {
            bool ollama = ProcessCpu.IsOllamaChild(p.Pid, p.CommandLine);
            var (exe, rest) = Procs.SplitCommandLine(p.CommandLine);
            Console.WriteLine($"llama-server {p.Pid} ({(ollama ? "runner de Ollama" : "llama.cpp propio")}): {Procs.ImagePath(p.Pid) ?? exe} {rest}");
        }
        return 0;
    }

    private static int Power(bool on)
    {
        var backend = Detector.Detect();
        var error = on ? Switch.Start(backend) : Switch.Stop(backend);
        if (error is not null) { Console.WriteLine(error); return 1; }
        int seconds = on ? 90 : 30;
        for (int i = 0; i < seconds * 2; i++)
        {
            if (ApiUp() == on)
            {
                Console.WriteLine($"{(on ? "encendido" : "apagado")} ({backend.Summary})");
                return 0;
            }
            Thread.Sleep(500);
        }
        Console.WriteLine(on ? "no respondió en 90 s" : "sigue respondiendo");
        return on ? 1 : 0;
    }

    /// Sin la app abierta: libera RAM con el agente (hay que haberlo activado en Ajustes).
    private static int CleanNow()
    {
        var status = MemoryAgent.Status();
        if (status != AgentStatus.Ready)
        {
            Console.WriteLine(status == AgentStatus.Outdated ? "el agente de memoria es de otra versión: actualízalo en Ajustes" : "activa la limpieza de RAM en Ajustes (pide permiso de administrador una vez)");
            return 1;
        }
        var areas = Defaults.Has(PrefKeys.CleanAreas) ? (CleanAreas)(Defaults.GetInt(PrefKeys.CleanAreas) & (int)CleanAreas.All) : CleanAreas.Default;
        var keep = MemoryAgent.ModelPids();
        var result = MemoryAgent.CleanNow(areas, keep);
        if (result.Error is { } error) { Console.WriteLine(error); return 1; }
        Console.WriteLine($"liberados {result.Freed.MemoryGB()} · zonas {areas} · protegidos {keep.Count}"
                          + (result.Failed != CleanAreas.None ? $" · fallaron {result.Failed}" : ""));
        return 0;
    }

    /// Sin la app abierta: saca de la memoria lo cargado, sin apagar Ollama.
    private static int SleepNow()
    {
        var loaded = OllamaApi.Loaded().GetAwaiter().GetResult();
        if (loaded.Count == 0) { Console.WriteLine("ningún modelo en memoria"); return 0; }
        int code = 0;
        foreach (var m in loaded)
        {
            var failure = OllamaApi.SetKeepAlive(m.Name, 0).GetAwaiter().GetResult();
            Console.WriteLine(failure is null ? $"dormido: {m.Name} ({m.Bytes.MemoryGB()})" : $"{m.Name}: {failure}");
            if (failure is not null) code = 1;
        }
        return code;
    }

    private static int Memory()
    {
        var m = SystemMemory.Current();
        Console.WriteLine($"memoria usada {m.Used.MemoryGB()} de {m.Total.MemoryGB()} (instalada {m.Installed.MemoryGB()}) · presión {m.Pressure}");
        var runners = ProcessCpu.RunnerPids();
        foreach (var pid in runners)
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "runner {0}: {1:0.000} s de CPU", pid, ProcessCpu.Seconds(pid) ?? -1));
        if (Gpu.Adapter is { } gpu && Gpu.Read(runners) is { } vram)
            Console.WriteLine($"GPU {gpu.Name} ({gpu.Luid}): en uso {vram.Used.MemoryGB()} de {gpu.Total.MemoryGB()} · modelo {vram.Model.MemoryGB()}");
        else Console.WriteLine("sin GPU dedicada");
        return 0;
    }

    /// Prueba de la liberación automática con un límite en segundos.
    private static int IdleTest(double limit)
    {
        var ollama = new OllamaController { IdleLimitOverride = limit };
        var start = DateTime.Now;
        double Elapsed() => (DateTime.Now - start).TotalSeconds;
        ollama.OnAutoRelease = names =>
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "[{0,5:0.0}s] liberado automáticamente: {1}", Elapsed(), string.Join(", ", names)));
        while (Elapsed() < limit + 120)
        {
            ollama.Refresh().GetAwaiter().GetResult();
            var idle = ollama.IdleSeconds is { } i ? i.ToString("0.0", CultureInfo.InvariantCulture) : "-";
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "[{0,5:0.0}s] modelos={1} sin uso={2} s",
                Elapsed(), string.Join(",", ollama.Loaded.Select(m => m.Name)), idle));
            if (ollama.AutoReleased is not null) return 0;
            Thread.Sleep(2500);
        }
        Console.WriteLine("no se liberó a tiempo");
        return 1;
    }

    // MARK: - con interfaz

    private static int RunWithApp(Func<int> job)
    {
        var app = new App();
        app.InitializeComponent();
        int code = 1;
        app.Dispatcher.BeginInvoke(() =>
        {
            try { code = job(); }
            catch (Exception e) { Console.WriteLine(e); }
            app.Shutdown();
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        app.Run();
        return code;
    }

    /// Captura del panel a 2x, con datos de ejemplo (`demo`) o los reales.
    private static int Snapshot(string output, string[] args) => RunWithApp(() =>
    {
        Theme.Apply();
        var ollama = new OllamaController();
        var prefs = new Prefs();
        var supervisor = new Supervisor(ollama, prefs);
        if (args.Contains("demo")) { ollama.LoadDemo(); prefs.LoadDemo(); supervisor.LoadDemo(game: args.Contains("game")); }
        else Task.Run(ollama.Refresh).GetAwaiter().GetResult();   // fuera del hilo de la interfaz: si no, se bloquea

        var panelView = args.Contains("settings") ? PanelView.Settings : args.Contains("activity") ? PanelView.Activity : PanelView.Main;
        var view = new ContentView(new PanelViewModel(supervisor, panelView));
        // El acrílico lo pinta DWM y una captura de WPF no lo ve: se simula un
        // escritorio oscuro y neutro desenfocado detrás (sin colores que tiñan el
        // vidrio negro), con el velo gris del acrílico oscuro.
        // El lienzo no pide tamaño: las manchas se colocan cuando se conoce el del panel.
        var desktop = new System.Windows.Controls.Canvas { Background = Theme.Brush(Theme.Hex(0x0A0A0B)) };
        desktop.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 90, KernelType = System.Windows.Media.Effects.KernelType.Gaussian };

        var frame = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = Theme.Brush(Theme.Hex(0x3A3A3E)),
            ClipToBounds = true,
            Child = new System.Windows.Controls.Grid
            {
                Children =
                {
                    desktop,
                    new Border { Background = Theme.Brush(Theme.Hex(0x1C1C1C, 0.55)) },
                    view,
                },
            },
        };
        frame.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        frame.Arrange(new Rect(frame.DesiredSize));
        frame.UpdateLayout();

        var size = frame.DesiredSize;
        void Blob(uint color, double x, double y, double d)
        {
            var e = new System.Windows.Shapes.Ellipse { Width = d, Height = d, Fill = Theme.Brush(Theme.Hex(color, 0.85)) };
            System.Windows.Controls.Canvas.SetLeft(e, x * size.Width - d / 2);
            System.Windows.Controls.Canvas.SetTop(e, y * size.Height - d / 2);
            desktop.Children.Add(e);
        }
        Blob(0x2A2A2E, 0.10, 0.10, 260);
        Blob(0x1F1F22, 0.95, 0.45, 240);
        Blob(0x26262A, 0.05, 0.80, 220);
        frame.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(size.Width * 2), (int)Math.Ceiling(size.Height * 2), 192, 192, PixelFormats.Pbgra32);
        var clip = new DrawingVisual();
        using (var dc = clip.RenderOpen())
        {
            dc.PushClip(new RectangleGeometry(new Rect(size), 8, 8));
            dc.DrawRectangle(new VisualBrush(frame), null, new Rect(size));
        }
        bmp.Render(clip);
        File.WriteAllBytes(output, Mark.Png(bmp));
        Console.WriteLine($"captura: {Path.GetFullPath(output)}");
        return 0;
    });

    /// El ícono .ico, los PNG y los SVG del logo, desde la misma geometría.
    private static int ExportLogo(string dir) => RunWithApp(() =>
    {
        Directory.CreateDirectory(dir);
        static string Hex(System.Windows.Media.Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        const string slug = "istargetsleeping";
        File.WriteAllBytes(Path.Combine(dir, $"{AppInfo.Name}.ico"), Mark.Ico([16, 20, 24, 32, 40, 48, 64, 128, 256]));
        foreach (var px in new[] { 256, 512, 1024 })
            File.WriteAllBytes(Path.Combine(dir, $"{slug}-icon-{px}.png"), Mark.Png(Mark.AppIcon(px)));
        File.WriteAllText(Path.Combine(dir, $"{slug}-icon.svg"), Logo.Svg(Hex(Theme.Blue), Hex(Mark.IconBackground)));
        File.WriteAllText(Path.Combine(dir, $"{slug}-mark-blue.svg"), Logo.Svg(Hex(Theme.Blue)));
        foreach (var (name, dark) in new[] { ("tray-dark", false), ("tray-light", true) })
            File.WriteAllBytes(Path.Combine(dir, $"{name}-64.png"), Mark.Png(Mark.StatusBitmap(64, Mark.Dot.On, false, dark)));
        File.WriteAllText(Path.Combine(dir, $"{slug}-mark-ink.svg"), Logo.Svg("#0A0A0B"));
        File.WriteAllText(Path.Combine(dir, $"{slug}-mark-white.svg"), Logo.Svg("#FFFFFF"));
        Console.WriteLine($"logo: {Path.GetFullPath(dir)}");
        return 0;
    });
}
