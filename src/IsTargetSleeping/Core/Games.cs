using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace IsTargetSleeping;

public sealed record GameInfo(string Name, string Exe, int Pid = 0);

/// Una carpeta donde viven juegos. `Name` es el nombre del juego si la carpeta es
/// de uno solo (Epic, GOG); si es una biblioteca (steamapps\common, C:\Riot Games),
/// el juego es la primera subcarpeta.
public sealed record GameRoot(string Path, string? Name = null);

/// Dónde instalan los juegos los lanzadores de este PC.
public static partial class GameLibraries
{
    private static List<GameRoot>? cache;
    private static DateTime cachedAt;

    /// Rutas de `steamapps\libraryfolders.vdf` (formato KeyValues de Valve).
    public static List<string> ParseSteamLibraries(string vdf) =>
        SteamPath().Matches(vdf).Select(m => m.Groups[1].Value.Replace(@"\\", @"\")).ToList();

    [GeneratedRegex("\"path\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex SteamPath();

    /// Carpeta y nombre de un manifiesto `.item` de Epic. Los DLC («addons») comparten
    /// carpeta con su juego y no dan nombre.
    public static GameRoot? ParseEpicManifest(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o) return null;
            var dir = o["InstallLocation"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(dir)) return null;
            var categories = (o["AppCategories"] as JsonArray)?.Select(c => c?.GetValue<string>()).ToList() ?? [];
            bool addon = categories.Contains("addons") && !categories.Contains("games");
            return new GameRoot(dir.TrimEnd('\\', '/'), addon ? null : o["DisplayName"]?.GetValue<string>());
        }
        catch { return null; }
    }

    public static List<GameRoot> Roots()
    {
        if (cache is not null && DateTime.Now - cachedAt < TimeSpan.FromMinutes(5)) return cache;
        var roots = new List<GameRoot>();

        // Steam: la carpeta de Steam y las bibliotecas de libraryfolders.vdf.
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string steam)
            {
                var libraries = new List<string> { steam.Replace('/', '\\') };
                var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf)) libraries.AddRange(ParseSteamLibraries(File.ReadAllText(vdf)));
                foreach (var lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
                    roots.Add(new GameRoot(Path.Combine(lib, "steamapps", "common")));
            }
        }
        catch { }

        // Epic: un manifiesto por juego (y por DLC) con su carpeta.
        try
        {
            var manifests = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";
            if (Directory.Exists(manifests))
            {
                var epic = Directory.GetFiles(manifests, "*.item")
                    .Select(f => { try { return ParseEpicManifest(File.ReadAllText(f)); } catch { return null; } })
                    .OfType<GameRoot>()
                    .GroupBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.FirstOrDefault(r => r.Name is not null) ?? g.First());
                roots.AddRange(epic);
            }
        }
        catch { }

        // GOG: una clave por juego con su carpeta y su nombre.
        try
        {
            using var gog = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
            foreach (var id in gog?.GetSubKeyNames() ?? [])
            {
                using var game = gog!.OpenSubKey(id);
                if (game?.GetValue("path") is string dir && dir.Length > 0)
                    roots.Add(new GameRoot(dir.TrimEnd('\\'), game.GetValue("gameName") as string));
            }
        }
        catch { }

        // Carpetas fijas de Riot, EA, Rockstar y la app Xbox (en cualquier disco).
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var fixedDirs = new List<string>
        {
            @"C:\Riot Games",
            Path.Combine(programFiles, "EA Games"),
            Path.Combine(programFiles, "Rockstar Games"),
        };
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
                fixedDirs.Add(Path.Combine(drive.RootDirectory.FullName, "XboxGames"));
        }
        catch { }
        roots.AddRange(fixedDirs.Where(Directory.Exists).Select(d => new GameRoot(d)));

        cache = roots.Where(r => r.Path.Length > 3).ToList();
        cachedAt = DateTime.Now;
        return cache;
    }

    /// Lanzadores, ayudantes y herramientas que viven junto a los juegos pero no lo son.
    private static readonly string[] NotGames =
    [
        "steam", "steamwebhelper", "steamservice", "gameoverlayui", "epicgameslauncher", "eadesktop", "origin",
        "galaxyclient", "rockstarservice", "socialclubhelper", "wallpaper32", "wallpaper64", "webwallpaper32",
        "ui32", "losslessscaling", "beservice", "vgc", "vgtray", "playgtav",
    ];

    private static readonly string[] NotGameParts =
    [
        "launcher", "crash", "helper", "report", "setup", "install", "redist", "updater", "anticheat", "overlay",
        "webview", "riotclient", "bootstrapper", "service",
    ];

    /// Subcarpetas de steamapps\common que no son juegos.
    private static readonly string[] NotGameFolders = ["wallpaper_engine", "Steamworks Shared", "Steam Controller Configs"];

    public static bool LooksLikeGame(string exePath)
    {
        var name = Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
        return !NotGames.Contains(name) && !NotGameParts.Any(name.Contains);
    }

    /// Si el ejecutable está en una biblioteca, el nombre del juego; si no, null.
    public static string? Match(string exePath, IEnumerable<GameRoot> roots)
    {
        foreach (var root in roots)
        {
            var prefix = root.Path.TrimEnd('\\') + "\\";
            if (!exePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (root.Name is { } name) return name;
            var rest = exePath[prefix.Length..];
            int slash = rest.IndexOf('\\');
            if (slash <= 0) return null;   // un .exe suelto en la raíz de la biblioteca
            var folder = rest[..slash];
            if (NotGameFolders.Contains(folder, StringComparer.OrdinalIgnoreCase)) return null;
            return folder;
        }
        return null;
    }
}

