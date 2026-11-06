using System.Text.Json;
using System.Text.Json.Nodes;

namespace IsTargetSleeping.Pet;

/// Permisos de la mascota: por herramienta `off | ask | auto`, mas "pausar" y "modo estricto". Vive en `policy.json`
/// (se relee si cambia el archivo, asi la app o la web pueden editarlo sin reiniciar). Sin archivo o con un archivo roto
/// rigen los valores por defecto, que son seguros: leer y lo reversible solas; lo sensible o en la PC, preguntando.
/// Si el archivo existe pero esta danado, la mascota queda PAUSADA (no se adivina que permisos tenia el usuario).
public sealed class PolicyStore
{
    private readonly string path;
    private readonly object gate = new();
    private readonly Dictionary<string, ToolMode> overrides = new(StringComparer.Ordinal);
    private bool paused, strict, riskyFromPhone;
    private int teamDaily = 300_000, teamQueued = 3;
    private string quiet = "";
    private (DateTime Time, long Length) loadedStamp;   // fecha Y tamano: dos escrituras seguidas pueden compartir la misma fecha

    public PolicyStore(string path)
    {
        this.path = path;
        Reload(force: true);
    }

    public static ToolMode DefaultFor(ToolRisk risk) => risk switch
    {
        ToolRisk.Read or ToolRisk.Reversible => ToolMode.Auto,
        _ => ToolMode.Ask,
    };

    /// Horario de silencio «HH:mm-HH:mm» (vacío = sin silencio): no se envian avisos proactivos dentro de el.
    public string Quiet { get { Reload(); lock (gate) return quiet; } }
    public bool Paused { get { Reload(); lock (gate) return paused; } }
    /// Presupuesto de los trabajos de Team que lanza Nimbo: tokens por día y máximo de trabajos en cola. La app lo aplica.
    public int TeamDailyTokens { get { Reload(); lock (gate) return teamDaily; } }
    public int TeamMaxQueued { get { Reload(); lock (gate) return teamQueued; } }
    public bool Strict { get { Reload(); lock (gate) return strict; } }
    /// Permitir desde el celular/placa lo que AgentRisk marca como delicado. Apagado por defecto (la app lo exige tambien).
    public bool RiskyFromPhone { get { Reload(); lock (gate) return riskyFromPhone && !paused; } }
    /// Se pueden resolver peticiones de agentes desde aqui (pausa y `agent.decide: off` lo cierran).
    public bool ApprovalsEnabled { get { Reload(); lock (gate) return !paused && !(overrides.TryGetValue("agent.decide", out var m) && m == ToolMode.Off); } }

    /// El modo efectivo. `untrusted` = la accion nace de contenido ajeno (un agente, un archivo, una pagina): nunca se ejecuta sola.
    public ToolMode Mode(ToolSpec spec, bool untrusted = false)
    {
        Reload();
        lock (gate)
        {
            if (paused) return ToolMode.Off;
            var mode = overrides.TryGetValue(spec.Name, out var m) ? m : DefaultFor(spec.Risk);
            if (mode == ToolMode.Auto && spec.Risk != ToolRisk.Read && (strict || untrusted)) mode = ToolMode.Ask;
            if (mode == ToolMode.Auto && spec.Risk == ToolRisk.Pc) mode = ToolMode.Ask;   // lo que ejecuta código o gasta tokens en la PC siempre pregunta
            return mode;
        }
    }

    public ToolMode? Override(string tool) { Reload(); lock (gate) return overrides.TryGetValue(tool, out var m) ? m : null; }

    public void Set(string tool, ToolMode? mode) => Update(() => { if (mode is null) overrides.Remove(tool); else overrides[tool] = mode.Value; });
    public void SetPaused(bool value) => Update(() => paused = value);
    public void SetStrict(bool value) => Update(() => strict = value);
    public void SetRiskyFromPhone(bool value) => Update(() => riskyFromPhone = value);
    public void SetQuiet(string value) => Update(() => quiet = value.Length == 0 || QuietPattern.IsMatch(value) ? value : "");
    private static readonly System.Text.RegularExpressions.Regex QuietPattern = new(@"^([01]\d|2[0-3]):[0-5]\d-([01]\d|2[0-3]):[0-5]\d$");

    private void Update(Action change)
    {
        lock (gate)
        {
            Reload(force: true);
            change();
            var tools = new JsonObject();
            foreach (var (k, v) in overrides) tools[k] = v.ToString().ToLowerInvariant();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, new JsonObject { ["paused"] = paused, ["strict"] = strict, ["risky_from_phone"] = riskyFromPhone, ["quiet"] = quiet, ["team_daily_tokens"] = teamDaily, ["team_max_queued"] = teamQueued, ["tools"] = tools }.ToJsonString());
            File.Move(tmp, path, overwrite: true);
            loadedStamp = Stamp();
        }
    }

    private (DateTime Time, long Length) Stamp() => File.Exists(path) ? (File.GetLastWriteTimeUtc(path), new FileInfo(path).Length) : (DateTime.MinValue, 0);

    private void Reload(bool force = false)
    {
        lock (gate)
        {
            var stamp = Stamp();
            if (!force && stamp == loadedStamp) return;
            loadedStamp = stamp;
            overrides.Clear();
            paused = strict = riskyFromPhone = false;
            quiet = "";
            (teamDaily, teamQueued) = (300_000, 3);
            if (stamp.Time == DateTime.MinValue) return;
            try
            {
                var root = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
                paused = root.TryGetProperty("paused", out var p) && p.ValueKind == JsonValueKind.True;
                strict = root.TryGetProperty("strict", out var s) && s.ValueKind == JsonValueKind.True;
                riskyFromPhone = root.TryGetProperty("risky_from_phone", out var rp) && rp.ValueKind == JsonValueKind.True;
                if (root.TryGetProperty("team_daily_tokens", out var td) && td.TryGetInt32(out var tdv) && tdv is >= 0 and <= 5_000_000) teamDaily = tdv;
                if (root.TryGetProperty("team_max_queued", out var tq) && tq.TryGetInt32(out var tqv) && tqv is >= 0 and <= 10) teamQueued = tqv;
                quiet = root.TryGetProperty("quiet", out var qv) && qv.ValueKind == JsonValueKind.String && System.Text.RegularExpressions.Regex.IsMatch(qv.GetString() ?? "", @"^([01]\d|2[0-3]):[0-5]\d-([01]\d|2[0-3]):[0-5]\d$") ? qv.GetString()! : "";
                if (root.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Object)
                    foreach (var t in tools.EnumerateObject())
                        if (t.Value.ValueKind == JsonValueKind.String && Enum.TryParse<ToolMode>(t.Value.GetString(), ignoreCase: true, out var mode) && Enum.IsDefined(mode))
                            overrides[t.Name] = mode;
            }
            catch (Exception e) when (e is IOException or JsonException) { overrides.Clear(); paused = true; strict = true; riskyFromPhone = false; }   // ilegible: falla cerrado (pausada) hasta que se repare
        }
    }
}
