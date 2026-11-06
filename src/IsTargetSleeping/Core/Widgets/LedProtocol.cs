namespace IsTargetSleeping;

/// La familia de órdenes de una tira LED Bluetooth: las ELK-BLEDOM («Colorido luz de 3 cables») y las MELK («Colorido mágico»), que hablan casi igual.
/// Las órdenes son tramas de nueve bytes (empiezan en 0x7E y acaban en 0xEF) que se escriben, sin respuesta, en la característica 0xFFF3 del servicio 0xFFF0.
public enum LedFamily { Elk, ElkAlt }

/// Cómo se mueve un efecto: salta de un color a otro, se funde o parpadea.
public enum LedEffectKind { Jump, Fade, Blink }

/// Un efecto dinámico que la propia tira sabe hacer: su código y el grupo en el que se enseña.
public sealed record LedEffect(int Code, string Key, string Group);

/// Una escena: un color fijo con su brillo, o un efecto con su velocidad.
public sealed record LedScene(string Key, bool IsEffect, byte R, byte G, byte B, int Brightness, int EffectCode = 0, int Speed = 0);

/// Las órdenes de las tiras LED, puras (sin Bluetooth): se prueban sin tiras.
public static class LedProtocol
{
    public const string ServiceUuid = "0000fff0-0000-1000-8000-00805f9b34fb";
    public const string WriteUuid = "0000fff3-0000-1000-8000-00805f9b34fb";
    public const int MaxBrightness = 100;

    private static byte[] Frame(byte b1, byte b2, byte b3, byte b4, byte b5, byte b6, byte b7) => [0x7E, b1, b2, b3, b4, b5, b6, b7, 0xEF];
    private static byte Pct(int value) => (byte)Math.Clamp(value, 0, MaxBrightness);