/// Busca un juego abierto: un proceso con ventana visible cuyo .exe está en una
/// biblioteca de juegos (o en la lista de juegos propios), o cualquier app en
/// pantalla completa exclusiva de Direct3D.
public sealed class GameDetector
{
    private readonly Dictionary<int, string?> paths = [];

    public GameInfo? Scan(IReadOnlyCollection<string> custom, IReadOnlyCollection<string> ignored)
    {
        var roots = GameLibraries.Roots();
        var visible = VisibleWindowPids(out int foregroundPid);
        var alive = new HashSet<int>();
        var found = new List<(int Pid, GameInfo Game)>();

        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                int pid = p.Id;
                if (pid <= 4 || pid == Environment.ProcessId) continue;
                alive.Add(pid);
                if (!paths.TryGetValue(pid, out var path)) paths[pid] = path = Procs.ImagePath(pid);
                if (path is null || !visible.Contains(pid)) continue;
                if (ignored.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;

                if (custom.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    found.Add((pid, new GameInfo(Describe(path) ?? Path.GetFileNameWithoutExtension(path), path, pid)));
                    continue;
                }
                if (!GameLibraries.LooksLikeGame(path)) continue;
                if (GameLibraries.Match(path, roots) is { } name) found.Add((pid, new GameInfo(name, path, pid)));
            }
        }
        foreach (var pid in paths.Keys.Where(k => !alive.Contains(k)).ToList()) paths.Remove(pid);

        if (found.Count > 0) return found.FirstOrDefault(f => f.Pid == foregroundPid).Game ?? found[0].Game;

        // Pantalla completa exclusiva: sea lo que sea, es un juego (o algo que no quiere interrupciones).
        if (Win32.SHQueryUserNotificationState(out int state) == 0 && state == Win32.QUNS_RUNNING_D3D_FULL_SCREEN
            && foregroundPid > 4 && Procs.ImagePath(foregroundPid) is { } fullscreen
            && !ignored.Contains(fullscreen, StringComparer.OrdinalIgnoreCase))
            return new GameInfo(Describe(fullscreen) ?? Path.GetFileNameWithoutExtension(fullscreen), fullscreen, foregroundPid);
        return null;
    }

    private static string? Describe(string path)
    {
        try
        {
            var d = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
            return string.IsNullOrEmpty(d) ? null : d;
        }
        catch { return null; }
    }

    private static HashSet<int> VisibleWindowPids(out int foregroundPid)
    {
        var set = new HashSet<int>();
        Win32.EnumWindows((hwnd, _) =>
        {
            if (Win32.IsWindowVisible(hwnd) && Win32.GetWindowRect(hwnd, out var r) && r.Width > 64 && r.Height > 64)
            {
                Win32.GetWindowThreadProcessId(hwnd, out int pid);
                set.Add(pid);
            }
            return true;
        }, IntPtr.Zero);
        var fg = Win32.GetForegroundWindow();
        foregroundPid = 0;
        if (fg != IntPtr.Zero) Win32.GetWindowThreadProcessId(fg, out foregroundPid);
        return set;
    }
}

public enum GameTransition { None, Enter, Exit }

/// Modo juego, lógica pura: entra cuando el juego lleva 5 s abierto y sale 30 s
/// después de cerrarlo (un juego que se reinicia o cambia de ventana no hace
/// parpadear a Ollama). Recuerda qué motores estaban encendidos para volver a
/// encender solo esos, y respeta lo que el usuario toque a mano mientras juega.
public sealed class GameModeTracker
{
    public static readonly TimeSpan EnterAfter = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ExitAfter = TimeSpan.FromSeconds(30);

    public bool Active { get; private set; }
    public string? Game { get; private set; }

    private string? candidate;
    private DateTime candidateSince, lastSeen;
    private HashSet<string> stopped = [];
    private readonly HashSet<string> touched = [];

    public GameTransition Sample(string? game, DateTime now)
    {
        if (game is not null)
        {
            lastSeen = now;
            if (Active) { Game = game; return GameTransition.None; }
            if (candidate != game) { candidate = game; candidateSince = now; }
            if (now - candidateSince < EnterAfter) return GameTransition.None;
            Active = true;
            Game = game;
            candidate = null;
            return GameTransition.Enter;
        }
        candidate = null;
        if (!Active || now - lastSeen < ExitAfter) return GameTransition.None;
        return ForceExit();
    }

    /// Al desactivar el modo juego en Ajustes: sale ya.
    public GameTransition ForceExit()
    {
        if (!Active) return GameTransition.None;
        Active = false;
        Game = null;
        return GameTransition.Exit;
    }

    /// Al entrar: los motores encendidos, que son los que se apagan.
    public void Stopped(IEnumerable<string> engines)
    {
        stopped = [.. engines];
        touched.Clear();
    }

    /// El usuario encendió o apagó un motor a mano durante el juego.
    public void Touch(string engine)
    {
        // A process action may finish after the game has exited, while restore
        // is deferred until its result. Preserve that manual choice as well.
        if (Active || stopped.Count > 0) touched.Add(engine);
    }

    /// Al salir: los que se apagaron por el juego, no se tocaron a mano y siguen apagados.
    public List<string> ToRestore(Func<string, bool> isOn)
    {
        var list = stopped.Where(e => !touched.Contains(e) && !isOn(e)).ToList();
        stopped = [];
        touched.Clear();
        return list;
    }
}
