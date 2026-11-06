using System.Text.Json.Nodes;
using System.Windows.Input;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// Una herramienta de la mascota con su selector de permiso (Normal · Auto · Preguntar · Apagado).
public sealed record PetToolRow(string Title, string Hint, List<Segment> Modes);

/// Permisos de la mascota Nimbo editados desde la app. Es el mismo `pet\policy.json` que lee el complemento (se relee solo al cambiar);
/// lo que no se conoce de ese archivo se conserva tal cual.
public sealed partial class PanelViewModel
{
    private static string PetPolicyPath => Path.Combine(Paths.DataDir, "pet", "policy.json");

    private static JsonObject PetPolicyLoad()
    {
        try { return JsonNode.Parse(File.ReadAllText(PetPolicyPath)) as JsonObject ?? []; }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { return []; }
    }

    private void PetPolicyEdit(Action<JsonObject> change)
    {
        var root = PetPolicyLoad();
        change(root);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PetPolicyPath)!);
            string tmp = PetPolicyPath + ".tmp";
            File.WriteAllText(tmp, root.ToJsonString());
            File.Move(tmp, PetPolicyPath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        Notify();
    }

    private static bool PetFlag(string key) => PetPolicyLoad()[key] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
    private void PetSetFlag(string key, bool value) => PetPolicyEdit(o => o[key] = value);

    // ---- interruptores generales
    public bool PetPausedOn => PetFlag("paused");
    public bool PetStrictOn => PetFlag("strict");
    public bool PetRiskyOn => PetFlag("risky_from_phone");
    public bool PetApprovalsOn => !(PetPolicyLoad()["tools"] is JsonObject t && t["agent.decide"]?.GetValue<string>() == "off");
    public string PetPolicyTitle => tr("Permisos de Nimbo");
    public string PetPolicyDetail => tr("Qué puede hacer la mascota por su cuenta y qué debe preguntar. Lo que pregunta te llega al celular, a la placa y a la PC: responde el primero que conteste.");
    public string PetPausedTitle => tr("Pausar a Nimbo");
    public string PetPausedDetail => tr("No hace ninguna acción ni acepta decisiones.");
    public string PetStrictTitle => tr("Modo estricto");
    public string PetStrictDetail => tr("Todo lo que hace por su cuenta pasa a preguntar.");
    public string PetRiskyTitle => tr("Permitir lo delicado desde el teléfono");
    public string PetRiskyDetail => tr("Comandos que Claude Code marca como peligrosos (borrar en masa, forzar un push…). Apagado es lo seguro: solo se pueden denegar.");
    public string PetApprovalsTitle => tr("Decidir permisos de agentes desde el teléfono");
    public string PetApprovalsDetail => tr("Ver y responder lo que piden Claude Code y otros agentes. Requiere «Aprobar permisos desde el notch».");
    public ICommand TogglePetPaused => new Command(() => PetSetFlag("paused", !PetFlag("paused")));
    public ICommand TogglePetStrict => new Command(() => PetSetFlag("strict", !PetFlag("strict")));
    public ICommand TogglePetRisky => new Command(() =>
    {
        bool on = !PetFlag("risky_from_phone");
        Prefs.SetSwitch(PrefKeys.PetApproveRisky, on);   // la app también lo exige por su cuenta
        PetSetFlag("risky_from_phone", on);
    });
    public ICommand TogglePetApprovals => new Command(() => PetPolicyEdit(o =>
    {
        var tools = o["tools"] as JsonObject ?? (JsonObject)(o["tools"] = new JsonObject());
        if (PetApprovalsOn) tools["agent.decide"] = "off"; else tools.Remove("agent.decide");
    }));

    // ---- horario de silencio
    public string PetQuietTitle => tr("Horario de silencio");
    public string PetQuietDetail => tr("Sin avisos proactivos en esas horas (los recordatorios y los permisos siguen llegando).");
    public List<Segment> PetQuietSegments
    {
        get
        {
            string current = PetPolicyLoad()["quiet"]?.GetValue<string>() ?? "";
            Segment Seg(string title, string value) => new(title, current == value, new Command(() => PetPolicyEdit(o => { if (value.Length == 0) o.Remove("quiet"); else o["quiet"] = value; })));
            return [Seg(tr("Nunca"), ""), Seg("22:00–07:00", "22:00-07:00"), Seg("23:00–07:00", "23:00-07:00"), Seg("00:00–08:00", "00:00-08:00")];
        }
    }

    // ---- una fila por herramienta
    private static readonly (string Name, string Title, string Hint)[] PetTools =
    [
        ("usage.set", "Widget de uso de IA", "Enciende o apaga el widget · se deshace fácil"),
        ("pomodoro.control", "Pomodoro", "Inicia, pausa, reinicia · se deshace fácil"),
        ("todo.add", "Tareas", "Añade tareas a tu lista · se deshace fácil"),
        ("note.add", "Notas", "Crea notas · se deshace fácil"),
        ("notify.show", "Avisos en el notch", "Muestra avisos y recordatorios · se deshace fácil"),
        ("reminder.add", "Recordatorios", "Crea recordatorios · se deshace fácil"),
        ("memory.remember", "Memoria: recordar", "Guarda lo que le pides que recuerde · se deshace fácil"),
        ("memory.forget", "Memoria: olvidar", "Borra un recuerdo · sensible: por defecto pregunta"),
    ];

    public IReadOnlyList<PetToolRow> PetToolRows
    {
        get
        {
            var tools = PetPolicyLoad()["tools"] as JsonObject;
            return PetTools.Select(t =>
            {
                string? mode = tools?[t.Name]?.GetValue<string>();
                Segment Seg(string title, string? value) => new(title, mode == value, new Command(() => PetPolicyEdit(o =>
                {
                    var ts = o["tools"] as JsonObject ?? (JsonObject)(o["tools"] = new JsonObject());
                    if (value is null) ts.Remove(t.Name); else ts[t.Name] = value;
                })));
                return new PetToolRow(tr(t.Title), tr(t.Hint), [Seg(tr("Normal"), null), Seg(tr("Auto"), "auto"), Seg(tr("Preguntar"), "ask"), Seg(tr("Apagado"), "off")]);
            }).ToList();
        }
    }

    public bool ShowPetPolicy => PetAddonInstalled;
}
