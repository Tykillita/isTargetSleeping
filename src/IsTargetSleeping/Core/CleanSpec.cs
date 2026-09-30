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
/// (`$(Arg0)` de la tarea programada): `a=e7;k=1234,5678;x=memreduct`.
///  - a: zonas (hexadecimal)  · k: PIDs que no se tocan  · x=memreduct: cerrar Mem Reduct.
/// El agente corre elevado: todo se valida estrictamente y lo desconocido se rechaza.
public readonly record struct CleanSpec(CleanAreas Areas, IReadOnlyList<int> Keep, bool CloseMemReduct = false)
{
    public const int MaxLength = 1024;
    public const int MaxKeep = 128;

    public string Format()
    {
        var sb = new StringBuilder();
        sb.Append("a=").Append(((int)Areas).ToString("x", CultureInfo.InvariantCulture));
        if (Keep.Count > 0) sb.Append(";k=").Append(string.Join(',', Keep.Take(MaxKeep).Select(p => p.ToString(CultureInfo.InvariantCulture))));
        if (CloseMemReduct) sb.Append(";x=memreduct");
        return sb.ToString();
    }

    public static CleanSpec? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength) return null;
        CleanAreas? areas = null;
        var keep = new List<int>();
        bool close = false;
        foreach (var part in text.Split(';'))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) return null;
            var (key, value) = (part[..eq], part[(eq + 1)..]);
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
                case "x" when value == "memreduct" && !close:
                    close = true;
                    break;
                default:
                    return null;
            }
        }
        return areas is { } a ? new CleanSpec(a, keep, close) : null;
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
