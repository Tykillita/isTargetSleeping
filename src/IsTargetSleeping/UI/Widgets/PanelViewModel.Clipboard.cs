using System.Windows;
using System.Windows.Input;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// Una fila del historial del portapapeles: volver a copiarla o quitarla.
public sealed record ClipRow(string Preview, string Detail, ICommand Copy, ICommand Remove, bool ShowDivider)
{
    public bool HasDetail => Detail.Length > 0;
    public string CopyHelp => tr("Copiar «%@» otra vez", Preview);
    public string RemoveHelp => tr("Quitar «%@» del historial", Preview);
}

/// El historial del portapapeles del notch: apagado por defecto, solo texto y solo en memoria (nada se escribe en disco).
public sealed partial class PanelViewModel
{
    public ClipboardHistory Clip { get; } = new();
    public List<ClipRow> ClipRows { get; private set; } = [];
    private ClipboardWatcher? clipWatcher;
    private bool clipPaused;
    private string clipSignature = "";

    public bool ClipboardOn => Prefs.Switch(PrefKeys.ClipboardHistory, false);
    public bool ShowingClipboard => view == PanelView.Clipboard;
    public bool HasClipItems => Clip.Count > 0;
    public bool NoClipItems => Clip.Count == 0;
    public string ClipboardTitle => tr("Historial del portapapeles");
    public string ClipboardDetail => tr("Guarda los últimos %d textos que copias, solo en la memoria de esta app: no escribe nada en disco, no guarda imágenes ni archivos, ignora lo que marcan los gestores de contraseñas y se vacía al cerrar la app.", ClipboardHistory.Max);
    public string ClipEmpty => tr("Aún no has copiado ningún texto. Lo que copies aparecerá aquí.");
    public string ClipPauseLabel => clipPaused ? tr("Reanudar") : tr("Pausar");
    public string ClipPauseHelp => clipPaused ? tr("Vuelve a guardar lo que copies") : tr("Deja de guardar lo que copies, sin borrar lo que ya hay");
    public string ClipClearLabel => tr("Vaciar");
    public string ClipClearHelp => tr("Quita todo del historial (no toca el portapapeles actual)");
    public string NotchTabClipboard => tr("Portapapeles");
    public ICommand ToggleClipboard { get; private set; } = null!;
    public ICommand PauseClip { get; private set; } = null!;
    public ICommand ClearClip { get; private set; } = null!;

    private void InitClipboard()
    {
        // Al apagarlo se borra todo: lo que se copió antes no queda esperando a que se vuelva a encender.
        ToggleClipboard = new Command(() => Prefs.Toggle(PrefKeys.ClipboardHistory, false));
        PauseClip = new Command(() => { clipPaused = !clipPaused; Notify(); });
        ClearClip = new Command(() => { Clip.Clear(); Notify(); });
    }

    private void RebuildClipboard()
    {
        bool on = ClipboardOn;
        if (on && clipWatcher is null)
        {
            clipWatcher = new ClipboardWatcher();
            clipWatcher.TextCopied += text => { if (!clipPaused && Clip.Add(text)) Notify(); };
        }
        else if (!on && clipWatcher is not null)
        {
            clipWatcher.Dispose();
            clipWatcher = null;
            Clip.Clear();
            clipPaused = false;
            if (view == PanelView.Clipboard) view = BackView;
        }
        var signature = string.Join("\u0001", Clip.Items) + "#" + Effective;
        if (signature == clipSignature) return;
        clipSignature = signature;
        ClipRows = Clip.Items.Select((text, i) =>
        {
            var copy = text;
            return new ClipRow(ClipboardHistory.Preview(text), text.Length > 90 || text.Contains('\n') ? tr("%d caracteres", text.Length) : "",
                new Command(() => { try { Clipboard.SetText(copy); } catch (Exception e) { AppLog.Write($"portapapeles: {e.Message}"); } }),
                new Command(() => { Clip.RemoveAt(Clip.Items.ToList().IndexOf(copy)); Notify(); }),
                i > 0);
        }).ToList();
    }
}
