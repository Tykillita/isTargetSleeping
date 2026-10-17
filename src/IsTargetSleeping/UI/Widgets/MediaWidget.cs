using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Media;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// Lo que suena ahora en Windows (Spotify, el navegador, el reproductor…), leído del propio sistema: título, artista, portada y controles.
/// Solo empieza a leer cuando algo lo necesita (el widget en alguna lista o la actividad activada); nada sale del PC.
public sealed class MediaWidget : WidgetModel
{
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? session;
    private bool started;
    private string artKey = "";
    private TimeSpan basePosition, duration;
    private DateTime baseAt;

    public MediaWidget() : base(WidgetCatalog.Find("media")!)
    {
        Toggle = new Command(() => { var s = session; if (s is not null) _ = s.TryTogglePlayPauseAsync(); });
        Next = new Command(() => { var s = session; if (s is not null) _ = s.TrySkipNextAsync(); });
        Previous = new Command(() => { var s = session; if (s is not null) _ = s.TrySkipPreviousAsync(); });
        Seek = new ParamCommand(p => SeekTo(p is double fraction ? fraction : 0));
        ToggleShuffle = new Command(() => { var s = session; if (s is not null) _ = s.TryChangeShuffleActiveAsync(!IsShuffle); });
        CycleRepeat = new Command(() =>
        {
            var s = session;
            if (s is null) return;
            // Nada → toda la lista → una canción → nada, como en los reproductores.
            var next = Repeat switch { MediaPlaybackAutoRepeatMode.None => MediaPlaybackAutoRepeatMode.List, MediaPlaybackAutoRepeatMode.List => MediaPlaybackAutoRepeatMode.Track, _ => MediaPlaybackAutoRepeatMode.None };
            _ = s.TryChangeAutoRepeatModeAsync(next);
        });
    }

    private static readonly Brush Dim = Frozen(Theme.WithOpacity(Colors.White, 0.5));
    private static Brush Frozen(Color color) { var b = new SolidColorBrush(color); b.Freeze(); return b; }
    private MediaPlaybackAutoRepeatMode Repeat = MediaPlaybackAutoRepeatMode.None;
    /// El color vivo de la portada: tiñe el ecualizador, la barra de progreso y los botones activos (sin portada, el blanco del tema).
    public Brush AccentBrush { get; private set; } = Frozen(Theme.Current.Fg);
    /// Mezclar y repetir solo salen si el reproductor los admite (Spotify sí; el navegador, casi nunca).
    public bool CanShuffle { get; private set; }
    public bool CanRepeat { get; private set; }
    public bool IsShuffle { get; private set; }
    private static readonly Brush Idle1 = Frozen(Color.FromArgb(0x14, 255, 255, 255)), Idle2 = Frozen(Color.FromArgb(0x30, 255, 255, 255));
    private Color accentColor = Theme.Current.Fg;
    /// Mezclar y repetir: apagados, un cuadrito de cristal con el icono tenue; encendidos, teñidos con el color de la portada.
    public Brush ShuffleBrush => IsShuffle ? AccentBrush : Dim;
    public Brush ShuffleBack => IsShuffle ? Frozen(Color.FromArgb(0x38, accentColor.R, accentColor.G, accentColor.B)) : Idle1;
    public Brush ShuffleRim => IsShuffle ? Frozen(Color.FromArgb(0x70, accentColor.R, accentColor.G, accentColor.B)) : Idle2;
    public Brush RepeatBrush => Repeat == MediaPlaybackAutoRepeatMode.None ? Dim : AccentBrush;
    public Brush RepeatBack => Repeat != MediaPlaybackAutoRepeatMode.None ? Frozen(Color.FromArgb(0x38, accentColor.R, accentColor.G, accentColor.B)) : Idle1;
    public Brush RepeatRim => Repeat != MediaPlaybackAutoRepeatMode.None ? Frozen(Color.FromArgb(0x70, accentColor.R, accentColor.G, accentColor.B)) : Idle2;
    public bool RepeatOne => Repeat == MediaPlaybackAutoRepeatMode.Track;
    public ICommand ToggleShuffle { get; }
    public ICommand CycleRepeat { get; }
    public string ShuffleHelp => tr("Aleatorio");
    public string RepeatHelp => Repeat switch { MediaPlaybackAutoRepeatMode.List => tr("Repetir todo"), MediaPlaybackAutoRepeatMode.Track => tr("Repetir esta canción"), _ => tr("Repetir") };
    /// Lo que falta para que acabe, con un menos delante («-4:45»), como en el reproductor de Droppy.
    public string RemainingText { get; private set; } = "-0:00";

