using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IsTargetSleeping;

/// Nombre, firma y versión en un solo sitio.
public static class AppInfo
{
    public const string Name = "isTargetSleeping";
    /// CodeSentry es la empresa de desarrollo y ciberseguridad de Ruben Pino (Tykillita).
    public const string Brand = "CodeSentry - Tykillita";
    /// La firma del pie del panel, de «Acerca de» y del README.
    public const string Signature = "By " + Brand;

    /// Proyecto en cuya idea se basa (créditos de «Acerca de»).
    public const string CreditsUrl = "https://github.com/eriktaveras/modelnap";

    public static string Version { get; } =
        (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev")
        .Split('+')[0];

    /// Repositorio de GitHub («usuario/repo») donde se publican las versiones. Vacío:
    /// no se buscan actualizaciones y la opción no aparece.
    public static string UpdateRepo => Defaults.GetString("updateRepo")?.Trim() ?? "";

    /// Solo para pruebas: otra URL base en lugar de https://api.github.com.
    public static string UpdateApi => Defaults.GetString("updateApi") is { Length: > 0 } api ? api.TrimEnd('/') : "https://api.github.com";
}

public static class Paths
{
    /// %LOCALAPPDATA%\isTargetSleeping: ajustes y logs propios.
    public static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name);

    public static string LogsDir => Path.Combine(DataDir, "Logs");
    public static string OwnLog => Path.Combine(LogsDir, "ollama.log");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string StatsFile => Path.Combine(DataDir, "stats.json");

    /// %LOCALAPPDATA%\Ollama: logs de la app oficial de Ollama para Windows.
    public static string OllamaAppData =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ollama");

    public const string Download = "https://ollama.com/download/windows";

    public static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
    }

    /// Abre el log con el visor predeterminado de .log, o con el Bloc de notas si
    /// no hay ninguno asociado.
    public static void OpenLog(string? path)
    {
        if (path is null || !File.Exists(path)) return;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { try { Process.Start("notepad.exe", $"\"{path}\""); } catch { } }
    }
}

/// Registro propio de la app (Logs\app.log): avisos, reinicios del vigilante,
/// modo juego… para poder saber después por qué pasó algo. Se recorta a ~1 MB.
public static class AppLog
{
    private static readonly object gate = new();
    public static string File => Path.Combine(Paths.LogsDir, "app.log");

    public static void Write(string message)
    {
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(Paths.LogsDir);
                var info = new FileInfo(File);
                if (info.Exists && info.Length > 1_000_000) System.IO.File.Move(File, File + ".1", overwrite: true);
                System.IO.File.AppendAllText(File, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}

/// Ajustes persistentes, en un JSON pequeño.
public static class Defaults
{
    private static readonly object gate = new();
    private static JsonObject? data;

    private static JsonObject Data
    {
        get
        {
            if (data is not null) return data;
            try { data = JsonNode.Parse(File.ReadAllText(Paths.SettingsFile)) as JsonObject; } catch { }
            return data ??= new JsonObject();
        }
    }

    public static bool Has(string key) { lock (gate) return Data.ContainsKey(key); }

    public static int GetInt(string key, int fallback = 0)
    {
        lock (gate) { try { return Data[key]?.GetValue<int>() ?? fallback; } catch { return fallback; } }
    }

    public static bool GetBool(string key, bool fallback = false)
    {
        lock (gate) { try { return Data[key]?.GetValue<bool>() ?? fallback; } catch { return fallback; } }
    }

    public static string? GetString(string key)
    {
        lock (gate) { try { return Data[key]?.GetValue<string>(); } catch { return null; } }
    }

    public static List<string> GetList(string key)
    {
        lock (gate)
        {
            try { return (Data[key] as JsonArray)?.Select(n => n?.GetValue<string>()).OfType<string>().ToList() ?? []; }
            catch { return []; }
        }
    }

    public static void SetList(string key, IEnumerable<string> values) =>
        Put(key, new JsonArray(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray()));

    public static void Set(string key, int value) => Put(key, JsonValue.Create(value));
    public static void Set(string key, bool value) => Put(key, JsonValue.Create(value));
    public static void Set(string key, string value) => Put(key, JsonValue.Create(value));

    public static void Remove(string key)
    {
        lock (gate) { if (Data.Remove(key)) Save(); }
    }

    private static void Put(string key, JsonNode? value)
    {
        lock (gate) { Data[key] = value; Save(); }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Paths.DataDir);
            var tmp = Paths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, Data.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, Paths.SettingsFile, overwrite: true);
        }
        catch { }
    }
}