    /// ELK-BLEDOM, MELK-…: lo que se anuncia por Bluetooth con esos nombres. El resto de dispositivos no se toca.
    public static bool IsLedName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && (name.StartsWith("ELK-", StringComparison.OrdinalIgnoreCase) || name.StartsWith("MELK", StringComparison.OrdinalIgnoreCase)
            || name.Contains("BLEDOM", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LEDBLE", StringComparison.OrdinalIgnoreCase));

    public static LedFamily FamilyFor(string? name) => name?.StartsWith("MELK", StringComparison.OrdinalIgnoreCase) == true ? LedFamily.ElkAlt : LedFamily.Elk;

    public static byte[] Power(bool on, LedFamily family = LedFamily.Elk) => family == LedFamily.Elk
        ? Frame(0x00, 0x04, on ? (byte)0xF0 : (byte)0x00, 0x00, on ? (byte)0x01 : (byte)0x00, 0xFF, 0x00)
        : Frame(0x04, 0x04, on ? (byte)0xF0 : (byte)0x00, 0x00, on ? (byte)0x01 : (byte)0x00, 0xFF, 0x00);

    public static byte[] Color(byte r, byte g, byte b, LedFamily family = LedFamily.Elk) => family == LedFamily.Elk
        ? Frame(0x00, 0x05, 0x03, r, g, b, 0x00)
        : Frame(0x07, 0x05, 0x03, r, g, b, 0x10);

    /// Brillo de 0 a 100.
    public static byte[] Brightness(int percent, LedFamily family = LedFamily.Elk) => family == LedFamily.Elk
        ? Frame(0x00, 0x01, Pct(percent), 0x00, 0x00, 0x00, 0x00)
        : Frame(0x04, 0x01, Pct(percent), 0x01, 0xFF, 0xFF, 0x00);

    public static byte[] Effect(int code, LedFamily family = LedFamily.Elk) => family == LedFamily.Elk
        ? Frame(0x00, 0x03, (byte)Math.Clamp(code, 0, 255), 0x03, 0x00, 0x00, 0x00)
        : Frame(0x05, 0x03, (byte)Math.Clamp(code, 0, 255), 0x06, 0xFF, 0xFF, 0x00);

    /// Velocidad del efecto de 0 a 100.
    public static byte[] Speed(int percent, LedFamily family = LedFamily.Elk) => family == LedFamily.Elk
        ? Frame(0x00, 0x02, Pct(percent), 0x00, 0x00, 0x00, 0x00)
        : Frame(0x04, 0x02, Pct(percent), 0x00, 0x00, 0x00, 0x00);

    /// Color HSV (tono 0–360, saturación y valor 0–1) a RGB.
    public static (byte R, byte G, byte B) FromHsv(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);
        double c = value * saturation, x = c * (1 - Math.Abs(hue / 60 % 2 - 1)), m = value - c;
        var (r, g, b) = hue switch { < 60 => (c, x, 0.0), < 120 => (x, c, 0.0), < 180 => (0.0, c, x), < 240 => (0.0, x, c), < 300 => (x, 0.0, c), _ => (c, 0.0, x) };
        return ((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    /// RGB a tono (0–360), saturación y valor (0–1).
    public static (double Hue, double Saturation, double Value) ToHsv(byte r, byte g, byte b)
    {
        double rr = r / 255.0, gg = g / 255.0, bb = b / 255.0, max = Math.Max(rr, Math.Max(gg, bb)), min = Math.Min(rr, Math.Min(gg, bb)), d = max - min;
        double hue = d == 0 ? 0 : max == rr ? 60 * (((gg - bb) / d % 6 + 6) % 6) : max == gg ? 60 * ((bb - rr) / d + 2) : 60 * ((rr - gg) / d + 4);
        return (hue, max == 0 ? 0 : d / max, max);
    }
}

/// Los efectos dinámicos de las tiras ELK (los que lleva la propia tira) y las escenas listas para usar.
public static class LedEffects
{
    public static readonly IReadOnlyList<LedEffect> All =
    [
        new(0x87, "Salto RGB", "Saltos"), new(0x88, "Salto de 7 colores", "Saltos"),
        new(0x89, "Fundido RGB", "Fundidos"), new(0x8A, "Fundido de 7 colores", "Fundidos"),
        new(0x8B, "Fundido rojo", "Fundidos"), new(0x8C, "Fundido verde", "Fundidos"), new(0x8D, "Fundido azul", "Fundidos"),
        new(0x8E, "Fundido amarillo", "Fundidos"), new(0x8F, "Fundido cian", "Fundidos"), new(0x90, "Fundido magenta", "Fundidos"), new(0x91, "Fundido blanco", "Fundidos"),
        new(0x92, "Fundido rojo y verde", "Fundidos"), new(0x93, "Fundido rojo y azul", "Fundidos"), new(0x94, "Fundido verde y azul", "Fundidos"),
        new(0x96, "Parpadeo rojo", "Parpadeos"), new(0x97, "Parpadeo verde", "Parpadeos"), new(0x98, "Parpadeo azul", "Parpadeos"),
        new(0x99, "Parpadeo amarillo", "Parpadeos"), new(0x9A, "Parpadeo cian", "Parpadeos"), new(0x9B, "Parpadeo magenta", "Parpadeos"), new(0x9C, "Parpadeo blanco", "Parpadeos"),
    ];

    public static LedEffect? Find(int code) => All.FirstOrDefault(e => e.Code == code);

    /// La clase de movimiento de un efecto: saltos de color, fundidos o parpadeos.
    public static LedEffectKind KindOf(LedEffect e) => e.Group switch { "Saltos" => LedEffectKind.Jump, "Fundidos" => LedEffectKind.Fade, _ => LedEffectKind.Blink };

    private static readonly (string Word, (byte R, byte G, byte B) Rgb)[] Named =
    [
        ("rojo", (0xFF, 0x3B, 0x30)), ("verde", (0x34, 0xC7, 0x59)), ("azul", (0x0A, 0x84, 0xFF)), ("amarillo", (0xFF, 0xD6, 0x0A)),
        ("cian", (0x32, 0xD2, 0xFF)), ("magenta", (0xFF, 0x2D, 0xCE)), ("blanco", (0xFF, 0xFF, 0xFF)),
    ];
    public static readonly IReadOnlyList<(byte R, byte G, byte B)> Rainbow =
        [(0xFF, 0x3B, 0x30), (0xFF, 0xD6, 0x0A), (0x34, 0xC7, 0x59), (0x32, 0xD2, 0xFF), (0xAF, 0x52, 0xDE)];

    /// Los colores que se ven en un efecto, por su nombre: «Parpadeo rojo» rojo, «Fundido rojo y azul» rojo y azul y los de «RGB» o «7 colores», el arcoíris.
    public static IReadOnlyList<(byte R, byte G, byte B)> PaletteOf(LedEffect e)
    {
        var colors = Named.Where(n => e.Key.Contains(n.Word, StringComparison.OrdinalIgnoreCase)).Select(n => n.Rgb).ToList();
        return colors.Count > 0 ? colors : Rainbow;
    }
    public static IReadOnlyList<string> Groups => All.Select(e => e.Group).Distinct().ToList();

    public static readonly IReadOnlyList<LedScene> Scenes =
    [
        new("Fiesta", true, 0, 0, 0, 100, EffectCode: 0x88, Speed: 85),
        new("Ocio", false, 0x8A, 0x4D, 0xFF, 60),
        new("Tranquilo", false, 0xFF, 0x9A, 0x3C, 35),
        new("Pausa para almorzar", false, 0xFF, 0xE2, 0xB0, 100),
        new("Lectura", false, 0xFF, 0xF2, 0xD6, 80),
        new("Noche", false, 0xFF, 0x2A, 0x00, 10),
    ];

    /// Los diez colores rápidos de la tira (rojo, verde, azul, cian, amarillo, blanco, naranja, magenta, violeta y rosa).
    public static readonly IReadOnlyList<(byte R, byte G, byte B)> Swatches =
    [
        (0xFF, 0x2A, 0x2A), (0x4C, 0xFF, 0x4C), (0x2A, 0x2A, 0xFF), (0x4C, 0xF2, 0xFF), (0xFF, 0xF2, 0x33),
        (0xFF, 0xFF, 0xFF), (0xFF, 0x7A, 0x1A), (0xF2, 0x33, 0xFF), (0x8A, 0x4D, 0xFF), (0xFF, 0x4D, 0x9A),
    ];
}
