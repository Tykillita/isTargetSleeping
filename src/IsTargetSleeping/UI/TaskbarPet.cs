using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using IsTargetSleeping.UI.Pets;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// La mascota junto a Inicio: opcional, en su propia ventana (Explorer no se toca).
/// Junta las piezas: lo que pasa en Ollama (`PetBrain`), cómo se mueve (`PetAnimations`,
/// `PetWalker`), dónde va (`TaskbarLocator`, `TaskbarPetLayout`), cómo se ve (la especie
/// elegida de `PetCatalog`), cómo se anima (`PetDirector`: transiciones, gestos,
/// reacciones, partículas, a 60 fps con `PetClock`) y la ventana (`PetSurface`). El logo de Windows es parte de su
/// casa: una segunda ventana, que nunca recibe clics, pinta sus «luces» encima del logo
/// real (`WindowsLogo`). Solo anima mientras se ve.
public sealed class TaskbarPet : IDisposable
{
    private readonly Supervisor supervisor;
    private readonly Prefs prefs;
    private readonly TaskbarLocator locator = new();
    private readonly PetBrain brain = new();
    private PetWalker walker = new(Environment.TickCount);
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly DispatcherTimer poll;
    private readonly PetClock petClock;
    private PetDirector director;
    private PixelCanvas? canvas;
    private readonly Win32.WinEventProc foregroundProc;
    private IntPtr foregroundHook;
    private PetSurface? surface, lights;
    private WindowsLogo? logo;
    private Win32.RECT? probedStart;
    private PetGlow? drawnGlow;
    private TaskbarLocator.Snapshot? snapshot;
    private PetSpot? spot;
    private IPetSpecies species = PetCatalog.All[0];
    private (PetPose Pose, int X, int Y, int Scale)? drawn;
    // Deslizarse entre sitios en vez de saltar (en píxeles físicos, con decimales).
    private (double X, double Y)? shownBase, slideTo;
    private (double X, double Y) slideFrom;
    private double slideStart;
    private bool slideCover, wasBehind, drawnCovered;
    private bool scanning, disposed, interactive, probing;
    private int generation;
    private double lastFrame;
    private DateTime? lastClean, lastNap;

    // Ratón: mirarla, tocarla y acariciarla (ir y venir sobre ella).
    private bool hovered;
    private int hoverX, hoverY, lastMouseX, lastDirection;
    private readonly List<double> turns = [];
    private double heartsUntil;

    private bool Enabled => prefs.Switch(PrefKeys.TaskbarPet, false);
    private double Now => clock.Elapsed.TotalSeconds;

    public TaskbarPet(Supervisor supervisor)
    {
        this.supervisor = supervisor;
        prefs = supervisor.Prefs;
        poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        poll.Tick += (_, _) => Refresh();
        petClock = new PetClock(Render);
        director = new PetDirector(species.Anchors, Environment.TickCount, species.Animations);
        foregroundProc = OnForeground;
        lastClean = supervisor.LastClean?.At;
        lastNap = supervisor.Ollama.AutoReleased?.At;
        prefs.Changed += Sync;
        supervisor.Changed += OnState;
        supervisor.Ollama.Changed += OnState;
        supervisor.Ollama.WatchdogActed += OnWatchdog;
        Sync();
    }

    // MARK: ajustes

