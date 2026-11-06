using System.Text.Json;

namespace IsTargetSleeping.Pet;

/// Lista CERRADA de cosas que la mascota puede pedirle a la app isTargetSleeping. Todo lo demas se ignora.
/// Solo interruptores reversibles de la propia app; nada que borre, envie, ejecute o toque archivos.
public static class AppActionNames
{
    public static readonly string[] All = ["none", "usage_widget_on", "usage_widget_off"];
    public static string Normalize(string? s) => s is not null && Array.IndexOf(All, s) >= 0 ? s : "none";

    /// Las acciones del esquema de salida del modelo -> la herramienta del registro (y sus argumentos).
    public static (string Tool, System.Text.Json.JsonElement Args) ToTool(string action)
    {
        bool on = action == "usage_widget_on";
        return ("usage.set", System.Text.Json.JsonDocument.Parse(on ? "{\"on\":true}" : "{\"on\":false}").RootElement.Clone());
    }
}

/// Intenciones claras del usuario que NO dependen de que un modelo pequeno se acuerde de usar la accion.
public static class AppIntents
{
    private static readonly System.Text.RegularExpressions.Regex UsageTopic = new(@"\b(uso|usage|consumo|l[ií]mite|cuota|tokens?)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly System.Text.RegularExpressions.Regex Asks = new(@"\b(cu[aá]nto|queda|quedan|gastad[oa]s?|usad[oa]s?|consumid[oa]s?|ver|revisa|mira|dime|muestra)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly System.Text.RegularExpressions.Regex Off = new(@"\b(apaga|desactiva|quita|oculta)\b.*\b(widget|uso|usage)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// Respuesta con accion ya decidida, o null si el modelo debe responder solo.
    public static ChatReply? Detect(string text, bool usageAvailable)
    {
        if (Off.IsMatch(text)) return new ChatReply("Listo, apago el widget de uso.", "smile", "usage_widget_off");
        if (!usageAvailable && UsageTopic.IsMatch(text) && Asks.IsMatch(text))
            return new ChatReply("Un momento, activo el widget de uso para mirarlo.", "think", "usage_widget_on");
        return null;
    }
}
