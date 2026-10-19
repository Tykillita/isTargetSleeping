namespace IsTargetSleeping;

/// <summary>Reglas puras para sesiones de audio y niveles recordados por ejecutable.</summary>
public static class AudioMixer
{
    public static string NormalizeExecutable(string? path) => Path.GetFileName(path?.Trim())?.ToLowerInvariant() ?? "";

    public static List<string> Remember(IEnumerable<string>? stored, string? executable, double level)
    {
        var key = NormalizeExecutable(executable);
        if (key.Length == 0) return stored?.ToList() ?? [];
        var values = (stored ?? []).Select(Parse).Where(x => x is not null).Select(x => x!.Value)
            .Where(x => !x.Executable.Equals(key, StringComparison.OrdinalIgnoreCase)).ToList();
        values.Add((key, Math.Clamp(level, 0, 1)));
        return values.OrderBy(x => x.Executable, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"{x.Executable}|{x.Level.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}").ToList();
    }

    public static double? Remembered(IEnumerable<string>? stored, string? executable)
    {
        var key = NormalizeExecutable(executable);
        foreach (var item in stored ?? [])
            if (Parse(item) is { } value && value.Executable.Equals(key, StringComparison.OrdinalIgnoreCase)) return value.Level;
        return null;
    }

    private static (string Executable, double Level)? Parse(string? item)
    {
        var parts = item?.Split('|', 2);
        return parts is { Length: 2 } && parts[0].Length > 0
            && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n)
            ? (parts[0].ToLowerInvariant(), Math.Clamp(n, 0, 1)) : null;
    }
}
