using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IsTargetSleeping.Pet;

/// Emparejado de la web movil: `pair` escribe un codigo de un solo uso (solo su hash) en `pair.json`; el servidor lo
/// comprueba al canjearlo. Caduca a los 5 minutos y se anula tras 5 intentos fallidos (40 bits no se adivinan en 5 tiros).
public sealed class PairingStore(string path, Func<DateTimeOffset>? clock = null)
{
    public const int MaxFails = 5;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";  // sin 0/O/1/I
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly object gate = new();
    private int fails;

    public string NewCode() => NewPairing().Code;

    /// Un código nuevo y un «nonce» secreto: con él, y solo desde esta PC, la página de emparejamiento pregunta si el teléfono ya se conectó.
    public (string Code, string Nonce) NewPairing()
    {
        string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var chars = new char[8];
        for (int i = 0; i < chars.Length; i++) chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        string code = new(chars);
        lock (gate)
        {
            fails = 0;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { sha256 = Hash(code), until = (now() + Lifetime).ToUnixTimeSeconds(), nonce, used = false }));
        }
        return (code[..4] + "-" + code[4..], nonce);
    }

    /// "waiting", "used" o "expired" para ese nonce; null si no corresponde al último código emitido.
    public string? Status(string? nonce)
    {
        lock (gate)
            try
            {
                if (nonce is not { Length: 32 } || !File.Exists(path)) return null;
                var root = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
                if (!root.TryGetProperty("nonce", out var nv) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(nv.GetString() ?? ""), Encoding.UTF8.GetBytes(nonce))) return null;
                if (root.TryGetProperty("used", out var uv) && uv.ValueKind == JsonValueKind.True) return "used";
                return now().ToUnixTimeSeconds() > root.GetProperty("until").GetInt64() ? "expired" : "waiting";
            }
            catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException) { return null; }
    }

    /// true una sola vez por codigo valido.
    public bool Redeem(string? input)
    {
        lock (gate)
        {
            try
            {
                if (!File.Exists(path)) return false;
                var root = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
                if (root.TryGetProperty("used", out var usedV) && usedV.ValueKind == JsonValueKind.True) return false;   // ya se usó: un solo uso
                if (now().ToUnixTimeSeconds() > root.GetProperty("until").GetInt64()) { File.Delete(path); return false; }
                string clean = new((input ?? "").Where(char.IsLetterOrDigit).ToArray());
                bool ok = clean.Length == 8 && CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(Hash(clean)), Convert.FromHexString(root.GetProperty("sha256").GetString() ?? ""));
                if (ok) File.WriteAllText(path, JsonSerializer.Serialize(new { sha256 = root.GetProperty("sha256").GetString(), until = root.GetProperty("until").GetInt64(), nonce = root.TryGetProperty("nonce", out var nn) ? nn.GetString() : "", used = true }));
                else if (++fails >= MaxFails) File.Delete(path);
                return ok;
            }
            catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or FormatException) { return false; }
        }
    }

    private static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.ToUpperInvariant())));
}

/// Sesiones de la web movil en memoria: cookie opaca + token CSRF por sesion. Reiniciar el servidor las cierra (hay que volver a emparejar).
public sealed class SessionStore(Func<DateTimeOffset>? clock = null)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);
    public const int Max = 16;
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly ConcurrentDictionary<string, (string Csrf, DateTimeOffset Until)> sessions = new();

    public (string Id, string Csrf) Create()
    {
        var t = now();
        foreach (var kv in sessions.Where(k => k.Value.Until <= t).ToList()) sessions.TryRemove(kv.Key, out _);
        while (sessions.Count >= Max && sessions.OrderBy(k => k.Value.Until).FirstOrDefault().Key is { } oldest) sessions.TryRemove(oldest, out _);
        string id = Token(), csrf = Token();
        sessions[id] = (csrf, t + Lifetime);
        return (id, csrf);
    }

    public string? Csrf(string? id) =>
        id is not null && sessions.TryGetValue(id, out var s) && s.Until > now() ? s.Csrf : null;

    public void Remove(string? id) { if (id is not null) sessions.TryRemove(id, out _); }

    private static string Token() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
