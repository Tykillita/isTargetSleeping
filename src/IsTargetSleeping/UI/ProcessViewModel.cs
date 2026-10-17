using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

public enum ProcessFilter { All, Windows, Background, System, Protected }
public enum ProcessSort { Ram, Cpu, Name, Count, Pid }

/// A stable row survives new samples and sorting, so selection never moves to a reused PID.
public sealed class ProcessRow : INotifyPropertyChanged
{
    public required string Key { get; init; }
    public required string GroupId { get; init; }
    public ProcessGroup? Group { get; private set; }
    public ProcessSnapshot? Process { get; private set; }
    public bool IsGroup => Process == null;
    public bool Expanded { get; private set; }
    /// Chevrón Fluent: a la derecha plegado, hacia abajo desplegado.
    public string Expansion => IsGroup ? Expanded ? "" : "" : "";
    public string Name => Group?.Name ?? Process!.Name;
    /// Junto al nombre: cuántos procesos tiene la aplicación, o el PID del proceso (los hijos se llaman igual que su padre).
    public string Tag => IsGroup ? Group!.Count > 1 ? "×" + Group.Count : "" : "PID " + Process!.Pid.ToString(CultureInfo.InvariantCulture);
    public string Executable => Group?.Executable ?? Process!.Executable;
    public string? Path => Group?.Path ?? Process?.Path;
    public string User => Group?.User ?? Process?.User ?? tr("No disponible");
    public string Count => IsGroup ? Group!.Count.ToString(CultureInfo.InvariantCulture) : "";
    public string Pid => Process?.Pid.ToString(CultureInfo.InvariantCulture) ?? "";
    public long? Ram { get; private set; }
    public double RamFraction { get; private set; }
    public string RamText => ProcessViewModel.Memory(Ram);
    public string RamPercent { get; private set; } = "";
    public double? Cpu => Group?.CpuPercent ?? Process?.CpuPercent;
    public string CpuText => Cpu.HasValue ? Cpu.Value.ToString("0.0", CultureInfo.InvariantCulture) + " %" : tr("No disponible");
    public string Status { get; private set; } = "";
    public Thickness NameInset => IsGroup ? new Thickness(0) : new Thickness(16, 0, 0, 0);
    public FontWeight Weight => IsGroup ? FontWeights.SemiBold : FontWeights.Normal;
    public ImageSource? Icon { get; private set; }
    public bool CanAct => (Group?.Processes ?? (Process != null ? [Process] : [])) is { Count: > 0 } processes
        && processes.All(p => p.Identity.IsValid && p.Pid != 4 && p.Pid != Environment.ProcessId
            && p.IsCritical != true && !OwnExecutable(p.Executable));
    public IReadOnlyList<ProcessIdentity> Identities => Group?.Processes.Select(p => p.Identity).ToArray()
        ?? (Process != null ? [Process.Identity] : []);
    public event PropertyChangedEventHandler? PropertyChanged;

    private static bool OwnExecutable(string exe) => exe.Equals("isTargetSleeping.exe", StringComparison.OrdinalIgnoreCase)
        || exe.Equals("isTargetSleeping.MemoryAgent.exe", StringComparison.OrdinalIgnoreCase);

