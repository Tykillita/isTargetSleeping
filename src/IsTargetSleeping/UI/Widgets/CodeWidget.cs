using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

public enum TabKind { Text, Image, Message }

/// Una pestaña del widget Código: un archivo de texto (editable), una imagen (visor) o un aviso (binario o demasiado grande).
public sealed class CodeTab(string path, TabKind kind) : INotifyPropertyChanged
{
    private string text = "";
    private bool active;
    public string Path { get; } = path;
    public string Name { get; } = System.IO.Path.GetFileName(path);
    public TabKind Kind { get; } = kind;
    public TextDoc? Doc { get; set; }
    public ImageSource? Image { get; set; }
    public string Message { get; set; } = "";
    public bool CloseWarned { get; set; }
    public ICommand Select { get; set; } = null!;
    public ICommand Close { get; set; } = null!;

    /// Con «\n» o «\r\n», tal como lo deja el cuadro de texto: al comparar y al guardar se normaliza.
    public string Text
    {
        get => text;
        set { if (value == text) return; text = value; Raise(); }
    }
    public bool Dirty => Doc is not null && text.Replace("\r\n", "\n") != Doc.Text;
    public bool Active { get => active; set { if (active == value) return; active = value; Raise(); } }
    public string Label => Dirty ? "● " + Name : Name;
    public string SelectHelp => Dirty ? tr("%@ (sin guardar)", Name) : tr("Pestaña %@", Name);
    public string CloseHelp => tr("Cerrar «%@»", Name);

    public event PropertyChangedEventHandler? PropertyChanged;
    public void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

/// Una fila del árbol de archivos: una carpeta se abre y se cierra, un archivo se abre en una pestaña.
public sealed record CodeRow(string Name, string Path, int Depth, bool IsDirectory, bool Expanded, bool Active, ICommand Click)
{
    public Thickness Indent => new(Depth * 12, 0, 0, 0);
    public string Chevron => !IsDirectory ? "" : Expanded ? "" : "";
    public ImageSource? Icon => FileIcons.For(Name, IsDirectory, Expanded);
    public bool HasIcon => Icon is not null;
    public bool NoIcon => Icon is null;
    public string Glyph => IsDirectory ? (Expanded ? "" : "") : "";
    public string Help => IsDirectory ? (Expanded ? tr("Contraer «%@»", Name) : tr("Expandir «%@»", Name)) : tr("Abrir «%@»", Name);
}

/// El widget Código: un IDE pequeño dentro del notch con el árbol de una carpeta, pestañas, visor de imágenes y editor de texto.
/// Un widget de dos celdas siempre (el árbol y el editor no caben en una): no tiene vista compacta. En la vista corta
/// solo resume (carpeta y pestañas), porque ahí no se puede escribir. Lo sin guardar vive solo en memoria.
public sealed class CodeWidget : WidgetModel
{
    public const int MaxTabs = 8;
    private readonly Prefs prefs;
    private readonly HashSet<string> expanded = new(StringComparer.OrdinalIgnoreCase);
    private string? root;
    private string status = "";
    private bool conflict;
    private readonly DispatcherTimer flash = new() { Interval = TimeSpan.FromSeconds(3) };

    public CodeWidget(Prefs prefs) : base(WidgetCatalog.Find("code")!)
    {
        this.prefs = prefs;
        if (prefs.StoredList(PrefKeys.CodeRoot)?.FirstOrDefault() is { Length: > 0 } saved && Directory.Exists(saved)) root = saved;
        flash.Tick += (_, _) =>
        {
            flash.Stop();
            if (status == tr("Guardado")) { status = ""; SoftRaise(); }
        };
        OpenFolder = new Command(PickFolder);
        Refresh = new Command(() => { RebuildRows(); Raise(); });
        Save = new Command(() => DoSave(false));
        Overwrite = new Command(() => DoSave(true));
        Reload = new Command(ReloadActive);
        CloseActive = new Command(() => { if (Active is { } tab) CloseTab(tab); });
        RebuildRows();
    }

    public List<CodeRow> Rows { get; private set; } = [];
    public List<CodeTab> Tabs { get; private set; } = [];
    public CodeTab? Active { get; private set; }
    public string? Root => root;
    public ICommand OpenFolder { get; }
    public ICommand Refresh { get; }
    public ICommand Save { get; }
    public ICommand Overwrite { get; }
    public ICommand Reload { get; }
    public ICommand CloseActive { get; }

