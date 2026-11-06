using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// Permisos que piden los agentes (Claude Code) mientras el notch está abierto o cerrado: se muestra uno, el resto espera su turno.
public sealed partial class PanelViewModel
{
    private readonly List<AgentApproval> approvalQueue = [];
    public AgentApproval? Approval { get; private set; }
    public bool HasApproval => Approval is not null;
    /// Llegó un permiso: el notch se abre solo para enseñarlo. Se vació la cola: puede volver a cerrarse.
    public event Action? ApprovalRequested;
    public event Action? ApprovalsCleared;

    public bool AgentApprovalsOn => Prefs.AgentApprovals;
    public System.Windows.Input.ICommand ToggleAgentApprovals => agentApprovalsToggle ??= new Command(Prefs.ToggleAgentApprovals);
    private System.Windows.Input.ICommand? agentApprovalsToggle;
    public string AgentApprovalsTitle => tr("Aprobar los permisos de Claude Code desde el notch");
    public string AgentApprovalsDetail => tr("Añade hooks a ~/.claude/settings.json (con copia de seguridad) y los quita al apagarlo. Si la app no corre, Claude Code pregunta en su terminal como siempre.");
    public bool AgentSoundsOn => Prefs.AgentSounds;
    public System.Windows.Input.ICommand ToggleAgentSounds => agentSoundsToggle ??= new Command(Prefs.ToggleAgentSounds);
    private System.Windows.Input.ICommand? agentSoundsToggle;
    public string AgentSoundsTitle => tr("Sonido de los agentes");
    public string AgentSoundsDetail => tr("Un aviso corto y suave cuando un agente pide permiso o termina.");
    private string? agentApprovalsStatus;
    public string? AgentApprovalsStatus => agentApprovalsStatus;
    public bool HasAgentApprovalsStatus => !string.IsNullOrEmpty(agentApprovalsStatus);
    public void SetAgentApprovalsStatus(string? status)
    {
        agentApprovalsStatus = status;
        Notify();
    }

    public void ShowApproval(HookRequest request)
    {
        // Los eventos nacen en el hilo del pipe: todo lo demás va al de la interfaz.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) { dispatcher.BeginInvoke(() => ShowApproval(request)); return; }
        var item = new AgentApproval(request);
        away.Record(AwayKind.PermissionAsked);
        _ = request.Answer.ContinueWith(answer => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { if (answer.IsCompletedSuccessfully) NoteApproval(answer.Result); Resolve(item); }));
        if (Approval is null) { Approval = item; Notify(); }
        else { approvalQueue.Add(item); Notify(); }
        if (NotifyAllows(AgentNotifyPolicy.KindOf(request.Kind))) ApprovalRequested?.Invoke();
        PublishPetApprovals();
    }

    private void Resolve(AgentApproval item)
    {
        if (approvalQueue.Remove(item)) Notify();
        else if (Approval == item)
        {
            Approval = approvalQueue.Count > 0 ? approvalQueue[0] : null;
            if (Approval is not null) approvalQueue.RemoveAt(0);
            Notify();
            if (Approval is null) ApprovalsCleared?.Invoke();
        }
        PublishPetApprovals();
    }
}
