using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace IsTargetSleeping.Pet;

/// Tokens de dispositivo: solo se guarda el SHA-256 (nunca el token). Se comparan en tiempo constante.
public sealed class TokenStore
{
    private readonly string path;
    private readonly object gate = new();
    private readonly List<(string Label, byte[] Hash)> tokens = [];

    public TokenStore(string path)
    {
        this.path = path;
        try
        {
            if (!File.Exists(path)) return;
            var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var t in doc.RootElement.GetProperty("tokens").EnumerateArray())
                tokens.Add((t.GetProperty("label").GetString() ?? "", Convert.FromHexString(t.GetProperty("sha256").GetString() ?? "")));
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or FormatException) { tokens.Clear(); }
    }

    public int Count { get { lock (gate) return tokens.Count; } }

    /// Crea un token nuevo (43 caracteres base64url) y devuelve el texto una sola vez.
    public string Issue(string label)
    {
        string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        lock (gate)
        {
            tokens.Add((label, SHA256.HashData(Encoding.UTF8.GetBytes(token))));
            Save();
        }
        return token;
    }

    public bool Validate(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 128) return false;
        byte[] h = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        bool ok = false;
        lock (gate)
            foreach (var (_, hash) in tokens) ok |= CryptographicOperations.FixedTimeEquals(h, hash);  // sin cortocircuito
        return ok;
    }

    public int Revoke(string label)
    {
        lock (gate)
        {
            int n = tokens.RemoveAll(t => t.Label == label);
            if (n > 0) Save();
            return n;
        }
    }

    /// Extrae el token de "Authorization: Bearer xxx".
    public static string? FromHeader(string? header) =>
        header is not null && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : null;

    private void Save()
    {
        var obj = new { tokens = tokens.Select(t => new { label = t.Label, sha256 = Convert.ToHexString(t.Hash).ToLowerInvariant() }) };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(obj));
    }
}

/// CA propia del modulo y certificado de servidor para la LAN (ECDSA P-256, lo que sabe validar mbedTLS en el ESP32).
public static class Pki
{
    public static X509Certificate2 EnsureCa(string dir, DateTimeOffset now)
    {
        string path = Path.Combine(dir, "ca.pfx");
        if (File.Exists(path))
        {
            try { return X509CertificateLoader.LoadPkcs12FromFile(path, null, X509KeyStorageFlags.Exportable); }
            catch (CryptographicException) { /* archivo danado: se regenera */ }
        }
        Directory.CreateDirectory(dir);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest("CN=isTargetSleeping Mascota CA", key, HashAlgorithmName.SHA256);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
        using var self = req.CreateSelfSigned(now.AddDays(-1), now.AddYears(10));
        byte[] pfx = self.Export(X509ContentType.Pfx);
        File.WriteAllBytes(path, pfx);
        return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.Exportable);
    }

    /// Certificado de servidor firmado por la CA; `hosts` pueden ser IP o nombres (van al SAN).
    public static X509Certificate2 IssueServer(X509Certificate2 ca, IEnumerable<string> hosts, DateTimeOffset now)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest("CN=isTargetSleeping Mascota", key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        int n = 0;
        foreach (var h in hosts.Where(h => !string.IsNullOrWhiteSpace(h)))
        {
            if (IPAddress.TryParse(h, out var ip))
            {
                san.AddIpAddress(ip);
                // mbedTLS 2.x (ESP32) solo compara el nombre del servidor con los SAN de tipo DNS e ignora los de IP:
                // sin esta entrada la placa rechaza el certificado (CN_MISMATCH) al conectar por IP.
                san.AddDnsName(h);
            }
            else san.AddDnsName(h);
            n++;
        }
        if (n == 0) throw new ArgumentException("el certificado necesita al menos un host o IP", nameof(hosts));
        req.CertificateExtensions.Add(san.Build());
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        byte[] serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F;
        using var signed = req.Create(ca, now.AddDays(-1), now.AddDays(825), serial);
        using var withKey = signed.CopyWithPrivateKey(key);
        return X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
    }

    public static string ToPem(X509Certificate2 cert) => cert.ExportCertificatePem();

    /// Valida un certificado de servidor contra una CA concreta (no contra el almacen de Windows).
    public static bool ChainsTo(X509Certificate2 server, X509Certificate2 ca, DateTimeOffset now)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(ca);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationTime = now.UtcDateTime;
        return chain.Build(server);
    }
}
