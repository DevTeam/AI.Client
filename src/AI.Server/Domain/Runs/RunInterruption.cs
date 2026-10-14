namespace AI.Domain.Runs;

public enum RunInterruptionCause { Shutdown, Crash }

/// <summary>
/// Why a run is <see cref="RunStatus.Interrupted"/>: the Host was closed (<see cref="StoppedAt"/>
/// known) or stopped without closing, and when it took the run back. <see cref="RecoveredAt"/> is
/// null between a shutdown and the next start.
/// </summary>
public sealed record RunInterruption(
    RunInterruptionCause Cause,
    DateTimeOffset? StoppedAt,
    DateTimeOffset? RecoveredAt,
    string? ToolName);