    public bool HasRoot => root is not null;
    public bool NoRoot => root is null;
    public string RootName => root is null ? "" : Path.GetFileName(root.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : root;
    public string EmptyText => tr("Elige una carpeta de proyecto para ver su árbol de archivos, abrirlos en pestañas y editarlos aquí mismo.");
    public string OpenFolderLabel => tr("Abrir carpeta");
    public string OpenFolderHelp => tr("Elige la carpeta del proyecto que enseña el árbol");
    public string RefreshHelp => tr("Volver a leer la carpeta");
    public string NoTabsText => tr("Elige un archivo del árbol.");
    public bool HasTabs => Tabs.Count > 0;
    public bool NoTabs => Tabs.Count == 0;
    public bool ShowText => Active is { Kind: TabKind.Text };
    public bool ShowImage => Active is { Kind: TabKind.Image };
    public bool ShowMessage => Active is { Kind: TabKind.Message };
    public ImageSource? ActiveImage => Active?.Image;
    public string ActiveMessage => Active?.Message ?? "";
    public string SaveLabel => tr("Guardar");
    public string SaveHelp => tr("Guarda los cambios en el archivo (Ctrl+S)");
    public string ReloadLabel => tr("Recargar");
    public string ReloadHelp => tr("Vuelve a leer el archivo del disco y descarta lo que hayas cambiado");
    public string OverwriteLabel => tr("Sobrescribir");
    public string OverwriteHelp => tr("Guarda tus cambios aunque el archivo haya cambiado en el disco");
    public string ActiveName => Active?.Name ?? "";
    public string EditorName => Active is null ? Title : tr("Editor de %@", Active.Name);
    public bool Conflict => conflict;
    /// Cuánto dura el «Guardado» (los tests lo acortan).
    public TimeSpan FlashDelay { get => flash.Interval; set => flash.Interval = value; }
    public int DirtyCount => Tabs.Count(t => t.Dirty);

    public string ActiveText
    {
        get => Active?.Text ?? "";
        set
        {
            if (Active is not { Kind: TabKind.Text } tab || tab.Text == value) return;
            tab.Text = value;
            tab.CloseWarned = false;
            status = "";
            conflict = false;
            SoftRaise();
        }
    }

    /// Lo que cabe en la cabecera: un conflicto se resume en pocas palabras (el texto entero va en el tooltip).
    public string ShortStatus => conflict ? tr("Cambió en el disco") : Status;
    public string Status => status.Length > 0 ? status : Active is { Kind: TabKind.Text } t ? t.Dirty ? tr("Sin guardar") : "" : "";

    /// La línea de la vista al pasar el ratón: carpeta y pestañas.
    public string Summary => root is null
        ? tr("Sin carpeta abierta")
        : Tabs.Count == 0
            ? tr("%@ · ningún archivo abierto", RootName)
            : DirtyCount > 0 ? tr("%@ · %d abiertos, %d sin guardar", RootName, Tabs.Count, DirtyCount) : tr("%@ · %d abiertos", RootName, Tabs.Count);

    private void SoftRaise()
    {
        foreach (var name in new[] { nameof(Status), nameof(ShortStatus), nameof(Conflict), nameof(Summary), nameof(DirtyCount) })
            RaiseProperty(name);
    }

    // MARK: la carpeta y el árbol

    private void PickFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = tr("Elige la carpeta del proyecto"), InitialDirectory = root ?? "" };
        if (dialog.ShowDialog() == true) SetRoot(dialog.FolderName);
    }

    /// La carpeta del árbol (se recuerda entre sesiones; solo guarda la ruta). Las pestañas abiertas siguen abiertas.
    public bool SetRoot(string path)
    {
        if (!Directory.Exists(path))
        {
            status = tr("La carpeta «%@» no existe.", path);
            Raise();
            return false;
        }
        root = path;
        expanded.Clear();
        prefs.SetList(PrefKeys.CodeRoot, [path]);
        status = "";
        RebuildRows();
        Raise();
        return true;
    }

    private void RebuildRows()
    {
        if (root is null) { Rows = []; return; }
        Rows = CodeTree.Flatten(root, expanded).Select(e =>
        {
            var entry = e;
            return new CodeRow(e.Name, e.Path, e.Depth, e.IsDirectory, e.Expanded, Active?.Path == e.Path,
                new Command(() => { if (entry.IsDirectory) Toggle(entry.Path); else OpenFile(entry.Path); }));
        }).ToList();
    }

