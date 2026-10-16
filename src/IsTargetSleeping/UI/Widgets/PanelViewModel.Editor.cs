using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// Edición rápida de un archivo de texto de la bandeja: abrir, cambiar, guardar y ver el resultado sin salir del notch.
/// El texto sin guardar vive solo en memoria: no se pierde al plegar el notch, pero sí al cerrar la app.
public sealed partial class PanelViewModel
{
    private TextDoc? editDoc;
    private string editText = "";
    private string editStatus = "";
    private bool editConflict, editCloseWarned;

    public bool ShowingEditor => view == PanelView.Editor;
    public string EditText
    {
        get => editText;
        set
        {
            if (value == editText) return;
            editText = value;
            editStatus = "";
            editConflict = false;
            editCloseWarned = false;
            RaiseEditor();
        }
    }
    public bool EditDirty => editDoc is not null && Norm(editText) != editDoc.Text;
    public string EditStatus => editStatus.Length > 0 ? editStatus : EditDirty ? tr("Sin guardar") : tr("Guardado");
    public bool EditConflict => editConflict;
    public string EditSaveLabel => tr("Guardar");
    public string EditSaveHelp => tr("Guarda los cambios en el archivo (Ctrl+S)");
    public string EditReloadLabel => tr("Recargar");
    public string EditReloadHelp => tr("Vuelve a leer el archivo del disco y descarta lo que hayas cambiado");
    public string EditOverwriteLabel => tr("Sobrescribir");
    public string EditOverwriteHelp => tr("Guarda tus cambios aunque el archivo haya cambiado en el disco");
    public string EditCloseLabel => tr("Cerrar");
    public string EditCloseHelp => tr("Vuelve a la bandeja");
    public string EditTitle => editDoc is null ? "" : Path.GetFileName(editDoc.Path);
    public ICommand SaveEdit { get; private set; } = null!;
    public ICommand ReloadEdit { get; private set; } = null!;
    public ICommand OverwriteEdit { get; private set; } = null!;
    public ICommand CloseEdit { get; private set; } = null!;

    private static string Norm(string text) => text.Replace("\r\n", "\n");

    private string EditSubtitle => EditDirty
        ? tr("Cambios sin guardar")
        : tr("Editando · %@", editDoc is null ? "" : Path.GetFileName(Path.GetDirectoryName(editDoc.Path)) ?? "");

    private void InitEditor()
    {
        SaveEdit = new Command(() => DoSave(false));
        OverwriteEdit = new Command(() => DoSave(true));
        ReloadEdit = new Command(() => { if (editDoc is not null) LoadEdit(editDoc.Path, keepView: true); });
        CloseEdit = new Command(CloseEditor);
    }

    /// Abre un archivo de la bandeja. Si no es texto válido o es demasiado grande, lo dice en la bandeja y no cambia de página.
    public void OpenEditor(string path)
    {
        if (!LoadEdit(path, keepView: false)) return;
        Show(PanelView.Editor);
        Notify();
    }

    private bool LoadEdit(string path, bool keepView)
    {
        var (doc, error, message) = TextFile.Load(path);
        if (doc is null)
        {
            var text = error switch
            {
                TextFileError.TooBig => tr("«%@» es demasiado grande para editarlo aquí (máximo 1 MB).", Path.GetFileName(path)),
                TextFileError.NotText => tr("«%@» no es un archivo de texto (UTF-8 o UTF-16), no se abre para no estropearlo.", Path.GetFileName(path)),
                _ => tr("No se pudo abrir «%@»: %@", Path.GetFileName(path), message ?? tr("no existe")),
            };
            if (keepView) { editStatus = text; RaiseEditor(); }
            else { trayStatus = text; Notify(); }
            return false;
        }
        editDoc = doc;
        editText = doc.Text;
        editStatus = "";
        editConflict = editCloseWarned = false;
        if (keepView) RaiseEditor();
        return true;
    }

    private void DoSave(bool overwrite)
    {
        if (editDoc is null) return;
        var (doc, result, message) = TextFile.Save(editDoc, editText, overwrite);
        editDoc = doc;
        editConflict = result == TextSaveResult.Conflict;
        editCloseWarned = false;
        editStatus = result switch
        {
            TextSaveResult.Saved => tr("Guardado a las %@", DateTime.Now.ToString("HH:mm:ss")),
            TextSaveResult.Conflict => tr("El archivo cambió en el disco desde que lo abriste. Recarga para ver lo nuevo o sobrescribe con tus cambios."),
            _ => tr("No se pudo guardar: %@", message ?? ""),
        };
        RaiseEditor();
    }

    /// Con cambios sin guardar, el primer intento avisa y el segundo descarta.
    private void CloseEditor()
    {
        if (EditDirty && !editCloseWarned)
        {
            editCloseWarned = true;
            editStatus = tr("Hay cambios sin guardar. Pulsa Cerrar otra vez para descartarlos.");
            RaiseEditor();
            return;
        }
        editDoc = null;
        editText = "";
        editStatus = "";
        editConflict = editCloseWarned = false;
        Show(PanelView.Tray);
        Notify();
    }

    private void RaiseEditor()
    {
        foreach (var name in new[] { nameof(EditText), nameof(EditDirty), nameof(EditStatus), nameof(EditConflict), nameof(HeaderTitle), nameof(Subtitle) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
