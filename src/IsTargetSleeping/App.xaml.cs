using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using IsTargetSleeping.UI;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping;

/// Vive en la bandeja del sistema, no en la barra de tareas: el ícono con su
/// estado, el panel, el menú del clic derecho y el atajo global.
public partial class App : Application
{
    private OllamaController ollama = null!;
    private Supervisor supervisor = null!;
    private Prefs prefs = null!;
    private DispatcherTimer? petCommands;
    private DateTime lastPetUsageTick;
    private TrayIcon tray = null!;
    private PanelWindow? panel;
    private NotchHost? notch;
    private PanelViewModel model = null!;
    private TrayAnimator animator = null!;
    private TaskbarPet? pet;
    private CaptureToolGuard? captureGuard;
    private HookServer? hookServer;
    private System.Windows.Point widgetDragStart;
    private double widgetResizeDelta;
    private bool widgetResizeDone;
    private bool widgetResizeWide;
    public bool EnsureTrayReady() => tray?.EnsureReady() == true;

    /// Al sacar el ratón del widget de tareas, la tarjeta ensanchada con un clic vuelve a su sitio.
    private void TodoRootLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is WidgetSlot { Model: TodoWidget todo }) todo.WideHover = false;
    }

    /// Los campos de Tareas: dejan visible el final del texto (como Nusa) aunque no tengan el foco, la hora lleva el cursor al final al enmascararse
    /// y el título coge el foco al abrirse el formulario (el notch se abre sin activarse: `FocusForTyping` lo activa para que lleguen las teclas).
    private void TodoInputChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox box || !box.IsVisible) return;
        if (e is SizeChangedEventArgs && !box.IsKeyboardFocused && box.Tag as string == "TodoTitle")
        {
            if (Window.GetWindow(box) is NotchWindow notch) notch.FocusForTyping(box); else box.Focus();
            box.Dispatcher.BeginInvoke(() => box.CaretIndex = box.Text.Length, System.Windows.Threading.DispatcherPriority.Input);
            return;
        }
        if (box.IsKeyboardFocused) { if (box.Tag as string == "TodoTime") box.CaretIndex = box.Text.Length; return; }
        box.Dispatcher.BeginInvoke(() => box.ScrollToHorizontalOffset(box.ExtentWidth), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnWidgetRowMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        widgetDragStart = e.GetPosition((IInputElement)sender);
    }

    private void OnWidgetRowMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || sender is not FrameworkElement { DataContext: WidgetRow { CanDrag: true, WidgetId: { } id } } row
            || e.OriginalSource is ButtonBase) return;
        var point = e.GetPosition(row);
        if (Math.Abs(point.X - widgetDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(point.Y - widgetDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop(row, new DataObject(typeof(string), id), DragDropEffects.Move);
    }

    private void OnWidgetRowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(string)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnWidgetRowDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(string)) is string source && sender is FrameworkElement { DataContext: WidgetRow { DropBefore: { } drop } })
            drop.Execute(source);
        e.Handled = true;
    }

    private void OnWidgetHomeDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(string)) is string source)
        {
            if (sender is FrameworkElement { DataContext: WidgetSlot target }) model.MoveWidgetBefore(source, target.Id);
            else model.AddWidgetToDraft(source);
        }
        e.Handled = true;
    }

    private void OnWidgetResizeDragStarted(object sender, DragStartedEventArgs e)
    {
        widgetResizeDelta = 0;
        widgetResizeDone = false;
        widgetResizeWide = sender is Thumb { DataContext: WidgetSlot slot } && slot.ResizeWide;
    }

    private void OnWidgetResizeDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (widgetResizeDone || sender is not Thumb { DataContext: WidgetSlot slot }) return;
        widgetResizeDelta += e.HorizontalChange;
        if (!WidgetLayout.DragResizeTriggered(widgetResizeWide, widgetResizeDelta)) return;
        slot.Resize.Execute(null);
        widgetResizeDone = true;
        e.Handled = true;
    }

    private void OnWidgetResizeDragCompleted(object sender, DragCompletedEventArgs e)
    {
        widgetResizeDelta = 0;
        widgetResizeDone = false;
    }

    public void StartTray(bool launchedAtLogin, string? pending = null)
    {
        Theme.OutlineStyle = Defaults.GetString(PrefKeys.ThemeStyle) == "outline";
        Theme.Apply();
        ollama = new OllamaController(new StatsStore(Paths.StatsFile));
        prefs = new Prefs();
        supervisor = new Supervisor(ollama, prefs);

        model = new PanelViewModel(supervisor);
        model.ProcessesRequested += () => ShowPanel();   // los procesos son una pestaña de Actividad: solo hay que enseñar el panel
        model.WindowRequested += v => AppWindow.Present(supervisor, v);
        tray = new TrayIcon();
        tray.Click += TogglePanel;
        tray.RightClick += ShowMenu;
        tray.HotKeyPressed += key =>
        {
            if (key == HotKey.Ocr) _ = ReadScreenOcr();
            else if (key == HotKey.Sleep) supervisor.Sleep();
            else if (key == HotKey.Clean) _ = supervisor.Clean(CleanReason.Manual);
            else ollama.Toggle();
        };
        tray.NotificationClicked += () => ShowPanel();
        tray.CommandReceived += supervisor.Execute;
        tray.AppearanceChanged += OnAppearanceChanged;
        animator = new TrayAnimator(tray);
        HotKey.Attach(tray.Handle);

        var notifier = new Notifier(tray, prefs);
        supervisor.Notify += notifier.Show;
        notifier.Shown += model.RaiseNotice;
        model.Hub.Finished += (title, text) => { notifier.Show(Notice.Pomodoro, title, text); model.NoteGoodThing(BondEvent.Focus); };
        model.Hub.TeamFinished += (title, text) => notifier.Show(Notice.Team, title, text);
        ollama.Changed += () => notifier.DownloadProgress(ollama.Pulling);
        prefs.Changed += () => notifier.DownloadProgress(ollama.Pulling);
        supervisor.ShowPanel += activity => ShowPanel(activity ? PanelView.Activity : null);
        supervisor.Changed += Repaint;

        prefs.OnHotKeyChange = ApplyHotKeys;
        prefs.OnOcrHotKeyChange = _ => ApplyHotKeys(prefs.HotKeyEnabled);
        prefs.Changed += Repaint;   // ícono con el % o animado
        // Aplazado: el interruptor está dentro de la vista que va a cambiar de ventana.
        prefs.Changed += () => Dispatcher.BeginInvoke(ApplyNotchDock);
        prefs.OpenLog = OpenLog;
        ApplyHotKeys(prefs.HotKeyEnabled);
        Links.EnsureRegistered();

        ollama.PowerChanged += _ => Repaint();
        ollama.Changed += Repaint;   // el % de RAM del tooltip (y del ícono, si se eligió)
        Repaint();
        ollama.StartPolling();
        supervisor.Start();
        pet = new TaskbarPet(supervisor);
        _ = PinTrayIconAsync();   // por defecto el ícono sale a la barra en vez de quedarse en el menú ^
        captureGuard = new CaptureToolGuard();   // cierra sola la herramienta de recortes si se cuelga y congela la pantalla
        prefs.Changed += ApplyAgentApprovals;
        ApplyAgentApprovals();
        ApplyNotchDock();
        petCommands = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };   // órdenes de la mascota (complemento «Mascota conectada»); sin la carpeta `pet` no hace nada
        petCommands.Tick += (_, _) =>
        {
            // El widget de Uso solo se lee con el panel visible: aquí se lee a petición de la mascota, y cada minuto mientras conversa.
            PetCommands.Poll(HandlePetTool);
            model.PublishPetApprovals(heartbeat: true);
            var petDir = Path.Combine(Paths.DataDir, "pet");
            bool petDirectoryExists = Directory.Exists(petDir);
            bool petActive = petDirectoryExists && PetLease.IsActive(petDir, DateTimeOffset.UtcNow);
            PublishPetState(petDirectoryExists, petActive);
            if (petDirectoryExists) { model.PublishPetSessions(); model.PublishPetTeam(); }
            if (petActive && prefs.Switch(PrefKeys.UsageEnabled, false) && DateTime.UtcNow - lastPetUsageTick > TimeSpan.FromMinutes(1))
            {
                lastPetUsageTick = DateTime.UtcNow;
                model.Hub.Usage.Tick(DateTime.Now);
            }
        };
        petCommands.Start();

        if (pending is not null)
        {
            // Lanzada por un enlace: se atiende cuando ya se sabe el estado de Ollama.
            var later = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            later.Tick += (_, _) => { later.Stop(); supervisor.Execute(pending); };
            later.Start();
        }
        // Primera vez: se abre el panel para que se vea dónde vive y qué hace.
        else if (prefs.ShouldOfferLogin && !launchedAtLogin)
        {
            var once = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.8) };
            once.Tick += (_, _) => { once.Stop(); ShowPanel(); };
            once.Start();
        }
    }

    private DateTime lastPetState;

    /// Estado de solo lectura para la mascota (pomodoro, tareas, clima): cada 10 s mientras conversa y cada minuto si no. El clima solo se lee a petición.
    private void PublishPetState(bool petDirectoryExists, bool petActive)
    {
        var gap = petActive ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(60);
        if (!petDirectoryExists || DateTime.UtcNow - lastPetState < gap) return;
        lastPetState = DateTime.UtcNow;
        var hub = model.Hub;
        if (petActive && prefs.Switch(PrefKeys.WeatherEnabled, false)) hub.Weather.Tick(DateTime.Now);
        PetBridge.PublishAppState(new
        {
            pomodoro = new { running = hub.Pomodoro.Running, focus = hub.Pomodoro.IsFocus, remaining = hub.Pomodoro.Remaining },
            todo = new { done = hub.Todo.DoneCount, total = hub.Todo.TotalCount, items = hub.Todo.Rows.Take(8).Select(r => new { title = r.Title, done = r.Done }).ToList() },
            weather = new { current = hub.Weather.Current },
        });
    }

    /// Herramientas que la mascota puede usar en la app (lista cerrada; todo lo demÃ¡s se rechaza). Corre en el hilo de la interfaz.
    private (bool Ok, string Message) HandlePetTool(string tool, System.Text.Json.JsonElement args)
    {
        switch (tool)
        {
            case "usage.set" when args.ValueKind == System.Text.Json.JsonValueKind.Object && args.TryGetProperty("on", out var on) && on.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False:
                prefs.SetSwitch(PrefKeys.UsageEnabled, on.GetBoolean());
                model.Hub.Usage.Tick(DateTime.Now);   // el widget solo se lee con el panel visible: aquÃ­ se lee a peticiÃ³n
                lastPetUsageTick = DateTime.UtcNow;
                return (true, on.GetBoolean() ? "widget de uso encendido" : "widget de uso apagado");
            case "pomodoro.control" when args.ValueKind == System.Text.Json.JsonValueKind.Object && args.TryGetProperty("action", out var pa) && pa.ValueKind == System.Text.Json.JsonValueKind.String:
            {
                var pom = model.Hub.Pomodoro;
                switch (pa.GetString())
                {
                    case "start": if (!pom.Running) pom.Toggle.Execute(null); return (true, "pomodoro en marcha");
                    case "pause": if (pom.Running) pom.Toggle.Execute(null); return (true, "pomodoro en pausa");
                    case "reset": pom.Reset.Execute(null); return (true, "pomodoro reiniciado");
                    case "skip": pom.Skip.Execute(null); return (true, "fase saltada");
                    default: return (false, "acción desconocida");
                }
            }
            case "todo.add" when args.ValueKind == System.Text.Json.JsonValueKind.Object && args.TryGetProperty("title", out var tt) && tt.ValueKind == System.Text.Json.JsonValueKind.String:
            {
                var todo = model.Hub.Todo;
                if (todo.ShowForm) return (false, "hay un formulario de tareas abierto en la app");
                int before = todo.TotalCount;
                todo.AddTask.Execute(null);
                todo.FormTitle = tt.GetString() ?? "";
                if (args.TryGetProperty("time", out var tm) && tm.ValueKind == System.Text.Json.JsonValueKind.String) todo.FormTime = tm.GetString() ?? "";
                todo.Save.Execute(null);
                return todo.TotalCount > before ? (true, "tarea añadida") : (false, "la app no pudo añadir la tarea");
            }
            case "note.add" when args.ValueKind == System.Text.Json.JsonValueKind.Object && args.TryGetProperty("text", out var nt) && nt.ValueKind == System.Text.Json.JsonValueKind.String:
            {
                var notes = model.Hub.Notes;
                notes.NewNote.Execute(null);
                notes.Text = nt.GetString() ?? "";
                notes.Save();
                return (true, "nota creada");
            }
            case "notify.show" when args.ValueKind == System.Text.Json.JsonValueKind.Object && args.TryGetProperty("text", out var xt) && xt.ValueKind == System.Text.Json.JsonValueKind.String:
            {
                string note = xt.GetString() ?? "";
                bool shown = tray.Notify("Nimbo", note, details: false);   // aviso nativo de Windows (centro de notificaciones)
                model.RaiseNotice(note, 25);                               // y la campana del notch, 25 s (un recordatorio no puede pasar desapercibido)
                AppLog.Write($"aviso Nimbo: {note} [{(shown ? "mostrado" : "Windows lo rechazó")}]");
                return (true, shown ? "aviso mostrado" : "aviso en el notch (Windows no lo mostró)");
            }
            case "agent.resume" when args.ValueKind == System.Text.Json.JsonValueKind.Object && args.TryGetProperty("id", out var rid) && rid.ValueKind == System.Text.Json.JsonValueKind.String:
                return model.PetResume(rid.GetString() ?? "");
            case "team.enqueue" when args.ValueKind == System.Text.Json.JsonValueKind.Object && args.TryGetProperty("task", out var etask) && etask.ValueKind == System.Text.Json.JsonValueKind.String:
                return model.PetTeamEnqueue(etask.GetString() ?? "",
                    args.TryGetProperty("profile", out var ep) && ep.ValueKind == System.Text.Json.JsonValueKind.String ? ep.GetString() : null,
                    args.TryGetProperty("root", out var er) && er.ValueKind == System.Text.Json.JsonValueKind.String ? er.GetString() : null);
            case "team.run" when args.ValueKind == System.Text.Json.JsonValueKind.Object && args.TryGetProperty("task", out var rtask) && rtask.ValueKind == System.Text.Json.JsonValueKind.String:
                return model.PetTeamRun(rtask.GetString() ?? "", args.TryGetProperty("root", out var rr) && rr.ValueKind == System.Text.Json.JsonValueKind.String ? rr.GetString() : null,
                    args.TryGetProperty("preset", out var rp2) && rp2.ValueKind == System.Text.Json.JsonValueKind.String ? rp2.GetString() : null,
                    args.TryGetProperty("effort", out var re) && re.ValueKind == System.Text.Json.JsonValueKind.String ? re.GetString() : null,
                    !(args.TryGetProperty("review_plan", out var rv) && rv.ValueKind == System.Text.Json.JsonValueKind.False),
                    args.TryGetProperty("attachments", out var rat) && rat.ValueKind == System.Text.Json.JsonValueKind.Array ? rat.EnumerateArray().Where(x => x.ValueKind == System.Text.Json.JsonValueKind.String).Select(x => x.GetString() ?? "").Take(6).ToList() : null);
            case "team.cancel":
                if (model.Hub.Team.Service.Current is null) return (false, "no hay ningún trabajo en curso");
                model.Hub.Team.Service.Cancel();
                return (true, "trabajo cancelado");
            case "team.approve" or "team.reject":
            {
                var svc = model.Hub.Team.Service;
                if (svc.Pending is null) return (false, "no hay ningún plan esperando");
                if (tool == "team.reject") svc.Reject();
                else svc.Approve(args.ValueKind == System.Text.Json.JsonValueKind.Object && args.TryGetProperty("text", out var tx) && tx.ValueKind == System.Text.Json.JsonValueKind.String ? tx.GetString() : null);
                return (true, tool == "team.reject" ? "plan rechazado" : "plan aprobado");
            }
            case "team.remove" when args.ValueKind == System.Text.Json.JsonValueKind.Object && args.TryGetProperty("index", out var qi) && qi.TryGetInt32(out var qn):
            {
                var queued = model.Hub.Team.Service.Queued;
                return qn >= 0 && qn < queued.Count && model.Hub.Team.Service.Remove(queued[qn]) ? (true, "quitado de la cola") : (false, "ya no está en la cola");
            }
            case "agent.decide" when args.ValueKind == System.Text.Json.JsonValueKind.Object && args.TryGetProperty("id", out var did) && args.TryGetProperty("verdict", out var dv):
            {
                string? note = args.TryGetProperty("message", out var dm) && dm.ValueKind == System.Text.Json.JsonValueKind.String ? dm.GetString() : null;
                Dictionary<string, string>? answers = null;
                if (args.TryGetProperty("answers", out var an) && an.ValueKind == System.Text.Json.JsonValueKind.Object)
                    answers = an.EnumerateObject().Where(p => p.Value.ValueKind == System.Text.Json.JsonValueKind.String).ToDictionary(p => p.Name, p => p.Value.GetString()!);
                return model.PetDecide(did.GetString() ?? "", dv.GetString() == "allow" ? "allow" : "deny", note, answers);
            }
            default:
                return (false, "herramienta desconocida");
        }
    }

    /// Un evento de un agente, ya en el hilo de la interfaz: pide permiso (tarjeta, aviso y la mascota se alerta) o terminó (la mascota salta).
    private void OnAgentEvent(HookRequest request)
    {
        if (request.NeedsAnswer)
        {
            model.ShowApproval(request);
            pet?.ReactToAgent(PetReaction.Sparkle);
            if (prefs.AgentSounds && model.NotifyAllows(AgentNotifyPolicy.KindOf(request.Kind))) AgentSoundPlayer.Play(AgentSound.Approval);
        }
        else if (request.Event == "Stop")
        {
            pet?.ReactToAgent(PetReaction.Jump);
            model.NoteGoodThing(BondEvent.AgentDone);
            if (prefs.AgentSounds && model.NotifyAllows(AgentNotifyKind.Finished)) AgentSoundPlayer.Play(AgentSound.Finished);
        }
    }

    /// «Aprobar permisos desde el notch»: con el interruptor encendido se arranca el pipe y se instalan los hooks de Claude Code; apagado, se quitan.
    /// Un settings.json que no se entiende no se toca: se deja el interruptor sin efecto y se dice por qué.
    private void ApplyAgentApprovals()
    {
        if (hookServer is not null) hookServer.Wait = AgentHooks.DecisionWait(prefs.StoredList(PrefKeys.AgentDecisionSeconds)?.FirstOrDefault());
        var on = prefs.AgentApprovals;
        if (on == (hookServer is not null)) return;
        try
        {
            if (on)
            {
                hookServer = new HookServer(AgentCenter.Rules, log: AgentCenter.Log, wait: AgentHooks.DecisionWait(prefs.StoredList(PrefKeys.AgentDecisionSeconds)?.FirstOrDefault()));
                hookServer.Decided += model.RefreshAgentCenter;
                hookServer.Requested += request => Dispatcher.BeginInvoke(() => OnAgentEvent(request));
                hookServer.Start();
                AgentHooksInstaller.Install(AgentHooksInstaller.DefaultSettings, Environment.ProcessPath ?? "");
                model.SetAgentApprovalsStatus(tr("Hooks instalados en ~/.claude/settings.json."));
            }
            else
            {
                hookServer?.Dispose();
                hookServer = null;
                AgentHooksInstaller.Uninstall(AgentHooksInstaller.DefaultSettings);
                model.SetAgentApprovalsStatus(null);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            hookServer?.Dispose();
            hookServer = null;
            model.SetAgentApprovalsStatus(tr("No se pudo tocar ~/.claude/settings.json: %@", ex.Message));
            // Deja el interruptor como estaba de verdad: sin hooks instalados, apagado.
            Dispatcher.BeginInvoke(() => { if (prefs.AgentApprovals && hookServer is null) prefs.SetSwitch(PrefKeys.AgentApprovals, false); });
        }
    }

    /// El Explorador crea la entrada del ícono unos instantes después de verlo la primera vez: se reintenta un rato. Solo se sube el ícono (nunca se
    /// baja por su cuenta): si el usuario lo apagó, la preferencia lo recuerda y no se toca.
    private async Task PinTrayIconAsync()
    {
        for (int attempt = 0; attempt < 20 && prefs.TrayPinned; attempt++)
        {
            if (TrayPin.Available)
            {
                if (!TrayPin.Pinned) TrayPin.SetPinned(true);
                return;
            }
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        captureGuard?.Dispose();
        hookServer?.Dispose();
        supervisor?.Stop();
        notch?.Stop();
        model?.Hub.Dispose();
        model?.Camera.Dispose();
        model?.SetPanelVisible(false);
        HotKey.Power.Unregister();
        HotKey.Sleep.Unregister();
        HotKey.Clean.Unregister();
        HotKey.Ocr.Unregister();
        ollama?.Stats.Flush();
        pet?.Dispose();
        tray?.Dispose();
        animator?.Dispose();
        base.OnExit(e);
    }

    private void ApplyHotKeys(bool on)
    {
        var failed = new List<string>();
        foreach (var (key, wanted) in new[] { (HotKey.Power, on), (HotKey.Sleep, prefs.SleepHotKeyEnabled), (HotKey.Clean, prefs.Switch(PrefKeys.CleanHotKey)), (HotKey.Ocr, prefs.Switch(PrefKeys.OcrEnabled, false)) })
        {
            if (!wanted) key.Unregister();
            else if (!key.Register()) failed.Add(key.Display);
        }
        prefs.HotKeyError = failed.Count == 0 ? null
            : tr("No se pudo activar el atajo %@. Puede que otra app ya lo use.", string.Join(", ", failed));
    }

    private async Task ReadScreenOcr()
    {
        try
        {
            var text = await OcrRegionWindow.ReadScreenSelection();
            if (text is null) return;
            if (text.Length > 0) Clipboard.SetText(text);
            else tray.Notify(tr("OCR"), tr("No se encontró texto en la selección."));
        }
        catch (Exception e) { AppLog.Write($"OCR: {e.Message}"); tray.Notify(tr("OCR"), tr("No se pudo leer la selección: %@", e.Message)); }
    }

    /// El panel es siempre vidrio negro; lo que cambia con el sistema es el ícono de
    /// la bandeja (barra de tareas clara u oscura, DPI).
    private void OnAppearanceChanged()
    {
        animator.Reset();
        Repaint();
    }

    // MARK: - ícono

    /// El estado de Ollama en el ícono: el radar mientras arranca o se apaga, la
    /// insignia azul encendido y el ojo abierto con un modelo en memoria. El tooltip
    /// lleva siempre el % de RAM; con «Ícono con el % de RAM» el ícono también.
    private void Repaint()
    {
        string game = supervisor.Game.Game ?? "";
        var status = ollama.Power switch
        {
            Power.On => supervisor.Game.Active ? tr("Jugando a %@ · Ollama encendido", game) : tr("Ollama encendido"),
            Power.Off => supervisor.Game.Active ? tr("Jugando a %@ · Ollama apagado", game) : tr("Ollama apagado"),
            Power.Missing => tr("Ollama no está instalado"),
            Power.Starting => tr("Ollama arrancando…"),
            _ => tr("Ollama apagándose…"),
        };
        var phase = ollama.Power switch
        {
            Power.Starting => TrayPhase.Starting,
            Power.On => TrayPhase.On,
            Power.Stopping => TrayPhase.Stopping,
            _ => TrayPhase.Off,
        };
        var mem = ollama.Memory;
        int percent = mem.Total > 0 ? (int)Math.Round(100.0 * mem.Used / mem.Total) : 0;
        // Con un modelo en memoria el ojo de la mira se abre.
        bool awake = ollama.Power == Power.On && ollama.Loaded.Count > 0;
        bool animate = !prefs.ReducedMotion;
        animator.Show(phase, awake, $"{AppInfo.Name} · {status} · RAM {percent} %",
            prefs.Switch(PrefKeys.TrayPercent, false), percent, mem.Pressure, animate);
    }

    // MARK: - panel

    public void ShowPanel(PanelView? view = null)
    {
        ProcessMonitor.GetForegroundPid();
        _ = ollama.Refresh();
        if (view is { } v) model.Show(v);
        if (view is { } w && model.OpensInWindow(w)) return;   // se abrió en su propia ventana
        if (notch is not null) notch.Reveal();
        else EnsurePanel().ShowAt(tray.Rect());
    }

    private ContentView CreateContent() => new(model);
    private PanelWindow EnsurePanel() => panel ??= new PanelWindow(CreateContent);

    /// El notch (lo principal) comparte la vista del panel: solo una ventana la aloja a la vez.
    private void ApplyNotchDock()
    {
        bool want = !prefs.Switch(PrefKeys.LegacyPanel, false);
        model.WindowHost = want;
        if (want && notch is not null) { notch.Reconcile(); return; }   // cambió algo de Ajustes (pantallas): el anfitrión lo comprueba
        if (want == (notch is not null)) return;
        if (want)
        {
            panel?.HidePanel();
            notch = new NotchHost(model, prefs, CreateContent);
            notch.Start();
        }
        else
        {
            notch!.Stop();
            notch = null;
        }
    }

    private void TogglePanel()
    {
        if (notch is not null) { notch.Toggle(); return; }
        if (panel?.IsVisible == true) { panel.HidePanel(); return; }
        // El clic en el ícono ya cerró el panel al quitarle el foco: no se reabre.
        if (panel is { } existing && (DateTime.Now - existing.LastHidden).TotalMilliseconds < 350) return;
        ShowPanel();
    }

    // MARK: - menú del clic derecho

    private void ShowMenu()
    {
        panel?.HidePanel();
        notch?.Windows.ToList().ForEach(w => w.Collapse());
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };

        if (ollama.Power == Power.Missing)
        {
            Add(menu, tr("Descargar Ollama…"), ollama.Toggle);
        }
        else
        {
            var power = Add(menu, ollama.Power == Power.On ? tr("Apagar Ollama") : tr("Encender Ollama"), ollama.Toggle);
            power.IsEnabled = !ollama.Power.IsTransition();
            if (prefs.HotKeyEnabled) power.InputGestureText = HotKey.Power.Display;
            var sleep = Add(menu, tr("Dormir el modelo"), supervisor.Sleep);
            sleep.IsEnabled = ollama.Loaded.Count > 0;
            if (prefs.SleepHotKeyEnabled) sleep.InputGestureText = HotKey.Sleep.Display;
            menu.Items.Add(MainModelMenu());
        }
        var clean = Add(menu, tr("Liberar RAM"), () => _ = supervisor.Clean(CleanReason.Manual));
        clean.IsEnabled = supervisor.Agent == AgentStatus.Ready && !supervisor.Cleaning;
        if (prefs.Switch(PrefKeys.CleanHotKey)) clean.InputGestureText = HotKey.Clean.Display;
        var percent = Add(menu, tr("Mostrar el % de RAM en el ícono"), () =>
        {
            prefs.Toggle(PrefKeys.TrayPercent, false);
            Repaint();
        });
        percent.IsChecked = prefs.Switch(PrefKeys.TrayPercent, false);
        // Solo en Windows 11 con la entrada del ícono ya creada por el Explorador.
        if (TrayPin.Available)
        {
            var pinned = Add(menu, tr("Mostrar el ícono en la barra"), () => prefs.SetTrayPinned(!TrayPin.Pinned));
            pinned.IsChecked = TrayPin.Pinned;
        }
        var still = Add(menu, tr("Movimiento reducido"), prefs.ToggleReducedMotion);   // Prefs.Changed repinta
        still.IsChecked = prefs.ReducedMotion;
        // Prefs.Changed ya llama a ApplyNotchDock: cambia entre notch y panel de la bandeja al instante.
        var legacy = Add(menu, tr("Versión legacy (panel de la bandeja)"), () => prefs.Toggle(PrefKeys.LegacyPanel, false));
        legacy.IsChecked = prefs.Switch(PrefKeys.LegacyPanel, false);
        menu.Items.Add(PetMenu());
        Add(menu, tr("Actividad"), () => ShowPanel(PanelView.Activity));

        menu.Items.Add(new Separator());
        var logs = Add(menu, tr("Ver el log de Ollama"), OpenLog);
        logs.IsEnabled = ollama.Backend.LogPath is { } log && File.Exists(log);
        var login = Add(menu, tr("Abrir al iniciar sesión"), () =>
        {
            try { prefs.ToggleLogin(); } catch (Exception e) { ollama.Error = tr("Inicio de sesión: %@", e.Message); }
        });
        login.IsChecked = prefs.OpensAtLogin;

        menu.Items.Add(new Separator());
        Add(menu, tr("Acerca de %@", AppInfo.Name), AboutWindow.Present);
        var checkUpdates = Add(menu, tr("Buscar actualizaciones ahora"), () =>
        {
            ShowPanel(PanelView.Settings);
            _ = supervisor.CheckUpdates(manual: true);
        });
        checkUpdates.IsEnabled = supervisor.UpdateStatus != UpdateStatus.Checking && !supervisor.UpdateBusy;
        menu.Items.Add(new Separator());
        Add(menu, tr("Salir"), Shutdown);

        menu.Opened += (_, _) =>
        {
            // Sin esto el menú no se cierra al hacer clic fuera: la bandeja no es una ventana de la app.
            if (PresentationSource.FromVisual(menu) is HwndSource source) Win32.SetForegroundWindow(source.Handle);
        };
        menu.IsOpen = true;
    }

    /// «Mascota junto a Inicio»: mostrarla, dónde vive, si se puede tocar y [cuál].
    private MenuItem PetMenu()
    {
        var root = new MenuItem { Header = tr("Mascota junto a Inicio") };
        var show = new MenuItem { Header = tr("Mostrar"), IsChecked = prefs.Switch(PrefKeys.TaskbarPet, false) };
        show.Click += (_, _) => prefs.Toggle(PrefKeys.TaskbarPet, false);
        root.Items.Add(show);
        root.Items.Add(new Separator());
        root.Items.Add(TaskbarPet.PlacementMenu(prefs));
        if (TaskbarPet.SpeciesMenu(prefs) is { } pets) root.Items.Add(pets);
        var touch = new MenuItem { Header = tr("Interactuar con la mascota"), IsChecked = prefs.Switch(PrefKeys.TaskbarPetInteractive) };
        touch.Click += (_, _) => prefs.Toggle(PrefKeys.TaskbarPetInteractive);
        root.Items.Add(touch);
        return root;
    }

    /// «Cargar al encender»: el modelo principal (el de la estrella del panel), o ninguno.
    /// Con Ollama apagado, los modelos salen de sus manifiestos en disco.
    private MenuItem MainModelMenu()
    {
        var main = prefs.MainModel;
        var root = new MenuItem { Header = tr("Cargar al encender") };
        void Option(string title, string? model)
        {
            var item = new MenuItem { Header = title, IsChecked = model == main };
            item.Click += (_, _) => prefs.SetMainModel(model);
            root.Items.Add(item);
        }
        Option(tr("Ninguno"), null);
        var names = ollama.Installed.Count > 0 ? ollama.Installed.Select(m => m.Name).ToList() : OllamaBlobs.Installed();
        if (main is not null && !names.Contains(main)) names.Insert(0, main);
        root.Items.Add(new Separator());
        foreach (var name in names) Option(name, name);
        if (names.Count == 0)
            root.Items.Add(new MenuItem { Header = tr("No hay modelos instalados"), IsEnabled = false });
        return root;
    }

    public void ShowUpdateRecovery(string message)
    {
        supervisor.ReportUpdateRecovery(message);
        ShowPanel(PanelView.Settings);
    }

    private static MenuItem Add(ContextMenu menu, string title, Action action)
    {
        var item = new MenuItem { Header = title };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
        return item;
    }

    private void OpenLog() => Paths.OpenLog(ollama.Backend.LogPath);
}
