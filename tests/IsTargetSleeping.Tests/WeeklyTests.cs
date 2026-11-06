using IsTargetSleeping;

internal static class WeeklyTests
{
    public static void Run(Action<string, bool> check)
    {
        var dir = Path.Combine(Path.GetTempPath(), "its-week-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(dir, "week.json");
        try
        {
            var day = new DateTime(2026, 11, 4, 10, 0, 0);   // miércoles
            check("el lunes de esa semana es el 2 de noviembre", WeekLog.MondayOf(day) == new DateTime(2026, 11, 2) && WeekLog.MondayOf(new DateTime(2026, 11, 8, 23, 0, 0)) == new DateTime(2026, 11, 2) && WeekLog.MondayOf(new DateTime(2026, 11, 9)) == new DateTime(2026, 11, 9));
            var log = new WeekLog(file, () => day);
            log.Record(WeekEvent.Chat); log.Record(WeekEvent.Chat); log.Record(WeekEvent.Approved);
            check("se cuentan los eventos de la semana", log.Count(WeekEvent.Chat) == 2 && log.Count(WeekEvent.Approved) == 1 && log.Count(WeekEvent.Denied) == 0);
            check("el resumen se guarda y se recupera", new WeekLog(file, () => day).Count(WeekEvent.Chat) == 2);
            day = day.AddDays(3);   // sábado: misma semana
            check("dentro de la misma semana se conserva", new WeekLog(file, () => day).Count(WeekEvent.Chat) == 2);
            day = day.AddDays(2);   // lunes siguiente
            check("el lunes siguiente empieza de cero", new WeekLog(file, () => day).Count(WeekEvent.Chat) == 0);
            File.WriteAllText(file, "{ roto");
            check("un archivo roto empieza de cero sin romper", new WeekLog(file, () => day).Count(WeekEvent.Chat) == 0);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        // Charla ambiente
        var noon = new DateTime(2026, 11, 4, 12, 0, 0);
        var long_ago = noon.AddDays(-1);
        string? Try(ChatterLevel l, PetMood m, int bond, DateTime last, bool quiet, int seed = 1)
        {
            var rnd = new Random(seed);
            for (int i = 0; i < 400; i++) if (Chatter.Pick(l, m, bond, noon, last, quiet, rnd) is { } line) return line;
            return null;
        }
        check("apagada no dice nada nunca", Try(ChatterLevel.Off, PetMood.Calm, 0, long_ago, false) is null);
        check("en silencio (juego, permiso, escuchando) no dice nada", Try(ChatterLevel.Normal, PetMood.Calm, 0, long_ago, true) is null);
        check("con un agente esperando permiso no habla (ya lo pide la tarjeta)", Try(ChatterLevel.Normal, PetMood.Alert, 0, long_ago, false) is null);
        check("respeta el mínimo entre frases", Try(ChatterLevel.Normal, PetMood.Calm, 0, noon.AddMinutes(-49), false) is null && Try(ChatterLevel.Rare, PetMood.Calm, 0, noon.AddMinutes(-119), false) is null);
        check("pasado el mínimo acaba diciendo una frase", Try(ChatterLevel.Normal, PetMood.Calm, 0, noon.AddMinutes(-51), false) is { Length: > 5 });
        check("«rara» espera más que «normal»", Chatter.Gap(ChatterLevel.Rare) > Chatter.Gap(ChatterLevel.Normal));
        check("no habla en cada llamada (es rara)", Enumerable.Range(0, 200).Count(i => Chatter.Pick(ChatterLevel.Normal, PetMood.Calm, 0, noon, long_ago, false, new Random(i)) is not null) < 60);
        check("solo con vínculo alto salen las frases cariñosas", Enumerable.Range(0, 300).Select(i => Chatter.Pick(ChatterLevel.Normal, PetMood.Calm, 0, noon, long_ago, false, new Random(i))).All(l => l is null || !l.StartsWith("Me gusta pasar")));
        check("cada humor (salvo alerta) tiene frases", new[] { PetMood.Calm, PetMood.Sleepy, PetMood.Worried, PetMood.Happy }.All(m => Try(ChatterLevel.Normal, m, 0, long_ago, false) is not null));
        check("se entiende el valor guardado", Chatter.Parse("rare") == ChatterLevel.Rare && Chatter.Parse("normal") == ChatterLevel.Normal && Chatter.Parse(null) == ChatterLevel.Off && Chatter.Parse("x") == ChatterLevel.Off);
    }
}
