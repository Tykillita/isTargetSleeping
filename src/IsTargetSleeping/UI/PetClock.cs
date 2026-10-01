using System.Diagnostics;
using System.Windows.Threading;

namespace IsTargetSleeping.UI;

/// El reloj de la animación de la mascota: un hilo aparte con un temporizador de alta
/// resolución (precisión de ~1 ms, frente a los 15,6 ms de `DispatcherTimer`) que pide
/// cada fotograma al hilo de la interfaz a ritmo constante. Si un fotograma aún no se
/// pintó, no encola otro; si se queda atrás, no recupera a ráfagas. Solo corre mientras
/// hay algo que animar.
public sealed class PetClock(Action tick) : IDisposable
{
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private volatile int generation, pending;
    private volatile bool running;
    private double interval = 1 / 60.0;

    public double Fps { get; private set; }

    /// A `fps` fotogramas por segundo; 0 lo para.
    public void Run(double fps)
    {
        if (fps <= 0) { Stop(); return; }
        Fps = fps;
        interval = 1 / fps;
        if (running) return;
        running = true;
        int mine = ++generation;
        new Thread(() => Loop(mine)) { IsBackground = true, Name = "isTargetSleeping pet clock", Priority = ThreadPriority.AboveNormal }.Start();
    }

    public void Stop()
    {
        running = false;
        Fps = 0;
    }

    private void Loop(int mine)
    {
        var timer = Win32.CreateWaitableTimerEx(IntPtr.Zero, null, Win32.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, Win32.TIMER_ALL_ACCESS);
        var clock = Stopwatch.StartNew();
        double next = 0;
        try
        {
            while (running && mine == generation)
            {
                next += interval;
                double wait = next - clock.Elapsed.TotalSeconds;
                if (wait < -interval) { next = clock.Elapsed.TotalSeconds; wait = 0; }   // se quedó atrás: sin ráfagas
                if (wait > 0) Sleep(timer, wait);
                if (!running || mine != generation) break;
                if (Interlocked.Exchange(ref pending, 1) == 0)
                    dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
                    {
                        pending = 0;
                        if (running && mine == generation) tick();
                    });
            }
        }
        finally
        {
            if (timer != IntPtr.Zero) Win32.CloseHandle(timer);
        }
    }

    private static void Sleep(IntPtr timer, double seconds)
    {
        if (timer != IntPtr.Zero)
        {
            long due = -(long)(seconds * 10_000_000);   // relativo, en unidades de 100 ns
            if (Win32.SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
            {
                Win32.WaitForSingleObject(timer, Win32.INFINITE);
                return;
            }
        }
        Thread.Sleep(Math.Max(1, (int)(seconds * 1000)));
    }

    public void Dispose() => Stop();
}
