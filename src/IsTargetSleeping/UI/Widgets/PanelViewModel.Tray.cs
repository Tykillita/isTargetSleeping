using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// Una fila de la bandeja de archivos: se puede quitar, enseñar en el Explorador, copiar su ruta y arrastrar hacia fuera.
public sealed record TrayRow(string Name, string Detail, ImageSource? Icon, bool IsFolder, string Path,
    ICommand Remove, ICommand Reveal, ICommand CopyPath, ICommand Edit, bool ShowDivider)
{
    public bool CanEdit => !IsFolder;
    public string EditHelp => tr("Editar «%@»", Name);
    public bool HasIcon => Icon is not null;
    public bool NoIcon => Icon is null;
    public string RemoveHelp => tr("Quitar «%@» de la bandeja", Name);
    public string RevealHelp => tr("Enseñar «%@» en el Explorador", Name);
    public string CopyHelp => tr("Copiar la ruta de «%@»", Name);
}

/// La bandeja de archivos del notch: lo que se arrastra al notch espera aquí (solo la ruta, en memoria).
public sealed partial class PanelViewModel
{
    public FileTray Tray { get; } = new();
    public List<TrayRow> TrayRows { get; private set; } = [];
    private string traySignature = "";
    private string trayStatus = "";
    private string? trayLastZip;

    public bool ShowingTray => view == PanelView.Tray;
    public bool HasTrayFiles => Tray.Count > 0;
    public bool NoTrayFiles => Tray.Count == 0;
    public string TrayEmpty => tr("Arrastra archivos o carpetas al notch para dejarlos aquí. Después los arrastras de vuelta a donde los quieras, y los de texto los puedes editar sin salir del notch.");
    public string TraySummary => Tray.Count == 0 ? "" : tr("%d elementos · %@", Tray.Count, FormatSize(Tray.TotalBytes));
    public string TrayStatus => trayStatus;
    public bool HasTrayStatus => trayStatus.Length > 0;
    public bool HasTrayZip => trayLastZip is not null;
    public string ZipLabel => tr("Comprimir todo");
    public string ZipHelp => tr("Crea un .zip con todo lo de la bandeja en el Escritorio");
    public string ClearTrayLabel => tr("Vaciar");
    public string ClearTrayHelp => tr("Quita todo de la bandeja (no borra ningún archivo)");
    public string RevealZipLabel => tr("Enseñar el .zip");
    public string NotchTabTray => tr("Bandeja");
    public ICommand ZipTray { get; private set; } = null!;
    public ICommand ClearTray { get; private set; } = null!;
    public ICommand RevealZip { get; private set; } = null!;
    /// El notch se abre en la bandeja cuando llegan archivos arrastrados.
    public event Action? TrayFilesAdded;

    private void InitTray()
    {
        ClearTray = new Command(() => { Tray.Clear(); trayStatus = ""; trayLastZip = null; Notify(); });
        RevealZip = new Command(() => { if (trayLastZip is not null) RevealInExplorer(trayLastZip); });
        ZipTray = new Command(async () =>
        {
            if (Tray.Count == 0) return;
            trayStatus = tr("Comprimiendo…");
            Notify();
            try
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                trayLastZip = await Task.Run(() => Tray.ZipTo(desktop, "isTargetSleeping-bandeja"));
                trayStatus = tr("Listo: %@", Path.GetFileName(trayLastZip));
            }
            catch (Exception e)
            {
                AppLog.Write($"bandeja: {e.Message}");
                trayLastZip = null;
                trayStatus = tr("No se pudo comprimir: %@", e.Message);
            }
            Notify();
        });
    }

    /// Lo que se suelta en el notch: se añade (lo que exista y no esté ya) y la bandeja se enseña.
    public void AddToTray(IEnumerable<string> paths)
    {
        if (Tray.Add(paths) == 0) return;
        trayStatus = "";
        Notify();
        TrayFilesAdded?.Invoke();
    }

    private void RebuildTray()
    {
        Tray.Prune();
        var signature = string.Join("|", Tray.Entries.Select(e => e.Path)) + "#" + Effective;
        if (signature == traySignature) return;
        traySignature = signature;
        TrayRows = Tray.Entries.Select((entry, i) =>
        {
            var path = entry.Path;
            return new TrayRow(entry.Name, entry.IsDirectory ? tr("Carpeta") : FormatSize(entry.Size), entry.IsDirectory ? null : AppIcons.For(path), entry.IsDirectory, path,
                new Command(() => { Tray.Remove(path); Notify(); }),
                new Command(() => RevealInExplorer(path)),
                new Command(() => { try { Clipboard.SetText(path); } catch (Exception e) { AppLog.Write($"bandeja: {e.Message}"); } }),
                new Command(() => OpenEditor(path)),
                i > 0);
        }).ToList();
    }

    private static void RevealInExplorer(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); } catch { }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1_048_576 => $"{bytes / 1024.0:0.#} KB",
        < 1_073_741_824 => $"{bytes / 1_048_576.0:0.#} MB",
        _ => $"{bytes / 1_073_741_824.0:0.##} GB",
    };
}
