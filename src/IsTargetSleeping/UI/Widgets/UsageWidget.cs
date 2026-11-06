using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// Una barra de un proveedor lista para pintar: «5 h ▬▬ 6 % · reinicia 21:10».
public sealed record UsageLimitRow(string Label, double Fraction, string PercentText, string ResetText, DateTimeOffset? ResetsAt = null)
{
    /// «», «warn» (desde el 70 %) o «danger» (desde el 90 %): el número cambia de color como la barra.
    public string Tone => Fraction >= 0.9 ? "danger" : Fraction >= 0.7 ? "warn" : "";
    public bool HasReset => ResetText.Length > 0;
    /// «6 % · reinicia 21:10» en una sola línea, para el diseño estrecho.
    public string Summary => HasReset ? $"{PercentText} · {ResetText}" : PercentText;
}

/// Un proveedor listo para pintar. Con `Problem` no hay cifras: se dice por qué (sesión caducada, sin conexión) en vez de inventarlas.
public sealed class UsageProviderRow(string id, string name, string plan, string updated, IReadOnlyList<UsageLimitRow> limits, string? extra, string? problem)
{
    public string Name => name;
    public string Id => id;
    /// El color de la insignia del proveedor; en alto contraste o con contornos, el del texto.
    public System.Windows.Media.Brush Badge => Theme.Brush(Theme.Flat ? Theme.Current.Fg : id switch
    {
        "claude" => System.Windows.Media.Color.FromRgb(0xD9, 0x77, 0x57),
        "codex" => System.Windows.Media.Color.FromRgb(0x10, 0xA3, 0x7F),
        "gemini" => System.Windows.Media.Color.FromRgb(0x7C, 0x6B, 0xF0),
        _ => Theme.Current.Secondary,
    });
    public System.Windows.Media.Brush OnBadge => Theme.Brush(Theme.Flat ? Theme.Current.Bg : System.Windows.Media.Colors.White);
    public string PrimaryTone => Primary?.Tone ?? "";
    public bool PrimaryAlert => PrimaryTone == "danger";
    public string Plan => plan;
    public bool HasPlan => plan.Length > 0;
    public string Updated => updated;
    public IReadOnlyList<UsageLimitRow> Limits => limits;
    public string Extra => extra ?? "";
    public bool HasExtra => !string.IsNullOrEmpty(extra);
    public string Problem => problem ?? "";
    public bool HasProblem => !string.IsNullOrEmpty(problem);
    public bool HasLimits => limits.Count > 0;
    public string PrimaryValue => HasLimits ? PrimaryPercent : HasProblem ? Problem : Extra;
    /// La barra que manda en el vistazo: la semanal si existe, y si no la primera.
    public UsageLimitRow? Primary => limits.FirstOrDefault(l => l.Label == tr("Semanal")) ?? limits.FirstOrDefault();
    public double PrimaryFraction => Primary?.Fraction ?? 0;
    public string PrimaryPercent => Primary?.PercentText ?? "—";
    /// Lo que acompaña al número grande de la vista de una fila: la otra barra («5 h 6 %»), o el motivo si no hay cifras.
    public string Aside => HasProblem ? Problem : limits.Count > 1 ? $"{limits.First(l => l != Primary).Label} {limits.First(l => l != Primary).PercentText}" : Primary?.ResetText ?? Extra;
    public string GlanceDetail => HasLimits ? "" : HasProblem ? Problem : Extra;
    public bool HasGlanceDetail => GlanceDetail.Length > 0;
}