    private void Sync()
    {
        if (disposed) return;
        if (!Enabled)
        {
            Stop();
            prefs.TaskbarPetError = null;
            return;
        }
        var chosen = PetCatalog.Find(prefs.PetSpecies);
        if (chosen != species)
        {
            species = chosen;
            brain.SetAnimations(species.Animations);
            director = new PetDirector(species.Anchors, Environment.TickCount, species.Animations);
            walker = new PetWalker(Environment.TickCount);
            surface?.Hide();
            lights?.Hide();
            petClock.Stop();
            canvas = null;
            spot = null;
            drawn = null;
            drawnGlow = null;
            drawnCovered = wasBehind = slideCover = hovered = false;
            shownBase = slideTo = null;
            turns.Clear();
            lastMouseX = lastDirection = 0;
            heartsUntil = 0;
        }
        bool wantsInteraction = prefs.Switch(PrefKeys.TaskbarPetInteractive);
        lights ??= new PetSurface(interactive: false);   // encima del logo: los clics van a Inicio
        if (surface is null)
        {
            surface = new PetSurface(wantsInteraction);
            surface.MouseMoved += OnMouseMove;
            surface.MouseLeft += () => { hovered = false; Render(); };
            surface.Clicked += OnClick;
            surface.RightClicked += ShowMenu;
            interactive = wantsInteraction;
        }
        else if (wantsInteraction != interactive)
        {
            surface.Interactive = interactive = wantsInteraction;
            hovered = false;
        }
        if (foregroundHook == IntPtr.Zero)
            foregroundHook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, foregroundProc, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
        OnState();
        if (!poll.IsEnabled) poll.Start();
        Refresh();
    }

    private void Stop()
    {
        generation++;
        poll.Stop();
        petClock.Stop();
        if (foregroundHook != IntPtr.Zero) Win32.UnhookWinEvent(foregroundHook);
        foregroundHook = IntPtr.Zero;
        surface?.Dispose();
        surface = null;
        lights?.Dispose();
        lights = null;
        logo = null;
        probedStart = null;
        drawnGlow = null;
        snapshot = null;
        spot = null;
        drawn = null;
        hovered = false;
    }

    // MARK: lo que pasa en el sistema

    private void OnState()
    {
        if (disposed || !Enabled) return;
        var ollama = supervisor.Ollama;
        var pressure = ollama.Memory.Pressure switch
        {
            SystemMemory.Level.Critical => PetPressure.Critical,
            SystemMemory.Level.Warning => PetPressure.High,
            _ => PetPressure.Normal,
        };
        double now = Now;
        brain.Update(new PetSignals(
            Up: ollama.Power == Power.On,
            Starting: ollama.Power == Power.Starting,
            Stopping: ollama.Power == Power.Stopping,
            ModelLoaded: ollama.Loaded.Count > 0,
            Loading: ollama.BusyModel is not null && ollama.Loaded.All(m => m.Name != ollama.BusyModel),
            Generating: ollama.IdleSeconds is < 5 && ollama.Loaded.Count > 0,
            Downloading: ollama.Pulling is not null,
            Cleaning: supervisor.Cleaning,
            Pressure: pressure,
            Game: supervisor.Game.Active), now);

        // Reacciones por lo que acaba de pasar.
        if (supervisor.LastClean?.At is { } cleaned && cleaned != lastClean)
        {
            lastClean = cleaned;
            if (supervisor.LastClean?.Freed > 0) brain.React(PetReaction.Sparkle, now);
        }
        if (ollama.AutoReleased?.At is { } napped && napped != lastNap)
        {
            lastNap = napped;
            brain.React(PetReaction.Nap, now);
        }
        Render();
    }

    private void OnWatchdog(WatchdogAction action, string reason)
    {
        if (!Enabled) return;
        brain.React(action == WatchdogAction.GiveUp ? PetReaction.Sad : PetReaction.Dizzy, Now);
        Render();
    }

    /// Al instante, sin esperar al sondeo: Inicio, Búsqueda o pantalla completa la ocultan,
    /// y si se toca la barra se vuelve a poner encima.
    private void OnForeground(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (disposed || surface is null || snapshot is not { } s) return;
        snapshot = s with { Suppressed = TaskbarLocator.Suppressed(s.Monitor) };
        if (TaskbarLocator.TaskbarInFront()) { surface.BringToTop(); lights?.BringToTop(); }
        Render();
    }

    // MARK: dónde va

