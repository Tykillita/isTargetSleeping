namespace IsTargetSleeping;

/// Por qué el notch no se enseña ahora (`None`: se enseña).
public enum HideReason { None, Fullscreen, PresentationMode, StartOrSearch, HiddenApp, LockedSession, SecureDesktop }

/// Todo lo que cuenta para decidir si el notch de un monitor se enseña, recogido en un instante.
/// `ForegroundFullscreen`: la ventana de primer plano cubre su monitor y ese monitor es el de este notch.
/// `ForegroundOnThisMonitor`: la ventana de primer plano está en el monitor de este notch.
/// `NotificationState`: `SHQueryUserNotificationState` (3: Direct3D a pantalla completa, 4: modo presentación).
public readonly record struct VisibilitySnapshot(
    bool HideInFullscreen,
    bool ForegroundFullscreen,
    bool ForegroundOnThisMonitor,
    int NotificationState,
    string? Process,
    bool ForegroundIsSelf,
    IReadOnlyCollection<string> HiddenApps,
    bool Pinned,
    bool SessionLocked,
    bool SecureDesktop);

/// Cuándo el notch se enseña y cuándo se aparta. Lógica pura (sin ventanas) para poder probarla.
public static class NotchVisibility
{
    private static readonly HashSet<string> StartAndSearch = new(StringComparer.OrdinalIgnoreCase) { "StartMenuExperienceHost", "SearchHost", "SearchApp" };

    /// El nombre tal como lo escribe el usuario («chrome», «Chrome.exe», « chrome ») o lo da Windows, para compararlos.
    public static string Normalize(string? name)
    {
        var text = (name ?? "").Trim();
        return text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? text[..^4].Trim() : text;
    }

    /// Las reglas, de más a menos fuerte: la sesión (bloqueada, escritorio seguro) oculta siempre; el resto cede ante un notch fijado, que el usuario abrió a propósito.
    public static HideReason Decide(VisibilitySnapshot s)
    {
        if (s.SessionLocked) return HideReason.LockedSession;
        if (s.SecureDesktop) return HideReason.SecureDesktop;
        if (s.Pinned || s.ForegroundIsSelf) return HideReason.None;
        var process = Normalize(s.Process);
        if (process.Length > 0)
        {
            if (StartAndSearch.Contains(process)) return HideReason.StartOrSearch;
            if (s.HiddenApps.Any(app => string.Equals(Normalize(app), process, StringComparison.OrdinalIgnoreCase))) return HideReason.HiddenApp;
        }
        if (!s.HideInFullscreen || !s.ForegroundOnThisMonitor) return HideReason.None;
        if (s.NotificationState is 3 or 4) return s.NotificationState == 4 ? HideReason.PresentationMode : HideReason.Fullscreen;
        return s.ForegroundFullscreen ? HideReason.Fullscreen : HideReason.None;
    }
}

/// Evita el parpadeo: apartar el notch es inmediato, pero volver a enseñarlo exige que la decisión de enseñarlo se mantenga un instante
/// (Alt+Tab, la Vista de tareas y las ventanas intermedias al cambiar de app cruzan por estados que no deben notarse).
public sealed class VisibilityGate
{
    public const double ShowDelay = 0.25;
    private double? wantsShowSince;

    public bool Shown { get; private set; } = true;

    /// Devuelve si cambió lo que se enseña. `now`: segundos desde cualquier origen.
    public bool Update(HideReason decision, double now)
    {
        bool before = Shown;
        if (decision != HideReason.None)
        {
            Shown = false;
            wantsShowSince = null;
        }
        else if (!Shown)
        {
            wantsShowSince ??= now;
            if (now - wantsShowSince.Value >= ShowDelay) { Shown = true; wantsShowSince = null; }
        }
        return Shown != before;
    }

    /// Cuándo hay que volver a preguntar para dejar pasar el retardo (null: nada pendiente).
    public double? PendingUntil => wantsShowSince is { } since ? since + ShowDelay : null;
}
