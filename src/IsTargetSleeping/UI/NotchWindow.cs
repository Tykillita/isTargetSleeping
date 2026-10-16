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
    private readonly Grid holder = new() { Margin = new Thickness(EarDip, 0, EarDip, 0) };
    private readonly Button pill;
    private readonly NotchPeek peek;
    /// Los botones flotantes bajo la isla: Resumen, Actividad, Ajustes y Procesos.
    /// Dos pastillas lado a lado, como en Droppy: la navegación y, al lado, los favoritos de un toque (encender, liberar RAM, procesos).
    private readonly Border dock = new() { HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly List<Border> dockPills = [];
    private readonly List<(Border Back, TextBlock Glyph, PanelView? View)> tabs = [];
    private readonly NotchState state = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly Stopwatch clock = Stopwatch.StartNew();

    private IntPtr hwnd;
    private IntPtr monitorHandle;
    private Win32.RECT monitor, work;
    private double scale = 1;
    private NotchRects rects;
    private bool suppressed, laidOut;
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
        shell.Children.Add(body);
        shell.Children.Add(holder);
        BuildDock();
        Content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Children = { shell, dock } };
        shell.SizeChanged += (_, e) => body.Data = NotchShape(e.NewSize.Width, e.NewSize.Height);
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
        Deactivated += (_, _) => Collapse();
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
        if (!Win32.GetCursorPos(out var cursor)) return;
        if (state.Mode == NotchMode.Collapsed) FollowCursorMonitor(cursor);
        if (++ticks % 12 == 1) suppressed = TaskbarLocator.Suppressed(monitor);   // ~cada 0,5 s
        bool inside = NotchLayout.Contains(state.Mode == NotchMode.Collapsed ? rects.Hot : NotchLayout.Inflate(activeRect, (int)Math.Round(14 * scale)), cursor.X, cursor.Y);
        if (state.Update(inside, suppressed, false, clock.Elapsed.TotalSeconds)) Apply();
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
        var dockRect = NotchLayout.DockBelow(rects.Expanded, scale, dockWidth);
        var pillRect = Wings(rects.Pill, ear);
        var openRect = NotchLayout.Union(Wings(rects.Expanded, ear), dockRect);
        activeRect = open ? NotchLayout.Union(rects.Expanded, dockRect) : rects.Pill;
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
            // La ventana crece primero (transparente) y luego se abre la pastilla; al revés al cerrar.
            Place(openRect);
            pill.Visibility = Visibility.Collapsed;
            peek.Visibility = mode == NotchMode.Expanded ? Visibility.Visible : Visibility.Hidden;
            view.Visibility = mode == NotchMode.Pinned ? Visibility.Visible : Visibility.Hidden;   // Hidden y no Collapsed: Collapsed mediría 0
            view.Width = openSize.Width - 2 * EarDip;
            peek.Width = view.Width;
            dock.Visibility = Visibility.Visible;
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
    private string DockSignature() => $"{string.Join(",", model.FavoriteIds)}#{L10n.Effective}#{model.HasTrayFiles}#{model.ClipboardOn}";

    private void RebuildDock()
    {
        BuildDock();
        if (laidOut) Apply();   // si el ancho del dock cambia la ventana, Apply lo nota al comparar con lo pintado
    }

    private void BuildDock()
    {
        dockSignature = DockSignature();
        tabs.Clear();
        dockPills.Clear();
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5, 4, 5, 4) };
        var favorites = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5, 4, 5, 4) };
        void Tab(StackPanel row, string glyph, string nameProperty, PanelView? target, Action click, string? literalName = null)
        {
            var text = new TextBlock { Text = glyph, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            text.SetResourceReference(TextBlock.FontFamilyProperty, "Icons");
            var back = new Border { Width = 36, Height = 26, CornerRadius = new CornerRadius(8), Margin = new Thickness(2, 0, 2, 0), Child = text, Background = Brushes.Transparent };
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
            // Un tinte plano al pasar el ratón (sin borde), como los controles del notch de Droppy.
            button.MouseEnter += (_, _) => back.SetResourceReference(Border.BackgroundProperty, "ButtonHover");
            button.MouseLeave += (_, _) => PaintTabs();
            row.Children.Add(button);
            tabs.Add((back, text, target));
        }
        // Navegación: un clic abre esa página entera. Favoritos: actúan sin fijar el notch.
        Tab(navigation, "", nameof(PanelViewModel.NotchTabHome), PanelView.Home, () => Go(PanelView.Home));
        Tab(navigation, "", nameof(PanelViewModel.NotchTabOllama), PanelView.Main, () => Go(PanelView.Main));
        Tab(navigation, "", nameof(PanelViewModel.NotchTabActivity), PanelView.Activity, () => Go(PanelView.Activity));
        // La bandeja solo aparece mientras hay archivos esperando.
        if (model.HasTrayFiles) Tab(navigation, "", nameof(PanelViewModel.NotchTabTray), PanelView.Tray, () => Go(PanelView.Tray));
        if (model.ClipboardOn) Tab(navigation, "", nameof(PanelViewModel.NotchTabClipboard), PanelView.Clipboard, () => Go(PanelView.Clipboard));
        Tab(navigation, "", nameof(PanelViewModel.NotchTabWidgets), PanelView.Widgets, () => Go(PanelView.Widgets));
        Tab(navigation, "", nameof(PanelViewModel.NotchTabSettings), PanelView.Settings, () => Go(PanelView.Settings));
        foreach (var id in model.FavoriteIds)
            if (FavoriteCatalog.Find(id) is { } favorite)
                Tab(favorites, favorite.Glyph, PanelViewModel.FavoriteNameProperty(id), null, () => model.RunFavorite(id),
                    FavoriteCatalog.IsApp(id) ? L10n.tr("Abrir %@", favorite.Title) : null);
        Border Pill(StackPanel row, double left)
        {
            var pillBorder = new Border { CornerRadius = new CornerRadius(14), Child = row, Margin = new Thickness(left, 0, 0, 0) };
            dockPills.Add(pillBorder);
            return pillBorder;
        }
        var strips = new StackPanel { Orientation = Orientation.Horizontal, Children = { Pill(navigation, 0) } };
        if (favorites.Children.Count > 0) strips.Children.Add(Pill(favorites, 8));
        dock.Child = strips;
        dock.Margin = new Thickness(0, NotchLayout.DockGap, 0, 0);
        dock.Height = NotchLayout.DockHeight;
        strips.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        dockWidth = Math.Ceiling(strips.DesiredSize.Width);
        PaintShell();
    }

    private void Go(PanelView target)
    {
        model.Show(target);
        if (state.Mode != NotchMode.Pinned) Pin();
    }

    private void PaintTabs()
    {
        foreach (var (back, glyph, target) in tabs)
        {
            bool selected = target is not null && state.Mode == NotchMode.Pinned && model.CurrentView == target;
            if (selected) back.SetResourceReference(Border.BackgroundProperty, "ButtonHover");
            else { back.ClearValue(Border.BackgroundProperty); back.Background = Brushes.Transparent; }
            glyph.SetResourceReference(TextBlock.ForegroundProperty, selected ? "Fg" : "Secondary");
        }
    }

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
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var span = TimeSpan.FromMilliseconds(180);
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

    /// «Dynamic Glass» (el vidrio de Droppy): negro macizo arriba, junto a la pantalla, que se funde hacia abajo en un
    /// vidrio translúcido, con un filo de luz fino. Con alto contraste, sólido. shortcut: una ventana con transparencia por
    /// píxel no puede desenfocar lo de detrás (DWM no lo permite); se imita con el degradado y el filo, no con desenfoque real.
    private void PaintShell()
    {
        bool contrast = SystemParameters.HighContrast;
        var baseColor = contrast ? Theme.Current.Bg : Theme.Hex(0x0A0A0B);
        baseColor.A = 255;
        Brush Fade(byte bottomAlpha)
        {
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            g.GradientStops.Add(new GradientStop(baseColor, 0));
            g.GradientStops.Add(new GradientStop(baseColor, 0.3));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(bottomAlpha, baseColor.R, baseColor.G, baseColor.B), 1));
            g.Freeze();
            return g;
        }
        var rim = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        rim.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
        rim.GradientStops.Add(new GradientStop(Color.FromArgb(0x42, 255, 255, 255), 0.4));
        rim.GradientStops.Add(new GradientStop(Color.FromArgb(0x1C, 255, 255, 255), 1));
        rim.Freeze();
        body.Fill = contrast ? Theme.Brush(baseColor) : Fade(0xD4);
        body.Stroke = contrast ? null : rim;
        body.StrokeThickness = 1;
        foreach (var pillBorder in dockPills)
        {
            pillBorder.Background = contrast ? Theme.Brush(baseColor) : Theme.Brush(Color.FromArgb(0xD2, baseColor.R, baseColor.G, baseColor.B));
            pillBorder.BorderBrush = contrast ? null : rim;
            pillBorder.BorderThickness = new Thickness(contrast ? 0 : 1);
        }
    }

    // MARK: la pastilla

    private readonly StackPanel pillRow = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Dictionary<string, TextBlock> pillTexts = [];
    private Marquee? pillMarquee;   // el dato «Música» de la pastilla (con su indicador), si está elegido
    private FrameworkElement? pillMediaItem;
    private Ellipse? pillDot;
    private string pillSignature = "", activitySignature = "";
    /// Las actividades en vivo (limpiando, encendiendo, temporizador…) van a la derecha de los datos; la pastilla crece para dar sitio.
    private readonly StackPanel activityRow = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel pillOuter = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Dictionary<string, Action<string>> activityTexts = [];
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
            pillMediaItem = null;
            foreach (var id in ids)
            {
                if (id == "media")
                {
                    bool reduced = model.Prefs.ReducedMotion;
                    pillMarquee = new Marquee(() =>
                    {
                        var t = new TextBlock { FontSize = 10.5, FontFamily = (FontFamily)Application.Current.FindResource("Mono"), VerticalAlignment = VerticalAlignment.Center };
                        t.SetResourceReference(TextBlock.ForegroundProperty, "Secondary");
                        return t;
                    }, MediaTextWidth, reduced);
                    var mediaItem = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(pillRow.Children.Count == 0 ? 0 : 10, 0, 0, 0), Children = { PillEqualizer.Build(reduced), pillMarquee } };
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
            pillMediaItem.Visibility = playing.Length > 0 ? Visibility.Visible : Visibility.Collapsed;   // sin música, el dato no ocupa sitio
        }
        if (pillDot is not null) pillDot.Fill = model.PillForeground;

        var activities = model.LiveActivities;
        var activityIds = string.Join(",", activities.Select(a => a.Id));
        if (activityIds != activitySignature)
        {
            activitySignature = activityIds;
            activityRow.Children.Clear();
            activityTexts.Clear();
            foreach (var activity in activities)
            {
                // La música lleva el indicador de reproducción y el título con el artista, que se desplaza dentro de un ancho fijo.
                if (activity.Id == "media")
                {
                    bool reduced = model.Prefs.ReducedMotion;
                    var marquee = new Marquee(() =>
                    {
                        var t = new TextBlock { FontSize = 10.5, FontFamily = (FontFamily)Application.Current.FindResource("Mono"), VerticalAlignment = VerticalAlignment.Center };
                        t.SetResourceReference(TextBlock.ForegroundProperty, "Secondary");
                        return t;
                    }, MediaTextWidth, reduced);
                    activityRow.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), Children = { PillEqualizer.Build(reduced), marquee } });
                    activityTexts[activity.Id] = value => marquee.Text = value;
                    continue;
                }
                var glyph = new TextBlock { Text = activity.Glyph, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
                glyph.SetResourceReference(TextBlock.FontFamilyProperty, "Icons");
                glyph.SetResourceReference(TextBlock.ForegroundProperty, activity.Id switch { "clean" or "power" => "Warm", "pressure" => "Danger", _ => "Secondary" });
                var text = new TextBlock { FontSize = 10.5, FontFamily = (FontFamily)Application.Current.FindResource("Mono"), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 110, TextTrimming = TextTrimming.CharacterEllipsis };
                text.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                activityRow.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), Children = { glyph, text } });
                activityTexts[activity.Id] = value => text.Text = value;
            }
        }
        foreach (var activity in activities) activityTexts[activity.Id](activity.Text);

        var unbounded = new Size(double.PositiveInfinity, double.PositiveInfinity);
        pillRow.Measure(unbounded);
        activityRow.Measure(unbounded);
        var width = Math.Clamp(Math.Ceiling(pillRow.DesiredSize.Width + activityRow.DesiredSize.Width) + 32, NotchLayout.PillWidth, 440);
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
        pillOuter.Children.Add(activityRow);
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
