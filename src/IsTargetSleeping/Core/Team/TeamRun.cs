using static IsTargetSleeping.L10n;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IsTargetSleeping;

public enum Verdict { Approved, Warning, Blocked }
public enum TeamOutcome { Done, NeedsUser, Failed, Cancelled }

public enum StepState { Pending, Running, Done, Failed }
/// Avance de un paso del plan (por su posición): empieza, y termina bien o mal con el agente que de verdad lo hizo.
public sealed record StepProgress(int Index, TeamAgent Agent, StepState State, AgentResult? Result);

public sealed record StepRecord(TeamStep Step, TeamAgent Agent, AgentResult Result, int Attempt);
public sealed record TeamRunResult(TeamOutcome Outcome, string Summary, IReadOnlyList<StepRecord> Records)
{
    public long Tokens => Records.Sum(r => r.Result.InputTokens + r.Result.OutputTokens);
}

/// Ejecuta un plan paso a paso. Los agentes no conversan: cada paso recibe artefactos compactos
/// (tarea, diseño, hallazgos de la revisión) y devuelve texto. Máx. de rondas y de fallos acotados.
public sealed class TeamRun(Func<TeamStep, string, CancellationToken, Task<AgentResult>> exec, int maxRounds = 3, int maxFailures = 2,
    Func<TeamStep, string, CancellationToken, Task<string?>>? gate = null, long maxTokens = 0, string context = "")
{
    // gate: tras planificar o diseñar se enseña el resultado al usuario, que lo aprueba, lo edita (devuelve el texto nuevo) o lo
    // rechaza (null cancela). maxTokens: tope de gasto (entrada nueva + salida) de toda la ejecución; 0 = sin tope. Es lo que
    // permite dejar tareas programadas sin vigilancia: se paran solas y piden decisión.
    // context: lo ya hecho en una ejecución anterior (un seguimiento sobre la misma rama).
    /// Lo oye la interfaz para pintar el avance; llega desde el hilo que ejecuta el plan.
    public event Action<StepProgress>? Progress;
    // El último error de un agente: sin él, «falló el paso» no dice si falta el CLI, se agotó el tiempo o la sesión caducó.
    private string? lastError;

    // shortcut: los pasos Implement consecutivos se ejecutan en serie en un solo worktree; en paralelo
    // exigirían un worktree por rama y fusionarlas (TeamWorktree), cuando midamos que compensa.
    public async Task<TeamRunResult> RunAsync(TeamPlan plan, string task, CancellationToken ct = default)
    {
        var records = new List<StepRecord>();
        // La tarea con lo ya hecho en la rama (si es un seguimiento): lo que sigue a cada plan se añade a esto, no a la tarea sola.
        bool answering = plan.Steps.Any(s => s.Role == TeamRole.Answer);
        string head = context.Length == 0 ? task : answering ? $"Conversación anterior:\n{context}\n\nNueva pregunta: {task}" : $"{task}\n\nContexto de lo ya hecho en esta rama:\n{context}";
        string spec = head, design = "", findings = "";
        var steps = plan.Steps;
        TeamRunResult End(TeamOutcome o, string s) => new(o, s, records);
        bool OverBudget() => maxTokens > 0 && records.Sum(x => x.Result.InputTokens + x.Result.OutputTokens) > maxTokens;
        string Budget() => tr("presupuesto de %@ tokens agotado: decide el usuario", Number(maxTokens));
        string Failure(string what) => lastError is { Length: > 0 } ? $"{what}: {lastError}" : what;

        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (ct.IsCancellationRequested) return End(TeamOutcome.Cancelled, tr("cancelado"));
            if (step.Role == TeamRole.Review) continue; // las revisiones se hacen tras implementar (abajo)

            var r = await Attempt(step, i, Prompt(step, spec, design, findings), records, ct);
            if (r is null) return End(ct.IsCancellationRequested ? TeamOutcome.Cancelled : TeamOutcome.Failed, Failure(tr("falló el paso %@", TeamActivity.RoleName(step.Role))));
            if (step.Role is TeamRole.Clarify or TeamRole.Plan or TeamRole.Design) r = r with { Message = CleanPlan(r.Message) };
            if (gate is not null && step.Role is TeamRole.Clarify or TeamRole.Plan or TeamRole.Design)
            {
                var edited = await gate(step, r.Message, ct);
                if (edited is null) return End(TeamOutcome.Cancelled, tr("el usuario no aprobó el plan"));
                r = r with { Message = edited };
            }
            switch (step.Role)
            {
                case TeamRole.Clarify or TeamRole.Plan: spec = $"{head}\n\n{r.Message}"; break;
                case TeamRole.Design: design = r.Message; break;
            }
            if (OverBudget()) return End(TeamOutcome.NeedsUser, Budget());

            // Tras el último Implement seguido de revisiones, se revisa (y se corrige hasta maxRounds).
            bool lastImplement = step.Role == TeamRole.Implement && (i + 1 >= steps.Count || steps[i + 1].Role != TeamRole.Implement);
            if (!lastImplement) continue;
            var reviewers = steps.Skip(i + 1).TakeWhile(s => s.Role == TeamRole.Review).ToList();
            for (int round = 1; reviewers.Count > 0; round++)
            {
                findings = "";
                var worst = Verdict.Approved;
                for (int k = 0; k < reviewers.Count; k++)
                {
                    var rv = reviewers[k];
                    var rr = await Attempt(rv, i + 1 + k, Prompt(rv, spec, design, ""), records, ct);
                    if (rr is null) return End(ct.IsCancellationRequested ? TeamOutcome.Cancelled : TeamOutcome.Failed, Failure(tr("falló la revisión")));
                    if (OverBudget()) return End(TeamOutcome.NeedsUser, Budget());
                    var v = ParseVerdict(rr.Message);
                    if (v > worst) worst = v;
                    if (v != Verdict.Approved) findings += rr.Message + "\n";
                }
                if (worst == Verdict.Approved) { findings = ""; break; }
                if (round >= maxRounds) return End(TeamOutcome.NeedsUser, $"{worst} tras {maxRounds} rondas: decide el usuario\n{findings}");
                var fix = await Attempt(step, i, Prompt(step, spec, design, findings), records, ct);
                if (fix is null) return End(ct.IsCancellationRequested ? TeamOutcome.Cancelled : TeamOutcome.Failed, Failure(tr("falló la corrección")));
                if (OverBudget()) return End(TeamOutcome.NeedsUser, Budget());
            }
        }
        return End(TeamOutcome.Done, records.LastOrDefault()?.Result.Message ?? "");
    }

    /// Reintenta y, si el agente sigue fallando, escala (Codex→Sonnet→Opus). Null = no se pudo.
    async Task<AgentResult?> Attempt(TeamStep step, int index, string prompt, List<StepRecord> records, CancellationToken ct)
    {
        var agent = step.Agent;
        for (int n = 1; ; n++)
        {
            if (ct.IsCancellationRequested) return null;
            AgentResult r;
            Progress?.Invoke(new(index, agent, StepState.Running, null));
            try { r = await exec(step with { Agent = agent }, prompt, ct); }
            catch (OperationCanceledException) { return null; }
            catch (Exception e) { r = new(false, "", Error: e.Message); }
            records.Add(new(step, agent, r, n));
            if (!r.Ok) lastError = r.Error;
            Progress?.Invoke(new(index, agent, r.Ok ? StepState.Done : StepState.Failed, r));
            if (r.Ok) return r;
            if (r.Permanent) return null;
            if (n % maxFailures == 0)
            {
                // Un revisor escalado nunca pasa a Codex ni a quien escribió: solo sube de Haiku/Codex a Sonnet y de Sonnet a Opus.
                var next = Escalate(agent);
                if (next is null) return null;
                agent = next.Value;
            }
            if (n >= maxFailures * 2) return null;
        }
    }

    public static TeamAgent? Escalate(TeamAgent a) => a switch
    {
        TeamAgent.Haiku or TeamAgent.Codex => TeamAgent.Sonnet,
        TeamAgent.Sonnet => TeamAgent.Opus,
        _ => null,
    };

    /// Quita de un plan lo que no es plan: las disculpas por no poder usar ExitPlanMode y la pregunta final de «¿apruebas?» (el usuario aprueba en el widget).
    public static string CleanPlan(string text)
    {
        // Un plan empieza en su primera sección: lo que el modelo dijo antes («Confirmado: … ya tengo lo necesario») es charla, no plan.
        text = text.Replace("\r\n", "\n");
        var start = Regex.Match(text, @"(?m)^\s*(\*\*|#{1,4}\s*)(Objetivo|Objective|Goal)\b", RegexOptions.IgnoreCase);
        if (start.Success && start.Index > 0) text = text[start.Index..].TrimStart();
        var paragraphs = text.Split("\n\n").ToList();
        paragraphs.RemoveAll(p => p.Contains("ExitPlanMode", StringComparison.OrdinalIgnoreCase) || p.Contains("exit plan mode", StringComparison.OrdinalIgnoreCase));
        while (paragraphs.Count > 1 && paragraphs[^1].Trim() is { } last && last.EndsWith('?') && (last.Contains("aprueb", StringComparison.OrdinalIgnoreCase) || last.Contains("approve", StringComparison.OrdinalIgnoreCase)))
            paragraphs.RemoveAt(paragraphs.Count - 1);
        return string.Join("\n\n", paragraphs).Trim();
    }

    static string Prompt(TeamStep step, string spec, string design, string findings) => step.Role switch
    {
        TeamRole.Clarify => $"Tarea:\n{spec}\n\nSolo lectura. Devuelve en menos de 15 líneas: archivos implicados, criterio de hecho y dudas. No edites nada.",
        TeamRole.Answer => $"{spec}\n\nResponde a lo anterior en el idioma en que está escrito. Empieza por la respuesta en una frase directa. Si hace falta más, añade debajo explicación breve con Markdown sencillo: párrafos cortos, listas con guiones y **negrita** solo en los términos clave; sin títulos grandes ni tablas, y cierra con una pregunta solo si de verdad falta un dato. Si necesitas datos actuales, búscalos en la web. {(step.ReadsProject ? "Si la pregunta es sobre el proyecto de la carpeta actual, lee los archivos que necesites (solo lectura). No edites nada." : "No hay proyecto ni archivos que tocar: no edites nada.")}",
        TeamRole.Plan => $"Tarea:\n{spec}\n\nSolo lectura: no edites ni crees ningún archivo. Explora el código lo necesario y responde con el plan de implementación, escrito aquí mismo (no en un archivo ni con herramientas de plan), en Markdown y en el idioma de la tarea, con estas secciones:\n\n**Objetivo**: una o dos frases.\n**Archivos a tocar**: lista con la ruta real de cada archivo y qué cambia en él.\n**Pasos**: numerados, concretos y en orden (nombra clases, funciones y plantillas que hayas visto).\n**Riesgos y casos límite**: lo que puede salir mal y cómo se evita.\n**Pruebas**: qué se prueba y cómo.\n**Criterio de hecho**: comprobable, no una opinión.\n\nCada paso debe poder hacerse sin esperar una decisión: si algo depende de lo que decida el usuario o queda fuera de lo pedido, no lo pongas como paso; anótalo en «Riesgos». Sé específico y completo, sin relleno. No pidas aprobación ni menciones herramientas: el usuario aprueba el plan en la interfaz.",
        TeamRole.Design => $"Tarea y plan:\n{spec}\n\nSolo lectura: no edites ni crees archivos. Responde aquí mismo, en Markdown, con la especificación visual: jerarquía de la pantalla, medidas, estados (normal, hover, foco, seleccionado, deshabilitado, vacío), animaciones y su duración, tokens de color del tema y textos en español e inglés. Sé concreto: cada decisión con su valor.",
        TeamRole.Implement => $"Tarea y plan:\n{spec}\n{(design.Length > 0 ? $"\nDiseño a seguir:\n{design}\n" : "")}{(findings.Length > 0 ? $"\nCorrige estos hallazgos de la revisión:\n{findings}\n" : "")}\nImplementa solo lo pedido: si el plan trae algún paso condicional, que dependa de una decisión del usuario o que se salga de lo pedido, no lo hagas y menciónalo al final. Al terminar di qué archivos cambiaste y qué pruebas corriste, en menos de 10 líneas.",
        _ => $"Revisa los cambios sin confirmar del repo (ejecuta `git diff HEAD`) contra esta tarea:\n{spec}\n\nResponde SOLO un JSON: {{\"verdict\":\"APPROVED|WARNING|BLOCKED\",\"findings\":[{{\"severity\":\"CRITICAL|HIGH|MEDIUM|LOW\",\"text\":\"...\"}}]}}. BLOCKED = hay algo CRITICAL; WARNING = hay algo HIGH.",
    };

    /// Lee el veredicto del JSON del revisor; si no se entiende, WARNING (no se aprueba por defecto).
    public static Verdict ParseVerdict(string text)
    {
        var m = Regex.Match(text, @"""verdict""\s*:\s*""(?<v>[A-Za-z]+)""", RegexOptions.IgnoreCase);
        var word = m.Success ? m.Groups["v"].Value : text;
        if (Regex.IsMatch(word, "BLOCKED", RegexOptions.IgnoreCase)) return Verdict.Blocked;
        if (Regex.IsMatch(word, "WARNING", RegexOptions.IgnoreCase)) return Verdict.Warning;
        return m.Success && Regex.IsMatch(word, "APPROVED", RegexOptions.IgnoreCase) ? Verdict.Approved : Verdict.Warning;
    }
}

