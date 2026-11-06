using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using IsTargetSleeping;
using IsTargetSleeping.UI;

/// Una pregunta del agente y un plan en el notch real: se eligen opciones, se envían y llegan al agente. Solo con ITS_ONLY_APPROVAL=1.
internal static class QuestionNotchTests
{
    private static JsonObject Payload(string tool, JsonObject input) => new()
    {
        ["hook_event_name"] = "PreToolUse", ["session_id"] = "d", ["cwd"] = @"C:\dev\app", ["tool_name"] = tool, ["tool_input"] = input,
    };

    private static HookRequest Request(string tool, JsonObject input) =>
        AgentHooks.Parse(new JsonObject { ["agent"] = "claude", ["payload"] = Payload(tool, input) }.ToJsonString())!;

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
            var supervisor = new Supervisor(controller, prefs);
            supervisor.LoadDemo();
            var model = new PanelViewModel(supervisor);
            var notch = new NotchWindow(model, new ContentView(model), screen);
            try
            {
                notch.Start();
                model.Show(PanelView.Main);
                var question = Request("AskUserQuestion", new JsonObject
                {
                    ["questions"] = new JsonArray(
                        new JsonObject
                        {
                            ["question"] = "¿Qué framework usamos para la interfaz?", ["header"] = "Interfaz", ["multiSelect"] = false,
                            ["options"] = new JsonArray(
                                new JsonObject { ["label"] = "React", ["description"] = "Biblioteca de componentes" },
                                new JsonObject { ["label"] = "Vue", ["description"] = "Framework progresivo" },
                                new JsonObject { ["label"] = "Svelte", ["description"] = "Compilado, sin runtime" }),
                        },
                        new JsonObject
                        {
                            ["question"] = "¿Qué secciones incluimos?", ["header"] = "Secciones", ["multiSelect"] = true,
                            ["options"] = new JsonArray(new JsonObject { ["label"] = "Introducción" }, new JsonObject { ["label"] = "Pruebas" }, new JsonObject { ["label"] = "Despliegue" }),
                        }),
                });
                model.ShowApproval(question);
                ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(900));
                notch.UpdateLayout();
                var card = model.Approval!;
                check($"[preguntas {locale}] la pregunta sale como pregunta y no como permiso", card is { IsQuestion: true, IsPermission: false, CanAlways: false } && card.Questions.Count == 2);
                check($"[preguntas {locale}] no se puede enviar sin elegir", !card.CanSend);
                card.AllowKey.Execute(null);
                check($"[preguntas {locale}] la tecla Y no contesta una pregunta", !question.Answer.IsCompleted);
                card.Questions[0].Options[1].Pick.Execute(null);
                card.Questions[1].Options[0].Pick.Execute(null);
                card.Questions[1].Options[2].Pick.Execute(null);
                ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(300));
                check($"[preguntas {locale}] con una elección por pregunta ya se puede enviar", card.CanSend);
                if (captureDir is { Length: > 0 }) ApprovalNotchTests.Save((FrameworkElement)notch.Content, 120, Path.Combine(captureDir, $"notch-pregunta-{locale}-120.png"));
                card.SendAnswers.Execute(null);
                ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(500));
                check($"[preguntas {locale}] las respuestas llegan al agente (una por pregunta, varias unidas)", question.Answer.IsCompleted && question.Answers is { Count: 2 } a
                    && a["¿Qué framework usamos para la interfaz?"] == "Vue" && a["¿Qué secciones incluimos?"] == "Introducción, Despliegue");

                var plan = Request("ExitPlanMode", new JsonObject
                {
                    ["plan"] = "## Plan\n1. Añadir la validación de la TVA en `invoice.ts`.\n2. Escribir las pruebas que faltan.\n3. Actualizar el CHANGELOG.\n\nNo toco ningún otro archivo.",
                });
                model.ShowApproval(plan);
                ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(700));
                check($"[preguntas {locale}] un plan sale con su texto y sin «Siempre»", model.Approval is { IsPlan: true, HasPlanText: true, CanAlways: false });
                if (captureDir is { Length: > 0 }) ApprovalNotchTests.Save((FrameworkElement)notch.Content, 120, Path.Combine(captureDir, $"notch-plan-{locale}-120.png"));
                model.Approval!.RequestChanges.Execute(null);
                ApprovalNotchTests.Pump(TimeSpan.FromMilliseconds(500));
                check($"[preguntas {locale}] pedir cambios rechaza el plan con un motivo", plan.Answer.Result == HookVerdict.Deny && plan.DenyMessage is { Length: > 10 });
            }
            finally { notch.Stop(); notch.Close(); model.Hub.Dispose(); }
        }
        L10n.Init(originalLanguage == "en" ? Language.En : Language.Es);
    }
}
