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
    private TrayAnimator animator = null!;
    private TaskbarPet? pet;
    public bool EnsureTrayReady() => tray?.EnsureReady() == true;

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
        animator = new TrayAnimator(tray);
        HotKey.Attach(tray.Handle);

        var notifier = new Notifier(tray, prefs);
        supervisor.Notify += notifier.Show;
        supervisor.ShowPanel += activity => ShowPanel(activity ? PanelView.Activity : null);
        supervisor.Changed += Repaint;

        prefs.OnHotKeyChange = ApplyHotKeys;
        prefs.Changed += Repaint;   // ícono con el % o animado
        prefs.OpenLog = OpenLog;
        ApplyHotKeys(prefs.HotKeyEnabled);
        Links.EnsureRegistered();

        ollama.PowerChanged += _ => Repaint();
        ollama.Changed += Repaint;   // el % de RAM del tooltip (y del ícono, si se eligió)
        Repaint();
        ollama.StartPolling();
        supervisor.Start();
        pet = new TaskbarPet(supervisor);

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
        pet?.Dispose();
        tray?.Dispose();
        animator?.Dispose();
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
        animator.Reset();
        Repaint();
    }

    // MARK: - ícono

    /// El estado de Ollama en el ícono: el radar mientras arranca o se apaga, la
    /// insignia azul encendido y el ojo abierto con un modelo en memoria. El tooltip
    /// lleva siempre el % de RAM; con «Ícono con el % de RAM» el ícono también.
    private void Repaint()
    {
        string game = supervisor.Game.Game ?? "";
        var status = ollama.Power switch
        {
            Power.On => supervisor.Game.Active ? tr("Jugando a %@ · Ollama encendido", game) : tr("Ollama encendido"),
            Power.Off => supervisor.Game.Active ? tr("Jugando a %@ · Ollama apagado", game) : tr("Ollama apagado"),
            Power.Missing => tr("Ollama no está instalado"),
            Power.Starting => tr("Ollama arrancando…"),
            _ => tr("Ollama apagándose…"),
        };
        var phase = ollama.Power switch
        {
            Power.Starting => TrayPhase.Starting,
            Power.On => TrayPhase.On,
            Power.Stopping => TrayPhase.Stopping,
            _ => TrayPhase.Off,
        };
        var mem = ollama.Memory;
        int percent = mem.Total > 0 ? (int)Math.Round(100.0 * mem.Used / mem.Total) : 0;
        // Con un modelo en memoria el ojo de la mira se abre.
        bool awake = ollama.Power == Power.On && ollama.Loaded.Count > 0;
        bool animate = !prefs.ReducedMotion;
        animator.Show(phase, awake, $"{AppInfo.Name} · {status} · RAM {percent} %",
            prefs.Switch(PrefKeys.TrayPercent, false), percent, mem.Pressure, animate);
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
            menu.Items.Add(MainModelMenu());
        }
        var clean = Add(menu, tr("Liberar RAM"), () => _ = supervisor.Clean(CleanReason.Manual));
        clean.IsEnabled = supervisor.Agent == AgentStatus.Ready && !supervisor.Cleaning;
        if (prefs.Switch(PrefKeys.CleanHotKey)) clean.InputGestureText = HotKey.Clean.Display;
        var percent = Add(menu, tr("Mostrar el % de RAM en el ícono"), () =>
        {
            prefs.Toggle(PrefKeys.TrayPercent, false);
            Repaint();
        });
        percent.IsChecked = prefs.Switch(PrefKeys.TrayPercent, false);
        var still = Add(menu, tr("Movimiento reducido"), prefs.ToggleReducedMotion);   // Prefs.Changed repinta
        still.IsChecked = prefs.ReducedMotion;
        menu.Items.Add(PetMenu());
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
        var checkUpdates = Add(menu, tr("Buscar actualizaciones ahora"), () =>
        {
            ShowPanel(PanelView.Settings);
            _ = supervisor.CheckUpdates(manual: true);
        });
        checkUpdates.IsEnabled = supervisor.UpdateStatus != UpdateStatus.Checking && !supervisor.UpdateBusy;
        menu.Items.Add(new Separator());
        Add(menu, tr("Salir"), Shutdown);

        menu.Opened += (_, _) =>
        {
            // Sin esto el menú no se cierra al hacer clic fuera: la bandeja no es una ventana de la app.
            if (PresentationSource.FromVisual(menu) is HwndSource source) Win32.SetForegroundWindow(source.Handle);
        };
        menu.IsOpen = true;
    }

    /// «Mascota junto a Inicio»: mostrarla, dónde vive, si se puede tocar y [cuál].
    private MenuItem PetMenu()
    {
        var root = new MenuItem { Header = tr("Mascota junto a Inicio") };
        var show = new MenuItem { Header = tr("Mostrar"), IsChecked = prefs.Switch(PrefKeys.TaskbarPet, false) };
        show.Click += (_, _) => prefs.Toggle(PrefKeys.TaskbarPet, false);
        root.Items.Add(show);
        root.Items.Add(new Separator());
        root.Items.Add(TaskbarPet.PlacementMenu(prefs));
        if (TaskbarPet.SpeciesMenu(prefs) is { } pets) root.Items.Add(pets);
        var touch = new MenuItem { Header = tr("Interactuar con la mascota"), IsChecked = prefs.Switch(PrefKeys.TaskbarPetInteractive) };
        touch.Click += (_, _) => prefs.Toggle(PrefKeys.TaskbarPetInteractive);
        root.Items.Add(touch);
        return root;
    }

    /// «Cargar al encender»: el modelo principal (el de la estrella del panel), o ninguno.
    /// Con Ollama apagado, los modelos salen de sus manifiestos en disco.
    private MenuItem MainModelMenu()
    {
        var main = prefs.MainModel;
        var root = new MenuItem { Header = tr("Cargar al encender") };
        void Option(string title, string? model)
        {
            var item = new MenuItem { Header = title, IsChecked = model == main };
            item.Click += (_, _) => prefs.SetMainModel(model);
            root.Items.Add(item);
        }
        Option(tr("Ninguno"), null);
        var names = ollama.Installed.Count > 0 ? ollama.Installed.Select(m => m.Name).ToList() : OllamaBlobs.Installed();
        if (main is not null && !names.Contains(main)) names.Insert(0, main);
        root.Items.Add(new Separator());
        foreach (var name in names) Option(name, name);
        if (names.Count == 0)
            root.Items.Add(new MenuItem { Header = tr("No hay modelos instalados"), IsEnabled = false });
        return root;
    }

    public void ShowUpdateRecovery(string message)
    {
        supervisor.ReportUpdateRecovery(message);
        ShowPanel(PanelView.Settings);
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
