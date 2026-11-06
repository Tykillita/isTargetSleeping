using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace IsTargetSleeping.Pet;

public sealed record NimboProviderSettings(string Provider, string? Model, string Auth, string? ApiKey, string? Project);
public sealed record NimboProviderProfile(string Provider, string Auth, string? Model, string? Project, bool HasApiKey);
public sealed record NimboProviderStatus(NimboProviderSettings Settings, bool HasApiKey, bool Configured, IReadOnlyDictionary<string, NimboProviderProfile> Profiles);
public sealed record NimboProviderSettingsDocument(NimboProviderSettings Active, Dictionary<string, NimboProviderSettings> Profiles);

public sealed class NimboProviderSettingsStore
{
    private readonly string path;
    private readonly IDataProtector protector;

    public NimboProviderSettingsStore(string dataDirectory)
    {
        path = Path.Combine(dataDirectory, "nimbo-provider.bin");
        string keys = Path.Combine(dataDirectory, "nimbo-provider-keys");
        Directory.CreateDirectory(keys);
        var provider = DataProtectionProvider.Create(new DirectoryInfo(keys), b =>
        {
            b.PersistKeysToFileSystem(new DirectoryInfo(keys));
            b.ProtectKeysWithDpapi();
            b.SetApplicationName("isTargetSleeping.Pet");
        });
        protector = provider.CreateProtector("Nimbo.ProviderSettings.v1");
    }

    public NimboProviderSettingsDocument? Load()
    {
        if (!File.Exists(path)) return null;
        try
        {
            string json = protector.Unprotect(File.ReadAllText(path));
            var document = JsonSerializer.Deserialize<NimboProviderSettingsDocument>(json)
                ?? throw new InvalidDataException("La configuración cifrada del proveedor de Nimbo está vacía.");
            var active = NimboProviderManager.Validate(document.Active);
            var profiles = document.Profiles.Values.Select(NimboProviderManager.Validate)
                .ToDictionary(NimboProviderManager.ProfileKey, StringComparer.Ordinal);
            profiles[NimboProviderManager.ProfileKey(active)] = active;
            return new(active, profiles);
        }
        catch (JsonException e) { throw new InvalidDataException("La configuración cifrada del proveedor de Nimbo no es válida.", e); }
    }

    public void Save(NimboProviderSettingsDocument document)
    {
        string temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, protector.Protect(JsonSerializer.Serialize(document)));
            File.Move(temp, path, true);
        }
        finally { try { File.Delete(temp); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
    }
}

public sealed class SwitchableChatBackend(IChatBackend initial) : IChatBackend
{
    private IChatBackend current = initial;
    public void Set(IChatBackend backend) => Volatile.Write(ref current, backend);
    public Task<ChatReply> GenerateAsync(IReadOnlyList<ChatTurn> history, CancellationToken ct) => Volatile.Read(ref current).GenerateAsync(history, ct);
}

public sealed class NimboProviderManager
{
    private readonly object gate = new();
    private readonly NimboProviderSettingsStore store;
    private readonly Func<NimboProviderSettings, IChatBackend> createBackend;
    private readonly SwitchableChatBackend backend;
    private Dictionary<string, NimboProviderSettings> profiles;
    private NimboProviderSettings settings;

    public NimboProviderManager(NimboProviderSettingsStore store, NimboProviderSettings fallback, Func<NimboProviderSettings, IChatBackend> createBackend)
    {
        this.store = store;
        this.createBackend = createBackend;
        var saved = store.Load();
        settings = saved?.Active ?? Validate(fallback);
        profiles = saved?.Profiles ?? new Dictionary<string, NimboProviderSettings>(StringComparer.Ordinal);
        profiles[ProfileKey(settings)] = settings;
        backend = new SwitchableChatBackend(createBackend(settings));
    }

    public IChatBackend Backend => backend;

    public NimboProviderStatus Status
    {
        get
        {
            lock (gate)
            {
                bool hasKey = !string.IsNullOrWhiteSpace(settings.ApiKey);
                bool configured = settings.Provider switch
                {
                    "ollama" => true,
                    "ollama-cloud" when settings.Auth == "ollama_oauth" => ProviderProcess.Find("ollama") is not null,
                    "codex" => ProviderProcess.Find("codex") is not null,
                    "gemini" when settings.Auth == "oauth" => !string.IsNullOrWhiteSpace(settings.Project) && ProviderProcess.Find("gcloud") is not null,
                    _ => hasKey,
                };
                var known = profiles.ToDictionary(x => x.Key, x => new NimboProviderProfile(x.Value.Provider, x.Value.Auth, x.Value.Model, x.Value.Project, !string.IsNullOrWhiteSpace(x.Value.ApiKey)), StringComparer.Ordinal);
                return new(settings, hasKey, configured, known);
            }
        }
    }

