using IsTargetSleeping;

internal static class AgentMetaTests
{
    public static void Run(Action<string, bool> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "its-git-" + Guid.NewGuid().ToString("N"));
        try
        {
            var repo = Path.Combine(root, "repo");
            Directory.CreateDirectory(Path.Combine(repo, ".git"));
            File.WriteAllText(Path.Combine(repo, ".git", "HEAD"), "ref: refs/heads/feature/invoice-vat\n");
            check("se lee la rama de una carpeta con git", AgentGit.Branch(repo) == "feature/invoice-vat");

            var detached = Path.Combine(root, "detached");
            Directory.CreateDirectory(Path.Combine(detached, ".git"));
            File.WriteAllText(Path.Combine(detached, ".git", "HEAD"), "0123456789abcdef0123456789abcdef01234567\n");
            check("con HEAD suelto sale el hash corto", AgentGit.Branch(detached) == "0123456");

            var real = Path.Combine(root, "worktrees", "wt1");
            Directory.CreateDirectory(real);
            File.WriteAllText(Path.Combine(real, "HEAD"), "ref: refs/heads/team/task-7\n");
            var wt = Path.Combine(root, "wt");
            Directory.CreateDirectory(wt);
            File.WriteAllText(Path.Combine(wt, ".git"), $"gitdir: {real}\n");
            check("un worktree (`.git` es un archivo) apunta a su carpeta real", AgentGit.Branch(wt) == "team/task-7");

            check("sin git, sin carpeta o con una ruta rara no hay rama ni error", AgentGit.Branch(root) is null && AgentGit.Branch(Path.Combine(root, "nada")) is null
                && AgentGit.Branch(null) is null && AgentGit.Branch("relativa") is null && AgentGit.Branch("C:\\<>|") is null);
        }
        finally { try { Directory.Delete(root, true); } catch { } }

        // — Tokens de contexto desde el log
        string Assistant(long input, long read, long create) =>
            "{\"type\":\"assistant\",\"message\":{\"usage\":{\"input_tokens\":" + input + ",\"cache_read_input_tokens\":" + read + ",\"cache_creation_input_tokens\":" + create
            + ",\"output_tokens\":5},\"content\":[{\"type\":\"text\",\"text\":\"hola\"}]}}";
        var many = AgentLogs.Parse("c1", "app", "Claude Code", [Assistant(10, 1000, 200), Assistant(12, 88_000, 1_500)], DateTime.UtcNow);
        check("el contexto es el de la última respuesta (entrada + caché)", many.ContextTokens == 12 + 88_000 + 1_500);
        check("sin datos de uso no hay contexto", AgentLogs.Parse("c2", "app", "Claude Code", ["{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"hola\"}]}}"], DateTime.UtcNow).ContextTokens == 0);
        var meta = new AgentSnapshot("s", "C:/x", "Claude Code", AgentState.Working, null, null, null, null, null, [], DateTime.UtcNow) { ContextTokens = 126_400 };
        check("el contexto se escribe en miles de tokens", meta.ContextLabel == "126k");
        check("menos de mil tokens se escriben tal cual", (meta with { }) is { } && new AgentSnapshot("s", "C:/x", "Claude Code", AgentState.Working, null, null, null, null, null, [], DateTime.UtcNow) { ContextTokens = 640 }.ContextLabel == "640");
    }
}