/// Un worktree y una rama por ejecución, fuera del repo del usuario: este no se toca hasta que él fusione o descarte.
public static class TeamWorktree
{
    static string Git => TeamProcess.Find("git") ?? "git";

    static async Task<(int Exit, string Out, string Err, bool TimedOut)> Run(string dir, CancellationToken ct, params string[] args) =>
        await TeamProcess.RunAsync(Git, args, "", dir, TimeSpan.FromMinutes(2), ct);

    /// ¿La carpeta es un repo de git (o un worktree)? `.git` es una carpeta o un archivo.
    public static bool IsRepo(string dir) => Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git"));

    public static async Task<(string? Dir, string? Error)> CreateAsync(string repo, string runId, string baseDir, CancellationToken ct = default)
    {
        var dir = Path.Combine(baseDir, runId);
        // El texto de git va en el idioma del sistema y no dice qué hacer: una carpeta sin repo se explica aparte, y no deja carpetas vacías.
        if (string.IsNullOrWhiteSpace(repo)) return (null, L10n.tr("Elige la carpeta del proyecto."));
        if (!IsRepo(repo)) return (null, L10n.tr("%@ no es un repositorio de git. Elige otra carpeta o escribe una pregunta sencilla: esas no necesitan proyecto.", Path.GetFileName(repo.TrimEnd('\\', '/'))));
        Directory.CreateDirectory(baseDir);
        var r = await Run(repo, ct, "worktree", "add", "-b", $"team/{runId}", dir);
        if (r.Exit == 0) return (dir, null);
        try { if (!Directory.EnumerateFileSystemEntries(baseDir).Any()) Directory.Delete(baseDir); } catch { }
        return (null, r.Err.Trim());
    }