    public NimboProviderStatus Configure(string provider, string? model, string auth, string? apiKey, string? project, bool clearApiKey)
    {
        lock (gate)
        {
            string profileKey = ProfileKey(provider, auth);
            profiles.TryGetValue(profileKey, out var previous);
            string? key = clearApiKey ? null : string.IsNullOrWhiteSpace(apiKey) ? previous?.ApiKey : apiKey;
            var next = Validate(new NimboProviderSettings(provider, model, auth, key, project));
            var nextBackend = createBackend(next);
            var nextProfiles = new Dictionary<string, NimboProviderSettings>(profiles, StringComparer.Ordinal) { [profileKey] = next };
            store.Save(new(next, nextProfiles));
            settings = next;
            profiles = nextProfiles;
            backend.Set(nextBackend);
            return Status;
        }
    }

    public void StartLogin(string provider)
    {
        lock (gate)
        {
            bool allowed = provider == settings.Provider && (provider switch
            {
                "codex" => settings.Auth == "oauth",
                "gemini" => settings.Auth == "oauth",
                "ollama-cloud" => settings.Auth == "ollama_oauth",
                _ => false,
            });
            if (!allowed)
                throw new ArgumentException("Selecciona primero el proveedor y el método OAuth correspondiente.");
        }
        ProviderProcess.StartLogin(provider);
    }

    public static NimboProviderSettings Validate(NimboProviderSettings value)
    {
        string provider = (value.Provider ?? "").Trim().ToLowerInvariant();
        string auth = (value.Auth ?? "").Trim().ToLowerInvariant();
        bool valid = provider switch
        {
            "ollama" => auth == "local",
            "codex" => auth == "oauth",
            "gemini" => auth is "api_key" or "oauth",
            "ollama-cloud" => auth is "api_key" or "ollama_oauth",
            "grok" or "anthropic" => auth == "api_key",
            _ => false,
        };
        if (!valid) throw new ArgumentException("Proveedor o método de autenticación no válido.");
        string? model = value.Model?.Trim();
        if (model?.Length > 160 || model?.Any(char.IsControl) == true) throw new ArgumentException("El modelo no puede superar 160 caracteres ni contener controles.");
        string? key = value.ApiKey?.Trim();
        if (key?.Length > 4096 || key?.Any(char.IsControl) == true) throw new ArgumentException("La clave API no puede superar 4096 caracteres ni contener controles.");
        string? project = value.Project?.Trim();
        if (project?.Length > 200 || project?.Any(char.IsControl) == true) throw new ArgumentException("El proyecto no puede superar 200 caracteres ni contener controles.");
        if (provider == "ollama") key = null;
        if (auth is "oauth" or "ollama_oauth") key = null;
        if (provider != "gemini") project = null;
        if (string.IsNullOrEmpty(model)) model = DefaultModel(provider, auth);
        return new(provider, model, auth, key, project);
    }

    public static string? DefaultModel(string provider, string? auth = null) => provider switch
    {
        "ollama" => "gemma4:latest",
        "ollama-cloud" when auth == "ollama_oauth" => "gemma4:cloud",
        "ollama-cloud" => "gemma4:31b",
        "grok" => "grok-4.7",
        "gemini" => "gemini-3.8-flash",
        "anthropic" => "claude-haiku-4-5-20251001",
        _ => null,
    };

    public static string ProfileKey(string provider, string auth) => provider.Trim().ToLowerInvariant() + ":" + auth.Trim().ToLowerInvariant();
    public static string ProfileKey(NimboProviderSettings settings) => ProfileKey(settings.Provider, settings.Auth);
}

public sealed class UnconfiguredChatBackend(string message) : IChatBackend
{
    public Task<ChatReply> GenerateAsync(IReadOnlyList<ChatTurn> history, CancellationToken ct) =>
        Task.FromException<ChatReply>(new ChatBackendException("model_unavailable", message));
}
