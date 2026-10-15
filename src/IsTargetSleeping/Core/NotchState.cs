namespace IsTargetSleeping;

/// Colapsada (la pastilla), expandida al pasar el ratón, o fijada (clic: tiene el foco y no se cierra sola).
public enum NotchMode { Collapsed, Expanded, Pinned }

/// Cuándo se abre y se cierra el dock tipo notch. Lógica pura: el tiempo, en segundos desde
/// cualquier origen; la ventana solo le cuenta dónde está el cursor.
///  - Colapsada: el cursor en su zona durante `EnterDelay` la expande.
///  - Expandida: el cursor fuera durante `LeaveDelay` la colapsa (el retardo evita cierres por un roce).
///  - Fijada: solo se cierra con `Unpin` (perder el foco o Esc).
///  - Con una app a pantalla completa en ese monitor se oculta, salvo que esté fijada.
public sealed class NotchState
{
    public const double EnterDelay = 0.15, LeaveDelay = 0.4;

    public NotchMode Mode { get; private set; }
    public bool Hidden { get; private set; }
    private double? enteredAt, leftAt;

    /// Devuelve si cambió algo que haya que pintar.
    public bool Update(bool inside, bool suppressed, bool keepOpen, double now)
    {
        var (mode, hidden) = (Mode, Hidden);
        Hidden = suppressed && Mode != NotchMode.Pinned;
        if (Hidden) Reset(NotchMode.Collapsed);
        else if (Mode == NotchMode.Collapsed)
        {
            if (!inside) enteredAt = null;
            else
            {
                enteredAt ??= now;
                if (now - enteredAt.Value >= EnterDelay) Reset(NotchMode.Expanded);
            }
        }
        else if (Mode == NotchMode.Expanded)
        {
            if (inside || keepOpen) leftAt = null;
            else
            {
                leftAt ??= now;
                if (now - leftAt.Value >= LeaveDelay) Reset(NotchMode.Collapsed);
            }
        }
        return mode != Mode || hidden != Hidden;
    }

    /// Un clic (o abrirlo desde la bandeja, un enlace…): se abre y se queda.
    public void Pin()
    {
        Hidden = false;
        Reset(NotchMode.Pinned);
    }

    /// Perdió el foco o se pulsó Esc: vuelve a la pastilla, esté como esté.
    public void Close() => Reset(NotchMode.Collapsed);

    private void Reset(NotchMode mode)
    {
        Mode = mode;
        enteredAt = leftAt = null;
    }
}