    private async void Refresh()
    {
        if (disposed || !Enabled || scanning) return;
        scanning = true;
        int request = generation;
        try
        {
            // UI Automation puede esperar a Explorer: nunca desde el hilo del panel.
            var found = await Task.Run(locator.Locate);
            if (disposed || !Enabled || request != generation) return;
            snapshot = found;
            // El logo se vuelve a mirar solo cuando Inicio se mueve, con la mascota oculta
            // un instante para que no salga en la captura.
            if (found is { Visible: true, Suppressed: false, Start: { } button } && !button.Equals(probedStart))
            {
                probedStart = button;
                probing = true;
                surface?.Hide();
                lights?.Hide();
                drawn = null;
                drawnGlow = null;
                try
                {
                    await Task.Delay(80);   // un par de fotogramas de DWM
                    if (disposed || !Enabled || request != generation) return;
                    logo = await Task.Run(() => WindowsLogo.Probe(button));
                }
                finally { probing = false; }
                if (disposed || !Enabled || request != generation) return;
            }
            var placement = prefs.PetPlacement;
            spot = found.Visible && found.Start is { } start
                ? TaskbarPetLayout.Place(placement, found.Bar, start, found.Tray, found.Monitor, found.Dpi, species.Size, logo?.Bounds)
                : null;
            prefs.TaskbarPetError = found.StartMissing ? tr("No pude localizar el botón Inicio en esta barra de tareas.")
                : spot is { Fallback: true } ? tr("No hay sitio a la izquierda de Inicio con esta barra: se muestra sobre él.")
                : null;
            if (surface?.Visible == true && TaskbarLocator.TaskbarInFront()) { surface.BringToTop(); lights?.BringToTop(); }
            Render();
        }
        catch
        {
            // La mascota nunca debe impedir abrir el panel o cerrar la app.
            if (!disposed && Enabled && request == generation)
            {
                surface?.Hide();
                prefs.TaskbarPetError = tr("No se pudo mostrar la mascota junto a Inicio.");
            }
        }
        finally { scanning = false; }
    }

    // MARK: dibujo

