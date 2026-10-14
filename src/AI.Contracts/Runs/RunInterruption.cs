namespace AI.Contracts.Runs;

/// <summary>What cut an interrupted run short.</summary>
public enum RunInterruptionCause
{
    /// <summary>The Host was closed while the run was generating: the application quit or restarted.</summary>
    Shutdown,

    /// <summary>The Host stopped without closing: the run was still generating when it started again.</summary>
    Crash
}

/// <summary>
/// Why an <see cref="ChatRunStatus.Interrupted"/> run stopped, so the client can tell work a
/// restart left behind from work the person stopped. Every run one start of the Host recovered
/// shares the same <paramref name="RecoveredAt"/>, which is how the client groups them.
/// </summary>
/// <param name="Cause">Whether the Host was closed or stopped without closing.</param>
/// <param name="RecoveredAt">When the Host started again and took the run back.</param>
/// <param name="StoppedAt">When the run was cut short; unknown after a crash.</param>
/// <param name="ToolName">The model-facing name of a tool call that was started and never got a
/// result, so its outcome is unknown. Null when the run stopped outside a tool call.</param>
public sealed record RunInterruption(
    RunInterruptionCause Cause,
    DateTimeOffset RecoveredAt,
    DateTimeOffset? StoppedAt = null,
    string? ToolName = null);
