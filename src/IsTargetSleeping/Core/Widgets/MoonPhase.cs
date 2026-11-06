namespace IsTargetSleeping;

/// La fase de la luna de un día, por el ciclo medio (sin red ni tablas): sirve para dibujarla y nombrarla, con un error de medio día como mucho.
public static class MoonPhase
{
    private const double SynodicMonth = 29.530588853;
    // Una luna nueva de referencia: 6 de enero de 2000, 18:14 UTC.
    private static readonly DateTime NewMoon = new(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);

    /// Dónde va la luna en su ciclo: 0 = nueva, 0,25 = cuarto creciente, 0,5 = llena, 0,75 = cuarto menguante, y de vuelta a 1 = nueva.
    public static double Fraction(DateTime when)
    {
        var days = (when.ToUniversalTime() - NewMoon).TotalDays % SynodicMonth;
        if (days < 0) days += SynodicMonth;
        return days / SynodicMonth;
    }

    /// El nombre de la fase en ocho tramos.
    public static string Name(double fraction) => ((int)Math.Floor(((fraction % 1 + 1) % 1 * 8 + 0.5)) % 8) switch
    {
        0 => L10n.tr("Luna nueva"),
        1 => L10n.tr("Luna creciente"),
        2 => L10n.tr("Cuarto creciente"),
        3 => L10n.tr("Gibosa creciente"),
        4 => L10n.tr("Luna llena"),
        5 => L10n.tr("Gibosa menguante"),
        6 => L10n.tr("Cuarto menguante"),
        _ => L10n.tr("Luna menguante"),
    };
}
