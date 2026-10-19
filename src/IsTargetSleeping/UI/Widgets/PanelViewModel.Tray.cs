using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// Una fila de la bandeja de archivos: se abre (doble clic), se edita o se abre como proyecto, se enseña en el Explorador,
/// se copia su ruta, se quita y se arrastra hacia fuera.
public sealed record TrayRow(string Name, string Detail, ImageSource? Icon, bool IsFolder, string Path,
    ICommand Remove, ICommand Reveal, ICommand CopyPath, ICommand Edit, ICommand Open, ICommand OpenProject, ICommand ReadText)
{
    public bool CanEdit => !IsFolder;
    public bool CanReadText => !IsFolder && CodeTree.IsImage(Path);
    public string ReadTextHelp => tr("Copiar texto de %@", Name);
    public string EditHelp => tr("Editar «%@»", Name);
    public string OpenHelp => tr("Abrir «%@»", Name);
    public string ProjectHelp => tr("Abrir «%@» como proyecto en Código", Name);
    public bool HasIcon => Icon is not null;
    public bool NoIcon => Icon is null;
    public string RemoveHelp => tr("Quitar «%@» de la bandeja", Name);
    public string RevealHelp => tr("Enseñar «%@» en el Explorador", Name);
    public string CopyHelp => tr("Copiar la ruta de «%@»", Name);
}

/// La bandeja de archivos del notch: lo que se arrastra al notch espera aquí (solo la ruta; en memoria, o recordada entre sesiones si el usuario lo pide).
public sealed partial class PanelViewModel
{
    public FileTray Tray { get; } = new();
    public List<TrayRow> TrayRows { get; private set; } = [];
    private string traySignature = "";
    private string trayStatus = "";
    private string? trayLastZip;
    private readonly Dictionary<string, FolderStats> trayStats = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> trayMeasuring = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ImageSource?> trayThumbnails = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherTimer? trayDragTimer;
    private bool trayDragging;

    public bool ShowingTray => view == PanelView.Tray;
    public bool HasTrayFiles => Tray.Count > 0;
    public bool NoTrayFiles => Tray.Count == 0;
    public bool HasManyTrayFiles => Tray.Count > 1;
    public string TrayEmpty => tr("Arrastra archivos o carpetas al notch para dejarlos aquí. Después los arrastras de vuelta a donde los quieras, y los de texto los puedes editar sin salir del notch.");
    public string TrayHint => tr("Doble clic abre un elemento; arrastra una fila para llevártelo a otro sitio.");
    public string TrayDropText => tr("Suelta para añadir a la bandeja");
    public bool TrayDragging => trayDragging;

    private static string Items(int count) => count == 1 ? tr("1 elemento") : tr("%d elementos", count);

    /// «3 elementos · 1.2 MB»; mientras se miden las carpetas, «calculando…» en lugar del tamaño.
    public string TraySummary
    {
        get
        {
            if (Tray.Count == 0) return "";
            bool measuring = Tray.Entries.Any(e => e.IsDirectory && !trayStats.ContainsKey(e.Path));
            long bytes = Tray.TotalBytes + Tray.Entries.Where(e => e.IsDirectory && trayStats.ContainsKey(e.Path)).Sum(e => trayStats[e.Path].Bytes);
            return $"{Items(Tray.Count)} · {(measuring ? tr("calculando…") : FormatSize(bytes))}";
        }
    }

    public string TrayStatus => trayStatus;
    public bool HasTrayStatus => trayStatus.Length > 0;
    public bool HasTrayZip => trayLastZip is not null;
    public string ZipLabel => tr("Comprimir todo");
    public string ZipHelp => tr("Crea un .zip con todo lo de la bandeja en el Escritorio");
    public string ClearTrayLabel => tr("Vaciar");
    public string ClearTrayHelp => tr("Quita todo de la bandeja (no borra ningún archivo)");
    public string RevealZipLabel => tr("Enseñar el .zip");
    public string PasteTrayLabel => tr("Pegar");
    public string PasteTrayHelp => tr("Añade a la bandeja los archivos que hayas copiado (Ctrl+C en el Explorador)");
    public string DragAllLabel => tr("Arrastrar todo");
    public string DragAllHelp => tr("Arrastra desde aquí todos los elementos de la bandeja para llevarlos a otro sitio");
    /// Las rutas de todo lo que hay en la bandeja, separadas por «|» (un carácter que no puede estar en una ruta de Windows).
    public string TrayAllPaths => string.Join(FileDrag.Separator, Tray.Entries.Select(e => e.Path));
    public string NotchTabTray => tr("Bandeja");
    public ICommand ZipTray { get; private set; } = null!;
    public ICommand ClearTray { get; private set; } = null!;
    public ICommand RevealZip { get; private set; } = null!;
    public ICommand PasteTray { get; private set; } = null!;
    /// El notch se abre en la bandeja cuando llegan archivos arrastrados.
    public event Action? TrayFilesAdded;