    /// Guarda el trabajo en la rama (Codex y Claude dejan los cambios sin confirmar). Null en Error = hecho; Committed falso = no había cambios.
    public static async Task<(bool Committed, string? Error)> CommitAsync(string dir, string message, CancellationToken ct = default)
    {
        var add = await Run(dir, ct, "add", "-A");
        if (add.Exit != 0) return (false, add.Err.Trim());
        var status = await Run(dir, ct, "status", "--porcelain");
        if (status.Out.Trim().Length == 0) return (false, null);
        // Sin identidad de git configurada, el commit fallaría: se usa una genérica solo en ese caso.
        var who = (await Run(dir, ct, "config", "user.name")).Out.Trim().Length > 0
            ? Array.Empty<string>() : ["-c", "user.name=isTargetSleeping", "-c", "user.email=team@istargetsleeping.local"];
        var commit = await Run(dir, ct, [.. who, "commit", "-m", message]);
        return commit.Exit == 0 ? (true, null) : (false, (commit.Err.Length > 0 ? commit.Err : commit.Out).Trim());
    }

    /// Fusiona la rama en la rama actual del repo; si hay conflictos, deshace y lo cuenta (el repo queda como estaba).
    public static async Task<string?> MergeAsync(string repo, string runId, CancellationToken ct = default)
    {
        var m = await Run(repo, ct, "merge", "--no-ff", "-m", $"Equipo: {runId}", $"team/{runId}");
        if (m.Exit == 0) return null;
        await Run(repo, ct, "merge", "--abort");
        return (m.Err.Length > 0 ? m.Err : m.Out).Trim();
    }

