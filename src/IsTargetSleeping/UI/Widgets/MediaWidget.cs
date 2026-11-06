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
    private MediaPlayer? previewPlayer;
    private bool started;
    private bool launchersLoaded;
    private bool previewOpenRequested;
    private bool previewOpened;
    private bool playWhenPreviewOpens;
    private bool loopPreview;
    private bool fallbackPreview;
    private TimeSpan? seekWhenPreviewOpens;
    private string artKey = "";
    private TimeSpan basePosition, duration;
    private DateTime baseAt;
    private IReadOnlyList<MediaLauncher> launchers = [];
    private static readonly TimeSpan PreviewDuration = TimeSpan.FromSeconds(48);

    // MARK: letras sincronizadas (lrclib.net), solo si el usuario las activó
    private readonly LyricsService lyricsService = new();
    private CancellationTokenSource? lyricCancel;
    private string lyricKey = "";
    private DateTime lyricRetryAt;
    private string lyricTrack = "";   // título y artista de la canción cuya letra se busca
    private int lyricAttempts;        // intentos fallidos o sin resultado con esta canción
    /// Dice si las letras están activadas (Ajustes); sin ello no se manda nada a internet.
    public Func<bool>? LyricsEnabled { get; set; }
    /// La vista de letras del notch está abierta (el notch ancho con la portada a un lado y la letra al otro).
    public bool LyricsOpen { get; private set; }
    public ICommand ToggleLyricsView { get; set; } = null!;
    public string LyricsHelp => tr("Letras");
    public void SetLyricsOpen(bool open) { if (LyricsOpen == open) return; LyricsOpen = open; Raise(); }
    public LyricsStatus LyricStatus { get; private set; } = LyricsStatus.Off;
    public IReadOnlyList<LyricLine> LyricLines { get; private set; } = [];
    /// El momento de la canción ahora mismo (el de la barra de progreso).
    public TimeSpan Position
    {
        get
        {
            var now = DateTime.Now;
            var position = fallbackPreview && previewOpened && IsPlaying ? previewPlayer!.Position : IsPlaying ? basePosition + (now - baseAt) : basePosition;
            return position > duration && duration > TimeSpan.Zero ? duration : position;
        }
    }
    /// La línea que se canta ahora (-1 antes de la primera o sin letra).
    public int LyricIndex => LyricStatus == LyricsStatus.Ready ? LyricsTimeline.CurrentIndex(LyricLines, Position) : -1;
    public string CurrentLyric => LyricIndex is >= 0 and var i && i < LyricLines.Count ? LyricLines[i].Text : "";

    /// Busca la letra de la canción que suena (si cambió, o `force`) y la deja lista; apaga todo si las letras están desactivadas o no hay canción.
    public void RefreshLyrics(bool force = false)
    {
        bool want = LyricsEnabled?.Invoke() == true && HasSession && !fallbackPreview && MediaTitle.Length > 0 && Artist.Length > 0;
        if (!want)
        {
            lyricCancel?.Cancel();
            lyricKey = "";
            if (LyricStatus != LyricsStatus.Off || LyricLines.Count > 0) { LyricStatus = LyricsStatus.Off; LyricLines = []; Raise(); }
            return;
        }
        // Otra canción: se empieza de cero. Antes la espera de un fallo de la canción anterior (30 s) dejaba sin buscar la letra de la siguiente.
        var track = $"{MediaTitle}|{Artist}";
        if (track != lyricTrack) (lyricTrack, lyricAttempts, lyricRetryAt) = (track, 0, default);
        if (!force && DateTime.Now < lyricRetryAt) return;
        var key = LyricsTimeline.Key(MediaTitle, Artist, (int)duration.TotalSeconds);
        if (!force && key == lyricKey) return;
        lyricKey = key;
        lyricCancel?.Cancel();
        var cancel = lyricCancel = new CancellationTokenSource();
        if (lyricAttempts == 0) { (LyricStatus, LyricLines) = (LyricsStatus.Loading, []); Raise(); }   // en los reintentos se queda el «sin letra» (sin parpadear a «cargando»)
        string title = MediaTitle, artist = Artist, album = ArtistLine.StartsWith(Artist + " - ") ? ArtistLine[(Artist.Length + 3)..] : "";
        var length = duration;
        bool retry = force || lyricAttempts > 0;   // los reintentos no se fían de un «sin letra» guardado
        _ = Task.Run(async () =>
        {
            List<LyricLine>? lines = null;
            bool failed = false;
            try { lines = await lyricsService.GetAsync(title, artist, album, length, retry, cancel.Token); }
            catch (OperationCanceledException) { return; }
            catch (Exception) { failed = true; }   // red caída o servidor ocupado
            OnUi(() =>
            {
                if (lyricKey != key) return;
                if (lines is { Count: > 0 }) { (LyricLines, LyricStatus) = (lines, LyricsStatus.Ready); Raise(); return; }
                // Un fallo de red o del servidor no es «sin letra»: se sigue en «Cargando letra…» y se reintenta enseguida (2, 4, 8 y 12 s); solo si se agotan se dice que no hay.
                if (failed && lyricAttempts < 4)
                {
                    lyricRetryAt = DateTime.Now.AddSeconds(new[] { 2, 4, 8, 12 }[lyricAttempts++]);
                    lyricKey = "";   // para que el próximo intento no se salte por «ya la busqué»
                    return;
                }
                (LyricLines, LyricStatus) = ([], LyricsStatus.None);
                // Si el servidor respondió que no hay, se enseña ya, pero se mira un par de veces más en silencio por si los metadatos venían a medias.
                if (!failed && lyricAttempts < 2)
                {
                    lyricRetryAt = DateTime.Now.AddSeconds(lyricAttempts == 0 ? 6 : 15);
                    lyricAttempts++;
                    lyricKey = "";
                }
                Raise();
            });
        });
    }

    public MediaWidget() : base(WidgetCatalog.Find("media")!)
    {
        Toggle = new Command(TogglePlayback);
        ToggleMute = new Command(() => Volume.ToggleMute());
        Next = new Command(() => { var s = session; if (s is not null) _ = s.TrySkipNextAsync(); });
        Previous = new Command(() => { var s = session; if (s is not null) _ = s.TrySkipPreviousAsync(); });
        Seek = new ParamCommand(p => SeekTo(p is double fraction ? fraction : 0));
        ToggleShuffle = new Command(() => { var s = session; if (s is not null) _ = s.TryChangeShuffleActiveAsync(!IsShuffle); });
        CycleRepeat = new Command(() =>
        {
            if (fallbackPreview)
            {
                loopPreview = !loopPreview;
                Repeat = loopPreview ? MediaPlaybackAutoRepeatMode.List : MediaPlaybackAutoRepeatMode.None;
                Raise();
                return;
            }
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
    public bool CanSkip { get; private set; }
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
    /// El volumen general de Windows, para el botón del altavoz de la vista corta: un toque silencia o activa.
    public AudioVolume Volume { get; } = new();
    public ICommand ToggleMute { get; }
    public string VolumeGlyph => Volume.Muted || Volume.Level <= 0 ? "" : Volume.Level < 0.34 ? "" : Volume.Level < 0.67 ? "" : "";
    public string VolumeHelp => Volume.Muted ? tr("Activar el sonido") : tr("Silenciar");
    public ICommand ToggleShuffle { get; }
    public ICommand CycleRepeat { get; }
    public string ShuffleHelp => tr("Aleatorio");
    public string RepeatHelp => fallbackPreview
        ? tr(loopPreview ? "Desactivar bucle" : "Activar bucle")
        : Repeat switch { MediaPlaybackAutoRepeatMode.List => tr("Repetir todo"), MediaPlaybackAutoRepeatMode.Track => tr("Repetir esta canción"), _ => tr("Repetir") };
    /// Lo que falta para que acabe, con un menos delante («-4:45»), como en el reproductor de Droppy.
    public string RemainingText { get; private set; } = "-0:00";

    public bool HasSession { get; private set; }
    public bool NoSession => !HasSession;
    public bool IsPlaying { get; private set; }
    public bool IsPaused => !IsPlaying;
    public bool IsFallbackPreview => fallbackPreview;
    public bool LaunchersLoaded => launchersLoaded;
    public bool HasLaunchers => launchers.Count > 0;
    public IReadOnlyList<MediaLauncher> Launchers => launchers;
    public string LaunchersTitle => tr("Abrir música");
    public string LaunchersHint => tr("Reproductores instalados");
    public string MediaTitle { get; private set; } = "";
    public string Artist { get; private set; } = "";
    /// «Artista - Álbum», como en la vista corta de Nusa (solo el artista si no hay álbum o es el mismo título).
    public string ArtistLine { get; private set; } = "";
    public double Progress { get; private set; }
    public ImageSource? Art { get; private set; }
    /// Un velo del color dominante de la portada para el fondo de la tarjeta (transparente sin portada).
    public Brush Tint { get; private set; } = Brushes.Transparent;

    /// El fondo de la vista de letras, como en Nusa: negro hasta casi la mitad y, de ahí al borde de abajo, el tono hondo de la portada, todo opaco (sin dejar pasar el fondo).
    public Brush Glow
    {
        get
        {
            var c = Tint is LinearGradientBrush { GradientStops.Count: > 0 } g ? g.GradientStops[0].Color : Colors.Transparent;
            if (c.A == 0) return Brushes.Transparent;
            var (r, gr, b) = ArtColor.Deep((c.R, c.G, c.B));
            Color Mix(double k) => Color.FromRgb((byte)(r * k), (byte)(gr * k), (byte)(b * k));
            var brush = new LinearGradientBrush([new GradientStop(Colors.Black, 0), new GradientStop(Colors.Black, 0.45), new GradientStop(Mix(0.35), 0.72), new GradientStop(Mix(1), 1)], new Point(0, 0), new Point(0, 1));
            brush.Freeze();
            return brush;
        }
    }
    public string SourceApp { get; private set; } = "";
    public string ElapsedText { get; private set; } = "0:00";
    /// Los tiempos con dos cifras («01:24», «-02:29»), como en la vista corta de un widget.
    public string ElapsedPadded { get; private set; } = "00:00";
    public string RemainingPadded { get; private set; } = "-00:00";
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

    /// Datos de muestra para la galería de widgets (nunca se mezclan con la sesión de verdad: es otra instancia).
    public void LoadDemo()
    {
        StopPreviewPlayer();
        fallbackPreview = false;
        accentColor = Color.FromRgb(0x59, 0xB4, 0xFF);
        AccentBrush = Frozen(accentColor);
        HasSession = true; IsPlaying = true; CanShuffle = true; CanRepeat = true; CanSkip = true; IsShuffle = true;
        MediaTitle = "Midnight City"; Artist = ArtistLine = "M83"; SourceApp = "Spotify";
        Progress = 0.4; ElapsedText = "1:37"; TotalText = "4:03"; RemainingText = "-2:26"; ElapsedPadded = "01:37"; RemainingPadded = "-02:26";
        Art = DemoArtwork();
        Raise();
    }

    /// La pista original, pausada, que sirve de preview interactivo cuando no hay un reproductor musical instalado.
    public void LoadIdlePreview()
    {
        StopPreviewPlayer();
        fallbackPreview = true;
        loopPreview = false;
        Repeat = MediaPlaybackAutoRepeatMode.None;
        HasSession = true; IsPlaying = false; CanShuffle = false; CanRepeat = true; CanSkip = false; IsShuffle = false;
        MediaTitle = tr("Una noche tranquila en Basin Street"); Artist = ArtistLine = tr("Jazz lento de Nueva Orleans"); SourceApp = tr("Vista previa");
        Art = DemoArtwork();
        (Tint, AccentBrush) = Art is BitmapSource bitmap ? Analyze(bitmap) : (Brushes.Transparent, Frozen(Theme.Current.Fg));
        accentColor = ((SolidColorBrush)AccentBrush).Color;
        duration = PreviewDuration; basePosition = TimeSpan.Zero; baseAt = DateTime.Now;
        UpdateProgress(DateTime.Now);
        Raise();
    }

    private static BitmapImage DemoArtwork()
    {
        var art = new BitmapImage(new Uri("pack://application:,,,/isTargetSleeping;component/Resources/music-demo-cover.jpg", UriKind.Absolute));
        art.Freeze();
        return art;
    }

    /// Empieza a leer la sesión de medios del sistema (una sola vez).
    public async void Start()
    {
        if (started) return;
        started = true;
        Volume.Changed += Raise;
        Volume.Start();
        var scan = Task.Run(MediaLaunchers.Find);
        try
        {
            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            manager.CurrentSessionChanged += (_, _) => OnUi(Attach);
        }
        catch (Exception e)
        {
            AppLog.Write($"multimedia: {e.Message}");
        }
        IReadOnlyList<MediaLauncher> found;
        try { found = await scan; }
        catch (Exception e) { AppLog.Write($"multimedia: no se pudieron buscar reproductores: {e.Message}"); found = []; }
        OnUi(() =>
        {
            launchers = found;
            launchersLoaded = true;
            Raise();
            Attach();
        });
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
            if (fallbackPreview) { StopPreviewPlayer(); fallbackPreview = false; }
            session.MediaPropertiesChanged += OnChanged;
            session.PlaybackInfoChanged += OnChanged;
            session.TimelinePropertiesChanged += OnChanged;
        }
        Refresh();
    }

    private void OnChanged(GlobalSystemMediaTransportControlsSession sender, object args) => OnUi(Refresh);

    private async void Refresh()
    {
        // Lo que se pinta es de la interfaz: si un evento de Windows llega por otro hilo, se reenvía (antes fallaba cada segundo).
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess()) { dispatcher.BeginInvoke(new Action(Refresh)); return; }
        try
        {
            var s = session;
            if (s is null)
            {
                ShowIdle();
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
            var album = props.AlbumTitle ?? "";
            ArtistLine = album.Length > 0 && !string.Equals(album, MediaTitle, StringComparison.OrdinalIgnoreCase) && Artist.Length > 0 ? $"{Artist} - {album}" : Artist;
            HasSession = MediaTitle.Length > 0;
            CanSkip = true;
            if (!HasSession) { ShowIdle(); return; }
            if (fallbackPreview) { StopPreviewPlayer(); fallbackPreview = false; }
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
            RefreshLyrics();
            Raise();
        }
        catch (Exception e) { AppLog.Write($"multimedia: {e.Message}"); }
    }

    private void ShowIdle()
    {
        if (!launchersLoaded) { HasSession = false; RefreshLyrics(); Raise(); return; }
        if (launchers.Count == 0)
        {
            if (!fallbackPreview) LoadIdlePreview();
            return;
        }
        StopPreviewPlayer();
        fallbackPreview = false;
        (HasSession, IsPlaying, CanShuffle, CanRepeat, CanSkip, IsShuffle, MediaTitle, Artist, SourceApp, Progress, Art, artKey, Tint) =
            (false, false, false, false, false, false, "", "", "", 0, null, "", Brushes.Transparent);
        duration = basePosition = TimeSpan.Zero;
        Raise();
    }

    private void TogglePlayback()
    {
        if (session is { } s) { _ = s.TryTogglePlayPauseAsync(); return; }
        if (!fallbackPreview) return;
        if (IsPlaying)
        {
            previewPlayer?.Pause();
            if (previewOpened) basePosition = previewPlayer!.Position;
            playWhenPreviewOpens = false;
            IsPlaying = false;
            baseAt = DateTime.Now;
        }
        else StartPreview();
        UpdateProgress(DateTime.Now);
        Raise();
    }

    private void StartPreview()
    {
        if (!previewOpened)
        {
            EnsurePreviewPlayer();
            if (!previewOpenRequested) { IsPlaying = false; return; }
            playWhenPreviewOpens = true;
            IsPlaying = true;
            baseAt = DateTime.Now;
            return;
        }
        if (basePosition >= duration) basePosition = TimeSpan.Zero;
        previewPlayer!.Position = basePosition;
        previewPlayer.Play();
        playWhenPreviewOpens = false;
        IsPlaying = true;
        baseAt = DateTime.Now;
    }

    private void EnsurePreviewPlayer()
    {
        if (previewOpenRequested) return;
        try
        {
            previewPlayer = new MediaPlayer { Volume = 0.58 };
            previewPlayer.MediaOpened += OnPreviewOpened;
            previewPlayer.MediaEnded += OnPreviewEnded;
            previewOpenRequested = true;
            previewPlayer.Open(new Uri(ExtractPreviewAudio(), UriKind.Absolute));
        }
        catch (Exception e)
        {
            previewOpenRequested = false;
            playWhenPreviewOpens = false;
            IsPlaying = false;
            try { previewPlayer?.Close(); } catch { }
            previewPlayer = null;
            AppLog.Write($"multimedia: no se pudo abrir la pista de muestra: {e.Message}");
        }
    }

    private static string ExtractPreviewAudio()
    {
        using var source = typeof(MediaWidget).Assembly.GetManifestResourceStream("IsTargetSleeping.Resources.music-new-orleans-jazz.wav")
            ?? throw new FileNotFoundException("No está la pista de muestra integrada.");
        var directory = Path.Combine(Path.GetTempPath(), "isTargetSleeping");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "music-new-orleans-jazz-v1.wav");
        if (File.Exists(path) && new FileInfo(path).Length == source.Length) return path;
        var temporary = path + ".tmp";
        using (var output = File.Create(temporary)) source.CopyTo(output);
        File.Move(temporary, path, true);
        return path;
    }

    private void OnPreviewOpened(object? sender, EventArgs e) => OnUi(() =>
    {
        if (!ReferenceEquals(sender, previewPlayer)) return;
        previewOpened = true;
        if (previewPlayer!.NaturalDuration.HasTimeSpan) duration = previewPlayer.NaturalDuration.TimeSpan;
        if (seekWhenPreviewOpens is { } target) { previewPlayer.Position = target; seekWhenPreviewOpens = null; }
        if (playWhenPreviewOpens) StartPreview();
        UpdateProgress(DateTime.Now);
        Raise();
    });

    private void OnPreviewEnded(object? sender, EventArgs e) => OnUi(() =>
    {
        if (!ReferenceEquals(sender, previewPlayer) || !fallbackPreview) return;
        if (loopPreview)
        {
            previewPlayer!.Position = TimeSpan.Zero;
            previewPlayer.Play();
            basePosition = TimeSpan.Zero;
            baseAt = DateTime.Now;
            IsPlaying = true;
        }
        else
        {
            basePosition = duration;
            baseAt = DateTime.Now;
            IsPlaying = false;
        }
        UpdateProgress(DateTime.Now);
        Raise();
    });

    private void StopPreviewPlayer()
    {
        var player = previewPlayer;
        previewPlayer = null;
        if (player is not null)
        {
            player.MediaOpened -= OnPreviewOpened;
            player.MediaEnded -= OnPreviewEnded;
            try { player.Stop(); player.Close(); } catch { }
        }
        previewOpenRequested = previewOpened = playWhenPreviewOpens = false;
        seekWhenPreviewOpens = null;
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

    private static string ClockPadded(TimeSpan span) => $"{(int)span.TotalMinutes:00}:{span.Seconds:00}";
    private static string Clock(TimeSpan span) => $"{(int)span.TotalMinutes}:{span.Seconds:00}";

    private void UpdateProgress(DateTime now)
    {
        var position = fallbackPreview && previewOpened && IsPlaying ? previewPlayer!.Position : IsPlaying ? basePosition + (now - baseAt) : basePosition;
        if (position > duration) position = duration;
        if (position < TimeSpan.Zero) position = TimeSpan.Zero;
        Progress = duration.TotalSeconds > 1 ? Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0, 1) : 0;
        (ElapsedText, TotalText) = (Clock(position), Clock(duration < TimeSpan.Zero ? TimeSpan.Zero : duration));
        ElapsedPadded = ClockPadded(position);
        RemainingPadded = "-" + ClockPadded(duration > position ? duration - position : TimeSpan.Zero);
        RemainingText = "-" + Clock(duration > position ? duration - position : TimeSpan.Zero);
    }

    /// Tocar una línea de la letra lleva la canción a ese momento.
    public void SeekToLyric(int index)
    {
        if (index < 0 || index >= LyricLines.Count || duration.TotalSeconds <= 1) return;
        SeekTo(LyricLines[index].Time.TotalSeconds / duration.TotalSeconds);
    }

    private void SeekTo(double fraction)
    {
        if (duration.TotalSeconds <= 1) return;
        var target = TimeSpan.FromTicks((long)(Math.Clamp(fraction, 0, 1) * duration.Ticks));
        if (fallbackPreview)
        {
            if (previewOpened) previewPlayer!.Position = target;
            else { seekWhenPreviewOpens = target; EnsurePreviewPlayer(); }
            (basePosition, baseAt) = (target, DateTime.Now);
            UpdateProgress(DateTime.Now);
            Raise();
            return;
        }
        var s = session;
        if (s is null) return;
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
