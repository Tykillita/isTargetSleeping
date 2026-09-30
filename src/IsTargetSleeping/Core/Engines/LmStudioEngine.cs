using System.Diagnostics;
using System.Text.Json.Nodes;

namespace IsTargetSleeping;

/// LM Studio, experimental (no probado en este PC): su servidor local en el
/// puerto 1234, la API REST /api/v0/models (con el estado «loaded» de cada modelo)
/// y la herramienta `lms` para arrancar y parar el servidor y descargar modelos.
/// No hay un proceso por modelo: la inactividad se mide por las apps conectadas
/// al puerto y por los cambios de modelo.
public sealed class LmStudioEngine : EngineBase
{
    public override string Id => "lmstudio";
    public override string Name => "LM Studio";
    public override bool Experimental => true;
    public override bool Installed => !IsDemo && Lms is not null;

    private static readonly HttpClient http = new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(1.5) };
    private DateTime? transitionSince;

    public LmStudioEngine() { Port = 1234; }

    private static string? Lms
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new[]
            {
                Path.Combine(home, ".lmstudio", "bin", "lms.exe"),
                Path.Combine(home, ".cache", "lm-studio", "bin", "lms.exe"),
            }.FirstOrDefault(File.Exists);
        }
    }

    protected override async Task Probe()
    {
        if (Lms is null) { Power = Power.Off; return; }
        JsonNode? models = null;
        try
        {
            using var resp = await http.GetAsync($"http://127.0.0.1:{Port}/api/v0/models");
            if (resp.IsSuccessStatusCode) models = JsonNode.Parse(await resp.Content.ReadAsStringAsync());
        }
        catch { }

        if (models is null)
        {
            Power = transitionSince is { } t && DateTime.Now - t < TimeSpan.FromSeconds(30) && Power == Power.Starting ? Power.Starting : Power.Off;
            Model = null;
            Memory = 0;
            return;
        }
        transitionSince = null;
        Power = Power.On;
        var loaded = (models["data"] as JsonArray)?.OfType<JsonObject>()
            .Where(m => m["state"]?.GetValue<string>() == "loaded")
            .Select(m => m["id"]?.GetValue<string>())
            .OfType<string>()
            .ToList() ?? [];
        Model = loaded.Count switch { 0 => null, 1 => loaded[0], _ => $"{loaded[0]} +{loaded.Count - 1}" };

        // Uso: cambios de modelo y apps conectadas al puerto (sin contar a LM Studio).
        tracker.Sample([], [.. loaded]);
        var clients = await Task.Run(() => TcpClients.Connected(Port).Select(AppIdentity.Of).OfType<ClientApp>().Any());
        if (clients) tracker.Touch();
    }

    public override Task SetOn(bool on) => Run(async () =>
    {
        transitionSince = DateTime.Now;
        Power = on ? Power.Starting : Power.Stopping;
        Notify();
        return await RunLms(on ? "server start" : "server stop");
    });

    public override Task Sleep() => Run(() => RunLms("unload --all"));

    private static Task<string?> RunLms(string args) => Task.Run(() =>
    {
        if (Lms is not { } lms) return "lms.exe no encontrado";
        try
        {
            using var p = Process.Start(new ProcessStartInfo(lms, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (p is null) return "lms no arrancó";
            var err = p.StandardError.ReadToEndAsync();
            p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(60_000)) { try { p.Kill(); } catch { } return "lms no respondió en 60 s"; }
            return p.ExitCode == 0 ? null : $"lms {args}: {err.Result.Trim()}";
        }
        catch (Exception e) { return (string?)e.Message; }
    });

    public void LoadDemo()
    {
        IsDemo = true;
        Power = Power.Off;
    }
}