    private void Render()
    {
        if (disposed || surface is null || probing) return;
        double now = Now, dt = Math.Clamp(now - lastFrame, 0, 0.1);
        lastFrame = now;
        brain.Expire(now);
        if (!Enabled || spot is not { } place || snapshot is not { Visible: true, Suppressed: false } || brain.Activity == PetActivity.Hidden)
        {
            surface.Hide();
            lights?.Hide();
            drawn = null;
            drawnGlow = null;
            petClock.Stop();
            return;
        }

        bool still = prefs.ReducedMotion;
        var activity = brain.Activity;
        var reaction = brain.Reaction;
        int n = place.Scale;
        int width = species.Size.Width * n, height = species.Size.Height * n;

        // Paseo: solo despierta, sin reacción en curso y sin el ratón encima.
        int x = place.X;
        bool walking = false;
        if (place.MaxX > place.MinX)
        {
            var mode = still ? PetWalker.Mode.GoHome
                : hovered || reaction != PetReaction.None ? PetWalker.Mode.Stay
                : activity == PetActivity.Alert ? PetWalker.Mode.Roam
                : activity is PetActivity.DeepSleep or PetActivity.Drowsy or PetActivity.Yawning or PetActivity.WakingUp ? PetWalker.Mode.GoHome
                : PetWalker.Mode.Stay;
            walker.Update(now, still ? 1e6 : dt, place.MinX, place.MaxX, place.X, mode, species.Animations.WalkSpeed * n);
            x = (int)Math.Round(walker.X);
            walking = walker.Walking || walker.Turning;
        }

        // Esperando a que arranque un modelo: escondida tras el logo, asomándose.
        bool hiding = logo is not null && activity is PetActivity.WakingUp or PetActivity.Drowsy
                      && prefs.PetPlacement == PetPlacement.Left && !place.Fallback && species.Size.HideRight > 0;

        var frame = director.Step(now, dt, new PetSituation(
            activity, reaction, brain.ReactionSince, brain.Pressure, still, hiding,
            walking, walker.Distance / n, walker.Facing, walker.Lean, walker.Turning,
            hovered, hoverX * 2.0 / width - 1, hoverY * 2.0 / height - 1));
        var pose = frame.Pose;

        // Dónde va: su sitio (o detrás del logo) y, si cambia, se desliza hasta él con frenada
        // en vez de saltar; al salir de detrás del logo, el logo la sigue tapando mientras sale.
        (double X, double Y) anchor = pose.Behind && logo is not null
            ? PetHiding.Behind(logo.Bounds, species.Size, n, 0)
            : (x, place.Y);
        if (shownBase is not { } shown || walking || still)
        {
            shownBase = anchor;
            slideTo = null;
        }
        else
        {
            if (slideTo != anchor)
            {
                if (Math.Abs(shown.X - anchor.X) + Math.Abs(shown.Y - anchor.Y) > 2 * n)
                    (slideFrom, slideStart, slideTo, slideCover) = (shown, now, anchor, pose.Behind || wasBehind);
                else
                {
                    shownBase = anchor;
                    slideTo = null;
                }
            }
            if (slideTo is { } goal)
            {
                double k = Math.Min(1, (now - slideStart) / 0.35);
                double e = TrayMotion.EaseOutCubic(k);
                shownBase = (slideFrom.X + (goal.X - slideFrom.X) * e, slideFrom.Y + (goal.Y - slideFrom.Y) * e);
                if (k >= 1) slideTo = null;
            }
        }
        wasBehind = pose.Behind;
        var at = shownBase!.Value;
        x = (int)Math.Round(at.X - (pose.Behind ? pose.Peek * n : 0) + frame.Dx * n);
        int y = (int)Math.Round(at.Y + frame.Dy * n);
        bool covered = logo is not null && (pose.Behind || slideTo is not null && slideCover);

        // Se dibuja de nuevo solo si algo cambió (pose, sitio, partículas o «pensando»).
        bool thinking = activity == PetActivity.Working && reaction == PetReaction.None && !still;
        bool busy = director.Particles.Live.Count > 0 || thinking;
        if (busy || drawn != (pose, x, y, n) || covered != drawnCovered)
        {
            if (canvas is null || canvas.Width != width || canvas.Height != height) canvas = new PixelCanvas(width, height);
            canvas.Clear();
            species.Draw(canvas, pose, n);
            ParticleSprites.Draw(canvas, director.Particles.Live, n);
            if (thinking) ParticleSprites.Thinking(canvas, species.Anchors, n, now);
            var bytes = canvas.ToBgra();
            // Detrás del logo: lo que cae encima de él no se dibuja (y sus clics van a Inicio).
            if (covered) Occlude(bytes, width, height, x, y, logo!.Bounds);
            surface.Render(bytes, width, height, x, y);
            drawn = (pose, x, y, n);
            drawnCovered = covered;
        }

        // Las luces del logo, con sus fundidos.
        var glow = frame.Glow.Quantized();
        if (lights is not null && drawnGlow != glow)
        {
            drawnGlow = glow;
            if (logo?.Lights(glow) is { } overlay)
                lights.Render(overlay, logo.Bounds.Width, logo.Bounds.Height, logo.Bounds.Left, logo.Bounds.Top);
            else lights.Hide();
        }

        // El siguiente fotograma: a 60 fps mientras se mueve, más despacio dormida y nada si
        // no hace falta (o con «Movimiento reducido», cuando acaba la reacción).
        petClock.Run(slideTo is not null || walking ? Math.Max(frame.Fps, 60) : frame.Fps);
    }

    /// Quita de `bytes` los píxeles que caen dentro de `cover` (en pantalla).
    private static void Occlude(byte[] bytes, int width, int height, int x, int y, Win32.RECT cover)
    {
        int left = Math.Max(0, cover.Left - x), right = Math.Min(width, cover.Right - x);
        int top = Math.Max(0, cover.Top - y), bottom = Math.Min(height, cover.Bottom - y);
        for (int row = top; row < bottom; row++)
            if (right > left) Array.Clear(bytes, (row * width + left) * 4, (right - left) * 4);
    }

    // MARK: ratón

