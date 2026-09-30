using System.Globalization;
using System.Text;
using System.Text.Json;

namespace IsTargetSleeping;

/// Textos traducibles. La clave es el propio texto en español (idioma base), así
/// el código se lee igual que la interfaz; las traducciones viven en
/// Resources/Strings.&lt;idioma&gt;.json y lo que falte sale en español.
/// Los formatos son de estilo printf (%@, %d, %1$@) y se traducen a los de .NET.
public static class L10n
{
    private static Dictionary<string, string>? table;
    private static readonly Dictionary<string, string> formats = new();

    /// Idioma con el que arrancó la app: se resuelve una vez (cambiarlo relanza la app).
    public static string Effective { get; private set; } = "es";

    public static void Init(Language? overrideLanguage = null)
    {
        var lang = overrideLanguage ?? LanguageExt.Current;
        Effective = lang switch
        {
            Language.Es => "es",
            Language.En => "en",
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "es" : "en",
        };
        table = Effective == "es" ? null : Load(Effective);
        formats.Clear();
    }

    private static Dictionary<string, string>? Load(string lang)
    {
        using var stream = typeof(L10n).Assembly.GetManifestResourceStream($"IsTargetSleeping.Strings.{lang}.json");
        if (stream is null) return null;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
    }

    public static string tr(string key) =>
        table is not null && table.TryGetValue(key, out var value) ? value : key;

    public static string tr(string key, params object[] args)
    {
        var raw = tr(key);
        if (!formats.TryGetValue(raw, out var net)) formats[raw] = net = ToNetFormat(raw);
        return string.Format(CultureInfo.InvariantCulture, net, args);
    }

    /// «%@ en %d min» → «{0} en {1} min»; también los posicionales «%2$d».
    private static string ToNetFormat(string printf)
    {
        var sb = new StringBuilder();
        int next = 0;
        for (int i = 0; i < printf.Length; i++)
        {
            char c = printf[i];
            if (c == '{') { sb.Append("{{"); continue; }
            if (c == '}') { sb.Append("}}"); continue; }
            if (c != '%' || i + 1 >= printf.Length) { sb.Append(c); continue; }
            if (printf[i + 1] == '%') { sb.Append('%'); i++; continue; }
            int j = i + 1, index = -1;
            int digits = j;
            while (digits < printf.Length && char.IsDigit(printf[digits])) digits++;
            if (digits > j && digits < printf.Length && printf[digits] == '$')
            {
                index = int.Parse(printf[j..digits]) - 1;
                j = digits + 1;
            }
            if (j < printf.Length && (printf[j] == '@' || printf[j] == 'd'))
            {
                sb.Append('{').Append(index >= 0 ? index : next++).Append('}');
                i = j;
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }
}

/// Idioma elegido en Ajustes. «Automático» sigue al sistema.
public enum Language { Auto, Es, En }

public static class LanguageExt
{
    private const string Key = "language";

    public static string Label(this Language l) => l switch
    {
        Language.Es => "ES",
        Language.En => "EN",
        _ => L10n.tr("Auto"),
    };

    public static Language Current => Defaults.GetString(Key) switch
    {
        "es" => Language.Es,
        "en" => Language.En,
        _ => Language.Auto,
    };

    public static void Apply(this Language l)
    {
        if (l == Language.Auto) Defaults.Remove(Key);
        else Defaults.Set(Key, l == Language.Es ? "es" : "en");
    }
}
