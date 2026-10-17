using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using IsTargetSleeping;
using IsTargetSleeping.UI;

internal static class ProcessUiTests
{
    public static void Run(Action<string, bool> check)
    {
        long mib = 1024 * 1024;
        ProcessSnapshot Make(int pid, string path, long ram, string sid = "S-1-user", int session = 1, bool window = false,
            bool system = false, string? protection = null, long created = 100) => new()
        {
            Identity = new ProcessIdentity(pid, created), Name = "Editor", Executable = "editor.exe", Path = path,
            OwnerSid = sid, User = system ? "SYSTEM" : @"PC\demo", SessionId = session, HasWindow = window, IsSystem = system,
            IsCritical = false, WorkingSetBytes = ram * 2, PrivateWorkingSetBytes = ram,
            CommitBytes = ram * 3, CpuPercent = (pid % 13) / 10.0, ParentPid = 1, ProtectionReason = protection,
        };
        var first = Make(201, @"C:\Tools\editor.exe", 600 * mib, window: true, protection: "Foreground");
        var child = Make(202, first.Path!, 200 * mib);
        var background = Make(301, @"D:\Tools\editor.exe", 100 * mib);
        var system = Make(401, first.Path!, 50 * mib, "S-1-system", 0, system: true);
        var now = DateTimeOffset.Now;
        var sample = new ProcessSample(now, 8L * 1024 * mib, true, [first, child, background, system]);
        int captureCount = 0;
        Task<ProcessSample> Capture(CancellationToken _) { captureCount++; return Task.FromResult(sample); }
        using var model = new ProcessViewModel(Capture, persistSettings: false);
        model.Apply(sample);
        check("procesos: rutas y propietarios diferentes mantienen tres grupos", model.Groups.Count == 3 && model.Rows.Count == 3);
        var group = model.Rows[0];
        check("procesos: RAM privada agrupada y porcentaje físico", group.Ram == 800 * mib && Math.Abs(group.RamFraction - 800.0 / 8192) < 0.00001);
        check("procesos: aplicaciones inicialmente ordenadas por mayor RAM", model.Rows.Select(r => r.Ram).SequenceEqual(new long?[] { 800 * mib, 100 * mib, 50 * mib }));
        model.Toggle(group);
        var selected = model.Rows.Single(r => r.Process?.Pid == child.Pid);
        model.Selected = selected;
        model.SetSort(ProcessSort.Pid, false);
        check("procesos: despliegue y orden por PID conservan selección", model.Rows.Where(r => !r.IsGroup).Select(r => r.Process!.Pid).SequenceEqual(new[] { 201, 202 }) && ReferenceEquals(model.Selected, selected));
        sample = sample with { CapturedAt = now.AddSeconds(2), Processes = [first with { PrivateWorkingSetBytes = 20 * mib }, child, background, system] };
        model.Apply(sample);
        check("procesos: refresco conserva objetos y selección por identidad", ReferenceEquals(model.Selected, selected) && selected.Ram == 200 * mib);
        check("procesos: finalizar PID y árbol limita raíces al PID seleccionado", model.Request(ProcessActionMode.Single)?.Targets.SequenceEqual(new[] { child.Identity }) == true
            && model.Request(ProcessActionMode.Tree)?.Targets.SequenceEqual(new[] { child.Identity }) == true && model.Request(ProcessActionMode.Application) == null);
        model.Search = "301";
        check("procesos: búsqueda por PID filtra el grupo correspondiente", model.Rows.Count == 1 && model.Rows[0].Identities.Single() == background.Identity && model.Selected == null);
        model.Search = "";
        model.Filter = ProcessFilter.Protected;
        check("procesos: filtro protegido mantiene su grupo y descendientes desplegados", model.Rows.Count(r => r.IsGroup) == 1 && model.Rows[0].Group!.IsProtected);
        model.Filter = ProcessFilter.System;
        check("procesos: filtro sistema", model.Rows.Count == 1 && model.Rows[0].Group!.IsSystem);
        model.Filter = ProcessFilter.Background;
        check("procesos: segundo plano excluye sistema y aplicaciones con ventana", model.Rows.Count == 1 && model.Rows[0].Group!.Processes.Single().Pid == background.Pid);
        model.Filter = ProcessFilter.All;
        model.MinimumMiB = 500;
        check("procesos: consumo mínimo refleja RAM de la muestra actual", model.Rows.Count == 0);
        model.MinimumMiB = 0;
        model.SetSort(ProcessSort.Ram, true);
        model.Selected = model.Rows.Single(r => r.Process?.Pid == child.Pid);
        model.Apply(sample with { Processes = [first, child with { Identity = new ProcessIdentity(child.Pid, 101) }, background, system] });
        check("procesos: PID reutilizado pierde selección y no hereda una acción", model.Selected == null && model.Request(ProcessActionMode.Single) == null);
        model.Apply(sample with { UsesPrivateWorkingSet = false, Processes = [first with { WorkingSetBytes = null, CpuPercent = null }] });
        check("procesos: fallback residente identifica modo y datos no disponibles", model.RamHeading == L10n.tr("RAM residente total")
            && model.Rows.Single(r => r.IsGroup).RamText == L10n.tr("No disponible") && model.Rows.Single(r => r.IsGroup).CpuText == L10n.tr("No disponible"));
        model.Apply(sample with { Processes = [first with { IsCritical = true }] });
        model.Selected = model.Rows.Single(r => r.IsGroup);
        check("procesos: proceso crítico bloquea finalización de aplicación", !model.Selected.CanAct && model.Request(ProcessActionMode.Application) == null);
        model.Apply(sample);
        model.SetSamplingActive(true);
        check("procesos: activar superficie comienza una muestra y temporizador", captureCount == 1 && model.IsSampling);
        model.Paused = true;
        check("procesos: pausa detiene temporizador", !model.IsSampling);
        model.Paused = false;
        model.SetSamplingActive(false);
        check("procesos: superficie oculta detiene muestreo", !model.IsSampling);

        int actions = 0;
        Task<ProcessActionResult> Execute(ProcessActionRequest request, bool elevated)
        {
            actions++;
            return Task.FromResult(new ProcessActionResult(request.Id, ProcessActionOutcome.Success, now, now, []));
        }
        static void Pump(Func<bool> done)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            var poll = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            var started = DateTime.UtcNow;
            poll.Tick += (_, _) => { if (done() || DateTime.UtcNow - started > TimeSpan.FromSeconds(4)) { poll.Stop(); frame.Continue = false; } };
            poll.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        foreach (var language in new[] { Language.Es, Language.En })
        {
            L10n.Init(language);
            actions = 0;
            var prefs = new Prefs();
            prefs.LoadDemo();
            var ollama = new OllamaController();
            ollama.LoadDemo();
            var supervisor = new Supervisor(ollama, prefs);
            supervisor.LoadDemo();
            var panel = new PanelViewModel(supervisor, PanelView.Activity, Capture);
            panel.WideLayout = true;
            panel.BodyMaxHeight = double.PositiveInfinity;
            panel.CurrentActivityTab = PanelViewModel.ActivityTab.Processes;
            var content = new ContentView(panel);
            var window = new Window { Content = content, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000, Width = 700, Height = 900 };
            window.Show();
            panel.Procs.Apply(sample);
            window.UpdateLayout();
            var list = Descendants(content).OfType<ListBox>().Single(l => l.DataContext == panel.Procs);
            check($"{language}: los procesos viven en la pestaña de Actividad, dentro del notch (sin ventana aparte)", list.IsVisible && list.Items.Count == 3
                && !Application.Current.Windows.OfType<Window>().Any(w => w != window && w.Title == L10n.tr("Reconocimiento de procesos")));
            check($"{language}: filtros y orden muestran etiquetas en el tema de la app", Descendants(content).OfType<TextBlock>().Any(t => t.Text == L10n.tr("Todos"))
                && Descendants(content).OfType<TextBlock>().Any(t => t.Text.StartsWith(L10n.tr("RAM") + " ", StringComparison.Ordinal))
                && !Descendants(content).OfType<TextBlock>().Any(t => t.Text.StartsWith("Option {", StringComparison.Ordinal)));
            check($"{language}: mostrar la pestaña no ejecuta acciones ni inicia muestreo en la demo", actions == 0 && !panel.Procs.IsSampling && !panel.ProcessSamplingActive);
            panel.Procs.Selected = panel.Procs.Rows[0];
            window.UpdateLayout();
            var buttons = Descendants(content).OfType<Button>().ToArray();
            check($"{language}: la acción de la selección es visible, accesible y está activa",
                buttons.Where(b => b.IsVisible && b.Content is string t && t == L10n.tr("Finalizar aplicación")).Count(b => b.Focusable && b.IsEnabled) == 1);
            check($"{language}: los botones de iconos de la pestaña tienen nombre", buttons.Where(b => b.IsVisible && b.Content is not string)
                .All(b => !string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(b)) || b.Content is TextBlock { Text.Length: > 0 }));
            string? previewDirectory = Environment.GetEnvironmentVariable("IST_PROCESS_PREVIEW_DIR");
            if (!string.IsNullOrEmpty(previewDirectory)) Export(content, 640, content.ActualHeight, 96, Path.Combine(previewDirectory, $"processes-{language}-wide.png"));
            var many = Enumerable.Range(0, 800).Select(i => Make(10000 + i, $@"C:\Tools\app{i / 4}.exe", (50 + i) * mib)).ToArray();
            panel.Procs.Apply(new ProcessSample(now, sample.TotalRamBytes, true, many));
            window.UpdateLayout();
            check($"{language}: cientos de procesos mantienen las filas virtualizadas", panel.Procs.Rows.Count == 200
                && Descendants(list).OfType<ListBoxItem>().Count() < 30);

            // Narrow (340): la misma pestaña, sin recortes horizontales.
            panel.WideLayout = false;
            panel.Procs.Apply(sample);
            window.UpdateLayout();
            check($"{language}: en estrecho la pestaña cabe en 340 sin desbordar", content.ActualWidth == 340 && list.ActualWidth > 200 && list.ActualWidth <= 340);
            window.Close();

            // Confirmación dentro del notch: hasta aceptarla no se finaliza nada.
            actions = 0;
            var request = new ProcessActionRequest(Guid.NewGuid(), ProcessActionMode.Application, [first.Identity]);
            using var flow = new ProcessViewModel(Capture, persistSettings: false, execute: Execute,
                preview: r => Task.FromResult(new ProcessActionPreview(r, [first, child], 800 * mib, true)));
            flow.Apply(sample);
            flow.Selected = flow.Rows[0];
            flow.ActApplication.Execute(null);
            Pump(() => flow.HasConfirmation);
            var card = flow.Confirmation;
            check($"{language}: finalizar pide confirmación con alcance, todos los PIDs y memoria, y no ejecuta nada antes",
                actions == 0 && card is { HasRows: true } && card.Rows.Count == 2 && card.Rows[0].Detail.Contains("201") && card.Rows[1].Detail.Contains("202")
                && card.Summary.Contains("800.0 MiB") && card.Warning == L10n.tr("El cierre es forzado. Puedes perder trabajo sin guardar.") && !flow.CanApplication);
            flow.Cancel.Execute(null);
            Pump(() => !flow.Operating);
            check($"{language}: cancelar no finaliza nada y deja las acciones listas otra vez", actions == 0 && !flow.HasConfirmation && flow.CanApplication && !flow.HasResult);
            flow.ActApplication.Execute(null);
            Pump(() => flow.HasConfirmation);
            flow.Confirm.Execute(null);
            Pump(() => actions == 1 && !flow.Operating);
            check($"{language}: aceptar finaliza una vez y cuenta lo completado", actions == 1 && !flow.HasConfirmation && flow.HasResult && flow.ResultText.Length > 0);
            string text = ProcessConfirmation.Text(new ProcessActionPreview(request, [first, child], 800 * mib, true));
            check($"{language}: el texto de la confirmación incluye alcance, PIDs y memoria", text.Contains("201") && text.Contains("202") && text.Contains("800.0 MiB"));
        }
    }

    private static void Export(FrameworkElement content, double width, double height, double dpi, string path)
    {
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        var image = new RenderTargetBitmap((int)Math.Ceiling(width * dpi / 96), (int)Math.Ceiling(height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        image.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var output = File.Create(path);
        encoder.Save(output);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
