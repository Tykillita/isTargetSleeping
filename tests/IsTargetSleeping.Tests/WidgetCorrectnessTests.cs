namespace IsTargetSleeping;

internal static class WidgetCorrectnessTests
{
    public static void Run(Action<string, bool> check)
    {
        var tenCells = new[] { "team@2x2", "clock@2x2", "system@2" };
        var dragged = WidgetLayout.MoveBefore(tenCells, "notes", "system", WidgetCatalog.MaxHomeCells, home: true);
        check("Home: arrastrar un widget apagado usa el tamaño elegido y respeta el límite exacto",
            WidgetLayout.Area(tenCells) == 10 && WidgetLayout.Area(dragged) == WidgetCatalog.MaxHomeCells
            && dragged.IndexOf("notes") < dragged.IndexOf("system@2"));
        var nearlyFull = tenCells.Append("pomodoro").ToArray();
        check("Home: un widget apagado que excede el espacio no entra al arrastrarlo",
            WidgetLayout.MoveBefore(nearlyFull, "notes", "system", WidgetCatalog.MaxHomeCells, home: true).SequenceEqual(nearlyFull));
        check("Home: cambiar el tamaño recalcula ancho por alto antes de aceptar el cambio",
            WidgetLayout.Area(new[] { "team@2x2", "clock@2x2", "system@2" }) == 10
            && WidgetLayout.Area(WidgetLayout.SetWidth(new[] { "team@2x2", "clock@2x2", "system@2" }, "system", 1,
                WidgetCatalog.MaxHomeCells, home: true)) == 9);

        var hourly = "\"hourly\":{\"time\":[\"2026-10-17T12:00\",\"2026-10-17T13:00\",\"2026-10-17T14:00\",\"2026-10-17T15:00\"],\"temperature_2m\":[27,28,29,30],\"weather_code\":[0,1,2,3]}";
        var forecast = WeatherForecast.Parse("Ciudad", "{\"current\":{\"time\":\"2026-10-17T12:15\",\"temperature_2m\":26,\"weather_code\":45}," + hourly + "}")!;
        check("Clima: conserva el dato actual y la siguiente hora no se marca ni se omite como Ahora",
            forecast.Hours.Count == 4 && forecast.Hours[0] is { IsNow: true, Temperature: 26, Time.Hour: 12, Time.Minute: 15 }
            && forecast.NextHours.Select(h => h.Time.Hour).SequenceEqual(new[] { 13, 14, 15 })
            && forecast.NextHours.All(h => !h.IsNow));
        var cityNight = WeatherForecast.Parse("Ciudad", "{\"current\":{\"time\":\"2026-10-17T02:15\",\"temperature_2m\":26,\"weather_code\":0},"
            + "\"hourly\":{\"time\":[\"2026-10-17T02:00\",\"2026-10-17T03:00\"],\"temperature_2m\":[26,25],\"weather_code\":[0,0]}}")!;
        check("Clima: el icono actual usa la hora local devuelta para la ciudad, no la hora del PC",
            cityNight.Hours[0].Time.Hour == 2 && cityNight.IsNight && cityNight.Hours[0].TimeLabel == "Ahora"
            && cityNight.NextHours.Single().TimeLabel == "03:00");

        var monday = new DateTime(2026, 10, 19, 7, 0, 0);
        var saturday = new DateTime(2026, 10, 24, 7, 0, 0);
        var sunday = new DateTime(2026, 10, 25, 7, 0, 0);
        var weekday = new LedTimer { Minute = 7 * 60, Days = 1 };
        var weekend = new LedTimer { Minute = 7 * 60, Days = 2 };
        check("LED: alarmas de cada día, entre semana y fin de semana coinciden con sus días y hora exacta",
            new LedTimer { Minute = 7 * 60 }.Matches(monday) && new LedTimer { Minute = 7 * 60 }.Matches(sunday)
            && weekday.Matches(monday) && !weekday.Matches(sunday) && weekend.Matches(saturday) && !weekend.Matches(monday)
            && !weekend.Matches(monday.AddMinutes(1)));
        var once = new LedTimer { Minute = 7 * 60, Days = 3 };
        check("LED: una alarma de una vez se consume en su primera coincidencia",
            once.TakeIfDue(sunday) && !once.Enabled && !once.TakeIfDue(monday));
        check("LED: la hora sugerida queda al menos media hora por delante, redondeada hacia arriba y cruza medianoche",
            LedTimer.DefaultMinute(new DateTime(2026, 10, 19, 10, 20, 0)) == 11 * 60
            && LedTimer.DefaultMinute(new DateTime(2026, 10, 19, 10, 35, 0)) == 11 * 60 + 15
            && LedTimer.DefaultMinute(new DateTime(2026, 10, 19, 23, 40, 0)) == 15);

        var root = Path.Combine(Path.GetTempPath(), "its-root-" + Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "its-outside-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "src"));
            Directory.CreateDirectory(outside);
            var inside = Path.Combine(root, "src", "main.cs");
            var external = Path.Combine(outside, "secret.cs");
            File.WriteAllText(inside, "inside");
            File.WriteAllText(external, "outside");
            check("Código: la raíz admite archivos internos y rechaza rutas relativas y absolutas externas",
                CodeTree.IsSafePath(root, inside)
                && !CodeTree.IsSafePath(root, Path.Combine(root, "..", Path.GetFileName(outside), "secret.cs"))
                && !CodeTree.IsSafePath(root, external));
            var link = Path.Combine(root, "linked.cs");
            try
            {
                File.CreateSymbolicLink(link, external);
                check("Código: leer o guardar por un enlace simbólico a otro archivo queda fuera de la raíz", !CodeTree.IsSafePath(root, link));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException or System.ComponentModel.Win32Exception)
            {
                Console.WriteLine("   enlace simbólico no disponible: no se pudo ejecutar el caso de enlace de Código");
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
            try { Directory.Delete(outside, true); } catch { }
        }
    }
}
