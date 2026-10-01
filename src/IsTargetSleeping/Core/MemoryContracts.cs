using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IsTargetSleeping;

public enum CleanOutcome { Success, Partial, Failed, NoWork, Pending }
public enum AgentTrackingState { Waiting, Pending, Completed, MissingReport }
/// Pure instance-tracking policy; callers supply elapsed monotonic time, never wall time.
public static class AgentRunTracking
{
    public static AgentTrackingState Evaluate(Guid requestId, TimeSpan elapsed, bool instanceEnded, CleanResult? report)
    {
        if (report is { Version: 2 } && report.RequestId == requestId && report.Outcome != CleanOutcome.Pending)
            return AgentTrackingState.Completed;
        if (instanceEnded && elapsed > TimeSpan.FromSeconds(2)) return AgentTrackingState.MissingReport;
        return elapsed >= TimeSpan.FromSeconds(130) ? AgentTrackingState.Pending : AgentTrackingState.Waiting;
    }
}
public sealed record CleanError(string Operation, int? Pid = null, string? Target = null,
    int? Win32Error = null, int? NtStatus = null, string? Message = null);
public sealed record CleanMemorySample(DateTimeOffset CapturedAt, long Used, long Available,
    long Committed, long CommitLimit, int PhysicalLoad, bool LowMemory);
public sealed record AgentTerminationReport(int Version, Guid RequestId, ProcessActionResult Result);
public sealed record CleanResult(long Freed, CleanAreas Failed, string? Error)
{
    public int Version { get; init; } = 2;
    public Guid RequestId { get; init; }
    public CleanOutcome Outcome { get; init; } = Error is not null ? CleanOutcome.Failed
        : Failed != CleanAreas.None ? CleanOutcome.Partial : CleanOutcome.Success;
    public CleanAreas Requested { get; init; }
    public long DurationMilliseconds { get; init; }
    public int ProcessesTreated { get; init; }
    public int ProcessesSkipped { get; init; }
    public int ProcessesFailed { get; init; }
    public int OperationsSucceeded { get; init; }
    public IReadOnlyList<CleanError> Errors { get; init; } = [];
    public CleanMemorySample? Before { get; init; }
    public CleanMemorySample? Immediate { get; init; }
    public CleanMemorySample? AfterFiveSeconds { get; init; }
    public CleanMemorySample? AfterThirtySeconds { get; init; }
    [JsonIgnore] public Task<CleanResult>? Completion { get; init; }
    public bool Completed => Outcome is CleanOutcome.Success or CleanOutcome.Partial or CleanOutcome.NoWork;
    public void Deconstruct(out CleanAreas failed, out string? error) { failed = Failed; error = Error; }
}