    // MARK: recordar la bandeja entre sesiones (apagado por defecto: solo las rutas, nunca copias)
    public bool TrayRememberOn => Prefs.Switch(PrefKeys.TrayRemember, false);
    public string TrayRememberTitle => tr("Recordar la bandeja al cerrar la app");
    public string TrayRememberDetail => tr("Guarda solo las rutas de lo que hay en la bandeja (nunca copias) y las recupera al abrir la app. Apagado, la bandeja se vacía al cerrar.");
    public ICommand ToggleTrayRemember { get; private set; } = null!;

    private void InitTray()
    {
        ClearTray = new Command(() => { Tray.Clear(); trayStatus = ""; trayLastZip = null; Notify(); });
        RevealZip = new Command(() => { if (trayLastZip is not null) RevealInExplorer(trayLastZip); });
        PasteTray = new Command(PasteFiles);
        ToggleTrayRemember = new Command(() =>
        {
            Prefs.Toggle(PrefKeys.TrayRemember, false);
            if (!TrayRememberOn) Prefs.SetList(PrefKeys.TrayItems, []);   // al apagarlo no queda nada guardado
            else SaveTray();
        });
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
        if (TrayRememberOn && Prefs.StoredList(PrefKeys.TrayItems) is { } stored) Tray.Add(stored);
    }

    /// Lo que se suelta en el notch: se añade (lo que exista y no esté ya) y la bandeja se enseña.
    public void AddToTray(IEnumerable<string> paths)
    {
        var requested = paths.ToList();
        int added = Tray.Add(requested);
        trayDragging = false;
        trayStatus = added < requested.Count && Tray.Count >= FileTray.Max ? tr("La bandeja está llena (%d). Quita algo para añadir más.", FileTray.Max) : "";
        if (added == 0 && trayStatus.Length == 0) { Notify(); return; }
        Notify();
        if (added > 0) TrayFilesAdded?.Invoke();
    }

    private void PasteFiles()
    {
        string[] files;
        try { files = Clipboard.ContainsFileDropList() ? Clipboard.GetFileDropList().Cast<string>().ToArray() : []; }
        catch (Exception e) { AppLog.Write($"bandeja: {e.Message}"); files = []; }
        if (files.Length == 0) { trayStatus = tr("No hay archivos copiados en el portapapeles."); Notify(); return; }
        AddToTray(files);
    }

    /// Mientras se arrastran archivos por encima del notch se enseña «Suelta para añadir»; se apaga solo poco después del último aviso.
    public void SetTrayDragging(bool on)
    {
        if (on)
        {
            trayDragTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            trayDragTimer.Tick -= StopTrayDragging;
            trayDragTimer.Tick += StopTrayDragging;
            trayDragTimer.Stop();
            trayDragTimer.Start();
        }
        else trayDragTimer?.Stop();
        if (trayDragging == on) return;
        trayDragging = on;
        Notify();
    }

    private void StopTrayDragging(object? sender, EventArgs e) => SetTrayDragging(false);

    /// Una carpeta de la bandeja pasa a ser el proyecto del widget Código (que se añade al Home si no estaba).
    public bool OpenInCode(string folder)
    {
        var ids = WidgetIds;
        if (!ids.Contains("code"))
        {
            if (!WidgetLayout.CanAdd(ids, "code")) { trayStatus = tr("Quita otro widget para hacer sitio."); Notify(); return false; }
            Prefs.SetList(PrefKeys.NotchWidgets, WidgetLayout.Add(ids, "code"));
        }
        if (!Hub.Code.SetRoot(folder)) { trayStatus = tr("La carpeta «%@» no existe.", folder); Notify(); return false; }
        Show(PanelView.Home);
        Notify();
        return true;
    }

