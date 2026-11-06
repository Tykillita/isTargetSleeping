using IsTargetSleeping;

internal static class NotchMascotTests
{
    public static void Run(Action<string, bool> check)
    {
        var t0 = new DateTime(2026, 10, 28, 12, 0, 0);
        DateTime At(double s) => t0.AddSeconds(s);

        check("puede verse solo con la mascota activada, el notch cerrado y a la vista, y sin música",
            NotchMascotCycle.IsVisible(true, true, false)
            && !NotchMascotCycle.IsVisible(false, true, false)
            && !NotchMascotCycle.IsVisible(true, false, false)
            && !NotchMascotCycle.IsVisible(true, true, true));

        var cycle = new NotchMascotCycle();
        cycle.Update(true, false, 5, At(0));
        check("con el usuario activo no hay mascota", !cycle.Visible);
        cycle.Update(true, true, 10, At(5));
        check("una actividad con el usuario activo no la trae", !cycle.Visible);

        cycle.Update(true, false, NotchMascotCycle.IdleSeconds + 1, At(60));
        check("tras la inactividad aparece dormida", cycle.Visible && !cycle.Awake);
        cycle.Update(true, true, NotchMascotCycle.IdleSeconds + 5, At(65));
        check("una actividad estando dormida la despierta sin quitarla", cycle.Visible && cycle.Awake);
        cycle.Update(true, false, NotchMascotCycle.IdleSeconds + 5 + NotchMascotCycle.AwakeSeconds + 1, At(65 + NotchMascotCycle.AwakeSeconds + 1));
        check("vuelve a dormirse tras un rato", cycle.Visible && !cycle.Awake);

        cycle.Update(true, false, 0.2, At(100));
        check("al volver el usuario abre los ojos y saluda", cycle.Visible && cycle.Awake && cycle.Greeting);
        cycle.Update(true, false, 0.5, At(100 + NotchMascotCycle.GreetSeconds / 2));
        check("durante el saludo sigue a la vista", cycle.Visible && cycle.Awake);
        cycle.Update(true, false, 0.9, At(100 + NotchMascotCycle.GreetSeconds + 0.1));
        check("tras el saludo se retira", !cycle.Visible && !cycle.Awake && !cycle.Greeting);
        cycle.Update(true, false, 5, At(110));
        check("con el usuario activo no reaparece", !cycle.Visible);

        cycle.Update(true, false, NotchMascotCycle.IdleSeconds + 1, At(200));
        cycle.Update(false, false, NotchMascotCycle.IdleSeconds + 2, At(201));
        check("abrir el notch o poner música la retira al instante", !cycle.Visible && !cycle.Awake);
    }
}
