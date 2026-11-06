using System.Text.Json.Nodes;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping.UI;

/// Nimbo como orquestador: pone en la cola de Team un trabajo para uno de tus agentes propios. El complemento ya pidió confirmación;
/// aquí se vuelve a validar todo (agente activo, proyecto conocido, cola y presupuesto del día). Nunca fusiona ni descarta ramas.
public sealed partial class PanelViewModel
{
    private static int PetPolicyInt(string key, int fallback, int min, int max) =>
        PetPolicyLoad()[key] is JsonValue v && v.TryGetValue<int>(out var n) ? Math.Clamp(n, min, max) : fallback;

    /// Carpetas que Nimbo puede usar: la de Team, la de código y los proyectos de las sesiones de agentes recientes. Nada fuera de esta lista.
    private IEnumerable<string> PetTeamRoots() =>
        new[] { Prefs.StoredList(PrefKeys.TeamRoot)?.FirstOrDefault(), Prefs.StoredList(PrefKeys.CodeRoot)?.FirstOrDefault() }
            .Concat(AgentSessions.Select(s => s.Project))
            .Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p)).Select(p => p!.TrimEnd('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase);

    /// Los proyectos que Nimbo puede nombrar (solo el nombre de la carpeta) y el que Equipo tiene elegido; lo ve la caja de chat del celular.
    public (IReadOnlyList<string> Roots, string? Default) PetTeamRootNames()
    {
        var names = PetTeamRoots().Select(r => Path.GetFileName(r)).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
        var def = Prefs.StoredList(PrefKeys.TeamRoot)?.FirstOrDefault() is { Length: > 0 } t && Directory.Exists(t) ? Path.GetFileName(t.TrimEnd('\\', '/')) : null;
        return (names, def);
    }

    /// Tokens que Team ha gastado hoy (historial + trabajo en curso).
    private long PetTeamTokensToday(TeamService svc)
    {
        var today = DateTime.Today;
        return svc.Store.LoadHistory().Where(e => e.Started >= today).Sum(e => e.Tokens) + (svc.Current?.Tokens ?? 0);
    }

    /// Cola corta, presupuesto del día y proyecto conocido: lo que se comprueba antes de poner cualquier trabajo de Nimbo en Team.
    private (string? Error, string Root, TeamService Svc, long Remaining) PetTeamGate(string task, string? rootName)
    {
        var svc = Hub.Team.Service;
        if (task.Length is 0 or > 300) return ("la tarea debe tener de 1 a 300 caracteres", "", svc, 0);
        int maxQueued = PetPolicyInt("team_max_queued", 3, 0, 10);
        if (svc.Queued.Count >= maxQueued) return ($"la cola de Equipo ya tiene {svc.Queued.Count} trabajos (máximo {maxQueued})", "", svc, 0);
        long daily = PetPolicyInt("team_daily_tokens", 300_000, 0, 5_000_000), used = PetTeamTokensToday(svc);
        if (used >= daily) return ($"se alcanzó el presupuesto de hoy ({used / 1000}k de {daily / 1000}k tokens)", "", svc, 0);

        var roots = PetTeamRoots().ToList();
        string root;
        if (string.IsNullOrWhiteSpace(rootName)) root = Prefs.StoredList(PrefKeys.TeamRoot)?.FirstOrDefault() is { Length: > 0 } t && Directory.Exists(t) ? t : "";
        else
        {
            var hits = roots.Where(r => string.Equals(Path.GetFileName(r), rootName.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            if (hits.Count != 1) return (hits.Count == 0 ? $"no conozco el proyecto «{rootName}»" : $"hay varios proyectos llamados «{rootName}»", "", svc, 0);
            root = hits[0];
        }
        return (null, root, svc, daily - used);
    }

    public (bool Ok, string Message) PetTeamEnqueue(string task, string? profileId, string? rootName)
    {
        task = task.Trim();
        var profile = AgentProfiles.Load().FirstOrDefault(p => p.Id == profileId && p.Enabled);
        if (profile is null) return (false, "ese agente no existe o está apagado");
        var (error, root, svc, _) = PetTeamGate(task, rootName);
        if (error is not null) return (false, error);
        if (root.Length == 0) return (false, "dime en qué proyecto: no hay una carpeta de Equipo elegida");
        if (profile.CanWrite && !TeamWorktree.IsRepo(root)) return (false, $"{Path.GetFileName(root)} no es un repositorio de git");

        var job = AgentProfiles.Job(profile, task, root);
        if (profile.CanWrite) job = job with { ReviewPlan = true };   // un agente que escribe enseña su plan antes
        svc.Enqueue(job);
        return (true, $"«{profile.Name}» en cola" + (profile.CanWrite ? " (te enseñará el plan antes de escribir)" : ""));
    }

    /// El reparto automático del widget Equipo (Claude y Codex): el enrutador elige agentes y pasos según la tarea. Siempre con el plan por aprobar,
    /// tope de tokens y rama aparte; una pregunta sencilla se responde sin proyecto.
    public (bool Ok, string Message) PetTeamRun(string task, string? rootName, string? presetName = null, string? effortName = null, bool reviewPlan = true, IReadOnlyList<string>? attachments = null)
    {
        task = task.Trim();
        var (error, root, svc, remaining) = PetTeamGate(task, rootName);
        if (error is not null) return (false, error);
        var profile = TeamRouter.Profile(task);
        var models = CodexModels.Load();
        var preset = Enum.TryParse<TeamPreset>(presetName, out var pr) && Enum.IsDefined(pr) ? pr : TeamPreset.Auto;
        var effort = TeamEffort.Parse(effortName);
        var steps = TeamRouter.Route(profile, preset).Steps.Select(s => TeamEffort.Apply(CodexModels.Apply(s, models), effort, models)).ToList();
        bool question = profile.Kind == TaskKind.Question;
        if (!question)
        {
            if (root.Length == 0) return (false, "dime en qué proyecto: no hay una carpeta de Equipo elegida");
            if (!TeamWorktree.IsRepo(root)) return (false, $"{Path.GetFileName(root)} no es un repositorio de git");
        }
        long cap = Math.Min(150_000, Math.Max(10_000, remaining));
        string? attachDir = null;
        if (attachments is { Count: > 0 })
        {
            var (aerr, adir, afiles) = PetTeamStageAttachments(attachments);
            if (aerr is not null) return (false, aerr);
            attachDir = adir;
            // como en el widget: los pasos ven los adjuntos (la revisión solo si la tarea es visual)
            steps = steps.Select(s => s with { Images = s.Role != TeamRole.Review || profile.Visual ? afiles : null }).ToList();
        }
        svc.Enqueue(new TeamJob(task, root, preset, steps, profile) { ReviewPlan = !question && reviewPlan, MaxTokens = cap, AttachmentDir = attachDir });
        string who = string.Join(" → ", steps.Select(s => $"{s.Agent} ({TeamActivity.RoleName(s.Role)})"));
        return (true, question ? "pregunta en cola" : $"en cola: {who}" + (reviewPlan ? "; te enseñará el plan antes de escribir" : "; sin revisar el plan"));
    }

    public static readonly HashSet<string> PetAttachExt = new(TeamEffort.ImageExtensions.Concat(new[]
    {
        ".pdf", ".txt", ".md", ".json", ".csv", ".log", ".xml", ".yaml", ".yml", ".toml", ".ini", ".cs", ".xaml", ".py", ".js", ".ts", ".tsx", ".jsx", ".html", ".css", ".sql", ".sh", ".ps1", ".cpp", ".c", ".h", ".java", ".go", ".rs", ".kt", ".swift",
    }), StringComparer.OrdinalIgnoreCase);

    /// Copia los archivos subidos desde el celular (pet\uploads\<id>) a una carpeta de adjuntos de Team y borra lo subido. Solo ids de 32 hex, una lista cerrada de extensiones y topes de peso.
    private static (string? Error, string? Dir, List<string> Files) PetTeamStageAttachments(IReadOnlyList<string> ids)
    {
        const long OneMax = 10L * 1024 * 1024, TotalMax = 25L * 1024 * 1024;
        if (ids.Count > 5) return ("máximo 5 adjuntos", null, []);
        var uploads = Path.Combine(Paths.DataDir, "pet", "uploads");
        var dir = Path.Combine(Paths.DataDir, "team", "attachments", Guid.NewGuid().ToString("N")[..8]);
        var files = new List<string>();
        try
        {
            Directory.CreateDirectory(dir);
            long total = 0;
            foreach (var id in ids)
            {
                if (id.Length != 32 || !id.All(Uri.IsHexDigit)) throw new InvalidDataException("adjunto inválido");
                var src = Path.Combine(uploads, id);
                var file = Directory.Exists(src) ? Directory.GetFiles(src).FirstOrDefault() : null;
                if (file is null) throw new InvalidDataException("un adjunto ya no está (se borran a las 2 horas)");
                if (!PetAttachExt.Contains(Path.GetExtension(file))) throw new InvalidDataException("tipo de archivo no permitido");
                var len = new FileInfo(file).Length;
                if (len > OneMax || (total += len) > TotalMax) throw new InvalidDataException("los adjuntos pesan demasiado (10 MB cada uno, 25 MB en total)");
                var dest = Path.Combine(dir, $"{files.Count + 1}-{Path.GetFileName(file)}");
                File.Copy(file, dest);
                files.Add(dest);
            }
            foreach (var id in ids) { try { Directory.Delete(Path.Combine(uploads, id), true); } catch (IOException) { } }
            return (null, dir, files);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
            return (e.Message, null, []);
        }
    }
}