    private void OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception e)
        {
            AppLog.Write($"bandeja: {e.Message}");
            trayStatus = tr("No se pudo abrir «%@»: %@", Path.GetFileName(path), e.Message);
            Notify();
        }
    }

    private void SaveTray()
    {
        if (TrayRememberOn) Prefs.SetList(PrefKeys.TrayItems, Tray.Entries.Select(e => e.Path));
    }

    /// El contenido de una carpeta (cuántos elementos y cuánto pesa) se mide fuera del hilo de la interfaz, una vez por carpeta.
    private void MeasureFolder(string path)
    {
        if (trayStats.ContainsKey(path) || !trayMeasuring.Add(path)) return;
        Task.Run(() => FileTray.Measure(path)).ContinueWith(task =>
        {
            var stats = task.Status == TaskStatus.RanToCompletion ? task.Result : new FolderStats(0, 0, 0, false);
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                trayMeasuring.Remove(path);
                trayStats[path] = stats;
                Notify();
            });
        });
    }

    private string DetailOf(TrayEntry entry)
    {
        if (!entry.IsDirectory) return FormatSize(entry.Size);
        if (!trayStats.TryGetValue(entry.Path, out var stats)) return tr("Carpeta");
        int count = stats.Files + stats.Folders;
        return stats.Truncated ? tr("Carpeta · más de %d elementos", count) : tr("Carpeta · %@ · %@", Items(count), FormatSize(stats.Bytes));
    }

    /// Las imágenes se enseñan en miniatura; el resto, con su icono de Windows.
    private ImageSource? ThumbnailOf(TrayEntry entry)
    {
        if (entry.IsDirectory) return FileIcons.For(entry.Name, true, false);   // la carpeta con el icono de su tipo (src, docs…)
        if (trayThumbnails.TryGetValue(entry.Path, out var cached)) return cached;
        ImageSource? image = null;
        if (CodeTree.IsImage(entry.Path))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(entry.Path);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 64;
                bitmap.EndInit();
                bitmap.Freeze();
                image = bitmap;
            }
            catch (Exception e) when (e is IOException or NotSupportedException or InvalidOperationException or UnauthorizedAccessException) { }
        }
        return trayThumbnails[entry.Path] = image ?? AppIcons.For(entry.Path);
    }

    private void RebuildTray()
    {
        Tray.Prune();
        foreach (var entry in Tray.Entries.Where(e => e.IsDirectory)) MeasureFolder(entry.Path);
        var signature = string.Join("|", Tray.Entries.Select(e => e.Path + ":" + (trayStats.TryGetValue(e.Path, out var s) ? $"{s.Files}/{s.Folders}/{s.Bytes}/{s.Truncated}" : "?"))) + "#" + Effective;
        if (signature == traySignature) return;
        traySignature = signature;
        SaveTray();
        TrayRows = Tray.Entries.Select(entry =>
        {
            var path = entry.Path;
            return new TrayRow(entry.Name, DetailOf(entry), ThumbnailOf(entry), entry.IsDirectory, path,
                new Command(() => { Tray.Remove(path); Notify(); }),
                new Command(() => RevealInExplorer(path)),
                new Command(() => { try { Clipboard.SetText(path); } catch (Exception e) { AppLog.Write($"bandeja: {e.Message}"); } }),
                new Command(() => OpenEditor(path)),
                new Command(() => OpenPath(path)),
                new Command(() => OpenInCode(path)),
                new Command(() => _ = ReadTrayImage(path)));
        }).ToList();
    }

    private async Task ReadTrayImage(string path)
    {
        trayStatus = tr("Reconociendo texto…"); Notify();
        try
        {
            var text = await Ocr.ReadImage(path);
            if (text.Length > 0) { Clipboard.SetText(text); trayStatus = tr("Texto copiado al portapapeles."); }
            else trayStatus = tr("No se encontró texto en la imagen.");
        }
        catch (Exception e) { trayStatus = tr("No se pudo leer la imagen: %@", e.Message); }
        Notify();
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
