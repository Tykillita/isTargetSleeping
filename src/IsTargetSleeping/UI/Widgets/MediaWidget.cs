using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
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
    }

    public bool HasSession { get; private set; }
    public bool NoSession => !HasSession;
    public bool IsPlaying { get; private set; }
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
            IsPlaying = s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var props = await s.TryGetMediaPropertiesAsync();
            if (!ReferenceEquals(s, session)) return;   // la sesión cambió mientras esperaba
            (MediaTitle, Artist) = (props.Title ?? "", props.Artist ?? "");
            HasSession = MediaTitle.Length > 0;
            SourceApp = AppName(s.SourceAppUserModelId);
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
                Tint = Art is BitmapSource bitmap ? MakeTint(bitmap) : Brushes.Transparent;
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

    /// El color dominante de la portada (los píxeles más vivos pesan más que los grises) como degradado suave de arriba a la izquierda.
    private static Brush MakeTint(BitmapSource bitmap)
    {
        try
        {
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            int width = converted.PixelWidth, height = converted.PixelHeight, stride = width * 4;
            var pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);
            double r = 0, g = 0, b = 0, total = 0;
            for (int i = 0; i + 3 < pixels.Length; i += 4)
            {
                double pb = pixels[i], pg = pixels[i + 1], pr = pixels[i + 2];
                double max = Math.Max(pr, Math.Max(pg, pb)), min = Math.Min(pr, Math.Min(pg, pb));
                double weight = 0.1 + (max - min) / 255.0 * (max / 255.0);   // vivo y claro pesa más
                (r, g, b, total) = (r + pr * weight, g + pg * weight, b + pb * weight, total + weight);
            }
            if (total <= 0) return Brushes.Transparent;
            var color = Color.FromRgb((byte)(r / total), (byte)(g / total), (byte)(b / total));
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(96, color.R, color.G, color.B), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(14, color.R, color.G, color.B), 1));
            brush.Freeze();
            return brush;
        }
        catch { return Brushes.Transparent; }
    }

    /// «SpotifyAB.SpotifyMusic_zpd…!Spotify», «chrome.exe», «Microsoft.ZuneMusic_…!Microsoft.ZuneMusic»: lo que se lee como nombre de la app.
    private static string AppName(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "";
        var name = id.Contains('!') ? id[(id.LastIndexOf('!') + 1)..] : id;
        name = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        if (name.Contains('.')) name = name[(name.LastIndexOf('.') + 1)..];
        return name.Length == 0 ? "" : char.ToUpperInvariant(name[0]) + name[1..];
    }

    private static string Clock(TimeSpan span) => $"{(int)span.TotalMinutes}:{span.Seconds:00}";

    private void UpdateProgress(DateTime now)
    {
        var position = IsPlaying ? basePosition + (now - baseAt) : basePosition;
        if (position > duration) position = duration;
        if (position < TimeSpan.Zero) position = TimeSpan.Zero;
        Progress = duration.TotalSeconds > 1 ? Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0, 1) : 0;
        (ElapsedText, TotalText) = (Clock(position), Clock(duration < TimeSpan.Zero ? TimeSpan.Zero : duration));
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
