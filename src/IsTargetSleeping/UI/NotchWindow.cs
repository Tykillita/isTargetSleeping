using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// El dock tipo notch: una pastilla negra pegada al borde superior del monitor del cursor
/// que, al acercar el ratón, se abre y muestra el panel entero (la misma vista que la
/// bandeja). Un clic la fija (toma el foco y se puede escribir en ella); Esc o perder el
/// foco la devuelven a pastilla. Sin acrílico: DWM no lo da en ventanas con transparencia
/// por píxel, y el negro liso es justo lo que lleva un notch.
public sealed class NotchWindow : Window
{
    /// Abajo, esquinas redondas; arriba, «orejas»: el negro se ensancha y se curva hacia fuera hasta el borde de la pantalla, como el notch de un Mac.
    private const double CornerRadiusDip = 26, EarDip = 40;   // 40: hombros ≈ 6,5 % del ancho del cuerpo, medidos en el video del notch de Droppy (44 px de 662)

    private readonly PanelViewModel model;
    private readonly FrameworkElement view;
    /// El negro animado: un trazo (con suavizado) con la forma del notch y, encima, el contenido recortado a su cuerpo.
    private readonly Grid shell = new() { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly System.Windows.Shapes.Path body = new() { Stretch = Stretch.None };
    /// El vidrio líquido del cuerpo: lo de detrás, desenfocado, bajo el tinte negro que se funde hacia abajo.
    private readonly NotchBackdrop backdrop = new();
    private readonly Grid backdropHost = new();   // recorta lo desenfocado con la forma del notch; la imagen es más grande que el notch abierto
    private bool glassBody;
    private double paintedLuma = -1;
    /// El canto del vidrio, como el de la lente del dock: un filo blanco fino que brilla en las esquinas de abajo y, pegado a él, un hilo de
    /// los colores de lo de detrás (refractados en el borde: fuertes en las esquinas, casi nada en medio). Nada de bandas ni franjas: se
    /// verían como un marco. Solo abajo y a los lados; el negro de arriba queda limpio.
    private readonly System.Windows.Shapes.Path bevelColor = Bevel(3.5), bevelLight = Bevel(3);
    private int bevelVersion = -1;
    private readonly Grid holder = new() { Margin = new Thickness(EarDip, 0, EarDip, 0) };
    private readonly Button pill;
    private readonly NotchPeek peek;
    /// Los botones flotantes bajo la isla: Resumen, Actividad, Ajustes y Procesos.
    /// Dos pastillas lado a lado, como en Droppy: la navegación y, al lado, los favoritos de un toque (encender, liberar RAM, procesos).
    private readonly Border dock = new() { HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
    /// Las cápsulas del dock son de cristal como el cuerpo del notch: capa de fondo desenfocado (la parte de la copia que cae bajo ellas), tinte y filo.
    private readonly List<(Border Capsule, Border Image, Border Tint, ImageBrush Brush)> dockGlass = [];
    private readonly List<(Border Back, TextBlock Glyph, PanelView? View)> tabs = [];
    private readonly List<(Border Back, TextBlock Glyph, PanelView? View, Action Click)> navTabs = [];   // los de la cápsula de navegación, en orden
    private DockLens? lens;
    /// Las actividades en vivo (limpiando, temporizador, música…) van en una burbuja aparte, a la derecha del notch cerrado, como en Droppy:
    /// el notch se queda compacto. La ventana reserva sitio a cada lado para ella (transparente, no recibe clics) para no cambiar de tamaño
    /// cuando aparece o desaparece una actividad (cambiarlo repetiría la animación de la pastilla).
    private static Border NewChip() => new()
    {
        CornerRadius = new CornerRadius(15.5), Height = 31, Padding = new Thickness(2, 0, 12, 0), Visibility = Visibility.Collapsed,
        VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0), Cursor = Cursors.Hand,
    };
    /// A la izquierda, lo de Ollama (liberando RAM, encendiendo…); a la derecha, lo demás (temporizador, música, volumen…): nunca se juntan ni tapan el notch.
    private readonly Border chip = NewChip(), chipLeft = NewChip();
    private readonly TranslateTransform chipPlace = new(), chipSlide = new(), chipLeftPlace = new(), chipLeftSlide = new();
    private const double ChipRoom = 260;
    private bool sticky;
    private Border? pinBack;
    private TextBlock? pinGlyph;
    private readonly NotchState state = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly Stopwatch clock = Stopwatch.StartNew();

    private IntPtr hwnd;
    private IntPtr monitorHandle;
    private Win32.RECT monitor, work;
    private double scale = 1;
    private NotchRects rects;
    private bool suppressed, laidOut;
    private bool hiddenByApp;
    private int ticks, generation;
    private bool? shownOpen;
    /// Lo último que se pintó: si no cambia, no se vuelve a animar (reiniciar la animación a medias la dejaría atascada).
    private (NotchMode Mode, Win32.RECT Window, Size Shell)? painted;
    private Size lastDesired, lastPeek;
    private Win32.RECT activeRect;
    private double dockWidth = NotchLayout.DockWidth;
    private string dockSignature = "";

    public NotchWindow(PanelViewModel model, FrameworkElement view)
    {
        this.model = model;
        this.view = view;
        Title = AppInfo.Name;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        UseLayoutRounding = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -10000;
        Top = -10000;
        Width = NotchLayout.PillWidth;
        Height = NotchLayout.PillHeight;
        AutomationProperties.SetName(this, AppInfo.Name);

        pill = BuildPill();
        view.VerticalAlignment = VerticalAlignment.Top;
        view.HorizontalAlignment = HorizontalAlignment.Center;
        view.Visibility = Visibility.Hidden;
        peek = new NotchPeek(model) { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Hidden };
        if (view is System.Windows.Controls.Control control) control.Background = Brushes.Transparent;   // deja ver el vidrio de abajo
        holder.Children.Add(pill);
        holder.Children.Add(peek);
        holder.Children.Add(view);
        backdrop.Element.HorizontalAlignment = HorizontalAlignment.Center;
        backdrop.Element.VerticalAlignment = VerticalAlignment.Top;
        backdropHost.Children.Add(backdrop.Element);
        shell.Children.Add(backdropHost);
        shell.Children.Add(body);
        shell.Children.Add(holder);
        shell.Children.Add(bevelColor);
        shell.Children.Add(bevelLight);
        BuildDock();
        foreach (var (bubble, row, place, slide) in new[] { (chip, activityRow, chipPlace, chipSlide), (chipLeft, activityRowLeft, chipLeftPlace, chipLeftSlide) })
        {
            bubble.Child = row;
            bubble.RenderTransform = new TransformGroup { Children = { place, slide } };
            bubble.MouseLeftButtonUp += (_, _) => Pin();
            bubble.SizeChanged += (_, _) => PlaceChip();
        }
        Content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Children = { new Grid { Children = { shell, chipLeft, chip } }, dock } };
        dock.SizeChanged += (_, _) => UpdateDockGlass();
        dock.IsVisibleChanged += (_, _) => Dispatcher.BeginInvoke(UpdateDockGlass, DispatcherPriority.Loaded);
        shell.SizeChanged += (_, e) => { PlaceChip(); UpdateDockGlass(); var shape = NotchShape(e.NewSize.Width, e.NewSize.Height); body.Data = shape; backdropHost.Clip = shape; PaintBevel(shape, e.NewSize.Height); };
        // El contenido se recorta a mano (el borde de arriba, fuera de la vista, queda recto).
        holder.SizeChanged += (_, e) => holder.Clip = new RectangleGeometry(
            new Rect(0, -CornerRadiusDip, e.NewSize.Width, e.NewSize.Height + CornerRadiusDip), CornerRadiusDip, CornerRadiusDip);
        PaintShell();
        Theme.Changed += PaintShell;

        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            // Ventana de herramienta (sin Alt+Tab ni barra de tareas) que no roba el foco al pasar el ratón.
            long ex = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
            Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, new IntPtr((ex | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE) & ~Win32.WS_EX_APPWINDOW));
        };
        timer.Tick += (_, _) => OnTick();
        // Un clic en la vista abierta la fija (sin consumirlo: el botón pulsado sigue funcionando).
        PreviewMouseLeftButtonDown += (_, e) => { if (state.Mode == NotchMode.Expanded && !OnButton(e.OriginalSource as DependencyObject)) Pin(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Collapse(); };
        Deactivated += (_, _) => { if (!sticky) Collapse(); };   // fijado abierto: perder el foco no lo cierra
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Apply, DispatcherPriority.Background);
        // Solo si el contenido cambió de tamaño de verdad: mientras la animación encoge o agranda el negro, la vista
        // también cambia de tamaño visible, y reaccionar a eso reiniciaría la animación una y otra vez.
        view.SizeChanged += (_, _) => { if (state.Mode != NotchMode.Collapsed) Dispatcher.BeginInvoke(RelayoutIfContentChanged, DispatcherPriority.Background); };
        peek.IsVisibleChanged += (_, _) => model.SetPeekVisible(peek.IsVisible);
        peek.SizeChanged += (_, _) => { if (state.Mode != NotchMode.Collapsed) Dispatcher.BeginInvoke(RelayoutIfContentChanged, DispatcherPriority.Background); };
        model.AttentionRequested += OnAttention;
        model.TrayFilesAdded += OnTrayFiles;
        // Archivos arrastrados al notch: se abre la bandeja y se pueden soltar en ella.
        // WPF solo da el arrastre al elemento bajo el cursor si ese elemento lo permite: se permite en todo lo que cubre la isla.
        AllowDrop = true;
        foreach (var part in new UIElement[] { shell, holder, peek, view, dock, pill }) part.AllowDrop = true;
        ((UIElement)Content).AllowDrop = true;
        PreviewDragEnter += OnDragOver;
        PreviewDragOver += OnDragOver;
        Drop += OnDrop;
        model.Hub.Ticked += RefreshPill;
        model.PropertyChanged += (_, _) =>
        {
            if (DockSignature() != dockSignature) RebuildDock();
            RefreshPill();
            PaintTabs();
            PaintBody();
            // La vista y la corta ya no cambian de tamaño solas (la ventana las recorta): se vuelve a medir con cada cambio.
            if (state.Mode != NotchMode.Collapsed) Dispatcher.BeginInvoke(RelayoutIfContentChanged, DispatcherPriority.Background);
        };
    }

    // MARK: ciclo de vida

    public void Start()
    {
        new WindowInteropHelper(this).EnsureHandle();
        model.WideLayout = true;
        model.Show(PanelView.Home);
        PickMonitor(Win32.MonitorFromPoint(default, 1 /* MONITOR_DEFAULTTOPRIMARY */));
        Apply();
        timer.Start();
    }

    private void OnTrayFiles() => OpenTray();

    private void OpenTray()
    {
        // Con un archivo a medio editar no se cambia de página: los archivos soltados quedan en la bandeja.
        if (!model.ShowingTray && !(model.ShowingEditor && model.EditDirty)) model.Show(PanelView.Tray);
        if (state.Mode != NotchMode.Pinned) Pin();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        model.SetTrayDragging(true);
        OpenTray();
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) model.AddToTray(files);
        e.Handled = true;
    }

    private void OnAttention()
    {
        if (state.Mode != NotchMode.Pinned) Pin();
    }

    public void Stop()
    {
        model.AttentionRequested -= OnAttention;
        model.TrayFilesAdded -= OnTrayFiles;
        model.Hub.Ticked -= RefreshPill;
        timer.Stop();
        Theme.Changed -= PaintShell;
        generation++;
        Hide();
        holder.Children.Remove(view);   // la vista vuelve al panel de la bandeja
        if (view is System.Windows.Controls.Control control) control.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "PanelTint");
        model.WideLayout = false;
        if (model.ShowingHome || model.ShowingWidgets) model.Show(PanelView.Main);   // la bandeja no tiene Home ni Widgets
    }

    /// Abrirlo y dejarlo abierto (clic en el ícono de la bandeja, un enlace, un aviso…).
    public void Reveal() => Pin();

    /// El clic en el ícono de la bandeja: abre y fija, o vuelve a la pastilla.
    public void Toggle()
    {
        if (state.Mode == NotchMode.Pinned) Collapse();
        else Pin();
    }

    public void Collapse()
    {
        if (state.Mode == NotchMode.Collapsed) return;
        sticky = false;
        SetNoActivate(true);
        state.Close();
        Apply();
    }

    private void Pin()
    {
        state.Pin();
        SetNoActivate(false);
        Apply();
        Activate();
        if (hwnd != IntPtr.Zero) Win32.SetForegroundWindow(hwnd);
    }

    // MARK: el ratón

    private void OnTick()
    {
        bool hideForApp = IsHiddenAppForeground();
        if (hideForApp != hiddenByApp)
        {
            hiddenByApp = hideForApp;
            if (hideForApp) Hide(); else Apply();
        }
        if (hideForApp) return;
        if (!Win32.GetCursorPos(out var cursor)) return;
        if (state.Mode == NotchMode.Collapsed) FollowCursorMonitor(cursor);
        if (++ticks % 12 == 1) suppressed = TaskbarLocator.Suppressed(monitor);   // ~cada 0,5 s
        bool inside = NotchLayout.Contains(state.Mode == NotchMode.Collapsed ? rects.Hot : NotchLayout.Inflate(activeRect, (int)Math.Round(14 * scale)), cursor.X, cursor.Y);
        if (state.Update(inside, suppressed, false, clock.Elapsed.TotalSeconds)) Apply();
    }

    private bool IsHiddenAppForeground()
    {
        var window = Win32.GetForegroundWindow();
        if (window == IntPtr.Zero || Win32.GetWindowThreadProcessId(window, out int pid) == 0) return false;
        try
        {
            using var process = Process.GetProcessById(pid);
            return model.Prefs.List(PrefKeys.HiddenApps).Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// Con el notch cerrado, tocar el borde superior de otro monitor lo lleva allí.
    private void FollowCursorMonitor(Win32.POINT cursor)
    {
        var handle = Win32.MonitorFromPoint(cursor, 2 /* MONITOR_DEFAULTTONEAREST */);
        if (handle == monitorHandle || handle == IntPtr.Zero) return;
        var info = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(handle, ref info)) return;
        Win32.GetDpiForMonitor(handle, 0, out uint dpi, out _);
        var there = NotchLayout.Place(info.rcMonitor, info.rcWork, (dpi == 0 ? 96 : dpi) / 96.0, view.Width, 0);
        if (!NotchLayout.Contains(there.Hot, cursor.X, cursor.Y)) return;
        PickMonitor(handle);
        shownOpen = null;
        painted = null;
        Apply();
    }

    private void PickMonitor(IntPtr handle)
    {
        var info = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
        if (handle == IntPtr.Zero || !Win32.GetMonitorInfo(handle, ref info)) return;
        Win32.GetDpiForMonitor(handle, 0, out uint dpi, out _);
        (monitorHandle, monitor, work, scale) = (handle, info.rcMonitor, info.rcWork, (dpi == 0 ? 96 : dpi) / 96.0);
        laidOut = false;
    }

    // MARK: pintar

    private static bool OnButton(DependencyObject? source)
    {
        for (; source is not null; source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source))
            if (source is System.Windows.Controls.Primitives.ButtonBase) return true;
        return false;
    }

    private void RelayoutIfContentChanged()
    {
        view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        peek.Measure(new Size(view.DesiredSize.Width, double.PositiveInfinity));
        if (view.DesiredSize != lastDesired || peek.DesiredSize != lastPeek) Apply();
    }

    private bool closing;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        closing = true;   // al cerrarse, perder el foco no debe volver a mostrarla
        base.OnClosing(e);
    }

    private void Apply()
    {
        if (closing) return;
        if (state.Hidden)
        {
            if (IsVisible) Hide();
            shownOpen = null;
            painted = null;
            return;
        }
        if (hwnd == IntPtr.Zero) return;
        model.BodyMaxHeight = PanelViewModel.BodyMaxFor(work.Height / scale, wide: true);
        view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        peek.Measure(new Size(view.DesiredSize.Width, double.PositiveInfinity));
        (lastDesired, lastPeek) = (view.DesiredSize, peek.DesiredSize);
        var mode = state.Mode;
        // El hover enseña la versión corta; el clic, el panel entero.
        var content = mode == NotchMode.Pinned ? view.DesiredSize : peek.DesiredSize;
        rects = NotchLayout.Place(monitor, work, scale, content.Width, content.Height, pillWidthDip);
        bool open = mode != NotchMode.Collapsed;
        if (!IsVisible) Show();

        int ear = (int)Math.Round(EarDip * scale);
        var dockRect = NotchLayout.WithOvershoot(NotchLayout.DockBelow(rects.Expanded, scale, dockWidth), scale);
        var pillRect = Wings(rects.Pill, ear + (int)Math.Round(ChipRoom * scale));
        // Los botones solo salen con el notch fijado (clic): en la vista al pasar el ratón no hay dock ni sitio para él.
        bool showDock = mode == NotchMode.Pinned;
        var openRect = showDock ? NotchLayout.Union(Wings(rects.Expanded, ear), dockRect) : Wings(rects.Expanded, ear);
        activeRect = !open ? rects.Pill : showDock ? NotchLayout.Union(rects.Expanded, dockRect) : rects.Expanded;
        var pillSize = new Size(rects.Pill.Width / scale + 2 * EarDip, rects.Pill.Height / scale);
        var openSize = new Size(rects.Expanded.Width / scale + 2 * EarDip, rects.Expanded.Height / scale);
        var now = (mode, open ? openRect : pillRect, open ? openSize : pillSize);
        var previousMode = painted?.Mode;
        if (painted is { } before && before.Mode == now.mode && before.Shell == now.Item3
            && before.Window.Left == now.Item2.Left && before.Window.Top == now.Item2.Top
            && before.Window.Right == now.Item2.Right && before.Window.Bottom == now.Item2.Bottom) return;
        painted = now;
        bool modeChanged = previousMode != mode;   // pastilla ↔ versión corta ↔ panel entero
        if (open)
        {
            // Lo de detrás se copia antes de que la ventana crezca; el vidrio se pinta (con su tinte) una vez que hay copia.
            if (previousMode is null or NotchMode.Collapsed) CaptureBackdrop();
            if (!glassBody) { glassBody = true; PaintBody(); PaintBevel((Geometry)body.Data, shell.ActualHeight); }
            // La ventana crece primero (transparente) y luego se abre la pastilla; al revés al cerrar.
            Place(openRect);
            pill.Visibility = Visibility.Collapsed;
            peek.Visibility = mode == NotchMode.Expanded ? Visibility.Visible : Visibility.Hidden;
            view.Visibility = mode == NotchMode.Pinned ? Visibility.Visible : Visibility.Hidden;   // Hidden y no Collapsed: Collapsed mediría 0
            view.Width = openSize.Width - 2 * EarDip;
            peek.Width = view.Width;
            dock.Visibility = showDock ? Visibility.Visible : Visibility.Collapsed;
            PaintTabs();
            // El contenido aparece con un fundido cuando la isla ya casi está abierta: si no, el borde que crece lo rebana
            // (el círculo de encendido se veía cortado unos 100 ms).
            if (modeChanged) Fade(0, 110, 80);
            Animate(openSize.Width, openSize.Height, null);
        }
        else
        {
            if (shownOpen is null) { Place(pillRect); shell.BeginAnimation(WidthProperty, null); shell.BeginAnimation(HeightProperty, null); }
            dock.Visibility = Visibility.Collapsed;
            if (modeChanged) Fade(1, 70, 0, to: 0);
            Animate(pillSize.Width, pillSize.Height, () =>
            {
                UpdateChip();
                glassBody = false;
                backdrop.Hide();
                PaintBody();
                bevelVersion = -1;
                if (body.Data is { } shape) PaintBevel(shape, shell.ActualHeight);
                Place(pillRect);
                Fade(0, 90, 0);   // la pastilla entra con un fundido
                view.Visibility = Visibility.Hidden;
                peek.Visibility = Visibility.Hidden;
                pill.Visibility = Visibility.Visible;
                if (!model.ShowingHome) model.Show(PanelView.Home);   // la próxima vez se abre en el Home
            });
        }
        shownOpen = open;
        laidOut = true;
        UpdateChip();
    }

    /// Fundido del contenido (pastilla, versión corta o panel). Con «movimiento reducido», sin fundido.
    private void Fade(double from, int ms, int delayMs, double to = 1)
    {
        holder.BeginAnimation(OpacityProperty, null);
        if (model.Prefs.ReducedMotion) { holder.Opacity = to; return; }
        holder.Opacity = from;
        holder.BeginAnimation(OpacityProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(delayMs) });
    }

    // MARK: los botones flotantes

    /// Lo que cambia el dock: qué favoritos hay y el idioma (los nombres accesibles).
    private string DockSignature() => $"{string.Join(",", model.FavoriteIds)}#{L10n.Effective}#{model.HasTrayFiles}#{model.ClipboardOn}#{string.Join(",", model.DockIds)}";

    private void RebuildDock()
    {
        BuildDock();
        if (laidOut) Apply();   // si el ancho del dock cambia la ventana, Apply lo nota al comparar con lo pintado
    }

    private void BuildDock()
    {
        dockSignature = DockSignature();
        tabs.Clear();
        navTabs.Clear();
        dockGlass.Clear();
        lens?.Dispose();
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(3, 2, 3, 2) };
        var favorites = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(3, 2, 3, 2) };
        void Tab(StackPanel row, string glyph, string nameProperty, PanelView? target, Action click, string? literalName = null)
        {
            var text = new TextBlock { Text = glyph, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            text.SetResourceReference(TextBlock.FontFamilyProperty, "Icons");
            // Como en Droppy: los iconos van sueltos dentro de la cápsula, con aire entre ellos; el seleccionado lleva un círculo claro detrás
            // (y el que tiene el ratón encima, uno más tenue). Al pulsar, el círculo se hunde un poco.
            var scale = new ScaleTransform(1, 1);
            var back = new Border
            {
                Width = DockButton, Height = DockButton, CornerRadius = new CornerRadius(DockButton / 2), Margin = new Thickness(4, 0, 4, 0), Child = text,
                Background = Brushes.Transparent, RenderTransform = scale, RenderTransformOrigin = new Point(0.5, 0.5),
            };
            var button = new Button { Content = back, Style = (Style)Application.Current.FindResource("Plain") };
            if (literalName is null)
            {
                button.SetBinding(AutomationProperties.NameProperty, new Binding(nameProperty) { Source = model });
                button.SetBinding(ToolTipProperty, new Binding(nameProperty) { Source = model });
            }
            else
            {
                AutomationProperties.SetName(button, literalName);
                button.ToolTip = literalName;
            }
            button.Click += (_, _) => click();
            button.MouseEnter += (_, _) => { hoveredButton = back; PaintTabs(); };
            button.MouseLeave += (_, _) => { if (ReferenceEquals(hoveredButton, back)) hoveredButton = null; Spring(scale, 1, 120, bounce: false); PaintTabs(); };
            if (!ReferenceEquals(row, navigation))
            {
                button.PreviewMouseLeftButtonDown += (_, _) => Spring(scale, 0.9, 70, bounce: false);
                button.PreviewMouseLeftButtonUp += (_, _) => Spring(scale, 1, 140, bounce: true);
            }
            row.Children.Add(button);
            tabs.Add((back, text, target));
            if (ReferenceEquals(row, navigation)) navTabs.Add((back, text, target, click));
        }
        // Navegación: un clic abre esa página entera. Favoritos: actúan sin fijar el notch.
        Tab(navigation, "", nameof(PanelViewModel.NotchTabHome), PanelView.Home, () => Go(PanelView.Home));
        // Inicio, Widgets y Ajustes siempre; Ollama y Actividad solo si se activan en la página Widgets (los favoritos van aparte).
        var dockPages = model.DockIds;
        if (dockPages.Contains("ollama")) Tab(navigation, "", nameof(PanelViewModel.NotchTabOllama), PanelView.Main, () => Go(PanelView.Main));
        if (dockPages.Contains("activity")) Tab(navigation, "", nameof(PanelViewModel.NotchTabActivity), PanelView.Activity, () => Go(PanelView.Activity));
        // La bandeja solo aparece mientras hay archivos esperando.
        if (model.HasTrayFiles) Tab(navigation, "", nameof(PanelViewModel.NotchTabTray), PanelView.Tray, () => Go(PanelView.Tray));
        if (model.ClipboardOn) Tab(navigation, "", nameof(PanelViewModel.NotchTabClipboard), PanelView.Clipboard, () => Go(PanelView.Clipboard));
        Tab(navigation, "", nameof(PanelViewModel.NotchTabWidgets), PanelView.Widgets, () => Go(PanelView.Widgets));
        Tab(navigation, "", nameof(PanelViewModel.NotchTabSettings), PanelView.Settings, () => Go(PanelView.Settings));
        foreach (var id in model.FavoriteIds)
            if (FavoriteCatalog.Find(id) is { } favorite)
                Tab(favorites, favorite.Glyph, PanelViewModel.FavoriteNameProperty(id), null, () => model.RunFavorite(id),
                    FavoriteCatalog.IsApp(id) ? L10n.tr("Abrir %@", favorite.Title) : null);
        // Fijar el notch abierto (como el de «fijar» de Droppy): mientras está activo, perder el foco no lo cierra.
        Tab(favorites, "\uE718", "", null, () => { sticky = !sticky; PaintTabs(); }, L10n.tr("Fijar el notch abierto"));
        (pinBack, pinGlyph, _) = tabs[^1];
        Border Pill(StackPanel row, double left)
        {
            var radius = new CornerRadius(NotchLayout.DockHeight / 2);
            var image = new Border { CornerRadius = radius, IsHitTestVisible = false };
            var tint = new Border { CornerRadius = radius, IsHitTestVisible = false };
            var capsule = new Border { CornerRadius = radius, Child = new Grid { Children = { image, tint, row } }, Margin = new Thickness(left, 0, 0, 0) };
            dockGlass.Add((capsule, image, tint, new ImageBrush { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.RelativeToBoundingBox }));
            return capsule;
        }
        var navCapsule = Pill(navigation, 0);
        var strips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { navCapsule } };
        if (favorites.Children.Count > 0) strips.Children.Add(Pill(favorites, 10));
        // La lente de cristal (como la de iOS 26) va en una capa sobre los botones; sobresale por arriba y por abajo, así que la ventana lleva margen.
        lens = new DockLens(() => model.Prefs.ReducedMotion);
        lens.SetButtons(navTabs.Select(t => (t.Back, t.Glyph)).ToList());
        var thisLens = lens;
        var host = new Grid { Height = NotchLayout.DockHeight + 2 * NotchLayout.DockPad };
        host.Children.Add(strips);
        host.Children.Add(lens.Layer);
        dock.Child = host;
        // Pulsar en la cápsula agranda la lente; arrastrar la mueve con el puntero; soltar la asienta en el botón más cercano y lo abre.
        navCapsule.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton != System.Windows.Input.MouseButton.Left || navTabs.Count == 0) return;
            navCapsule.CaptureMouse();
            thisLens.BeginDrag(e.GetPosition(thisLens.Layer));
            e.Handled = true;
        };
        navCapsule.PreviewMouseMove += (_, e) => { if (navCapsule.IsMouseCaptured) thisLens.DragTo(e.GetPosition(thisLens.Layer)); };
        navCapsule.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!navCapsule.IsMouseCaptured) return;
            e.Handled = true;
            // Primero se asienta la lente y después se suelta el ratón: soltarlo antes avisaría de «ratón perdido» y cancelaría el arrastre.
            int index = thisLens.EndDrag(e.GetPosition(thisLens.Layer));
            navCapsule.ReleaseMouseCapture();
            if (index >= 0 && index < navTabs.Count) navTabs[index].Click();
        };
        navCapsule.LostMouseCapture += (_, _) => thisLens.CancelDrag();
        dock.Margin = new Thickness(0, NotchLayout.DockGap - NotchLayout.DockPad, 0, 0);
        dock.Height = NotchLayout.DockHeight + 2 * NotchLayout.DockPad;
        strips.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        dockWidth = Math.Ceiling(strips.DesiredSize.Width);
        PaintShell();
    }

    private void Go(PanelView target)
    {
        model.Show(target);
        if (state.Mode != NotchMode.Pinned) Pin();
    }

    private const double DockButton = 28;
    private static readonly Brush HoverGlass = Glass(0x1C, 0x06), HoverRim = Glass(0x2C, 0x08);

    /// Un degradado vertical de blanco translúcido: `top` de opacidad arriba y `bottom` abajo.
    private static Brush Glass(byte top, byte bottom)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(top, 255, 255, 255), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(bottom, 255, 255, 255), 1));
        brush.Freeze();
        return brush;
    }
    private Border? hoveredButton;

    /// El resorte de los botones del dock al pulsar: se hunden un poco y vuelven con un pequeño rebote.
    private void Spring(ScaleTransform scale, double to, int ms, bool bounce)
        => Motion.Spring(scale, to, ms, model.Prefs.ReducedMotion, bounce);

    /// Reposo: el icono suelto sobre la cápsula. Con el ratón encima, un círculo tenue detrás; seleccionado, un círculo claro.
    private void PaintTabs()
    {
        bool contrast = SystemParameters.HighContrast;
        int selectedIndex = navTabs.FindIndex(t => state.Mode == NotchMode.Pinned && t.View is not null && model.CurrentView == t.View);
        lens?.Select(selectedIndex);
        foreach (var (back, glyph, target) in tabs)
        {
            bool selected = target is not null && state.Mode == NotchMode.Pinned && model.CurrentView == target;
            bool hot = ReferenceEquals(back, hoveredButton);
            if (contrast)
            {
                if (selected || hot) back.SetResourceReference(Border.BackgroundProperty, "ButtonHover");
                else { back.ClearValue(Border.BackgroundProperty); back.Background = Brushes.Transparent; }
            }
            else
            {
                // El círculo de selección es la lente de cristal (DockLens): aquí solo queda el tenue del ratón encima.
                back.Background = hot && !selected ? HoverGlass : Brushes.Transparent;
                back.BorderBrush = hot && !selected ? HoverRim : Brushes.Transparent;
                back.BorderThickness = new Thickness(1);
            }
            glyph.SetResourceReference(TextBlock.ForegroundProperty, selected || hot ? "Fg" : "Secondary");
        }
        if (pinBack is not null && pinGlyph is not null)
        {
            pinGlyph.Text = sticky ? "\uE840" : "\uE718";   // clavo vacío / clavo clavado
            if (sticky && !contrast)
            {
                pinBack.Background = SelectedPin;
                pinBack.BorderBrush = HoverRim;
                pinGlyph.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
            }
        }
    }

    private static readonly Brush SelectedPin = Glass(0x34, 0x12);

    /// La ventana es más ancha que el cuerpo: a cada lado caben las orejas.
    private static Win32.RECT Wings(Win32.RECT r, int ear) => new() { Left = r.Left - ear, Top = r.Top, Right = r.Right + ear, Bottom = r.Bottom };

    private static Geometry NotchShape(double w, double h)
    {
        // Con la pastilla (30 px de alto) las orejas y las esquinas se encogen para que la forma siga cerrando.
        double e = Math.Min(EarDip, h / 3), r = Math.Min(CornerRadiusDip, h - e);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, 0), true, true);
            c.LineTo(new Point(w, 0), true, false);
            c.ArcTo(new Point(w - e, e), new Size(e, e), 0, false, SweepDirection.Counterclockwise, true, false);   // oreja derecha
            c.LineTo(new Point(w - e, h - r), true, false);
            c.ArcTo(new Point(w - e - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(e + r, h), true, false);
            c.ArcTo(new Point(e, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(e, e), true, false);
            c.ArcTo(new Point(0, 0), new Size(e, e), 0, false, SweepDirection.Counterclockwise, true, false);        // oreja izquierda
        }
        g.Freeze();
        return g;
    }

    private void Place(Win32.RECT r) =>
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, r.Left, r.Top, r.Width, r.Height, Win32.SWP_NOACTIVATE);

    /// Abre o cierra el negro; con «movimiento reducido» salta directo.
    private void Animate(double width, double height, Action? done)
    {
        int mine = ++generation;
        bool first = !laidOut || double.IsNaN(shell.Width);
        if (model.Prefs.ReducedMotion || first)
        {
            shell.BeginAnimation(WidthProperty, null);
            shell.BeginAnimation(HeightProperty, null);
            shell.Width = width;
            shell.Height = height;
            done?.Invoke();
            return;
        }
        var ease = Motion.Ease();
        var span = Motion.PanelDuration;
        var growH = new DoubleAnimation(height, span) { EasingFunction = ease };
        growH.Completed += (_, _) =>
        {
            if (mine != generation) return;
            shell.BeginAnimation(WidthProperty, null);
            shell.BeginAnimation(HeightProperty, null);
            shell.Width = width;
            shell.Height = height;
            done?.Invoke();
        };
        shell.BeginAnimation(WidthProperty, new DoubleAnimation(width, span) { EasingFunction = ease });
        shell.BeginAnimation(HeightProperty, growH);
    }

    private void SetNoActivate(bool on)
    {
        if (hwnd == IntPtr.Zero) return;
        long ex = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
        ex = on ? ex | Win32.WS_EX_NOACTIVATE : ex & ~(long)Win32.WS_EX_NOACTIVATE;
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, new IntPtr(ex));
    }

    /// «Dynamic Glass» (el vidrio de Droppy): negro macizo arriba, junto a la pantalla, que se funde hacia abajo en un vidrio de verdad:
    /// lo de detrás del notch, desenfocado (ver `NotchBackdrop`), bajo un tinte translúcido, con un filo de luz fino. Con alto contraste, sólido.
    private void PaintShell()
    {
        bool contrast = SystemParameters.HighContrast;
        var baseColor = contrast ? Theme.Current.Bg : Theme.Hex(0x000000);
        baseColor.A = 255;
        var rim = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        rim.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
        rim.GradientStops.Add(new GradientStop(Color.FromArgb(0x42, 255, 255, 255), 0.4));
        rim.GradientStops.Add(new GradientStop(Color.FromArgb(0x66, 255, 255, 255), 1));   // el canto de abajo, el que más luz recoge
        rim.Freeze();
        PaintBody();
        body.Stroke = contrast ? null : rim;
        body.StrokeThickness = 1;
        foreach (var bubble in new[] { chip, chipLeft })
        {
            bubble.Background = Theme.Brush(baseColor);
            bubble.BorderBrush = contrast ? null : rim;
            bubble.BorderThickness = new Thickness(contrast ? 0 : 1);
        }
        PaintDock();
        PaintTabs();
    }

    private static readonly Brush DockRim = Across((0, White(0xC0)), (0.1, White(0x66)), (0.5, White(0x34)), (0.9, White(0x66)), (1, White(0xC0)));

    /// Las cápsulas del dock: el fondo desenfocado de debajo (con su color) bajo un tinte de negro puro que se oscurece con lo claro que sea, y el filo
    /// con luz en las esquinas, igual que el notch. Sin copia del fondo (alto contraste o si falló), el tinte solo es casi opaco.
    private void PaintDock()
    {
        bool contrast = SystemParameters.HighContrast;
        bool glass = !contrast && backdrop.Element.Opacity > 0 && backdrop.Element.Source is not null;
        double lum = glass ? backdrop.Luminance : 1;
        byte top = (byte)(glass ? 0x40 + 0x70 * lum : 0xD2), bottom = (byte)(glass ? 0x28 + 0x60 * lum : 0x9E);
        var tint = new LinearGradientBrush(Color.FromArgb(top, 0, 0, 0), Color.FromArgb(bottom, 0, 0, 0), 90);
        tint.Freeze();
        foreach (var (capsule, _, layer, _) in dockGlass)
        {
            layer.Background = contrast ? Theme.Brush(Theme.Current.Bg) : tint;
            capsule.BorderBrush = contrast ? null : DockRim;
            capsule.BorderThickness = new Thickness(contrast ? 0 : 1);
        }
        UpdateDockGlass();
    }

    /// Pone en cada cápsula la parte de la copia del fondo que cae justo debajo (se llama también cuando el notch cambia de tamaño y el dock se mueve).
    private void UpdateDockGlass()
    {
        var back = backdrop.Element;
        foreach (var (capsule, image, _, brush) in dockGlass)
        {
            if (SystemParameters.HighContrast || back.Source is not { } source || back.Opacity == 0 || !(back.Width > 0) || !(back.Height > 0) || capsule.ActualWidth <= 0) { image.Background = null; continue; }
            Point origin;
            try { origin = capsule.TranslatePoint(new Point(0, 0), back); }
            catch (InvalidOperationException) { image.Background = null; continue; }
            var area = new Rect(origin.X / back.Width, origin.Y / back.Height, capsule.ActualWidth / back.Width, capsule.ActualHeight / back.Height);
            if (area.Left < 0 || area.Top < 0 || area.Right > 1.001 || area.Bottom > 1.001) { image.Background = null; continue; }
            brush.ImageSource = source;
            brush.Viewbox = area;
            image.Background = brush;
        }
    }

    /// El relleno del cuerpo: la pastilla cerrada es negro macizo; abierto, negro arriba (pegado a la pantalla, donde está la cámara) que
    /// se funde hacia abajo en un tinte translúcido sobre lo desenfocado de detrás. El tinte se oscurece con lo claro que sea lo de detrás
    /// (una nieve blanca no puede dejar el texto sin leer).
    private void PaintBody()
    {
        bool contrast = SystemParameters.HighContrast;
        var baseColor = contrast ? Theme.Current.Bg : Theme.Hex(0x000000);
        baseColor.A = 255;
        if (contrast || !glassBody) { body.Fill = Theme.Brush(baseColor); return; }
        byte tint = (byte)(0x24 + 0x70 * Math.Clamp(backdrop.Luminance, 0, 1));   // sin tarjetas detrás, el tinte es lo que da el contraste
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        g.GradientStops.Add(new GradientStop(baseColor, 0));
        double glassLevel = model.Prefs.GlassLevel;
        g.GradientStops.Add(new GradientStop(baseColor, glassLevel));
        g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Min(255, tint + 0x60), baseColor.R, baseColor.G, baseColor.B), Math.Min(.97, glassLevel + .28)));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(tint, baseColor.R, baseColor.G, baseColor.B), 1));
        g.Freeze();
        body.Fill = g;
        paintedLuma = backdrop.Luminance;
    }

    private static System.Windows.Shapes.Path Bevel(double thickness, bool clip = true) => new()
    {
        Stretch = Stretch.None, StrokeThickness = thickness, IsHitTestVisible = false, Visibility = Visibility.Collapsed, Tag = clip,
        // Solo el borde de abajo y los costados: arriba, junto a la pantalla, el negro sigue limpio.
        OpacityMask = new LinearGradientBrush([new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.4), new GradientStop(Color.FromArgb(255, 0, 0, 0), 0.82)], new Point(0, 0), new Point(0, 1)),
    };

    private static LinearGradientBrush Across(params (double At, Color Color)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        foreach (var (at, color) in stops) brush.GradientStops.Add(new GradientStop(color, at));
        brush.Freeze();
        return brush;
    }

    private static Color White(byte alpha) => Color.FromArgb(alpha, 255, 255, 255);

    /// Pone el canto en la forma actual del notch. Sin vidrio (pastilla cerrada o alto contraste) no hay canto.
    private void PaintBevel(Geometry shape, double heightDip)
    {
        bool on = glassBody && !SystemParameters.HighContrast && heightDip > 80;
        foreach (var part in new[] { bevelColor, bevelLight })
        {
            part.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            part.Data = shape;
            part.Clip = shape;
        }
        if (!on) return;
        if (bevelLight.Stroke is null)
        {
            // El filo: casi nada en el tramo recto y mucha luz en las esquinas de abajo (donde el vidrio curva y más refracta).
            bevelLight.Stroke = Across((0, White(0xF0)), (0.05, White(0xD0)), (0.14, White(0x34)), (0.5, White(0x3C)), (0.86, White(0x34)), (0.95, White(0xD0)), (1, White(0xF0)));
        }
        if (backdrop.Version == bevelVersion) return;
        bevelVersion = backdrop.Version;
        var colors = backdrop.EdgeColors(heightDip - 6, 12);
        if (colors is null) { bevelColor.Stroke = null; return; }
        // El hilo de color: los colores de lo de detrás en las esquinas y casi transparente entre ellas.
        bevelColor.Stroke = Across((0, Color.FromArgb(0xE0, colors[0].R, colors[0].G, colors[0].B)), (0.06, Color.FromArgb(0xB0, colors[1].R, colors[1].G, colors[1].B)),
            (0.2, Color.FromArgb(0x16, colors[2].R, colors[2].G, colors[2].B)), (0.5, Color.FromArgb(0x10, colors[6].R, colors[6].G, colors[6].B)),
            (0.8, Color.FromArgb(0x16, colors[9].R, colors[9].G, colors[9].B)), (0.94, Color.FromArgb(0xB0, colors[10].R, colors[10].G, colors[10].B)),
            (1, Color.FromArgb(0xE0, colors[11].R, colors[11].G, colors[11].B)));
    }

    /// Copia lo de detrás del notch, a su mayor tamaño (el panel entero), justo antes de que la ventana crezca: así la copia no se ve a sí
    /// misma. Se hace una vez cada vez que se abre; si lo de detrás cambia con el notch abierto, el vidrio se queda con la foto de entonces.
    /// shortcut: sin refresco en vivo (la propia ventana saldría en la copia), cuando se pueda excluir sin esconderla de las capturas.
    private void CaptureBackdrop()
    {
        var area = NotchLayout.Place(monitor, work, scale, view.DesiredSize.Width, 0, pillWidthDip).Expanded;
        int ear = (int)Math.Round(EarDip * scale);
        area.Left -= ear;
        area.Right += ear;
        area.Bottom = (int)Math.Min(monitor.Bottom, area.Top + Math.Round((model.BodyMaxHeight + 260) * scale));
        if (backdrop.Capture(area, scale)) { if (Math.Abs(backdrop.Luminance - paintedLuma) > 0.06) PaintBody(); PaintDock(); }
    }

    // MARK: la pastilla

    private readonly StackPanel pillRow = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Dictionary<string, TextBlock> pillTexts = [];
    private Marquee? pillMarquee;   // el dato «Música» de la pastilla (con su indicador), si está elegido
    private FrameworkElement? pillMediaItem;
    private Action? pillMediaPaint;

    /// Las burbujas, a cada lado del borde del notch (el cuerpo con sus orejas), con un hueco de 6. Se usa el tamaño medido aunque aún no se haya
    /// dibujado: con 0 se pintaría un instante encima del notch.
    private void PlaceChip()
    {
        double half = shell.ActualWidth / 2 + 6;
        chipPlace.X = half + Math.Max(chip.ActualWidth, chip.DesiredSize.Width) / 2;
        chipLeftPlace.X = -(half + Math.Max(chipLeft.ActualWidth, chipLeft.DesiredSize.Width) / 2);
    }

    /// Salen con un fundido y un pequeño deslizamiento desde el notch; con el notch abierto no hay burbujas (las actividades están en su panel).
    private void UpdateChip(bool pulseBrightness = false)
    {
        bool closed = state.Mode == NotchMode.Collapsed && shownOpen != true && !state.Hidden;
        ShowChip(chip, activityRow, chipSlide, closed, -18, pulseBrightness);
        ShowChip(chipLeft, activityRowLeft, chipLeftSlide, closed, 18);
    }

    private void ShowChip(Border bubble, Panel row, TranslateTransform slide, bool closed, double from, bool pulse = false)
    {
        bool want = closed && row.Children.Count > 0;
        if (want == (bubble.Visibility == Visibility.Visible) && !pulse) return;
        bubble.Visibility = want ? Visibility.Visible : Visibility.Collapsed;
        bubble.BeginAnimation(OpacityProperty, null);
        slide.BeginAnimation(TranslateTransform.XProperty, null);
        if (!want || model.Prefs.ReducedMotion) { bubble.Opacity = 1; slide.X = 0; if (want) { bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); PlaceChip(); } return; }
        bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        PlaceChip();
        var span = TimeSpan.FromMilliseconds(190);
        bubble.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, span));
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from, 0, span) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }

    /// La música en la pastilla, como en Droppy: la portada pequeña, el título que se desplaza y el ecualizador, teñido con el color de la portada.
    private FrameworkElement BuildMediaItem(Thickness margin, out Marquee marquee, out Action paint, double textWidth = MediaTextWidth)
    {
        bool reduced = model.Prefs.ReducedMotion;
        marquee = new Marquee(() =>
        {
            var t = new TextBlock { FontSize = 10.5, FontFamily = (FontFamily)Application.Current.FindResource("Mono"), VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, "Secondary");
            return t;
        }, textWidth, reduced);
        var equalizer = PillEqualizer.Build(reduced);
        equalizer.Margin = new Thickness(8, 0, 0, 0);
        var art = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        ImageSource? shown = null;
        var media = model.Hub.Media;
        paint = () =>
        {
            PillEqualizer.Paint(equalizer, media.AccentBrush);
            if (!ReferenceEquals(shown, media.Art))
            {
                shown = media.Art;
                art.Background = shown is null ? null : new ImageBrush(shown) { Stretch = Stretch.UniformToFill };
            }
            art.Visibility = shown is null ? Visibility.Collapsed : Visibility.Visible;
        };
        paint();
        return new StackPanel { Orientation = Orientation.Horizontal, Margin = margin, Children = { art, marquee, equalizer } };
    }
    private Ellipse? pillDot;
    private string pillSignature = "", activitySignature = "";
    private long brightnessRevision;
    /// Las actividades en vivo (limpiando, encendiendo, temporizador…) van a la derecha de los datos; la pastilla crece para dar sitio.
    private readonly StackPanel activityRow = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel activityRowLeft = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel pillOuter = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Dictionary<string, Action<string>> activityTexts = [];
    private readonly Dictionary<string, ActivityRing> activityRings = [];
    private const double MediaTextWidth = 120;   // el título de la canción se desplaza dentro de este ancho: la pastilla no crece con él
    private double pillWidthDip = NotchLayout.PillWidth;

    /// Los datos de la pastilla cerrada son los que elija el usuario (página Widgets): se reconstruyen si cambia la lista y se repintan con cada dato nuevo.
    private void RefreshPill()
    {
        var ids = model.PillIds;
        var signature = string.Join(",", ids);
        if (signature != pillSignature)
        {
            pillSignature = signature;
            pillRow.Children.Clear();
            pillTexts.Clear();
            pillDot = null;
            pillMarquee = null;
            pillMediaPaint = null;
            pillMediaItem = null;
            foreach (var id in ids)
            {
                if (id == "media")
                {
                    var mediaItem = BuildMediaItem(new Thickness(pillRow.Children.Count == 0 ? 0 : 10, 0, 0, 0), out pillMarquee, out pillMediaPaint);
                    pillMediaItem = mediaItem;
                    pillRow.Children.Add(mediaItem);
                    continue;
                }
                var text = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                switch (id)
                {
                    case "state": text.FontWeight = FontWeights.SemiBold; text.SetResourceReference(TextBlock.ForegroundProperty, "Fg"); break;
                    case "model": text.MaxWidth = 92; text.SetResourceReference(TextBlock.ForegroundProperty, "Secondary"); break;
                    case "clock": text.SetResourceReference(TextBlock.ForegroundProperty, "Fg"); break;
                    default:
                        text.FontSize = 10.5;
                        text.FontFamily = (FontFamily)Application.Current.FindResource("Mono");
                        text.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                        break;
                }
                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(pillRow.Children.Count == 0 ? 0 : 10, 0, 0, 0) };
                if (id == "state")
                {
                    pillDot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
                    item.Children.Add(pillDot);
                }
                item.Children.Add(text);
                pillRow.Children.Add(item);
                pillTexts[id] = text;
            }
        }
        foreach (var (id, text) in pillTexts) text.Text = model.PillValue(id);
        if (pillMarquee is not null && pillMediaItem is not null)
        {
            var playing = model.PillValue("media");
            pillMarquee.Text = playing;
            pillMediaPaint?.Invoke();
            pillMediaItem.Visibility = playing.Length > 0 ? Visibility.Visible : Visibility.Collapsed;   // sin música, el dato no ocupa sitio
        }
        if (pillDot is not null) pillDot.Fill = model.PillForeground;

        var activities = model.LiveActivities;
        bool brightnessChanged = model.Hub.Huds.BrightnessRevision != brightnessRevision;
        brightnessRevision = model.Hub.Huds.BrightnessRevision;
        var activityIds = string.Join(",", activities.Select(a => a.Id));
        if (activityIds != activitySignature)
        {
            activitySignature = activityIds;
            activityRow.Children.Clear();
            activityRowLeft.Children.Clear();
            activityTexts.Clear();
            activityRings.Clear();
            foreach (var activity in activities)
            {
                // La música lleva el indicador de reproducción y el título con el artista, que se desplaza dentro de un ancho fijo.
                if (activity.Id == "media")
                {
                    (activity.Left ? activityRowLeft : activityRow).Children.Add(BuildMediaItem(new Thickness(10, 0, 0, 0), out var marquee, out var paint, 84));
                    activityTexts[activity.Id] = value => { marquee.Text = value; paint(); };
                    continue;
                }
                FrameworkElement glyph;
                if (activity.Progress >= 0)
                {
                    var ring = new ActivityRing { Width = 12, Height = 12, Progress = activity.Progress, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                    activityRings[activity.Id] = ring;
                    glyph = ring;
                }
                else
                {
                    var icon = new TextBlock { Text = activity.Glyph, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
                    icon.SetResourceReference(TextBlock.FontFamilyProperty, "Icons");
                    icon.SetResourceReference(TextBlock.ForegroundProperty, activity.Id switch { "clean" or "power" => "Warm", "pressure" => "Danger", _ => "Secondary" });
                    glyph = icon;
                }
                var text = new TextBlock { FontSize = 10.5, FontFamily = (FontFamily)Application.Current.FindResource("Mono"), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 110, TextTrimming = TextTrimming.CharacterEllipsis };
                text.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                (activity.Left ? activityRowLeft : activityRow).Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), Children = { glyph, text } });
                activityTexts[activity.Id] = value => text.Text = value;
            }
        }
        foreach (var activity in activities)
        {
            activityTexts[activity.Id](activity.Text);
            if (activityRings.TryGetValue(activity.Id, out var ring)) ring.Progress = activity.Progress;
        }
        UpdateChip(brightnessChanged && activities.Any(a => a.Id == "brightness"));

        var unbounded = new Size(double.PositiveInfinity, double.PositiveInfinity);
        pillRow.Measure(unbounded);
        activityRow.Measure(unbounded);
        var width = Math.Clamp(Math.Ceiling(pillRow.DesiredSize.Width) + 32, NotchLayout.PillWidth, 440);
        if (Math.Abs(width - pillWidthDip) > 1)
        {
            pillWidthDip = width;
            if (pill is not null) pill.Width = width;
            // Apply compara lo pintado con lo nuevo: con el notch abierto (la píldora ni se ve) no repinta, y cerrado solo cambia de ancho.
            // Antes aquí se invalidaba `painted`, y cada cambio de ancho (reloj, actividades en vivo) repetía la apertura con su fundido: parpadeo.
            if (laidOut) Apply();
        }
    }

    private Button BuildPill()
    {
        pillOuter.Children.Add(pillRow);
        RefreshPill();
        var button = new Button
        {
            Content = pillOuter, Width = pillWidthDip, Height = NotchLayout.PillHeight,
            Style = (Style)Application.Current.FindResource("Plain"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
        };
        button.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(PanelViewModel.NotchHelp)) { Source = model });
        button.Click += (_, _) => Pin();
        return button;
    }
}