/// Uso de proveedores con métricas propias: sesiones OAuth locales, el CLI de Muse, gasto de la API Grok y actividad del servidor Ollama local.
public sealed class UsageWidget : WidgetModel
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5), ManualGap = TimeSpan.FromSeconds(30);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private const string CacheKey = "aiUsageCache";

    private readonly Prefs prefs;
    private readonly Dictionary<string, ProviderUsage> last = [];
    private List<(string Provider, string? Problem)> detected = [];
    private DateTime nextRead, lastStart;
    private bool reading, loaded, demo, readFailed, cacheChecked;
    private string? homeOverride;

    public bool Enabled => demo || prefs.Switch(PrefKeys.UsageEnabled, false);
    public IReadOnlyList<UsageProviderRow> Rows { get; private set; } = [];
    public bool HasRows => Rows.Count > 0;
    public bool NoRows => !HasRows;
    public bool Refreshing => reading;
    public string RefreshHelp => tr("Actualizar el uso ahora");
    public string UpdatedText => last.Count == 0 ? "" : tr("actualizado %@", AiUsage.Ago(last.Values.Max(u => u.Updated), DateTime.Now));

    public string EmptyTitle => !Enabled ? tr("Uso de IA desactivado") : !loaded ? tr("Consultando…")
        : readFailed ? tr("No se pudo consultar el uso.") : tr("Sin sesiones de IA");
    public string EmptyText => !Enabled ? tr("Lee sesiones locales de Claude Code, Codex y Gemini; Muse y Ollama local si están disponibles, y Grok API si está configurado. Apagado por defecto.")
        : !loaded ? "" : readFailed ? tr("Comprueba la conexión y vuelve a intentar.")
        : tr("Activa Claude Code, Codex, Gemini, Muse u Ollama local. Grok API requiere XAI_MANAGEMENT_API_KEY y XAI_TEAM_ID.");
    public string EmptyHint => Enabled && loaded && !readFailed ? tr("Muse y Ollama aparecen si están disponibles en local. Grok API muestra gasto del equipo, no el cupo de la suscripción Grok.") : "";
    public bool CanEnable => !Enabled;
    public bool CanRefresh => Enabled;
    public string EnableText => tr("Activar Uso de IA");
    public ICommand EnableCommand { get; }
    private ICommand? refresh;
    public ICommand RefreshCommand => refresh ??= new Command(() => { if (DateTime.Now - lastStart >= ManualGap) nextRead = DateTime.MinValue; Tick(DateTime.Now); });

    public UsageWidget(Prefs prefs) : base(WidgetCatalog.Find("usage")!)
    {
        this.prefs = prefs;
        EnableCommand = new Command(() => prefs.SetSwitch(PrefKeys.UsageEnabled, true));
        if (prefs.Switch(PrefKeys.UsageEnabled, false)) RestoreCache();
    }

    /// Solo para pruebas: otra carpeta personal donde buscar las sesiones.
    public void UseHome(string home) => homeOverride = home;

    /// Solo para la galería y las capturas: datos de ejemplo, sin leer credenciales ni usar la red.
    public void LoadDemo()
    {
        demo = true; loaded = true; readFailed = false;
        var now = DateTime.Now;
        DateTimeOffset At(double hours) => new(now.AddHours(hours));
        last.Clear();
        last["claude"] = new("claude", "Claude", "Max", [new("5h", 6, At(2.2)), new("week", 32, At(52))], "Opus 0 % · Sonnet 14 %", now.AddMinutes(-2));
        last["codex"] = new("codex", "Codex", "Plus", [new("5h", 0, At(4)), new("week", 2, At(98))], null, now.AddMinutes(-2));
        last["gemini"] = new("gemini", "Gemini", "Google AI Pro", [new("gemini-2.5-pro", 78, At(40)), new("gemini-2.5-flash", 0, At(3))], null, now.AddMinutes(-1));
        detected = [("claude", null), ("codex", null), ("gemini", null)];
        Rebuild();
        Raise();
    }

    public override void Tick(DateTime now)
    {
        if (!Enabled)
        {
            PetBridge.ClearUsage();
            if (last.Count > 0 || detected.Count > 0 || loaded) { last.Clear(); detected = []; loaded = false; Rebuild(); Raise(); }
            cacheChecked = false;
            return;
        }
        if (demo) return;
        if (!loaded && last.Count == 0) RestoreCache();
        if (!reading && now >= nextRead) Read(now);
        else if (loaded) { Rebuild(); Raise(); }   // el «hace N min» sigue corriendo
    }

    private void Read(DateTime now)
    {
        reading = true; readFailed = false; lastStart = now; nextRead = now + Interval;
        Raise();
        var home = homeOverride ?? AiUsage.HomeDir;
        _ = Task.Run(async () =>
        {
            async Task<(string Provider, ProviderUsage? Usage, string? Problem)> ReadSession(UsageSession session)
            {
                if (session.Expired) return (session.Provider, null, tr("La sesión caducó: abre %@ para renovarla.", AiUsage.DisplayName(session.Provider)));
                try
                {
                    var usage = await AiUsage.FetchAsync(session, Http);
                    return (session.Provider, usage, usage is null ? tr("No se pudo consultar el uso.") : null);
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    return (session.Provider, null, tr("No se pudo consultar el uso."));
                }
            }
            var providers = AiUsage.Detect(home).Select(ReadSession).ToArray();
            var ollamaTask = AiUsage.FetchOllamaAsync(Http);
            var results = await Task.WhenAll(providers);
            return await ollamaTask is { } ollama
                ? results.Append((Provider: "ollama", Usage: ollama, Problem: (string?)null)).ToArray()
                : results;
        }).ContinueWith(task => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            reading = false;
            if (!Enabled) return;
            loaded = true;
            if (task.Status == TaskStatus.RanToCompletion)
            {
                readFailed = false;
                detected = task.Result.Select(r => (r.Provider, r.Usage is null && !last.ContainsKey(r.Provider) ? r.Problem : null)).ToList();
                foreach (var r in task.Result.Where(r => r.Usage is not null)) last[r.Provider] = r.Usage!;
                foreach (var gone in last.Keys.Where(k => task.Result.All(r => r.Provider != k)).ToList()) last.Remove(gone);   // cerró sesión: desaparece
                SaveCache();
                if (task.Result.Any(r => r.Usage is null)) nextRead = DateTime.Now.AddMinutes(2);
            }
            else { readFailed = true; nextRead = DateTime.Now.AddMinutes(2); }
            Rebuild();
            Raise();
        }));
    }

    private void RestoreCache()
    {
        if (cacheChecked) return;
        cacheChecked = true;
        var json = Defaults.GetString(CacheKey);
        if (json is not { Length: > 0 and <= 262144 }) return;
        try
        {
            foreach (var usage in JsonSerializer.Deserialize<ProviderUsage[]>(json) ?? [])
                if (usage is not null && usage.Id is ("claude" or "codex" or "gemini" or "muse" or "ollama" or "grok-api")
                    && !string.IsNullOrWhiteSpace(usage.Name) && usage.Plan is not null && usage.Limits is { Count: <= 8 }
                    && usage.Limits.All(limit => limit is not null && !string.IsNullOrWhiteSpace(limit.Label) && limit.Label.Length <= 40
                        && double.IsFinite(limit.Percent) && limit.Percent is >= 0 and <= 100))
                    last[usage.Id] = usage;
        }
        catch (JsonException) { Defaults.Remove(CacheKey); }

        if (last.Count == 0) return;
        detected = last.Keys.Select(provider => (Provider: provider, Problem: (string?)null)).ToList();
        loaded = true;
        Rebuild();
    }

    private void SaveCache()
    {
        if (last.Count == 0) { Defaults.Remove(CacheKey); return; }
        Defaults.Set(CacheKey, JsonSerializer.Serialize(last.Values));
    }

    private void Rebuild()
    {
        var now = DateTime.Now;
        UsageLimitRow Limit(UsageLimit l) => new(l.Label switch { "5h" => tr("5 h"), "week" => tr("Semanal"), var m when m.StartsWith("gemini-", StringComparison.Ordinal) => m["gemini-".Length..], _ => l.Label }, Math.Clamp(l.Percent / 100, 0, 1), $"{Math.Round(l.Percent):0} %", AiUsage.Reset(l.ResetsAt, now), l.ResetsAt);
        Rows = detected.Select(d => last.TryGetValue(d.Provider, out var u)
            ? new UsageProviderRow(d.Provider, u.Name, u.Plan, AiUsage.Ago(u.Updated, now), u.Limits.Select(Limit).ToList(), u.Extra, null)
            : new UsageProviderRow(d.Provider, AiUsage.DisplayName(d.Provider), "", "", [], null, d.Problem ?? tr("No se pudo consultar el uso."))).ToList();
        if (Enabled && !demo)
            PetBridge.PublishUsage(detected.Select(d => last.TryGetValue(d.Provider, out var u)
                ? (u.Id, u.Name, u.Plan, (IReadOnlyList<(string, double, DateTimeOffset?)>)u.Limits.Select(l => (l.Label, l.Percent, l.ResetsAt)).ToList(), u.Extra, (string?)null)
                : (d.Provider, AiUsage.DisplayName(d.Provider), "", (IReadOnlyList<(string, double, DateTimeOffset?)>)[], (string?)null, d.Problem ?? tr("No se pudo consultar el uso."))));
    }

    /// Lo que enseña la pastilla del notch cerrado: el % semanal de cada proveedor («32 % · 2 % · 78 %»), o vacío si no hay.
    public string PillText => string.Join(" · ", Rows.Where(r => r.HasLimits).Select(r => r.PrimaryPercent));
}
