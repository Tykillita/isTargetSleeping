using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using IsTargetSleeping;
using IsTargetSleeping.UI;
using IsTargetSleeping.UI.Widgets;

internal static class PerformanceTests
{
    public static void Run(Action<string, bool> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "its-agent-cache-" + Guid.NewGuid().ToString("N"));
        var oldCodex = Environment.GetEnvironmentVariable("CODEX_HOME");
        var oldClaude = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        try
        {
            var codex = Path.Combine(root, "codex");
            var claude = Path.Combine(root, "claude");
            var sessions = Path.Combine(codex, "sessions");
            Directory.CreateDirectory(sessions);
            Directory.CreateDirectory(Path.Combine(claude, "projects"));
            Environment.SetEnvironmentVariable("CODEX_HOME", codex);
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", claude);
            var path = Path.Combine(sessions, "rollout-test.jsonl");
            File.WriteAllText(path, "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}\n");
            var watcher = new AgentsWatcher();
            var first = watcher.ReadRecent().Single();
            var unchanged = watcher.ReadRecent().Single();
            check("agentes: archivo sin cambios reutiliza el resultado", ReferenceEquals(first, unchanged) && first.State == AgentState.Working);
            L10n.Init(Language.En);
            check("agentes: cambiar el idioma renueva el resultado", !ReferenceEquals(unchanged, watcher.ReadRecent().Single()));
            L10n.Init(Language.Es);
            File.AppendAllText(path, "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\"}}\n");
            var finished = watcher.ReadRecent().Single();
            check("agentes: un registro agregado actualiza el estado", !ReferenceEquals(first, finished) && finished.State == AgentState.Finished);
            File.WriteAllText(path, "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}\n");
            check("agentes: un registro truncado se vuelve a leer", watcher.ReadRecent().Single().State == AgentState.Working);
            File.AppendAllText(path, "{\"type\":\"event_msg\",\"payload\":{");
            check("agentes: un registro parcial no altera el estado", watcher.ReadRecent().Single().State == AgentState.Working);
            File.AppendAllText(path, "\"type\":\"task_complete\"}}\n");
            check("agentes: al completar el registro parcial se actualiza", watcher.ReadRecent().Single().State == AgentState.Finished);
            File.Delete(path);
            var afterDelete = watcher.ReadRecent();
            var cache = (System.Collections.IDictionary)typeof(AgentsWatcher).GetField("cache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(watcher)!;
            check("agentes: eliminar el registro libera el resultado cacheado", afterDelete.Count == 0 && cache.Count == 0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", oldCodex);
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", oldClaude);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        var oldData = Environment.GetEnvironmentVariable("ITS_DATA_DIR");
        var data = Path.Combine(Path.GetTempPath(), "its-perf-data-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("ITS_DATA_DIR", data);
        try
        {
            var prefs = new Prefs();
            prefs.LoadDemo();
            var ollama = new OllamaController();
            ollama.LoadDemo();
            var supervisor = new Supervisor(ollama, prefs);
            supervisor.LoadDemo();
            var model = new PanelViewModel(supervisor);
            var temporaryFile = Path.GetTempFileName();
            try
            {
                model.AddToTray([temporaryFile]);
                var thumbnails = (System.Collections.IDictionary)typeof(PanelViewModel).GetField("trayThumbnails", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(model)!;
                bool cached = thumbnails.Contains(temporaryFile);
                model.ClearTray.Execute(null);
                check("bandeja: quitar archivos libera sus miniaturas", cached && thumbnails.Count == 0);
        }
        finally { File.Delete(temporaryFile); }
        var view = new ContentView(model);
        var window = new NotchWindow(model, view);
        static int Subscribers(PanelViewModel model, string name) =>
            (typeof(PanelViewModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(model) as Delegate)?.GetInvocationList().Length ?? 0;
        var before = Subscribers(model, "PropertyChanged");
        var changing = Subscribers(model, "PeekPageChanging");
        var changed = Subscribers(model, "PeekPageChanged");
        var lyricClock = (DispatcherTimer)typeof(NotchWindow).GetField("lyricClock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var mascotClock = (DispatcherTimer)typeof(NotchWindow).GetField("mascotClock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        bool inactiveClocksStopped = !lyricClock.IsEnabled && !mascotClock.IsEnabled;
        model.Prefs.SetSwitch(PrefKeys.NotchLyrics, true);
        bool lyricsStart = lyricClock.IsEnabled;
        model.Prefs.SetSwitch(PrefKeys.NotchLyrics, false);
        bool lyricsStop = !lyricClock.IsEnabled;
        model.Prefs.SetSwitch(PrefKeys.NotchMascot, true);
        bool mascotStart = mascotClock.IsEnabled;
        model.Prefs.SetSwitch(PrefKeys.NotchMascot, false);
        check("notch: los relojes de letras y mascota solo corren al activar su función",
            inactiveClocksStopped && lyricsStart && lyricsStop && mascotStart && !mascotClock.IsEnabled);

        var visiblePeek = new NotchPeek(model);
        var headerClock = (DispatcherTimer)typeof(NotchPeek).GetField("headerClock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(visiblePeek)!;
        var peekHost = new Window { Content = visiblePeek, Width = 420, Height = 260, Left = -20000, Top = -20000, ShowInTaskbar = false, ShowActivated = false };
        bool headerStartsPaused = !headerClock.IsEnabled;
        peekHost.Show();
        bool headerRunsWhenVisible = headerClock.IsEnabled;
        prefs.SetList(PrefKeys.NotchPeekWidgets, ["pomodoro", "todo@1", "shelf@1"]);
        peekHost.UpdateLayout();
        var homeBeforePaging = model.HomeWidgets;
        var foldersBeforePaging = model.HomeFolders;
        model.StepPeekPage(1, wrap: true);
        bool arrowChangesPage = model.PeekWidgets.Single().Id == "todo" && model.PeekDots[1].IsSelected;
        model.PeekIcons[2].Pick.Execute(null);
        bool dockChangesPage = model.PeekWidgets.Single().Id == "shelf" && model.PeekIcons[2].IsSelected;
        model.StepPeekPage(1, wrap: true);
        check("notch: flechas y dock cambian de página sin reconstruir el Home ni sus carpetas",
            arrowChangesPage && dockChangesPage && model.PeekWidgets.Single().Id == "pomodoro"
            && ReferenceEquals(homeBeforePaging, model.HomeWidgets) && ReferenceEquals(foldersBeforePaging, model.HomeFolders));
        model.PeekIcons[2].Pick.Execute(null);
        model.SetPeekVisible(true);
        check("notch: al abrir la vista corta vuelve a la primera página", model.PeekWidgets.Single().Id == "pomodoro" && model.PeekDots[0].IsSelected);
        model.SetPeekVisible(false);
        bool originalReducedMotion = prefs.ReducedMotion;
        prefs.SetSwitch(PrefKeys.ReducedMotion, false);
        peekHost.UpdateLayout();
        model.StepPeekPage(1, wrap: true);
        prefs.SetSwitch(PrefKeys.ReducedMotion, true);
        model.StepPeekPage(1, wrap: true);
        var page = (FrameworkElement)visiblePeek.FindName("pageHost");
        var oldPage = (System.Windows.Controls.Image)visiblePeek.FindName("oldPage");
        check("notch: movimiento reducido cancela la transición en curso",
            !page.HasAnimatedProperties && !page.RenderTransform.HasAnimatedProperties && page.CacheMode is null && oldPage.Source is null);
        prefs.SetSwitch(PrefKeys.ReducedMotion, originalReducedMotion);
        var setEq = typeof(NotchPeek).GetMethod("SetEq", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var setPomodoro = typeof(NotchPeek).GetMethod("SetPomodoro", BindingFlags.Instance | BindingFlags.NonPublic)!;
        static FrameworkElement Element(NotchPeek peek, string name) =>
            (FrameworkElement)typeof(NotchPeek).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(peek)!;
        static bool Animated(FrameworkElement element) => element.RenderTransform.HasAnimatedProperties
            || element.RenderTransform is System.Windows.Media.TransformGroup group && group.Children.Any(transform => transform.HasAnimatedProperties);
        var eqBars = new[] { Element(visiblePeek, "eq1"), Element(visiblePeek, "eq2"), Element(visiblePeek, "eq3"), Element(visiblePeek, "eq4") };
        var waveBack = Element(visiblePeek, "pomoWaveBack");
        bool reducedMotion = model.Prefs.ReducedMotion;
        setEq.Invoke(visiblePeek, [true]);
        setPomodoro.Invoke(visiblePeek, [true]);
        bool headerAnimationsStart = eqBars.All(Animated) && Animated(waveBack);
        visiblePeek.Visibility = Visibility.Collapsed;
        bool headerStopsWhenHidden = !headerClock.IsEnabled;
        bool headerAnimationsStop = !eqBars.Any(Animated) && !Animated(waveBack)
            && !(bool)typeof(NotchPeek).GetField("eqOn", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(visiblePeek)!
            && !(bool)typeof(NotchPeek).GetField("sandMoving", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(visiblePeek)!;
        visiblePeek.Visibility = Visibility.Visible;
        bool headerResumesWhenVisible = headerClock.IsEnabled;
        setEq.Invoke(visiblePeek, [true]);
        setPomodoro.Invoke(visiblePeek, [true]);
        bool headerAnimationsResume = eqBars.All(Animated) && Animated(waveBack);
        visiblePeek.Stop();
        bool headerStopsOnDispose = !headerClock.IsEnabled;
        bool headerAnimationsStopOnDispose = !eqBars.Any(Animated) && !Animated(waveBack);
        peekHost.Close();
        check("notch: reloj y animaciones del encabezado paran ocultos y retoman al mostrarse",
            headerStartsPaused && headerRunsWhenVisible && headerStopsWhenHidden && headerResumesWhenVisible && headerStopsOnDispose
            && headerAnimationsStop && headerAnimationsStopOnDispose
            && (reducedMotion || headerAnimationsStart && headerAnimationsResume));

        var camera = model.Camera;
        var updateFrame = typeof(CameraPreview).GetMethod("UpdateFrame", BindingFlags.Instance | BindingFlags.NonPublic)!;
        byte[] firstPixels = [10, 20, 30, 255, 40, 50, 60, 255, 70, 80, 90, 255, 100, 110, 120, 255];
        byte[] nextPixels = [120, 110, 100, 255, 90, 80, 70, 255, 60, 50, 40, 255, 30, 20, 10, 255];
        int changes = 0;
        camera.PropertyChanged += (_, _) => changes++;
        using var sourceBitmap = new Windows.Graphics.Imaging.SoftwareBitmap(
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, 2, 2, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
        using (var writer = new Windows.Storage.Streams.DataWriter())
        {
            writer.WriteBytes(firstPixels);
            sourceBitmap.CopyFromBuffer(writer.DetachBuffer());
        }
        var copyFramePixels = typeof(CameraPreview).GetMethod("CopyFramePixels", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var first = ((byte[] Pixels, int Width, int Height))copyFramePixels.Invoke(camera, [sourceBitmap])!;
        updateFrame.Invoke(camera, [first.Pixels, first.Width, first.Height]);
        var firstCopied = first.Pixels.ToArray();
        var bitmap = (System.Windows.Media.Imaging.WriteableBitmap)camera.Frame!;
        var copied = new byte[firstPixels.Length];
        bitmap.CopyPixels(copied, 8, 0);
        int notifications = changes;
        using (var writer = new Windows.Storage.Streams.DataWriter())
        {
            writer.WriteBytes(nextPixels);
            sourceBitmap.CopyFromBuffer(writer.DetachBuffer());
        }
        var next = ((byte[] Pixels, int Width, int Height))copyFramePixels.Invoke(camera, [sourceBitmap])!;
        updateFrame.Invoke(camera, [next.Pixels, next.Width, next.Height]);
        bitmap.CopyPixels(copied, 8, 0);
        check("cámara: fotogramas sin pérdida reutilizan la imagen y no cambian su enlace",
            first.Width == 2 && first.Height == 2 && firstCopied.SequenceEqual(firstPixels)
            && ReferenceEquals(first.Pixels, next.Pixels) && copied.SequenceEqual(nextPixels)
            && ReferenceEquals(bitmap, camera.Frame) && changes == notifications);

        window.Stop();
        bool stopped = new[] { "hudTimer", "lyricClock", "mascotClock" }.All(name =>
            !((DispatcherTimer)typeof(NotchWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!).IsEnabled);
        check("notch: cierra relojes y desvincula las vistas del modelo", stopped
            && Subscribers(model, "PropertyChanged") == before - 1
            && Subscribers(model, "PeekPageChanging") == changing - 1
            && Subscribers(model, "PeekPageChanged") == changed - 1);
        window.Close();
        model.Hub.Dispose();
        model.Camera.Dispose();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ITS_DATA_DIR", oldData);
            if (Directory.Exists(data)) Directory.Delete(data, true);
        }
    }
}
