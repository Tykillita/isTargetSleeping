using System.Diagnostics;
using IsTargetSleeping;

internal static class ProcessTests
{
    public static void Run(Action<string, bool> check)
    {
        ProcessSnapshot P(int pid, long created = 100, string? path = @"C:\apps\sample.exe", string? sid = "S-1-5-21-1", int session = 1,
            int? parent = null, long? ram = 100, double? cpu = 0) => new()
        {
            Identity = new(pid, created), Name = "Sample", Executable = "sample.exe", Path = path,
            OwnerSid = sid, User = "user", SessionId = session, ParentPid = parent,
            WorkingSetBytes = ram, PrivateWorkingSetBytes = ram, CommitBytes = 500, CpuPercent = cpu
        };
        var sample = new ProcessSample(DateTimeOffset.UtcNow, 10000, true,
            [P(10), P(11, path: @"c:\APPS\SAMPLE.exe"), P(12, path: @"C:\other\sample.exe"), P(13, sid: "S-1-5-21-2"),
                P(14, session: 2), P(15, path: null), P(16, path: null)]);
        var groups = sample.Groups;
        check("procesos: agrupa por ruta completa, SID y sesión", groups.Count == 6 && groups.Single(g => g.Count == 2).RamBytes == 200);
        check("procesos: ruta inaccesible no agrupa nombres coincidentes", groups.Count(g => g.Path == null) == 2);
        check("procesos: CPU/RAM inaccesibles conservan null", new ProcessSample(DateTimeOffset.UtcNow, 1, true,
            [P(10), P(11, ram: null, cpu: null)]).Groups.Single() is { RamBytes: null, CpuPercent: null });
        check("procesos: RAM residente y compromiso permanecen separados", P(10).WorkingSetBytes == 100 && P(10).CommitBytes == 500);
        var id = new ProcessIdentity(10, 100);
        check("procesos: CPU normalizada por procesadores", ProcessMonitor.CalculateCpuPercent(id, 5, id, 6, 2, 4) == 12.5);
        check("procesos: CPU inicial/desconocida no equivale a cero", ProcessMonitor.CalculateCpuPercent(id, null, id, 6, 2, 4) == null);
        check("procesos: PID reutilizado invalida CPU", ProcessMonitor.CalculateCpuPercent(id, 5, id with { CreatedFileTime = 101 }, 6, 2, 4) == null);
        check("procesos: contador CPU decreciente invalida muestra", ProcessMonitor.CalculateCpuPercent(id, 7, id, 6, 2, 4) == null);
        check("procesos: intervalo CPU inválido conserva null", ProcessMonitor.CalculateCpuPercent(id, 5, id, 6, 0, 4) == null);
        check("procesos: bandeja no reemplaza aplicación externa activa", ProcessMonitor.IsShellSurface("Shell_TrayWnd")
            && ProcessMonitor.IsShellSurface("NotifyIconOverflowWindow") && !ProcessMonitor.IsShellSurface("CabinetWClass"));
        var tree = new[] { P(10, 100), P(11, 110, parent: 10), P(12, 120, parent: 11), P(13, 90, parent: 10), P(14, 100) };
        var request = new ProcessActionRequest(Guid.NewGuid(), ProcessActionMode.Tree, [id]);
        var appRequest = request with { Mode = ProcessActionMode.Application };
        var normalized = ProcessActions.ExpandApplicationGroup(appRequest, sample.Processes);
        check("procesos: confirmación de aplicación incluye nuevos PIDs del mismo grupo", normalized.Targets.Select(p => p.Pid).SequenceEqual(new[] { 10, 11 }));
        check("procesos: aplicación excluye otros ejecutables, usuarios y sesiones", !normalized.Targets.Any(p => p.Pid is 12 or 13 or 14));
        check("procesos: raíz reutilizada no autoriza otros miembros de aplicación", ProcessActions.ExpandApplicationGroup(
            appRequest with { Targets = [id with { CreatedFileTime = 99 }] }, sample.Processes).Targets.SequenceEqual([id with { CreatedFileTime = 99 }]));
        check("procesos: aplicación sin ruta disponible conserva solo su identidad", ProcessActions.ExpandApplicationGroup(
            appRequest with { Targets = [new(15, 100)] }, sample.Processes).Targets.SequenceEqual([new ProcessIdentity(15, 100)]));
        check("procesos: ampliar aplicación no cambia solicitudes individuales", ProcessActions.ExpandApplicationGroup(
            request with { Mode = ProcessActionMode.Single }, sample.Processes).Targets.SequenceEqual([id]));
        bool applicationLimitRejected = false;
        try { ProcessActions.ExpandApplicationGroup(appRequest, Enumerable.Range(10, 1025).Select(pid => P(pid)).ToArray()); }
        catch (ArgumentException) { applicationLimitRejected = true; }
        check("procesos: rechaza grupo de aplicación ampliado mayor de 1024 sin truncar", applicationLimitRejected);
        var expanded = ProcessActions.ExpandTargets(request, tree);
        check("procesos: árbol incluye descendientes y excluye parentesco reutilizado", expanded.Select(p => p.Pid).Order().SequenceEqual(new[] { 10, 11, 12 }));
        check("procesos: acción individual no expande hijos", ProcessActions.ExpandTargets(request with { Mode = ProcessActionMode.Single }, tree).Count == 1);
        check("procesos: raíz reutilizada no autoriza descendientes", ProcessActions.ExpandTargets(request with { Targets = [id with { CreatedFileTime = 99 }] }, tree).Count == 0);
        check("procesos: raíz ya cerrada conserva descendientes con identidad autorizada", ProcessActions.ExpandTargets(request, tree.Skip(1).ToArray())
            .Select(p => p.Pid).Order().SequenceEqual(new[] { 11, 12 }));
        check("procesos: árbol del sistema nunca expande desde PID 0", ProcessActions.ExpandTargets(request with { Targets = [new(0, 0)] },
            [P(0, 0), P(11, 110, parent: 0)]).Count == 1);
        check("procesos: finaliza hojas antes de raíz", ProcessActions.LeafFirst(expanded.Select(p => p.Identity), expanded).Select(p => p.Pid).SequenceEqual(new[] { 12, 11, 10 }));
        check("procesos: exclusiones se amplían a descendientes válidos", ProcessMonitor.ExpandProtection(new HashSet<int> { 10 }, tree).SetEquals([10, 11, 12]));
        check("procesos: resultados parciales conservan denegados", ProcessActions.Summarize([
            new(id, ProcessTargetOutcome.Terminated), new(new(11, 110), ProcessTargetOutcome.Denied)]) == ProcessActionOutcome.Partial);
        check("procesos: procesos ya cerrados no indican trabajo", ProcessActions.Summarize([new(id, ProcessTargetOutcome.AlreadyExited)]) == ProcessActionOutcome.NoWork);
        var result = new ProcessActionResult(Guid.NewGuid(), ProcessActionOutcome.Partial, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            [new(id, ProcessTargetOutcome.Blocked), new(new(11, 110), ProcessTargetOutcome.Denied), new(new(12, 120), ProcessTargetOutcome.Terminated)]);
        check("procesos: solo acceso denegado se ofrece al agente elevado", result.RetryTargets.SequenceEqual([new ProcessIdentity(11, 110)]) && result.Remaining.Count == 2);
        bool limitRejected = false;
        try { ProcessActions.Preview(request with { Targets = Enumerable.Range(1, 1025).Select(n => new ProcessIdentity(n, 100)).ToArray() }); }
        catch (ArgumentException) { limitRejected = true; }
        check("procesos: rechaza solicitudes de más de 1024 identidades", limitRejected);

        if (!OperatingSystem.IsWindows()) return;
        var monitor = new ProcessMonitor();
        var first = monitor.Capture();
        var own = first.Processes.FirstOrDefault(p => p.Pid == Environment.ProcessId);
        check("procesos: primera muestra sin porcentaje CPU", own is { CpuPercent: null } && own.Identity.IsValid);
        Thread.Sleep(50);
        var second = monitor.Capture();
        check("procesos: segunda muestra CPU disponible", second.Processes.Single(p => p.Pid == Environment.ProcessId).CpuPercent.HasValue);
        check("procesos: propia app protegida en lectura", own?.ProtectionReason == "OwnApplication");
        var fallback = new ProcessMonitor(false).Capture().Processes.Single(p => p.Pid == Environment.ProcessId);
        check("procesos: fallback etiqueta residente total sin RAM privada", fallback.WorkingSetBytes >= 0 && fallback.PrivateWorkingSetBytes == null);
        var blocked = ProcessActions.Execute(request with { Targets = [new(0, 0), new(4, 0), own!.Identity] });
        check("procesos: sistema y propia app rechazados antes de finalizar", blocked.Items.Count == 3 && blocked.Items.All(i => i.Outcome == ProcessTargetOutcome.Blocked));
        Fixture(check);
    }

