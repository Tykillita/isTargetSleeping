using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace IsTargetSleeping.UI;

/// Base de lo que dibuja el widget Equipo: se repinta con el tema y tiene un pulso (0→1, en bucle) que solo corre mientras
/// hay algo en marcha, el control se ve y el movimiento no está reducido. Sin eso no hay ninguna animación en reposo.
public abstract class TeamPulseElement : FrameworkElement
{
    public static readonly DependencyProperty PulseProperty = DependencyProperty.Register(nameof(Pulse), typeof(double), typeof(TeamPulseElement),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AnimatedProperty = DependencyProperty.Register(nameof(Animated), typeof(bool), typeof(TeamPulseElement),
        new FrameworkPropertyMetadata(false, (d, _) => ((TeamPulseElement)d).SyncPulse()));
    public static readonly DependencyProperty MotionProperty = DependencyProperty.Register(nameof(Motion), typeof(bool), typeof(TeamPulseElement),
        new FrameworkPropertyMetadata(true, (d, _) => ((TeamPulseElement)d).SyncPulse()));

    public double Pulse { get => (double)GetValue(PulseProperty); set => SetValue(PulseProperty, value); }
    /// Hay algo trabajando aquí: el halo respira.
    public bool Animated { get => (bool)GetValue(AnimatedProperty); set => SetValue(AnimatedProperty, value); }
    /// Falso con «movimiento reducido»: el estado se ve igual pero sin respirar.
    public bool Motion { get => (bool)GetValue(MotionProperty); set => SetValue(MotionProperty, value); }

    protected TeamPulseElement()
    {
        IsVisibleChanged += (_, _) => SyncPulse();
        Loaded += (_, _) => { Theme.Changed += InvalidateVisual; SyncPulse(); };
        Unloaded += (_, _) => { Theme.Changed -= InvalidateVisual; BeginAnimation(PulseProperty, null); };
    }

    private void SyncPulse()
    {
        if (Animated && Motion && IsVisible && IsLoaded)
            BeginAnimation(PulseProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1.2)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        else { BeginAnimation(PulseProperty, null); Pulse = 0; }
    }

    protected static Color Shade(Color c, double factor) =>
        Color.FromArgb(c.A, (byte)Math.Clamp(c.R * factor, 0, 255), (byte)Math.Clamp(c.G * factor, 0, 255), (byte)Math.Clamp(c.B * factor, 0, 255));
}

/// La insignia de cada agente. La forma, no solo el color, dice quién es (con alto contraste los colores se igualan):
/// Claude = cuadrado redondeado ámbar con un destello de pétalos (Opus, con aro y más pétalos; Sonnet; Haiku, más pequeño);
/// Codex = hexágono azul profundo con un indicador de terminal «&gt;_». Marca propia de la app, no los logotipos oficiales.
public sealed class TeamAgentBadge : TeamPulseElement
{
    public static readonly DependencyProperty AgentProperty = DependencyProperty.Register(nameof(Agent), typeof(TeamAgent), typeof(TeamAgentBadge),
        new FrameworkPropertyMetadata(TeamAgent.Sonnet, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(StepState), typeof(TeamAgentBadge),
        new FrameworkPropertyMetadata(StepState.Pending, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ConcernProperty = DependencyProperty.Register(nameof(Concern), typeof(bool), typeof(TeamAgentBadge),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public TeamAgent Agent { get => (TeamAgent)GetValue(AgentProperty); set => SetValue(AgentProperty, value); }
    public StepState State { get => (StepState)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    /// Terminó con reparos (la revisión no lo dio por bueno del todo).
    public bool Concern { get => (bool)GetValue(ConcernProperty); set => SetValue(ConcernProperty, value); }

    static TeamAgentBadge()
    {
        // Tamaño por defecto de la insignia; cada plantilla pone el suyo y no queda fijado por el constructor.
        WidthProperty.OverrideMetadata(typeof(TeamAgentBadge), new FrameworkPropertyMetadata(32d));
        HeightProperty.OverrideMetadata(typeof(TeamAgentBadge), new FrameworkPropertyMetadata(32d));
    }

    // Longitudes relativas de los pétalos del destello: desiguales a propósito, para que parezca orgánico y no una rueda de radios.
    private static readonly double[] SonnetRays = [1.0, 0.7, 0.92, 0.62, 0.98, 0.74, 0.9, 0.66, 1.0, 0.72];
    private static readonly double[] OpusRays = [1.0, 0.74, 0.94, 0.64, 0.98, 0.7, 0.92, 0.78, 1.0, 0.66, 0.9, 0.72];
    private static readonly double[] HaikuRays = [1.0, 0.68, 0.94, 0.7, 0.98, 0.64, 0.9, 0.72];

    protected override void OnRender(DrawingContext dc)
    {
        double s = Math.Min(ActualWidth, ActualHeight);
        if (s < 8) return;
        var theme = Theme.Current;
        bool codex = Agent == TeamAgent.Codex, detail = s >= 20;
        var main = codex ? theme.On : ClaudeFill(theme);
        double dim = State == StepState.Pending ? 0.62 : 1;
        var center = new Point(s / 2, s / 2);
        double body = s * 0.88;    // deja sitio al halo
        var origin = new Point((s - body) / 2, (s - body) / 2);
        var dark = theme.Bg.A < 255 ? Color.FromRgb(12, 12, 14) : theme.Bg;

        if (State == StepState.Running && Animated)
        {
            double halo = body / 2 + 1 + Pulse * 3;
            dc.DrawEllipse(null, new Pen(Theme.Brush(Theme.WithOpacity(main, 0.75 * (1 - Pulse))), 1.5), center, halo, halo);
        }

        Geometry shape = codex ? Hexagon(center, body / 2) : new RectangleGeometry(new Rect(origin, new Size(body, body)), body * 0.3, body * 0.3);
        // Relleno con volumen: más claro arriba, más hondo abajo; el azul de Codex cae hacia el índigo para no confundirse con el ámbar.
        var top = Shade(main, codex ? 1.12 : 1.1);
        var bottom = codex ? Color.FromRgb((byte)(main.R * 0.34), (byte)(main.G * 0.42), (byte)Math.Min(255, main.B * 0.82)) : Shade(main, 0.74);
        dc.PushOpacity(dim);
        dc.DrawGeometry(new LinearGradientBrush(top, bottom, 90), null, shape);
        if (detail)
        {
            // Brillo de la mitad de arriba y borde de luz: el relieve que separa la insignia del fondo.
            dc.PushClip(shape);
            dc.DrawRectangle(new LinearGradientBrush(Color.FromArgb(70, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90), null, new Rect(origin.X, origin.Y, body, body * 0.55));
            dc.Pop();
            dc.DrawGeometry(null, new Pen(new LinearGradientBrush(Color.FromArgb(110, 255, 255, 255), Color.FromArgb(20, 255, 255, 255), 90), 1), shape);
        }
        dc.Pop();

        dc.PushOpacity(dim);
        if (codex) DrawCodexMark(dc, center, body, s);
        else DrawClaudeMark(dc, center, body, s);
        dc.Pop();

        if (s >= 24 && (State is StepState.Done or StepState.Failed || Concern)) DrawState(dc, theme, s, dark);
    }

    /// Fondo de la insignia de Claude: blanco en el tema oscuro y el color de texto del sistema (negro o blanco) en alto contraste.
    private static Color ClaudeFill(Theme theme) => theme.Warm == theme.Fg ? theme.Warm : Color.FromRgb(246, 246, 248);

    private void DrawClaudeMark(DrawingContext dc, Point c, double body, double s)
    {
        // El destello se lee sobre cualquier relleno: en alto contraste el ámbar pasa a ser el color de texto del sistema (negro o blanco).
        var fill = ClaudeFill(Theme.Current);
        bool darkFill = (0.2126 * fill.R + 0.7152 * fill.G + 0.0722 * fill.B) < 110;
        var ink = Theme.Brush(darkFill ? Color.FromArgb(240, 255, 255, 255) : Color.FromArgb(235, 40, 24, 8));
        var rays = Agent switch { TeamAgent.Opus => OpusRays, TeamAgent.Haiku => HaikuRays, _ => SonnetRays };
        double radius = body * (Agent == TeamAgent.Haiku ? 0.3 : 0.37), width = radius * (Agent == TeamAgent.Haiku ? 0.2 : 0.17);
        for (int i = 0; i < rays.Length; i++)
        {
            double a = -Math.PI / 2 + 2 * Math.PI * i / rays.Length;
            double length = radius * rays[i];
            var dir = new Vector(Math.Cos(a), Math.Sin(a));
            var normal = new Vector(-dir.Y, dir.X);
            var baseP = c + dir * (radius * 0.1);
            var tip = c + dir * length;
            var mid = c + dir * (length * 0.5);
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(baseP, true, true);
                ctx.QuadraticBezierTo(mid + normal * width, tip, true, true);
                ctx.QuadraticBezierTo(mid - normal * width, baseP, true, true);
            }
            g.Freeze();
            dc.DrawGeometry(ink, null, g);
        }
        dc.DrawEllipse(ink, null, c, radius * 0.16, radius * 0.16);
        if (Agent == TeamAgent.Opus) dc.DrawEllipse(null, new Pen(ink, Math.Max(1, s / 24)), c, body * 0.44, body * 0.44);
    }

    private static void DrawCodexMark(DrawingContext dc, Point c, double body, double s)
    {
        var ink = Theme.Brush(Color.FromArgb(245, 246, 249, 255));
        var pen = new Pen(ink, Math.Max(1.4, s / 9)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        double k = body * 0.27;
        // Un indicador de terminal: «>» y el cursor «_».
        dc.DrawGeometry(null, pen, Polyline(new Point(c.X - k * 0.9, c.Y - k * 0.72), new Point(c.X - k * 0.1, c.Y - k * 0.02), new Point(c.X - k * 0.9, c.Y + k * 0.68)));
        dc.DrawLine(pen, new Point(c.X + k * 0.15, c.Y + k * 0.7), new Point(c.X + k * 0.95, c.Y + k * 0.7));
    }

    private void DrawState(DrawingContext dc, Theme theme, double s, Color dark)
    {
        double d = s * 0.44;
        var c = new Point(s - d / 2, s - d / 2);
        var color = State == StepState.Failed ? theme.Danger : Concern ? theme.Warm : theme.On;
        dc.DrawEllipse(Theme.Brush(dark), null, c, d / 2 + 1.2, d / 2 + 1.2);
        dc.DrawEllipse(new LinearGradientBrush(Shade(color, 1.12), Shade(color, 0.82), 90), null, c, d / 2 - 0.4, d / 2 - 0.4);
        var pen = new Pen(Theme.Brush(Color.FromRgb(12, 12, 14)), Math.Max(1.3, s / 16)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        double k = d * 0.2;
        if (State == StepState.Failed) { dc.DrawLine(pen, new Point(c.X - k, c.Y - k), new Point(c.X + k, c.Y + k)); dc.DrawLine(pen, new Point(c.X + k, c.Y - k), new Point(c.X - k, c.Y + k)); }
        else if (Concern) { dc.DrawLine(pen, new Point(c.X, c.Y - k * 1.2), new Point(c.X, c.Y + k * 0.35)); dc.DrawEllipse(pen.Brush, null, new Point(c.X, c.Y + k * 1.1), 0.9, 0.9); }
        else dc.DrawGeometry(null, pen, Polyline(new Point(c.X - k * 1.2, c.Y), new Point(c.X - k * 0.3, c.Y + k * 0.9), new Point(c.X + k * 1.3, c.Y - k * 0.9)));
    }

    private static StreamGeometry Hexagon(Point center, double radius)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        for (int i = 0; i < 6; i++)
        {
            double a = Math.PI / 3 * i - Math.PI / 2;
            var p = new Point(center.X + Math.Cos(a) * radius, center.Y + Math.Sin(a) * radius);
            if (i == 0) c.BeginFigure(p, true, true); else c.LineTo(p, true, true);
        }
        g.Freeze();
        return g;
    }

    internal static StreamGeometry Polyline(params Point[] points)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(points[0], false, false);
            foreach (var p in points.Skip(1)) c.LineTo(p, true, true);
        }
        g.Freeze();
        return g;
    }
}

/// Anillo con un arco por paso del plan (hecho, trabajando, falló, pendiente): la vista de una celda de la tubería.
public sealed class TeamRing : TeamPulseElement
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(nameof(Segments), typeof(IReadOnlyList<StepState>), typeof(TeamRing),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<StepState>? Segments { get => (IReadOnlyList<StepState>?)GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }

    static TeamRing()
    {
        WidthProperty.OverrideMetadata(typeof(TeamRing), new FrameworkPropertyMetadata(64d));
        HeightProperty.OverrideMetadata(typeof(TeamRing), new FrameworkPropertyMetadata(64d));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var list = Segments;
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size < 16) return;
        var theme = Theme.Current;
        double stroke = Math.Max(3, size / 11), radius = (size - stroke) / 2 - 3;
        var center = new Point(size / 2, size / 2);
        int n = Math.Max(1, list?.Count ?? 0);
        double gap = n == 1 ? 0 : 10, sweep = (360.0 - gap * n) / n;
        for (int i = 0; i < n; i++)
        {
            var state = list is { Count: > 0 } ? list[i] : StepState.Pending;
            var color = state switch
            {
                StepState.Done => theme.On,
                StepState.Running => theme.Accent,
                StepState.Failed => theme.Danger,
                _ => Theme.WithOpacity(theme.Muted, 0.42),
            };
            double opacity = state == StepState.Running ? 0.55 + 0.45 * (Motion ? 1 - Pulse : 1) : 1;
            double start = -90 + gap / 2 + i * (sweep + gap);
            dc.DrawGeometry(null, new Pen(Theme.Brush(Theme.WithOpacity(color, opacity)), stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Arc(center, radius, start, start + Math.Max(sweep, 1)));
        }
    }

    private static StreamGeometry Arc(Point center, double radius, double fromDeg, double toDeg)
    {
        Point At(double deg) => new(center.X + radius * Math.Cos(deg * Math.PI / 180), center.Y + radius * Math.Sin(deg * Math.PI / 180));
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(At(fromDeg), false, false);
            c.ArcTo(At(toDeg), new Size(radius, radius), 0, toDeg - fromDeg > 180, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }
}

/// El hilo que une los pasos de la tubería: se llena hasta el paso activo y la cabeza brilla mientras trabaja.
public sealed class TeamTrack : TeamPulseElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(nameof(Fraction), typeof(double), typeof(TeamTrack),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, (d, e) => ((TeamTrack)d).Slide((double)e.NewValue)));
    public static readonly DependencyProperty ShownProperty = DependencyProperty.Register(nameof(Shown), typeof(double), typeof(TeamTrack),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public double Shown { get => (double)GetValue(ShownProperty); private set => SetValue(ShownProperty, value); }

    static TeamTrack() => HeightProperty.OverrideMetadata(typeof(TeamTrack), new FrameworkPropertyMetadata(4d));

    private void Slide(double to)
    {
        if (!Motion || !IsLoaded) { BeginAnimation(ShownProperty, null); Shown = to; return; }
        BeginAnimation(ShownProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(450)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 4) return;
        var theme = Theme.Current;
        double y = h / 2, shown = Math.Clamp(Shown, 0, 1) * w;
        dc.DrawRoundedRectangle(Theme.Brush(Theme.WithOpacity(theme.Muted, 0.3)), null, new Rect(0, y - 1, w, 2), 1, 1);
        if (shown < 1) return;
        dc.DrawRoundedRectangle(new LinearGradientBrush(Theme.WithOpacity(theme.On, 0.5), theme.On, 0), null, new Rect(0, y - 1.25, shown, 2.5), 1.25, 1.25);
        if (Animated)
            dc.DrawEllipse(Theme.Brush(Theme.WithOpacity(theme.On, 0.9 - 0.6 * Pulse)), null, new Point(shown, y), 2 + 3 * Pulse, 2 + 3 * Pulse);
    }
}

/// El hilo entre dos pasos de la tubería (cada nodo pone media distancia a cada lado). Apagado es una línea fina de puntos; encendido, un trazo con
/// degradado y un resplandor; y mientras el paso de su lado trabaja (`Flow`) le corren unas chispas hacia el siguiente: se ve que pasa algo.
public sealed class TeamWire : TeamPulseElement
{
    public static readonly DependencyProperty LitProperty = DependencyProperty.Register(nameof(Lit), typeof(bool), typeof(TeamWire),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FlowProperty = DependencyProperty.Register(nameof(Flow), typeof(bool), typeof(TeamWire),
        new FrameworkPropertyMetadata(false, (d, _) => ((TeamWire)d).Animated = ((TeamWire)d).Flow));
    /// El tramo ya se recorrió: se enciende.
    public bool Lit { get => (bool)GetValue(LitProperty); set => SetValue(LitProperty, value); }
    /// Corren chispas por el hilo (el paso de este lado está trabajando).
    public bool Flow { get => (bool)GetValue(FlowProperty); set => SetValue(FlowProperty, value); }

    static TeamWire() => HeightProperty.OverrideMetadata(typeof(TeamWire), new FrameworkPropertyMetadata(8d));

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, y = ActualHeight / 2;
        if (w < 2) return;
        var theme = Theme.Current;
        if (Lit)
        {
            var glow = Theme.WithOpacity(theme.On, 0.22);
            dc.DrawRoundedRectangle(Theme.Brush(glow), null, new Rect(0, y - 2.5, w, 5), 2.5, 2.5);
            dc.DrawRoundedRectangle(new LinearGradientBrush(Theme.WithOpacity(theme.On, 0.55), theme.On, 0), null, new Rect(0, y - 1.25, w, 2.5), 1.25, 1.25);
        }
        else
        {
            var pen = new Pen(Theme.Brush(Theme.WithOpacity(theme.Muted, 0.55)), 1.5) { DashStyle = new DashStyle([0.1, 3.2], 0), DashCap = PenLineCap.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(pen, new Point(0, y), new Point(w, y));
        }
        if (!Flow || !Motion) return;
        // Tres chispas que avanzan en fila y se apagan al llegar al final.
        for (int i = 0; i < 3; i++)
        {
            double at = (Pulse + i / 3.0) % 1.0, fade = Math.Sin(at * Math.PI);
            dc.DrawEllipse(Theme.Brush(Theme.WithOpacity(theme.Accent, 0.25 + 0.7 * fade)), null, new Point(at * w, y), 1.6 + 1.4 * fade, 1.6 + 1.4 * fade);
        }
    }
}

/// Un arco de luz que gira alrededor del agente que trabaja: es lo que hace que un paso largo se vea vivo y no parado.
public sealed class TeamHalo : FrameworkElement
{
    public static readonly DependencyProperty AngleProperty = DependencyProperty.Register(nameof(Angle), typeof(double), typeof(TeamHalo),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SpinProperty = DependencyProperty.Register(nameof(Spin), typeof(bool), typeof(TeamHalo),
        new FrameworkPropertyMetadata(false, (d, _) => ((TeamHalo)d).Sync()));
    public static readonly DependencyProperty MotionProperty = DependencyProperty.Register(nameof(Motion), typeof(bool), typeof(TeamHalo),
        new FrameworkPropertyMetadata(true, (d, _) => ((TeamHalo)d).Sync()));

    public double Angle { get => (double)GetValue(AngleProperty); set => SetValue(AngleProperty, value); }
    /// El paso está trabajando.
    public bool Spin { get => (bool)GetValue(SpinProperty); set => SetValue(SpinProperty, value); }
    /// Falso con «movimiento reducido»: el arco se queda quieto.
    public bool Motion { get => (bool)GetValue(MotionProperty); set => SetValue(MotionProperty, value); }

    public TeamHalo()
    {
        IsHitTestVisible = false;
        IsVisibleChanged += (_, _) => Sync();
        Loaded += (_, _) => { Theme.Changed += InvalidateVisual; Sync(); };
        Unloaded += (_, _) => { Theme.Changed -= InvalidateVisual; BeginAnimation(AngleProperty, null); };
    }

    private void Sync()
    {
        if (Spin && Motion && IsVisible && IsLoaded) BeginAnimation(AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever });
        else { BeginAnimation(AngleProperty, null); Angle = Spin ? 220 : 0; }
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (!Spin) return;
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size < 16) return;
        var theme = Theme.Current;
        double stroke = 2.2, radius = (size - stroke) / 2, cx = size / 2;
        // La estela: tres tramos que se aclaran hacia la cola, para que el arco parezca moverse.
        for (int i = 0; i < 3; i++)
        {
            double start = Angle - 38 * (i + 1), sweep = 38;
            var pen = new Pen(Theme.Brush(Theme.WithOpacity(theme.Accent, 0.95 - 0.3 * i)), stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, pen, Arc(new Point(cx, cx), radius, start, start + sweep));
        }
    }

    private static StreamGeometry Arc(Point center, double radius, double fromDeg, double toDeg)
    {
        Point At(double deg) => new(center.X + radius * Math.Cos(deg * Math.PI / 180), center.Y + radius * Math.Sin(deg * Math.PI / 180));
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(At(fromDeg), false, false);
            c.ArcTo(At(toDeg), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }
}

/// Un número que rueda hasta su valor (tokens, segundos). Con movimiento reducido salta directo.
public sealed class RollingNumber : TextBlock
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(long), typeof(RollingNumber),
        new PropertyMetadata(0L, (d, e) => ((RollingNumber)d).Roll((long)e.NewValue)));
    public static readonly DependencyProperty ShownProperty = DependencyProperty.Register(nameof(Shown), typeof(double), typeof(RollingNumber),
        new PropertyMetadata(0d, (d, e) => ((RollingNumber)d).Write((double)e.NewValue)));
    public static readonly DependencyProperty SuffixProperty = DependencyProperty.Register(nameof(Suffix), typeof(string), typeof(RollingNumber),
        new PropertyMetadata("", (d, _) => ((RollingNumber)d).Write(((RollingNumber)d).Shown)));
    public static readonly DependencyProperty MotionProperty = DependencyProperty.Register(nameof(Motion), typeof(bool), typeof(RollingNumber), new PropertyMetadata(true));

    public long Value { get => (long)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Shown { get => (double)GetValue(ShownProperty); set => SetValue(ShownProperty, value); }
    /// Texto que va detrás del número («tokens», «s»).
    public string Suffix { get => (string)GetValue(SuffixProperty); set => SetValue(SuffixProperty, value); }
    public bool Motion { get => (bool)GetValue(MotionProperty); set => SetValue(MotionProperty, value); }

    private void Roll(long to)
    {
        if (!Motion || !IsLoaded || Math.Abs(to - Shown) < 2) { BeginAnimation(ShownProperty, null); Shown = to; return; }
        BeginAnimation(ShownProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(500)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }

    private void Write(double v) => Text = L10n.Number((long)Math.Round(v)) + (Suffix.Length > 0 ? " " + Suffix : "");
}

/// Reparto de tokens entre Claude (ámbar) y Codex (azul). Sin datos, solo la pista. Es un reparto de lo gastado, no una cifra de ahorro.
public sealed class TokenSplitBar : FrameworkElement
{
    public static readonly DependencyProperty ClaudeShareProperty = DependencyProperty.Register(nameof(ClaudeShare), typeof(double), typeof(TokenSplitBar),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty HasDataProperty = DependencyProperty.Register(nameof(HasData), typeof(bool), typeof(TokenSplitBar),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public double ClaudeShare { get => (double)GetValue(ClaudeShareProperty); set => SetValue(ClaudeShareProperty, value); }
    public bool HasData { get => (bool)GetValue(HasDataProperty); set => SetValue(HasDataProperty, value); }

    public TokenSplitBar() { Height = 6; Loaded += (_, _) => Theme.Changed += InvalidateVisual; Unloaded += (_, _) => Theme.Changed -= InvalidateVisual; }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 4) return;
        var theme = Theme.Current;
        var clip = new RectangleGeometry(new Rect(0, 0, w, h), h / 2, h / 2);
        dc.PushClip(clip);
        dc.DrawRectangle(Theme.Brush(Theme.WithOpacity(theme.Muted, 0.25)), null, new Rect(0, 0, w, h));
        if (HasData)
        {
            double claude = Math.Clamp(ClaudeShare, 0, 1) * w;
            dc.DrawRectangle(Theme.Brush(theme.Warm), null, new Rect(0, 0, claude, h));
            dc.DrawRectangle(Theme.Brush(theme.On), null, new Rect(claude, 0, w - claude, h));
            if (claude > 1 && claude < w - 1) dc.DrawRectangle(Theme.Brush(Color.FromRgb(12, 12, 14)), null, new Rect(claude - 0.75, 0, 1.5, h));
        }
        dc.Pop();
    }
}
