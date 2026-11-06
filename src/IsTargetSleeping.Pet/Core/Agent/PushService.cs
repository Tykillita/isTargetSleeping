using System.Text.Json;
using System.Text.Json.Nodes;
using WebPush;

namespace IsTargetSleeping.Pet;

/// Notificaciones push al teléfono (Web Push + VAPID): llegan aunque la web esté cerrada o el teléfono bloqueado.
/// En iPhone (iOS 16.4+) solo funcionan con la web añadida a la pantalla de inicio. Las claves VAPID y las suscripciones viven en `push.json`.
public sealed class PushService
{
    public const int MaxSubscriptions = 5;
    // Un endpoint de suscripción es una URL a la que ESTE servidor hace POST: solo se aceptan los servicios de push conocidos.
    private static readonly string[] AllowedHosts = ["fcm.googleapis.com", "updates.push.services.mozilla.com", "web.push.apple.com", "push.apple.com", "notify.windows.com", "push.services.mozilla.com"];

    public sealed record Sub(string Endpoint, string P256dh, string Auth);

    private readonly string path;
    private readonly object gate = new();
    private readonly List<Sub> subs = [];
    private readonly VapidDetails vapid;
    private readonly WebPushClient client = new();

    public PushService(string path)
    {
        this.path = path;
        string pub = "", priv = "";
        try
        {
            if (File.Exists(path))
            {
                var root = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
                pub = root.GetProperty("public").GetString() ?? "";
                priv = root.GetProperty("private").GetString() ?? "";
                foreach (var s in root.GetProperty("subs").EnumerateArray())
                    subs.Add(new Sub(s.GetProperty("endpoint").GetString()!, s.GetProperty("p256dh").GetString()!, s.GetProperty("auth").GetString()!));
            }
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { subs.Clear(); pub = priv = ""; }
        if (pub.Length == 0 || priv.Length == 0)
        {
            var keys = VapidHelper.GenerateVapidKeys();
            pub = keys.PublicKey;
            priv = keys.PrivateKey;
        }
        // Apple rechaza (403 BadJwtToken) un «sub» con dominio inventado o local: tiene que parecer un contacto real. No se envia ningun dato personal.
        vapid = new VapidDetails("mailto:nimbo@example.com", pub, priv);
        Save();
    }

    public string PublicKey => vapid.PublicKey;
    public int Count { get { lock (gate) return subs.Count; } }

    public static bool IsAllowedEndpoint(string? endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && endpoint!.Length <= 600
        && AllowedHosts.Any(h => u.Host == h || u.Host.EndsWith("." + h, StringComparison.Ordinal));

    public bool Add(string endpoint, string p256dh, string auth)
    {
        if (!IsAllowedEndpoint(endpoint) || p256dh.Length is 0 or > 200 || auth.Length is 0 or > 100) return false;
        lock (gate)
        {
            subs.RemoveAll(s => s.Endpoint == endpoint);
            subs.Add(new Sub(endpoint, p256dh, auth));
            while (subs.Count > MaxSubscriptions) subs.RemoveAt(0);
            Save();
        }
        return true;
    }

    public void Remove(string endpoint) { lock (gate) { subs.RemoveAll(s => s.Endpoint == endpoint); Save(); } }

    /// Envia a todas las suscripciones. Las que el servicio da por muertas (404/410) se borran. Nunca lanza.
    public async Task SendAsync(string title, string body, string url = "/", string? tag = null)
    {
        List<Sub> list;
        lock (gate) list = [.. subs];
        if (list.Count == 0) return;
        string payload = new JsonObject { ["title"] = title, ["body"] = body.Length > 160 ? body[..160] : body, ["url"] = url, ["tag"] = tag ?? "nimbo" }.ToJsonString();
        foreach (var s in list)
        {
            string host = new Uri(s.Endpoint).Host;
            try { await client.SendNotificationAsync(new PushSubscription(s.Endpoint, s.P256dh, s.Auth), payload, vapid); Trace($"{host} ok · {title}"); }
            catch (WebPushException e) when ((int)e.StatusCode is 404 or 410) { Trace($"{host} suscripción muerta ({(int)e.StatusCode}): se borra"); Remove(s.Endpoint); }
            catch (WebPushException e) { Trace($"{host} rechazado {(int)e.StatusCode} {e.StatusCode}: {Short(e.Message)}"); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException) { Trace($"{host} sin red: {Short(e.Message)}"); }
        }
    }

    /// Envia ahora y cuenta lo que paso con cada suscripcion (para diagnosticar: `isTargetSleeping-pet push-test`).
    public async Task<IReadOnlyList<string>> TestAsync()
    {
        await SendAsync("Nimbo", "Prueba de notificación", "/", "test");
        string[] lines = File.Exists(LogPath) ? File.ReadAllLines(LogPath) : [];
        return lines.TakeLast(Math.Max(1, Count + 2)).ToList();
    }

    private string LogPath => path + ".log";
    private static string Short(string s) => s.Length > 200 ? s[..200] : s;

    /// Bitacora corta de envios (últimas 200 líneas): sin ella un push que falla no deja rastro.
    private void Trace(string line)
    {
        try
        {
            lock (gate)
            {
                var all = File.Exists(LogPath) ? File.ReadAllLines(LogPath).ToList() : [];
                all.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}");
                File.WriteAllLines(LogPath, all.TakeLast(200));
            }
        }
        catch (IOException) { }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, new JsonObject
            {
                ["public"] = vapid.PublicKey, ["private"] = vapid.PrivateKey,
                ["subs"] = new JsonArray([.. subs.Select(s => (JsonNode?)new JsonObject { ["endpoint"] = s.Endpoint, ["p256dh"] = s.P256dh, ["auth"] = s.Auth })]),
            }.ToJsonString());
            File.Move(tmp, path, overwrite: true);
        }
        catch (IOException) { }
    }
}
