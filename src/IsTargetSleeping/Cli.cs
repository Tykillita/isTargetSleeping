using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IsTargetSleeping.UI;
using IsTargetSleeping.UI.Pets;

namespace IsTargetSleeping;

/// Uso por terminal, con el mismo camino que el interruptor del panel:
///   isTargetSleeping --status | --on | --off | --sleep | --clean
///   isTargetSleeping --url istargetsleeping://sleep
///   isTargetSleeping --memory
///   isTargetSleeping --idle-test 20
    ///   isTargetSleeping --snapshot salida.png [settings|activity|pet] [demo] [game] [--lang en] [--pet id]
///   isTargetSleeping --export-logo carpeta
///   isTargetSleeping --export-tray carpeta
///   isTargetSleeping --export-pet carpeta
/// Es una app de ventana: en PowerShell, canaliza la salida (`| Write-Output`)
/// para que la terminal espere a que termine.
public static class Cli
{
    private static readonly string[] Flags = ["--status", "--on", "--off", "--sleep", "--clean", "--memory", "--idle-test", "--snapshot", "--export-logo", "--export-tray", "--export-pet", "--help"];

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
            if (Value(args, "--export-tray") is { } trayDir) return ExportTray(trayDir);
            if (Value(args, "--export-pet") is { } petDir) return ExportPet(petDir);
            if (args.Contains("--on")) return Power(on: true);
            if (args.Contains("--off")) return Power(on: false);
            if (args.Contains("--sleep")) return SleepNow();
            if (args.Contains("--clean")) return CleanNow();
            if (args.Contains("--status")) return Status();
            Console.WriteLine($"{AppInfo.Name} --status | --on | --off | --sleep | --clean | --url link | --memory | --idle-test N | --snapshot out.png [settings|activity|pet] [demo] [game] [--lang en] [--pet mira|llama|capybara|orange-cat] | --export-logo dir | --export-tray dir | --export-pet dir");
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
        if (result.Error is { } error) Console.WriteLine(error);
        Console.WriteLine($"{result.Outcome} · cambio observado a los 5 s: {result.Freed.MemoryGB()} · duración {result.DurationMilliseconds / 1000.0:0.0} s · tratados {result.ProcessesTreated} · zonas {areas}"
                          + (result.Failed != CleanAreas.None ? $" · fallaron {result.Failed}" : ""));
        foreach (var failure in result.Errors)
            Console.WriteLine($"  {failure.Operation} · PID {failure.Pid?.ToString() ?? "-"} · Win32 {failure.Win32Error?.ToString() ?? "-"} · NTSTATUS {failure.NtStatus?.ToString("X8") ?? "-"}: {failure.Message}");
        if (result.Before is { } before && result.AfterThirtySeconds is { } after)
            Console.WriteLine($"cambio observado a los 30 s: {(before.Used - after.Used).MemoryGB()} · disponible {after.Available.MemoryGB()} · comprometida {after.Committed.MemoryGB()} / {after.CommitLimit.MemoryGB()}");
        return result.Outcome switch { CleanOutcome.Partial => 2, CleanOutcome.Failed or CleanOutcome.Pending => 1, _ => 0 };
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
        if (args.Contains("pet")) return SnapshotPet(output, PetCatalog.Find(Value(args, "--pet")));
        var ollama = new OllamaController();
        var prefs = new Prefs();
        var supervisor = new Supervisor(ollama, prefs);
        if (args.Contains("demo")) { ollama.LoadDemo(); prefs.LoadDemo(); supervisor.LoadDemo(game: args.Contains("game")); }
        else Task.Run(ollama.Refresh).GetAwaiter().GetResult();   // fuera del hilo de la interfaz: si no, se bloquea
        if (args.Contains("demo") && Value(args, "--pet") is { } previewPet)
        {
            prefs.SetPetSpecies(PetCatalog.Find(previewPet).Id);
            prefs.SetSwitch(PrefKeys.TaskbarPet, true);
        }
        if (args.Contains("update")) supervisor.LoadUpdateDemo(
            args.Contains("downloading") ? UpdateStage.Downloading : args.Contains("verifying") ? UpdateStage.Verifying
                : args.Contains("applying") ? UpdateStage.Applying : UpdateStage.Idle, args.Contains("update-error"));

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
        if (args.Contains("settings") && args.Contains("demo") && Value(args, "--pet") is not null)
        {
            view.BodyScroll.ScrollToBottom();
            frame.UpdateLayout();
        }

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