    private static void Fixture(Action<string, bool> check)
    {
        // Only this test-created parent, cmd child and ping grandchild are targeted.
        string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(powershell) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("$child = Start-Process -FilePath $env:ComSpec -ArgumentList '/d /c ping -n 120 127.0.0.1 > nul' -WindowStyle Hidden -PassThru; [Console]::WriteLine($child.Id); [Console]::Out.Flush(); Start-Sleep -Seconds 120");
        using var parent = new Process { StartInfo = start };
        Process? child = null;
        try
        {
            parent.Start();
            var childLine = parent.StandardOutput.ReadLineAsync();
            if (!childLine.Wait(TimeSpan.FromSeconds(10)) || !int.TryParse(childLine.Result, out int childPid))
            { check("procesos: fixture padre/hijo creado", false); return; }
            child = Process.GetProcessById(childPid);
            _ = child.Handle; // Pin the test-owned process for safe cleanup, even if its PID exits.
            var identity = ProcessMonitor.QueryIdentity(parent.Id)!.Value;
            var request = new ProcessActionRequest(Guid.NewGuid(), ProcessActionMode.Tree, [identity]);
            var changed = ProcessActions.Execute(request with { Targets = [identity with { CreatedFileTime = identity.CreatedFileTime + 1 }] });
            check("procesos: revalidación real rechaza PID con hora incorrecta", changed.Items.Single().Outcome == ProcessTargetOutcome.Changed && !parent.HasExited);
            var preview = ProcessActions.Preview(request);
            check("procesos: confirmación real incluye hijo y nieto", preview.Processes.Any(p => p.Pid == childPid) && preview.Processes.Count >= 3);
            var cancelled = ProcessActions.Execute(request, new CancellationToken(true));
            check("procesos: cancelación previa conserva fixture vivo", cancelled.Outcome == ProcessActionOutcome.Cancelled && !parent.HasExited);
            var terminated = ProcessActions.Execute(request);
            bool allEnded = terminated.Outcome == ProcessActionOutcome.Success
                && terminated.Items.Count >= 3 && terminated.Items.All(i => i.Completed) && terminated.Items.Any(i => i.Identity.Pid == childPid);
            if (!allEnded)
                Console.WriteLine($"Fixture de procesos: {terminated.Outcome}, error {terminated.Error ?? "-"}; "
                    + string.Join("; ", terminated.Items.Select(i => $"PID {i.Identity.Pid} {i.Outcome} {i.Reason ?? "-"} {i.Win32Error?.ToString() ?? "-"}")));
            check("procesos: finalización real verifica cada descendiente", allEnded);
            check("procesos: padre e hijo fixture salieron", parent.WaitForExit(5000) && child.WaitForExit(5000));
        }
        catch (Exception e)
        {
            Console.WriteLine($"Fixture de procesos: {e.GetType().Name}: {e.Message}");
            check("procesos: fixture de finalización completado", false);
        }
        finally
        {
            // Managed instances are held handles to processes created exclusively above.
            try { if (!parent.HasExited) { parent.Kill(entireProcessTree: true); parent.WaitForExit(5000); } } catch { }
            try { if (child != null && !child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(5000); } } catch { }
            child?.Dispose();
        }
    }
}
