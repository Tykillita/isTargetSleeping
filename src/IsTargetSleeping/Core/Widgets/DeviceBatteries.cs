namespace IsTargetSleeping;

/// El tipo de aparato al que pertenece una batería: decide su icono.
public enum DeviceKind { Laptop, Mouse, Keyboard, Headphones, Speaker, Gamepad, Phone, Watch, Pen, Other }

/// La batería de un aparato: el propio equipo o un accesorio Bluetooth que informa de su nivel.
public sealed record DeviceBattery(string Id, string Name, DeviceKind Kind, int Percent, bool Charging = false);

/// Clasifica y ordena las baterías de los aparatos. Lógica pura (sin Windows): la lectura de verdad vive en `DeviceBatteryReader`.
public static class DeviceKinds
{
    private static readonly string[] MouseWords = ["mouse", "ratón", "raton", "mice", "trackball", "trackpad"];
    private static readonly string[] KeyboardWords = ["keyboard", "teclado", "keychron", "keypad"];
    private static readonly string[] HeadphoneWords = ["headphone", "headset", "earbud", "earphone", "buds", "airpods", "auricular", "audífono", "audifono", "hands-free", "handsfree", "wh-1000", "wf-1000"];
    private static readonly string[] SpeakerWords = ["speaker", "altavoz", "soundbar", "boombox", "bocina", "jbl", "echo dot"];
    private static readonly string[] GamepadWords = ["controller", "gamepad", "mando", "joystick", "xbox", "dualsense", "dualshock", "joy-con", "joycon"];
    private static readonly string[] PhoneWords = ["iphone", "android", "phone", "galaxy", "pixel", "móvil", "movil"];
    private static readonly string[] WatchWords = ["watch", "reloj", "band"];
    private static readonly string[] PenWords = ["stylus", "lápiz", "lapiz", "surface pen", " pen"];

    private static bool Has(string text, string[] words) => words.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// El tipo de un aparato: primero lo que dicen sus hermanos del mismo contenedor de Windows (una clase «Mouse» o «Keyboard»), luego su nombre.
    /// `classes` son las clases de Windows de esos hermanos («Mouse», «Keyboard», «HIDClass», «MEDIA»…); `handsFree`, que lo conoce el perfil de manos libres.
    public static DeviceKind Classify(string name, IEnumerable<string>? siblingNames = null, IEnumerable<string>? classes = null, bool handsFree = false)
    {
        var cls = classes?.ToList() ?? [];
        if (cls.Contains("Mouse", StringComparer.OrdinalIgnoreCase)) return DeviceKind.Mouse;
        if (cls.Contains("Keyboard", StringComparer.OrdinalIgnoreCase)) return DeviceKind.Keyboard;
        var text = string.Join(" ", new[] { name }.Concat(siblingNames ?? []));
        if (Has(text, MouseWords)) return DeviceKind.Mouse;
        if (Has(text, KeyboardWords)) return DeviceKind.Keyboard;
        if (Has(text, GamepadWords)) return DeviceKind.Gamepad;
        if (Has(text, SpeakerWords)) return DeviceKind.Speaker;
        if (Has(text, HeadphoneWords) || handsFree) return DeviceKind.Headphones;
        if (Has(text, PhoneWords)) return DeviceKind.Phone;
        if (Has(text, WatchWords)) return DeviceKind.Watch;
        if (Has(text, PenWords)) return DeviceKind.Pen;
        return DeviceKind.Other;
    }

    /// El nombre sin los sufijos de los perfiles de Bluetooth («Cubitt Headphones Hands-Free AG» → «Cubitt Headphones»).
    public static string CleanName(string name)
    {
        var clean = name.Trim();
        foreach (var suffix in new[] { " Hands-Free AG", " Hands-Free", " Handsfree", " Avrcp Transport", " Stereo", " A2DP", " LE" })
            if (clean.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) clean = clean[..^suffix.Length].TrimEnd();
        return clean;
    }

    private static int Order(DeviceKind kind) => kind switch
    {
        DeviceKind.Laptop => 0, DeviceKind.Phone => 1, DeviceKind.Watch => 2, DeviceKind.Headphones => 3, DeviceKind.Speaker => 4,
        DeviceKind.Mouse => 5, DeviceKind.Keyboard => 6, DeviceKind.Gamepad => 7, DeviceKind.Pen => 8, _ => 9,
    };

    /// La lista final: sin repetidos (un mismo aparato sale por varios perfiles; se queda el primero), con el porcentaje entre 0 y 100,
    /// el propio equipo primero y luego por tipo y nombre.
    public static List<DeviceBattery> Merge(IEnumerable<DeviceBattery> raw) =>
        raw.Where(d => d.Percent is >= 0 and <= 100 && d.Name.Length > 0)
            .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderBy(d => Order(d.Kind)).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
}
