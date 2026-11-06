using System.Globalization;

namespace IsTargetSleeping;

/// Una tira LED guardada: su dirección Bluetooth, su nombre y lo último que se le mandó (la tira no dice cómo está, así que se recuerda).
public sealed class LedDevice(ulong address, string name, LedFamily family)
{
    public ulong Address { get; } = address;
    public string Name { get; set; } = name;
    public LedFamily Family { get; } = family;
    public bool On { get; set; } = true;
    public byte R { get; set; } = 0xFF;
    public byte G { get; set; } = 0xFF;
    public byte B { get; set; } = 0xFF;
    public int Brightness { get; set; } = 100;
    public int Effect { get; set; }
    public int Speed { get; set; } = 50;
    /// La conexión viva con la tira (la crea la interfaz; aquí no se sabe de Bluetooth).
    public object? Link { get; set; }

    public string Serialize() => string.Join("|", Address.ToString("X12"), Name.Replace("|", " "), (int)Family, On ? 1 : 0, $"{R:X2}{G:X2}{B:X2}", Brightness, Effect, Speed);

    public static bool TryParseBluetoothAddress(string? text, out ulong address)
    {
        address = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var compact = text.Replace(":", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        return compact.Length == 12 && ulong.TryParse(compact, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
    }

    public static LedDevice? Parse(string text)
    {
        var p = text.Split('|');
        if (p.Length < 8 || !ulong.TryParse(p[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address) || !int.TryParse(p[2], out var family) || p[4].Length != 6) return null;
        var rgb = Convert.ToUInt32(p[4], 16);
        return new LedDevice(address, p[1], (LedFamily)Math.Clamp(family, 0, 1))
        {
            On = p[3] == "1", R = (byte)(rgb >> 16), G = (byte)(rgb >> 8), B = (byte)rgb,
            Brightness = int.TryParse(p[5], out var b) ? Math.Clamp(b, 0, 100) : 100, Effect = int.TryParse(p[6], out var e) ? e : 0, Speed = int.TryParse(p[7], out var s) ? Math.Clamp(s, 0, 100) : 50,
        };
    }
}
/// Una alarma de las luces: enciende o apaga todas a una hora, cada día, entre semana, el fin de semana o una sola vez. Corren mientras la app está abierta.
public sealed class LedTimer
{
    public bool TurnOn { get; set; }
    public int Minute { get; set; }   // minutos desde las 00:00
    public int Days { get; set; }     // 0 todos, 1 entre semana, 2 fin de semana, 3 una vez
    public bool Enabled { get; set; } = true;

    public static long MinuteStamp(DateTime now) => now.Ticks / TimeSpan.TicksPerMinute;

    public string Serialize() => $"{(TurnOn ? "on" : "off")}|{Minute}|{Days}|{(Enabled ? 1 : 0)}";
    public static LedTimer? Parse(string text)
    {
        var p = text.Split('|');
        return p.Length == 4 && int.TryParse(p[1], out var m) && int.TryParse(p[2], out var d)
            ? new LedTimer { TurnOn = p[0] == "on", Minute = Math.Clamp(m, 0, 1439), Days = Math.Clamp(d, 0, 3), Enabled = p[3] == "1" } : null;
    }
    /// La primera hora propuesta queda al menos 30 minutos por delante y redondeada al siguiente cuarto de hora.
    public static int DefaultMinute(DateTime now)
    {
        var next = now.AddMinutes(30).TimeOfDay.Ticks;
        var quarter = TimeSpan.FromMinutes(15).Ticks;
        return (int)((((next + quarter - 1) / quarter) * quarter % TimeSpan.TicksPerDay) / TimeSpan.TicksPerMinute);
    }

    /// ¿Toca hoy? (Una sola vez: cualquier día.)
    public bool Matches(DateTime now) => Enabled && now.Hour * 60 + now.Minute == Minute && Days switch
    {
        1 => now.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday),
        2 => now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
        _ => true,
    };

    /// Consume una alarma de «una vez» al dispararla; las demás conservan su repetición.
    public bool TakeIfDue(DateTime now)
    {
        if (!Matches(now)) return false;
        if (Days == 3) Enabled = false;
        return true;
    }
}
