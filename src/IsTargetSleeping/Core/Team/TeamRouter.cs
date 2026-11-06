using System.Text.Json;
using System.Text.RegularExpressions;

namespace IsTargetSleeping;

public enum TeamPreset { Auto, Saving, Quality, Design, Speed, ClaudeOnly, CodexOnly }
public enum TeamAgent { Haiku, Sonnet, Opus, Codex }
public enum TeamRole { Clarify, Plan, Design, Implement, Review, Answer }
public enum TaskKind { Unknown, Bug, Feature, Refactor, Test, Docs, Migration, Polish, Redesign, Question }

/// Un paso del plan. <see cref="CanWrite"/> falso = sandbox de solo lectura.
public sealed record TeamStep(TeamAgent Agent, TeamRole Role, bool CanWrite, int TimeoutMinutes, string Why,
    CodexTier Tier = CodexTier.Standard, string? Model = null, string? Effort = null)
{
    /// Imágenes de referencia que ve este paso (rutas); null = ninguna.
    public IReadOnlyList<string>? Images { get; init; }
    /// Un paso Answer que puede leer la carpeta del proyecto elegido (solo lectura): preguntas como «¿qué hace este programa?».
    public bool ReadsProject { get; init; }
    /// El agente propio que ejecuta este paso (instrucciones, herramientas y modelo salen de su perfil); null = paso normal de Team.
    public AgentProfile? Profile { get; init; }
}

/// Lo que el router sabe de la tarea, calculado en local (sin llamar a ningún modelo).
public sealed record TaskProfile(TaskKind Kind, bool Visual, int Files, bool Clear, bool Risky);

public sealed record TeamPlan(TaskProfile Profile, TeamPreset Preset, IReadOnlyList<TeamStep> Steps);

/// Umbrales y palabras clave del router. Los valores por defecto son hipótesis (no hay benchmark de diseño
/// que separe a Claude y Codex): se ajustan con un archivo propio y, después, con la telemetría.
public sealed record TeamPolicy
{
    public int SmallFiles { get; init; } = 2;
    public int BigFiles { get; init; } = 6;
    public int ClearChars { get; init; } = 200;
    public string[] Redesign { get; init; } = ["rediseñ", "redesign", "replantea", "rethink", "nueva interfaz", "new interface", "rework the ui", "from scratch"];
    public string[] Polish { get; init; } = ["pule", "pulir", "polish", "alinea", "align", "padding", "margen", "margin", "espaciado", "spacing", "color", "tooltip", "ajusta el botón", "tweak"];
    public string[] Bug { get; init; } = ["bug", "error", "falla", "crash", "arregla", "corrige", "fix", "no funciona", "broken"];
    public string[] Test { get; init; } = ["prueba", "test", "cobertura", "coverage"];
    public string[] Docs { get; init; } = ["readme", "documenta", "document ", "changelog", "comentario", "docs"];
    public string[] Migration { get; init; } = ["migra", "migrate", "migración", "migration", "upgrade", "actualiza a"];
    public string[] Refactor { get; init; } = ["refactor", "renombra", "rename", "limpia", "extrae", "simplifica", "simplify"];
    public string[] Feature { get; init; } = ["agrega", "añade", "implementa", "nueva función", "feature", "add ", "implement", "nuevo", "new "];
    public string[] VisualWords { get; init; } = ["botón", "button", "interfaz", "ui ", "layout", "pantalla", "screen", "icono", "icon", "tema", "theme", "xaml", "css", "diseño", "design"];
    public string[] VisualExtensions { get; init; } = [".xaml", ".css", ".html", ".svg"];
    public string[] Acceptance { get; init; } = ["debe ", "should ", "hecho cuando", "done when", "criterio", "acceptance", "tiene que", "must "];
    public string[] Risk { get; init; } = ["seguridad", "security", "auth", "contraseña", "password", "token", "instalador", "installer", "borrar", "delete", "admin", "agent_version", "firma", "signing"];
    public string[] RiskPaths { get; init; } = ["IsTargetSleeping.Agent", "packaging", "installer"];

