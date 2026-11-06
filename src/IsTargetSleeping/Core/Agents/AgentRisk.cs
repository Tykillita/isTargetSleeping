using System.Text.RegularExpressions;

namespace IsTargetSleeping;

/// Qué órdenes de un agente merecen un clic deliberado: borrar de forma recursiva, forzar un push, descartar cambios, ejecutar algo descargado,
/// escalar privilegios, formatear, borrar bases de datos, apagar el equipo o tocar credenciales. Si una petición lo es, no se puede permitir con la
/// tecla Y ni con «Siempre», y ninguna regla guardada la deja pasar sola. Es una red de seguridad barata, no un análisis completo.
public static class AgentRisk
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly (Regex Pattern, string Reason)[] Commands =
    [
        (new(@"\brm\s+(-\w*[rf]\w*|--recursive|--force)\b", Options), "Borra archivos de forma recursiva o sin preguntar"),
        (new(@"\b(remove-item|rmdir|rd|del|erase)\b.*(-recurse|\s/s\b)", Options), "Borra archivos de forma recursiva o sin preguntar"),
        (new(@"\bgit\s+push\b.*(--force\b|--force-with-lease|\s-f\b)", Options), "Fuerza un push y reescribe el historial remoto"),
        (new(@"\bgit\s+(reset\s+--hard|clean\s+-\w*f\w*|checkout\s+--\s|restore\s+\.|branch\s+-D)", Options), "Descarta cambios o ramas sin poder recuperarlos"),
        (new(@"\b(curl|wget|iwr|invoke-webrequest)\b.*\|\s*(sh|bash|zsh|iex|powershell|pwsh)\b|\biex\s*\(", Options), "Ejecuta directamente algo descargado de internet"),
        (new(@"\bsudo\b|\bsu\s+-|\brunas\b", Options), "Pide privilegios elevados"),
        (new(@"\b(mkfs|fdisk|diskpart)\b|\bdd\s+if=|\bformat\s+[a-z]:", Options), "Formatea o escribe directamente en un disco"),
        (new(@"\b(drop|truncate)\s+(table|database|schema)\b", Options), "Borra una base de datos o una tabla"),
        (new(@"\b(shutdown|reboot|halt|restart-computer|stop-computer)\b", Options), "Apaga o reinicia el equipo"),
        (new(@"\bchmod\s+-R\s+7|\bchown\s+-R\b|\breg\s+(delete|add)\b", Options), "Cambia permisos o el registro de forma masiva"),
    ];

    private static readonly Regex SensitivePath = new(@"(\.ssh|\.env(\.|$)|id_rsa|id_ed25519|\.aws|credentials|\.npmrc|\.netrc|\.claude/settings)", Options);
    private static readonly HashSet<string> WriteTools = ["Write", "Edit", "MultiEdit", "NotebookEdit"];

    /// ¿Esta ruta o nombre apunta a credenciales o ajustes sensibles? Para no enseñarlos fuera de la PC (teléfono, placa).
    public static bool IsSensitivePath(string? path) => !string.IsNullOrEmpty(path) && SensitivePath.IsMatch(path.Replace('\\', '/'));

    /// La razón por la que es delicada (en español, clave de traducción), o null si no lo parece.
    public static string? Check(string? tool, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (tool == "Bash" || tool == "PowerShell")
            foreach (var (pattern, reason) in Commands)
                if (pattern.IsMatch(text)) return reason;
        if (tool is not null && WriteTools.Contains(tool) && SensitivePath.IsMatch(text.Replace('\\', '/'))) return "Modifica credenciales o ajustes sensibles";
        return null;
    }
}

public sealed record DecisionEntry(DateTime At, string Agent, string Project, string Tool, string Detail, string Verdict, string? Risk);

/// El registro de decisiones: qué pidió un agente, cuándo y qué se respondió (permitir, denegar, «siempre», una regla que lo dejó pasar, o la terminal).
/// Un archivo de líneas JSON en local; se recorta solo para no crecer sin fin. No guarda el contenido de los archivos, solo el comando o la ruta.
public sealed class DecisionLog(string path, int max = 500)
{
    private readonly object gate = new();

    public void Add(DecisionEntry entry)
    {
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, System.Text.Json.JsonSerializer.Serialize(entry) + "\n");
                var lines = File.ReadAllLines(path);
                if (lines.Length > max * 1.2) File.WriteAllLines(path, lines[^max..]);
            }
        }
        catch { /* sin disco: el registro de esta sesión se pierde, nada más */ }
    }

    /// Las últimas, la más reciente primero.
    public IReadOnlyList<DecisionEntry> Recent(int count)
    {
        try
        {
            lock (gate)
            {
                if (!File.Exists(path)) return [];
                var entries = new List<DecisionEntry>();
                foreach (var line in File.ReadLines(path).Reverse().Take(count * 2))
                {
                    try { if (System.Text.Json.JsonSerializer.Deserialize<DecisionEntry>(line) is { } e) entries.Add(e); } catch { }
                    if (entries.Count == count) break;
                }
                return entries;
            }
        }
        catch { return []; }
    }

    public void Clear() { try { lock (gate) File.Delete(path); } catch { } }
}