    /// Vista previa de la mascota en cada actividad y reacción, sobre una barra simulada.
    /// No cambia ajustes ni crea la ventana que sigue a Inicio.
    private static int SnapshotPet(string output, IPetSpecies pet)
    {
        const int scale = 3, columns = 5;
        var poses = Enum.GetValues<PetActivity>().Where(a => a != PetActivity.Hidden)
            .Select(a => (a.ToString(), Simulate(pet, new PetSituation(a), 1.0, 1).Last()))
            .Concat(Enum.GetValues<PetReaction>().Where(r => r != PetReaction.None)
                .Select(r => (r.ToString(), Simulate(pet, new PetSituation(PetActivity.Alert, r, 0.5), 0.5 + pet.Animations.ReactionDuration(r) * 0.4, 0.1).Last())))
            .ToList();
        int cellW = (pet.Size.Width + 6) * scale, cellH = (pet.Size.Height + 8) * scale;
        int width = columns * cellW, height = (poses.Count + columns - 1) / columns * cellH;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Theme.Brush(Theme.Hex(0x101317)), null, new Rect(0, 0, width, height));
            for (int i = 0; i < poses.Count; i++)
            {
                double x = i % columns * cellW, y = i / columns * cellH;
                dc.DrawRectangle(Theme.Brush(Theme.Hex(0x20252C)), null, new Rect(x, y + cellH - 6 * scale, cellW, 6 * scale));
                dc.DrawImage(PetCell(pet, poses[i].Item2, scale), new Rect(x, y, cellW, cellH));
                dc.DrawText(new FormattedText(poses[i].Item1, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 11, Theme.Brush(Theme.Current.Secondary), 1.0), new Point(x + 6, y + 4));
            }
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        File.WriteAllBytes(output, Mark.Png(bitmap));
        Console.WriteLine($"captura: {Path.GetFullPath(output)}");
        return 0;
    }

    /// Un fotograma simulado: la pose, las partículas y cuánto se desplaza toda la mascota.
    private readonly record struct PetShot(PetPose Pose, Particle[] Particles, double Dx, double Dy, bool Thinking);

    /// Lo que haría la mascota en la barra durante `seconds`, a 30 fps, tomando fotogramas
    /// cada `every` segundos (con el mismo director que la de verdad).
    private static List<PetShot> Simulate(IPetSpecies pet, PetSituation situation, double seconds, double every,
        PetSituation? before = null, double beforeSeconds = 0)
    {
        var director = new PetDirector(pet.Anchors, seed: 1, animations: pet.Animations);
        var shots = new List<PetShot>();
        double t = 0, nextShot = 0;
        const double dt = 1 / 30.0;
        if (before is { } warm)
            for (; t < beforeSeconds; t += dt) director.Step(t, dt, warm);
        double start = t;
        for (; t <= start + seconds + 1e-9; t += dt)
        {
            var s = situation with { ReactionSince = situation.Reaction == PetReaction.None ? 0 : start + situation.ReactionSince };
            var frame = director.Step(t, dt, s);
            if (t - start + 1e-9 >= nextShot)
            {
                shots.Add(new(frame.Pose, [.. director.Particles.Live], frame.Dx, frame.Dy,
                    situation.Activity == PetActivity.Working && situation.Reaction == PetReaction.None));
                nextShot += every;
            }
        }
        return shots;
    }

    /// Una celda con la mascota (y sitio alrededor para los saltos y las partículas).
    private static BitmapSource PetCell(IPetSpecies pet, PetShot shot, int scale)
    {
        int w = pet.Size.Width * scale, h = pet.Size.Height * scale;
        var canvas = new PixelCanvas(w, h);
        pet.Draw(canvas, shot.Pose, scale);
        ParticleSprites.Draw(canvas, shot.Particles, scale);
        if (shot.Thinking) ParticleSprites.Thinking(canvas, pet.Anchors, scale, 0.3);
        var sprite = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, canvas.ToBgra(), w * 4);
        int cw = (pet.Size.Width + 6) * scale, ch = (pet.Size.Height + 8) * scale;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawImage(sprite, new Rect(3 * scale + Math.Round(shot.Dx * scale), 6 * scale + Math.Round(shot.Dy * scale), w, h));
        var cell = new RenderTargetBitmap(cw, ch, 96, 96, PixelFormats.Pbgra32);
        cell.Render(visual);
        return cell;
    }

    /// Hojas con la mascota animada, simulada a 30 fps: cada actividad, transición, los
    /// gestos de su carácter, reacción y el trato con el ratón (con sus partículas), a 2× y
    /// 3×, sobre barra oscura y clara.
    private static int ExportPet(string dir) => RunWithApp(() =>
    {
        Directory.CreateDirectory(dir);
        ExportPetCollection(dir);
        foreach (var pet in PetCatalog.All)
        {
            var rows = new List<(string Name, List<PetShot> Shots)>();
            foreach (var a in Enum.GetValues<PetActivity>().Where(a => a != PetActivity.Hidden))
            {
                double length = pet.Animations.For(a).Length;
                rows.Add((a.ToString(), Simulate(pet, new PetSituation(a), length, length / 12)));
            }
            rows.Add(("Peek", Simulate(pet, new PetSituation(PetActivity.Drowsy, Hiding: true), pet.Animations.Peek.Length, pet.Animations.Peek.Length / 16)));
            (PetActivity From, PetActivity To, bool FromHiding)[] changes =
            [
                (PetActivity.DeepSleep, PetActivity.WakingUp, false), (PetActivity.Alert, PetActivity.Working, false),
                (PetActivity.Working, PetActivity.Alert, false), (PetActivity.Alert, PetActivity.Downloading, false),
                (PetActivity.Eating, PetActivity.Alert, false), (PetActivity.Alert, PetActivity.DeepSleep, false),
                (PetActivity.Drowsy, PetActivity.Eating, true),
            ];
            foreach (var (from, to, hiding) in changes)
            {
                double length = (pet.Animations.Transition(from, to, hiding, false)?.Length ?? 0) + 0.4;
                rows.Add(($"{from} → {to}", Simulate(pet, new PetSituation(to), length, length / 16, new PetSituation(from, Hiding: hiding), 1)));
            }
            // Solo los gestos de su carácter, con las partículas que sueltan.
            foreach (var g in pet.Animations.IdleGestures(false).Concat(pet.Animations.IdleGestures(true)).Select(o => o.Gesture).Distinct())
            {
                var clip = pet.Animations.Gesture(g);
                var fx = new PetParticles(pet.Anchors, 1);
                var shots = new List<PetShot>();
                double next = 0;
                for (double t = 0; t <= clip.Length + 1e-9; t += 1 / 30.0)
                {
                    fx.Update(1 / 30.0);
                    foreach (var d in pet.Animations.GestureParticles(g)) fx.Drip(d.Kind, d.Every, t);
                    if (t + 1e-9 < next) continue;
                    var motion = pet.Animations.Motion(g, t);
                    shots.Add(new PetShot(clip.At(t), [.. fx.Live], motion.Dx, motion.Dy, false));
                    next += clip.Length / 11;
                }
                rows.Add(($"gesture {g}", shots));
            }
            // El ratón: llega, va de un lado a otro y se queda quieto encima.
            {
                var director = new PetDirector(pet.Anchors, seed: 1, animations: pet.Animations);
                var shots = new List<PetShot>();
                for (double t = 0; t <= 3.6; t += 1 / 30.0)
                {
                    double x = t < 2.4 ? Math.Sin(t * 2.6) : 0.3;
                    var frame = director.Step(t, 1 / 30.0, new PetSituation(PetActivity.Alert, Hovered: t >= 0.3, HoverX: x, HoverY: -0.2));
                    if (Math.Round(t * 30) % 9 == 0) shots.Add(new(frame.Pose, [.. director.Particles.Live], frame.Dx, frame.Dy, false));
                }
                rows.Add(("ratón", shots));
            }
            foreach (var r in Enum.GetValues<PetReaction>().Where(r => r != PetReaction.None))
                rows.Add((r.ToString(), Simulate(pet, new PetSituation(PetActivity.Alert, r, 0), pet.Animations.ReactionDuration(r), pet.Animations.ReactionDuration(r) / 12)));
            foreach (int facing in new[] { -1, 1 })
                rows.Add(($"Walk {facing}", Enumerable.Range(0, 12).Select(i => new PetShot(pet.Animations.Walk(i * 0.5, facing, 0, false), [], 0, 0, false)).ToList()));
            rows.Add(("Reduced motion", Enum.GetValues<PetActivity>().Where(a => a != PetActivity.Hidden)
                .Select(a => Simulate(pet, new PetSituation(a, Still: true), 0, 1).Last()).ToList()));

            foreach (var scale in new[] { 2, 3 })
            foreach (var (theme, background) in new[] { ("dark", Theme.Hex(0x1C1C1C)), ("light", Theme.Hex(0xEEEEEE)) })
            {
                int zoom = 2, cellW = (pet.Size.Width + 6) * scale * zoom, cellH = (pet.Size.Height + 8) * scale * zoom, gap = 4, label = 170;
                int width = label + rows.Max(r => r.Shots.Count) * (cellW + gap), height = rows.Count * (cellH + gap) + gap;
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(Theme.Brush(background), null, new Rect(0, 0, width, height));
                    var fg = Theme.Brush(theme == "light" ? Colors.Black : Colors.White);
                    for (int row = 0; row < rows.Count; row++)
                    {
                        double y = gap + row * (cellH + gap);
                        dc.DrawText(new FormattedText(rows[row].Name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                            new Typeface("Segoe UI"), 13, fg, 1.0), new Point(6, y + cellH / 2.0 - 9));
                        for (int k = 0; k < rows[row].Shots.Count; k++)
                            dc.DrawImage(Enlarge(PetCell(pet, rows[row].Shots[k], scale), zoom), new Rect(label + k * (cellW + gap), y, cellW, cellH));
                    }
                }
                var sheet = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                sheet.Render(visual);
                File.WriteAllBytes(Path.Combine(dir, $"pet-{pet.Id}-{scale}x-{theme}.png"), Mark.Png(sheet));
            }
        }

        // Escondida tras el logo: cada fotograma junto a un logo de Windows dibujado (a 100 %, ampliado).
        foreach (var pet in PetCatalog.All.Where(p => p.Size.HideRight > 0 && p.Animations.HidesBehindLogo))
        {
            const int n = 2, zoom = 4, cellW = 90, cellH = 60, count = 24;
            var logo = new Win32.RECT { Left = 50, Top = 18, Right = 73, Bottom = 41 };
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Theme.Brush(Theme.Hex(0x0C2A2E)), null, new Rect(0, 0, count * cellW * zoom, cellH * zoom));
                for (int k = 0; k < count; k++)
                {
                    var pose = pet.Animations.Peek.At(pet.Animations.Peek.Length * k / count);
                    dc.PushTransform(new TransformGroup { Children = [new TranslateTransform(k * cellW, 0), new ScaleTransform(zoom, zoom)] });
                    var (x, y) = PetHiding.Behind(logo, pet.Size, n, 0);
                    var pane = new RectangleGeometry(new Rect(logo.Left, logo.Top, logo.Width, logo.Height));
                    dc.PushClip(Geometry.Combine(new RectangleGeometry(new Rect(0, 0, cellW, cellH)), pane, GeometryCombineMode.Exclude, null));
                    var canvas = new PixelCanvas(pet.Size.Width * n, pet.Size.Height * n);
                    pet.Draw(canvas, pose, n);
                    var sprite = BitmapSource.Create(canvas.Width, canvas.Height, 96, 96, PixelFormats.Pbgra32, null, canvas.ToBgra(), canvas.Width * 4);
                    dc.DrawImage(sprite, new Rect(x - Math.Round(pose.Peek * n), y - Math.Round(pose.PeekY * n), canvas.Width, canvas.Height));
                    dc.Pop();
                    var light = Theme.Brush(Theme.Hex(0x4CC2FF));
                    var deep = Theme.Brush(Theme.Hex(0x1A8FE8));
                    dc.DrawRectangle(light, null, new Rect(logo.Left, logo.Top, 11, 11));
                    dc.DrawRectangle(light, null, new Rect(logo.Left + 12, logo.Top, 11, 11));
                    dc.DrawRectangle(deep, null, new Rect(logo.Left, logo.Top + 12, 11, 11));
                    dc.DrawRectangle(deep, null, new Rect(logo.Left + 12, logo.Top + 12, 11, 11));
                    dc.Pop();
                }
            }
            var sheet = new RenderTargetBitmap(count * cellW * zoom, cellH * zoom, 96, 96, PixelFormats.Pbgra32);
            sheet.Render(visual);
            File.WriteAllBytes(Path.Combine(dir, $"pet-{pet.Id}-peek.png"), Mark.Png(sheet));
        }
        Console.WriteLine($"mascota: {Path.GetFullPath(dir)}");
        return 0;
    });

    /// Comparación de las siluetas y camas, con muestras a tamaño de barra y ampliadas.
    private static void ExportPetCollection(string dir)
    {
        foreach (var (theme, background, ink) in new[]
            { ("dark", 0x171C22u, 0xF0E9DCu), ("light", 0xF1EEE7u, 0x38322Cu) })
        {
            const int cellW = 180, height = 350;
            int width = cellW * PetCatalog.All.Count;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Theme.Brush(Theme.Hex(background)), null, new Rect(0, 0, width, height));
                for (int i = 0; i < PetCatalog.All.Count; i++)
                {
                    var pet = PetCatalog.All[i];
                    double left = i * cellW;
                    var text = new FormattedText(pet.Name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface("Segoe UI Semibold"), 15, Theme.Brush(Theme.Hex(ink)), 1.0);
                    dc.DrawText(text, new Point(left + (cellW - text.Width) / 2, 18));
                    foreach (var (activity, y) in new[] { (PetActivity.Alert, 42), (PetActivity.DeepSleep, 154) })
                    {
                        var shot = new PetShot(pet.Animations.Still(activity), [], 0, 0, false);
                        var bitmap = PetCell(pet, shot, 3);
                        dc.DrawImage(bitmap, new Rect(left + (cellW - bitmap.PixelWidth) / 2, y, bitmap.PixelWidth, bitmap.PixelHeight));
                    }
                    // El tamaño sin ampliar permite juzgar si los detalles sobreviven en la barra.
                    foreach (var (activity, x) in new[] { (PetActivity.Alert, 18), (PetActivity.DeepSleep, 94) })
                    {
                        var shot = new PetShot(pet.Animations.Still(activity), [], 0, 0, false);
                        var bitmap = PetCell(pet, shot, 2);
                        dc.DrawImage(bitmap, new Rect(left + x, 268, bitmap.PixelWidth, bitmap.PixelHeight));
                    }
                }
            }
            var sheet = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            sheet.Render(visual);
            File.WriteAllBytes(Path.Combine(dir, $"pet-collection-{theme}.png"), Mark.Png(sheet));
        }
    }

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

    /// Hojas con cada fotograma de las animaciones del ícono de la bandeja y del % de
    /// RAM, a 16/20/24/32 px sobre barra oscura y clara, ampliadas píxel a píxel.
    private static int ExportTray(string dir) => RunWithApp(() =>
    {
        Directory.CreateDirectory(dir);
        const double dt = 1 / TrayMotion.Fps;
        // Cada fila: los estados por los que pasa (un segundo en cada uno) y cuántos
        // fotogramas se toman desde el último cambio.
        var rows = new (string Name, (TrayPhase Phase, bool Awake)[] Steps, int Frames)[]
        {
            ("searching", [(TrayPhase.Off, false), (TrayPhase.Starting, false)], 30),
            ("lock-on", [(TrayPhase.Off, false), (TrayPhase.Starting, false), (TrayPhase.On, false)], 14),
            ("letting go", [(TrayPhase.On, true), (TrayPhase.Stopping, false)], 30),
            ("off", [(TrayPhase.On, false), (TrayPhase.Stopping, false), (TrayPhase.Off, false)], 12),
            ("failed", [(TrayPhase.Off, false), (TrayPhase.Starting, false), (TrayPhase.Off, false)], 12),
            ("eye open", [(TrayPhase.On, false), (TrayPhase.On, true)], 9),
            ("eye close", [(TrayPhase.On, true), (TrayPhase.On, false)], 11),
        };
        int[] values = [0, 1, 7, 11, 38, 44, 63, 66, 71, 88, 99];
        foreach (var px in new[] { 16, 20, 24, 32 })
        foreach (var (theme, light) in new[] { ("dark", false), ("light", true) })
        {
            var strips = new List<(string, List<BitmapSource>)>();
            foreach (var (name, steps, frames) in rows)
            {
                var motion = new TrayMotion(steps[0].Phase, steps[0].Awake, animate: true);
                double t = 0;
                foreach (var (phase, awake) in steps.Skip(1))
                {
                    t += 1;
                    motion.Set(phase, awake, t);
                }
                strips.Add((name, Enumerable.Range(0, frames).Select(k =>
                    Mark.PoseBitmap(px, motion.PoseAt(t + k * dt), light)).ToList()));
            }
            strips.Add(("static", [Mark.PoseBitmap(px, TrayPose.Off, light), Mark.PoseBitmap(px, TrayPose.On(false), light),
                Mark.PoseBitmap(px, TrayPose.On(true), light), Mark.PoseBitmap(px, TrayPose.Busy, light)]));
            strips.Add(("ram %", values.Select(v => Mark.PercentBitmap(px, v, SystemMemory.Level.Normal, Mark.Dot.On, light)).ToList()));
            strips.Add(("shimmer", Enumerable.Range(0, 12).Select(k => Mark.PercentBitmap(px, 63, SystemMemory.Level.Normal,
                Mark.Dot.Busy, light, 2.0 * k / TrayMotion.LoopFrames)).ToList()));

            int zoom = Math.Max(4, 128 / px), cell = px * zoom, gap = 6, label = 110;
            int width = label + strips.Max(s => s.Item2.Count) * (cell + gap), height = strips.Count * (cell + gap) + gap;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var bg = light ? Color.FromRgb(0xEE, 0xEE, 0xEE) : Color.FromRgb(0x1C, 0x1C, 0x1C);
                var fg = Theme.Brush(light ? Colors.Black : Colors.White);
                dc.DrawRectangle(Theme.Brush(bg), null, new Rect(0, 0, width, height));
                for (int row = 0; row < strips.Count; row++)
                {
                    var (name, images) = strips[row];
                    double y = gap + row * (cell + gap);
                    dc.DrawText(new FormattedText(name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"), 13, fg, 1.0), new Point(6, y + cell / 2.0 - 9));
                    for (int k = 0; k < images.Count; k++)
                        dc.DrawImage(Enlarge(images[k], zoom), new Rect(label + k * (cell + gap), y, cell, cell));
                }
            }
            var sheet = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            sheet.Render(visual);
            File.WriteAllBytes(Path.Combine(dir, $"tray-{theme}-{px}.png"), Mark.Png(sheet));
        }
        Console.WriteLine($"bandeja: {Path.GetFullPath(dir)}");
        return 0;
    });

    /// Ampliado sin suavizar, para ver cada píxel.
    private static BitmapSource Enlarge(BitmapSource source, int zoom)
    {
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight;
        var pixels = new byte[w * h * 4];
        bgra.CopyPixels(pixels, w * 4, 0);
        int bw = w * zoom, bh = h * zoom;
        var big = new byte[bw * bh * 4];
        for (int y = 0; y < bh; y++)
            for (int x = 0; x < bw; x++)
                Buffer.BlockCopy(pixels, ((y / zoom) * w + x / zoom) * 4, big, (y * bw + x) * 4, 4);
        return BitmapSource.Create(bw, bh, 96, 96, PixelFormats.Pbgra32, null, big, bw * 4);
    }
}
