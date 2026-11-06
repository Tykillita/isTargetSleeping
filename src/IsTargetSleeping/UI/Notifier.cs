namespace IsTargetSleeping.UI;

/// Avisos nativos de Windows con un límite: el mismo aviso (tipo y título) como
/// mucho una vez por minuto, para no llenar el centro de notificaciones si algo se
/// repite. «Modo juego» y «Fin del modo juego» son avisos distintos.
public sealed class Notifier(TrayIcon tray, Prefs prefs)
{
    private readonly Dictionary<(Notice, string), DateTime> last = [];

    /// Se dispara con cada aviso que se muestra (título): el notch cerrado enseña entonces una campana un rato.
    public event Action<string>? Shown;

    public bool IsOn(Notice kind) => prefs.Switch(kind.Key(), kind.DefaultOn());

    public void Show(Notice kind, string title, string text)
    {
        string result;
        var now = DateTime.Now;
        if (!IsOn(kind)) result = "desactivado";
        else if (last.TryGetValue((kind, title), out var at) && now - at < TimeSpan.FromMinutes(1)) result = "limitado (1/min)";
        else
        {
            last[(kind, title)] = now;
            if (kind == Notice.Download) tray.DownloadProgress(null);
            result = tray.Notify(title, text, details: kind == Notice.Sleep) ? "mostrado" : "Windows lo rechazó";
            Shown?.Invoke(title);
        }
        AppLog.Write($"aviso {kind}: {title} — {text} [{result}]");
    }

    public void DownloadProgress(PullJob? job) => tray.DownloadProgress(IsOn(Notice.Download) ? job : null);
}
