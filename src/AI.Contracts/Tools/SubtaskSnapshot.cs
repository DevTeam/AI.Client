namespace AI.Contracts.Tools;

/// <summary>A file changed by the subtask, measured from that subtask's own workspace baseline.</summary>
public sealed record SubtaskArtifact(
    string Path,
    string Change,
    int? Additions = null,
    int? Deletions = null,
    string? PreviousPath = null);

/// <summary>A check the subtask actually performed, not a check the parent still has to repeat.</summary>
/// <param name="Status">Passed, Failed or NotRun.</param>
public sealed record SubtaskCheck(string Name, string Status, string? Details = null);

/// <summary>What one delegated task produced. The transcript is deliberately not here.</summary>
/// <param name="Answer">The subtask's final reply, which is all the calling model is given.</param>
/// <param name="Connection">Which connection answered, so the record shows what the work was worth.</param>
/// <param name="ElapsedMilliseconds">Wall time of the delegated run, including its model and tool calls; not provider-only latency.</param>
public sealed record SubtaskOutcome(
    string Task,
    string Answer,
    string Connection,
    int Messages,
    int ToolCalls,
    int FilesChanged,
    bool Completed,
    string? Error,
    IReadOnlyList<SubtaskArtifact> Artifacts,
    IReadOnlyList<SubtaskCheck> Checks,
    IReadOnlyList<string> Unresolved,
    long ElapsedMilliseconds = 0);

/// <summary>One turn of a subtask, kept for the user to read and withheld from the model.</summary>
public sealed record SubtaskTranscriptEntry(string Task, string Role, string Content, string? ToolName);

public sealed record SubtaskTranscript(IReadOnlyList<SubtaskTranscriptEntry> Transcript);


/// <summary>A run-owned handle; Result is present only for a terminal task retrieved by wait or stop.</summary>
public sealed record SubtaskSnapshot(string TaskId, string Task, string Status,
    SubtaskOutcome? Result = null, string? Activity = null, string? Error = null);

public sealed record SubtaskWaitResult(IReadOnlyList<SubtaskSnapshot> Tasks, string? Error = null, string? Next = null,
    bool TimedOut = false);

public sealed record SubtaskCompletion(SubtaskOutcome Outcome, IReadOnlyList<SubtaskTranscriptEntry> Transcript);
