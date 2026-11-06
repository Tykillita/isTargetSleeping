using System.Globalization;
using System.Windows;
using System.Windows.Input;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

public sealed record ClipRow(string Preview, string Detail, bool IsPinned, ICommand Copy, ICommand Pin, ICommand Remove, bool ShowDivider)
{
    public bool HasDetail => Detail.Length > 0;
    public string CopyHelp => tr("Copiar «%@» otra vez", Preview);
    public string PinHelp => IsPinned ? tr("Dejar de fijar «%@»", Preview) : tr("Fijar «%@» en esta sesión", Preview);
    public string RemoveHelp => tr("Quitar «%@» del historial", Preview);
}

/// El historial del portapapeles del notch: apagado por defecto, solo texto y solo en memoria.
public sealed partial class PanelViewModel
{
    public ClipboardHistory Clip { get; } = new();
    public List<ClipRow> ClipRows { get; private set; } = [];
    private ClipboardWatcher? clipWatcher;
    private bool clipPaused, clipPinnedOnly;
    private string clipSignature = "", clipSearch = "", clipNotice = "";

    public bool ClipboardOn => Prefs.Switch(PrefKeys.ClipboardHistory, false);
    public bool ShowingClipboard => view == PanelView.Clipboard;
    public bool HasClipItems => Clip.Count > 0;
    public bool NoClipItems => Clip.Count == 0;
    public bool ClipHasNoMatches => Clip.Count > 0 && ClipRows.Count == 0;
    public bool ClipHasSearch => clipSearch.Length > 0;
    public bool ClipHasNotice => clipNotice.Length > 0;
    public string ClipboardTitle => tr("Copias recientes");
    public string ClipboardDetail => tr("Guarda hasta %d textos en esta sesión. No almacena imágenes ni archivos, ignora lo marcado por gestores de contraseñas y se borra al cerrar la app.", ClipboardHistory.Max);
    public string ClipEmpty => tr("Copia un texto y aparecerá aquí. El historial es privado y temporal.");
    public string ClipNoMatches => clipPinnedOnly && !ClipHasSearch ? tr("Aún no has fijado textos en esta sesión.") : tr("No hay textos que coincidan con la búsqueda.");
    public string ClipState => clipPaused ? tr("Pausado") : tr("Capturando texto");
    public string ClipCountLabel => tr("%d textos · %d fijados", Clip.Count, Clip.PinnedCount);
    public string ClipNotice => clipNotice;
    public string ClipSearch { get => clipSearch; set { if (clipSearch == value) return; clipSearch = value ?? ""; Notify(); } }
    public bool ClipPinnedOnly => clipPinnedOnly;
    public string ClipFilterLabel => tr("Filtrar historial");
    public string ClipSearchLabel => tr("Buscar en el historial del portapapeles");
    public string ClipSearchHint => tr("Buscar texto copiado");
    public List<Segment> ClipFilterSegments => [
        new(tr("Todos"), !clipPinnedOnly, new Command(() => { clipPinnedOnly = false; Notify(); })),
        new(tr("Fijados"), clipPinnedOnly, new Command(() => { clipPinnedOnly = true; Notify(); }))
    ];
    public string ClipPauseLabel => clipPaused ? tr("Reanudar") : tr("Pausar");
    public string ClipPauseHelp => clipPaused ? tr("Vuelve a guardar lo que copies") : tr("Deja de guardar lo que copies, sin borrar lo que ya hay");
    public string ClipClearLabel => tr("Vaciar historial");
    public string ClipClearHelp => tr("Quita todo del historial (no toca el portapapeles actual)");
    public string ClipClearSearchLabel => tr("Limpiar búsqueda");
    public string NotchTabClipboard => tr("Portapapeles");
    public ICommand ToggleClipboard { get; private set; } = null!;
    public ICommand PauseClip { get; private set; } = null!;
    public ICommand ClearClip { get; private set; } = null!;
    public ICommand ClearClipSearch { get; private set; } = null!;

    private void InitClipboard()
    {
        ToggleClipboard = new Command(() => Prefs.Toggle(PrefKeys.ClipboardHistory, false));
        PauseClip = new Command(() => { clipPaused = !clipPaused; clipNotice = ""; Notify(); });
        ClearClip = new Command(() => { Clip.Clear(); clipPinnedOnly = false; clipSearch = clipNotice = ""; Notify(); });
        ClearClipSearch = new Command(() => ClipSearch = "");
    }

    private void RebuildClipboard()
    {
        bool on = ClipboardOn;
        if (on && clipWatcher is null)
        {
            clipWatcher = new ClipboardWatcher();
            clipWatcher.TextCopied += text =>
            {
                if (clipPaused) return;
                if (Clip.Add(text)) { clipNotice = ""; Notify(); }
            };
        }
        else if (!on && clipWatcher is not null)
        {
            clipWatcher.Dispose();
            clipWatcher = null;
            Clip.Clear();
            clipPaused = clipPinnedOnly = false;
            clipSearch = clipNotice = "";
            if (view == PanelView.Clipboard) view = BackView;
        }

        var signature = $"{Clip.Revision}#{clipSearch}#{clipPinnedOnly}#{Effective}";
        if (signature == clipSignature) return;
        clipSignature = signature;
        var entries = Clip.Entries.AsEnumerable();
        if (clipPinnedOnly) entries = entries.Where(e => e.IsPinned);
        if (!string.IsNullOrWhiteSpace(clipSearch)) entries = entries.Where(e => e.Text.Contains(clipSearch.Trim(), StringComparison.OrdinalIgnoreCase));
        var visible = entries.ToList();
        var culture = CultureInfo.GetCultureInfo(Effective == "es" ? "es-ES" : "en-US");
        ClipRows = visible.Select((entry, i) =>
        {
            var text = entry.Text;
            var count = entry.WasTruncated
                ? tr("%d de %d caracteres · recortado", text.Length, entry.OriginalLength)
                : tr("%d caracteres", text.Length);
            var detail = $"{entry.CopiedAt.ToLocalTime().ToString("t", culture)} · {count}";
            return new ClipRow(ClipboardHistory.Preview(text), detail, entry.IsPinned,
                new Command(() =>
                {
                    try { Clipboard.SetText(text); clipNotice = tr("Texto copiado de nuevo"); }
                    catch (Exception e) { AppLog.Write($"portapapeles: {e.Message}"); clipNotice = tr("No se pudo copiar el texto"); }
                    Notify();
                }),
                new Command(() =>
                {
                    if (!Clip.SetPinned(text, !entry.IsPinned) && !entry.IsPinned)
                        clipNotice = tr("Puedes fijar hasta %d textos", ClipboardHistory.MaxPinned);
                    else clipNotice = "";
                    Notify();
                }),
                new Command(() => { Clip.RemoveAt(Clip.Entries.ToList().FindIndex(e => e.Text == text)); clipNotice = ""; Notify(); }),
                i > 0);
        }).ToList();
    }
}
