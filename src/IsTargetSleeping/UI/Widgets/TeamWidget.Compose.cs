using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// Una imagen adjunta a la tarea: su miniatura, su nombre y quitarla.
public sealed class TeamAttachmentRow
{
    public TeamAttachmentRow(string path, ImageSource? thumbnail, Action<TeamAttachmentRow> remove)
    {
        Path = path;
        Thumbnail = thumbnail;
        Remove = new Command(() => remove(this));
    }

    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path);
    public ImageSource? Thumbnail { get; }
    public ICommand Remove { get; }
    public string Help => tr("Quitar «%@»", Name);
}

/// La caja donde se escribe la tarea: imágenes adjuntas (botón +, arrastrar o pegar), el reparto y el esfuerzo de los agentes.
public sealed partial class TeamWidget
{
    private readonly System.Collections.ObjectModel.ObservableCollection<TeamAttachmentRow> attachments = [];
    private string? attachDir;
    /// Imágenes pesadas ya copiadas, a la espera de que el usuario diga si las quiere (Continuar) o no (Cancelar).
    private readonly List<string> heavyPending = [];

    // MARK: imágenes

    public IReadOnlyList<TeamAttachmentRow> Attachments => attachments;
    public bool HasAttachments => attachments.Count > 0;
    public ICommand AttachImages => attachImages ??= new Command(PickImages);
    private ICommand? attachImages;
    public string AttachHelp => tr("Adjuntar imágenes (también se pueden arrastrar o pegar)");
    public string AttachedHelp => tr("Imágenes adjuntas");

    private string AttachDirectory()
    {
        if (attachDir is null)
        {
            // En la demo no se escribe en la carpeta de datos real.
            var root = demo ? Path.Combine(Path.GetTempPath(), "its-team-attachments") : Path.Combine(Paths.DataDir, "team", "attachments");
            attachDir = Path.Combine(root, Guid.NewGuid().ToString("N")[..8]);
        }
        Directory.CreateDirectory(attachDir);
        return attachDir;
    }

