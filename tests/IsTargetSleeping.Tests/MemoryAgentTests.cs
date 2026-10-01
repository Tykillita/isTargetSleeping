using System.Text.Json;
using IsTargetSleeping;

internal static class MemoryAgentTests
{
    public static void Run(Action<string, bool> check)
    {
        Guid id = Guid.NewGuid();
        var identity = new ProcessIdentity(1234, 133000000000000000);
        var request = new CleanSpec(CleanAreas.WorkingSets, [5678])
        { RequestId = id, Selective = true, KeepIdentities = [identity], OwnerSid = "S-1-5-21-1-2-3-1001", SessionId = 2 };
        var parsed = CleanSpec.Parse(request.Format());
        check("protocolo v2 conserva identidad, solicitud y modo selectivo", parsed is { } spec
            && spec.RequestId == id && spec.Selective && spec.OwnerSid == request.OwnerSid && spec.SessionId == 2
            && spec.KeepIdentities.SequenceEqual(request.KeepIdentities) && spec.Keep.SequenceEqual(request.Keep));
        check("protocolo legado de zonas y exclusiones sigue válido", CleanSpec.Parse("a=e7;k=1234,5678;x=memreduct")
            is { Areas: CleanAreas.Default, CloseMemReduct: true, Keep.Count: 2 });
        var many = new CleanSpec(CleanAreas.WorkingSets, Enumerable.Range(1, 1024).ToArray());
        check("1024 exclusiones se conservan sin truncarlas", CleanSpec.Parse(many.Format()) is { Keep.Count: 1024 });
        check("1025 exclusiones fallan al formar la solicitud", Throws(() =>
            new CleanSpec(CleanAreas.WorkingSets, Enumerable.Range(1, 1025).ToArray()).Format()));
        check("límite del protocolo cuenta identidades y PIDs juntos", CleanSpec.Parse("a=1;k="
            + string.Join(',', Enumerable.Range(1, 1024)) + ";i=1:123") is null);
        check("protocolo rechaza campos repetidos, Unicode e identidad inválida",
            CleanSpec.Parse("a=1;r=" + id.ToString("N") + ";r=" + id.ToString("N")) is null
            && CleanSpec.Parse("a=1;k=１２") is null && CleanSpec.Parse("a=1;i=123:0") is null
            && CleanSpec.Parse("a=1;i=0:123") is null && CleanSpec.Parse("a=1;k=1\n") is null);
        check("modo selectivo exige sesión y SID", CleanSpec.Parse("a=1;m=selective") is null
            && CleanSpec.Parse("a=1;m=selective;u=S-1-5-18;s=0") is { Selective: true });
        check("solicitud grande nunca se acepta", CleanSpec.Parse("a=1;k=" + new string('1', CleanSpec.MaxLength)) is null);

        var termination = new ProcessActionRequest(id, ProcessActionMode.Tree, [identity]);
        var terminationParsed = ProcessActionProtocol.Parse(ProcessActionProtocol.Format(termination));
        check("protocolo UAC conserva el alcance y las identidades", terminationParsed is not null
            && terminationParsed.Id == id && terminationParsed.Mode == ProcessActionMode.Tree
            && terminationParsed.Targets.SequenceEqual(termination.Targets));
        check("la tarea de limpieza nunca acepta finalizar procesos",
            CleanSpec.Parse(ProcessActionProtocol.Format(termination)) is null
            && ProcessActionProtocol.Parse(request.Format()) is null);
        check("finalización rechaza modos desconocidos y entradas adicionales",
            ProcessActionProtocol.Parse($"r={id:N};m=9;i=123:456") is null
            && ProcessActionProtocol.Parse($"r={id:N};m=0;i=123:456;cmd=calc") is null
            && ProcessActionProtocol.Parse($"r={id:N};m=0;i=123:-456") is null);
        check("finalización rechaza más de 1024 identidades", Throws(() =>
            ProcessActionProtocol.Format(termination with { Targets = Enumerable.Repeat(identity, 1025).ToArray() })));
        check("finalización limita también el tamaño total", ProcessActionProtocol.Parse(
            $"r={id:N};m=1;i=" + new string('0', CleanSpec.MaxLength) + "1:123") is null);

        var before = new CleanMemorySample(DateTimeOffset.UtcNow, 3000, 1000, 4000, 8000, 75, false);
        var clean = new CleanResult(-321, CleanAreas.ModifiedFileCache, null)
        { RequestId = id, Outcome = CleanOutcome.Partial, Requested = CleanAreas.Default, DurationMilliseconds = 2400,
            ProcessesTreated = 3, ProcessesFailed = 1, Before = before, Immediate = before,
            Errors = [new("WorkingSets", 123, Win32Error: 5), new("SystemFileCache", NtStatus: unchecked((int)0xC0000022))],
            Completion = Task.FromResult(new CleanResult(0, CleanAreas.None, null)) };
        string json = JsonSerializer.Serialize(clean, AgentJsonContext.Default.CleanResult);
        var roundtrip = JsonSerializer.Deserialize(json, AgentJsonContext.Default.CleanResult);
        check("informe v2 conserva diferencias negativas, resultados y códigos", roundtrip is { Version: 2, Freed: -321,
            Outcome: CleanOutcome.Partial, ProcessesTreated: 3, ProcessesFailed: 1 }
            && roundtrip.RequestId == id && roundtrip.Errors[0].Win32Error == 5
            && roundtrip.Errors[1].NtStatus == unchecked((int)0xC0000022) && roundtrip.Before == before);
        check("informe serializado excluye tareas de seguimiento", !json.Contains("Completion", StringComparison.Ordinal)
            && roundtrip?.Completion is null);
        var now = DateTimeOffset.UtcNow;
        var processReport = new ProcessActionResult(id, ProcessActionOutcome.Partial, now, now,
            [new(identity, ProcessTargetOutcome.Denied, "Access denied", 5)]);
        var processRoundtrip = JsonSerializer.Deserialize(JsonSerializer.Serialize(processReport,
            AgentJsonContext.Default.ProcessActionResult), AgentJsonContext.Default.ProcessActionResult);
        check("informe UAC conserva objetivos denegados para reintentar", processRoundtrip is { Outcome: ProcessActionOutcome.Partial }
            && processRoundtrip.Id == id && processRoundtrip.RetryTargets.SequenceEqual([identity]));
        check("nombres de informe dependen únicamente del GUID", Path.GetFileName(AgentReports.CleanPath(id)) == $"clean-{id:N}.json"
            && Path.GetFileName(AgentReports.TerminatePath(id)) == $"terminate-{id:N}.json");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(clean, AgentJsonContext.Default.CleanResult);
        check("informes de limpieza correlacionan GUID y versión", AgentReports.DecodeClean(bytes, id)?.RequestId == id
            && AgentReports.DecodeClean(bytes, Guid.NewGuid()) is null
            && AgentReports.DecodeClean(JsonSerializer.SerializeToUtf8Bytes(clean with { Version = 1 },
                AgentJsonContext.Default.CleanResult), id) is null);
        check("lectura de informes rechaza tamaño y JSON inválido", AgentReports.DecodeClean(new byte[AgentReports.MaxReportBytes + 1], id) is null
            && AgentReports.DecodeClean("{invalid"u8, id) is null && AgentReports.DecodeTerminate("null"u8, id) is null);
        var envelope = new AgentTerminationReport(2, id, processReport);
        check("informes UAC requieren versión y GUID internos coincidentes", AgentReports.DecodeTerminate(JsonSerializer.SerializeToUtf8Bytes(envelope,
            AgentJsonContext.Default.AgentTerminationReport), id)?.Id == id
            && AgentReports.DecodeTerminate(JsonSerializer.SerializeToUtf8Bytes(envelope with { RequestId = Guid.NewGuid() },
                AgentJsonContext.Default.AgentTerminationReport), id) is null);
        check("agente activo después de 30 segundos sigue en espera", AgentRunTracking.Evaluate(id,
            TimeSpan.FromSeconds(31), false, null) == AgentTrackingState.Waiting
            && AgentRunTracking.Evaluate(id, TimeSpan.FromSeconds(129), false, null) == AgentTrackingState.Waiting);
        check("agente activo pasa a pendiente a los 130 segundos", AgentRunTracking.Evaluate(id,
            TimeSpan.FromSeconds(130), false, null) == AgentTrackingState.Pending);
        check("resultado tardío completa únicamente su propia solicitud", AgentRunTracking.Evaluate(id,
            TimeSpan.FromSeconds(150), false, clean) == AgentTrackingState.Completed
            && AgentRunTracking.Evaluate(Guid.NewGuid(), TimeSpan.FromSeconds(150), false, clean) == AgentTrackingState.Pending);
        check("instancia terminada sin informe se distingue de espera", AgentRunTracking.Evaluate(id,
            TimeSpan.FromSeconds(20), true, null) == AgentTrackingState.MissingReport
            && AgentRunTracking.Evaluate(id, TimeSpan.FromSeconds(1), true, null) == AgentTrackingState.Waiting);

        ProcessSnapshot Candidate(int pid, double? cpu, long ram = CleanSelection.MinimumResidentBytes) => new()
        { Identity = new(pid, 100000 + pid), Name = "app", Executable = "app.exe", OwnerSid = "S-1-5-21-1", SessionId = 1,
            IsCritical = false, WorkingSetBytes = ram, CpuSeconds = cpu };
        var start = new ProcessSample(now, 1024L * 1024 * 1024, false, [Candidate(100, 2)]);
        var end = start with { CapturedAt = now.AddSeconds(2), Processes = [Candidate(100, 2.02)] };
        var sid = "S-1-5-21-1";
        check("selección automática acepta exactamente 128 MiB y 1 % de un núcleo", CleanSelection.Select(start, end, sid, 1,
            new HashSet<int>(), 2).Select(p => p.Pid).SequenceEqual([100]));
        check("selección exige mismo usuario y sesión", CleanSelection.Select(start, end, "S-1-5-21-2", 1, new HashSet<int>(), 2).Count == 0
            && CleanSelection.Select(start, end, sid, 2, new HashSet<int>(), 2).Count == 0);
        check("selección rechaza RAM baja, actividad y CPU desconocida", new[]
        { Candidate(100, 2, CleanSelection.MinimumResidentBytes - 1), Candidate(100, 2.021), Candidate(100, null) }
            .All(p => CleanSelection.Select(start, end with { Processes = [p] }, sid, 1, new HashSet<int>(), 2).Count == 0));
        check("selección no actúa sobre identidad reutilizada ni proceso protegido", CleanSelection.Select(start,
            end with { Processes = [Candidate(100, 2) with { Identity = new(100, 999999) }] }, sid, 1, new HashSet<int>(), 2).Count == 0
            && CleanSelection.Select(start, end, sid, 1, new HashSet<int> { 100 }, 2).Count == 0
            && CleanSelection.Select(start, end with { Processes = [Candidate(100, 2) with { ProtectionReason = "Foreground" }] },
                sid, 1, new HashSet<int>(), 2).Count == 0);
        check("selección rechaza criticidad desconocida", CleanSelection.Select(start,
            end with { Processes = [Candidate(100, 2) with { IsCritical = null }] }, sid, 1, new HashSet<int>(), 2).Count == 0);
        var bulkStart = start with { Processes = Enumerable.Range(100, 20).Select(pid => Candidate(pid, 0,
            CleanSelection.MinimumResidentBytes + pid)).ToArray() };
        var bulkEnd = bulkStart with { CapturedAt = now.AddSeconds(2) };
        var selected = CleanSelection.Select(bulkStart, bulkEnd, sid, 1, new HashSet<int>(), 2);
        check("selección limita diez y prioriza quienes más RAM ocupan", selected.Count == 10
            && selected.Select(p => p.Pid).SequenceEqual(Enumerable.Range(110, 10).Reverse()));
        check("recortes paran cuando desaparece presión física y conservan aviso del kernel",
            !CleanSelection.ShouldContinue(before with { PhysicalLoad = 84, LowMemory = false })
            && CleanSelection.ShouldContinue(before with { PhysicalLoad = 85, LowMemory = false })
            && CleanSelection.ShouldContinue(before with { PhysicalLoad = 70, LowMemory = true }));
        check("selección tolera solo contadores de CPU válidos", !CleanSelection.LowActivity(2, 1, 2)
            && !CleanSelection.LowActivity(0, 0, 0) && !CleanSelection.LowActivity(0, double.NaN, 2));
    }
    private static bool Throws(Action action)
    {
        try { action(); return false; } catch (ArgumentException) { return true; }
    }
}
