using System.Text.Json.Nodes;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping;

/// Un `llama-server.exe` de llama.cpp lanzado por ti (no los runners de Ollama,
/// que son hijos de `ollama.exe`). Sirve el modelo con el que arrancó y no sabe
/// descargarlo sin cerrarse: «dormir» es parar el proceso y recordar su línea de
/// comandos para relanzarlo exactamente igual.
public sealed class LlamaCppEngine : EngineBase
{
    public override string Id => "llamacpp";
    public override string Name => "llama.cpp";
    public override bool Installed => pid is not null || remembered is not null;
    public override string? LogPath => File.Exists(Log) ? Log : null;

    private static string Log => Path.Combine(Paths.LogsDir, "llama-server.log");
    private const string CommandKey = "llamacppCommand";

    private int? pid;
    private (string Exe, string Args)? remembered;
    private string host = "127.0.0.1";
    private DateTime? startedAt;

    private static readonly HttpClient http = new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(1.5) };

    public LlamaCppEngine()
    {
        Port = 8080;
        var saved = Defaults.GetList(CommandKey);
        if (saved.Count == 2 && File.Exists(saved[0])) remembered = (saved[0], saved[1]);
    }

    /// El primer llama-server propio (el de PID más bajo, para no saltar entre varios).
    /// Uno que está cerrándose sigue en la lista un momento sin línea de comandos: no
    /// cuenta (si no, se recordaría una línea vacía).
    private static (int Pid, string CommandLine)? Find() =>
        Procs.ByName("llama-server")
            .Where(p => p.CommandLine.Length > 0 && Procs.Alive(p.Pid) && !ProcessCpu.IsOllamaChild(p.Pid, p.CommandLine))
            .OrderBy(p => p.Pid)
            .Select(p => ((int, string)?)(p.Pid, p.CommandLine))
            .FirstOrDefault();

    protected override async Task Probe()
    {
        var found = await Task.Run(Find);
        if (found is not { } f)
        {
            pid = null;
            Model = null;
            Memory = 0;
            // Arrancando: el proceso puede tardar un momento en aparecer.
            if (startedAt is { } s && DateTime.Now - s < TimeSpan.FromSeconds(10)) { Power = Power.Starting; return; }
            startedAt = null;
            Power = Power.Off;
            return;
        }

        pid = f.Pid;
        var (exe, args) = Procs.SplitCommandLine(f.CommandLine);
        exe = Procs.ImagePath(f.Pid) ?? exe;
        if (File.Exists(exe) && remembered != (exe, args))
        {
            remembered = (exe, args);
            Defaults.SetList(CommandKey, [exe, args]);
        }
        Port = int.TryParse(Procs.Option(f.CommandLine, "--port"), out var port) ? port : 8080;
        var h = Procs.Option(f.CommandLine, "--host") ?? "127.0.0.1";
        host = h is "0.0.0.0" or "::" or "localhost" ? "127.0.0.1" : h;

        int status = await Status("health");
        if (status == 200)
        {
            Power = Power.On;
            startedAt = null;
        }
        else Power = Power.Starting;   // 503 mientras carga el modelo, o aún sin responder

        var alias = Procs.Option(f.CommandLine, "-a", "--alias");
        string? modelPath = null;
        if (Power == Power.On && await Json("props") is JsonObject props)
            modelPath = props["model_path"]?.GetValue<string>();
        modelPath ??= Procs.Option(f.CommandLine, "-m", "--model");
        Model = alias ?? (modelPath is null ? null : OllamaBlobs.NameFor(modelPath) ?? Path.GetFileNameWithoutExtension(modelPath));

        var (cpu, memory) = await Task.Run(() => (Procs.CpuSeconds(f.Pid), (Procs.PrivateBytes(f.Pid) ?? 0) + Gpu.ProcessUsage(f.Pid)));
        Memory = memory;
        if (cpu is { } seconds) tracker.Sample(new() { [f.Pid] = seconds }, Model is null ? [] : [Model]);

        // /slots dice si está generando aunque el CPU no se mueva (todo en la GPU).
        if (await Json("slots") is JsonArray slots && slots.OfType<JsonObject>().Any(s => s["is_processing"]?.GetValue<bool>() == true))
            tracker.Touch();
    }

    private async Task<int> Status(string path)
    {
        try
        {
            using var resp = await http.GetAsync($"http://{host}:{Port}/{path}");
            return (int)resp.StatusCode;
        }
        catch { return 0; }
    }

    private async Task<JsonNode?> Json(string path)
    {
        try
        {
            using var resp = await http.GetAsync($"http://{host}:{Port}/{path}");
            return resp.IsSuccessStatusCode ? JsonNode.Parse(await resp.Content.ReadAsStringAsync()) : null;
        }
        catch { return null; }
    }

    public override Task SetOn(bool on) => on ? Start() : Sleep();

    private Task Start() => Run(async () =>
    {
        if (pid is not null) return null;
        if (remembered is not { } cmd) return tr("No sé cómo arrancarlo: lanza llama-server una vez y lo recordaré.");
        var failure = await Task.Run(() => Switch.StartDetached(cmd.Exe, cmd.Args, Log));
        if (failure is null) { startedAt = DateTime.Now; Power = Power.Starting; }
        return failure;
    });

    public override Task Sleep() => Run(async () =>
    {
        if (pid is not { } p) return null;
        await Task.Run(() => Procs.KillTree(p));
        for (int i = 0; i < 20 && Procs.Alive(p); i++) await Task.Delay(100);
        startedAt = null;
        return null;
    });

    /// Olvida la línea de comandos recordada (y la tarjeta desaparece si no está en marcha).
    public void Forget()
    {
        remembered = null;
        Defaults.Remove(CommandKey);
        Notify();
    }

    public void LoadDemo()
    {
        IsDemo = true;
        Power = Power.On;
        pid = 1;
        Model = "qwen2.5-coder:1.5b";
        Memory = 1_900_000_000;
        IdleSeconds = 4 * 60 + 30;
    }
}

