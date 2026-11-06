namespace IsTargetSleeping;

/// Windows no expone un estado público global de uso; se combina el registro de privacidad con el servicio de fotogramas.
public static class CameraUsage
{
    public static bool IsActive(long lastUsedTimeStart, long lastUsedTimeStop) => lastUsedTimeStart > 0 && lastUsedTimeStart > lastUsedTimeStop;

    public static int[] ActiveProcessIds(IEnumerable<(int ProcessId, bool Streaming)> processes) => processes
        .Where(process => process.Streaming && process.ProcessId > 0)
        .Select(process => process.ProcessId)
        .Distinct()
        .Order()
        .ToArray();
}
