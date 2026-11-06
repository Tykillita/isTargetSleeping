using IsTargetSleeping;

internal static class AgentAlertsTests
{
    private static string ReadLine(string file) =>
        "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"name\":\"Read\",\"input\":{\"file_path\":\"" + file + "\"}}]}}";

    private static AgentSnapshot Session(AgentState state, DateTime lastActivity, int repeat = 0, string? subject = null) =>
        new("s1", "C:/proj/app", "Claude Code", state, null, null, null, null, null, [], lastActivity) { RepeatCount = repeat, RepeatSubject = subject };

    public static void Run(Action<string, bool> check)
    {
        var now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        AgentAlert[] Alerts(AgentSnapshot s, params LimitInfo[] limits) => [.. AgentAlerts.Detect([s], limits, now)];

        // — Atascado
        check("sin dar señales 3 minutos es un aviso de atasco", Alerts(Session(AgentState.Working, now.AddMinutes(-3))) is [{ Kind: AgentAlertKind.Stalled, Number: 3 }]);
        check("1 minuto sin señales aún no avisa", Alerts(Session(AgentState.Working, now.AddMinutes(-1))).Length == 0);
        check("pasados 5 minutos ya lo dice su estado «sin actividad»", Alerts(Session(AgentState.Working, now.AddMinutes(-6))).Length == 0);
        check("una sesión terminada o esperando permiso no se marca como atascada", Alerts(Session(AgentState.Finished, now.AddMinutes(-3))).Length == 0 && Alerts(Session(AgentState.NeedsAttention, now.AddMinutes(-3))).Length == 0);

        // — Bucle
        check("seis llamadas seguidas sobre el mismo archivo son un bucle", Alerts(Session(AgentState.Working, now, 6, "Read app.cs")) is [{ Kind: AgentAlertKind.Loop, Number: 6, Subject: "Read app.cs" }]);
        check("cinco aún no", Alerts(Session(AgentState.Working, now, 5, "Read app.cs")).Length == 0);

        // — Cómo se cuenta la repetición desde el log
        var same = Enumerable.Range(0, 7).Select(_ => ReadLine("C:/proj/app/Widget.cs"));
        var looping = AgentLogs.Parse("c1", "app", "Claude Code", same, DateTime.UtcNow);
        check("el log con la misma lectura siete veces cuenta siete", looping is { RepeatCount: 7, RepeatSubject: "Read Widget.cs" });
        var mixed = AgentLogs.Parse("c2", "app", "Claude Code", [ReadLine("C:/a.cs"), ReadLine("C:/b.cs"), ReadLine("C:/b.cs")], DateTime.UtcNow);
        check("solo cuentan las últimas seguidas", mixed.RepeatCount == 2);
        check("sin repetir no hay bucle", AgentLogs.Parse("c3", "app", "Claude Code", [ReadLine("C:/a.cs"), ReadLine("C:/b.cs")], DateTime.UtcNow).RepeatCount == 1);

        // — Límites de uso
        var reset = new DateTimeOffset(now).AddMinutes(75);
        var idle = Session(AgentState.Inactive, now);
        check("un límite al 95 % avisa de que se acaba", AgentAlerts.Detect([idle], [new("Claude", "5 h", 0.95, reset)], now) is [{ Kind: AgentAlertKind.LimitNear, Number: 95 }]);
        check("un límite agotado avisa y dice cuándo vuelve", AgentAlerts.Detect([idle], [new("Claude", "5 h", 1.0, reset)], now) is [{ Kind: AgentAlertKind.LimitHit, Until: { } until }] && until == reset);
        check("por debajo del 90 % no avisa", AgentAlerts.Detect([idle], [new("Claude", "5 h", 0.89, reset)], now).Count == 0);
        check("la cuenta atrás se dice en minutos y en horas", AgentAlerts.Countdown(new DateTimeOffset(now).AddMinutes(12), new DateTimeOffset(now)) == "12 min"
            && AgentAlerts.Countdown(reset, new DateTimeOffset(now)) == "1 h 15 min" && AgentAlerts.Countdown(new DateTimeOffset(now).AddHours(2), new DateTimeOffset(now)) == "2 h"
            && AgentAlerts.Countdown(null, new DateTimeOffset(now)) == "" && AgentAlerts.Countdown(new DateTimeOffset(now).AddMinutes(-1), new DateTimeOffset(now)) == "");
        check("cada aviso tiene una clave estable para no repetirse", Alerts(Session(AgentState.Working, now.AddMinutes(-3)))[0].Key == Alerts(Session(AgentState.Working, now.AddMinutes(-4)))[0].Key);
    }
}
