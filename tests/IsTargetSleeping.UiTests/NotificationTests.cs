using System.Reflection;
using System.Xml.Linq;
using IsTargetSleeping;
using IsTargetSleeping.UI;
using Windows.UI.Notifications;

internal static class NotificationTests
{
    public static void Run(Action<string, bool> check)
    {
        var service = typeof(TrayIcon).Assembly.GetType("IsTargetSleeping.UI.ModernNotifications")!;
        var payload = service.GetMethod("Payload", BindingFlags.Static | BindingFlags.NonPublic)!;
        var progress = service.GetMethod("Progress", BindingFlags.Static | BindingFlags.NonPublic)!;
        XDocument Xml(string? button, bool download) => XDocument.Parse((string)payload.Invoke(null,
            ["file:///logo.png", "Modelo <uno>", "A & B", button, download])!);

        var plain = Xml(null, false);
        check("aviso simple: texto escapado y logo de origen", plain.Descendants("text").First().Value == "Modelo <uno>"
            && plain.Descendants("text").Last().Value == "A & B"
            && (string?)plain.Descendants("image").Single().Attribute("placement") == "appLogoOverride"
            && !plain.Descendants("action").Any());
        var sleep = Xml("Ver detalles", false);
        check("modelo dormido: detalles abre actividad", (string?)sleep.Descendants("action").Single().Attribute("arguments") == Links.Url("activity"));
        var download = Xml("Abrir", true);
        check("descarga: botón y datos de progreso", (string?)download.Descendants("action").Single().Attribute("arguments") == Links.Url("show")
            && (string?)download.Descendants("progress").Single().Attribute("value") == "{value}");
        foreach (var language in new[] { Language.Es, Language.En })
        {
            L10n.Init(language);
            var job = new PullJob("test") { Status = "pulling manifest" };
            NotificationData Data() => (NotificationData)progress.Invoke(null, [job, 1u])!;
            check($"{language}: progreso indeterminado al empezar", Data().Values["value"] == "indeterminate");
            job.Fraction = .62;
            var data = Data();
            check($"{language}: progreso real y estado traducido", data.Values["value"] == "0.62"
                && data.Values["percent"].Contains("62") && data.Values["status"] == L10n.tr("Leyendo el manifiesto…"));
            job.Fraction = double.NaN;
            check($"{language}: progreso inválido es indeterminado", Data().Values["value"] == "indeterminate");
        }
    }
}
