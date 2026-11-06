using IsTargetSleeping;

internal static class AiUsageTests
{
    public static void Run(Action<string, bool> check)
    {
        var now = new DateTime(2026, 10, 21, 17, 0, 0, DateTimeKind.Local);

        // MARK: Claude
        var claude = AiUsage.ParseClaude("""
            {"five_hour":{"utilization":6.0,"resets_at":"2026-10-21T21:10:00+00:00"},
             "seven_day":{"utilization":32,"resets_at":"2026-10-24T23:00:00Z"},
             "seven_day_opus":{"utilization":0,"resets_at":null},
             "seven_day_sonnet":{"utilization":14.4,"resets_at":null},"extra_usage":null}
            """, "max", now);
        check("Claude: dos barras (5 h y semanal) con su porcentaje", claude is { Limits.Count: 2 } && claude.Limits[0].Label == "5h" && claude.Limits[0].Percent == 6 && claude.Limits[1].Percent == 32);
        check("Claude: el reinicio sale de resets_at", claude!.Limits[0].ResetsAt == DateTimeOffset.Parse("2026-10-21T21:10:00Z"));
        check("Claude: el plan y los cupos extra por modelo", claude.Plan == "Max" && claude.Extra == "Opus 0% · Sonnet 14%");
        check("Claude: sin ventanas no hay datos (no se inventan)", AiUsage.ParseClaude("{\"five_hour\":null}", null, now) is null && AiUsage.ParseClaude("no es json", null, now) is null);
        check("Claude: un porcentaje fuera de rango se acota", AiUsage.ParseClaude("{\"five_hour\":{\"utilization\":140}}", null, now)!.Limits[0].Percent == 100);

        // MARK: Codex
        var codex = AiUsage.ParseCodex("""
            {"plan_type":"plus","rate_limit":{"primary_window":{"used_percent":0,"limit_window_seconds":18000,"reset_after_seconds":3600,"reset_at":1792000000},
             "secondary_window":{"used_percent":2.5,"limit_window_seconds":604800,"reset_after_seconds":90000}}}
            """, now);
        check("Codex: ventana de 5 h y semanal", codex is { Limits.Count: 2 } && codex.Limits[0].Label == "5h" && codex.Limits[1].Label == "week" && codex.Limits[1].Percent == 2.5);
        check("Codex: reset_at manda y reset_after_seconds es el respaldo",
            codex!.Limits[0].ResetsAt == DateTimeOffset.FromUnixTimeSeconds(1792000000) && codex.Limits[1].ResetsAt == new DateTimeOffset(now.ToUniversalTime()).AddSeconds(90000));
        check("Codex: el plan sale de plan_type", codex.Plan == "Plus");
        check("Codex: sin rate_limit no hay datos", AiUsage.ParseCodex("{\"plan_type\":\"plus\"}", now) is null);

        // MARK: Gemini
        var gemini = AiUsage.ParseGemini("""
            {"buckets":[{"modelId":"gemini-2.5-pro","remainingFraction":0.22,"resetTime":"2026-10-25T15:59:00Z"},
                        {"modelId":"gemini-2.5-flash","remainingFraction":1,"resetTime":"2026-10-21T22:00:00Z"},
                        {"modelId":"gemini-2.5-pro","remainingFraction":0.5},
                        {"modelId":"gemini-2.0-flash","remainingFraction":0.9},{"modelId":"sin-dato"}]}
            """, "Google AI Pro", now);
        check("Gemini: el cupo más gastado va primero (el peor por modelo)", gemini is { Limits.Count: 2 } && gemini.Limits[0].Label == "gemini-2.5-pro" && Math.Abs(gemini.Limits[0].Percent - 78) < 1e-9);
        check("Gemini: lo que sobra va a la línea secundaria", gemini!.Extra == "gemini-2.5-flash 0%" && gemini.Limits[1].Label == "gemini-2.0-flash");
        check("Gemini: el plan y, sin cubos, no hay datos", gemini.Plan == "Google AI Pro" && AiUsage.ParseGemini("{}", null, now) is null);

        // MARK: fuentes adicionales
        var muse = AiUsage.ParseMuse("Plan: Power Usage\n5-hour window: 31.5% used\nWeekly usage: 42%", now);
        check("Muse: extrae ventanas 5 h y semanal sin convertir otra métrica en cuota",
            muse is { Limits.Count: 2 } && muse.Limits[0] is { Label: "5h", Percent: 31.5 } && muse.Limits[1] is { Label: "week", Percent: 42 });
        check("Muse: salida desconocida no inventa una cuota", AiUsage.ParseMuse("Muse Code usage is available", now) is null);

        var ollama = AiUsage.ParseOllama("""
            {"models":[{"name":"qwen2.5-coder:14b","size_vram":1234},{"model":"llama3.2:latest"}]}
            """, now);
        check("Ollama: muestra los modelos cargados sin porcentaje de cuota",
            ollama is { Id: "ollama", Limits.Count: 0 } && ollama.Extra!.Contains("qwen2.5-coder:14b", StringComparison.Ordinal) && ollama.Extra.Contains("llama3.2:latest", StringComparison.Ordinal));
        check("Ollama: servidor activo sin modelo sigue siendo un dato válido",
            AiUsage.ParseOllama("{\"models\":[]}", now) is { Limits.Count: 0, Extra: "Sin modelos cargados" });

        var grok = AiUsage.ParseGrokApi("""
            {"timeSeries":[{"dataPoints":[{"values":[0.1]},{"values":[0.25]}]},{"dataPoints":[{"values":[0.4]}]}]}
            """, now);
        check("Grok API: suma gasto del equipo sin presentarlo como cuota", grok is { Id: "grok-api", Limits.Count: 0, Extra: "US$0.75 este mes" });
        check("Grok API: una respuesta sin timeSeries no se convierte en cero", AiUsage.ParseGrokApi("{}", now) is null);

        // MARK: detección de sesiones (carpeta personal temporal, solo lectura)
        var home = Path.Combine(Path.GetTempPath(), "its-usage-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(home);
            check("sin credenciales o CLI no se detecta ningún proveedor", AiUsage.Detect(home, null, null, museAvailable: false).Count == 0);

            long future = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeMilliseconds(), past = DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeMilliseconds();
            Directory.CreateDirectory(Path.Combine(home, ".claude"));
            File.WriteAllText(Path.Combine(home, ".claude", ".credentials.json"), $"{{\"claudeAiOauth\":{{\"accessToken\":\"tok-claude\",\"expiresAt\":{future},\"subscriptionType\":\"max\"}}}}");
            Directory.CreateDirectory(Path.Combine(home, ".codex"));
            var exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
            static string B64(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var jwt = $"{B64("{\"alg\":\"none\"}")}.{B64($"{{\"exp\":{exp}}}")}.firma";
            File.WriteAllText(Path.Combine(home, ".codex", "auth.json"), $"{{\"tokens\":{{\"access_token\":\"{jwt}\",\"account_id\":\"acc-1\"}}}}");
            Directory.CreateDirectory(Path.Combine(home, ".gemini"));
            File.WriteAllText(Path.Combine(home, ".gemini", "oauth_creds.json"), $"{{\"access_token\":\"tok-gem\",\"expiry_date\":{past}}}");

            var sessions = AiUsage.Detect(home, null, null, museAvailable: false);
            check("se detectan Claude, Codex y Gemini con sesión", sessions.Select(s => s.Provider).SequenceEqual(["claude", "codex", "gemini"]));
            check("Claude: plan y vigencia leídos", sessions[0].Plan == "max" && !sessions[0].Expired);
            check("Codex: cuenta y vigencia leídas del JWT", sessions[1].AccountId == "acc-1" && !sessions[1].Expired);
            check("Gemini: un token caducado se marca como caducado", sessions[2].Expired);

            var allConfigured = AiUsage.Detect(home, "management-key", "team-1", museAvailable: true);
            check("Muse y Grok API se agregan sin alterar las sesiones OAuth existentes",
                allConfigured.Select(s => s.Provider).SequenceEqual(["claude", "codex", "gemini", "muse", "grok-api"])
                && allConfigured[^1] is { Token: "management-key", AccountId: "team-1", Plan: "API" });
            check("Grok API no aparece si falta el equipo", AiUsage.Detect(home, "management-key", null, museAvailable: false).All(s => s.Provider != "grok-api"));

            File.WriteAllText(Path.Combine(home, ".codex", "auth.json"), "{\"OPENAI_API_KEY\":\"sk-x\",\"tokens\":null}");
            File.WriteAllText(Path.Combine(home, ".gemini", "oauth_creds.json"), "{ roto");
            check("sin token OAuth (clave de API) o con el archivo roto, el proveedor no aparece", AiUsage.Detect(home, null, null, museAvailable: false).Select(s => s.Provider).SequenceEqual(["claude"]));
        }
        finally { try { Directory.Delete(home, true); } catch (IOException) { } }

        // MARK: textos
        check("«hace N min» y «ahora»", AiUsage.Ago(now.AddSeconds(-20), now) == "ahora" && AiUsage.Ago(now.AddMinutes(-2), now) == "hace 2 min" && AiUsage.Ago(now.AddHours(-3), now) == "hace 3 h");
        check("el reinicio de hoy lleva solo la hora; sin dato, nada",
            AiUsage.Reset(new DateTimeOffset(now.AddHours(2)), now) == "reinicia 19:00" && AiUsage.Reset(null, now) == "");
        check("nombres visibles distinguen Grok API, Muse Code y Ollama local", AiUsage.DisplayName("grok-api") == "Grok API" && AiUsage.DisplayName("muse") == "Muse Code" && AiUsage.DisplayName("ollama") == "Ollama local");
        check("el widget está en el catálogo y en la pastilla", WidgetCatalog.Find("usage") is { Cells: 1 } && PillCatalog.Find("usage") is not null
            && PillCatalog.WidgetsNeeded(["usage"]).SequenceEqual(["usage"]) && WidgetLayout.Sizes("usage").Count == 4);
    }
}
