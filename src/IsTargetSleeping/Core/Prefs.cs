using System.Diagnostics;
using Microsoft.Win32;
using static IsTargetSleeping.L10n;

namespace IsTargetSleeping;

/// Atajos globales: Ctrl+Alt+O enciende o apaga Ollama, Ctrl+Alt+L libera RAM y Ctrl+Alt+S duerme el
/// modelo. `RegisterHotKey` no pide permisos ni lee el resto del teclado: Windows
/// solo avisa cuando se pulsa esa combinación exacta. Si otra app ya la tiene,
/// Windows lo dice y el panel lo cuenta. Todos van a la ventana oculta de la bandeja.
public sealed class HotKey
{
    public static readonly HotKey Power = new(0x544F, 0x4F, "Ctrl+Alt+O");   // 'TO'
    public static readonly HotKey Sleep = new(0x5453, 0x53, "Ctrl+Alt+S");   // 'TS'
    public static readonly HotKey Clean = new(0x544C, 0x4C, "Ctrl+Alt+L");   // 'TL' (L de «liberar»)

    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
    private static IntPtr hwnd;

    private readonly int id;
    private readonly uint key;
    private bool registered;
    public string Display { get; }

    private HotKey(int id, uint key, string display)
    {
        this.id = id;
        this.key = key;
        Display = display;
    }

    public static void Attach(IntPtr window) => hwnd = window;

    public bool Register()
    {
        if (registered) return true;
        registered = Win32.RegisterHotKey(hwnd, id, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, key);
        return registered;
    }

    public void Unregister()
    {
        if (registered) Win32.UnregisterHotKey(hwnd, id);
        registered = false;
    }

    public bool Matches(IntPtr wParam) => wParam.ToInt32() == id;
}

/// Claves de los ajustes con interruptor (todos activados por defecto).
public static class PrefKeys
{
    public const string SleepHotKey = "sleepHotKeyEnabled";
    public const string Watchdog = "watchdogEnabled";
    public const string GameMode = "gameModeEnabled";
    public const string GameRestore = "gameModeRestore";
    public const string CustomGames = "customGames";
    public const string IgnoredGames = "ignoredGames";
    public const string UpdateCheck = "updateCheck";
    // Liberar RAM
    public const string CleanHotKey = "cleanHotKeyEnabled";
    public const string CleanAreas = "cleanAreas";            // int, bits de Mem Reduct
    public const string CleanThreshold = "cleanThreshold";    // int, % (0 = no)
    public const string CleanInterval = "cleanInterval";      // int, min (0 = no)
    public const string CleanOnCritical = "cleanOnCritical";
    public const string CleanOnGame = "cleanOnGame";
    public const string TrayPercent = "trayPercent";          // ícono con el % de RAM (apagado por defecto)
}

/// Abrir al iniciar sesión: el valor de HKCU\…\Run, respetando si lo desactivaste
/// desde el Administrador de tareas (StartupApproved).
public static class LoginItem
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private static string Command => $"\"{Environment.ProcessPath}\" --login";

    public static bool Enabled
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            if (run?.GetValue(AppInfo.Name) is not string) return false;
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            // Primer byte 2 = activado, 3 = desactivado en el Administrador de tareas.
            return approved?.GetValue(AppInfo.Name) is not byte[] flags || flags.Length == 0 || (flags[0] & 1) == 0;
        }
    }

    public static void Register()
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKey);
        run.SetValue(AppInfo.Name, Command);
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        approved?.DeleteValue(AppInfo.Name, throwOnMissingValue: false);
    }

    public static void Unregister()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        run?.DeleteValue(AppInfo.Name, throwOnMissingValue: false);
    }
}

/// Preferencias de la app que la interfaz necesita observar.
public sealed class Prefs
{
    private const string AskedKey = "loginItemAsked";

    public bool ShouldOfferLogin { get; private set; }
    private string? hotKeyError;
    public string? HotKeyError { get => hotKeyError; set { hotKeyError = value; Changed?.Invoke(); } }

    private bool hotKeyEnabled = Defaults.GetBool("hotKeyEnabled", true);
    public bool HotKeyEnabled
    {
        get => hotKeyEnabled;
        set
        {
            hotKeyEnabled = value;
            Defaults.Set("hotKeyEnabled", value);
            OnHotKeyChange?.Invoke(value);
            Changed?.Invoke();
        }
    }

    private bool sleepHotKeyEnabled = Defaults.GetBool(PrefKeys.SleepHotKey, true);
    public bool SleepHotKeyEnabled
    {
        get => sleepHotKeyEnabled;
        set
        {
            sleepHotKeyEnabled = value;
            Defaults.Set(PrefKeys.SleepHotKey, value);
            OnHotKeyChange?.Invoke(hotKeyEnabled);
            Changed?.Invoke();
        }
    }

    /// Interruptores sencillos (vigilante, modo juego, avisos…), activados por defecto.
    public bool Switch(string key, bool fallback = true) => demoLogin is null ? Defaults.GetBool(key, fallback) : fallback;

    public void SetSwitch(string key, bool value)
    {
        if (demoLogin is null) Defaults.Set(key, value);
        Changed?.Invoke();
    }

    public void Toggle(string key, bool fallback = true) => SetSwitch(key, !Switch(key, fallback));

    public List<string> List(string key) => demoLogin is null ? Defaults.GetList(key) : [];

    public void SetList(string key, IEnumerable<string> values)
    {
        if (demoLogin is null) Defaults.SetList(key, values);
        Changed?.Invoke();
    }

    /// Se llama con el estado del atajo principal; el de dormir se lee de SleepHotKeyEnabled.
    public Action<bool>? OnHotKeyChange;
    public Action? OpenLog;
    public event Action? Changed;
    public Language Language { get; private set; } = LanguageExt.Current;

    public Prefs()
    {
        ShouldOfferLogin = !Defaults.GetBool(AskedKey) && !LoginItem.Enabled;
    }

    private bool? demoLogin;
    public bool OpensAtLogin => demoLogin ?? LoginItem.Enabled;

    /// Para las capturas: sin la pregunta de inicio de sesión y con el atajo activo.
    public void LoadDemo()
    {
        ShouldOfferLogin = false;
        demoLogin = true;
        hotKeyError = null;
    }

    public void AnswerLogin(bool yes)
    {
        Defaults.Set(AskedKey, true);
        ShouldOfferLogin = false;
        if (yes) { try { LoginItem.Register(); } catch { } }
        Changed?.Invoke();
    }

    public void ToggleLogin()
    {
        Defaults.Set(AskedKey, true);
        ShouldOfferLogin = false;
        try
        {
            if (OpensAtLogin) LoginItem.Unregister(); else LoginItem.Register();
        }
        finally { Changed?.Invoke(); }
    }

    /// Cambia el idioma y relanza la app: los textos se resuelven al arrancar.
    public void SetLanguage(Language lang)
    {
        if (lang == Language) return;
        lang.Apply();
        Language = lang;
        Changed?.Invoke();
        if (demoLogin is not null) return;
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--wait-pid {Environment.ProcessId}")
            { UseShellExecute = false });
        }
        catch { }
        System.Windows.Application.Current?.Shutdown();
    }
}
