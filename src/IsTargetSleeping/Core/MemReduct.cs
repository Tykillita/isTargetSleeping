using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Win32;

namespace IsTargetSleeping;

/// Lo que interesa de la configuración de Mem Reduct.
public sealed record MemReductConfig(bool AutoEnabled, int AutoPercent, bool IntervalEnabled, int IntervalMinutes,
    CleanAreas Areas, bool NotifyResults);

/// Mem Reduct (Henry++): detectarlo, importar su configuración y reemplazarlo —
/// quitarlo del inicio y apagar su limpieza automática— o volver a él.
public static class MemReduct
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Mem Reduct";
    private const string SavedRunKey = "memReductRun";
    private const string SavedIniKey = "memReductIni";
    public const string ReplacedKey = "memReductReplaced";

    public static string IniPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Henry++", "Mem Reduct", "memreduct.ini");

    public static string? Exe => new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Mem Reduct", "memreduct.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Mem Reduct", "memreduct.exe"),
    }.FirstOrDefault(File.Exists);

    public static bool Installed => Exe is not null || File.Exists(IniPath);

    public static bool Running
    {
        get
        {
            var list = Process.GetProcessesByName("memreduct");
            foreach (var p in list) p.Dispose();
            return list.Length > 0;
        }
    }

    public static bool StartsAtLogin
    {
        get
        {
            try { using var run = Registry.CurrentUser.OpenSubKey(RunKey); return run?.GetValue(RunValue) is string; }
            catch { return false; }
        }
    }

    public static bool Replaced => Defaults.GetBool(ReplacedKey);

    public static MemReductConfig? Config()
    {
        try { return File.Exists(IniPath) ? ParseIni(File.ReadAllText(IniPath)) : null; } catch { return null; }
    }

    // MARK: ini (lógica pura)

    /// Valores de la sección [memreduct]. Lo que falta toma los valores por defecto de Mem Reduct.
    public static MemReductConfig ParseIni(string text)
    {
        var values = Section(text, "memreduct");
        bool Bool(string key, bool fallback) => values.TryGetValue(key, out var v) ? v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1" : fallback;
        int Int(string key, int fallback) => values.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;
        var mask = Int("ReductMask2", (int)CleanAreas.Default) & (int)CleanAreas.All;
        return new MemReductConfig(
            Bool("AutoreductEnable", false), Math.Clamp(Int("AutoreductValue", 90), 1, 99),
            Bool("AutoreductIntervalEnable", false), Math.Clamp(Int("AutoreductIntervalValue", 30), 1, 1440),
            (CleanAreas)mask, Bool("BalloonCleanResults", true));
    }

    private static Dictionary<string, string> Section(string text, string name)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool inside = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { inside = line.Equals($"[{name}]", StringComparison.OrdinalIgnoreCase); continue; }
            int eq = line.IndexOf('=');
            if (inside && eq > 0) values[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return values;
    }

    /// Cambia (o añade) claves de la sección [memreduct] sin tocar el resto del archivo.
    public static string SetIniValues(string text, IReadOnlyDictionary<string, string> changes)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        var pending = new Dictionary<string, string>(changes, StringComparer.OrdinalIgnoreCase);
        int sectionStart = -1, sectionEnd = lines.Count;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith('['))
            {
                if (sectionStart >= 0) { sectionEnd = i; break; }
                if (line.Equals("[memreduct]", StringComparison.OrdinalIgnoreCase)) sectionStart = i;
                continue;
            }
            int eq = line.IndexOf('=');
            if (sectionStart >= 0 && eq > 0 && pending.Remove(line[..eq].Trim(), out var value))
                lines[i] = $"{line[..eq].Trim()}={value}";
        }
        if (sectionStart < 0)
        {
            if (lines.Count > 0 && lines[^1].Length > 0) lines.Add("");
            lines.Add("[memreduct]");
            sectionEnd = lines.Count;
        }
        else
        {
            // Tras la última línea con contenido de la sección, no tras las vacías.
            while (sectionEnd > sectionStart + 1 && lines[sectionEnd - 1].Trim().Length == 0) sectionEnd--;
        }
        lines.InsertRange(sectionEnd, pending.Select(p => $"{p.Key}={p.Value}"));
        return string.Join("\r\n", lines);
    }

    // MARK: reemplazar y volver

    /// Lo quita del inicio de sesión y apaga su limpieza automática (recordando cómo
    /// estaba). Cerrar el proceso, que corre como administrador, lo hace el agente.
    public static void Replace()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (run?.GetValue(RunValue) is string command)
            {
                Defaults.Set(SavedRunKey, command);
                run.DeleteValue(RunValue, throwOnMissingValue: false);
            }
        }
        catch { }
        try
        {
            if (File.Exists(IniPath))
            {
                var text = File.ReadAllText(IniPath);
                var config = ParseIni(text);
                Defaults.Set(SavedIniKey, $"{config.AutoEnabled}|{config.IntervalEnabled}");
                File.WriteAllText(IniPath, SetIniValues(text, new Dictionary<string, string>
                {
                    ["AutoreductEnable"] = "false",
                    ["AutoreductIntervalEnable"] = "false",
                }), IsUtf16(IniPath) ? Encoding.Unicode : new UTF8Encoding(false));
            }
        }
        catch { }
        Defaults.Set(ReplacedKey, true);
    }

    /// Vuelve a dejarlo como estaba: en el inicio de sesión y con su limpieza automática.
    public static void Restore()
    {
        try
        {
            if (Defaults.GetString(SavedRunKey) is { } command)
            {
                using var run = Registry.CurrentUser.CreateSubKey(RunKey);
                run.SetValue(RunValue, command);
            }
        }
        catch { }
        try
        {
            if (File.Exists(IniPath) && Defaults.GetString(SavedIniKey) is { } saved && saved.Split('|') is [var auto, var interval])
            {
                File.WriteAllText(IniPath, SetIniValues(File.ReadAllText(IniPath), new Dictionary<string, string>
                {
                    ["AutoreductEnable"] = auto.ToLowerInvariant(),
                    ["AutoreductIntervalEnable"] = interval.ToLowerInvariant(),
                }), IsUtf16(IniPath) ? Encoding.Unicode : new UTF8Encoding(false));
            }
        }
        catch { }
        Defaults.Remove(ReplacedKey);
        Defaults.Remove(SavedRunKey);
        Defaults.Remove(SavedIniKey);
    }

    private static bool IsUtf16(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            return f.ReadByte() == 0xFF && f.ReadByte() == 0xFE;
        }
        catch { return false; }
    }
}
