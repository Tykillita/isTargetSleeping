using Microsoft.Win32;

namespace IsTargetSleeping.UI;

/// Las ventanas del notch. En el modo «seguir al cursor» (el de siempre) hay una sola, que cambia de monitor con el ratón; con pantallas elegidas hay una
/// por monitor, fijada a él. La primera aloja la vista compartida del panel; las demás crean la suya (una vista no puede estar en dos ventanas).
internal sealed class NotchHost
{
    private readonly PanelViewModel model;
    private readonly Prefs prefs;
    private readonly Func<System.Windows.FrameworkElement> sharedView;
    private readonly List<NotchWindow> windows = [];
    private string signature = "";
    private bool started;

    public NotchHost(PanelViewModel model, Prefs prefs, Func<System.Windows.FrameworkElement> sharedView)
    {
        this.model = model;
        this.prefs = prefs;
        this.sharedView = sharedView;
    }

    /// Los notch que hay ahora (para quien necesite hablarles: la mascota, la bandeja).
    public IReadOnlyList<NotchWindow> Windows => windows;

    public void Start()
    {
        started = true;
        SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
        Reconcile(force: true);
    }

    public void Stop()
    {
        started = false;
        SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
        CloseAll();
    }

    private void OnDisplaysChanged(object? sender, EventArgs e) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Reconcile(force: true));

    /// Pone las ventanas según Ajustes y los monitores conectados; si no cambió nada, no toca nada.
    public void Reconcile(bool force = false)
    {
        if (!started) return;
        var screens = Screens.List();
        // Por defecto el notch vive en la pantalla principal (como el de Nusa): con «seguir al cursor» solo estaba en la pantalla que el ratón tocó por última vez.
        var mode = prefs.StoredList(PrefKeys.NotchScreenMode)?.FirstOrDefault() ?? NotchScreens.Selected;
        var selection = NotchScreens.Select(screens, mode, prefs.StoredList(PrefKeys.NotchScreens));
        var now = NotchScreens.Signature(selection) + "#" + string.Join(",", screens.Select(s => s.Id + s.Bounds.Left + "," + s.Bounds.Top));
        if (!force && now == signature && windows.Count > 0) return;
        signature = now;
        CloseAll();
        var ids = selection is null ? [(string?)null] : selection.Select(s => (string?)s.Id).ToList();
        foreach (var id in ids)
        {
            var viewFactory = windows.Count == 0 ? sharedView : () => new ContentView(model);
            var window = new NotchWindow(model, viewFactory, id);
            window.Pinned += OnPinned;
            windows.Add(window);
            window.Start();
        }
    }

    private void CloseAll()
    {
        foreach (var window in windows)
        {
            window.Pinned -= OnPinned;
            window.Stop();
            window.Close();
        }
        windows.Clear();
    }

    /// Al fijar uno, los de las otras pantallas vuelven a su pastilla (el panel es uno solo).
    private void OnPinned(NotchWindow pinned)
    {
        foreach (var other in windows)
            if (!ReferenceEquals(other, pinned)) other.Collapse();
    }

    /// El notch de la pantalla donde está el cursor (o el primero).
    private NotchWindow? Nearest()
    {
        if (windows.Count == 0) return null;
        if (Win32.GetCursorPos(out var cursor) && Win32.MonitorFromPoint(cursor, 2) is var monitor && monitor != IntPtr.Zero)
            return windows.FirstOrDefault(w => w.MonitorHandle == monitor) ?? windows[0];
        return windows[0];
    }

    public void Reveal() => Nearest()?.Reveal();

    public void Toggle() => Nearest()?.Toggle();
}