/// Reports are written only by the elevated agent in its administrator-owned directory.
/// Guid-only filenames and bounded reads keep responses independent of task scheduler globals.
public static class AgentReports
{
    public const int MaxReportBytes = 1_048_576;
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "isTargetSleeping", "cleaner", "reports");
    public static string CleanPath(Guid id) => Path.Combine(DirectoryPath, $"clean-{id:N}.json");
    public static string TerminatePath(Guid id) => Path.Combine(DirectoryPath, $"terminate-{id:N}.json");

    public static void Write(CleanResult report) => Atomic(CleanPath(report.RequestId),
        JsonSerializer.SerializeToUtf8Bytes(report, AgentJsonContext.Default.CleanResult));
    public static void Write(ProcessActionResult report) => Atomic(TerminatePath(report.Id),
        JsonSerializer.SerializeToUtf8Bytes(new AgentTerminationReport(2, report.Id, report), AgentJsonContext.Default.AgentTerminationReport));
    public static CleanResult? ReadClean(Guid id)
    {
        try
        {
            var bytes = ReadBounded(CleanPath(id));
            return bytes is null ? null : DecodeClean(bytes, id);
        }
        catch { return null; }
    }
    public static ProcessActionResult? ReadTerminate(Guid id)
    {
        try
        {
            var bytes = ReadBounded(TerminatePath(id));
            return bytes is null ? null : DecodeTerminate(bytes, id);
        }
        catch { return null; }
    }
    public static CleanResult? DecodeClean(ReadOnlySpan<byte> bytes, Guid id)
    {
        if (bytes.Length is <= 0 or > MaxReportBytes || id == Guid.Empty) return null;
        try
        {
            var result = JsonSerializer.Deserialize(bytes, AgentJsonContext.Default.CleanResult);
            return result is { Version: 2 } && result.RequestId == id && Enum.IsDefined(result.Outcome) && result.Outcome != CleanOutcome.Pending
                && result.ProcessesTreated >= 0 && result.ProcessesFailed >= 0 && result.ProcessesSkipped >= 0
                && result.Errors is { Count: <= 4096 } ? result : null;
        }
        catch (JsonException) { return null; }
    }
    public static ProcessActionResult? DecodeTerminate(ReadOnlySpan<byte> bytes, Guid id)
    {
        if (bytes.Length is <= 0 or > MaxReportBytes || id == Guid.Empty) return null;
        try
        {
            var result = JsonSerializer.Deserialize(bytes, AgentJsonContext.Default.AgentTerminationReport);
            return result is { Version: 2, Result: not null } && result.RequestId == id && result.Result.Id == id
                && result.Result.Items is { Count: <= CleanSpec.MaxKeep } && Enum.IsDefined(result.Result.Outcome)
                ? result.Result : null;
        }
        catch (JsonException) { return null; }
    }
    private static byte[]? ReadBounded(string path)
    {
        using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (file.Length is <= 0 or > MaxReportBytes) return null;
        byte[] bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        return bytes;
    }
    private static void Atomic(string path, byte[] bytes)
    {
        if (bytes.Length > MaxReportBytes) throw new InvalidDataException("Oversized agent report.");
        Directory.CreateDirectory(DirectoryPath);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(bytes); file.Flush(flushToDisk: true); }
            File.Move(temp, path, overwrite: true);
            // Bound retained history; never delete another operation's recent response.
            foreach (var old in new DirectoryInfo(DirectoryPath).EnumerateFiles("*.json")
                .OrderByDescending(f => f.LastWriteTimeUtc).Skip(256)
                .Where(f => f.LastWriteTimeUtc < DateTime.UtcNow.AddHours(-1)))
                try { old.Delete(); } catch { }
        }
        finally { try { File.Delete(temp); } catch { } }
    }
}

/// Only ASCII tokens and numeric process identities cross the UAC boundary.
public static class ProcessActionProtocol
{
    public static string Format(ProcessActionRequest request)
    {
        if (request.Id == Guid.Empty || !Enum.IsDefined(request.Mode) || request.Targets.Count is 0 or > CleanSpec.MaxKeep
            || request.Targets.Any(t => !t.IsValid)) throw new ArgumentException("Invalid termination request.");
        var text = $"r={request.Id:N};m={(int)request.Mode};i=" + string.Join(',', request.Targets.Select(t =>
            t.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
            t.CreatedFileTime.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (text.Length > CleanSpec.MaxLength) throw new ArgumentException("Oversized termination request.");
        return text;
    }
    public static ProcessActionRequest? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > CleanSpec.MaxLength || text.Any(c => c > 127 || char.IsControl(c))) return null;
        var parts = text.Split(';');
        if (parts.Length != 3 || !parts[0].StartsWith("r=", StringComparison.Ordinal)
            || !Guid.TryParseExact(parts[0][2..], "N", out var id) || id == Guid.Empty
            || !parts[1].StartsWith("m=", StringComparison.Ordinal) || parts[1].Length != 3
            || !char.IsAsciiDigit(parts[1][2]) || !int.TryParse(parts[1][2..], out int mode)
            || !Enum.IsDefined((ProcessActionMode)mode) || !parts[2].StartsWith("i=", StringComparison.Ordinal)) return null;
        var targets = new List<ProcessIdentity>();
        foreach (var part in parts[2][2..].Split(','))
        {
            var values = part.Split(':');
            if (values.Length != 2 || values.Any(v => v.Length == 0 || !v.All(char.IsAsciiDigit))
                || !int.TryParse(values[0], out int pid) || !long.TryParse(values[1], out long created)
                || pid <= 0 || created <= 0) return null;
            targets.Add(new ProcessIdentity(pid, created));
            if (targets.Count > CleanSpec.MaxKeep) return null;
        }
        return new ProcessActionRequest(id, (ProcessActionMode)mode, targets.Distinct().ToArray());
    }
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(CleanResult))]
[JsonSerializable(typeof(ProcessActionResult))]
[JsonSerializable(typeof(AgentTerminationReport))]
public partial class AgentJsonContext : JsonSerializerContext;
