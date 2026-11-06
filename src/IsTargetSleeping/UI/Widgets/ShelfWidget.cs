using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// Un mosaico del Estante: el icono del archivo, su nombre corto y, debajo, el tamaño (o «Carpeta»).
public sealed record ShelfTile(string Path, string Name, string ShortName, string Detail, ImageSource? Icon, bool Selecting, bool Selected,
    ICommand Activate, ICommand Remove, ICommand Copy, string RemoveHelp, string CopyHelp)
{
    public bool NotSelecting => !Selecting;
}

/// El widget Estante: una bandeja temporal de archivos y carpetas. Guarda solo las rutas (nunca mueve ni copia nada) en shelf.json;
/// los archivos que desaparecen se quitan solos. Un clic abre el archivo; «Select» marca varios para quitarlos; se arrastra hacia fuera.
/// En las capturas no toca el disco.
public sealed class ShelfWidget : WidgetModel
{
    private readonly bool persist;
    private readonly Prefs? prefs;
    private readonly DispatcherTimer dragTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly ShelfList list = new();
    private IReadOnlyList<ShelfTile> tiles = [];
    private bool dragging;
    private bool removing;

    public ShelfWidget(bool persist, Prefs? prefs = null) : base(WidgetCatalog.Find("shelf")!)
    {
        this.persist = persist;
        this.prefs = prefs;
        if (prefs is not null) prefs.Changed += OnPrefsChanged;
        if (persist) Load();
        dragTimer.Tick += (_, _) => SetDragging(false);
        ToggleSelect = new Command(() => { if (!removing) { list.ToggleSelect(); Changed(false); } });
        ClearOrRemove = new Command(() => RequestRemoval(list.Selecting ? list.Files.Where(list.IsMarked).ToArray() : list.Files.ToArray()));
        Rebuild();
    }

    public static string FilePath => Path.Combine(Paths.DataDir, "shelf.json");

