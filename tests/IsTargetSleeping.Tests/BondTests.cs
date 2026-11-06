using IsTargetSleeping;

internal static class BondTests
{
    public static void Run(Action<string, bool> check)
    {
        var dir = Path.Combine(Path.GetTempPath(), "its-bond-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(dir, "bond.json");
        try
        {
            var day = new DateTime(2026, 11, 3, 12, 0, 0);
            var bond = new Bond(file, () => day);
            check("un vínculo nuevo empieza en el nivel 0 sin XP", bond is { Xp: 0, Level: 0 });
            bond.Record(BondEvent.Chat);
            bond.Record(BondEvent.AgentDone);
            bond.Record(BondEvent.Focus);
            check("cada evento suma su XP (2 + 3 + 5)", bond.Xp == 10);
            for (int i = 0; i < 40; i++) bond.Record(BondEvent.Chat);
            check("los eventos de un tipo tienen tope diario (no se farmea)", bond.Xp == 28);
            check("un archivo se guarda y se recupera", new Bond(file, () => day).Xp == bond.Xp);

            day = day.AddDays(1);
            bool leveled = false;
            for (int i = 0; i < 12; i++) leveled |= bond.Record(BondEvent.Chat);   // 20 más: pasa de 28 a 48
            check("al día siguiente vuelve a contar y se sube de nivel", bond.Xp == 48 && bond.Level == 1 && leveled);
            check("el progreso hacia el siguiente nivel va de 0 a 1", bond.Progress is > 0 and < 1);

            day = day.AddDays(400);
            check("ausentarse no baja el vínculo", new Bond(file, () => day).Xp == 48);

            for (int d = 0; d < 60; d++) { day = day.AddDays(1); bond.Record(BondEvent.Chat); bond.Record(BondEvent.AgentDone); bond.Record(BondEvent.Focus); }
            check("el nivel máximo se alcanza y no pasa de él", bond.Level == Bond.MaxLevel && bond.Progress == 1);

            File.WriteAllText(file, "{ no es json");
            check("un archivo roto empieza de cero sin romper", new Bond(file, () => day).Xp == 0);
            bond.Reset();
            check("reiniciar el vínculo lo borra", bond.Xp == 0 && !File.Exists(file));

            check("la memoria crítica preocupa por encima de todo", PetMoods.Of(12, true, true, true) == PetMood.Worried);
            check("un agente esperando permiso pone alerta", PetMoods.Of(12, true, false, true) == PetMood.Alert);
            check("tras algo bueno está contenta", PetMoods.Of(12, false, false, true) == PetMood.Happy);
            check("de noche tiene sueño", PetMoods.Of(2, false, false, false) == PetMood.Sleepy && PetMoods.Of(23, false, false, false) == PetMood.Sleepy);
            check("de día, sin nada, está tranquila", PetMoods.Of(14, false, false, false) == PetMood.Calm);
            check("todos los humores y niveles tienen texto para el prompt", Enum.GetValues<PetMood>().All(m => PetMoods.PromptLine(m).Length > 10)
                && Enumerable.Range(0, 5).All(l => PetMoods.BondPromptLine(l).Contains("You and the user")));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