    public static TeamPolicy Default { get; } = new();

    /// Lee un archivo de política; si no existe o está roto, usa los valores por defecto.
    public static TeamPolicy Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<TeamPolicy>(File.ReadAllText(path)) ?? Default : Default; }
        catch { return Default; }
    }
}

public static class TeamRouter
{
    static readonly Regex FileToken = new(@"[\w\\/.-]+\.(cs|xaml|css|html|js|ts|json|md|ps1|csproj|svg|iss)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Una pregunta suelta («¿qué significa otorrinolaringólogo?»): arranca como pregunta, no pide acciones sobre un proyecto ni nombra archivos.
    // «código», «función» y «script» no están: «dame 3 paletas con códigos hex» es una pregunta, y las preguntas sobre el código de un proyecto se responden
    // leyendo su carpeta (TeamStep.ReadsProject); lo que pide cambios lo delata el verbo.
    // shortcut: lista de palabras en español e inglés; una pregunta rara sobre código sin esas palabras se tratará como tarea (y pedirá proyecto).
    static readonly Regex QuestionStart = new(@"^\s*[¿?]|\?\s*$|^\s*(qu[eé]|c[oó]mo|cu[aá]l(es)?|cu[aá]nt[oa]s?|cu[aá]ndo|d[oó]nde|qui[eé]n(es)?|por qu[eé]|para qu[eé]|define|explica|expl[ií]came|dime|dame|traduce|resume|diferencia|significa|what|how|why|when|where|who|which|explain|tell me|translate|summari[sz]e|meaning)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    static readonly string[] ProjectWords =
    [
        "arregl", "corrig", "implement", "añad", "agreg", "crea ", "crear", "refactor", "rediseñ", "pule", "pulir", "elimin", "borra", "migr", "actualiz", "instal",
        "ejecut", "compil", "optimiz", "renombr", "commit", "merge", "deploy", "fix ", "add ", "create", "redesign", "polish", "delete", "remove", "migrate",
        "update", "install", "build", "rename", "bug", "repositor", "proyecto", "project", "archivo", "carpeta", "folder",
        "xaml", "pruebas", "tests",
    ];

    static readonly Regex ProjectRef = new(@"\b(este|esta|mi|el|la|this|my|the)\s+(programa|app|aplicaci[oó]n|proyecto|c[oó]digo|repo|repositorio|web|p[aá]gina|sitio|script|funci[oó]n|clase|archivo|program|application|project|code|repository|website|page|function|class|file)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// ¿La pregunta habla del proyecto elegido («¿qué hace este programa?»)? Entonces quien responde puede leer su carpeta; si no, sigue sin ver ningún proyecto.
    public static bool RefersToProject(string prompt) => ProjectRef.IsMatch(prompt);

    public static bool IsQuestion(string prompt, IReadOnlyCollection<string> named)
    {
        var text = prompt.ToLowerInvariant();
        return named.Count == 0 && prompt.Trim().Length is > 0 and <= 300 && !prompt.Contains('`') && !prompt.Contains('{') && prompt.Count(c => c == '\n') <= 2
            && QuestionStart.IsMatch(prompt) && !ProjectWords.Any(text.Contains);
    }

    public static TaskProfile Profile(string prompt, IReadOnlyCollection<string>? files = null, TeamPolicy? policy = null)
    {
        policy ??= TeamPolicy.Default;
        var text = prompt.ToLowerInvariant();
        var named = (files ?? []).Concat(FileToken.Matches(prompt).Select(m => m.Value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        bool Has(string[] words) => words.Any(text.Contains);
        if (IsQuestion(prompt, named)) return new TaskProfile(TaskKind.Question, false, 0, true, false);

        // El orden importa: «rediseñar» gana a «pulir», y «arregla» a «añade».
        var kind = Has(policy.Redesign) ? TaskKind.Redesign
            : Has(policy.Migration) ? TaskKind.Migration
            : Has(policy.Polish) ? TaskKind.Polish
            : Has(policy.Bug) ? TaskKind.Bug
            : Has(policy.Refactor) ? TaskKind.Refactor
            : Has(policy.Test) ? TaskKind.Test
            : Has(policy.Docs) ? TaskKind.Docs
            : Has(policy.Feature) ? TaskKind.Feature
            : TaskKind.Unknown;

        bool visual = kind is TaskKind.Redesign or TaskKind.Polish || Has(policy.VisualWords)
            || named.Any(f => policy.VisualExtensions.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase)));
        bool clear = named.Count > 0 && (Has(policy.Acceptance) || prompt.Length >= policy.ClearChars);
        bool risky = Has(policy.Risk) || named.Any(f => policy.RiskPaths.Any(p => f.Contains(p, StringComparison.OrdinalIgnoreCase)));
        return new TaskProfile(kind, visual, named.Count, clear, risky);
    }

    /// Revisión propia: quien revisa escribió todo lo que revisa. En un rediseño Claude y Codex se revisan entre sí, y eso no lo es.
    public static bool SelfReviewed(IEnumerable<TeamStep> steps)
    {
        var list = steps.ToList();
        var writers = list.Where(s => s.Role == TeamRole.Implement).Select(s => s.Agent).ToList();
        return writers.Count > 0 && list.Any(s => s.Role == TeamRole.Review && writers.All(w => w == s.Agent));
    }

    /// Los pasos de un seguimiento sobre una rama ya hecha: implementa quien implementó y revisa el otro modelo.
    public static IReadOnlyList<TeamStep> FollowUpSteps(TeamAgent implementer) =>
    [
        new(implementer, TeamRole.Implement, true, 30, "seguimiento: cambios pedidos sobre la misma rama", CodexTier.Standard),
        new(implementer == TeamAgent.Codex ? TeamAgent.Sonnet : TeamAgent.Codex, TeamRole.Review, false, 10, "revisión cruzada sobre el diff", CodexTier.Standard),
    ];

    /// Lo que se puede elegir para un paso, en el orden de la rotación: Haiku, Sonnet, Opus y cada modelo de Codex (sin caché, solo «Codex»).
    public static IReadOnlyList<(TeamAgent Agent, string? Slug)> StepChoices(IReadOnlyList<CodexModel> models)
    {
        var list = new List<(TeamAgent, string?)> { (TeamAgent.Haiku, null), (TeamAgent.Sonnet, null), (TeamAgent.Opus, null) };
        if (models.Count == 0) list.Add((TeamAgent.Codex, null));
        else list.AddRange(models.Select(m => (TeamAgent.Codex, (string?)m.Slug)));
        return list;
    }

    /// El siguiente en la rotación al pulsar el icono de un paso. null = volver a lo que eligió el router (el último de la vuelta).
    /// Desde «lo del router» (no elegido a mano) se empieza por el primero.
    public static (TeamAgent Agent, string? Slug)? NextChoice(bool chosenByHand, TeamAgent agent, string? slug, IReadOnlyList<CodexModel> models)
    {
        var choices = StepChoices(models);
        int position = chosenByHand ? choices.ToList().FindIndex(c => c.Agent == agent && (agent != TeamAgent.Codex || c.Slug == slug)) : choices.Count;
        if (position < 0) position = choices.Count;
        int next = (position + 1) % (choices.Count + 1);
        return next == choices.Count ? null : choices[next];
    }

    /// Si quien respondería una pregunta no está disponible (no instalado o sin sesión) y el otro modelo barato sí, responde el otro:
    /// Haiku y el Codex ligero se cubren el uno al otro.
    public static IReadOnlyList<TeamStep> AnswerWith(IReadOnlyList<TeamStep> steps, bool claudeOk, bool codexOk)
    {
        if (steps is not [{ Role: TeamRole.Answer } only]) return steps;
        bool swap = only.Agent == TeamAgent.Codex ? !codexOk && claudeOk : !claudeOk && codexOk;
        return swap ? [only with { Agent = only.Agent == TeamAgent.Codex ? TeamAgent.Haiku : TeamAgent.Codex }] : steps;
    }

    public static TeamPlan Route(TaskProfile p, TeamPreset preset = TeamPreset.Auto, TeamPolicy? policy = null)
    {
        policy ??= TeamPolicy.Default;
        if (p.Kind == TaskKind.Question)
        {
            // Un solo agente, sin proyecto, sin rama y sin revisión: Haiku (rápido y barato); Calidad, Sonnet; Ahorro y Solo Codex, Codex ligero.
            var answerer = preset is TeamPreset.Saving or TeamPreset.CodexOnly ? TeamAgent.Codex : preset == TeamPreset.Quality ? TeamAgent.Sonnet : TeamAgent.Haiku;
            return new TeamPlan(p, preset, [new TeamStep(answerer, TeamRole.Answer, false, 4, "pregunta sencilla: se responde sin proyecto ni rama", CodexTier.Light)]);
        }
        var small = p.Files is > 0 && p.Files <= policy.SmallFiles;
        // Qué modelo de Codex hace falta: lo exigente o de riesgo, el más potente; lo fácil, el barato; el resto, el de diario.
        var tier = p.Risky || p.Files >= policy.BigFiles || p.Kind == TaskKind.Migration ? CodexTier.Frontier
            : p.Kind is TaskKind.Test or TaskKind.Docs || small && p.Kind is not (TaskKind.Redesign or TaskKind.Feature) ? CodexTier.Light : CodexTier.Standard;
        const int Short = 5, Think = 15, Build = 30, Check = 10;
        // Aclarar solo lee y resume: lo hace el modelo más barato (Haiku; en Ahorro, el Codex ligero), no Sonnet.
        TeamStep Clarify() => new(preset == TeamPreset.Saving ? TeamAgent.Codex : TeamAgent.Haiku, TeamRole.Clarify, false, Short, "falta criterio de hecho o archivos: se aclara antes de gastar, con el modelo más barato", CodexTier.Light);
        TeamStep Plan(TeamAgent a, string why) => new(a, TeamRole.Plan, false, Think, why, tier);
        TeamStep Design(TeamAgent a) => new(a, TeamRole.Design, false, Think, "diseño nuevo: jerarquía, estados y tokens antes de codificar", tier);
        TeamStep Do(TeamAgent a, string why) => new(a, TeamRole.Implement, true, Build, why, tier);
        // Revisión cruzada: nunca revisa el mismo modelo que escribió.
        TeamStep Review(TeamAgent author) => new(
            author == TeamAgent.Codex ? (p.Risky ? TeamAgent.Opus : TeamAgent.Sonnet) : TeamAgent.Codex,
            TeamRole.Review, false, Check, "revisión cruzada sobre el diff", tier == CodexTier.Light ? CodexTier.Standard : tier); // quien revisa no baja del nivel estándar

        var bigPlanner = p.Risky || p.Kind == TaskKind.Migration || p.Files >= policy.BigFiles ? TeamAgent.Opus : TeamAgent.Sonnet;
        var steps = new List<TeamStep>();
        if (preset != TeamPreset.ClaudeOnly && preset != TeamPreset.CodexOnly && preset != TeamPreset.Speed
            && !p.Clear && p.Kind != TaskKind.Redesign && !(p.Kind == TaskKind.Polish && (small || p.Files == 0))) // el plan de Opus ya aclara un rediseño
            steps.Add(Clarify());

        void Logic(string why)
        {
            steps.Add(Do(TeamAgent.Codex, why));
            steps.Add(Review(TeamAgent.Codex));
        }
        void Visual(TeamAgent planner)
        {
            steps.Add(Plan(planner, "rediseño: se planifica la experiencia, no el código"));
            steps.Add(Design(planner));
            steps.Add(Do(TeamAgent.Sonnet, "XAML/CSS visual: lo escribe Claude"));
            steps.Add(Do(TeamAgent.Codex, "lógica y pruebas de la interfaz (Codex es más barato)"));
            steps.Add(Review(TeamAgent.Sonnet)); // Codex revisa el código de Claude
            steps.Add(Review(TeamAgent.Codex));  // Claude revisa lo que escribió Codex
        }

        switch (preset)
        {
            case TeamPreset.ClaudeOnly:
                steps.Add(Do(TeamAgent.Sonnet, "preset Solo Claude"));
                break;
            case TeamPreset.CodexOnly:
                steps.Add(Do(TeamAgent.Codex, "preset Solo Codex"));
                break;
            case TeamPreset.Speed:
                steps.Add(Do(p.Visual ? TeamAgent.Sonnet : TeamAgent.Codex, "preset Velocidad: un agente, sin revisión (más riesgo)"));
                break;
            case TeamPreset.Saving:
                if (!p.Clear) steps.Add(Plan(TeamAgent.Sonnet, "plan corto: Codex implementa casi todo"));
                Logic("preset Ahorro: máximo Codex");
                break;
            case TeamPreset.Quality:
                steps.Add(Plan(TeamAgent.Opus, "preset Calidad: Claude planifica siempre"));
                if (p.Visual) steps.Add(Design(TeamAgent.Opus));
                if (p.Visual) { steps.Add(Do(TeamAgent.Sonnet, "XAML/CSS visual: lo escribe Claude")); steps.Add(Review(TeamAgent.Sonnet)); }
                else Logic("preset Calidad: Codex implementa, otro modelo revisa");
                break;
            default: // Auto y Design
                if (p.Kind == TaskKind.Redesign || (preset == TeamPreset.Design && p.Visual && p.Kind != TaskKind.Polish)) Visual(TeamAgent.Opus);
                else if (small && !p.Risky && p.Kind is TaskKind.Polish or TaskKind.Bug or TaskKind.Unknown or TaskKind.Docs)
                    steps.Add(Do(p.Kind == TaskKind.Docs ? TeamAgent.Haiku : TeamAgent.Sonnet, "cambio pequeño: delegar costaría más de lo que ahorra"));
                else if (p.Kind == TaskKind.Polish)
                    steps.Add(Do(TeamAgent.Sonnet, "retoque visual: no pide diseño nuevo ni tanta potencia"));
                else if (p.Visual && p.Kind == TaskKind.Feature)
                {
                    steps.Add(Plan(TeamAgent.Sonnet, "función visual: plan y diseño ligeros"));
                    steps.Add(Design(TeamAgent.Sonnet));
                    steps.Add(Do(TeamAgent.Sonnet, "XAML/CSS visual: lo escribe Claude"));
                    steps.Add(Review(TeamAgent.Sonnet));
                }
                else if (p.Kind == TaskKind.Bug)
                {
                    steps.Add(Plan(TeamAgent.Sonnet, "diagnóstico breve antes de corregir"));
                    Logic("corrección con prueba: Codex implementa");
                }
                else
                {
                    steps.Add(Plan(bigPlanner, bigPlanner == TeamAgent.Opus ? "grande o de riesgo: planifica Opus" : "plan corto para que Codex implemente"));
                    Logic("lógica bien especificada: Codex implementa");
                }
                break;
        }

        // Riesgo: toda tarea con riesgo termina con una revisión, aunque la haya escrito un solo agente.
        if (p.Risky && preset is TeamPreset.Auto or TeamPreset.Design && steps.All(s => s.Role != TeamRole.Review))
            steps.Add(Review(steps.Last(s => s.Role == TeamRole.Implement).Agent));
        return new TeamPlan(p, preset, steps);
    }
}