    private void Toggle(string dir)
    {
        if (!expanded.Add(dir)) expanded.Remove(dir);
        RebuildRows();
        Raise();
    }

    // MARK: las pestañas

    public void OpenFile(string path)
    {
        if (Tabs.FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase)) is { } existing) { Activate(existing); return; }
        if (Tabs.Count >= MaxTabs)
        {
            status = tr("Hay %d pestañas abiertas: cierra alguna.", MaxTabs);
            Raise();
            return;
        }
        var tab = CodeTree.IsImage(path) ? LoadImage(path) : LoadText(path);
        if (tab is null) { Raise(); return; }
        tab.Select = new Command(() => Activate(tab));
        tab.Close = new Command(() => CloseTab(tab));
        Tabs = [.. Tabs, tab];
        Activate(tab);
    }

    private CodeTab? LoadText(string path)
    {
        var (doc, error, message) = TextFile.Load(path);
        var name = Path.GetFileName(path);
        if (doc is not null) return new CodeTab(path, TabKind.Text) { Doc = doc, Text = doc.Text };
        if (error == TextFileError.Unreadable)
        {
            status = tr("No se pudo abrir «%@»: %@", name, message ?? tr("no existe"));
            return null;
        }
        return new CodeTab(path, TabKind.Message)
        {
            Message = error == TextFileError.TooBig
                ? tr("«%@» es demasiado grande para mostrarlo aquí (máximo 1 MB).", name)
                : tr("«%@» no es un archivo de texto (UTF-8 o UTF-16): no se puede mostrar ni editar aquí.", name),
        };
    }

    private CodeTab? LoadImage(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = BitmapCacheOption.OnLoad;   // no deja el archivo abierto
            image.DecodePixelWidth = 1200;
            image.EndInit();
            image.Freeze();
            return new CodeTab(path, TabKind.Image) { Image = image };
        }
        catch (Exception e) when (e is IOException or NotSupportedException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new CodeTab(path, TabKind.Message) { Message = tr("No se pudo mostrar «%@»: %@", Path.GetFileName(path), e.Message) };
        }
    }

    private void Activate(CodeTab? tab)
    {
        Active = tab;
        foreach (var t in Tabs) t.Active = t == tab;
        status = "";
        conflict = false;
        RebuildRows();
        Raise();
    }

    /// Con cambios sin guardar, el primer intento avisa y el segundo descarta.
    private void CloseTab(CodeTab tab)
    {
        if (tab.Dirty && !tab.CloseWarned)
        {
            tab.CloseWarned = true;
            status = tr("«%@» tiene cambios sin guardar. Ciérralo otra vez para descartarlos.", tab.Name);
            Raise();
            return;
        }
        int index = Tabs.IndexOf(tab);
        Tabs = Tabs.Where(t => t != tab).ToList();
        if (Active == tab) Activate(Tabs.Count == 0 ? null : Tabs[Math.Min(index, Tabs.Count - 1)]);
        else { RebuildRows(); Raise(); }
    }

    private void DoSave(bool overwrite)
    {
        if (Active is not { Kind: TabKind.Text, Doc: { } doc } tab) return;
        bool wasDirty = tab.Dirty;
        var (saved, result, message) = TextFile.Save(doc, tab.Text, overwrite);
        tab.Doc = saved;
        tab.CloseWarned = false;
        conflict = result == TextSaveResult.Conflict;
        status = result switch
        {
            TextSaveResult.Saved => wasDirty ? tr("Guardado") : "",
            TextSaveResult.Conflict => tr("El archivo cambió en el disco desde que lo abriste. Recarga para ver lo nuevo o sobrescribe con tus cambios."),
            _ => tr("No se pudo guardar: %@", message ?? ""),
        };
        tab.Raise();
        SoftRaise();
        // «Guardado» solo avisa de que lo editado ya está en disco, y se apaga solo.
        flash.Stop();
        if (result == TextSaveResult.Saved && status.Length > 0) flash.Start();
    }

    private void ReloadActive()
    {
        if (Active is not { Kind: TabKind.Text } tab) return;
        var (doc, _, message) = TextFile.Load(tab.Path);
        if (doc is null)
        {
            status = tr("No se pudo abrir «%@»: %@", tab.Name, message ?? tr("no existe"));
            SoftRaise();
            return;
        }
        tab.Doc = doc;
        tab.Text = doc.Text;
        tab.CloseWarned = false;
        status = "";
        conflict = false;
        Raise();
    }
}
