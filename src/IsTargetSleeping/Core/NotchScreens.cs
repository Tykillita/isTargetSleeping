namespace IsTargetSleeping;

/// Un monitor conectado: su identificador estable (`\\.\DISPLAY1`), su rectángulo y su manejador.
public sealed record ScreenInfo(string Id, Win32.RECT Bounds, bool Primary, IntPtr Handle = default);

/// En qué monitores vive un notch. Lógica pura (sin ventanas) para poder probarla.
public static class NotchScreens
{
    public const string FollowCursor = "cursor", Selected = "selected";

    /// null: el modo «seguir al cursor» (una sola ventana que cambia de monitor con el ratón). Si no, los monitores que llevan notch:
    /// los elegidos que estén conectados (el principal si no hay ninguno), sin repetir los monitores duplicados (mismo rectángulo).
    public static List<ScreenInfo>? Select(IReadOnlyList<ScreenInfo> screens, string? mode, IReadOnlyCollection<string>? chosen)
    {
        if (mode != Selected || screens.Count == 0) return null;
        var picked = screens.Where(s => chosen is not null && chosen.Contains(s.Id)).ToList();
        if (picked.Count == 0) picked = [screens.FirstOrDefault(s => s.Primary) ?? screens[0]];
        var distinct = new List<ScreenInfo>();
        foreach (var screen in picked.OrderByDescending(s => s.Primary))
            if (!distinct.Any(d => d.Bounds.Left == screen.Bounds.Left && d.Bounds.Top == screen.Bounds.Top
                    && d.Bounds.Right == screen.Bounds.Right && d.Bounds.Bottom == screen.Bounds.Bottom))
                distinct.Add(screen);
        return distinct;
    }

    /// Lo que cambia las ventanas del notch: modo y monitores elegidos (para no rehacerlas sin motivo).
    public static string Signature(IReadOnlyList<ScreenInfo>? selection) => selection is null ? FollowCursor : string.Join("|", selection.Select(s => s.Id).Order());

    public static string PreferenceSignature(string? mode, IReadOnlyCollection<string>? chosen)
    {
        var screens = chosen is null ? "" : string.Join("|", chosen.Order(StringComparer.OrdinalIgnoreCase));
        return $"{mode ?? Selected}|{screens}";
    }
}