    private void Load()
    {
        try { if (File.Exists(FilePath)) list.Load(ShelfStore.Parse(File.ReadAllText(FilePath))); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { AppLog.Write($"estante: {e.Message}"); }
    }

    // MARK: lo que enseña

    public string HeaderTitle => tr("Estante");
    public IReadOnlyList<ShelfTile> Tiles => tiles;
    public bool HasContent => list.Files.Count > 0;
    public bool IsEmpty => list.Files.Count == 0;
    public bool Selecting => list.Selecting;
    public bool IsRemoving => removing;
    public bool Dragging => dragging;
    public bool NotDragging => !dragging;
    public bool EmptyIdle => IsEmpty && !dragging;
    public bool ReducedMotion => prefs?.ReducedMotion ?? false;
    public string DropText => dragging ? tr("Suelta para añadir") : tr("Suelta archivos aquí");
    public string DropHint => tr("Arrástralos fuera cuando quieras · clic para abrir o copiar");
    public string SelectLabel => list.Selecting ? tr("Listo") : tr("Elegir");
    public string ClearLabel => list.Selecting ? tr("Quitar") : tr("Vaciar todo");
    public string SelectHelp => list.Selecting ? tr("Salir de la selección") : tr("Elegir archivos para quitarlos");
    public string ClearHelp => list.Selecting ? tr("Quitar los archivos marcados del estante (no borra nada)") : tr("Vaciar el estante (no borra ningún archivo)");

    public ICommand ToggleSelect { get; }
    public ICommand ClearOrRemove { get; }
    public event Func<IReadOnlyList<string>, Action, bool>? RemovalAnimationRequested;
    public event Action<System.Windows.Point>? DragMoved;
    public event Action? DragEnded;

    private void OnPrefsChanged() => RaiseProperty(nameof(ReducedMotion));

    public void UpdateDragPosition(System.Windows.Point screenPoint)
    {
        SetDragging(true);
        DragMoved?.Invoke(screenPoint);
    }

    // MARK: acciones

    /// Lo suelta el notch cuando se arrastran archivos encima con el estante a la vista.
    public void AddFiles(IEnumerable<string> paths, System.Windows.Point? screenPoint = null)
    {
        list.Add(paths);
        SetDragging(false);
        Changed(true);
        if (screenPoint is { } at) Dropped?.Invoke(at);
    }

    /// Se soltó algo: el punto (en píxeles de pantalla) donde sale la onda.
    public event Action<System.Windows.Point>? Dropped;

    /// Mientras se arrastra algo por encima, la zona se ilumina; se apaga sola si el arrastre se acaba sin soltar.
    public void SetDragging(bool on)
    {
        dragTimer.Stop();
        if (on) dragTimer.Start();
        if (dragging == on) return;
        dragging = on;
        Raise();
        if (!on) DragEnded?.Invoke();
    }

    private void Activate(string path)
    {
        if (list.Selecting) { list.Mark(path); Changed(false); return; }
        if (!File.Exists(path) && !Directory.Exists(path)) { list.Remove(path); Changed(true); return; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception e) { AppLog.Write($"estante: {e.Message}"); }
    }

    private void Remove(string path) => RequestRemoval([path]);

    private void RequestRemoval(IReadOnlyList<string> paths)
    {
        if (removing) return;
        if (paths.Count == 0) { Changed(true); return; }
        removing = true;
        RaiseProperty(nameof(IsRemoving));
        var committed = false;
        void Commit()
        {
            if (committed) return;
            committed = true;
            foreach (var path in paths) list.Remove(path);
            removing = false;
            Changed(true);
        }

        if (RemovalAnimationRequested is { } handlers)
            foreach (Func<IReadOnlyList<string>, Action, bool> handler in handlers.GetInvocationList())
                try { if (handler(paths, Commit)) return; }
                catch (Exception e) { AppLog.Write($"estante: animación de retirada: {e.Message}"); }
        Commit();
    }

    /// Pone el archivo en el portapapeles como si lo hubieras copiado en el Explorador (Ctrl+V lo copia donde quieras).
    private static void CopyToClipboard(string path)
    {
        try { System.Windows.Clipboard.SetFileDropList([path]); }
        catch (Exception e) { AppLog.Write($"estante: {e.Message}"); }
    }

    /// Cada segundo (con el widget a la vista) se quita lo que ya no existe.
    public override void Tick(DateTime now)
    {
        if (list.Prune()) Changed(true);
    }

    private void Changed(bool save)
    {
        Rebuild();
        if (save) Save();
        Raise();
    }

    private void Rebuild()
    {
        tiles = list.Files.Select(path =>
        {
            var folder = Directory.Exists(path);
            var name = Path.GetFileName(path.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : path;
            long size = 0;
            if (!folder) try { size = new FileInfo(path).Length; } catch (IOException) { }
            return new ShelfTile(path, name, ShelfStore.ShortName(name), folder ? tr("Carpeta") : ShelfStore.Size(size),
                folder ? FileIcons.For(name, true, false) : AppIcons.For(path, large: true), list.Selecting, list.IsMarked(path),
                new Command(() => Activate(path)), new Command(() => Remove(path)),
                new Command(() => CopyToClipboard(path)), tr("Quitar del estante"), tr("Copiar el archivo (Ctrl+V lo pega)"));
        }).ToList();
    }

    public void Save()
    {
        if (!persist) return;
        try { NotesStore.SaveAtomic(FilePath, ShelfStore.Serialize(list.Files)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { AppLog.Write($"estante: {e.Message}"); }
    }

    /// Solo para la galería y las capturas: archivos de verdad que siempre existen, sin tocar el disco.
    public void LoadDemo()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        list.Add([Environment.ProcessPath ?? "", Path.Combine(windows, "notepad.exe"), Path.Combine(windows, "explorer.exe"), AppContext.BaseDirectory.TrimEnd('\\')]);
        Rebuild();
        Raise();
    }
}

/// La luz acompaña el arrastre y, al soltar, deja una pequeña gota y ondas suaves sobre la superficie.
public sealed class ShelfRipple : System.Windows.Controls.Canvas
{
    public static readonly System.Windows.DependencyProperty SourceProperty =
        System.Windows.DependencyProperty.Register(nameof(Source), typeof(ShelfWidget), typeof(ShelfRipple), new System.Windows.PropertyMetadata(null, (d, e) => ((ShelfRipple)d).Attach(e.OldValue as ShelfWidget, e.NewValue as ShelfWidget)));

    public ShelfWidget? Source { get => (ShelfWidget?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    private Ellipse? dragAura;
    private bool auraActive;

    public ShelfRipple() { IsHitTestVisible = false; ClipToBounds = true; }

    private void Attach(ShelfWidget? old, ShelfWidget? now)
    {
        if (old is not null) old.Dropped -= Play;
        if (old is not null) { old.DragMoved -= MoveAura; old.DragEnded -= HideAura; }
        if (now is not null) { now.Dropped += Play; now.DragMoved += MoveAura; now.DragEnded += HideAura; }
    }

    private void Play(System.Windows.Point screen)
    {
        HideAura();
        if (!IsVisible || Source?.ReducedMotion == true || !SystemParameters.ClientAreaAnimation) return;
        System.Windows.Point at;
        try { at = PointFromScreen(screen); } catch (InvalidOperationException) { return; }
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var pearl = Theme.Flat ? Theme.Current.Fg : Color.FromRgb(0xE8, 0xEC, 0xFF);
        AddImpact(at, pearl);
        double reachX = Math.Max(Math.Abs(at.X), Math.Abs(ActualWidth - at.X));
        double reachY = Math.Max(Math.Abs(at.Y), Math.Abs(ActualHeight - at.Y));
        double outerDiameter = 2 * Math.Sqrt(reachX * reachX + reachY * reachY) + 24;
        int duration = (int)Math.Clamp(outerDiameter * 3.4, 1900, 3000);
        AddWave(at, pearl, outerDiameter * 0.34, 0, (int)(duration * 0.8), 0.62);
        AddWave(at, pearl, outerDiameter * 0.67, 220, (int)(duration * 0.9), 0.46);
        AddWave(at, pearl, outerDiameter, 440, duration, 0.32);
    }

    private void MoveAura(System.Windows.Point screen)
    {
        if (!IsVisible || Source is not { Dragging: true } source) return;
        System.Windows.Point at;
        try { at = PointFromScreen(screen); } catch (InvalidOperationException) { return; }
        var glow = dragAura ??= CreateAura();
        if (!Children.Contains(glow)) Children.Insert(0, glow);
        SetLeft(glow, at.X - glow.Width / 2);
        SetTop(glow, at.Y - glow.Height / 2);
        if (auraActive) return;
        auraActive = true;
        glow.BeginAnimation(OpacityProperty, null);
        if (source.ReducedMotion || !SystemParameters.ClientAreaAnimation) { glow.Opacity = 0.78; return; }
        glow.Opacity = 0;
        glow.BeginAnimation(OpacityProperty, new DoubleAnimation(0.78, TimeSpan.FromMilliseconds(140)) { EasingFunction = EaseOut() });
    }

    private void HideAura()
    {
        if (!auraActive || dragAura is not { } glow) return;
        auraActive = false;
        glow.BeginAnimation(OpacityProperty, null);
        if (Source is not { ReducedMotion: false } || !SystemParameters.ClientAreaAnimation) { glow.Opacity = 0; return; }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(240)) { EasingFunction = EaseOut() };
        fade.Completed += (_, _) =>
        {
            if (auraActive || !ReferenceEquals(dragAura, glow)) return;
            glow.BeginAnimation(OpacityProperty, null);
            glow.Opacity = 0;
            Children.Remove(glow);
        };
        glow.BeginAnimation(OpacityProperty, fade);
    }

    private Ellipse CreateAura()
    {
        var pearl = Theme.Flat ? Theme.Current.Fg : Color.FromRgb(0xEF, 0xED, 0xFF);
        var fill = new RadialGradientBrush { Center = new(0.5, 0.5), GradientOrigin = new(0.44, 0.4), RadiusX = 0.5, RadiusY = 0.5 };
        fill.GradientStops.Add(new(Color.FromArgb(0x58, pearl.R, pearl.G, pearl.B), 0));
        fill.GradientStops.Add(new(Color.FromArgb(0x28, pearl.R, pearl.G, pearl.B), 0.42));
        fill.GradientStops.Add(new(Color.FromArgb(0, pearl.R, pearl.G, pearl.B), 1));
        fill.Freeze();
        return new Ellipse { Width = 206, Height = 142, Fill = fill, Opacity = 0, Effect = new BlurEffect { Radius = 12, RenderingBias = RenderingBias.Performance }, IsHitTestVisible = false };
    }

    private void AddImpact(System.Windows.Point at, Color pearl)
    {
        var fill = new RadialGradientBrush(Color.FromArgb(0xB8, pearl.R, pearl.G, pearl.B), Color.FromArgb(0, pearl.R, pearl.G, pearl.B));
        fill.Freeze();
        var glint = new Ellipse
        {
            Width = 46, Height = 46, Fill = fill, Effect = new BlurEffect { Radius = 8, RenderingBias = RenderingBias.Performance },
            RenderTransformOrigin = new(0.5, 0.5), RenderTransform = new ScaleTransform(0.35, 0.35), Opacity = 0, IsHitTestVisible = false,
        };
        SetLeft(glint, at.X - glint.Width / 2);
        SetTop(glint, at.Y - glint.Height / 2);
        Children.Add(glint);
        var ease = EaseOut();
        var growX = new DoubleAnimation(0.3, 1.5, TimeSpan.FromMilliseconds(900)) { EasingFunction = ease };
        var fade = new DoubleAnimation(0.7, 0, TimeSpan.FromMilliseconds(950)) { EasingFunction = ease };
        fade.Completed += (_, _) => Children.Remove(glint);
        var scale = (ScaleTransform)glint.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, growX);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, growX);
        glint.BeginAnimation(OpacityProperty, fade);
    }

    private void AddWave(System.Windows.Point at, Color pearl, double diameter, int delayMs, int durationMs, double opacity)
    {
        var stroke = new SolidColorBrush(Color.FromArgb(0xA0, pearl.R, pearl.G, pearl.B));
        stroke.Freeze();
        var wave = new Ellipse
        {
            Width = diameter, Height = diameter, Fill = Brushes.Transparent, Stroke = stroke, StrokeThickness = 2.4,
            Effect = new BlurEffect { Radius = 1.6, RenderingBias = RenderingBias.Performance },
            RenderTransformOrigin = new(0.5, 0.5), RenderTransform = new ScaleTransform(0.04, 0.04), Opacity = 0, IsHitTestVisible = false,
        };
        SetLeft(wave, at.X - diameter / 2);
        SetTop(wave, at.Y - diameter / 2);
        Children.Add(wave);
        var delay = TimeSpan.FromMilliseconds(delayMs);
        var duration = TimeSpan.FromMilliseconds(durationMs);
        // Como una ola en el agua: el anillo se abre frenando poco a poco y se va apagando mientras crece (no se queda fijo y se corta al final).
        var scale = (ScaleTransform)wave.RenderTransform;
        var spread = new CubicEase { EasingMode = EasingMode.EaseOut };
        var grow = new DoubleAnimation(0.04, 1, duration) { BeginTime = delay, EasingFunction = spread };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        var fade = new DoubleAnimationUsingKeyFrames { BeginTime = delay };
        fade.KeyFrames.Add(new SplineDoubleKeyFrame(opacity, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(durationMs * 0.16)), new KeySpline(0.2, 0.6, 0.3, 1)));
        fade.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(duration), new KeySpline(0.3, 0, 0.5, 1)));
        fade.Completed += (_, _) => Children.Remove(wave);
        wave.BeginAnimation(OpacityProperty, fade);
    }

    private static CubicEase EaseOut()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ease.Freeze();
        return ease;
    }
}
