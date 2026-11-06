using System.Collections.ObjectModel;
using System.Windows.Input;
using IsTargetSleeping.UI.Pets;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// Una burbuja de la charla con la mascota.
public sealed record ChatLine(string Text, bool FromMe);

/// La página «Chat» del notch (opcional, en el dock): hablas con la mascota y responde el modelo local que ya esté cargado.
public sealed partial class PanelViewModel
{
    private readonly ChatService chat = new();
    /// El vínculo con la mascota: sube charlando, cuando un agente termina y al acabar un Pomodoro; nunca baja.
    public Bond Bond { get; } = new(Path.Combine(Paths.DataDir, "bond.json"));
    public WeekLog Week { get; } = new(Path.Combine(Paths.DataDir, "week.json"));
    public string PetWeekTitle => tr("Esta semana");
    public string PetWeekLine => tr("%d mensajes · %d agentes terminaron · %d permisos concedidos · %d denegados · %d Pomodoros · %d siestas del modelo",
        Week.Count(WeekEvent.Chat), Week.Count(WeekEvent.AgentDone), Week.Count(WeekEvent.Approved), Week.Count(WeekEvent.Denied), Week.Count(WeekEvent.Focus),
        Ollama.Stats.Week(DateTime.Now).Naps);

    /// Un permiso se respondió desde el notch: cuenta para el resumen (plegar a la terminal no cuenta).
    private void NoteApproval(HookVerdict verdict)
    {
        if (verdict is HookVerdict.Allow or HookVerdict.Always) Week.Record(WeekEvent.Approved);
        else if (verdict == HookVerdict.Deny) Week.Record(WeekEvent.Denied);
        else return;
        PropertyChanged?.Invoke(this, new(nameof(PetWeekLine)));
    }

    // Charla ambiente: cada minuto decide si toca una frase corta. Apagada por defecto; calla en modo juego, con un permiso pendiente o escuchando.
    private System.Windows.Threading.DispatcherTimer? chatterTimer;
    private DateTime lastChatter = DateTime.Now;
    private readonly Random chatterRandom = new();
    public ChatterLevel ChatterLevel => Chatter.Parse(Prefs.StoredList(PrefKeys.PetChatter)?.FirstOrDefault());
    public string ChatterTitle => tr("Charla ambiente de la mascota");
    public string ChatterDetail => tr("De vez en cuando dice una frase corta y amable en la página Chat, con una campana en el notch. No usa el modelo y calla en modo juego.");
    public List<Segment> ChatterSegments => [
        new Segment(tr("Nunca"), ChatterLevel == ChatterLevel.Off, new Command(() => Prefs.SetList(PrefKeys.PetChatter, ["off"]))),
        new Segment(tr("Rara vez"), ChatterLevel == ChatterLevel.Rare, new Command(() => Prefs.SetList(PrefKeys.PetChatter, ["rare"]))),
        new Segment(tr("A veces"), ChatterLevel == ChatterLevel.Normal, new Command(() => Prefs.SetList(PrefKeys.PetChatter, ["normal"]))),
    ];

    private void StartChatter()
    {
        chatterTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        chatterTimer.Tick += (_, _) => ChatterTick();
        chatterTimer.Start();
    }

    private void ChatterTick()
    {
        bool quiet = Supervisor.Game.Active || HasApproval || ChatListening || ChatBusy;
        quiet |= !NotifyAllows(AgentNotifyKind.Ambient);
        if (Chatter.Pick(ChatterLevel, Mood, Bond.Level, DateTime.Now, lastChatter, quiet, chatterRandom) is not { } key) return;
        lastChatter = DateTime.Now;
        var line = tr(key);
        ChatLines.Add(new(line, false));
        RaiseNotice(line);
    }
    private DateTime happyUntil;

