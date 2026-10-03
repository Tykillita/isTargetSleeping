using System.Globalization;
using System.Text;

namespace IsTargetSleeping;

/// Zonas de memoria que se pueden liberar. Los bits son los mismos que usa Mem
/// Reduct en `ReductMask2` (src/main.h), así su configuración se importa tal cual.
[Flags]
public enum CleanAreas
{
    None = 0,
    /// Memoria de trabajo de los procesos (menos los protegidos: modelos y juego).
    WorkingSets = 0x01,
    /// Caché de archivos del sistema.
    SystemFileCache = 0x02,
    /// Lista en espera de prioridad baja (lo que menos se echa de menos).
    StandbyLowPriority = 0x04,
    /// Lista en espera completa: Windows vuelve a leer de disco lo que necesite.
    Standby = 0x08,
    /// Lista de páginas modificadas: se escriben a disco.
    ModifiedList = 0x10,
    /// Combinar páginas iguales (Windows 10+).
    CombineLists = 0x20,
    /// Caché del registro (Windows 8.1+).
    RegistryCache = 0x40,
    /// Caché de archivos modificados de los volúmenes.
    ModifiedFileCache = 0x80,

    All = 0xFF,
    /// El predeterminado de Mem Reduct (REDUCT_MASK_DEFAULT).
    Default = WorkingSets | SystemFileCache | StandbyLowPriority | RegistryCache | CombineLists | ModifiedFileCache,
    /// Las que pueden congelar el PC un momento (REDUCT_MASK_FREEZES).
    Slow = Standby | ModifiedList,
}

/// Lo que la app le pide al agente con privilegios, como un único argumento
/// (`$(Arg0)` de la tarea programada): `a=e7;k=1234,5678`.
///  - a: zonas (hexadecimal)  · k: PIDs que no se tocan  · r: solicitud  · i: identidades que no se tocan.
/// El agente corre elevado: todo se valida estrictamente y lo desconocido se rechaza.
public readonly record struct CleanSpec(CleanAreas Areas, IReadOnlyList<int> Keep)
{
    public const int MaxLength = 32_000;
    public const int MaxKeep = 1024;
    public Guid RequestId { get; init; }
    public IReadOnlyList<ProcessIdentity> KeepIdentities { get; init; } = [];

    public string Format()
    {
        if (((int)Areas & ~(int)CleanAreas.All) != 0 || Keep.Count + KeepIdentities.Count > MaxKeep
            || Keep.Any(p => p <= 0) || KeepIdentities.Any(p => p.Pid <= 0 || p.CreatedFileTime <= 0))
            throw new ArgumentException("Invalid clean request.");
        var sb = new StringBuilder();
        sb.Append("a=").Append(((int)Areas).ToString("x", CultureInfo.InvariantCulture));
        if (Keep.Count > 0) sb.Append(";k=").Append(string.Join(',', Keep.Select(p => p.ToString(CultureInfo.InvariantCulture))));
        if (RequestId != Guid.Empty) sb.Append(";r=").Append(RequestId.ToString("N"));
        if (KeepIdentities.Count > 0) sb.Append(";i=").Append(string.Join(',', KeepIdentities.Select(p =>
            p.Pid.ToString(CultureInfo.InvariantCulture) + ":" + p.CreatedFileTime.ToString(CultureInfo.InvariantCulture))));
        var text = sb.ToString();
        if (Parse(text) is null) throw new ArgumentException("Invalid or oversized clean request.");
        return text;
    }

    public static CleanSpec? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength || text.Any(c => c > 127 || char.IsControl(c))) return null;
        CleanAreas? areas = null;
        var keep = new List<int>();
        var identities = new List<ProcessIdentity>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Guid requestId = Guid.Empty;
        foreach (var part in text.Split(';'))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) return null;
            var (key, value) = (part[..eq], part[(eq + 1)..]);
            if (!seen.Add(key)) return null;
            switch (key)
            {
                case "a" when areas is null:
                    if (value.Length is 0 or > 2 || !int.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var mask)
                        || (mask & ~(int)CleanAreas.All) != 0) return null;
                    areas = (CleanAreas)mask;
                    break;
                case "k" when keep.Count == 0:
                    foreach (var pid in value.Split(','))
                    {
                        if (pid.Length is 0 or > 10 || !pid.All(char.IsAsciiDigit) || !int.TryParse(pid, CultureInfo.InvariantCulture, out var n) || n <= 0)
                            return null;
                        keep.Add(n);
                    }
                    if (keep.Count > MaxKeep) return null;
                    break;
                case "r":
                    if (value.Length != 32 || !Guid.TryParseExact(value, "N", out requestId) || requestId == Guid.Empty) return null;
                    break;
                case "i":
                    foreach (var item in value.Split(','))
                    {
                        var fields = item.Split(':');
                        if (fields.Length != 2 || !fields.All(f => f.Length > 0 && f.All(char.IsAsciiDigit))
                            || !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out int pid) || pid <= 0
                            || !long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks) || ticks <= 0) return null;
                        identities.Add(new ProcessIdentity(pid, ticks));
                        if (identities.Count + keep.Count > MaxKeep) return null;
                    }
                    break;
                default:
                    return null;
            }
        }
        if (keep.Count + identities.Count > MaxKeep) return null;
        return areas is { } a ? new CleanSpec(a, keep) { RequestId = requestId, KeepIdentities = identities } : null;
    }
}

/// Códigos de salida del agente (lo que la app lee en «Resultado de la última ejecución» de la tarea).
public static class AgentExit
{
    /// Terminó: los bits bajos son las zonas que fallaron.
    public const int Done = 0x10000;
    public const int BadSpec = 2;
    public const int NotElevated = 5;

    public static bool IsDone(int code) => (code & ~0xFF) == Done;
    public static CleanAreas Failed(int code) => (CleanAreas)(code & 0xFF);
}
