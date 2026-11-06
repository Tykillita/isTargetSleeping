namespace IsTargetSleeping;

public enum AgentGlowState { Off, Working, Attention, Alert, Done }

/// La luz del notch según lo que hacen tus agentes: azul que respira mientras trabajan, ámbar si algo espera tu respuesta, rojo si un agente se atasca,
/// se repite o se agota un límite, y un destello verde un momento al terminar. Lógica pura para poder probarla; el dibujo está en el notch.
public static class AgentGlowRules
{
    public static readonly TimeSpan DoneFor = TimeSpan.FromSeconds(8);

    /// De más a menos urgente: lo que espera tu respuesta, un aviso, una sesión que pide atención, trabajo en curso y, un momento, «terminó».
    public static AgentGlowState Of(bool pendingRequest, bool alert, bool needsAttention, bool working, DateTime? finishedAt, DateTime now)
    {
        if (pendingRequest || needsAttention && !alert) return AgentGlowState.Attention;
        if (alert) return AgentGlowState.Alert;
        if (working) return AgentGlowState.Working;
        return finishedAt is { } at && now - at >= TimeSpan.Zero && now - at < DoneFor ? AgentGlowState.Done : AgentGlowState.Off;
    }

    /// (rojo, verde, azul) de cada estado.
    public static (byte R, byte G, byte B) Color(AgentGlowState state) => state switch
    {
        AgentGlowState.Working => (0x5A, 0xA9, 0xFF),
        AgentGlowState.Attention => (0xFF, 0xB3, 0x47),
        AgentGlowState.Alert => (0xFF, 0x6B, 0x6B),
        AgentGlowState.Done => (0x4C, 0xD9, 0x64),
        _ => (0, 0, 0),
    };

    /// Lo que tarda un latido completo (0: sin latido, solo se desvanece).
    public static double PulseSeconds(AgentGlowState state) => state switch
    {
        AgentGlowState.Working => 2.6,
        AgentGlowState.Attention => 1.0,
        AgentGlowState.Alert => 1.6,
        _ => 0,
    };
}
