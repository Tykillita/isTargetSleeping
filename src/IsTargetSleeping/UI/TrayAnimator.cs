using System.Diagnostics;
using System.Windows.Threading;

namespace IsTargetSleeping.UI;

/// Pinta el ícono de la bandeja fotograma a fotograma mientras algo cambia (a 20 fps,
/// y el temporizador se para en cuanto acaba). Los bucles del radar se dibujan una
/// vez por tamaño y color de barra y se reutilizan; las transiciones cortas se dibujan
/// al vuelo (≈1 ms por fotograma a 16–32 px). Sin animaciones (el ajuste de la app o
/// los «Efectos de animación» de Windows), cada cambio va directo a su pose final.
public sealed class TrayAnimator : IDisposable
{
    private readonly TrayIcon tray;
    private readonly DispatcherTimer timer;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Dictionary<(int Px, bool Light, bool Searching, int Frame), IntPtr> loops = [];
    private TrayMotion? motion;
    private (string Tip, bool Percent, int Value, SystemMemory.Level Pressure) input;
    private object? painted;
    private int px;
    private bool light;

    public TrayAnimator(TrayIcon tray)
    {
        this.tray = tray;
        timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromSeconds(1 / TrayMotion.Fps) };
        timer.Tick += (_, _) => Paint();
        Measure();
    }

    private double Now => clock.Elapsed.TotalSeconds;

    /// El estado actual; compara con el anterior, elige la animación y pinta.
    public void Show(TrayPhase phase, bool awake, string tip, bool showPercent, int percent, SystemMemory.Level pressure, bool animate)
    {
        double now = Now;
        if (motion is null) motion = new TrayMotion(phase, awake, animate);
        else
        {
            motion.SetAnimate(animate, now);
            motion.Set(phase, awake, now);
        }
        input = (tip, showPercent, percent, pressure);
        Paint();
    }

    /// Cambió el DPI o el color de la barra: los fotogramas guardados ya no sirven.
    public void Reset()
    {
        var stale = loops.Values.ToList();
        loops.Clear();
        painted = null;
        Measure();
        if (motion is not null) Paint();
        foreach (var icon in stale) Win32.DestroyIcon(icon);
    }

    private void Measure()
    {
        px = tray.IconSize;
        light = Theme.TaskbarIsLight();
    }

    private void Paint()
    {
        if (motion is not { } m) return;
        double now = Now;
        bool running;
        if (input.Percent)
        {
            // El % no tiene mira: mientras cambia de estado, la franja de luz recorre la barra.
            var dot = m.Phase switch { TrayPhase.On => Mark.Dot.On, TrayPhase.Off => Mark.Dot.None, _ => Mark.Dot.Busy };
            int? frame = m.Animate && dot == Mark.Dot.Busy
                ? (int)Math.Floor(now * TrayMotion.Fps) % TrayMotion.LoopFrames : null;
            running = frame is not null;
            var key = (input, dot, frame, px, light);
            if (!Equals(painted, key))
            {
                painted = key;
                double? shimmer = frame is int f ? (double)f / TrayMotion.LoopFrames : null;
                tray.Update(Mark.PercentBitmap(px, input.Value, input.Pressure, dot, light, shimmer), input.Tip);
            }
        }
        else if (m.LoopFrame(now) is int frame)
        {
            running = true;
            bool searching = m.Kind == TrayAnimation.Searching;
            var key = (input.Tip, searching, frame, px, light);
            if (!Equals(painted, key))
            {
                painted = key;
                if (!loops.TryGetValue((px, light, searching, frame), out var icon))
                {
                    icon = Mark.ToHIcon(Mark.PoseBitmap(px, TrayMotion.Loop(searching, frame), light));
                    loops[(px, light, searching, frame)] = icon;
                }
                tray.SetIcon(icon, input.Tip, owned: false);
            }
        }
        else
        {
            running = m.IsAnimating(now);
            var pose = m.PoseAt(now);
            var key = (input.Tip, pose, px, light);
            if (!Equals(painted, key))
            {
                painted = key;
                tray.Update(Mark.PoseBitmap(px, pose, light), input.Tip);
            }
        }
        if (running && !timer.IsEnabled) timer.Start();
        else if (!running) timer.Stop();
    }

    /// Después de `TrayIcon.Dispose`: el ícono que muestra puede ser uno de estos.
    public void Dispose()
    {
        timer.Stop();
        foreach (var icon in loops.Values) Win32.DestroyIcon(icon);
        loops.Clear();
    }
}
