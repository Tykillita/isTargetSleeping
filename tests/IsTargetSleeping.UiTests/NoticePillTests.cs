using IsTargetSleeping;

internal static class NoticePillTests
{
    public static void Run(Action<string, bool> check)
    {
        var previous = new[] { "brightness", "locks", "bluetooth", "clean", "volume", "pressure", "meeting", "agents", "media" };
        var prefs = new Prefs();
        var migrated = prefs.List(PrefKeys.NotchActivities);
        check("la migración añade el aviso y conserva el orden de las actividades existentes",
            migrated.SequenceEqual([.. previous, "notice"]));
        prefs.SetList(PrefKeys.NotchActivities, previous);
        check("la migración se ejecuta una sola vez y respeta una desactivación posterior",
            !new Prefs().List(PrefKeys.NotchActivities).Contains("notice"));
    }
}
