namespace IsTargetSleeping;

public enum HudKind { Volume, Brightness, CapsLock, NumLock, Language, Lyric, Bluetooth }

/// Lo que enseña el aviso del sistema dentro del notch (volumen, brillo, Bloq. Mayús, Bloq. Núm, idioma del teclado), como en Nusa:
/// un icono, un título y, según el tipo, una barra (`Fraction`), un estado (`On`) o una etiqueta corta (`Chip`).
public sealed record HudInfo(HudKind Kind, string Title, double? Fraction = null, bool? On = null, string? Chip = null, bool Muted = false, bool Dim = false,
    string? Sub = null, DeviceKind Device = DeviceKind.Other, double? Battery = null)
{
    /// Cuánto se queda a la vista: la tarjeta Bluetooth, con más que contar, se queda más que un volumen.
    public double Seconds => Kind == HudKind.Bluetooth ? 4.5 : HudPicker.ShowSeconds;

    /// Los de barra enseñan solo icono y barra; los demás, el título.
    public bool HasBar => Fraction is not null;

    /// El ancho de la pastilla mientras se enseña: el de Nusa (≈265 px las barras, ≈280 los interruptores y ≈320 el idioma).
    public double Width => Kind switch { HudKind.Language => 320, HudKind.Lyric => 300, HudKind.Bluetooth => 310, HudKind.CapsLock or HudKind.NumLock => 280, _ => 270 };

    /// El alto, más que el de la pastilla de siempre (30); la letra, que se tiñe por abajo, algo más (como en Nusa).
    public double Height => Kind switch { HudKind.Lyric => 42, HudKind.Bluetooth => 52, _ => 37 };
}

public static class HudPicker
{
    /// Cuánto se queda a la vista un aviso del sistema.
    public const double ShowSeconds = 2.2;

    /// De todos los avisos recientes, el último que cambió (y que aún no caducó); null si no hay ninguno.
    public static HudInfo? Latest(IEnumerable<(DateTime At, HudInfo Info)> events, DateTime now) =>
        events.Where(e => (now - e.At).TotalSeconds >= 0 && (now - e.At).TotalSeconds < e.Info.Seconds)
            .OrderByDescending(e => e.At).Select(e => e.Info).FirstOrDefault();
}
