using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace IsTargetSleeping.UI;

/// Glassmorphism negro: el panel es vidrio ahumado sobre el acrílico de Windows
/// 11 (desenfoca de verdad lo que hay detrás), con tarjetas de vidrio claro
/// translúcido, bordes de luz y texto blanco. La interfaz es monocroma; el azul
/// característico marca el estado encendido, el punto de la bandeja y el logo;
/// ámbar es lo que está cambiando y coral, lo crítico.
public sealed record Theme(
    Color Bg, Color Card, Color Line, Color Fg, Color Secondary, Color Muted,
    Color Accent, Color AccentSoft, Color OnAccent, Color On, Color OnSoft,
    Color Warm, Color WarmSoft, Color Danger, Color Track,
    Color Button, Color ButtonHover, Color Popup)
{
    /// El azul característico: estado encendido, punto de la bandeja y logo.
    public static readonly Color Blue = Hex(0x4DA3FF);

    public static readonly Theme Glass = new(
        Bg: Argb(0xC7, 0x050506),          // tinte negro (78 %) sobre el acrílico: casi no se cuela color de detrás
        Card: Argb(0x0F, 0xFFFFFF),        // vidrio claro de las tarjetas
        Line: Argb(0x1A, 0xFFFFFF),
        Fg: Hex(0xF5F5F7),
        Secondary: Argb(0xA6, 0xFFFFFF),
        Muted: Argb(0x6B, 0xFFFFFF),
        Accent: Hex(0xF5F5F7),             // blanco: selección, interruptores, barra del modelo
        AccentSoft: Argb(0x1F, 0xFFFFFF),
        OnAccent: Hex(0x0A0A0B),
        On: Blue,                          // estado encendido
        OnSoft: WithOpacity(Blue, 0.18),
        Warm: Hex(0xF5BD57),
        WarmSoft: Argb(0x24, 0xF5BD57),
        Danger: Hex(0xFF7A70),
        Track: Argb(0x14, 0xFFFFFF),
        Button: Argb(0x1F, 0xFFFFFF),
        ButtonHover: Argb(0x33, 0xFFFFFF),
        Popup: Argb(0xF2, 0x121214));      // menús y tooltips (sin acrílico de DWM)

    public static Theme Current { get; } = Glass;
    public static bool IsDark => true;
    public static event Action? Changed;

    /// El acrílico de DWM llegó con Windows 11 22H2 (compilación 22621). En
    /// Windows 10 el panel usa el mismo vidrio negro, pero opaco.
    public static bool BackdropSupported => Environment.OSVersion.Version.Build >= 22621;

    public static Color Hex(uint rgb, double opacity = 1) =>
        Color.FromArgb((byte)Math.Round(opacity * 255), (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static Color Argb(byte alpha, uint rgb) => Color.FromArgb(alpha, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static Color WithOpacity(Color c, double opacity) => Color.FromArgb((byte)Math.Round(opacity * c.A), c.R, c.G, c.B);

    public static SolidColorBrush Brush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static LinearGradientBrush Gradient(Color from, Color to, double angle = 90)
    {
        var b = new LinearGradientBrush(from, to, angle);
        b.Freeze();
        return b;
    }

    /// La barra de tareas tiene su propio modo («Modo de Windows»): el ícono de la
    /// bandeja lo sigue aunque el panel sea siempre negro.
    public static bool TaskbarIsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        }
        catch { return false; }
    }

    /// Publica los colores como recursos: la interfaz los usa con DynamicResource.
    public static void Apply(ResourceDictionary? resources = null)
    {
        var r = resources ?? Application.Current.Resources;
        var t = Current;
        foreach (var (name, color) in new[]
        {
            ("Bg", t.Bg), ("Card", t.Card), ("Line", t.Line), ("Fg", t.Fg), ("Secondary", t.Secondary),
            ("Muted", t.Muted), ("Accent", t.Accent), ("AccentSoft", t.AccentSoft), ("OnAccent", t.OnAccent),
            ("On", t.On), ("OnSoft", t.OnSoft),("Warm", t.Warm), ("WarmSoft", t.WarmSoft),
            ("Danger", t.Danger), ("Track", t.Track), ("ButtonBg", t.Button), ("ButtonHover", t.ButtonHover),
            ("PopupBg", t.Popup),
        })
        {
            r[name] = Brush(color);
            r[name + "Color"] = color;
        }
        r["MutedHalf"] = Brush(WithOpacity(t.Muted, 0.55));
        r["DangerSoft"] = Brush(WithOpacity(t.Danger, 0.16));

        // Vidrio negro: el tinte del panel (opaco si no hay acrílico), tarjetas de
        // vidrio neutro con un canto fino, y ninguna luz de color: la profundidad la
        // da una sombra negra que cae hacia abajo, no un brillo.
        r["PanelTint"] = BackdropSupported ? Brush(t.Bg) : Brush(Hex(0x0A0A0B));
        r["CardFill"] = Gradient(Argb(0x0F, 0xFFFFFF), Argb(0x06, 0xFFFFFF));
        r["CardStroke"] = Gradient(Argb(0x24, 0xFFFFFF), Argb(0x0A, 0xFFFFFF), 70);
        r["Sheen"] = Gradient(Argb(0x00, 0x000000), Argb(0x59, 0x000000));
        Changed?.Invoke();
    }
}
