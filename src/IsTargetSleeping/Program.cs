using System.Diagnostics;
using IsTargetSleeping.UI;

namespace IsTargetSleeping;

public static class Program
{
    private const string MutexName = @"Local\isTargetSleeping.SingleInstance";

    [STAThread]
    public static int Main(string[] args)
    {
        // Relanzada tras cambiar de idioma o actualizarse: espera a que la anterior termine.
        if (Array.IndexOf(args, "--wait-pid") is var w and >= 0 && w + 1 < args.Length && int.TryParse(args[w + 1], out var old))
        {
            try { using var p = Process.GetProcessById(old); p.WaitForExit(10_000); } catch { }
        }

        Language? lang = null;
        if (Array.IndexOf(args, "--lang") is var l and >= 0 && l + 1 < args.Length)
            lang = args[l + 1].StartsWith("en", StringComparison.OrdinalIgnoreCase) ? Language.En : Language.Es;
        L10n.Init(lang);

        // Enlaces (istargetsleeping://…) y --sleep: si la app ya corre, se lo pasa a ella.
        string? pending = null;
        if (Array.IndexOf(args, "--url") is var u and >= 0 && u + 1 < args.Length) pending = $"url:{args[u + 1]}";
        else if (args.Contains("--sleep")) pending = "sleep";
        else if (args.Contains("--clean")) pending = "clean";
        if (pending is not null && TrayIcon.SendToRunning(pending)) return 0;
        // Sin la app abierta, --sleep y --clean se hacen directamente (API de Ollama, agente de memoria).
        if (pending is "sleep" or "clean") return Cli.Run(args);

        // Modos de terminal (--status, --on, --off, --memory, --idle-test, --snapshot…).
        if (Cli.Wants(args)) return Cli.Run(args);

        // Una sola instancia: abrirla otra vez muestra el panel de la que ya corre.
        using var mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            // Puede que la primera aún esté arrancando y su ventana no exista: se reintenta un momento.
            for (int i = 0; i < 20 && !TrayIcon.SendToRunning(pending ?? "show"); i++) Thread.Sleep(250);
            return 0;
        }

        Updater.CleanUpOld();
        var app = new App();
        app.InitializeComponent();
        // En Startup ya corre el Dispatcher: los await vuelven al hilo de la interfaz.
        app.Startup += (_, _) => app.StartTray(launchedAtLogin: args.Contains("--login"), pending);
        return app.Run();
    }
}
