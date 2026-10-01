using System.Diagnostics;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping;

public sealed partial class Supervisor
{
    public bool Processing { get; private set; }
    private HashSet<int> modelProtectionPids = [Environment.ProcessId];

    public IReadOnlySet<int> GetProcessProtectionPids()
    {
        var roots = new HashSet<int>(modelProtectionPids) { Environment.ProcessId };
        if (CurrentGame is { Pid: > 0 } game) roots.Add(game.Pid);
        if (ProcessMonitor.LastExternalForegroundPid is { } foreground) roots.Add(foreground);
        return roots;
    }

    public async Task<ProcessActionResult> ExecuteProcessAction(ProcessActionRequest request, bool elevated)
    {
        var now = DateTimeOffset.Now;
        if (Processing || Cleaning || Ollama.IsDemo)
            return new(request.Id, ProcessActionOutcome.Pending, now, now,
                request.Targets.Select(p => new ProcessTargetResult(p, ProcessTargetOutcome.Pending,
                    tr("Hay otra operación de memoria en curso."))).ToArray());
        Processing = true;
        Changed?.Invoke();
        Action<bool>? finishOllama = null;
        bool committed = false;
        try
        {
            var preview = await Task.Run(() => ProcessActions.Preview(request));
            var serverPids = await Task.Run(() => Detector.ServerPids()
                .Where(pid => preview.Processes.Any(p => p.Pid == pid)).ToArray());
            if (serverPids.Length > 0) finishOllama = Ollama.PrepareExternalTermination();
            var result = await MemoryAgent.ExecuteProcessActionAsync(request, elevated);
            while (result.Outcome == ProcessActionOutcome.Pending && result.Completion is { } continuation)
                result = await continuation;
            if (finishOllama != null)
            {
                committed = serverPids.All(pid => result.Items.Any(i => i.Identity.Pid == pid && i.Completed));
                finishOllama(committed);
                finishOllama = null;
            }
            if (result.Items.Any(i => i.Completed))
            {
                foreach (var engine in Engines)
                {
                    bool affected = preview.Processes.Any(p => result.Items.Any(i => i.Identity == p.Identity && i.Completed)
                        && (engine.Id == "llamacpp" ? p.Executable.Equals("llama-server.exe", StringComparison.OrdinalIgnoreCase)
                            : p.Executable.StartsWith("LM Studio", StringComparison.OrdinalIgnoreCase)
                                || p.Executable.Equals("lms.exe", StringComparison.OrdinalIgnoreCase)));
                    if (affected) Game.Touch(engine.Id);
                }
            }
            string subject = string.Join(", ", preview.Processes.Where(p => request.Targets.Contains(p.Identity))
                .Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Take(3));
            Ollama.Stats.Add(new StatEvent(DateTime.Now, StatKind.ProcessTerminated,
                Subject: subject, Detail: request.Mode.ToString(), ProcessAction: result));
            AppLog.Write($"finalizar procesos ({request.Mode}): {result.Outcome} · terminados {result.Items.Count(i => i.Completed)} · restantes {result.Remaining.Count}");
            return result;
        }
        catch (Exception e)
        {
            return new(request.Id, ProcessActionOutcome.Failed, now, DateTimeOffset.Now,
                request.Targets.Select(p => new ProcessTargetResult(p, ProcessTargetOutcome.Failed, e.Message)).ToArray());
        }
        finally
        {
            finishOllama?.Invoke(false);
            Processing = false;
            if (deferredGameExit && !Game.Active) ExitGame();
            await Ollama.Refresh();
            foreach (var engine in Engines) await engine.Refresh();
            Changed?.Invoke();
        }
    }

    public void Stop()
    {
        timer?.Stop();
        cleaningLifetime.Cancel();
        gameCleanGeneration++;
    }
}
