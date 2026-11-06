using System.IO;
using System.Windows;
using IsTargetSleeping;
using IsTargetSleeping.UI;

/// Los avisos de agentes (atascado, bucle, límite de uso) en la página Agentes del notch real. Solo con ITS_ONLY_APPROVAL=1.
internal static class AlertNotchTests
{
    public static void Run(Action<string, bool> check)
    {
        if (ApprovalNotchTests.SkipWithoutSecondary(check, out var screen)) return;
        var captureDir = Environment.GetEnvironmentVariable("ITS_CAPTURE_UI");
        if (captureDir is { Length: > 0 }) Directory.CreateDirectory(captureDir);
        var originalLanguage = L10n.Effective;
        foreach (var (language, locale) in new[] { (Language.Es, "es"), (Language.En, "en") })
        {
            L10n.Init(language);
            Theme.Apply();
            var controller = new OllamaController();
            var prefs = new Prefs();
            controller.LoadDemo(); prefs.LoadDemo();
            prefs.SetSwitch(PrefKeys.AgentApprovals, true);
            var supervisor = new Supervisor(controller, prefs);
            supervisor.LoadDemo();
            var model = new PanelViewModel(supervisor);
            var notch = new NotchWindow(model, new ContentView(model), screen);
            try
            {
                notch.Start();
                model.Hub.Agents.LoadDemo();
                model.Show(PanelView.Agents);
                model.ShowAwayDigest(new AwaySummary(2, 3, 1, 1, TimeSpan.FromMinutes(47)));
                model.SetAgentAlerts([
                    new(AgentAlertKind.Stalled, "stall|a", "Rediseño del Home", "", 3, null),
                    new(AgentAlertKind.Loop, "loop|b", "Pruebas del notch", "Read Widget.cs", 7, null),
                    new(AgentAlertKind.LimitNear, "near|c", "Codex", "5 h", 93, DateTimeOffset.Now.AddMinutes(48)),
                    new(AgentAlertKind.LimitHit, "hit|d", "Claude", "5 h", 100, DateTimeOffset.Now.AddMinutes(75)),
                ]);
                notch.Toggle();
                ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(900));
                notch.UpdateLayout();
                check($"[avisos {locale}] al volver se enseña lo que pasó mientras no estabas", model.HasAwayDigest && model.AwayDigestText.Contains('2') && model.ChatLines.Any(l => l.Text.Contains(model.AwayDigestText)));
                var rows = model.AgentAlertRows;
                check($"[avisos {locale}] los cuatro avisos se listan, los graves marcados", rows.Count == 4 && !rows[0].Severe && rows[1].Severe && rows[3].Severe);
                check($"[avisos {locale}] el límite agotado dice cuándo vuelve", rows[3].Detail.Contains("1 h 15") || rows[3].Detail.Contains("1 h 14"));
                check($"[avisos {locale}] el resumen cuenta los avisos", model.AgentsSummary.Contains('4'));
                if (captureDir is { Length: > 0 }) ApprovalNotchTests.Save((FrameworkElement)notch.Content, 120, Path.Combine(captureDir, $"notch-avisos-{locale}-120.png"));

                // Retomar una sesión terminada en una terminal: el botón sale solo en las que se pueden retomar (no se pulsa: abriría una terminal).
                model.SetAgentAlerts([]);
                model.DismissAwayDigest.Execute(null);
                var repo = Path.Combine(Path.GetTempPath(), "its-meta-repo-" + locale);
                Directory.CreateDirectory(Path.Combine(repo, ".git"));
                File.WriteAllText(Path.Combine(repo, ".git", "HEAD"), "ref: refs/heads/feature/invoice-vat" + (char)10);
                var resumable = new AgentSnapshot("01234567-89ab-cdef-0123-456789abcdef", repo, "Claude Code", AgentState.Finished, null, null, 12, 2, "Listo", [], DateTime.UtcNow.AddMinutes(-9)) { Title = "Migración de pruebas", ContextTokens = 126_400 };
                model.Hub.Agents.RecentSessions.Insert(0, resumable);
                ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(500));
                check($"[avisos {locale}] una sesión terminada con carpeta se puede retomar en una terminal", resumable.CanResumeInTerminal && model.AgentSessions.Any(x => x.CanResumeInTerminal));
                check($"[avisos {locale}] la sesión enseña su rama de git y su contexto", resumable.GitBranch == "feature/invoice-vat" && resumable.ContextLabel == "126k" && resumable.HasMeta);
                if (captureDir is { Length: > 0 }) ApprovalNotchTests.Save((FrameworkElement)notch.Content, 120, Path.Combine(captureDir, $"notch-retomar-{locale}-120.png"));

                // Con algo a pantalla completa (un juego), un permiso no abre el notch ni se trae al frente: espera en la cola.
                var state = (NotchState)typeof(NotchWindow).GetField("state", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(notch)!;
                notch.Collapse();
                ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(500));
                typeof(NotchWindow).GetField("hideReason", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(notch, HideReason.Fullscreen);
                var held = AgentHooks.Parse(new System.Text.Json.Nodes.JsonObject
                {
                    ["agent"] = "claude",
                    ["payload"] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["hook_event_name"] = "PermissionRequest", ["session_id"] = "g", ["cwd"] = @"C:\dev\game", ["tool_name"] = "Bash",
                        ["tool_input"] = new System.Text.Json.Nodes.JsonObject { ["command"] = "npm test" },
                    },
                }.ToJsonString())!;
                model.ShowApproval(held);
                ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(500));
                check($"[avisos {locale}] con pantalla completa el permiso espera y el notch no se abre", model.HasApproval && state.Mode != NotchMode.Pinned);
                held.Decide(HookVerdict.Fold);
                ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(300));

                // Nivel «Nada»: un permiso no abre el notch ni suena; espera en la página Agentes.
                typeof(NotchWindow).GetField("hideReason", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(notch, HideReason.None);
                model.Prefs.SetList(PrefKeys.AgentNotify, ["nothing"]);
                var quiet = AgentHooks.Parse(new System.Text.Json.Nodes.JsonObject
                {
                    ["agent"] = "claude",
                    ["payload"] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["hook_event_name"] = "PermissionRequest", ["session_id"] = "q", ["cwd"] = @"C:\dev\quiet", ["tool_name"] = "Bash",
                        ["tool_input"] = new System.Text.Json.Nodes.JsonObject { ["command"] = "npm run lint" },
                    },
                }.ToJsonString())!;
                model.ShowApproval(quiet);
                ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(500));
                check($"[avisos {locale}] con el nivel «Nada» el permiso no abre el notch y queda en la lista", model.HasApproval && state.Mode != NotchMode.Pinned && !model.NotifyAllows(AgentNotifyKind.Permission));
                quiet.Decide(HookVerdict.Fold);
                model.Prefs.SetList(PrefKeys.AgentNotify, ["important"]);
                check($"[avisos {locale}] con «Solo lo importante» sí permite permisos y calla el «terminó»", model.NotifyAllows(AgentNotifyKind.Permission) && !model.NotifyAllows(AgentNotifyKind.Finished));
                model.Prefs.SetList(PrefKeys.AgentSnooze, [DateTime.Now.AddHours(1).ToString("o")]);
                check($"[avisos {locale}] posponer pone todo en silencio", model.IsSnoozed && !model.NotifyAllows(AgentNotifyKind.Permission));
                model.CurrentSettingsTab = PanelViewModel.SettingsTab.General;
                model.Show(PanelView.Settings);
                notch.Toggle();
                ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(700));
                if (captureDir is { Length: > 0 }) ApprovalNotchTests.Save((FrameworkElement)notch.Content, 120, Path.Combine(captureDir, $"notch-ajustes-avisos-{locale}-120.png"));
                model.Prefs.SetList(PrefKeys.AgentSnooze, [""]);
                model.Prefs.SetList(PrefKeys.AgentNotify, ["all"]);
            }
            finally { notch.Stop(); notch.Close(); model.Hub.Dispose(); }
        }
        L10n.Init(originalLanguage == "en" ? Language.En : Language.Es);
    }
}
