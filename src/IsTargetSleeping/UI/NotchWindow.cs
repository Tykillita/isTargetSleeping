using System.Diagnostics;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
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
    private const double PillEarDip = 7;   // los hombros de la pastilla cerrada: la ventana mide su ancho más 7 por lado
    private static double shapeEar = 40, shapeRadius = 26, shapeRadiusTall = 26;   // los del panel; la vista corta de un widget pone los suyos (17 y 32/40)
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
    /// El tinte de la portada, una sola capa con la forma del notch: la pastilla con letra y la vista abierta comparten el mismo degradado, que se estira con la forma
    /// (antes cada una lo pintaba por su cuenta y, al abrir o cerrar, se cruzaban dos degradados distintos y salía un bloque negro).
    private readonly System.Windows.Shapes.Path glow = new() { Stretch = Stretch.None, IsHitTestVisible = false, Opacity = 0 };
    private Color? glowColor;
    private bool glowOn;
    private GradientStop? glowBlackEnd, glowTintStart;
    private double glowOpen;   // 0 = pastilla, 1 = vista abierta: el color asoma antes cuando la forma es pequeña
    private double paintedLuma = -1;
    /// El canto del vidrio, como el de la lente del dock: un filo blanco fino que brilla en las esquinas de abajo y, pegado a él, un hilo de
    /// los colores de lo de detrás (refractados en el borde: fuertes en las esquinas, casi nada en medio). Nada de bandas ni franjas: se
    /// verían como un marco. Solo abajo y a los lados; el negro de arriba queda limpio.
    private readonly System.Windows.Shapes.Path bevelColor = Bevel(3.5), bevelLight = Bevel(3);
    /// La luz de los agentes: un hilo de color en el borde de abajo (y las esquinas) que se enciende según lo que hacen.
    private readonly System.Windows.Shapes.Path stateGlow = Bevel(2.6);
    private const int BeamLayers = 48;
    // La cola del haz: capas del mismo borde, cada una más larga y casi transparente, que acaban todas en la cabeza; sumadas dan un degradado suave, sin escalones.
    private readonly Grid stateGlowTail = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed, Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 0.8, RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance } };
    private double beamTurn;
    private DateTime beamStartedAt;
    private AgentGlowState? beamPending;
    private bool beamLapDone;
    private System.Windows.Threading.DispatcherTimer? beamWaitTimer;
    /// Fin de la vuelta del haz actual: ahí entra el estado que esperaba.
    private System.Windows.Threading.DispatcherTimer beamWait => beamWaitTimer ??= MakeBeamWait();
    private System.Windows.Threading.DispatcherTimer MakeBeamWait()
    {
        var timer = new System.Windows.Threading.DispatcherTimer();
        timer.Tick += (_, _) => { timer.Stop(); beamLapDone = true; ApplyStateGlow(); beamLapDone = false; };
        return timer;
    }
    private double beamPeriod;
    private double[] beamDash = [];
    private System.Windows.Shapes.Path[] beamParts = [];
    private bool beamHooked;
    private AgentGlowState glowState;
    private bool glowStill;
    private readonly DispatcherTimer glowTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private int bevelVersion = -1;
    // El margen es el de los hombros de la pastilla cerrada (7): con el de los del panel (40) la pastilla de 150 solo tenía 70 px de ancho útil y se recortaba todo menos la hora.
    private readonly Grid holder = new() { Margin = new Thickness(PillEarDip, 0, PillEarDip, 0) };
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
    // Las actividades en vivo (limpiando, temporizador, música…) van dentro de la pastilla cerrada: a la izquierda, lo de Ollama; a la derecha, lo demás.
    private bool sticky;
    // Un cuadro de texto con el foco: aunque el notch no esté fijado, perder el foco un instante no lo cierra mientras se escribe.
    private bool typing;
    private bool collapseAfterShelfRemoval;
    private Border? pinBack;
    private TextBlock? pinGlyph;
    private readonly NotchState state = new();
    private readonly DispatcherTimer transitionTimer = new(DispatcherPriority.Input);
    private readonly DispatcherTimer fallbackTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly NotchMouseMonitor mouseMonitor;
    private readonly VisibilityMonitor visibility;
    private readonly VisibilityGate gate = new();
    private readonly DispatcherTimer gateTimer = new();
    private HideReason hideReason;

    /// El notch se está dibujando (no lo aparta la pantalla completa, una app oculta, Inicio, la sesión bloqueada ni el escritorio seguro).
    public bool IsShown => gate.Shown;
    /// La pantalla donde está ahora (para que el anfitrión elija a cuál va un «abrir»).
    public IntPtr MonitorHandle => monitorHandle;
    /// Se lanza (en el hilo de la interfaz) cuando el notch se oculta o se muestra.
    public event Action<bool>? ShownChanged;
    private HwndSource? hwndSource;
    private NotchMode? scheduledMode;

    private IntPtr hwnd;
    private IntPtr monitorHandle;
    private Win32.RECT monitor, work;
    private double scale = 1;
    private NotchRects rects;
    private bool laidOut;
    private long backdropAt = -10_000;
    private bool BackdropFresh => clock.ElapsedMilliseconds - backdropAt < 1500;
    private int generation;
    private bool closeCleanupPending;
    private bool? shownOpen;
    /// Lo último que se pintó: si no cambia, no se vuelve a animar (reiniciar la animación a medias la dejaría atascada).
    private (NotchMode Mode, Win32.RECT Window, Size Shell)? painted;
    private Size lastDesired, lastPeek;
    private Win32.RECT activeRect;
    private double dockWidth = NotchLayout.DockWidth;
    private string dockSignature = "";

    /// El identificador del monitor al que está fijado este notch (null: sigue al cursor).
    private readonly string? lockedScreenId;
    /// Este notch se fijó (clic): el anfitrión cierra los de las demás pantallas.
    public event Action<NotchWindow>? Pinned;

    public NotchWindow(PanelViewModel model, FrameworkElement view, string? lockedScreenId = null)
    {
        this.model = model;
        this.lockedScreenId = lockedScreenId;
        this.view = view;
        mouseMonitor = new NotchMouseMonitor(Dispatcher, OnMouseMoved, OnMouseClicked);
        visibility = new VisibilityMonitor(Dispatcher, EvaluateVisibility);
        gateTimer.Tick += (_, _) => { gateTimer.Stop(); EvaluateVisibility(); };
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
        shell.Children.Add(glow);
        shell.Children.Add(holder);
        shell.Children.Add(bevelColor);
        shell.Children.Add(bevelLight);
        shell.Children.Add(stateGlowTail);
        shell.Children.Add(stateGlow);
        glowTimer.Tick += (_, _) => ApplyStateGlow();
        BuildDock();
        Content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Children = { shell, dock } };
        dock.SizeChanged += (_, _) => UpdateDockGlass();
        dock.IsVisibleChanged += (_, _) => Dispatcher.BeginInvoke(UpdateDockGlass, DispatcherPriority.Loaded);
        shell.SizeChanged += (_, e) =>
        {
            UpdateDockGlass();
            var shape = NotchShape(e.NewSize.Width, e.NewSize.Height);
            body.Data = shape;
            glow.Data = shape;
            backdropHost.Clip = shape;
            PaintBevel(shape, e.NewSize.Height);
            stateGlow.Clip = shape;
            foreach (var layer in stateGlowTail.Children.OfType<System.Windows.Shapes.Path>()) layer.Clip = shape;
            if (glowState != AgentGlowState.Off && !glowStill) SetupBeam(e.NewSize.Width, e.NewSize.Height);   // el haz sigue la forma fotograma a fotograma (sin reiniciarse); el aro fijo, el contorno cerrado
            else { stateGlow.Data = shape; foreach (var layer in stateGlowTail.Children.OfType<System.Windows.Shapes.Path>()) layer.Data = shape; }
            // Mientras la forma crece o se recoge, el cuerpo pasa de negro macizo a vidrio según lo abierta que esté (cúbico: casi todo el vidrio llega al final).
            double full = lastExpanded is { } expanded ? expanded.Height / Math.Max(0.5, scale) : e.NewSize.Height;
            double o = Math.Clamp((e.NewSize.Height - 45) / Math.Max(1, full - 45), 0, 1);
            glowOpen = o;
            ShapeGlow();
            o = o * o * o;
            if (glassBody && !Theme.Flat && Math.Abs(o - openness) > 0.015) { openness = o; PaintBody(); }
        };
        // El contenido se recorta a mano (el borde de arriba, fuera de la vista, queda recto).
        // El recorte sigue a la forma: de un borde del cuerpo al otro (el hombro de ese alto, medido desde el margen del contenedor) y con su radio.
        holder.SizeChanged += (_, e) =>
        {
            var (ear, radius) = ShapeMetrics(e.NewSize.Height);
            double inset = Math.Max(0, ear - PillEarDip);
            // Cerrado, la mascota sobresale PillEarDip por lado para pintar sus propios hombros en blanco: el recorte no debe cortárselos.
            // Solo los hombros de arriba: ensanchar todo el recorte le quitaba la curva de abajo y la mascota quedaba cuadrada.
            double w = Math.Max(0, e.NewSize.Width - 2 * inset);
            Geometry clip = new RectangleGeometry(new Rect(inset, -radius, w, e.NewSize.Height + radius), radius, radius);
            if (ear <= PillEarDip + 0.5)
                clip = new GeometryGroup { FillRule = FillRule.Nonzero, Children = { clip, new RectangleGeometry(new Rect(inset - PillEarDip, -radius, w + 2 * PillEarDip, radius + PillEarDip)) } };
            holder.Clip = clip;
        };
        PaintShell();
        Theme.Changed += PaintShell;

        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            // Ventana de herramienta (sin Alt+Tab ni barra de tareas) que no roba el foco al pasar el ratón.
            long ex = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
            Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, new IntPtr((ex | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE) & ~Win32.WS_EX_APPWINDOW));
            hwndSource = HwndSource.FromHwnd(hwnd);
            hwndSource?.AddHook(OnWindowMessage);
        };
        transitionTimer.Tick += (_, _) => OnTransitionTimer();
        fallbackTimer.Tick += (_, _) => OnFallbackTick();
        // Ya no se fija con un clic en la vista corta: se abre con el botón del mini dock (`OpenFullFromPeek`).
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (TextInputAt(e.OriginalSource as DependencyObject) is { } box) FocusForTyping(box);
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && !model.CloseTopOverlay()) Collapse(); };
        Deactivated += (_, _) => { if (!sticky && !typing) Collapse(); };   // fijado abierto: perder el foco no lo cierra
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Apply, DispatcherPriority.Background);
        // Solo si el contenido cambió de tamaño de verdad: mientras la animación encoge o agranda el negro, la vista
        // también cambia de tamaño visible, y reaccionar a eso reiniciaría la animación una y otra vez.
        view.SizeChanged += OnViewSizeChanged;
        peek.IsVisibleChanged += (_, _) => model.SetPeekVisible(peek.IsVisible);
        peek.SizeChanged += (_, _) => { if (state.Mode != NotchMode.Collapsed) RelayoutIfContentChanged(); };
        peek.DockLayoutChanged += () =>
        {
            if (state.Mode == NotchMode.Collapsed) return;
            dockResize = true;   // el mini dock solo mueve el borde de abajo unos píxeles: se anima suave, sin el rebote de abrir
            try { RelayoutIfContentChanged(); } finally { dockResize = false; }
        };
        model.AttentionRequested += OnAttention;
        model.TrayFilesAdded += OnTrayFiles;
        model.Hub.Shelf.PropertyChanged += OnShelfChanged;
        // Archivos arrastrados al notch: se abre la bandeja y se pueden soltar en ella.
        // WPF solo da el arrastre al elemento bajo el cursor si ese elemento lo permite: se permite en todo lo que cubre la isla.
        AllowDrop = true;
        foreach (var part in new UIElement[] { shell, holder, peek, view, dock, pill }) part.AllowDrop = true;
        ((UIElement)Content).AllowDrop = true;
        PreviewDragEnter += OnDragOver;
        PreviewDragOver += OnDragOver;
        PreviewDrop += OnDrop;
        model.Hub.Ticked += RefreshPill;
        model.PropertyChanged += OnModelChanged;
        model.ApprovalRequested += OnApprovalRequested;
        model.ApprovalsCleared += OnApprovalsCleared;
        SyncOptionalTimers();
        glowTimer.Start();
        ApplyStateGlow();
    }

    /// Enciende, cambia o apaga la luz de los agentes. Con movimiento reducido o alto contraste no late: queda fija (y en alto contraste, en el color del texto).
    private void ApplyStateGlow()
    {
        var next = model.AgentGlowNow;
        PetBridge.Publish(next);   // la mascota de la placa reacciona a lo mismo que ilumina el notch (solo si el complemento esta instalado)
        bool flat = Theme.Flat, still = model.Prefs.ReducedMotion || flat;
        if (next == glowState && still == glowStill) { beamPending = null; beamWait.Stop(); return; }
        // Si ya hay un haz dando la vuelta y llega otro estado (de cualquier color), el nuevo espera a que el actual acabe su vuelta y entonces entra: nunca se pisan.
        if (!beamLapDone && glowState != AgentGlowState.Off && next != AgentGlowState.Off && !glowStill && !still && beamTurn > 0)
        {
            if (beamPending == next) return;
            beamPending = next;
            double elapsed = (DateTime.UtcNow - beamStartedAt).TotalSeconds;
            beamWait.Stop();
            beamWait.Interval = TimeSpan.FromSeconds(Math.Max(0.05, beamTurn - elapsed % beamTurn));
            beamWait.Start();
            return;
        }
        beamLapDone = false;
        beamPending = null;
        beamWait.Stop();
        glowState = next;
        glowStill = still;
        StopBeam();
        if (next == AgentGlowState.Off) { stateGlow.Visibility = stateGlowTail.Visibility = Visibility.Collapsed; return; }
        var (r, g, b) = AgentGlowRules.Color(next);
        stateGlow.Stroke = flat ? Theme.Brush(Theme.Current.Fg) : new SolidColorBrush(Color.FromRgb(r, g, b));
        foreach (var layer in stateGlowTail.Children.OfType<System.Windows.Shapes.Path>()) layer.Stroke = stateGlow.Stroke;
        stateGlow.Effect = flat || !still ? null : new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(r, g, b), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.5 };
        stateGlow.Visibility = Visibility.Visible;
        if (still) { stateGlow.Opacity = 1; stateGlow.Data = body.Data; stateGlow.StrokeDashArray = null; stateGlow.StrokeThickness = 2.6; stateGlowTail.Visibility = Visibility.Collapsed; return; }   // fija: el aro entero, sin haz
        StartBeam();
        if (next == AgentGlowState.Done) { stateGlow.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.15, AgentGlowRules.DoneFor)); stateGlowTail.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, AgentGlowRules.DoneFor)); }   // «terminó»: el haz da unas vueltas y se apaga solo
    }

    private void StopBeam()
    {
        if (beamHooked) { CompositionTarget.Rendering -= OnBeamFrame; beamHooked = false; }
        stateGlow.BeginAnimation(OpacityProperty, null);
        stateGlowTail.BeginAnimation(OpacityProperty, null);
        stateGlow.Opacity = stateGlowTail.Opacity = 1;
    }

    /// El haz de luz: una punta nítida con una cola que se desvanece detrás, que entra por la oreja derecha, recorre el notch y sale por la izquierda (bordes con guiones
    /// cuyo desfase se pone en cada fotograma). Como el largo y la fase se calculan en cada fotograma, el haz sigue la forma mientras el notch se abre o cambia de widget.
    private void StartBeam()
    {
        while (stateGlowTail.Children.Count < BeamLayers) stateGlowTail.Children.Add(Bevel(2.6));
        var layers = stateGlowTail.Children.OfType<System.Windows.Shapes.Path>().ToArray();
        beamParts = [.. layers, stateGlow];
        foreach (var part in beamParts)
        {
            part.Stroke = stateGlow.Stroke;
            part.StrokeThickness = 3;   // fino: más ancho se ve como un bloque en la pastilla (solo mide ~38 px)
            part.OpacityMask = null;   // la máscara del canto de vidrio (sin luz arriba) cortaba la cola de golpe
            part.StrokeDashCap = PenLineCap.Flat;
            part.Visibility = Visibility.Visible;
            part.Clip = stateGlow.Clip;
        }
        // Un solo degradado de punta a cola: el borde delantero es nítido y a pleno color, y la luz baja de forma continua hasta cero en el extremo de atrás.
        // Cada capa suma su parte de modo que lo acumulado a distancia d de la punta sea ~ (1 - d/largo)^1,4.
        int n = layers.Length;
        double Cover(int count) => 0.97 * Math.Pow((double)count / n, 1.4);
        for (int i = 0; i < n; i++) layers[i].Opacity = 1 - (1 - Cover(n - i)) / (1 - Cover(n - i - 1));
        stateGlow.Opacity = 1;
        stateGlowTail.Visibility = Visibility.Visible;
        // Más lenta que un latido: una vuelta de varios segundos (un haz rápido se ve nervioso). 1,55 = (camino + cola) / camino.
        beamTurn = (glowState == AgentGlowState.Done ? 3.2 : AgentGlowRules.PulseSeconds(glowState) * 2.4) * 1.55;
        beamStartedAt = DateTime.UtcNow;
        SetupBeam(shell.ActualWidth, shell.ActualHeight);
        if (!beamHooked) { CompositionTarget.Rendering += OnBeamFrame; beamHooked = true; }
    }

    /// Ajusta el contorno y los guiones al tamaño actual: el periodo (camino + cola) hace que el haz no vuelva a entrar hasta que la cola ha salido.
    private void SetupBeam(double w, double h)
    {
        if (w < 1 || h < 1 || beamParts.Length == 0) return;
        const double t = 3;
        var (e, r) = ShapeMetrics(h);
        double length = Math.PI * (e + r) + 2 * (h - r - e) + (w - 2 * e - 2 * r);   // las dos orejas y las dos esquinas, los lados y la base
        if (length < 20) return;
        var shape = NotchBeamPath(w, h);
        double path = length / t, tail = 0.55 * path;
        beamPeriod = path + tail;
        int n = beamParts.Length - 1;
        if (beamDash.Length != n + 1) beamDash = new double[n + 1];
        for (int i = 0; i < n; i++) beamDash[i] = tail * (i + 1.0) / n;
        beamDash[n] = tail / n;   // la punta: el tramo más corto, a pleno color
        for (int i = 0; i <= n; i++)
        {
            beamParts[i].Data = shape;
            beamParts[i].StrokeDashArray = [beamDash[i], beamPeriod - beamDash[i]];
        }
    }

    private void OnBeamFrame(object? sender, EventArgs e)
    {
        if (beamTurn <= 0 || beamParts.Length == 0 || beamPeriod <= 0) return;
        double front = (DateTime.UtcNow - beamStartedAt).TotalSeconds % beamTurn / beamTurn * beamPeriod;
        for (int i = 0; i < beamParts.Length; i++) beamParts[i].StrokeDashOffset = beamDash[i] - front;   // el guion ocupa [frente - largo, frente]
    }

    private bool pinnedForApproval;

    /// Un agente pide permiso: el notch se abre y se queda fijo hasta que se responda (si ya estaba abierto por el usuario, no se cierra después).
    private void OnApprovalRequested()
    {
        if (state.Mode == NotchMode.Pinned) return;
        // Con algo a pantalla completa (un juego, un vídeo) el notch no se abre ni se trae al frente: la petición espera en la cola y se enseña al salir.
        if (hideReason != HideReason.None || model.Supervisor.Game.Active) { approvalWaitingForScreen = true; return; }
        pinnedForApproval = true;
        Pin();
    }

    private bool approvalWaitingForScreen;

    private void OnApprovalsCleared()
    {
        if (!pinnedForApproval) return;
        pinnedForApproval = false;
        if (state.Mode == NotchMode.Pinned) Collapse();
    }

    private void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        ApplyStateGlow();
        SyncOptionalTimers();
        if (e.PropertyName is nameof(PanelViewModel.LiveActivities) or nameof(PanelViewModel.CameraIndicatorLabel))
        {
            RefreshPill();
            return;
        }
        if (DockSignature() != dockSignature) RebuildDock();
        RefreshPill();
        PaintTabs();
        PaintBody();
        if (state.Mode != NotchMode.Collapsed) Dispatcher.BeginInvoke(RelayoutIfContentChanged, DispatcherPriority.Background);
    }

    private void OnViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (state.Mode != NotchMode.Collapsed) Dispatcher.BeginInvoke(RelayoutIfContentChanged, DispatcherPriority.Background);
    }

    // MARK: ciclo de vida

    public void Start()
    {
        new WindowInteropHelper(this).EnsureHandle();
        model.Camera.StartMonitoring();
        model.WideLayout = true;
        model.Show(PanelView.Home);
        var home = lockedScreenId is null ? null : Screens.List().FirstOrDefault(sc => sc.Id == lockedScreenId);
        PickMonitor(home?.Handle ?? Win32.MonitorFromPoint(default, 1 /* MONITOR_DEFAULTTOPRIMARY */));
        visibility.Start();
        bool mouseHooked = mouseMonitor.Start();
        EvaluateVisibility();
        if (gate.Shown) Apply();
        if (!mouseHooked)
        {
            // Si Windows rechaza el gancho del ratón, se sigue el cursor por sondeo.
            fallbackTimer.Interval = TimeSpan.FromMilliseconds(40);
            fallbackTimer.Start();
        }
        if (Win32.GetCursorPos(out var cursor)) OnMouseMoved(cursor);
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
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.OriginalSource is DependencyObject target && TeamAttach.Inside(target)) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        // Un mosaico del propio Estante que se arrastra por encima del notch no lo ilumina ni fija el panel.
        if (FileDrag.Active is FrameworkElement { DataContext: ShelfTile }) return;
        // Si el Estante está visible, usa la posición real del cursor para mover su luz de arrastre.
        if (model.SelectPeekWidget("shelf") || model.Hub.ShelfShown)
        {
            var shelf = model.Hub.Shelf;
            if (Win32.GetCursorPos(out var cursor)) shelf.UpdateDragPosition(new Point(cursor.X, cursor.Y));
            else shelf.SetDragging(true);
            return;
        }
        // Si no está visible, el archivo va a la Bandeja.
        model.SetTrayDragging(true);
        OpenTray();
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.OriginalSource is DependencyObject target && TeamAttach.Inside(target)) return;
        if (FileDrag.Active is FrameworkElement { DataContext: ShelfTile }) { e.Handled = true; return; }
        var files = e.Data.GetData(DataFormats.FileDrop, true) switch
        {
            string[] paths => paths,
            System.Collections.Specialized.StringCollection paths => paths.Cast<string>().ToArray(),
            _ => [],
        };
        if (files.Length > 0)
        {
            if (model.SelectPeekWidget("shelf") || model.Hub.ShelfShown) model.Hub.Shelf.AddFiles(files, Win32.GetCursorPos(out var cursor) ? new Point(cursor.X, cursor.Y) : null);
            else model.AddToTray(files);
        }
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
        model.Hub.Shelf.PropertyChanged -= OnShelfChanged;
        model.Hub.Ticked -= RefreshPill;
        model.PropertyChanged -= OnModelChanged;
        glowTimer.Stop();
        model.ApprovalRequested -= OnApprovalRequested;
        model.ApprovalsCleared -= OnApprovalsCleared;
        view.SizeChanged -= OnViewSizeChanged;
        peek.Stop();
        lens?.Dispose();
        model.Camera.StopMonitoring();
        if (model.ShowingCamera || model.ShowingChat || model.ShowingAgents) model.Show(PanelView.Main);
        else model.Camera.StopPreview();
        transitionTimer.Stop();
        fallbackTimer.Stop();
        mouseMonitor.Dispose();
        visibility.Dispose();
        gateTimer.Stop();
        hudTimer.Stop();
        lyricClock.Stop();
        mascotClock.Stop();
        mascot.Stop();
        hwndSource?.RemoveHook(OnWindowMessage);
        hwndSource = null;
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
        if (model.Hub.Shelf.IsRemoving) { collapseAfterShelfRemoval = true; return; }
        collapseAfterShelfRemoval = false;
        transitionTimer.Stop();
        scheduledMode = null;
        sticky = false;
        SetNoActivate(true);
        state.Close();
        model.CloseAllOverlays();
        Apply();
    }

    private void OnShelfChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!collapseAfterShelfRemoval || model.Hub.Shelf.IsRemoving) return;
        collapseAfterShelfRemoval = false;
        Dispatcher.BeginInvoke(Collapse, DispatcherPriority.Background);
    }

    private static System.Windows.Controls.Primitives.TextBoxBase? TextInputAt(DependencyObject? source)
    {
        for (var d = source; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is System.Windows.Controls.Primitives.TextBoxBase box) return box;
        return null;
    }

    /// El notch se abre sin activarse (para no robar el foco a la app que usas): al pulsar un cuadro de texto aparecía el cursor pero las teclas
    /// no llegaban, porque la ventana no estaba activa de verdad. Aquí se activa, se le da el foco al cuadro y no se cierra mientras se escribe.
    internal void FocusForTyping(System.Windows.Controls.Primitives.TextBoxBase box)
    {
        typing = true;
        box.LostKeyboardFocus += OnTypingEnded;
        SetNoActivate(false);
        if (!IsActive) Activate();
        if (hwnd != IntPtr.Zero) Win32.SetForegroundWindow(hwnd);
        Dispatcher.BeginInvoke(() => Keyboard.Focus(box), DispatcherPriority.Input);
        void OnTypingEnded(object? s, KeyboardFocusChangedEventArgs e) { box.LostKeyboardFocus -= OnTypingEnded; typing = false; }
    }

    private void Pin()
    {
        transitionTimer.Stop();
        scheduledMode = null;
        state.Pin();
        Pinned?.Invoke(this);
        EvaluateVisibility();
        model.ClearNotice();   // abrirlo del todo es verlo: la campana de la cabecera se apaga
        SetNoActivate(false);
        Apply();
        if (NoForeground) return;
        Activate();
        if (hwnd != IntPtr.Zero) Win32.SetForegroundWindow(hwnd);
    }

    /// Solo para las pruebas visuales: abrir el notch no trae la ventana al frente (no le quita el foco a un juego a pantalla completa).
    public static bool NoForeground;

    // MARK: el ratón

    private void OnTransitionTimer()
    {
        transitionTimer.Stop();
        scheduledMode = null;
        if (Win32.GetCursorPos(out var cursor)) OnMouseMoved(cursor);
    }

    private void OnFallbackTick()
    {
        if (Win32.GetCursorPos(out var cursor)) OnMouseMoved(cursor);
    }

    private void OnMouseMoved(Win32.POINT cursor)
    {
        if (closing) return;
        if (state.Mode == NotchMode.Expanded) peek.TrackCursor(cursor.X, cursor.Y);   // el mini dock de los puntos no depende de MouseEnter de WPF
        if (!gate.Shown)
        {
            // Oculto en este monitor (pantalla completa, app oculta…): si el cursor pasó a otro, el notch vive allí y se vuelve a decidir.
            var other = Win32.MonitorFromPoint(cursor, 2 /* MONITOR_DEFAULTTONEAREST */);
            if (lockedScreenId is null && other != IntPtr.Zero && other != monitorHandle && state.Mode == NotchMode.Collapsed) { PickMonitor(other); EvaluateVisibility(); }
            return;
        }
        if (state.Mode == NotchMode.Collapsed) FollowCursorMonitor(cursor);
        if (state.Mode == NotchMode.Pinned) { transitionTimer.Stop(); scheduledMode = null; return; }
        // Cerrado: la zona de la pastilla (y, mientras la forma se recoge, también lo que aún se ve: volver a entrar la reabre desde donde está). Abierto: la forma con un borde
        // generoso, para que un roce o salirse un par de píxeles por el lateral no la cierre.
        var hoverRect = state.Mode == NotchMode.Collapsed
            ? lingerRect is { } linger ? NotchLayout.Union(rects.Hot, NotchLayout.Inflate(linger, (int)Math.Round(10 * scale))) : rects.Hot
            : NotchLayout.Inflate(activeRect, (int)Math.Round(20 * scale));
        bool inside = NotchLayout.Contains(hoverRect, cursor.X, cursor.Y);
        var previousMode = state.Mode;
        bool hoverOpens = !model.Prefs.OpenOnClick;
        bool keepOpen = model.Hub.Shelf.IsRemoving;
        if (state.Update(inside && (hoverOpens || state.Mode != NotchMode.Collapsed), false, keepOpen, clock.Elapsed.TotalSeconds))
        {
            if (previousMode != NotchMode.Collapsed && state.Mode == NotchMode.Collapsed) model.CloseAllOverlays();
            Apply();
        }
        if (state.Mode == NotchMode.Collapsed && inside && hoverOpens && !state.Hidden)
        {
            // La copia del fondo (lo más pesado de abrir) se hace durante el retardo de entrada, no a mitad de la animación.
            if (!BackdropFresh && lingerRect is null) { CaptureBackdrop(); backdropAt = clock.ElapsedMilliseconds; }
            PrepareWindowForOpen();   // la ventana crece ahora, durante el retardo de entrada: si crece al empezar la animación, tarda ~120 ms en aplicarse y la forma queda recortada
            ScheduleTransition(NotchMode.Expanded, NotchState.EnterDelay);
        }
        else if (state.Mode == NotchMode.Expanded && !inside && !state.Hidden && !keepOpen) ScheduleTransition(NotchMode.Collapsed, NotchState.LeaveDelay);
        else { transitionTimer.Stop(); scheduledMode = null; }
    }

    /// Con «abrir con clic», un clic en la zona de ratón del notch (un poco más ancha que la pastilla y hasta el borde de la pantalla) también lo abre, no solo
    /// sobre la pastilla. Sobre la propia pastilla lo gestiona su botón.
    private void OnMouseClicked(Win32.POINT cursor)
    {
        if (closing || !gate.Shown || state.Mode != NotchMode.Collapsed || !model.Prefs.OpenOnClick || state.Hidden) return;
        if (!NotchLayout.Contains(rects.Hot, cursor.X, cursor.Y)) return;
        if (placed is { } window && NotchLayout.Contains(window, cursor.X, cursor.Y)) return;
        state.Expand();
        Apply();
    }

    /// Agranda la ventana (transparente) al tamaño con el que se abrirá, antes de que empiece la animación. Cambiar el tamaño de una ventana en capas tarda un buen rato en
    /// aplicarse; si se hacía justo al abrir, la forma crecía recortada por la ventana vieja y la animación se veía a saltos (incluso con un pequeño hundimiento al empezar).
    /// Lo que ocupa la vista corta abierta, con margen para el rebote.
    private Win32.RECT OpenFootprint()
    {
        var open = Wings(rects.Expanded, (int)Math.Round(shapeEar * scale));
        int growX = (int)Math.Round(10 * scale) + (int)(open.Width * 0.03), growY = (int)Math.Round(10 * scale) + (int)(open.Height * 0.07);
        return new Win32.RECT { Left = open.Left - growX, Top = open.Top, Right = open.Right + growX, Bottom = open.Bottom + growY };
    }

    /// La ventana de la pastilla ya abarca lo que ocupará abierta (es transparente y no tapa clics): cambiar su tamaño justo al pasar el ratón hacía que la
    /// pastilla saltara de sitio uno o dos fotogramas, que se veía como un parpadeo.
    private Win32.RECT CollapsedWindow(Win32.RECT pill) => rects.Expanded.Width > 0 ? NotchLayout.Union(pill, OpenFootprint()) : pill;

    private void PrepareWindowForOpen()
    {
        if (hwnd == IntPtr.Zero || state.Mode != NotchMode.Collapsed || placed is not { } current) return;
        var open = OpenFootprint();
        SyncPeekWidth();   // el ancho de la vista corta (420 o 520) se fija ahora, no a mitad de la apertura
        var target = NotchLayout.Union(current, open);
        if (target.Left != current.Left || target.Top != current.Top || target.Right != current.Right || target.Bottom != current.Bottom) Place(target);
    }

    private void ScheduleTransition(NotchMode next, double seconds)
    {
        if (scheduledMode == next && transitionTimer.IsEnabled) return;
        transitionTimer.Stop();
        scheduledMode = next;
        transitionTimer.Interval = TimeSpan.FromSeconds(seconds);
        transitionTimer.Start();
    }

    private IntPtr OnWindowMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x001A /* WM_SETTINGCHANGE */ && gate.Shown)
        {
            EvaluateVisibility();
            if (Win32.GetCursorPos(out var cursor)) OnMouseMoved(cursor);
        }
        return IntPtr.Zero;
    }

    /// Decide si el notch se enseña ahora (ver `NotchVisibility`) y lo muestra u oculta con un fundido corto. Se llama por eventos del sistema y cada 2 s.
    private void EvaluateVisibility()
    {
        if (closing || hwnd == IntPtr.Zero) return;
        var snapshot = visibility.Capture(monitorHandle, monitor, model.Prefs.HideInFullscreen, model.Prefs.List(PrefKeys.HiddenApps), state.Mode == NotchMode.Pinned);
        hideReason = NotchVisibility.Decide(snapshot);
        bool changed = gate.Update(hideReason, clock.Elapsed.TotalSeconds);
        // Si ya se puede enseñar pero falta el retardo anti-parpadeo, se vuelve a preguntar justo cuando pase.
        if (gate.PendingUntil is { } until)
        {
            gateTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.02, until - clock.Elapsed.TotalSeconds));
            gateTimer.Start();
        }
        else gateTimer.Stop();
        if (!changed) return;
        if (gate.Shown) ShowNotch(); else HideNotch();
        ShownChanged?.Invoke(gate.Shown);
        // Salió de la pantalla completa y había una petición esperando: ahora sí se abre.
        if (gate.Shown && approvalWaitingForScreen && model.HasApproval && !model.Supervisor.Game.Active)
        {
            approvalWaitingForScreen = false;
            Dispatcher.BeginInvoke(() => { if (state.Mode != NotchMode.Pinned && model.HasApproval) { pinnedForApproval = true; Pin(); } });
        }
    }

    private void HideNotch()
    {
        // Una sesión bloqueada o el escritorio seguro apartan incluso un notch fijado: antes se cierra su estado.
        if (state.Mode != NotchMode.Collapsed)
        {
            sticky = false;
            state.Close();
            model.CloseAllOverlays();
        }
        transitionTimer.Stop();
        scheduledMode = null;
        BeginAnimation(OpacityProperty, null);
        void Finish()
        {
            if (gate.Shown) return;   // volvió a enseñarse mientras se desvanecía
            Hide();
            Opacity = 1;
            shownOpen = null;
            painted = null;
        }
        if (!IsVisible || model.Prefs.ReducedMotion) { Finish(); return; }
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => Finish();
        BeginAnimation(OpacityProperty, fade);
    }

    private void ShowNotch()
    {
        BeginAnimation(OpacityProperty, null);
        shownOpen = null;
        painted = null;
        bool fade = !model.Prefs.ReducedMotion;
        Opacity = fade ? 0 : 1;
        Apply();
        if (fade) BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        if (Win32.GetCursorPos(out var cursor)) OnMouseMoved(cursor);
    }

    /// Con el notch cerrado, tocar el borde superior de otro monitor lo lleva allí.
    private void FollowCursorMonitor(Win32.POINT cursor)
    {
        if (lockedScreenId is not null) return;   // fijado a su pantalla: no salta
        var handle = Win32.MonitorFromPoint(cursor, 2 /* MONITOR_DEFAULTTONEAREST */);
        if (handle == monitorHandle || handle == IntPtr.Zero) return;
        var info = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(handle, ref info)) return;
        Win32.GetDpiForMonitor(handle, 0, out uint dpi, out _);
        var there = NotchLayout.Place(info.rcMonitor, info.rcWork, (dpi == 0 ? 96 : dpi) / 96.0, double.IsNaN(view.Width) ? view.DesiredSize.Width : view.Width, 0);
        if (!NotchLayout.Contains(there.Hot, cursor.X, cursor.Y)) return;
        PickMonitor(handle);
        shownOpen = null;
        painted = null;
        EvaluateVisibility();   // lo que lo aparta puede ser distinto en este monitor
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

    /// La vista al pasar el ratón con un widget por página es una isla estrecha; con 2 o 4, tan ancha como el panel.
    private void SyncPeekWidth()
    {
        bool single = model.PeekPerPage == 1;
        peek.Width = single ? (model.LyricsShown ? PeekLyricsWidth : PeekSingleWidth) : double.IsNaN(view.Width) ? 640 : view.Width;
        peek.SetContentMinHeight(single ? PeekSingleHeight : 0);   // todas las páginas del mismo alto: los puntos de página siempre en el mismo sitio
    }
    private const double PeekSingleHeight = 126;   // 140 en Nusa; aquí los puntos quedan más cerca de los controles
    private const double PeekSingleWidth = 420;
    private const double NaturalPanelWidth = 640;

    private bool dockResize;

    private void RelayoutIfContentChanged()
    {
        view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SyncPeekWidth();
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
        if (!gate.Shown) return;   // oculto: nada que pintar hasta que vuelva a enseñarse
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
        SyncPeekWidth();
        peek.Measure(new Size(view.DesiredSize.Width, double.PositiveInfinity));
        (lastDesired, lastPeek) = (view.DesiredSize, peek.DesiredSize);
        var mode = state.Mode;
        // El hover enseña la versión corta; el clic, el panel entero.
        var content = mode == NotchMode.Pinned ? view.DesiredSize : peek.DesiredSize;
        rects = NotchLayout.Place(monitor, work, scale, content.Width, content.Height, pillWidthDip, stripNow, pillHeightDip);
        bool open = mode != NotchMode.Collapsed;
        if (!IsVisible) Show();

        // La vista corta con un widget: las orejas y esquinas de Nusa (22, 32 y 47 con el mini dock) con el vidrio de siempre; el resto, la forma del panel.
        bool compactPeek = mode == NotchMode.Expanded && model.PeekPerPage == 1;
        (shapeEar, shapeRadius, shapeRadiusTall) = compactPeek ? (22, 32, 47) : (EarDip, CornerRadiusDip, CornerRadiusDip);
        double earDip = shapeEar;
        int ear = (int)Math.Round(earDip * scale);
        var dockRect = NotchLayout.WithOvershoot(NotchLayout.DockBelow(rects.Expanded, scale, dockWidth), scale);
        var pillRect = Wings(rects.Pill, (int)Math.Round(PillEarDip * scale));
        // Los botones solo salen con el notch fijado (clic): en la vista al pasar el ratón no hay dock ni sitio para él.
        bool showDock = mode == NotchMode.Pinned;
        var openRect = showDock ? NotchLayout.Union(Wings(rects.Expanded, ear), dockRect) : Wings(rects.Expanded, ear);
        // La ventana deja un margen transparente (más cuanto más grande es lo que se abre) para que el rebote al abrir no se recorte. No tapa clics y el ratón usa sus propias medidas.
        int growX = (int)Math.Round(10 * scale) + (int)(openRect.Width * 0.03), growY = (int)Math.Round(10 * scale) + (int)(openRect.Height * 0.07);
        openRect = new Win32.RECT { Left = openRect.Left - growX, Top = openRect.Top, Right = openRect.Right + growX, Bottom = openRect.Bottom + growY };
        activeRect = !open ? rects.Pill : showDock ? NotchLayout.Union(rects.Expanded, dockRect) : rects.Expanded;
        var pillSize = new Size(rects.Pill.Width / scale + 2 * PillEarDip, rects.Pill.Height / scale);
        var openSize = new Size(rects.Expanded.Width / scale + 2 * earDip, rects.Expanded.Height / scale);
        var now = (mode, open ? openRect : pillRect, open ? openSize : pillSize);
        var previousMode = painted?.Mode;
        var previousWindow = painted?.Window;
        if (painted is { } before && before.Mode == now.mode && before.Shell == now.Item3
            && before.Window.Left == now.Item2.Left && before.Window.Top == now.Item2.Top
            && before.Window.Right == now.Item2.Right && before.Window.Bottom == now.Item2.Bottom) return;
        painted = now;
        bool modeChanged = previousMode != mode;   // pastilla ↔ versión corta ↔ panel entero
        if (open)
        {
            closeCleanupPending = false;
            // Lo de detrás se copia antes de que la ventana crezca; el vidrio se pinta (con su tinte) una vez que hay copia.
            if (previousMode is null or NotchMode.Collapsed)
            {
                // Si el notch se reabre mientras aún se recoge (lingerRect), no se copia el fondo: la copia saldría con el propio notch negro dentro, y el vidrio enseñaba
                // por un instante un borde negro grande (como dos notchs a la vez). Se sigue usando la copia anterior, que es limpia.
                if (!BackdropFresh && lingerRect is null) CaptureBackdrop();
                backdropAt = -10_000;   // cada apertura usa su copia: la siguiente vez, una nueva
            }
            if (!glassBody) { glassBody = true; PaintBody(); PaintBevel((Geometry)body.Data, shell.ActualHeight); }
            // La ventana crece primero (transparente) y luego se abre la pastilla; al revés al cerrar.
            Place(placed is { } already ? NotchLayout.Union(already, NotchLayout.Union(openRect, OpenFootprint())) : NotchLayout.Union(openRect, OpenFootprint()));   // no más pequeña que la ventana de la pastilla: si encogiera, la pastilla daría un salto
            bool crossfade = modeChanged && (previousMode is null or NotchMode.Collapsed);
            if (!crossfade) pill.Visibility = Visibility.Collapsed;
            peek.Visibility = mode == NotchMode.Expanded ? Visibility.Visible : Visibility.Hidden;
            view.Visibility = mode == NotchMode.Pinned ? Visibility.Visible : Visibility.Hidden;   // Hidden y no Collapsed: Collapsed mediría 0
            if (mode == NotchMode.Pinned) view.Width = openSize.Width - 2 * earDip;
            else view.ClearValue(WidthProperty);   // el ancho natural del panel: el de la vista corta (más estrecha) no debe quedarse puesto al hacer clic
            SyncPeekWidth();
            dock.Visibility = showDock ? Visibility.Visible : Visibility.Collapsed;
            PaintTabs();
            // Como en Nusa: la forma crece rápido y frenando (150 ms) y el contenido se cruza con la pastilla: ella se desvanece (70 ms) mientras la
            // vista corta o el panel aparecen (110 ms, tras 40 ms), para que el borde que crece no los rebane.
            holder.BeginAnimation(OpacityProperty, null);
            holder.Opacity = 1;
            if (modeChanged)
            {
                FadeIn(mode == NotchMode.Expanded ? peek : view, 160, 60);   // el contenido entra enseguida: si tarda, la forma crece vacía y se ve un bloque negro
                if (crossfade) FadeOut(pill, 120, 0, () => { if (state.Mode != NotchMode.Collapsed) pill.Visibility = Visibility.Collapsed; });
            }
            lastExpanded = activeRect;
            lingerRect = null;
            Animate(openSize.Width, openSize.Height, null, opening: !dockResize && openSize.Height > shell.ActualHeight, resize: dockResize);
        }
        else
        {
            if (shownOpen is null) { Place(CollapsedWindow(pillRect)); shell.BeginAnimation(WidthProperty, null); shell.BeginAnimation(HeightProperty, null); }
            // Si la pastilla cambia de ancho (música, datos), la ventana abarca primero la vieja y la nueva para que la forma se anime entera por dentro.
            else if (previousMode == NotchMode.Collapsed && previousWindow is { } was)
            {
                // La ventana solo crece: encogerla (al final de una retracción) hace que Windows redibuje toda la ventana y la pastilla parpadeaba. Lo que sobra
                // es transparente (no tapa clics) y el pase del ratón usa sus propias medidas.
                var target = NotchLayout.Union(placed ?? was, CollapsedWindow(pillRect));
                if (placed is not { } current || current.Left != target.Left || current.Top != target.Top || current.Right != target.Right || current.Bottom != target.Bottom) Place(target);
            }
            dock.Visibility = Visibility.Collapsed;
            // Al cerrar: el contenido se desvanece rápido (60 ms) mientras la forma se encoge (140 ms) y la pastilla vuelve con un fundido.
            holder.BeginAnimation(OpacityProperty, null);
            holder.Opacity = 1;
            if (modeChanged)
            {
                FadeOut(peek.Visibility == Visibility.Visible ? peek : view, 220, 0);
                if (!stripNow)
                {
                    pill.Visibility = Visibility.Visible;
                    FadeIn(pill, 130, 100);   // la pastilla vuelve en cuanto el contenido se ha ido: con más espera el notch se quedaba un instante negro y vacío
                }
            }
            if (modeChanged) lingerRect = lastExpanded;   // mientras la forma se recoge, sigue contando para el ratón
            bool finishClose = modeChanged || closeCleanupPending;
            closeCleanupPending = finishClose;
            bool resizeOnly = !finishClose;   // el cierre conserva su limpieza si un cambio de tamaño reemplaza la animación
            Animate(pillSize.Width, pillSize.Height, resize: resizeOnly, done: finishClose ? (Action)(() =>
            {
                closeCleanupPending = false;
                lingerRect = null;
                glassBody = false;
                backdrop.Hide();
                PaintBody();
                bevelVersion = -1;
                if (body.Data is { } shape) PaintBevel(shape, shell.ActualHeight);
                Place(CollapsedWindow(pillRect));
                view.Visibility = Visibility.Hidden;
                peek.Visibility = Visibility.Hidden;
                foreach (var part in new UIElement[] { view, peek }) { part.BeginAnimation(OpacityProperty, null); part.Opacity = 1; }
                pill.Visibility = stripNow ? Visibility.Hidden : Visibility.Visible;
                if (!model.ShowingHome) model.Show(PanelView.Home);   // la próxima vez se abre en el Home
            }) : null);
        }
        shownOpen = open;
        laidOut = true;
        UpdateGlow();
    }

    private void FadeIn(UIElement element, int ms, int delayMs)
    {
        element.BeginAnimation(OpacityProperty, null);
        if (model.Prefs.ReducedMotion) { element.Opacity = 1; return; }
        element.Opacity = 0;
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms * model.Prefs.AnimSpeed))
            { BeginTime = TimeSpan.FromMilliseconds(delayMs * model.Prefs.AnimSpeed), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }

    private void FadeOut(UIElement element, int ms, int delayMs, Action? done = null)
    {
        element.BeginAnimation(OpacityProperty, null);
        if (model.Prefs.ReducedMotion) { element.Opacity = 0; done?.Invoke(); return; }
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(ms * model.Prefs.AnimSpeed)) { BeginTime = TimeSpan.FromMilliseconds(delayMs * model.Prefs.AnimSpeed), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        if (done is not null) fade.Completed += (_, _) => done();
        element.BeginAnimation(OpacityProperty, fade);
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
    private string DockSignature() => $"{string.Join(",", model.FavoriteIds)}#{L10n.Effective}#{model.HasTrayFiles}#{model.ClipboardOn}#{model.CameraDockOn}#{model.ChatDockOn}#{model.AgentsDockOn}#{string.Join(",", model.DockIds)}";

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
        void Tab(StackPanel row, string glyph, string nameProperty, PanelView? target, Action click, string? literalName = null, ImageSource? iconSource = null)
        {
            var text = new TextBlock { Text = glyph, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            text.SetResourceReference(TextBlock.FontFamilyProperty, "Icons");
            text.Visibility = iconSource is null ? Visibility.Visible : Visibility.Collapsed;
            var icon = new Image { Source = iconSource, Width = 18, Height = 18, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Visibility = iconSource is null ? Visibility.Collapsed : Visibility.Visible, IsHitTestVisible = false };
            // Como en Droppy: los iconos van sueltos dentro de la cápsula, con aire entre ellos; el seleccionado lleva un círculo claro detrás
            // (y el que tiene el ratón encima, uno más tenue). Al pulsar, el círculo se hunde un poco.
            var scale = new ScaleTransform(1, 1);
            var back = new Border
            {
                Width = DockButton, Height = DockButton, CornerRadius = new CornerRadius(DockButton / 2), Margin = new Thickness(4, 0, 4, 0), Child = new Grid { Children = { text, icon } },
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
        if (model.AgentsDockOn) Tab(navigation, "\uE716", nameof(PanelViewModel.NotchTabAgents), PanelView.Agents, () => Go(PanelView.Agents));
        if (model.ChatDockOn) Tab(navigation, "\uE8BD", nameof(PanelViewModel.NotchTabChat), PanelView.Chat, () => Go(PanelView.Chat));
        if (model.CameraDockOn) Tab(navigation, "\uE722", nameof(PanelViewModel.NotchTabCamera), PanelView.Camera, () => Go(PanelView.Camera));
        Tab(navigation, "", nameof(PanelViewModel.NotchTabWidgets), PanelView.Widgets, () => Go(PanelView.Widgets));
        Tab(navigation, "", nameof(PanelViewModel.NotchTabSettings), PanelView.Settings, () => Go(PanelView.Settings));
        foreach (var id in model.FavoriteIds)
            if (FavoriteCatalog.Find(id) is { } favorite)
                Tab(favorites, favorite.Glyph, PanelViewModel.FavoriteNameProperty(id), null, () => model.RunFavorite(id),
                    FavoriteCatalog.IsApp(id) ? L10n.tr("Abrir %@", favorite.Title) : null,
                    FavoriteCatalog.IsApp(id) ? AppIcons.For(FavoriteCatalog.AppPath(id), large: true) : null);
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
        if (model.OpensInWindow(target)) return;   // Ajustes y Widgets abren su ventana: el notch se queda como está
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
        bool contrast = Theme.Flat;
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

    /// El hombro y el radio de las esquinas de abajo para un alto dado: los de la pastilla (7 y 14) hasta 36 px y, de ahí a 70, pasan a los del panel.
    private static (double Ear, double Radius) ShapeMetrics(double h)
    {
        double t = Math.Clamp((h - 36) / 34, 0, 1);   // de pastilla a panel en pocos píxeles: con la forma pequeña a medio abrir ya lleva los hombros y las esquinas grandes, como la de Nusa
        double eClosed = Math.Min(PillEarDip, h / 4), rClosed = Math.Min(14, h - eClosed);
        double eOpen = Math.Min(shapeEar, h / 3), rOpen = Math.Min(shapeRadius + (shapeRadiusTall - shapeRadius) * Math.Clamp((h - 140) / 31, 0, 1), h - eOpen);
        double e = eClosed + (eOpen - eClosed) * t;
        return (e, Math.Min(rClosed + (rOpen - rClosed) * t, h - e));
    }

    /// El contorno del notch sin la línea de arriba: solo las orejas y el cuerpo, de la oreja derecha a la izquierda. Por ahí corre el haz, que no pasa por encima (junto a la pantalla).
    private static Geometry NotchBeamPath(double w, double h)
    {
        var (e, r) = ShapeMetrics(h);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(w, 0), true, false);   // «rellena» aunque no se pinte: el aplanado que mide el largo ignora las figuras sin relleno
            c.ArcTo(new Point(w - e, e), new Size(e, e), 0, false, SweepDirection.Counterclockwise, true, false);
            c.LineTo(new Point(w - e, h - r), true, false);
            c.ArcTo(new Point(w - e - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(e + r, h), true, false);
            c.ArcTo(new Point(e, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(e, e), true, false);
            c.ArcTo(new Point(0, 0), new Size(e, e), 0, false, SweepDirection.Counterclockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    private static Geometry NotchShape(double w, double h)
    {
        // La pastilla cerrada (≈38 px) es casi un rectángulo: hombros de 7 y esquinas de 14, como la de Nusa. Al abrirse pasan poco a poco a las orejas
        // y esquinas grandes del panel (entre 44 y 104 px de alto).
        var (e, r) = ShapeMetrics(h);
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

    private Win32.RECT? placed;   // donde está la ventana ahora
    private double openness = 1;   // 0 = pastilla, 1 = abierto del todo: cuánto vidrio lleva el cuerpo
    private Win32.RECT? lastExpanded, lingerRect;   // lo último que se enseñó abierto, y mientras se cierra: la forma que aún se ve

    private void Place(Win32.RECT r)
    {
        placed = r;
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, r.Left, r.Top, r.Width, r.Height, Win32.SWP_NOACTIVATE);
    }

    /// Abre o cierra el negro; con «movimiento reducido» salta directo.
    private void Animate(double width, double height, Action? done, bool opening = false, bool resize = false)
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
        // Abrir: un resorte con rebote (el ancho y el alto con su propio ritmo); cerrar: suave y sin rebote; la pastilla que cambia de largo, la curva larga y suave de siempre.
        IEasingFunction widthEase, heightEase;
        TimeSpan widthSpan, heightSpan;
        if (resize && dockResize) (widthEase, heightEase, widthSpan, heightSpan) = (Motion.DockEase(), Motion.DockEase(), Motion.DockDuration, Motion.DockDuration);
        else if (resize) (widthEase, heightEase, widthSpan, heightSpan) = (Motion.WidthEase(), Motion.WidthEase(), Motion.WidthDuration, Motion.WidthDuration);
        else if (opening) (widthEase, heightEase, widthSpan, heightSpan) = (Motion.OpenWidthEase(), Motion.OpenHeightEase(), Motion.OpenWidthDuration, Motion.OpenHeightDuration);
        else if (done is not null) (widthEase, heightEase, widthSpan, heightSpan) = (Motion.CloseEase(), Motion.CloseEase(), Motion.CloseDuration, Motion.CloseDuration);
        else (widthEase, heightEase, widthSpan, heightSpan) = (Motion.Ease(), Motion.Ease(), Motion.PanelDuration, Motion.PanelDuration);
        double speed = model.Prefs.AnimSpeed;
        Motion.Busy((widthSpan > heightSpan ? widthSpan : heightSpan) * speed + TimeSpan.FromMilliseconds(80));
        var growH = new DoubleAnimation(height, heightSpan * speed) { EasingFunction = heightEase };
        growH.Completed += (_, _) =>
        {
            if (mine != generation) return;
            shell.BeginAnimation(WidthProperty, null);
            shell.BeginAnimation(HeightProperty, null);
            shell.Width = width;
            shell.Height = height;
            done?.Invoke();
        };
        shell.BeginAnimation(WidthProperty, new DoubleAnimation(width, widthSpan * speed) { EasingFunction = widthEase });
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
        bool contrast = Theme.Flat;
        var rim = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        rim.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
        rim.GradientStops.Add(new GradientStop(Color.FromArgb(0x42, 255, 255, 255), 0.4));
        rim.GradientStops.Add(new GradientStop(Color.FromArgb(0x66, 255, 255, 255), 1));   // el canto de abajo, el que más luz recoge
        rim.Freeze();
        bool outline = contrast && !SystemParameters.HighContrast;   // «claro con contornos»: una línea negra fina hace de canto
        shellStroke = outline ? Theme.Brush(Theme.Current.Line) : contrast ? null : rim;
        body.StrokeThickness = outline ? 1.5 : 1;
        PaintBody();
        PaintDock();
        PaintTabs();
    }

    private static readonly Brush DockRim = Across((0, White(0xC0)), (0.1, White(0x66)), (0.5, White(0x34)), (0.9, White(0x66)), (1, White(0xC0)));

    /// Las cápsulas del dock: el fondo desenfocado de debajo (con su color) bajo un tinte de negro puro que se oscurece con lo claro que sea, y el filo
    /// con luz en las esquinas, igual que el notch. Sin copia del fondo (alto contraste o si falló), el tinte solo es casi opaco.
    private void PaintDock()
    {
        bool contrast = Theme.Flat;
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
            if (Theme.Flat || back.Source is not { } source || back.Opacity == 0 || !(back.Width > 0) || !(back.Height > 0) || capsule.ActualWidth <= 0) { image.Background = null; continue; }
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
        bool contrast = Theme.Flat;
        var baseColor = contrast ? Theme.Current.Bg : Theme.Hex(0x000000);
        baseColor.A = 255;
        body.Stroke = glassBody || contrast ? shellStroke : null;   // la pastilla cerrada es negro puro, sin canto
        if (contrast || !glassBody) { body.Fill = Theme.Brush(baseColor); return; }
        byte tint = (byte)(0x24 + 0x70 * Math.Clamp(backdrop.Luminance, 0, 1));   // sin tarjetas detrás, el tinte es lo que da el contraste
        bool shelfPeek = state.Mode == NotchMode.Expanded && model.PeekWidgets.Any(widget => WidgetLayout.BaseId(widget.Id) == "shelf");
        int shelfLift = shelfPeek ? 0x40 : 0, shelfFloor = shelfPeek ? 0x28 : 0;
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        double glassLevel = model.Prefs.GlassLevel;
        // El vidrio se gana con la apertura: con la forma pequeña a medio abrir el cuerpo es negro macizo (como el de Nusa) y se va haciendo vidrio al llegar al tamaño
        // final. Antes la mitad de abajo ya era translúcida desde el primer fotograma, con el fondo de detrás sin difuminar todavía: salía un borde transparente y raro.
        byte Glass(int alpha) => (byte)(255 - (255 - Math.Max(shelfFloor, alpha - shelfLift)) * openness);
        if (shelfPeek)
        {
            // Como en Nusa: la cabecera es negro macizo y se funde hacia abajo con el vidrio en una curva suave (smoothstep), sin una línea que separe cabecera y fondo.
            // El negro macizo llega hasta pasada la línea del título (~36 px de 140): el fondo desenfocado de detrás tiene escalones por celdas que, si asomaran antes, se verían como una raya.
            double start = 0.27, end = Math.Min(.97, Math.Max(glassLevel + .28, start + .3));
            double target = Glass(Math.Min(255, tint + 0x30));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(255, baseColor.R, baseColor.G, baseColor.B), 0));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(255, baseColor.R, baseColor.G, baseColor.B), start));
            for (int k = 1; k <= 4; k++)
            {
                double u = k / 5.0, smooth = u * u * (3 - 2 * u);
                g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 - (255 - target) * smooth), baseColor.R, baseColor.G, baseColor.B), start + (end - start) * u));
            }
            g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)target, baseColor.R, baseColor.G, baseColor.B), end));
        }
        else
        {
            // Mantener el primer fotograma del degradado translúcido evita una franja negra opaca en el borde superior.
            g.GradientStops.Add(new GradientStop(Color.FromArgb(Glass(Math.Min(0xE0, tint + 0xA0)), baseColor.R, baseColor.G, baseColor.B), 0));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(Glass(Math.Min(255, tint + 0x60)), baseColor.R, baseColor.G, baseColor.B), glassLevel));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(Glass(Math.Min(255, tint + 0x30)), baseColor.R, baseColor.G, baseColor.B), Math.Min(.97, glassLevel + .28)));
        }
        g.GradientStops.Add(new GradientStop(Color.FromArgb(Glass(tint), baseColor.R, baseColor.G, baseColor.B), 1));
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
        // Con la vista de letras (fondo opaco teñido con la portada, como en Nusa) no hay filo de cristal: el acabado es limpio.
        bool lyrics = model.LyricsShown;
        body.Stroke = (glassBody || Theme.Flat) && !lyrics ? shellStroke : null;
        bool on = glassBody && !Theme.Flat && heightDip > 80 && !lyrics;
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
        // El panel mide lo que mide su contenido; antes de la primera medida (o con el ancho de la vista corta puesto) valdría 0 y la copia saldría estrecha:
        // se copia siempre, al menos, el ancho natural del panel.
        double panelWidth = Math.Max(view.DesiredSize.Width, NaturalPanelWidth);
        var area = NotchLayout.Place(monitor, work, scale, panelWidth, 0, pillWidthDip).Expanded;
        int ear = (int)Math.Round(EarDip * scale);
        area.Left -= ear;
        area.Right += ear;
        area.Bottom = (int)Math.Min(monitor.Bottom, area.Top + Math.Round((model.BodyMaxHeight + 260) * scale));
        backdrop.CaptureAsync(area, scale, () =>
        {
            if (Math.Abs(backdrop.Luminance - paintedLuma) > 0.06) PaintBody();
            PaintDock();
            if (body.Data is { } shape) { bevelVersion = -1; PaintBevel(shape, shell.ActualHeight); }   // el hilo de color del canto sale de la copia nueva
        });
    }

    // MARK: la pastilla

    private readonly StackPanel pillRow = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Dictionary<string, TextBlock> pillTexts = [];

    /// Los indicadores que se dibujan como icono propio (los demás, como el equipo o el evento en curso, llevan su anillo de progreso).
    private static readonly HashSet<string> IconIds = ["media", "clean", "agents", "notice", "volume", "brightness", "pomodoro", "power", "pressure", "locks", "bluetooth", "awake"];

    /// Iconos dibujados a mano (18×18), con su animación: el movimiento cuenta lo que pasa sin texto.
    /// «clean»: un chip cuyos bloques se vacían en cadena. «agents»: una chispa que late dentro de una órbita que gira. «notice»: una campana que
    /// vibra con un punto amarillo. «volume»: ondas que laten. «brightness»: un sol cuyos rayos giran. «pomodoro»: un disco rojo con arena que se mueve.
    /// «power»: el símbolo de encendido que respira. «pressure»: un triángulo de aviso que late rápido. «locks»: la flecha de Bloq Mayús que se
    /// eleva. «bluetooth»: la runa con ondas. «awake»: una taza con vapor. Con «movimiento reducido» quedan quietos.
    private FrameworkElement BuildActivityIcon(string id, out Action? update)
    {
        update = null;
        bool still = model.Prefs.ReducedMotion;
        var canvas = new Canvas { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        System.Windows.Shapes.Path Line(string data, double thickness = 1.9)
        {
            var path = new System.Windows.Shapes.Path { Data = Geometry.Parse(data), StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
            path.SetResourceReference(Shape.StrokeProperty, "Fg");
            return path;
        }
        System.Windows.Shapes.Path Solid(string data)
        {
            var path = new System.Windows.Shapes.Path { Data = Geometry.Parse(data) };
            path.SetResourceReference(Shape.FillProperty, "Fg");
            return path;
        }
        DoubleAnimation Loop(double from, double to, double ms, double delayMs = 0) =>
            new(from, to, TimeSpan.FromMilliseconds(ms)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(delayMs) };
        void Pulse(UIElement element, double from, double to, double ms, double delayMs = 0)
        {
            if (!still) element.BeginAnimation(OpacityProperty, Loop(from, to, ms, delayMs));
        }
        ScaleTransform Breathe(FrameworkElement element, double from, double to, double ms)
        {
            var scale = new ScaleTransform(1, 1);
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = scale;
            if (!still) foreach (var axis in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty }) scale.BeginAnimation(axis, Loop(from, to, ms));
            return scale;
        }
        RotateTransform Spin(FrameworkElement element, double seconds)
        {
            var turn = new RotateTransform(0, 9, 9);
            element.RenderTransform = turn;
            if (!still) turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(seconds)) { RepeatBehavior = RepeatBehavior.Forever });
            return turn;
        }
        switch (id)
        {
            case "clean":
            {
                canvas.Children.Add(Line("M5,4.5 H13 A2,2 0 0 1 15,6.5 V11.5 A2,2 0 0 1 13,13.5 H5 A2,2 0 0 1 3,11.5 V6.5 A2,2 0 0 1 5,4.5 Z", 1.7));
                var pins = Line("M6,2 V4.5 M9,2 V4.5 M12,2 V4.5 M6,13.5 V16 M9,13.5 V16 M12,13.5 V16", 1.5);
                canvas.Children.Add(pins);
                Pulse(pins, 0.35, 1, 520);
                for (int i = 0; i < 3; i++)
                {
                    var block = new Rectangle { Width = 2.1, Height = 4.2, RadiusX = .7, RadiusY = .7, RenderTransformOrigin = new Point(0.5, 1) };
                    block.SetResourceReference(Shape.FillProperty, "Fg");
                    var scale = new ScaleTransform(1, 1);
                    block.RenderTransform = scale;
                    Canvas.SetLeft(block, 4.95 + 3 * i);
                    Canvas.SetTop(block, 7);
                    canvas.Children.Add(block);
                    if (!still) scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.15, TimeSpan.FromMilliseconds(460)) { BeginTime = TimeSpan.FromMilliseconds(130 * i), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
                }
                break;
            }
            case "media":
            {
                // Ecualizador de cuatro barras gruesas que suben y bajan a ritmos distintos.
                var timings = new (double From, double To, double Ms)[] { (0.3, 1, 450), (1, 0.3, 600), (0.45, 0.95, 350), (0.85, 0.3, 520) };
                for (int i = 0; i < timings.Length; i++)
                {
                    var bar = new Rectangle { Width = 2.8, Height = 14, RadiusX = 1.4, RadiusY = 1.4, RenderTransformOrigin = new Point(0.5, 1) };
                    bar.SetResourceReference(Shape.FillProperty, "Fg");
                    var scale = new ScaleTransform(1, still ? 0.7 : timings[i].From);
                    bar.RenderTransform = scale;
                    Canvas.SetLeft(bar, 1.6 + 4.1 * i);
                    Canvas.SetTop(bar, 2);
                    canvas.Children.Add(bar);
                    if (!still) scale.BeginAnimation(ScaleTransform.ScaleYProperty, Loop(timings[i].From, timings[i].To, timings[i].Ms));
                }
                break;
            }
            case "agents":
            {
                var orbit = new Canvas { Width = 18, Height = 18 };
                Spin(orbit, 1.4);
                orbit.Children.Add(Line("M9,2 A7,7 0 0 1 16,9", 1.9));
                var tail = Line("M9,16 A7,7 0 0 1 2,9", 1.9);
                tail.Opacity = 0.4;
                orbit.Children.Add(tail);
                canvas.Children.Add(orbit);
                var star = Solid("M9,4.6 C9.5,7.4 10.6,8.5 13.4,9 C10.6,9.5 9.5,10.6 9,13.4 C8.5,10.6 7.4,9.5 4.6,9 C7.4,8.5 8.5,7.4 9,4.6 Z");
                canvas.Children.Add(star);
                Breathe(star, 0.65, 1.12, 700);
                break;
            }
            case "notice":
            {
                // La campana vibra unos 0,8 s y descansa 0,9 s: balanceo que se amortigua, con el punto amarillo latiendo encima.
                var bell = new Canvas { Width = 18, Height = 18 };
                bell.Children.Add(Line("M9,2.8 C6.1,2.8 4.8,5 4.8,7.4 V10.6 L3.2,13.2 H14.8 L13.2,10.6 V7.4 C13.2,5 11.9,2.8 9,2.8 Z", 1.9));
                bell.Children.Add(Line("M7.3,15.3 A1.8,1.8 0 0 0 10.7,15.3", 1.9));
                var swing = new RotateTransform(0, 9, 2.4);
                bell.RenderTransform = swing;
                canvas.Children.Add(bell);
                var dot = new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xC9, 0x3C)) };
                Canvas.SetLeft(dot, 11.2);
                Canvas.SetTop(dot, 0.2);
                canvas.Children.Add(dot);
                Breathe(dot, 0.8, 1.2, 420);
                if (!still)
                {
                    var rock = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1700), RepeatBehavior = RepeatBehavior.Forever };
                    foreach (var (at, angle) in new[] { (0, 0.0), (90, -17.0), (210, 17.0), (330, -13.0), (450, 13.0), (570, -8.0), (690, 8.0), (800, 0.0), (1700, 0.0) })
                        rock.KeyFrames.Add(new EasingDoubleKeyFrame(angle, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(at))) { EasingFunction = ease });
                    swing.BeginAnimation(RotateTransform.AngleProperty, rock);
                }
                break;
            }
            case "volume":
            {
                canvas.Children.Add(Solid("M2.2,7 H5.4 L9.6,3.4 V14.6 L5.4,11 H2.2 Z"));
                var near = Line("M11.9,6.7 A3.4,3.4 0 0 1 11.9,11.3", 1.8);
                var far = Line("M14.2,4.4 A6.6,6.6 0 0 1 14.2,13.6", 1.8);
                canvas.Children.Add(near);
                canvas.Children.Add(far);
                Pulse(near, 0.4, 1, 480);
                Pulse(far, 0.2, 1, 480, 240);
                break;
            }
            case "brightness":
            {
                var core = new Ellipse { Width = 6.2, Height = 6.2, StrokeThickness = 1.9 };
                core.SetResourceReference(Shape.StrokeProperty, "Fg");
                Canvas.SetLeft(core, 5.9);
                Canvas.SetTop(core, 5.9);
                canvas.Children.Add(core);
                Breathe(core, 0.82, 1.15, 900);
                var rays = new Canvas { Width = 18, Height = 18 };
                rays.Children.Add(Line("M9,1.2 V3.2 M9,14.8 V16.8 M1.2,9 H3.2 M14.8,9 H16.8 M3.5,3.5 L4.9,4.9 M13.1,13.1 L14.5,14.5 M3.5,14.5 L4.9,13.1 M13.1,4.9 L14.5,3.5", 1.8));
                Spin(rays, 6);
                canvas.Children.Add(rays);
                break;
            }
            case "pomodoro":
            {
                var disk = new Ellipse { Width = 18, Height = 18, Fill = new SolidColorBrush(Color.FromRgb(0x2A, 0x0A, 0x14)) };
                canvas.Children.Add(disk);
                var sand = new Canvas { Width = 18, Height = 18, Clip = new EllipseGeometry(new Point(9, 9), 8.8, 8.8) };
                var back = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M0,0 Q4.5,-3.8 9,0 T18,0 T27,0 T36,0 V22 H0 Z"),
                    Fill = new SolidColorBrush(Color.FromRgb(0xB8, 0x40, 0x3F)),
                    RenderTransform = new TranslateTransform(),
                };
                var front = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M0,0 Q4.5,-3.2 9,0 T18,0 T27,0 T36,0 V22 H0 Z"),
                    Fill = new SolidColorBrush(Color.FromRgb(0xF2, 0x5A, 0x56)),
                    RenderTransformOrigin = new Point(0, 0),
                };
                var slosh = new ScaleTransform(1, 1);
                var waveShift = new TranslateTransform();
                front.RenderTransform = new TransformGroup { Children = { slosh, waveShift } };
                sand.Children.Add(back);
                sand.Children.Add(front);
                canvas.Children.Add(sand);
                var rim = new Ellipse { Width = 18, Height = 18, Stroke = new SolidColorBrush(Color.FromRgb(0xE8, 0x73, 0x6F)), StrokeThickness = 1 };
                canvas.Children.Add(rim);
                void UpdateSandLevel()
                {
                    double y = 18 * (1 - (0.45 + 0.4 * Math.Clamp(model.Hub.Pomodoro.Progress, 0, 1)));
                    Canvas.SetTop(back, y);
                    Canvas.SetTop(front, y + 1.4);
                }
                UpdateSandLevel();
                update = UpdateSandLevel;
                if (!still)
                {
                    ((TranslateTransform)back.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
                        new DoubleAnimation(0, -18, TimeSpan.FromMilliseconds(2600)) { RepeatBehavior = RepeatBehavior.Forever });
                    waveShift.BeginAnimation(TranslateTransform.XProperty,
                        new DoubleAnimation(-18, 0, TimeSpan.FromMilliseconds(1900)) { RepeatBehavior = RepeatBehavior.Forever });
                    slosh.BeginAnimation(ScaleTransform.ScaleYProperty,
                        new DoubleAnimation(0.5, 1.6, TimeSpan.FromMilliseconds(1300)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
                }
                break;
            }
            case "power":
            {
                var symbol = new Canvas { Width = 18, Height = 18 };
                symbol.Children.Add(Line("M9,2 V8.4 M5,4.6 A6.2,6.2 0 1 0 13,4.6", 2));
                canvas.Children.Add(symbol);
                Breathe(symbol, 0.88, 1.08, 650);
                Pulse(symbol, 0.5, 1, 650);
                break;
            }
            case "pressure":
            {
                var warning = new Canvas { Width = 18, Height = 18 };
                warning.Children.Add(Line("M9,2.6 L16.2,14.8 H1.8 Z", 1.8));
                warning.Children.Add(Line("M9,7.3 V10.6", 1.9));
                warning.Children.Add(Solid("M9,11.7 m-1,0 a1,1 0 1 0 2,0 a1,1 0 1 0 -2,0 Z"));
                canvas.Children.Add(warning);
                Breathe(warning, 0.9, 1.14, 330);
                break;
            }
            case "locks":
            {
                var arrow = new Canvas { Width = 18, Height = 18 };
                arrow.Children.Add(Line("M9,2.4 L15,8.8 H11.2 V12.4 H6.8 V8.8 H3 Z", 1.8));
                arrow.Children.Add(Line("M6.8,15.4 H11.2", 1.9));
                var rise = new TranslateTransform();
                arrow.RenderTransform = rise;
                canvas.Children.Add(arrow);
                if (!still) rise.BeginAnimation(TranslateTransform.YProperty, Loop(1, -1.4, 420));
                break;
            }
            case "bluetooth":
            {
                canvas.Children.Add(Line("M4.4,5.3 L12.4,12.7 L8.6,16.4 V1.6 L12.4,5.3 L4.4,12.7", 1.9));
                var near = Line("M14,6.4 A4,4 0 0 1 14,11.6", 1.7);
                var far = Line("M15.9,4.4 A6.8,6.8 0 0 1 15.9,13.6", 1.7);
                canvas.Children.Add(near);
                canvas.Children.Add(far);
                Pulse(near, 0.3, 1, 520);
                Pulse(far, 0.15, 1, 520, 260);
                break;
            }
            case "awake":
            {
                canvas.Children.Add(Line("M3.4,7.8 H12 V11.4 A3.4,3.4 0 0 1 8.6,14.8 H6.8 A3.4,3.4 0 0 1 3.4,11.4 Z", 1.8));
                canvas.Children.Add(Line("M12,8.8 H13.4 A1.9,1.9 0 0 1 13.4,12.6 H11.7", 1.8));
                for (int i = 0; i < 2; i++)
                {
                    var steam = Line($"M{5.9 + 3.4 * i},1.6 Q{4.9 + 3.4 * i},3.3 {5.9 + 3.4 * i},5", 1.6);
                    var rise = new TranslateTransform();
                    steam.RenderTransform = rise;
                    canvas.Children.Add(steam);
                    Pulse(steam, 0.15, 1, 700, 350 * i);
                    if (!still) rise.BeginAnimation(TranslateTransform.YProperty, Loop(1.2, -1.2, 700, 350 * i));
                }
                break;
            }
        }
        return Enlarge(canvas);
    }

    /// Los iconos se dibujan en 18×18 y se enseñan tal cual: la pastilla mide como la de Nusa (150×30) y cabe un icono a cada lado de la hora.
    private static FrameworkElement Enlarge(Canvas canvas) =>
        new Viewbox { Width = 18, Height = 18, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Child = canvas };

    /// El primer elemento visible de la fila pegado al borde y los demás con 8 de separación (los elementos se crean con 10 de margen a la izquierda).
    private static void SpaceRow(StackPanel row)
    {
        bool first = true;
        foreach (var child in row.Children.OfType<FrameworkElement>())
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            child.Margin = new Thickness(first ? 0 : 8, 0, 0, 0);
            first = false;
        }
    }

    /// La portada de la canción que suena, pequeña, en el hueco izquierdo (solo si la cámara no lo ocupa). El ecualizador es otro indicador de la derecha.
    /// El título queda de ayuda y de nombre accesible.
    private void ShowCover(bool on)
    {
        if (!on)
        {
            if (coverArt is not null) activityRowLeft.Children.Remove(coverArt);
            return;
        }
        var media = model.Hub.Media;
        coverArt ??= new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(5), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        if (!ReferenceEquals(coverShown, media.Art))
        {
            coverShown = media.Art;
            coverArt.Background = coverShown is null ? null : new ImageBrush(coverShown) { Stretch = Stretch.UniformToFill };
        }
        coverArt.Visibility = coverShown is null ? Visibility.Collapsed : Visibility.Visible;
        var title = PanelViewModel.MediaPillText(media.MediaTitle, media.Artist);
        coverArt.ToolTip = title;
        AutomationProperties.SetName(coverArt, title);
        if (!activityRowLeft.Children.Contains(coverArt)) activityRowLeft.Children.Insert(0, coverArt);
    }

    /// El anillo de batería (la del equipo) en el hueco izquierdo cuando la cámara y la portada no lo ocupan; el rayo dentro mientras carga.
    private void ShowBattery(bool on)
    {
        if (!on)
        {
            if (batteryItem is not null) activityRowLeft.Children.Remove(batteryItem);
            return;
        }
        var info = model.Hub.BatteryInfo;
        if (batteryItem is null)
        {
            batteryRing = new BatteryRing { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
            batteryItem = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { batteryRing } };
        }
        batteryRing!.Fraction = info.Fraction;
        batteryRing.Charging = info.Charging;
        batteryRing.InvalidateVisual();   // por si cambió el tema
        var help = $"{tr("Batería")} {info.Label}" + (info.Charging ? " · " + tr("Cargando") : "");
        batteryItem.ToolTip = help;
        AutomationProperties.SetName(batteryItem, help);
        if (!activityRowLeft.Children.Contains(batteryItem)) activityRowLeft.Children.Insert(0, batteryItem);
    }

    /// Cada indicador entra con un pequeño rebote y un fundido; sin «movimiento reducido».
    private void AnimateActivityIn()
    {
        activityRow.BeginAnimation(OpacityProperty, null);
        if (model.Prefs.ReducedMotion) { activityRow.Opacity = 1; activityRow.RenderTransform = Transform.Identity; return; }
        var grow = new ScaleTransform(0.5, 0.5);
        activityRow.RenderTransformOrigin = new Point(0.5, 0.5);
        activityRow.RenderTransform = grow;
        var bounce = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut };
        foreach (var axis in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
            grow.BeginAnimation(axis, new DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = bounce });
        activityRow.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
    }

    /// Un pulso al cambiar el brillo: las actividades de la derecha reaparecen con un fundido (sin «movimiento reducido»).
    private void PulseActivities()
    {
        activityRow.BeginAnimation(OpacityProperty, null);
        if (!model.Prefs.ReducedMotion) activityRow.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190)));
    }

    private Ellipse? pillDot;
    private string pillSignature = "", activitySignature = "", rightSignature = "";
    private long brightnessRevision;
    /// Las actividades en vivo, dentro de la pastilla: lo de Ollama (limpiando, encendiendo…) a la izquierda de los datos y lo demás (temporizador, música…) a la derecha; la pastilla crece para dar sitio.
    private readonly StackPanel activityRow = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel activityRowLeft = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    /// Tres columnas: las actividades de Ollama pegadas a la izquierda, los datos (la hora) centrados y las demás actividades pegadas a la derecha.
    /// Las dos de los lados miden lo mismo para que lo del centro quede en el centro del notch.
    /// Cinco columnas: hueco izquierdo fijo (cámara, portada) | datos antes de la hora | la hora | datos después | hueco derecho fijo (actividad, ecualizador).
    /// Las dos de datos son de estrella y los dos huecos miden lo mismo: la hora queda en el centro exacto del notch.
    private readonly Grid pillOuter = new()
    {
        VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(PillPadding, 0, PillPadding, 0),
        ColumnDefinitions =
        {
            new ColumnDefinition { Width = new GridLength(SideSlot) }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto },
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = new GridLength(SideSlot) },
        },
    };
    private readonly StackPanel pillBefore = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly StackPanel pillAfter = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
    private Border? coverArt;           // la portada, en el hueco izquierdo mientras suena música y no hay cámara
    private ImageSource? coverShown;
    private BatteryRing? batteryRing;   // el anillo de batería del hueco izquierdo libre
    private StackPanel? batteryItem;
    private readonly Dictionary<string, Action<string>> activityTexts = [];
    private readonly Dictionary<string, ActivityRing> activityRings = [];
    private readonly Dictionary<string, Action> activityIconUpdates = [];
    private Ellipse? cameraDot, cameraHalo;
    private Brush? shellStroke;   // el canto del cuerpo abierto (la pastilla cerrada no lo lleva)
    private const double SideSlot = 32;    // el hueco fijo (DIP) a cada lado de la hora: a la izquierda la cámara (o la portada), a la derecha un indicador
    private const double PillPadding = 10;
    private double pillWidthDip = NotchLayout.PillWidth;
    private bool stripNow;   // «Notch mínimo» y nada que enseñar: la pastilla cerrada es la franja fina
    // Mide lo que el cuerpo del notch: ancho exacto (los hombros de 7 por lado, para que su contorno coincida con el de `NotchShape` y las orejas queden cóncavas)
    // y un poco más alto que la pastilla, para no dejar un borde negro abajo.
    private readonly NotchMascot mascot = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(-PillEarDip, 0, -PillEarDip, -9) };   // la mascota, encima de la pastilla cerrada mientras se asoma
    // El aviso del sistema (volumen, brillo, Bloq. Mayús/Núm, idioma): la pastilla se ensancha y se hace un poco más alta mientras se enseña.
    private readonly NotchHudView hudView = new() { Visibility = Visibility.Collapsed };
    private readonly DispatcherTimer hudTimer = new() { Interval = TimeSpan.FromSeconds(HudPicker.ShowSeconds + 0.1) };
    private double pillHeightDip = NotchLayout.PillHeight;
    // Con letras sincronizadas, el notch cerrado enseña la línea que se canta: este reloj repinta la pastilla solo cuando cambia de línea.
    private readonly DispatcherTimer lyricClock = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private int lastLyricIndex = -2;
    private const double PeekLyricsWidth = 520;

    /// El ancho de la pastilla con la letra: lo que mide la línea (negrita de 13) más la nota y los respiros, para que nunca se corte con «…» mientras quepa en la pantalla.
    private double LyricPillWidth(string line)
    {
        var text = new FormattedText(line, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 13, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        double wide = Math.Clamp(Math.Ceiling(text.Width * 1.05) + 2 * 32 + 2 * 18 + 8, 300, Math.Max(300, 0.8 * (work.Width / Math.Max(1, scale))));
        return wide;
    }
    private bool hudShown;
    private string hudKey = "";
    private readonly NotchMascotCycle mascotCycle = new();
    private readonly DispatcherTimer mascotClock = new() { Interval = TimeSpan.FromMilliseconds(250) };   // rápido: el saludo empieza en cuanto vuelve el usuario
    private bool mascotShown;

    /// Los datos de la pastilla cerrada son los que elija el usuario (página Widgets): se reconstruyen si cambia la lista y se repintan con cada dato nuevo.
    private void RefreshPill()
    {
        var ids = model.PillIds;
        var signature = string.Join(",", ids);
        if (signature != pillSignature)
        {
            pillSignature = signature;
            pillRow.Children.Clear();
            pillBefore.Children.Clear();
            pillAfter.Children.Clear();
            pillTexts.Clear();
            pillDot = null;
            // La música no es un dato de texto: es el par portada + ecualizador de los extremos (ver abajo). Si hay hora, ella es el centro y lo que
            // el usuario eligió antes de ella va a su izquierda y lo de después, a su derecha; sin hora, todo va junto en el centro.
            var shown = ids.Where(i => i != "media").ToList();
            int clockAt = shown.IndexOf("clock");
            foreach (var id in shown)
            {
                var target = clockAt < 0 || shown.IndexOf(id) == clockAt ? pillRow : shown.IndexOf(id) < clockAt ? pillBefore : pillAfter;
                var text = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                switch (id)
                {
                    case "state": text.FontWeight = FontWeights.SemiBold; text.SetResourceReference(TextBlock.ForegroundProperty, "Fg"); break;
                    case "model": text.MaxWidth = 92; text.SetResourceReference(TextBlock.ForegroundProperty, "Secondary"); break;
                    // MinWidth: «1:05 AM» y «12:54 AM» no cambian el ancho de la pastilla al pasar la hora.
                    case "clock": text.FontSize = 16; text.FontWeight = FontWeights.Bold; text.MinWidth = 66; text.TextAlignment = TextAlignment.Center; text.SetResourceReference(TextBlock.ForegroundProperty, "Fg"); break;
                    default:
                        text.FontSize = 10.5;
                        text.FontFamily = (FontFamily)Application.Current.FindResource("Mono");
                        text.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                        break;
                }
                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(target.Children.Count == 0 ? 0 : 10, 0, 0, 0) };
                if (id == "state")
                {
                    pillDot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
                    item.Children.Add(pillDot);
                }
                item.Children.Add(text);
                target.Children.Add(item);
                pillTexts[id] = text;
            }
            // Un respiro entre los datos de los lados y la hora.
            pillBefore.Margin = new Thickness(0, 0, pillBefore.Children.Count > 0 ? 12 : 0, 0);
            pillAfter.Margin = new Thickness(pillAfter.Children.Count > 0 ? 12 : 0, 0, 0, 0);
        }
        foreach (var (id, text) in pillTexts)
        {
            var value = model.PillValue(id);
            if (id != "clock" || value.Length < 4 || value[^3] != ' ') { text.Text = value; continue; }
            // La hora en grande y «AM»/«PM» más pequeño y apagado, como en Nusa.
            var suffix = new Run(" " + value[^2..]) { FontSize = 10, FontWeight = FontWeights.SemiBold };
            suffix.SetResourceReference(TextElement.ForegroundProperty, "Secondary");
            text.Inlines.Clear();
            text.Inlines.Add(new Run(value[..^3]));
            text.Inlines.Add(suffix);
        }
        if (pillDot is not null) pillDot.Fill = model.PillForeground;

        var activities = model.LiveActivities;
        bool brightnessChanged = model.Hub.Huds.BrightnessRevision != brightnessRevision;
        brightnessRevision = model.Hub.Huds.BrightnessRevision;
        var activityIds = $"{string.Join(",", activities.Select(a => a.Id))}#camera:{model.Camera.IsInUse}";
        if (activityIds != activitySignature)
        {
            activitySignature = activityIds;
            activityRow.Children.Clear();
            activityRowLeft.Children.Clear();
            activityTexts.Clear();
            activityRings.Clear();
            activityIconUpdates.Clear();
            cameraDot = null;
            foreach (var activity in activities)
            {
                if (activity.Id == "camera")
                {
                    activityRowLeft.Children.Add(BuildCameraIndicator());
                    activityTexts[activity.Id] = _ => PaintCameraIndicator();
                    continue;
                }
                // Los indicadores de la derecha usan un icono animado; Pomodoro conserva su disco rojo. El texto sirve de ayuda y nombre accesible.
                FrameworkElement glyph;
                Action? updateIcon = null;
                if (IconIds.Contains(activity.Id)) glyph = BuildActivityIcon(activity.Id, out updateIcon);
                else if (activity.Progress >= 0)
                {
                    var ring = new ActivityRing { Width = 16, Height = 16, Progress = activity.Progress, VerticalAlignment = VerticalAlignment.Center };
                    activityRings[activity.Id] = ring;
                    glyph = ring;
                }
                else
                {
                    var icon = new TextBlock { Text = activity.Glyph, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
                    icon.SetResourceReference(TextBlock.FontFamilyProperty, "Icons");
                    icon.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
                    glyph = icon;
                }
                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), Children = { glyph } };
                (activity.Left ? activityRowLeft : activityRow).Children.Add(item);
                activityTexts[activity.Id] = value => { item.ToolTip = value; AutomationProperties.SetName(item, value); };
                if (updateIcon is not null) activityIconUpdates[activity.Id] = updateIcon;
            }
        }
        // El indicador de la derecha entra con su animación cada vez que cambia (otro tipo, o el turno de otro).
        var rightIds = string.Join(",", activities.Where(a => !a.Left).Select(a => a.Id));
        if (rightIds != rightSignature)
        {
            rightSignature = rightIds;
            if (rightIds.Length > 0) AnimateActivityIn();
        }
        foreach (var activity in activities)
        {
            activityTexts[activity.Id](activity.Text);
            if (activityRings.TryGetValue(activity.Id, out var ring)) ring.Progress = activity.Progress;
            if (activityIconUpdates.TryGetValue(activity.Id, out var updateIcon)) updateIcon();
        }
        PaintCameraIndicator();
        if (brightnessChanged && activities.Any(a => a.Id == "brightness")) PulseActivities();

        // Con música (si el usuario la eligió como dato de la pastilla o como actividad): la portada en el extremo izquierdo y el ecualizador, teñido
        // con el color de la portada, en el derecho. La pastilla se ensancha igual por los dos lados y la hora sigue en el centro.
        // Con música (si el usuario la quiere en la pastilla) y sin cámara, la portada ocupa el hueco izquierdo; el ecualizador va a la derecha como un indicador más.
        ShowCover(model.ShowMusicInPill && !activityRowLeft.Children.OfType<Button>().Any());
        // Si el hueco izquierdo sigue libre (ni cámara ni portada), el anillo de batería: así la pastilla nunca queda vacía en un portátil.
        bool leftFree = !activityRowLeft.Children.OfType<Button>().Any() && !(coverArt is not null && coverArt.Visibility == Visibility.Visible && activityRowLeft.Children.Contains(coverArt));
        ShowBattery(leftFree && model.Hub.BatteryInfo.HasBattery && !mascotShown);   // con la mascota puesta, sin anillo de batería
        // Sin márgenes negativos (WPF recorta lo que sobresale de su hueco): cada fila empieza pegada al borde y separa sus elementos con un respiro de 8.
        SpaceRow(activityRowLeft);
        SpaceRow(activityRow);
        var unbounded = new Size(double.PositiveInfinity, double.PositiveInfinity);
        pillRow.Measure(unbounded);
        pillBefore.Measure(unbounded);
        pillAfter.Measure(unbounded);
        // El hueco de cada lado es fijo (un icono): las actividades entran y salen dentro de él y el notch no cambia de ancho. A la derecha se
        // enseña una; la cámara (izquierda) siempre cabe.
        double used = 0;
        foreach (UIElement child in activityRow.Children)
        {
            child.Visibility = Visibility.Visible;
            child.Measure(unbounded);
            double w = Math.Ceiling(child.DesiredSize.Width) + (used == 0 ? 0 : 8);
            if (used + w > SideSlot) child.Visibility = Visibility.Collapsed; else used += w;
        }
        SpaceRow(activityRow);
        double side = SideSlot;
        double center = Math.Ceiling(pillRow.DesiredSize.Width);
        double around = Math.Max(Math.Ceiling(pillBefore.DesiredSize.Width), Math.Ceiling(pillAfter.DesiredSize.Width));   // lo de antes y después de la hora mide lo mismo
        var width = Math.Clamp(2 * side + 2 * around + center + 2 * PillPadding, NotchLayout.PillWidth, 620);
        pillOuter.ColumnDefinitions[0].Width = pillOuter.ColumnDefinitions[4].Width = new GridLength(side);
        // El aviso del sistema (volumen, brillo, Bloq. Mayús/Núm, idioma) manda sobre el ancho y el alto de la pastilla mientras se enseña.
        // El aviso (letra, volumen…) se queda decidido también con el notch abierto (debajo, sin verse): al cerrarse la pastilla ya vuelve con la letra puesta. Antes se
        // apagaba al abrir, y al cerrar salía un instante la pastilla normal (la hora) y luego la letra.
        var hud = gate.Shown ? model.CurrentHud : null;
        UpdateHud(hud);
        if (hud is not null)
        {
            // La letra se ajusta a cada línea (como en Nusa): arranca en el ancho base, crece si la línea no cabe y se recoge cuando llega una más corta. Ya no hay saltos
            // al cambiar: la animación del ancho parte de lo que se ve (ver abajo) y no del valor viejo.
            double wanted = hud.Kind == HudKind.Lyric ? Math.Max(hud.Width, LyricPillWidth(hud.Title)) : hud.Width;
            width = Math.Max(NotchLayout.PillWidth, wanted);
        }
        double height = hud is not null ? hud.Height : NotchLayout.PillHeight;
        // Con «Notch mínimo», sin música sonando ni actividades, la pastilla se queda en la franja; cualquiera de las dos la devuelve.
        bool strip = model.Prefs.NotchMinimal && activities.Count == 0 && !model.Hub.Media.IsPlaying && !mascotShown;
        if (strip != stripNow)
        {
            stripNow = strip;
            if (pill is not null && state.Mode == NotchMode.Collapsed) pill.Visibility = strip ? Visibility.Hidden : Visibility.Visible;
            if (laidOut) Apply();
        }
        if (Math.Abs(width - pillWidthDip) > 1 || Math.Abs(height - pillHeightDip) > 0.5)
        {
            pillWidthDip = width;
            pillHeightDip = height;
            if (pill is not null)
            {
                // Se parte del valor que se ve ahora: al quitar la animación anterior, la propiedad vuelve a su valor guardado (el de hace varios cambios), y la
                // pastilla se encogía de golpe antes de volver a crecer.
                double fromHeight = pill.Height;
                pill.BeginAnimation(HeightProperty, null);
                pill.Height = fromHeight;
                if (model.Prefs.ReducedMotion || !laidOut || state.Mode != NotchMode.Collapsed) pill.Height = height;
                else pill.BeginAnimation(HeightProperty, new DoubleAnimation(fromHeight, height, Motion.WidthDuration * model.Prefs.AnimSpeed) { EasingFunction = Motion.WidthEase() });
            }
            // El contenido sigue a la forma: el ancho de la pastilla se anima a la vez que el del notch (la hora, centrada, no se mueve).
            if (pill is not null)
            {
                double fromWidth = pill.Width;
                pill.BeginAnimation(WidthProperty, null);
                pill.Width = fromWidth;
                if (model.Prefs.ReducedMotion || !laidOut || state.Mode != NotchMode.Collapsed) pill.Width = width;
                else pill.BeginAnimation(WidthProperty, new DoubleAnimation(fromWidth, width, Motion.WidthDuration * model.Prefs.AnimSpeed) { EasingFunction = Motion.WidthEase() });
            }
            // Apply compara lo pintado con lo nuevo: con el notch abierto (la píldora ni se ve) no repinta, y cerrado solo cambia de ancho.
            // Antes aquí se invalidaba `painted`, y cada cambio de ancho (reloj, actividades en vivo) repetía la apertura con su fundido: parpadeo.
            if (laidOut) Apply();
        }
    }

    private Button BuildPill()
    {
        activityRowLeft.HorizontalAlignment = HorizontalAlignment.Left;
        activityRow.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(pillBefore, 1);
        Grid.SetColumn(pillRow, 2);
        Grid.SetColumn(pillAfter, 3);
        Grid.SetColumn(activityRow, 4);
        pillOuter.Children.Add(activityRowLeft);
        pillOuter.Children.Add(pillBefore);
        pillOuter.Children.Add(pillRow);
        pillOuter.Children.Add(pillAfter);
        pillOuter.Children.Add(activityRow);
        RefreshPill();
        var button = new Button
        {
            Content = new Grid { Children = { mascot, pillOuter, hudView } },   // la mascota debajo: los indicadores salen encima de su cuerpo; el aviso del sistema, encima de todo
            Width = pillWidthDip, Height = NotchLayout.PillHeight,
            Style = (Style)Application.Current.FindResource("Plain"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
        };
        button.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(PanelViewModel.NotchHelp)) { Source = model });
        button.Click += (_, _) =>
        {
            // «Abrir con clic»: el primer clic abre la vista corta; el panel entero, con otro clic sobre ella.
            if (model.Prefs.OpenOnClick && state.Mode == NotchMode.Collapsed) { state.Expand(); Apply(); }
            else Pin();
        };
        hudTimer.Tick += (_, _) => { hudTimer.Stop(); RefreshPill(); };   // el aviso caduca: se vuelve a decidir
        lyricClock.Tick += (_, _) =>
        {
            UpdateGlow();   // la letra de la vista abierta se puede encender o apagar sin pasar por Apply
            if (model.Prefs.Lyrics) model.Hub.Media.RefreshLyrics();   // si la canción o su duración cambió, o toca reintentar, la busca (sin esperar a un evento del reproductor)
            if (!model.Prefs.Lyrics || !model.Hub.Media.LyricsOpen || !model.Hub.Media.IsPlaying) { if (lastLyricIndex != -2) { lastLyricIndex = -2; RefreshPill(); } return; }
            // Además de la línea, cuenta el estado (buscando, sin letra): al cambiarlo hay que repintar la pastilla.
            int index = model.Hub.Media.LyricStatus == LyricsStatus.Ready ? model.Hub.Media.LyricIndex : -10 - (int)model.Hub.Media.LyricStatus;
            if (index == lastLyricIndex) return;
            lastLyricIndex = index;
            RefreshPill();
        };
        lyricClock.Start();
        mascotClock.Tick += (_, _) => TickMascot();
        mascotClock.Start();
        return button;
    }

    /// Enseña u oculta el aviso del sistema encima de la pastilla (la hora y la mascota se apartan con un fundido) y arma su caducidad.
    /// El degradado es el mismo en la pastilla y abierto; solo se estira: con la forma pequeña el color asoma antes (como el de la pastilla de Nusa) y con la grande llega al
    /// reparto de la vista de letras (negro hasta casi la mitad).
    private void ShapeGlow()
    {
        if (glowBlackEnd is null || glowTintStart is null) return;
        glowBlackEnd.Offset = 0.04 + 0.41 * glowOpen;
        glowTintStart.Offset = 0.42 + 0.30 * glowOpen;
    }

    /// Pone o quita el degradado de la portada: con la pastilla enseñando la letra, o con la vista abierta y la letra a la vista. Cambia de color con la portada.
    private void UpdateGlow()
    {
        // Sin degradado en el panel entero (fijado): ahí se transparentaba por las tarjetas y no quedaba bien.
        bool want = !Theme.Flat && model.Prefs.Lyrics && state.Mode != NotchMode.Pinned && (state.Mode == NotchMode.Collapsed ? hudShown && model.CurrentHud?.Kind == HudKind.Lyric : model.LyricsShown);
        var tint = model.Hub.Media.Tint;
        var color = tint is LinearGradientBrush { GradientStops.Count: > 0 } g ? g.GradientStops[0].Color : tint is SolidColorBrush solid ? solid.Color : Colors.Transparent;
        if (color.A == 0) want = false;
        if (want && glowColor != color)
        {
            glowColor = color;
            var (r, gr, b) = ArtColor.Deep((color.R, color.G, color.B));
            Color Mix(double k) => Color.FromRgb((byte)(r * k), (byte)(gr * k), (byte)(b * k));
            glowBlackEnd = new GradientStop(Colors.Black, 0.45);
            glowTintStart = new GradientStop(Mix(0.35), 0.72);
            glow.Fill = new LinearGradientBrush([new GradientStop(Colors.Black, 0), glowBlackEnd, glowTintStart, new GradientStop(Mix(1), 1)], new Point(0, 0), new Point(0, 1));
            ShapeGlow();
        }
        if (want == glowOn) return;
        glowOn = want;
        glow.BeginAnimation(OpacityProperty, null);
        if (model.Prefs.ReducedMotion) { glow.Opacity = want ? 1 : 0; return; }
        glow.BeginAnimation(OpacityProperty, new DoubleAnimation(want ? 1 : 0, TimeSpan.FromMilliseconds(want ? 160 : 220)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }

    private void UpdateHud(HudInfo? hud)
    {
        bool on = hud is not null;
        var key = on ? $"{hud!.Kind}|{hud.Title}|{hud.Fraction:F2}|{hud.On}|{hud.Chip}|{hud.Muted}" : "";
        if (on)
        {
            hudView.Tint = hud!.Kind == HudKind.Lyric ? model.Hub.Media.Tint : null;
            UpdateGlow();
            hudView.MaxTextWidth = Math.Max(120, 0.8 * (work.Width / Math.Max(1, scale)) - 2 * 32 - 2 * 18 - 8);
            hudView.Show(hud);
        }
        if (on && key != hudKey) { hudKey = key; hudTimer.Stop(); hudTimer.Interval = TimeSpan.FromSeconds(hud!.Seconds + 0.1); hudTimer.Start(); }
        if (on == hudShown) return;
        hudShown = on;
        hudKey = key;
        UpdateGlow();
        if (on)
        {
            hudView.Visibility = Visibility.Visible;
            FadeIn(hudView, 140, 70);
            FadeOut(pillOuter, 90, 0);
            FadeOut(mascot, 90, 0);
        }
        else
        {
            FadeOut(hudView, 100, 0, () => { if (!hudShown) hudView.Visibility = Visibility.Collapsed; });
            FadeIn(pillOuter, 160, 80);
            FadeIn(mascot, 160, 80);
        }
    }

    private void SyncOptionalTimers()
    {
        bool lyrics = model.Prefs.Lyrics;
        if (lyricClock.IsEnabled != lyrics)
        {
            if (lyrics) { lastLyricIndex = -2; lyricClock.Start(); }
            else
            {
                lyricClock.Stop();
                UpdateGlow();
                if (lastLyricIndex != -2) { lastLyricIndex = -2; RefreshPill(); }
            }
        }

        bool mascotEnabled = model.Prefs.NotchMascot;
        if (mascotClock.IsEnabled == mascotEnabled) return;
        if (mascotEnabled) mascotClock.Start();
        else { TickMascot(); mascotClock.Stop(); }
    }

    private int mascotSignature;

    /// La mascota siempre está con el notch cerrado (dormida) y se despierta cuando algo cambia. Los indicadores (actividades, cámara) salen en su
    /// sitio con ella puesta: su cuerpo blanco se funde con el notch negro y los ojos pasan a blanco. Solo la retiran la música y abrir el notch.
    private void TickMascot()
    {
        bool camera = activityRowLeft.Children.OfType<Button>().Any();
        int activities = model.LiveActivities.Count;
        int signature = activities * 2 + (camera ? 1 : 0);
        bool stimulus = signature != mascotSignature;
        mascotSignature = signature;
        bool visible = NotchMascotCycle.IsVisible(model.Prefs.NotchMascot, IsShown && state.Mode == NotchMode.Collapsed, model.Hub.Media.IsPlaying);
        mascotCycle.Update(visible, stimulus, UserIdle.Seconds(), DateTime.Now);
        bool shownChanged = mascotCycle.Visible != mascotShown;
        if (!shownChanged && !mascotShown) return;
        mascot.Reduced = model.Prefs.ReducedMotion;
        if (shownChanged)
        {
            mascotShown = mascotCycle.Visible;
            if (mascotShown)
            {
                mascot.Show();
                mascot.Visibility = Visibility.Visible;
                FadeIn(mascot, 220, 0);
            }
            else
            {
                mascot.Stop();
                // Se funde y entonces vuelve el notch normal (hora, datos e indicadores con su tinta).
                FadeOut(mascot, 260, 0, () =>
                {
                    if (mascotShown) return;
                    mascot.Visibility = Visibility.Collapsed;
                    ApplyMascotPill();
                    FadeIn(pillOuter, 200, 0);
                });
            }
        }
        if (mascotShown)
        {
            mascot.Greeting = mascotCycle.Greeting;
            if (!mascotCycle.Greeting) mascot.Awake = mascotCycle.Awake;
        }
        ApplyMascotPill();
        if (shownChanged) RefreshPill();   // con «Notch mínimo», la franja se ensancha mientras está la mascota
    }

    /// La mascota está debajo: se esconden la hora y los datos del centro, y los indicadores salen encima, en su sitio, con tinta oscura sobre el blanco.
    /// Mientras se funde al irse, la pastilla normal espera a que termine.
    private void ApplyMascotPill()
    {
        bool onMascot = mascot.Visibility == Visibility.Visible;
        pillBefore.Visibility = pillRow.Visibility = pillAfter.Visibility = onMascot ? Visibility.Hidden : Visibility.Visible;
        foreach (var row in new[] { activityRow, activityRowLeft })
        {
            if (onMascot) row.Resources["Fg"] = mascot.Ink; else row.Resources.Remove("Fg");
        }
    }

    private Button BuildCameraIndicator()
    {
        cameraHalo = new Ellipse { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        cameraDot = new Ellipse { Width = 8, Height = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var mark = new Grid { SnapsToDevicePixels = true, UseLayoutRounding = true, Children = { cameraHalo, cameraDot } };
        var button = new Button
        {
            Content = mark,
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            Margin = new Thickness(10, 0, 0, 0),
            Style = (Style)Application.Current.FindResource("Plain"),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        button.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(PanelViewModel.CameraIndicatorLabel)) { Source = model });
        button.SetBinding(ToolTipProperty, new Binding(nameof(PanelViewModel.CameraIndicatorLabel)) { Source = model });
        button.Click += (_, _) => Go(PanelView.Camera);
        PaintCameraIndicator();
        return button;
    }

    private void PaintCameraIndicator()
    {
        if (cameraDot is null || cameraHalo is null) return;
        var theme = Theme.Current;
        bool active = model.Camera.IsInUse;
        cameraHalo.Fill = active && !Theme.Flat ? Theme.Brush(theme.OnSoft) : Brushes.Transparent;
        cameraDot.Fill = Theme.Brush(active ? theme.On : theme.Muted);
        cameraDot.Effect = active && !Theme.Flat ? new System.Windows.Media.Effects.DropShadowEffect { Color = theme.On, BlurRadius = 4, ShadowDepth = 0, Opacity = 0.42 } : null;
    }
}