    public bool HasSession { get; private set; }
    public bool NoSession => !HasSession;
    public bool IsPlaying { get; private set; }
    public bool IsPaused => !IsPlaying;
    public string MediaTitle { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public double Progress { get; private set; }
    public ImageSource? Art { get; private set; }
    /// Un velo del color dominante de la portada para el fondo de la tarjeta (transparente sin portada).
    public Brush Tint { get; private set; } = Brushes.Transparent;
    public string SourceApp { get; private set; } = "";
    public string ElapsedText { get; private set; } = "0:00";
    public string TotalText { get; private set; } = "0:00";
    public bool HasArt => Art is not null;
    public ICommand Toggle { get; }
    public ICommand Next { get; }
    public ICommand Previous { get; }
    /// Ir a un punto de la canción (fracción de 0 a 1): la barra de progreso se puede pulsar o arrastrar.
    public ICommand Seek { get; }
    public string SeekHelp => tr("Ir a otro momento de la canción");
    public string ToggleGlyph => IsPlaying ? "" : "";   // pausa / reproducir
    public string ToggleHelp => IsPlaying ? tr("Pausar") : tr("Reproducir");
    public string NextHelp => tr("Siguiente");
    public string PreviousHelp => tr("Anterior");
    public string Idle => tr("No suena nada ahora.");

    /// Empieza a leer la sesión de medios del sistema (una sola vez).
    public async void Start()
    {
        if (started) return;
        started = true;
        try
        {
            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            manager.CurrentSessionChanged += (_, _) => OnUi(Attach);
            Attach();
        }
        catch (Exception e)
        {
            AppLog.Write($"multimedia: {e.Message}");
            started = false;
        }
    }

    private static void OnUi(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);

    private void Attach()
    {
        if (session is not null)
        {
            session.MediaPropertiesChanged -= OnChanged;
            session.PlaybackInfoChanged -= OnChanged;
            session.TimelinePropertiesChanged -= OnChanged;
        }
        session = manager?.GetCurrentSession();
        if (session is not null)
        {
            session.MediaPropertiesChanged += OnChanged;
            session.PlaybackInfoChanged += OnChanged;
            session.TimelinePropertiesChanged += OnChanged;
        }
        Refresh();
    }

    private void OnChanged(GlobalSystemMediaTransportControlsSession sender, object args) => OnUi(Refresh);

    private async void Refresh()
    {
        try
        {
            var s = session;
            if (s is null)
            {
                (HasSession, IsPlaying, MediaTitle, Artist, Progress, Art, artKey, Tint) = (false, false, "", "", 0, null, "", Brushes.Transparent);
                Raise();
                return;
            }
            var info = s.GetPlaybackInfo();
            IsPlaying = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            (CanShuffle, CanRepeat) = (info.Controls.IsShuffleEnabled, info.Controls.IsRepeatEnabled);
            IsShuffle = info.IsShuffleActive == true;
            Repeat = info.AutoRepeatMode ?? MediaPlaybackAutoRepeatMode.None;
            var props = await s.TryGetMediaPropertiesAsync();
            if (!ReferenceEquals(s, session)) return;   // la sesión cambió mientras esperaba
            (MediaTitle, Artist) = (props.Title ?? "", props.Artist ?? "");
            HasSession = MediaTitle.Length > 0;
            SourceApp = MediaSource.Name(s.SourceAppUserModelId);
            var timeline = s.GetTimelineProperties();
            duration = timeline.EndTime - timeline.StartTime;
            basePosition = timeline.Position;
            baseAt = DateTime.Now - (DateTimeOffset.Now - timeline.LastUpdatedTime);
            UpdateProgress(DateTime.Now);
            var key = $"{MediaTitle}|{Artist}";
            if (key != artKey)
            {
                artKey = key;
                Art = props.Thumbnail is { } thumbnail ? await LoadArt(thumbnail) : null;
                (Tint, AccentBrush) = Art is BitmapSource bitmap ? Analyze(bitmap) : (Brushes.Transparent, Frozen(Theme.Current.Fg));
                accentColor = ((SolidColorBrush)AccentBrush).Color;
            }
            Raise();
        }
        catch (Exception e) { AppLog.Write($"multimedia: {e.Message}"); }
    }

    private static async Task<ImageSource?> LoadArt(Windows.Storage.Streams.IRandomAccessStreamReference reference)
    {
        try
        {
            using var stream = await reference.OpenReadAsync();
            var memory = new MemoryStream();
            await stream.AsStreamForRead().CopyToAsync(memory);
            memory.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 160;
            image.StreamSource = memory;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }

    /// El color dominante de la portada: un velo suave de arriba a la izquierda para el fondo de la tarjeta y su versión viva para teñir lo que suena.
    private static (Brush Tint, Brush Accent) Analyze(BitmapSource bitmap)
    {
        try
        {
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            int width = converted.PixelWidth, height = converted.PixelHeight, stride = width * 4;
            var pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);
            var (dr, dg, db) = ArtColor.Dominant(pixels);
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(96, dr, dg, db), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(14, dr, dg, db), 1));
            brush.Freeze();
            var (vr, vg, vb) = ArtColor.Vivid((dr, dg, db));
            return (brush, Frozen(Color.FromRgb(vr, vg, vb)));
        }
        catch { return (Brushes.Transparent, Frozen(Theme.Current.Fg)); }
    }

    private static string Clock(TimeSpan span) => $"{(int)span.TotalMinutes}:{span.Seconds:00}";

    private void UpdateProgress(DateTime now)
    {
        var position = IsPlaying ? basePosition + (now - baseAt) : basePosition;
        if (position > duration) position = duration;
        if (position < TimeSpan.Zero) position = TimeSpan.Zero;
        Progress = duration.TotalSeconds > 1 ? Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0, 1) : 0;
        (ElapsedText, TotalText) = (Clock(position), Clock(duration < TimeSpan.Zero ? TimeSpan.Zero : duration));
        RemainingText = "-" + Clock(duration > position ? duration - position : TimeSpan.Zero);
    }

    private void SeekTo(double fraction)
    {
        var s = session;
        if (s is null || duration.TotalSeconds <= 1) return;
        var target = TimeSpan.FromTicks((long)(Math.Clamp(fraction, 0, 1) * duration.Ticks));
        _ = s.TryChangePlaybackPositionAsync(target.Ticks);
        (basePosition, baseAt) = (target, DateTime.Now);   // la barra salta ya, sin esperar a que el reproductor lo confirme
        UpdateProgress(DateTime.Now);
        Raise();
    }

    public override void Tick(DateTime now)
    {
        if (!HasSession) return;
        UpdateProgress(now);
        Raise();
    }
}
