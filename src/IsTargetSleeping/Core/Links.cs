using Microsoft.Win32;

namespace IsTargetSleeping;

public enum LinkAction { On, Off, Toggle, Sleep, Load, Show, Activity, Clean }

public readonly record struct Link(LinkAction Action, string? Model = null);

/// Enlaces `istargetsleeping://…` para Stream Deck, PowerToys, scripts o una nota:
/// on, off, toggle, sleep, clean, load/&lt;modelo&gt;, show y activity. Se registran en
/// HKCU\Software\Classes (sin administrador); Windows abre la app con
/// `--url "<enlace>"` y, si ya corre, la segunda instancia se lo pasa a la primera.
public static class Links
{
    public const string Scheme = "istargetsleeping";
    private const string Key = @"Software\Classes\" + Scheme;

    public static readonly string[] Examples =
        ["on", "off", "toggle", "sleep", "clean", "load/llama3.2", "show", "activity"];

    public static string Url(string path) => $"{Scheme}://{path}";

    public static Link? Parse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var s = url.Trim().Trim('"');
        if (!s.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase)) return null;
        s = s[(Scheme.Length + 1)..].Trim('/');
        int slash = s.IndexOf('/');
        var action = (slash < 0 ? s : s[..slash]).ToLowerInvariant();
        var rest = slash < 0 ? "" : s[(slash + 1)..].TrimEnd('/');
        string Decode(string v) { try { return Uri.UnescapeDataString(v); } catch { return v; } }
        return action switch
        {
            "on" => new Link(LinkAction.On),
            "off" => new Link(LinkAction.Off),
            "toggle" => new Link(LinkAction.Toggle),
            "sleep" => new Link(LinkAction.Sleep),
            "clean" => new Link(LinkAction.Clean),
            "show" => new Link(LinkAction.Show),
            "activity" => new Link(LinkAction.Activity),
            "load" when rest.Length > 0 => new Link(LinkAction.Load, Decode(rest)),
            _ => null,
        };
    }

    private static string Command(string exe) => $"\"{exe}\" --url \"%1\"";

    /// Se registra al arrancar si falta o apunta a otro .exe (se movió o se actualizó).
    public static void EnsureRegistered()
    {
        if (Environment.ProcessPath is not { } exe) return;
        try
        {
            using (var current = Registry.CurrentUser.OpenSubKey(Key + @"\shell\open\command"))
            {
                if (current?.GetValue(null) as string == Command(exe)) return;
            }
            using var root = Registry.CurrentUser.CreateSubKey(Key);
            root.SetValue(null, $"URL:{AppInfo.Name}");
            root.SetValue("URL Protocol", "");
            using (var icon = root.CreateSubKey("DefaultIcon")) icon.SetValue(null, $"\"{exe}\",0");
            using var command = root.CreateSubKey(@"shell\open\command");
            command.SetValue(null, Command(exe));
        }
        catch { }
    }
}