    /// Un agente terminó o se acabó un Pomodoro: suma vínculo y la mascota está contenta un rato.
    public void NoteGoodThing(BondEvent e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) { dispatcher.BeginInvoke(() => NoteGoodThing(e)); return; }
        Bond.Record(e);
        if (e == BondEvent.AgentDone) { NoteAgentFinished(); away.Record(AwayKind.AgentDone); }
        Week.Record(e switch { BondEvent.Chat => WeekEvent.Chat, BondEvent.AgentDone => WeekEvent.AgentDone, _ => WeekEvent.Focus });
        happyUntil = DateTime.Now.AddMinutes(2);
        PropertyChanged?.Invoke(this, new(nameof(BondLine)));
        PropertyChanged?.Invoke(this, new(nameof(MoodLine)));
        PropertyChanged?.Invoke(this, new(nameof(PetWeekLine)));
    }

    public PetMood Mood => PetMoods.Of(DateTime.Now.Hour, HasApproval, Mem.PhysicalPressure == SystemMemory.Level.Critical, DateTime.Now < happyUntil);
    private string MoodName => Mood switch
    {
        PetMood.Worried => tr("preocupación"), PetMood.Alert => tr("alerta"), PetMood.Happy => tr("alegría"), PetMood.Sleepy => tr("sueño"), _ => tr("calma"),
    };
    private string LevelName => Bond.Level switch
    {
        0 => tr("recién conocidos"), 1 => tr("compañeros"), 2 => tr("amigos"), 3 => tr("buenos amigos"), _ => tr("inseparables"),
    };
    public string MoodLine => tr("Humor: %@", MoodName);
    public string BondLine => tr("Vínculo: %@ · %d/%d", LevelName, Bond.Level, Bond.MaxLevel);
    public string ResetBondLabel => tr("Reiniciar el vínculo");
    public ICommand ResetBond => resetBond ??= new Command(() => { Bond.Reset(); PropertyChanged?.Invoke(this, new(nameof(BondLine))); });
    private ICommand? resetBond;
    private readonly List<ChatTurn> chatHistory = [];
    private CancellationTokenSource? chatCancel;
    public ObservableCollection<ChatLine> ChatLines { get; } = [];
    public bool ShowingChat => view == PanelView.Chat;
    public bool ChatDockOn => Prefs.Switch(PrefKeys.ChatDock, false);
    public string NotchTabChat => tr("Chat");
    private string PetName => PetCatalog.Find(Prefs.PetSpecies).Name;
    public string ChatPageTitle => tr("Habla con %@", PetName);
    public string ChatPageDetail => tr("Responde el modelo local que ya tengas cargado en Ollama: no se carga ninguno por su cuenta y nada sale de este PC. La charla solo vive en memoria.");
    public string ChatDockTitle => tr("Chat con la mascota en el dock");
    public string ChatDockDetail => tr("Añade una página para hablar con tu mascota con el modelo local que ya esté cargado.");
    public string ChatPlaceholder => tr("Escribe un mensaje…");
    public string ChatSendLabel => tr("Enviar");
    public string ChatStopLabel => tr("Parar");
    public string ChatClearLabel => tr("Borrar la charla");
    public string ChatEmptyText => tr("Di hola a %@. Si no hay un modelo cargado, cárgalo antes en Ollama.", PetName);
    public bool ChatEmpty => ChatLines.Count == 0;
    private string chatInput = "";
    public string ChatInput { get => chatInput; set { chatInput = value; PropertyChanged?.Invoke(this, new(nameof(ChatInput))); } }
    private string? chatStatus;
    public string? ChatStatus => chatStatus;
    public bool HasChatStatus => !string.IsNullOrEmpty(chatStatus);
    public bool ChatBusy => chatCancel is not null;
    public bool ChatIdle => chatCancel is null;
    // Voz local (reconocimiento y síntesis de Windows): solo al pulsar el micrófono, una frase cada vez; nunca escucha por su cuenta.
    public bool VoiceAvailable => Voice.CanListen(Effective);
    private CancellationTokenSource? listenCancel;
    public bool ChatListening => listenCancel is not null;
    public bool ChatSpeakOn => Prefs.Switch(PrefKeys.ChatSpeak, false);
    public string ChatMicLabel => ChatListening ? tr("Dejar de escuchar") : tr("Hablar");
    public string ChatSpeakLabel => tr("Leer las respuestas en voz alta");
    public ICommand ToggleChatSpeak { get; private set; } = null!;
    public ICommand ToggleListen { get; private set; } = null!;
    public ICommand ToggleChatDock { get; private set; } = null!;
    public ICommand SendChat { get; private set; } = null!;
    public ICommand StopChat { get; private set; } = null!;
    public ICommand ClearChat { get; private set; } = null!;

    private void InitChat()
    {
        ChatLines.CollectionChanged += (_, _) => PropertyChanged?.Invoke(this, new(nameof(ChatEmpty)));
        StartChatter();
        StartAgentAlerts();
        StartAway();
        ToggleChatDock = new Command(() => { Prefs.Toggle(PrefKeys.ChatDock, false); Notify(); });
        SendChat = new Command(() => _ = AskChatAsync());
        ToggleChatSpeak = new Command(() => { Prefs.Toggle(PrefKeys.ChatSpeak, false); if (!ChatSpeakOn) Voice.StopSpeaking(); Notify(); });
        ToggleListen = new Command(() => { if (listenCancel is not null) listenCancel.Cancel(); else _ = ListenAsync(); });
        StopChat = new Command(() => { chatCancel?.Cancel(); Voice.StopSpeaking(); });
        ClearChat = new Command(() =>
        {
            chatCancel?.Cancel();
            chatHistory.Clear();
            ChatLines.Clear();
            SetChatStatus(null);
            PropertyChanged?.Invoke(this, new(nameof(ChatEmpty)));
        });
    }

    private void SetChatStatus(string? status)
    {
        chatStatus = status;
        PropertyChanged?.Invoke(this, new(nameof(ChatStatus)));
        PropertyChanged?.Invoke(this, new(nameof(HasChatStatus)));
    }

    private void ChatBusyChanged()
    {
        PropertyChanged?.Invoke(this, new(nameof(ChatBusy)));
        PropertyChanged?.Invoke(this, new(nameof(ChatIdle)));
    }

    private async Task ListenAsync()
    {
        listenCancel = new();
        SetChatStatus(tr("Escuchando… habla ahora"));
        PropertyChanged?.Invoke(this, new(nameof(ChatListening)));
        PropertyChanged?.Invoke(this, new(nameof(ChatMicLabel)));
        try
        {
            var text = await Voice.Listen(Effective, listenCancel.Token);
            SetChatStatus(null);
            if (text is null) { if (!listenCancel.IsCancellationRequested) SetChatStatus(tr("No te he oído. Pulsa el micrófono y vuelve a intentarlo.")); }
            else { ChatInput = text; await AskChatAsync(); }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetChatStatus(tr("No puedo usar el micrófono: %@", ex.Message));   // sin micrófono o sin permiso de Windows
        }
        finally
        {
            listenCancel?.Dispose();
            listenCancel = null;
            PropertyChanged?.Invoke(this, new(nameof(ChatListening)));
            PropertyChanged?.Invoke(this, new(nameof(ChatMicLabel)));
        }
    }

    private async Task AskChatAsync()
    {
        var text = ChatInput.Trim();
        if (text.Length == 0 || chatCancel is not null) return;
        ChatInput = "";
        SetChatStatus(null);
        chatHistory.Add(new("user", text));
        ChatLines.Add(new(text, true));
        ChatLines.Add(new("…", false));
        PropertyChanged?.Invoke(this, new(nameof(ChatEmpty)));
        var reply = ChatLines.Count - 1;
        chatCancel = new();
        ChatBusyChanged();
        try
        {
            var model = ChatService.PickModel(await OllamaApi.Loaded());
            var result = await chat.Ask(chatHistory, model, Effective == "es" ? "Spanish" : "English", partial => ChatLines[reply] = new(partial, false), chatCancel.Token, string.Join(" ", PetPersonality.For(Prefs.PetSpecies ?? PetCatalog.Find(null).Id), PetMoods.PromptLine(Mood), PetMoods.BondPromptLine(Bond.Level)));
            if (result.Failure == ChatFailure.None && result.Text.Length > 0)
            {
                chatHistory.Add(new("assistant", result.Text));
                ChatLines[reply] = new(result.Text, false);
                NoteGoodThing(BondEvent.Chat);
                if (ChatSpeakOn) Voice.Speak(result.Text, Effective);
            }
            else
            {
                if (reply < ChatLines.Count) ChatLines.RemoveAt(reply);   // sin respuesta: la burbuja vacía no se queda
                if (chatHistory.Count > 0) chatHistory.RemoveAt(chatHistory.Count - 1);
                SetChatStatus(result.Failure switch
                {
                    ChatFailure.NoModel => tr("No hay ningún modelo cargado: cárgalo en Ollama y vuelve a escribir."),
                    ChatFailure.Offline => tr("Ollama no responde. Enciéndelo y vuelve a intentarlo."),
                    ChatFailure.Error => tr("El modelo no pudo responder: %@", result.Detail ?? ""),
                    _ => null,
                });
            }
        }
        finally
        {
            chatCancel?.Dispose();
            chatCancel = null;
            ChatBusyChanged();
        }
    }
}
