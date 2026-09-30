using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using IsTargetSleeping.UI;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping;

/// Vive en la bandeja del sistema, no en la barra de tareas: el ícono con su
/// estado, el panel, el menú del clic derecho y el atajo global.
public partial class App : Application
{
    private OllamaController ollama = null!;
    private Supervisor supervisor = null!;
    private Prefs prefs = null!;
    private TrayIcon tray = null!;
    private PanelWindow panel = null!;
    private PanelViewModel model = null!;
    private DispatcherTimer? blink;
    private bool blinkOn = true;
    private Power blinkPower;
    private (Mark.Dot Dot, bool Dimmed, string Tip, bool Percent) painted;

    public void StartTray(bool launchedAtLogin, string? pending = null)
    {
        Theme.Apply();
        ollama = new OllamaController(new StatsStore(Paths.StatsFile));
        prefs = new Prefs();
        supervisor = new Supervisor(ollama, prefs);

        model = new PanelViewModel(supervisor);
        panel = new PanelWindow(new ContentView(model));

        tray = new TrayIcon();
        tray.Click += TogglePanel;
        tray.RightClick += ShowMenu;
        tray.HotKeyPressed += key =>
        {
            if (key == HotKey.Sleep) supervisor.Sleep();
            else if (key == HotKey.Clean) _ = supervisor.Clean(CleanReason.Manual);
            else ollama.Toggle();
        };
        tray.NotificationClicked += () => ShowPanel();
        tray.CommandReceived += supervisor.Execute;
        tray.AppearanceChanged += OnAppearanceChanged;
        HotKey.Attach(tray.Handle);

        var notifier = new Notifier(tray, prefs);
        supervisor.Notify += notifier.Show;
        supervisor.ShowPanel += activity => ShowPanel(activity ? PanelView.Activity : null);
        supervisor.Changed += Repaint;

        prefs.OnHotKeyChange = ApplyHotKeys;
        prefs.OpenLog = OpenLog;
        ApplyHotKeys(prefs.HotKeyEnabled);
        Links.EnsureRegistered();

        ollama.PowerChanged += _ => Repaint();
        ollama.Changed += Repaint;   // el % de RAM del tooltip (y del ícono, si se eligió)
        Repaint();
        ollama.StartPolling();
        supervisor.Start();

        if (pending is not null)
        {
            // Lanzada por un enlace: se atiende cuando ya se sabe el estado de Ollama.
            var later = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            later.Tick += (_, _) => { later.Stop(); supervisor.Execute(pending); };
            later.Start();
        }
        // Primera vez: se abre el panel para que se vea dónde vive y qué hace.
        else if (prefs.ShouldOfferLogin && !launchedAtLogin)
        {
            var once = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.8) };
            once.Tick += (_, _) => { once.Stop(); ShowPanel(); };
            once.Start();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        HotKey.Power.Unregister();
        HotKey.Sleep.Unregister();
        HotKey.Clean.Unregister();
        ollama?.Stats.Flush();
        tray?.Dispose();
        base.OnExit(e);
    }

    private void ApplyHotKeys(bool on)
    {
        var failed = new List<string>();
        foreach (var (key, wanted) in new[] { (HotKey.Power, on), (HotKey.Sleep, prefs.SleepHotKeyEnabled), (HotKey.Clean, prefs.Switch(PrefKeys.CleanHotKey)) })
        {
            if (!wanted) key.Unregister();
            else if (!key.Register()) failed.Add(key.Display);
        }
        prefs.HotKeyError = failed.Count == 0 ? null
            : tr("No se pudo activar el atajo %@. Puede que otra app ya lo use.", string.Join(", ", failed));
    }

    /// El panel es siempre vidrio negro; lo que cambia con el sistema es el ícono de
    /// la bandeja (barra de tareas clara u oscura, DPI).
    private void OnAppearanceChanged()
    {
        painted = default;
        Repaint();
    }

    // MARK: - ícono

    /// Parpadea mientras Ollama cambia de estado y lleva el punto verde si está encendido.
    private void Repaint()
    {
        if (blink is not null && ollama.Power == blinkPower) return;   // ya parpadea
        blink?.Stop();
        blink = null;
        blinkPower = ollama.Power;
        switch (ollama.Power)
        {
            case Power.On:
                Paint(Mark.Dot.On, false, supervisor.Game.Active ? tr("Jugando a %@ · Ollama encendido", supervisor.Game.Game ?? "") : tr("Ollama encendido"));
                break;
            case Power.Off:
                Paint(Mark.Dot.None, true, supervisor.Game.Active ? tr("Jugando a %@ · Ollama apagado", supervisor.Game.Game ?? "") : tr("Ollama apagado"));
                break;
            case Power.Missing:
                Paint(Mark.Dot.None, true, tr("Ollama no está instalado"));
                break;
            default:
                var tip = ollama.Power == Power.Starting ? tr("Ollama arrancando…") : tr("Ollama apagándose…");
                blinkOn = true;
                Paint(Mark.Dot.Busy, false, tip);
                blink = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.5) };
                blink.Tick += (_, _) =>
                {
                    blinkOn = !blinkOn;
                    Paint(blinkOn ? Mark.Dot.Busy : Mark.Dot.None, !blinkOn, tip);
                };
                blink.Start();
                break;
        }
    }

    /// El tooltip lleva siempre el % de RAM; con «Ícono con el % de RAM» el ícono
    /// también (como Mem Reduct), en ámbar o coral según la presión.
    private void Paint(Mark.Dot dot, bool dimmed, string tip)
    {
        var mem = ollama.Memory;
        int percent = mem.Total > 0 ? (int)Math.Round(100.0 * mem.Used / mem.Total) : 0;
        tip = $"{tip} · RAM {percent} %";
        bool showPercent = prefs.Switch(PrefKeys.TrayPercent, false);
        if (painted == (dot, dimmed, tip, showPercent)) return;
        painted = (dot, dimmed, tip, showPercent);
        var icon = showPercent
            ? Mark.PercentBitmap(tray.IconSize, percent, mem.Pressure, dot, Theme.TaskbarIsLight())
            : Mark.StatusBitmap(tray.IconSize, dot, dimmed, Theme.TaskbarIsLight());
        tray.Update(icon, $"{AppInfo.Name} · {tip}");
    }

    // MARK: - panel

    public void ShowPanel(PanelView? view = null)
    {
        _ = ollama.Refresh();
        if (view is { } v) model.Show(v);
        panel.ShowAt(tray.Rect());
    }

    private void TogglePanel()
    {
        if (panel.IsVisible) { panel.HidePanel(); return; }
        // El clic en el ícono ya cerró el panel al quitarle el foco: no se reabre.
        if ((DateTime.Now - panel.LastHidden).TotalMilliseconds < 350) return;
        ShowPanel();
    }

    // MARK: - menú del clic derecho

    private void ShowMenu()
    {
        panel.HidePanel();
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };

        if (ollama.Power == Power.Missing)
        {
            Add(menu, tr("Descargar Ollama…"), ollama.Toggle);
        }
        else
        {
            var power = Add(menu, ollama.Power == Power.On ? tr("Apagar Ollama") : tr("Encender Ollama"), ollama.Toggle);
            power.IsEnabled = !ollama.Power.IsTransition();
            if (prefs.HotKeyEnabled) power.InputGestureText = HotKey.Power.Display;
            var sleep = Add(menu, tr("Dormir el modelo"), supervisor.Sleep);
            sleep.IsEnabled = ollama.Loaded.Count > 0;
            if (prefs.SleepHotKeyEnabled) sleep.InputGestureText = HotKey.Sleep.Display;
        }
        var clean = Add(menu, tr("Liberar RAM"), () => _ = supervisor.Clean(CleanReason.Manual));
        clean.IsEnabled = supervisor.Agent == AgentStatus.Ready && !supervisor.Cleaning;
        if (prefs.Switch(PrefKeys.CleanHotKey)) clean.InputGestureText = HotKey.Clean.Display;
        Add(menu, tr("Actividad"), () => ShowPanel(PanelView.Activity));

        menu.Items.Add(new Separator());
        var logs = Add(menu, tr("Ver el log de Ollama"), OpenLog);
        logs.IsEnabled = ollama.Backend.LogPath is { } log && File.Exists(log);
        var login = Add(menu, tr("Abrir al iniciar sesión"), () =>
        {
            try { prefs.ToggleLogin(); } catch (Exception e) { ollama.Error = tr("Inicio de sesión: %@", e.Message); }
        });
        login.IsChecked = prefs.OpensAtLogin;

        menu.Items.Add(new Separator());
        Add(menu, tr("Acerca de %@", AppInfo.Name), AboutWindow.Present);
        menu.Items.Add(new Separator());
        Add(menu, tr("Salir"), Shutdown);

        menu.Opened += (_, _) =>
        {
            // Sin esto el menú no se cierra al hacer clic fuera: la bandeja no es una ventana de la app.
            if (PresentationSource.FromVisual(menu) is HwndSource source) Win32.SetForegroundWindow(source.Handle);
        };
        menu.IsOpen = true;
    }

    private static MenuItem Add(ContextMenu menu, string title, Action action)
    {
        var item = new MenuItem { Header = title };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
        return item;
    }

    private void OpenLog() => Paths.OpenLog(ollama.Backend.LogPath);
}