    internal void Update(ProcessGroup? group, ProcessSnapshot? process, ProcessSample sample, bool expanded, bool reappeared, ImageSource? icon)
    {
        Group = group;
        Process = process;
        Expanded = expanded;
        Ram = group?.RamBytes ?? process?.RamBytes(sample.UsesPrivateWorkingSet);
        RamFraction = Ram.HasValue && sample.TotalRamBytes > 0 ? Math.Clamp((double)Ram.Value / sample.TotalRamBytes, 0, 1) : 0;
        RamPercent = Ram.HasValue && sample.TotalRamBytes > 0
            ? (RamFraction * 100).ToString("0.0", CultureInfo.InvariantCulture) + " %" : tr("No disponible");
        bool protectedForCleaning = group?.IsProtected ?? process?.ProtectionReason != null;
        Status = reappeared ? tr("Reaparecido") : !CanAct ? tr("Finalización bloqueada")
            : protectedForCleaning ? tr("Protegido para limpieza") : (group?.HasWindow ?? process?.HasWindow == true)
                ? tr("Con ventana") : tr("Segundo plano");
        Icon = icon;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}

/// Sampling owns no window handles. The timer runs only while its surface is visible.
public sealed class ProcessViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly Func<CancellationToken, Task<ProcessSample>> capture;
    private readonly bool persistSettings;
    private readonly DispatcherTimer timer;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<string, ProcessRow> knownRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ImageSource?> icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> expanded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateTimeOffset At, HashSet<ProcessIdentity> Before)> endedGroups = new(StringComparer.OrdinalIgnoreCase);
    private bool disposed;
    private bool active;
    private bool paused;
    private bool refreshing;
    private string search = "";
    private ProcessFilter filter;
    private int minimumMiB;
    private ProcessSort sort;
    private bool descending = true;
    private ProcessRow? selected;
    private string? error;

    private readonly Func<ProcessActionRequest, bool, Task<ProcessActionResult>>? execute;
    private readonly Func<ProcessActionRequest, Task<ProcessActionPreview>> previewAction;
    private readonly Func<Task<string?>>? installAgent;
    private readonly Func<AgentStatus>? agentStatus;
    private TaskCompletionSource<bool>? asking;
    private bool operating;
    private string resultText = "";
    private Color resultColor;

    public ProcessViewModel(Func<CancellationToken, Task<ProcessSample>>? capture = null,
        Func<IReadOnlySet<int>>? protectedPids = null, bool persistSettings = true,
        Func<ProcessActionRequest, bool, Task<ProcessActionResult>>? execute = null,
        Func<ProcessActionRequest, Task<ProcessActionPreview>>? preview = null,
        Func<Task<string?>>? installAgent = null, Func<AgentStatus>? agentStatus = null)
    {
        this.execute = execute;
        this.installAgent = installAgent;
        this.agentStatus = agentStatus;
        previewAction = preview ?? (request => Task.Run(() => ProcessActions.Preview(request)));
        Rows.CollectionChanged += (_, _) => { Changed(nameof(ShowEmpty)); Changed(nameof(EmptyText)); };
        RefreshCommand = new Command(() => _ = RefreshAsync());
        TogglePause = new Command(() => Paused = !Paused);
        CycleMinimum = new Command(() => MinimumMiB = MinimumChoices[(Array.IndexOf(MinimumChoices, MinimumMiB) + 1) % MinimumChoices.Length]);
        ToggleRow = new ParamCommand(p => { if (p is ProcessRow row) Toggle(row); });
        ActApplication = new Command(() => _ = Act(ProcessActionMode.Application));
        ActProcess = new Command(() => _ = Act(ProcessActionMode.Single));
        ActTree = new Command(() => _ = Act(ProcessActionMode.Tree));
        Confirm = new Command(() => asking?.TrySetResult(true));
        Cancel = new Command(() => asking?.TrySetResult(false));
        if (capture == null)
        {
            var monitor = new ProcessMonitor();
            this.capture = ct => monitor.CaptureAsync(protectedPids?.Invoke(), cancellationToken: ct);
        }
        else this.capture = capture;
        this.persistSettings = persistSettings;
        if (persistSettings)
        {
            if (Enum.TryParse<ProcessSort>(Defaults.GetString("processSort"), out var saved)) sort = saved;
            descending = Defaults.GetBool("processSortDescending", sort != ProcessSort.Name && sort != ProcessSort.Pid);
        }
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += Tick;
    }

    public ObservableCollection<ProcessRow> Rows { get; } = [];
    public ProcessSample? Snapshot { get; private set; }
    public IReadOnlyList<ProcessGroup> Groups { get; private set; } = [];
    public string RamHeading => Snapshot?.UsesPrivateWorkingSet == true ? tr("RAM privada residente") : tr("RAM residente total");
    public string RamExplanation => Snapshot?.UsesPrivateWorkingSet == true
        ? tr("RAM privada residente. Las sumas pueden diferir de la RAM global por memoria compartida y consumo del sistema.")
        : tr("RAM residente total en esta versión de Windows. Las sumas pueden diferir de la RAM global por memoria compartida y consumo del sistema.");
    public bool IsRefreshing => refreshing;
    public bool IsSampling => timer.IsEnabled;
    public string? Error => error;
    public string Summary => error ?? (Snapshot == null ? tr("Leyendo procesos…")
        : tr("%@ aplicaciones · %@ procesos · actualizado %@", Groups.Count, Snapshot.Processes.Count,
            Snapshot.CapturedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)));
    public bool Paused { get => paused; set { if (paused == value) return; paused = value; UpdateTimer(); Changed(nameof(Paused)); Changed(nameof(PauseGlyph)); Changed(nameof(PauseLabel)); } }
    public string Search { get => search; set { if (search == value) return; search = value; Rebuild(); Changed(nameof(Search)); } }
    public ProcessFilter Filter { get => filter; set { if (filter == value) return; filter = value; Rebuild(); Changed(nameof(Filter)); Changed(nameof(FilterSegments)); } }
    public int MinimumMiB { get => minimumMiB; set { if (minimumMiB == value) return; minimumMiB = value; Rebuild(); Changed(nameof(MinimumMiB)); Changed(nameof(MinimumLabel)); } }
    public ProcessSort Sort => sort;
    public bool Descending => descending;
    public ProcessRow? Selected
    {
        get => selected;
        set
        {
            if (ReferenceEquals(selected, value)) return;
            selected = value;
            Changed(nameof(Selected));
            Changed(nameof(Details));
            ActionsChanged();
        }
    }
    private void ActionsChanged()
    {
        Changed(nameof(CanApplication));
        Changed(nameof(CanProcess));
        Changed(nameof(SelectedIsGroup));
        Changed(nameof(SelectedIsProcess));
    }

    // MARK: lo que se ve en el notch (la tabla de antes, en una ventana, ahora vive en la pestaña Procesos de Actividad)

    public static readonly int[] MinimumChoices = [0, 100, 500, 1024];
    public string PauseGlyph => paused ? "\uE768" : "\uE769";
    public string PauseLabel => paused ? tr("Reanudar") : tr("Pausar");
    public string RefreshLabel => tr("Actualizar");
    public string CancelLabel => tr("Cancelar");
    public string ConfirmLabel => Confirmation?.Affirmative ?? tr("Finalizar");
    public string SearchLabel => tr("Buscar procesos");
    public string SearchHint => tr("Buscar por nombre, ejecutable, ruta o PID");
    public string ListLabel => tr("Aplicaciones y procesos activos");
    public string ToggleLabel => tr("Desplegar o contraer los procesos de la aplicación");
    public string FilterLabel => tr("Tipo");
    public string SortLabel => tr("Ordenar por");
    public string MinimumLabel => tr("RAM mínima") + ": " + (minimumMiB == 0 ? tr("Sin mínimo")
        : minimumMiB >= 1024 ? "≥ 1 GiB" : $"≥ {minimumMiB} MiB");
    public bool ShowEmpty => Rows.Count == 0;
    public string EmptyText => Snapshot == null ? error ?? tr("Leyendo procesos…")
        : tr("No hay procesos con estos filtros. Cambia la búsqueda o el consumo mínimo.");
    public string ApplicationLabel => tr("Finalizar aplicación");
    public string ProcessLabel => tr("Finalizar proceso");
    public string TreeLabel => tr("Finalizar árbol");
    public string ApplicationHelp => tr("Todos los procesos de la aplicación y sus descendientes");
    public string ProcessHelp => tr("Solo el proceso seleccionado");
    public string TreeHelp => tr("El proceso seleccionado y sus descendientes");
    public bool SelectedIsGroup => selected?.IsGroup == true;
    public bool SelectedIsProcess => selected is { IsGroup: false };
    public bool CanApplication => !operating && selected?.CanAct == true && selected.IsGroup;
    public bool CanProcess => !operating && selected?.CanAct == true && !selected.IsGroup;
    public bool CanRefresh => !operating && !refreshing;
    public ICommand RefreshCommand { get; }
    public ICommand TogglePause { get; }
    public ICommand CycleMinimum { get; }
    public ICommand ToggleRow { get; }
    public ICommand ActApplication { get; }
    public ICommand ActProcess { get; }
    public ICommand ActTree { get; }
    public ICommand Confirm { get; }
    public ICommand Cancel { get; }

    public List<Segment> FilterSegments => new[]
    {
        (ProcessFilter.All, tr("Todos")), (ProcessFilter.Windows, tr("Ventana")), (ProcessFilter.Background, tr("Fondo")),
        (ProcessFilter.System, tr("Sistema")), (ProcessFilter.Protected, tr("Protegidos")),
    }.Select(f => new Segment(f.Item2, filter == f.Item1, new Command(() => Filter = f.Item1))).ToList();

    /// Pulsar el criterio elegido invierte el orden; la flecha dice cuál es.
    public List<Segment> SortSegments => new[]
    {
        (ProcessSort.Ram, tr("RAM")), (ProcessSort.Cpu, tr("CPU")), (ProcessSort.Name, tr("Nombre")),
        (ProcessSort.Count, tr("Cantidad")), (ProcessSort.Pid, tr("PID")),
    }.Select(c => new Segment(sort == c.Item1 ? c.Item2 + (descending ? " ↓" : " ↑") : c.Item2, sort == c.Item1,
        new Command(() => SetSort(c.Item1)))).ToList();

    /// La confirmación sale dentro del notch, con la lista exacta de lo que se cerrará; hasta aceptarla no se finaliza nada.
    public ProcessConfirm? Confirmation { get; private set; }
    public bool HasConfirmation => Confirmation != null;
    public bool HasResult => resultText.Length > 0;
    public string ResultText => resultText;
    public Brush ResultBrush => Theme.Brush(resultColor);
    public bool Operating => operating;
    public string Details
    {
        get
        {
            if (selected == null) return tr("Selecciona una aplicación o despliega sus procesos para ver los detalles.");
            if (selected.Process is { } p)
                return tr("PID %@ · Padre %@ · Usuario %@ · Sesión %@ · Memoria comprometida %@", p.Pid,
                    p.ParentPid?.ToString(CultureInfo.InvariantCulture) ?? tr("No disponible"), selected.User,
                    p.SessionId?.ToString(CultureInfo.InvariantCulture) ?? tr("No disponible"), Memory(p.CommitBytes))
                    + Environment.NewLine + (p.Path ?? tr("Ruta no disponible"))
                    + (p.ProtectionReason != null ? Environment.NewLine + tr("Protegido para limpieza: %@", Protection(p.ProtectionReason)) : "");
            var g = selected.Group!;
            long? committed = g.Processes.All(p => p.CommitBytes.HasValue) ? g.Processes.Sum(p => p.CommitBytes!.Value) : null;
            return tr("%@ procesos · Usuario %@ · Sesión %@ · Memoria comprometida %@", g.Count, selected.User,
                g.SessionId?.ToString(CultureInfo.InvariantCulture) ?? tr("No disponible"), Memory(committed))
                + Environment.NewLine + (g.Path ?? tr("Ruta no disponible"));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private async void Tick(object? sender, EventArgs e) => await RefreshAsync();

    public void SetSamplingActive(bool value)
    {
        if (disposed || active == value) return;
        active = value;
        UpdateTimer();
        if (active && !paused) _ = RefreshAsync();
    }

    private void UpdateTimer()
    {
        if (active && !paused && !disposed) timer.Start(); else timer.Stop();
        Changed(nameof(IsSampling));
    }

    public async Task RefreshAsync()
    {
        if (disposed || refreshing) return;
        refreshing = true;
        Changed(nameof(IsRefreshing));
        Changed(nameof(CanRefresh));
        try
        {
            var sample = await capture(lifetime.Token);
            var preparedIcons = await AppIcons.LoadAsync(sample.Processes.Select(p => p.Path));
            if (!disposed)
            {
                foreach (var (path, icon) in preparedIcons)
                    if (!icons.ContainsKey(path)) icons[path] = icon;
                Apply(sample);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!disposed)
            {
                error = tr("No se pudieron leer los procesos: %@", ex.Message);
                Changed(nameof(Error));
                Changed(nameof(Summary));
            }
        }
        finally { refreshing = false; Changed(nameof(IsRefreshing)); Changed(nameof(CanRefresh)); }
    }

    public void Apply(ProcessSample sample)
    {
        if (disposed) return;
        Snapshot = sample;
        Groups = sample.Groups;
        var livePaths = sample.Processes.Select(p => p.Path).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stalePath in icons.Keys.Where(path => !livePaths.Contains(path)).ToArray()) icons.Remove(stalePath);
        error = null;
        foreach (var id in endedGroups.Where(p => sample.CapturedAt - p.Value.At > TimeSpan.FromMinutes(2)).Select(p => p.Key).ToArray())
            endedGroups.Remove(id);
        Rebuild();
        Changed(nameof(Snapshot));
        Changed(nameof(Groups));
        Changed(nameof(Summary));
        Changed(nameof(RamHeading));
        Changed(nameof(RamExplanation));
        Changed(nameof(Error));
        Changed(nameof(EmptyText));
    }

    public void Toggle(ProcessRow row)
    {
        if (!row.IsGroup) return;
        if (!expanded.Add(row.GroupId)) expanded.Remove(row.GroupId);
        Rebuild();
    }

    public void SetSort(ProcessSort value, bool? sortDescending = null)
    {
        descending = sortDescending ?? (sort == value ? !descending : value != ProcessSort.Name && value != ProcessSort.Pid);
        sort = value;
        if (persistSettings)
        {
            Defaults.Set("processSort", value.ToString());
            Defaults.Set("processSortDescending", descending);
        }
        Rebuild();
        Changed(nameof(Sort));
        Changed(nameof(Descending));
        Changed(nameof(SortSegments));
    }

    private void SetOperating(bool value)
    {
        operating = value;
        Changed(nameof(Operating));
        Changed(nameof(CanRefresh));
        ActionsChanged();
    }

    private void ShowResult(string text, Color? color = null)
    {
        resultText = text;
        resultColor = color ?? Theme.Current.Secondary;
        Changed(nameof(ResultText));
        Changed(nameof(ResultBrush));
        Changed(nameof(HasResult));
    }

    private async Task<bool> Ask(ProcessConfirm card)
    {
        var source = asking = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Confirmation = card;
        Changed(nameof(Confirmation));
        Changed(nameof(HasConfirmation));
        try { return await source.Task; }
        finally
        {
            asking = null;
            Confirmation = null;
            if (!disposed) { Changed(nameof(Confirmation)); Changed(nameof(HasConfirmation)); }
        }
    }

    private async Task Act(ProcessActionMode mode)
    {
        if (operating || execute == null || Request(mode) is not { } request) return;
        SetOperating(true);
        try
        {
            var preview = await previewAction(request);
            if (disposed) return;
            if (preview.Processes.Count == 0) { ShowResult(tr("Los procesos seleccionados ya no están activos.")); return; }
            ShowResult("");
            if (!await Ask(ProcessConfirmation.For(preview))) return;
            ShowResult(tr("Finalizando procesos…"));
            var result = await execute(request, false);
            if (disposed) return;
            RecordAction(result);
            var allItems = result.Items.ToList();
            if (result.RetryTargets.Count > 0)
            {
                bool ready = agentStatus?.Invoke() == AgentStatus.Ready;
                string button = ready ? tr("Reintentar como administrador") : tr("Instalar agente y reintentar");
                string message = ready
                    ? tr("Windows denegó el acceso a %@ procesos. Puedes reintentar los restantes con permisos de administrador.", result.RetryTargets.Count)
                    : tr("Windows denegó el acceso a %@ procesos. Instala o actualiza el agente para reintentar con permisos de administrador.", result.RetryTargets.Count);
                var retryIds = result.RetryTargets.ToHashSet();
                var retryProcesses = preview.Processes.Where(p => retryIds.Contains(p.Identity)).ToArray();
                bool canElevate = ready || installAgent != null;
                if (canElevate && await Ask(ProcessConfirmation.Retry(tr("Acceso denegado"), message, retryProcesses, preview.UsesPrivateWorkingSet, button)))
                {
                    if (!ready)
                    {
                        ShowResult(tr("Instalando agente…"));
                        string? installError = await installAgent!();
                        if (disposed) return;
                        if (installError != null) { ShowResult(Describe(result) + Environment.NewLine + installError, Theme.Current.Danger); return; }
                    }
                    ShowResult(tr("Finalizando procesos como administrador…"));
                    var elevatedRequest = new ProcessActionRequest(Guid.NewGuid(), request.Mode, result.RetryTargets);
                    var elevated = await execute(elevatedRequest, true);
                    if (disposed) return;
                    RecordAction(elevated);
                    var replacements = elevated.Items.ToDictionary(i => i.Identity);
                    allItems = allItems.Select(i => replacements.GetValueOrDefault(i.Identity, i)).ToList();
                    foreach (var item in elevated.Items.Where(i => allItems.All(existing => existing.Identity != i.Identity))) allItems.Add(item);
                    var outcome = elevated.Outcome == ProcessActionOutcome.Cancelled ? ProcessActionOutcome.Cancelled
                        : allItems.All(i => i.Completed) ? ProcessActionOutcome.Success : allItems.Any(i => i.Completed)
                            ? ProcessActionOutcome.Partial : elevated.Outcome;
                    result = new ProcessActionResult(request.Id, outcome, result.StartedAt, elevated.FinishedAt, allItems);
                }
            }
            var t = Theme.Current;
            ShowResult(Describe(result), result.Outcome switch
            {
                ProcessActionOutcome.Success => t.Fg,
                ProcessActionOutcome.Partial or ProcessActionOutcome.Pending => t.Warm,
                ProcessActionOutcome.NoWork or ProcessActionOutcome.Cancelled => t.Secondary,
                _ => t.Danger,
            });
        }
        catch (Exception ex) { if (!disposed) ShowResult(tr("No se pudieron finalizar los procesos: %@", ex.Message), Theme.Current.Danger); }
        finally
        {
            SetOperating(false);
            if (!disposed) await RefreshAsync();
        }
    }

    public static string Describe(ProcessActionResult result)
    {
        string heading = result.Outcome switch
        {
            ProcessActionOutcome.Success => tr("Procesos finalizados"), ProcessActionOutcome.Partial => tr("Finalización parcial"),
            ProcessActionOutcome.NoWork => tr("Los procesos seleccionados ya no están activos."),
            ProcessActionOutcome.Pending => tr("La finalización sigue pendiente"), ProcessActionOutcome.Cancelled => tr("La solicitud de administrador se canceló"),
            _ => tr("No se pudieron finalizar los procesos"),
        };
        var remaining = result.Items.Where(i => !i.Completed).Select(i => tr("PID %@: %@", i.Identity.Pid, ReasonText(i.Reason, i.Outcome))
            + (i.Win32Error.HasValue ? tr(" (código %@)", i.Win32Error.Value) : ""));
        return heading + tr(" · %@ de %@ completados", result.Items.Count(i => i.Completed), result.Items.Count)
            + (result.Remaining.Count > 0 ? Environment.NewLine + string.Join(" · ", remaining) : "")
            + (result.Error != null ? Environment.NewLine + ReasonText(result.Error, ProcessTargetOutcome.Failed) : "");
    }
    private static string TargetText(ProcessTargetOutcome outcome) => outcome switch
    {
        ProcessTargetOutcome.Denied => tr("Acceso denegado"), ProcessTargetOutcome.Blocked => tr("Finalización bloqueada"),
        ProcessTargetOutcome.Changed => tr("El PID cambió de proceso"), ProcessTargetOutcome.Pending => tr("Pendiente"),
        _ => tr("Error"),
    };
    private static string ReasonText(string? reason, ProcessTargetOutcome outcome) => reason switch
    {
        null => TargetText(outcome), "TargetLimit" => tr("Se alcanzó el límite de procesos"),
        "CriticalOrUnknown" or "CriticalUnknown" => tr("No se pudo comprobar que el proceso no sea crítico"),
        "Critical" => tr("Proceso crítico de Windows"), "TerminateFailed" => tr("Windows no pudo finalizar el proceso"),
        "ExitNotConfirmed" => tr("Salida todavía no confirmada"), "Cancelled" => tr("Solicitud cancelada"),
        "Deadline" => tr("Se agotó el tiempo de espera"), "OwnOrSystemProcess" => tr("Proceso propio o del sistema"),
        "UnavailableIdentity" => tr("Identidad no disponible"), "OpenFailed" => outcome == ProcessTargetOutcome.Denied
            ? tr("Acceso denegado") : tr("No se pudo abrir el proceso"), "PidReused" => tr("El PID cambió de proceso"),
        "UnavailablePath" => tr("Ruta no disponible"), "OwnApplication" => tr("Proceso de isTargetSleeping"),
        _ => tr(reason),
    };

    public ProcessActionRequest? Request(ProcessActionMode mode)
    {
        if (selected?.CanAct != true || mode == ProcessActionMode.Application != selected.IsGroup) return null;
        return new ProcessActionRequest(Guid.NewGuid(), mode, selected.Identities);
    }

    public void RecordAction(ProcessActionResult result)
    {
        if (Snapshot == null) return;
        var terminated = result.Items.Where(p => p.Outcome == ProcessTargetOutcome.Terminated).Select(p => p.Identity).ToHashSet();
        foreach (var g in Groups.Where(g => g.Processes.Any(p => terminated.Contains(p.Identity))))
            endedGroups[g.Id] = (result.FinishedAt, g.Processes.Select(p => p.Identity).ToHashSet());
    }

    private void Rebuild()
    {
        if (Snapshot is not { } sample) return;
        var selectedKey = selected?.Key;
        var next = new List<ProcessRow>();
        IEnumerable<ProcessGroup> candidates = Groups.Where(Matches);
        var ordered = candidates.OrderBy(g => GroupSortValue(g), ValueComparer.Instance);
        if (descending) ordered = candidates.OrderByDescending(g => GroupSortValue(g), ValueComparer.Instance);
        foreach (var group in ordered.ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Id, StringComparer.OrdinalIgnoreCase))
        {
            bool isExpanded = expanded.Contains(group.Id);
            bool reappeared = endedGroups.TryGetValue(group.Id, out var ended) && group.Processes.Any(p => !ended.Before.Contains(p.Identity));
            Add("group:" + group.Id, group, null);
            if (isExpanded)
            {
                var children = group.Processes.OrderBy(p => ProcessSortValue(p, sample), ValueComparer.Instance);
                if (descending) children = group.Processes.OrderByDescending(p => ProcessSortValue(p, sample), ValueComparer.Instance);
                foreach (var p in children.ThenBy(p => p.Pid)) Add($"pid:{p.Pid}:{p.Identity.CreatedFileTime}", null, p);
            }
            void Add(string key, ProcessGroup? g, ProcessSnapshot? p)
            {
                if (!knownRows.TryGetValue(key, out var row)) knownRows[key] = row = new ProcessRow { Key = key, GroupId = group.Id };
                string? path = g?.Path ?? p?.Path;
                ImageSource? icon = null;
                if (path != null) icons.TryGetValue(path, out icon);
                row.Update(g, p, sample, isExpanded, reappeared, icon);
                next.Add(row);
            }
        }
        var wanted = next.Select(r => r.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = Rows.Count - 1; i >= 0; i--) if (!wanted.Contains(Rows[i].Key)) Rows.RemoveAt(i);
        for (int i = 0; i < next.Count; i++)
        {
            if (i < Rows.Count && ReferenceEquals(Rows[i], next[i])) continue;
            int existing = Rows.IndexOf(next[i]);
            if (existing >= 0) Rows.Move(existing, i); else Rows.Insert(i, next[i]);
        }
        var liveKeys = Groups.Select(g => "group:" + g.Id).Concat(sample.Processes.Select(p => $"pid:{p.Pid}:{p.Identity.CreatedFileTime}"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in knownRows.Keys.Where(k => !liveKeys.Contains(k)).ToArray()) knownRows.Remove(stale);
        Selected = selectedKey == null ? null : Rows.FirstOrDefault(r => r.Key == selectedKey);
        Changed(nameof(Details));
    }

    private bool Matches(ProcessGroup group)
    {
        if (MinimumMiB > 0 && (!group.RamBytes.HasValue || group.RamBytes < (long)MinimumMiB * 1024 * 1024)) return false;
        if (filter switch { ProcessFilter.Windows => !group.HasWindow, ProcessFilter.Background => group.HasWindow || group.IsSystem,
            ProcessFilter.System => !group.IsSystem, ProcessFilter.Protected => !group.IsProtected, _ => false }) return false;
        string term = search.Trim();
        return term.Length == 0 || group.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
            || group.Executable.Contains(term, StringComparison.OrdinalIgnoreCase) || (group.Path?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            || group.Processes.Any(p => p.Pid.ToString(CultureInfo.InvariantCulture).Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private object GroupSortValue(ProcessGroup g) => sort switch
    { ProcessSort.Cpu => g.CpuPercent ?? -1, ProcessSort.Name => g.Name, ProcessSort.Count => (double)g.Count,
        ProcessSort.Pid => (double)g.Processes.Min(p => p.Pid), _ => (double)(g.RamBytes ?? -1) };
    private object ProcessSortValue(ProcessSnapshot p, ProcessSample sample) => sort switch
    { ProcessSort.Cpu => p.CpuPercent ?? -1, ProcessSort.Name => p.Name, ProcessSort.Pid => (double)p.Pid,
        ProcessSort.Count => (double)p.Pid, _ => (double)(p.RamBytes(sample.UsesPrivateWorkingSet) ?? -1) };
    private sealed class ValueComparer : IComparer<object>
    {
        public static readonly ValueComparer Instance = new();
        public int Compare(object? x, object? y) => x is string a && y is string b
            ? StringComparer.OrdinalIgnoreCase.Compare(a, b) : Comparer<double>.Default.Compare((double)x!, (double)y!);
    }

    public static string Memory(long? bytes) => !bytes.HasValue ? tr("No disponible") : bytes.Value >= 1024L * 1024 * 1024
        ? (bytes.Value / (1024.0 * 1024 * 1024)).ToString("0.00", CultureInfo.InvariantCulture) + " GiB"
        : (bytes.Value / (1024.0 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " MiB";

    private static string Protection(string reason) => reason switch
    {
        "Critical" => tr("Proceso crítico de Windows"), "OwnApplication" => AppInfo.Name,
        "CriticalUnknown" => tr("No se pudo comprobar que el proceso no sea crítico"),
        "UnavailableIdentity" => tr("Identidad no disponible"), "Foreground" => tr("Aplicación en primer plano"),
        "Excluded" => tr("Exclusión configurada"), _ => reason,
    };

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        asking?.TrySetResult(false);
        timer.Stop();
        timer.Tick -= Tick;
        lifetime.Cancel();
        lifetime.Dispose();
        knownRows.Clear();
        icons.Clear();
        expanded.Clear();
        endedGroups.Clear();
        Rows.Clear();
        Changed(nameof(IsSampling));
    }
}

/// Una fila de la confirmación: qué proceso, su PID y ejecutable, y cuánta RAM libera.
public sealed record ConfirmRow(string Name, string Detail, string Memory);

/// Lo que se muestra antes de finalizar: alcance, cuántos procesos y cuánta RAM, la lista exacta y el aviso de cierre forzado.
public sealed record ProcessConfirm(string Title, string Message, string Summary, IReadOnlyList<ConfirmRow> Rows, string Affirmative, string Glyph, string Warning)
{
    public bool HasRows => Rows.Count > 0;
}

public static class ProcessConfirmation
{
    private const string GlyphWarning = "\uE7BA", GlyphShield = "\uEA18";

    public static ProcessConfirm For(ProcessActionPreview preview) => Build(preview.Request.Mode switch
    {
        ProcessActionMode.Application => tr("Finalizar aplicación"),
        ProcessActionMode.Tree => tr("Finalizar árbol"), _ => tr("Finalizar proceso"),
    }, Scope(preview.Request.Mode), preview.Processes, preview.UsesPrivateWorkingSet, preview.RamBytes, tr("Finalizar"), GlyphWarning);

    /// Acceso denegado: los procesos que faltan, y el reintento con permisos de administrador.
    public static ProcessConfirm Retry(string title, string message, IReadOnlyList<ProcessSnapshot> processes, bool usesPrivate, string affirmative) =>
        Build(title, message, processes, usesPrivate, null, affirmative, GlyphShield);

    private static ProcessConfirm Build(string title, string message, IReadOnlyList<ProcessSnapshot> processes, bool usesPrivate,
        long? totalRam, string affirmative, string glyph)
    {
        string ramLabel = usesPrivate ? tr("RAM privada residente") : tr("RAM residente total");
        long? ram = totalRam ?? processes.Sum(p => p.RamBytes(usesPrivate) ?? 0);
        return new ProcessConfirm(title, message, processes.Count == 0 ? "" : tr("%@ procesos · %@: %@", processes.Count, ramLabel, ProcessViewModel.Memory(ram)),
            processes.Select(p => new ConfirmRow(p.Name, tr("PID %@", p.Pid) + (string.IsNullOrEmpty(p.Executable) ? "" : " · " + p.Executable),
                ProcessViewModel.Memory(p.RamBytes(usesPrivate)))).ToList(),
            affirmative, glyph, tr("El cierre es forzado. Puedes perder trabajo sin guardar."));
    }

    private static string Scope(ProcessActionMode mode) => mode switch
    {
        ProcessActionMode.Application => tr("Todos los procesos de la aplicación y sus descendientes"),
        ProcessActionMode.Tree => tr("El proceso seleccionado y sus descendientes"),
        _ => tr("Solo el proceso seleccionado"),
    };

    /// El mismo contenido como texto (registro, pruebas y lectores de pantalla).
    public static string Text(ProcessActionPreview preview)
    {
        string ramLabel = preview.UsesPrivateWorkingSet ? tr("RAM privada residente") : tr("RAM residente total");
        var lines = preview.Processes.Select(p => tr("%@ · PID %@ · %@", p.Name, p.Pid,
            ProcessViewModel.Memory(p.RamBytes(preview.UsesPrivateWorkingSet))));
        return Scope(preview.Request.Mode) + Environment.NewLine
            + tr("%@ procesos · %@: %@", preview.Processes.Count, ramLabel, ProcessViewModel.Memory(preview.RamBytes))
            + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, lines)
            + Environment.NewLine + Environment.NewLine + tr("El cierre es forzado. Puedes perder trabajo sin guardar.");
    }
}
