using System.Windows.Input;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// La tarjeta del notch con un permiso que un agente pide: lo que quiere hacer y tres respuestas. Responder completa `Request.Answer`;
/// quien la muestra la quita cuando eso pasa (también si venció y decide la terminal).
public sealed class AgentApproval : System.ComponentModel.INotifyPropertyChanged
{
    public HookRequest Request { get; }
    /// Identificador que ve la mascota (32 hex): no es el de la sesión del agente.
    public string PetId { get; } = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    public AgentApproval(HookRequest request)
    {
        Request = request;
        Questions = AgentQuestions.Parse(request.ToolInput).Select(q => new QuestionRow(q)).ToList();
        foreach (var q in Questions) q.Changed += () => PropertyChanged?.Invoke(this, new(nameof(CanSend)));
        SendAnswers = new Command(() => { if (CanSend) request.Reply(AgentQuestions.Answers(Questions.Select(q => (q.Text, q.Chosen)))); });
        RequestChanges = new Command(() => request.Reject("The user wants changes to the plan. Ask what they would like to change before continuing."));
        Allow = new Command(() => request.Decide(HookVerdict.Allow));
        AllowKey = new Command(() => { if (request.Risk is null && request.Kind != HookKind.Question) request.Decide(HookVerdict.Allow); });
        Deny = new Command(() => request.Decide(HookVerdict.Deny));
        Always = new Command(() => request.Decide(HookVerdict.Always));
        Fold = new Command(() => request.Decide(HookVerdict.Fold));
    }

    public string Title => $"{(Request.Agent == "claude" ? "Claude Code" : Request.Agent)} · {Path.GetFileName((Request.Cwd ?? "").TrimEnd((char)92, '/'))}".TrimEnd(' ', (char)0xB7);
    public HookKind Kind => Request.Kind;
    public bool IsPermission => Kind == HookKind.Permission || (Kind == HookKind.Question && Questions.Count == 0);
    public bool IsQuestion => Kind == HookKind.Question && Questions.Count > 0;
    public bool IsPlan => Kind == HookKind.Plan;
    public string Question => IsQuestion ? tr("Te hace una pregunta") : IsPlan ? tr("Propone un plan y espera tu visto bueno") : tr("Pide permiso para usar %@", Request.Tool ?? "?");
    public IReadOnlyList<QuestionRow> Questions { get; }
    public string? PlanText => AgentQuestions.PlanText(Request.ToolInput);
    public bool HasPlanText => PlanText is not null;
    public string SendLabel => tr("Enviar respuesta");
    public string ApprovePlanLabel => tr("Aprobar el plan");
    public string ChangesLabel => tr("Pedir cambios");
    public bool CanSend => Questions.All(q => q.HasChoice);
    public string? Detail => Request.Detail;
    public bool HasDetail => !string.IsNullOrWhiteSpace(Request.Detail);
    public bool ShowDetail => IsPermission && HasDetail;
    public bool ShowHint => !IsQuestion || true;
    /// «Siempre» solo existe para Claude Code, el único del que se guarda una regla propia.
    public bool CanAlways => Request.Agent == "claude" && HookRules.KeyFor(Request) is not null && Request.Risk is null;
    /// Lo delicado (ver `AgentRisk`) pide un clic deliberado: sin tecla Y y sin «Siempre».
    public bool IsRisky => Request.Risk is not null;
    public string RiskText => Request.Risk is null ? "" : tr("Cuidado: %@", tr(Request.Risk));
    /// La tecla Y: no permite lo delicado.
    public ICommand AllowKey { get; private set; } = null!;
    public string AllowLabel => tr("Permitir");
    public string DenyLabel => tr("Denegar");
    public string AlwaysLabel => tr("Siempre");
    public string FoldLabel => tr("Decidir en la terminal");
    public string Hint => IsQuestion ? tr("Elige una opción por pregunta · Esc lo deja a la terminal") : Request.Risk is null ? tr("Y permite · N deniega · Esc lo deja a la terminal") : tr("N deniega · Esc lo deja a la terminal. Lo delicado se permite solo con el botón.");
    public ICommand Allow { get; }
    public ICommand Deny { get; }
    public ICommand Always { get; }
    public ICommand Fold { get; }
    public ICommand SendAnswers { get; }
    public ICommand RequestChanges { get; }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

/// Una opción de una pregunta del agente, marcable.
public sealed class OptionRow : System.ComponentModel.INotifyPropertyChanged
{
    private readonly QuestionRow owner;
    private bool selected;
    public OptionRow(QuestionRow owner, AskOption option) { this.owner = owner; Label = option.Label; Description = option.Description; Pick = new Command(() => owner.Toggle(this)); }
    public string Label { get; }
    public string Description { get; }
    public bool HasDescription => Description.Length > 0;
    public ICommand Pick { get; }
    public bool IsSelected { get => selected; set { selected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

/// Una pregunta con sus opciones: una sola elegible o varias, según `multiSelect`.
public sealed class QuestionRow
{
    public QuestionRow(AskQuestion question)
    {
        Text = question.Text; Header = question.Header; Multi = question.Multi;
        Options = question.Options.Select(o => new OptionRow(this, o)).ToList();
    }
    public string Text { get; }
    public string Header { get; }
    public bool HasHeader => Header.Length > 0;
    public bool Multi { get; }
    public IReadOnlyList<OptionRow> Options { get; }
    public IEnumerable<string> Chosen => Options.Where(o => o.IsSelected).Select(o => o.Label);
    public bool HasChoice => Options.Any(o => o.IsSelected);
    public event Action? Changed;
    internal void Toggle(OptionRow option)
    {
        if (Multi) option.IsSelected = !option.IsSelected;
        else foreach (var o in Options) o.IsSelected = o == option;
        Changed?.Invoke();
    }
}