    private void OnMouseMove(int x, int y)
    {
        hovered = true;
        hoverX = x;
        hoverY = y;
        // Caricia: ir y venir sobre ella unas cuantas veces en poco tiempo.
        int direction = Math.Sign(x - lastMouseX);
        double now = Now;
        if (direction != 0 && lastDirection != 0 && direction != lastDirection)
        {
            turns.Add(now);
            turns.RemoveAll(t => now - t > 1.5);
            if (turns.Count >= 4 && now >= heartsUntil)
            {
                brain.React(PetReaction.Hearts, now);
                heartsUntil = now + 3;
                turns.Clear();
            }
        }
        if (direction != 0) lastDirection = direction;
        lastMouseX = x;
        Render();
    }

    private void OnClick()
    {
        brain.React(PetReaction.Jump, Now);
        Render();
        supervisor.Execute("show");
    }

    /// Clic derecho: lo más útil de Ollama y dónde vive la mascota.
    private void ShowMenu()
    {
        var ollama = supervisor.Ollama;
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        MenuItem Add(ItemsControl parent, string title, Action action, bool check = false)
        {
            var item = new MenuItem { Header = title, IsChecked = check };
            item.Click += (_, _) => action();
            parent.Items.Add(item);
            return item;
        }

        if (ollama.Power != Power.Missing)
        {
            var power = Add(menu, ollama.Power == Power.On ? tr("Apagar Ollama") : tr("Encender Ollama"), ollama.Toggle);
            power.IsEnabled = !ollama.Power.IsTransition();
            var sleep = Add(menu, tr("Dormir el modelo"), supervisor.Sleep);
            sleep.IsEnabled = ollama.Loaded.Count > 0;
            if (prefs.MainModel is { } main && ollama.Power == Power.On && ollama.Loaded.All(m => m.Name != main))
                Add(menu, tr("Cargar %@", main), () => ollama.Load(main));
            menu.Items.Add(new Separator());
        }
        menu.Items.Add(PlacementMenu(prefs));
        if (SpeciesMenu(prefs) is { } pets) menu.Items.Add(pets);
        Add(menu, tr("Ocultar mascota"), () => prefs.SetSwitch(PrefKeys.TaskbarPet, false));

        menu.Opened += (_, _) =>
        {
            // Sin esto el menú no se cierra al hacer clic fuera.
            if (PresentationSource.FromVisual(menu) is HwndSource source) Win32.SetForegroundWindow(source.Handle);
        };
        menu.IsOpen = true;
    }

    /// «Posición › Sobre Inicio · A la izquierda de Inicio · De paseo» (también en la bandeja).
    public static MenuItem PlacementMenu(Prefs prefs)
    {
        var root = new MenuItem { Header = tr("Posición") };
        foreach (var (placement, title) in Placements)
        {
            var item = new MenuItem { Header = title, IsChecked = prefs.PetPlacement == placement };
            item.Click += (_, _) => prefs.SetPetPlacement(placement);
            root.Items.Add(item);
        }
        return root;
    }

    /// «Mascota › …», solo cuando la colección tiene más de una.
    public static MenuItem? SpeciesMenu(Prefs prefs)
    {
        if (PetCatalog.All.Count < 2) return null;
        var current = PetCatalog.Find(prefs.PetSpecies);
        var root = new MenuItem { Header = tr("Mascota") };
        foreach (var pet in PetCatalog.All)
        {
            var item = new MenuItem { Header = pet.Name, IsChecked = pet == current };
            item.Click += (_, _) => prefs.SetPetSpecies(pet.Id);
            root.Items.Add(item);
        }
        return root;
    }

    public static (PetPlacement, string)[] Placements =>
    [
        (PetPlacement.Above, tr("Sobre Inicio")),
        (PetPlacement.Left, tr("A la izquierda de Inicio")),
        (PetPlacement.Walk, tr("De paseo")),
    ];

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        prefs.Changed -= Sync;
        supervisor.Changed -= OnState;
        supervisor.Ollama.Changed -= OnState;
        supervisor.Ollama.WatchdogActed -= OnWatchdog;
        Stop();
    }
}