/// Los modelos de Ollama son blobs «sha256-…»: su nombre está en los manifiestos.
public static class OllamaBlobs
{
    private static Dictionary<string, string>? names;
    private static DateTime builtAt;

    /// OLLAMA_MODELS (del proceso o de las variables del usuario) o la carpeta de siempre.
    private static string ModelsDir
    {
        get
        {
            var dir = Environment.GetEnvironmentVariable("OLLAMA_MODELS");
            if (string.IsNullOrWhiteSpace(dir))
            {
                try { dir = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Environment")?.GetValue("OLLAMA_MODELS") as string; } catch { }
            }
            return string.IsNullOrWhiteSpace(dir)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ollama", "models")
                : dir;
        }
    }

    public static string? NameFor(string blobPath)
    {
        var file = Path.GetFileName(blobPath);
        if (!file.StartsWith("sha256-", StringComparison.OrdinalIgnoreCase)) return null;
        if (names is null || DateTime.Now - builtAt > TimeSpan.FromMinutes(5)) Build();
        return names!.TryGetValue(file.Replace('-', ':'), out var name) ? name : null;
    }

    private static void Build()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var root = Path.Combine(ModelsDir, "manifests");
        try
        {
            foreach (var manifest in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var layers = JsonNode.Parse(File.ReadAllText(manifest))?["layers"] as JsonArray;
                    var model = layers?.OfType<JsonObject>().FirstOrDefault(l => l["mediaType"]?.GetValue<string>() == "application/vnd.ollama.image.model");
                    if (model?["digest"]?.GetValue<string>() is not { } digest) continue;
                    // manifests\registry.ollama.ai\library\gemma3\4b → gemma3:4b
                    var parts = Path.GetRelativePath(root, manifest).Split(Path.DirectorySeparatorChar);
                    if (parts.Length < 3) continue;
                    var tag = parts[^1];
                    var repo = string.Join('/', parts[1..^1]);
                    if (parts[0] == "registry.ollama.ai" && repo.StartsWith("library/")) repo = repo["library/".Length..];
                    else if (parts[0] != "registry.ollama.ai") repo = $"{parts[0]}/{repo}";
                    map.TryAdd(digest, $"{repo}:{tag}");
                }
                catch { }
            }
        }
        catch { }
        names = map;
        builtAt = DateTime.Now;
    }
}