    public static async Task<string?> DiscardAsync(string repo, string dir, string runId, CancellationToken ct = default)
    {
        var r = await Run(repo, ct, "worktree", "remove", "--force", dir);
        if (r.Exit != 0 && Directory.Exists(dir)) return r.Err.Trim();
        var b = await Run(repo, ct, "branch", "-D", $"team/{runId}");
        // La carpeta del proyecto (`<proyecto>-<hash>`) queda vacía al quitar su única copia de trabajo: no se deja huérfana.
        try { if (Path.GetDirectoryName(dir.TrimEnd('\\', '/')) is { Length: > 0 } parent && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent); } catch { }
        return b.Exit == 0 ? null : b.Err.Trim();
    }
}

/// Registro local de la telemetría: una línea JSON por paso. Solo agente, rol, tiempo y tokens (nunca prompts).
public static class TeamLog
{
    public static void Append(string file, string runId, StepRecord r, TimeSpan elapsed)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.AppendAllText(file, JsonSerializer.Serialize(new
            {
                at = DateTime.UtcNow, run = runId, agent = r.Agent.ToString(), role = r.Step.Role.ToString(), attempt = r.Attempt,
                ok = r.Result.Ok, seconds = (int)elapsed.TotalSeconds, input = r.Result.InputTokens, output = r.Result.OutputTokens,
            }) + "\n");
        }
        catch { }
    }
}
