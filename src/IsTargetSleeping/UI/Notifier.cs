namespace IsTargetSleeping.UI;

/// Avisos nativos de Windows con un límite: el mismo aviso (tipo y título) como
/// mucho una vez por minuto, para no llenar el centro de notificaciones si algo se
/// repite. «Modo juego» y «Fin del modo juego» son avisos distintos.
public sealed class Notifier(TrayIcon tray, Prefs prefs)
{
    private readonly Dictionary<(Notice, string), DateTime> last = [];

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
            result = tray.Notify(title, text) ? "mostrado" : "Windows lo rechazó";
        }
        AppLog.Write($"aviso {kind}: {title} — {text} [{result}]");
    }
}
