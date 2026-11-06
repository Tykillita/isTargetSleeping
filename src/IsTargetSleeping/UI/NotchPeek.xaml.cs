using System.Windows.Controls;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace IsTargetSleeping.UI;

/// Lo que se ve al pasar el ratón por el notch: el resumen corto por páginas (el botón del mini dock abre el panel entero).
public partial class NotchPeek : System.Windows.Controls.UserControl
{
    private readonly PanelViewModel model;
    private readonly DispatcherTimer settle = new() { Interval = TimeSpan.FromMilliseconds(70) };
    private readonly DispatcherTimer headerClock = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool dockOpen;
    private int dockTransition;
    private readonly TranslateTransform PageShift = new();
    public event Action? DockLayoutChanged;

    private readonly TranslateTransform OldShift = new();
    private bool Animates => !model.Prefs.ReducedMotion && IsVisible && pageHost.ActualWidth > 0 && pageHost.ActualHeight > 0;

    /// Antes del cambio: foto de la página que sale, puesta encima, para que se vea salir y no desaparecer de golpe.
    private void CaptureOldPage()
    {
        oldPage.BeginAnimation(OpacityProperty, null);
        oldPage.Visibility = Visibility.Collapsed;
        if (!Animates)
        {
            PageShift.BeginAnimation(TranslateTransform.XProperty, null);
            pageHost.BeginAnimation(OpacityProperty, null);
            pageHost.ClearValue(CacheModeProperty);
            oldPage.Source = null;
            return;
        }
        // Una pulsación rápida fotografía el widget completo, sin heredar la opacidad de la transición anterior.
        PageShift.BeginAnimation(TranslateTransform.XProperty, null);
        pageHost.BeginAnimation(OpacityProperty, null);
        var dpi = VisualTreeHelper.GetDpi(this);
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(top.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(top.ActualHeight * dpi.DpiScaleY),
            96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bmp.Render(pageHost);
        oldPage.Source = bmp;
        oldPage.Width = top.ActualWidth;
        oldPage.Height = top.ActualHeight;
        oldPage.Opacity = 1;
        oldPage.Visibility = Visibility.Visible;
    }

    /// Cambio de página: la que sale se desliza y se funde hacia el lado contrario mientras la nueva entra desde el lado al que vas.
    private void SlidePage(int direction)
    {
        if (oldPage.Visibility != Visibility.Visible) return;
        double speed = model.Prefs.AnimSpeed;
        Motion.Busy(TimeSpan.FromMilliseconds(450 * speed));   // las lecturas de los widgets esperan hasta que termine el movimiento
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        // En el registro salían 17-39 fotogramas en 700 ms (a tirones): la página se vuelve una textura mientras se mueve, y así cada fotograma
        // solo la desplaza y la funde, sin repintar sus widgets. Se quita al terminar para que el clima, la música, etc. sigan animándose.
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        pageHost.CacheMode = new BitmapCache(dpi);
        var enter = new DoubleAnimation(direction * 56, 0, TimeSpan.FromMilliseconds(400 * speed)) { EasingFunction = ease };
        enter.Completed += (_, _) => pageHost.ClearValue(CacheModeProperty);
        PageShift.BeginAnimation(TranslateTransform.XProperty, enter);
        pageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400 * speed)) { EasingFunction = ease });
        OldShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -direction * 56, TimeSpan.FromMilliseconds(400 * speed)) { EasingFunction = ease });
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400 * speed)) { EasingFunction = ease };
        fade.Completed += (_, _) => { if (oldPage.Opacity == 0) { oldPage.Visibility = Visibility.Collapsed; oldPage.Source = null; } };
        oldPage.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
    }

    public NotchPeek(PanelViewModel model)
    {
        this.model = model;
        InitializeComponent();
        DataContext = model;
        pageHost.RenderTransform = PageShift;
        oldPage.RenderTransform = OldShift;
        model.PeekPageChanging += CaptureOldPage;
        model.PeekPageChanged += SlidePage;
        PreviewMouseWheel += (_, e) => { if (e.Delta != 0) { model.StepPeekPage(e.Delta < 0 ? 1 : -1); e.Handled = true; } };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Left or Key.Right)) return;
            model.StepPeekPage(e.Key == Key.Right ? 1 : -1, wrap: true);
            e.Handled = true;
        };
        // Los puntos y el dock comparten el área de hover, aunque el notch cambie de alto.
        settle.Tick += (_, _) => { settle.Stop(); SetDock(footerHover || peekFooter.IsMouseOver); };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { headerClock.Start(); UpdateHeader(); }
            else { footerHover = false; SetDock(false); headerClock.Stop(); PauseHeaderAnimations(); }
        };
        headerRing.ChargingBrush = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
        headerClock.Tick += (_, _) => UpdateHeader();
        if (IsVisible) headerClock.Start();
        UpdateHeader();
    }

    public void Stop()
    {
        model.PeekPageChanging -= CaptureOldPage;
        model.PeekPageChanged -= SlidePage;
        settle.Stop();
        headerClock.Stop();
        PauseHeaderAnimations();
    }

    private void PauseHeaderAnimations()
    {
        if (bellOn) ((System.Windows.Media.RotateTransform)bellBody.RenderTransform).BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
        SetPomodoro(false);
        SetEq(false);
    }

    /// La cabecera de la vista corta: batería, fecha y hora (con AM/PM pequeño).
    private void UpdateHeader()
    {
        if (!IsVisible) return;
        var now = DateTime.Now;
        var culture = System.Globalization.CultureInfo.GetCultureInfo(L10n.Effective == "es" ? "es-ES" : "en-US");
        headerDate.Text = now.ToString("ddd d MMM yyyy", culture).Replace(".", "");
        headerTime.Inlines.Clear();
        headerTime.Inlines.Add(new System.Windows.Documents.Run(now.ToString("h:mm", culture)));
        headerTime.Inlines.Add(new System.Windows.Documents.Run(" " + now.ToString("tt", System.Globalization.CultureInfo.InvariantCulture)) { FontSize = 10, FontWeight = FontWeights.SemiBold });
        bool showLiveIndicator = model.ShowPeekHeader && !model.PeekWidgets.Any(widget => WidgetLayout.BaseId(widget.Id) == "shelf");
        bool unread = showLiveIndicator && model.UnreadNotice;
        SetBell(unread);
        bool pomo = showLiveIndicator && !unread && model.Hub.Pomodoro.Running;
        SetPomodoro(pomo);
        SetEq(showLiveIndicator && !unread && !pomo && model.Hub.Media.IsPlaying && model.Hub.Media.HasSession);
        var battery = model.Hub.BatteryInfo;
        headerRing.Visibility = battery.HasBattery ? Visibility.Visible : Visibility.Collapsed;
        headerRing.Fraction = battery.Fraction;
        headerRing.Charging = battery.Charging;
    }

    private bool bellOn;
    private DateTime? bellStamp;
    /// La campana de la cabecera: se balancea unos 0,8 s y descansa 0,9 s mientras haya un aviso sin ver.
    private void SetBell(bool on)
    {
        // Un aviso nuevo vuelve a hacerla vibrar; si no, tras tres balanceos queda quieta con su punto rojo.
        if (on == bellOn && (!on || bellStamp == model.NoticeStamp)) return;
        bellOn = on;
        bellStamp = model.NoticeStamp;
        headerBell.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        var swing = (System.Windows.Media.RotateTransform)bellBody.RenderTransform;
        if (!on || model.Prefs.ReducedMotion) { swing.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null); return; }
        var rock = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1700), RepeatBehavior = new RepeatBehavior(3) };
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        foreach (var (at, angle) in new[] { (0, 0.0), (90, -17.0), (210, 17.0), (330, -13.0), (450, 13.0), (570, -8.0), (690, 8.0), (800, 0.0), (1700, 0.0) })
            rock.KeyFrames.Add(new EasingDoubleKeyFrame(angle, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(at))) { EasingFunction = ease });
        swing.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, rock);
    }

    private bool sandMoving;
    /// El disco del Pomodoro: el nivel de la arena sube con el progreso de la fase (del 45 % al 85 %) y las dos olas corren sin parar mientras cuenta.
    private void SetPomodoro(bool on)
    {
        headerPomo.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        bool move = on && !model.Prefs.ReducedMotion;
        if (on)
        {
            double y = 19 * (1 - (0.45 + 0.4 * Math.Clamp(model.Hub.Pomodoro.Progress, 0, 1)));
            Canvas.SetTop(pomoWaveBack, y);
            Canvas.SetTop(pomoWaveFront, y + 1.5);
        }
        if (move == sandMoving) return;
        sandMoving = move;
        var back = (TranslateTransform)pomoWaveBack.RenderTransform;
        var front = (TransformGroup)pomoWaveFront.RenderTransform;
        var slosh = (ScaleTransform)front.Children[0];
        var shift = (TranslateTransform)front.Children[1];
        if (!move)
        {
            back.BeginAnimation(TranslateTransform.XProperty, null);
            shift.BeginAnimation(TranslateTransform.XProperty, null);
            slosh.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            return;
        }
        back.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -19, TimeSpan.FromMilliseconds(2600)) { RepeatBehavior = RepeatBehavior.Forever });
        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-19, 0, TimeSpan.FromMilliseconds(1900)) { RepeatBehavior = RepeatBehavior.Forever });
        slosh.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.5, 1.6, TimeSpan.FromMilliseconds(1300))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
    }

    private bool eqOn;
    /// El ecualizador de la cabecera: cuatro barras gruesas que suben y bajan a ritmos distintos (las de la pastilla cerrada) mientras suena la música.
    private void SetEq(bool on)
    {
        if (on == eqOn) return;
        eqOn = on;
        headerEq.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        var bars = new[] { eq1, eq2, eq3, eq4 };
        (double From, double To, double Ms)[] timings = [(0.3, 1, 450), (1, 0.3, 600), (0.45, 0.95, 350), (0.85, 0.3, 520)];
        for (int i = 0; i < bars.Length; i++)
        {
            var scale = (System.Windows.Media.ScaleTransform)bars[i].RenderTransform;
            if (!on || model.Prefs.ReducedMotion) { scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null); scale.ScaleY = 0.7; continue; }
            scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new DoubleAnimation(timings[i].From, timings[i].To, TimeSpan.FromMilliseconds(timings[i].Ms))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        }
    }

    /// El alto mínimo del contenido: el mismo en todas las páginas; los puntos y el mini dock van debajo.
    public void SetContentMinHeight(double height) => top.MinHeight = Math.Max(60, height);

    private readonly System.Windows.Media.TranslateTransform dockSlide = new();

    /// Al pasar el ratón por los puntos, el notch crece por debajo y salen los iconos de las páginas y Ajustes.
    private void SetDock(bool on)
    {
        if (on == dockOpen) return;
        dockOpen = on;
        int transition = ++dockTransition;
        dots.IsHitTestVisible = !on;
        if (model.Prefs.ReducedMotion)
        {
            dots.BeginAnimation(OpacityProperty, null);
            dockRow.BeginAnimation(OpacityProperty, null);
            dots.Opacity = on ? 0 : 1;
            dockRow.Opacity = on ? 1 : 0;
            dockRow.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            DockLayoutChanged?.Invoke();
            return;
        }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        // Rápido: el borde de abajo y los iconos se mueven a la vez (200 ms), no uno tras otro. Los puntos se apagan en 90 ms y los iconos entran sin espera.
        dots.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 0 : 1, TimeSpan.FromMilliseconds(on ? 90 : 120)) { EasingFunction = ease });
        if (on)
        {
            dockRow.BeginAnimation(OpacityProperty, null);
            dockRow.Opacity = 0;
            dockRow.Visibility = Visibility.Visible;
            DockLayoutChanged?.Invoke();
            // Sube desde 6 px más abajo mientras aparece, acompañando al borde que baja.
            dockRow.RenderTransform = dockSlide;
            dockSlide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(190)) { EasingFunction = ease });
            dockRow.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(130)) { BeginTime = TimeSpan.FromMilliseconds(20), EasingFunction = ease });
        }
        else
        {
            // Los iconos se van en 80 ms y justo después el borde sube (antes eran 150 ms de fundido y 320 de resorte: se sentía lento).
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(80)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fade.Completed += (_, _) =>
            {
                if (transition != dockTransition || dockOpen) return;
                dockRow.Visibility = Visibility.Collapsed;
                dockRow.BeginAnimation(OpacityProperty, null);
                dockRow.Opacity = 0;
                DockLayoutChanged?.Invoke();
            };
            dockRow.BeginAnimation(OpacityProperty, fade);
        }
    }


    private bool footerHover;

    /// El cursor, en píxeles de pantalla, lo da el vigilante global del notch en cada movimiento. MouseEnter/MouseLeave de WPF no bastan: si el notch crece o se mueve
    /// bajo un cursor quieto, o la ventana no recibe el movimiento, no llegan y el mini dock tardaba en salir (o no salía hasta mover el ratón arriba y abajo).
    /// Aquí se mide el pie a mano, con un margen de 8 px por arriba para que no haya que acertar en los 14 px de los puntos.
    public void TrackCursor(int x, int y)
    {
        if (!IsVisible || !peekFooter.IsVisible || PresentationSource.FromVisual(peekFooter) is not { } source) { footerHover = false; return; }
        var topLeft = peekFooter.PointToScreen(new Point(0, 0));
        var toDevice = source.CompositionTarget.TransformToDevice;
        double pad = 8 * toDevice.M22, width = peekFooter.ActualWidth * toDevice.M11, height = peekFooter.ActualHeight * toDevice.M22;
        bool hover = x >= topLeft.X && x <= topLeft.X + width && y >= topLeft.Y - pad && y <= topLeft.Y + height + (dockOpen ? pad : 0);
        if (hover)
        {
            footerHover = true;
            settle.Stop();
            if (!dockOpen) SetDock(true);
        }
        else if (footerHover)
        {
            footerHover = false;
            settle.Stop();
            settle.Start();
        }
    }

    private void OnFooterEnter(object sender, MouseEventArgs e) { settle.Stop(); SetDock(true); }
    private void OnFooterLeave(object sender, MouseEventArgs e) { settle.Stop(); settle.Start(); }

    private void OnPrev(object sender, RoutedEventArgs e) => model.StepPeekPage(-1, wrap: true);
    private void OnNext(object sender, RoutedEventArgs e) => model.StepPeekPage(1, wrap: true);
}