    private void PickImages()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = tr("Adjuntar imágenes"),
            Multiselect = true,
            Filter = tr("Imágenes") + "|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp",
        };
        if (dialog.ShowDialog() == true) AddImages(dialog.FileNames);
    }

    /// Copia las imágenes a una carpeta propia de la tarea (así no dependen del original) y avisa de las que no caben.
    /// Devuelve cuántas se añadieron.
    public int AddImages(IEnumerable<string> paths)
    {
        int added = 0, rejected = 0;
        foreach (var path in paths)
        {
            if (attachments.Count + heavyPending.Count >= TeamEffort.MaxImages || TeamEffort.Reject(path) is not null) { rejected++; continue; }
            try
            {
                var target = UniqueName(AttachDirectory(), Path.GetFileName(path));
                File.Copy(path, target);
                if (Admit(target)) added++;
            }
            catch { rejected++; }
        }
        status = rejected > 0 ? tr("No se añadieron %d imágenes: caben %d como máximo y solo se admiten PNG, JPG, GIF, WEBP o BMP (no SVG).", rejected, TeamEffort.MaxImages) : "";
        Raise();
        return added;
    }

    /// Ctrl+V sobre la caja: una captura, o archivos de imagen copiados en el explorador. False si no hay nada que adjuntar
    /// (entonces se pega el texto como siempre).
    public bool PasteAttachments()
    {
        try
        {
            if (Clipboard.ContainsFileDropList() && !Clipboard.ContainsText())
            {
                var images = Clipboard.GetFileDropList().Cast<string>().Where(f => TeamEffort.ImageExtensions.Contains(Path.GetExtension(f))).ToList();
                if (images.Count > 0) { AddImages(images); return true; }
            }
            return !Clipboard.ContainsText() && PasteImage();
        }
        catch { return false; }
    }

    /// Pega la imagen del portapapeles (una captura, por ejemplo). False si no hay ninguna.
    public bool PasteImage()
    {
        try
        {
            if (!Clipboard.ContainsImage() || Clipboard.GetImage() is not { } image) return false;
            if (attachments.Count + heavyPending.Count >= TeamEffort.MaxImages) { status = tr("Ya hay %d imágenes: es el máximo.", TeamEffort.MaxImages); Raise(); return true; }
            var target = UniqueName(AttachDirectory(), "captura.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var file = File.Create(target)) encoder.Save(file);
            Admit(target);
            status = "";
            Raise();
            return true;
        }
        catch { return false; }
    }

    /// Una imagen ya copiada: se adjunta, salvo que pese mucho, que espera la decisión del usuario. True si se adjuntó.
    private bool Admit(string target)
    {
        if (TeamEffort.IsHeavy(target)) { heavyPending.Add(target); return false; }
        attachments.Add(MakeAttachment(target));
        return true;
    }

    public bool HasHeavyWarning => heavyPending.Count > 0;
    public string HeavyWarningText => heavyPending.Count == 1
        ? tr("«%@» pesa %@ MB. Pasarse de %d MB puede costar más tokens de lo habitual.", Path.GetFileName(heavyPending[0]), HeavyMegabytes(heavyPending[0]), TeamEffort.HeavyImageMegabytes)
        : tr("%d imágenes pesan más de %d MB. Pasarse de ese tamaño puede costar más tokens de lo habitual.", heavyPending.Count, TeamEffort.HeavyImageMegabytes);
    public string HeavyContinueLabel => tr("Continuar");
    public string HeavyCancelLabel => tr("Cancelar");
    public ICommand HeavyContinue => heavyContinue ??= new Command(() =>
    {
        foreach (var path in heavyPending)
        {
            if (attachments.Count < TeamEffort.MaxImages) attachments.Add(MakeAttachment(path));
            else { try { File.Delete(path); } catch { } }
        }
        heavyPending.Clear();
        status = "";
        Raise();
    });
    public ICommand HeavyCancel => heavyCancel ??= new Command(() =>
    {
        foreach (var path in heavyPending) { try { File.Delete(path); } catch { } }
        heavyPending.Clear();
        CleanAttachDir();
        Raise();
    });
    private ICommand? heavyContinue, heavyCancel;
    private static string HeavyMegabytes(string path)
    {
        try { return (new FileInfo(path).Length / 1048576.0).ToString("0.#", CultureInfo.CurrentCulture); }
        catch { return "?"; }
    }

    private void CleanAttachDir()
    {
        if (attachments.Count == 0 && heavyPending.Count == 0 && attachDir is not null) { try { Directory.Delete(attachDir, true); } catch { } attachDir = null; }
    }

    private TeamAttachmentRow MakeAttachment(string path)
    {
        ImageSource? thumbnail = null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.DecodePixelWidth = 64;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            thumbnail = bitmap;
        }
        catch { /* una imagen que no se puede mostrar se adjunta igual: solo sin miniatura */ }
        return new TeamAttachmentRow(path, thumbnail, RemoveAttachment);
    }

    private void RemoveAttachment(TeamAttachmentRow row)
    {
        attachments.Remove(row);
        try { File.Delete(row.Path); } catch { }
        CleanAttachDir();
        status = "";
        Raise();
    }

    private static string UniqueName(string dir, string name)
    {
        var target = Path.Combine(dir, name);
        for (int i = 2; File.Exists(target); i++) target = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(name)}-{i}{Path.GetExtension(name)}");
        return target;
    }

    // MARK: esfuerzo

    public TeamEffortLevel EffortLevel => TeamEffort.Parse(prefs.StoredList(PrefKeys.TeamEffort)?.FirstOrDefault());

    /// Posición del deslizador: 0 = Auto … 4 = Máximo.
    public double EffortIndex
    {
        get => (int)EffortLevel;
        set
        {
            var level = (TeamEffortLevel)(int)Math.Round(Math.Clamp(value, 0, TeamEffort.All.Count - 1));
            if (level == EffortLevel) return;
            prefs.SetList(PrefKeys.TeamEffort, [level.ToString()]);
            Rebuild();
        }
    }
    public double EffortMaximum => TeamEffort.All.Count - 1;

    public static string EffortName(TeamEffortLevel level) => level switch
    {
        TeamEffortLevel.Low => tr("Bajo"),
        TeamEffortLevel.Medium => tr("Medio"),
        TeamEffortLevel.High => tr("Alto"),
        TeamEffortLevel.Max => tr("Máximo"),
        _ => tr("Auto"),
    };
    public string EffortText => EffortName(EffortLevel);
    /// Aguja del tacómetro: 0 (Bajo) … 1 (Máximo); −1 en Auto, que se dibuja como «A».
    public double EffortFraction => EffortLevel == TeamEffortLevel.Auto ? -1 : ((int)EffortLevel - 1) / (TeamEffort.All.Count - 2d);
    public string EffortTitle => tr("Esfuerzo");
    public string EffortFaster => tr("Más rápido");
    public string EffortSmarter => tr("Más a fondo");
    public string EffortHelp => tr("Cuánto razonan los agentes. Auto lo decide el router en cada paso; más esfuerzo piensa más y gasta más tokens.");
    public string EffortButtonHelp => tr("Esfuerzo: %@", EffortText);
    public string PresetButtonHelp => tr("Reparto: %@", selected.Name);
    public string SendHelp => tr("Lanzar la tarea (Intro); Mayús+Intro, salto de línea");
    // Los tres nodos de ejemplo del plan vacío: quién planifica, quién hace y quién revisa.
    public string GhostPlanLabel => tr("Planificar");
    public string GhostDoLabel => tr("Implementar");
    public string GhostReviewLabel => tr("Revisar");
    public string KeysHint => tr("Intro lanza · Mayús+Intro nueva línea");
}
