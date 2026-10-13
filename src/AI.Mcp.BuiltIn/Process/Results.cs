namespace AI.Mcp.BuiltIn.Process;

using AI.Contracts.Processes;

/// <param name="Limits">The resources the process tree may use; <c>null</c> means <see cref="ChildProcessLimits.Default"/>.</param>
public sealed record ProcessRequest(
    string Executable,
    string[] Arguments,
    string WorkingDirectory,
    int TimeoutMs = ProcessRunner.MaxTimeoutMs,
    ChildProcessLimits? Limits = null);

/// <param name="LimitExceeded">
/// <see cref="ChildProcessJob.Memory"/> or <see cref="ChildProcessJob.Processes"/> when the tree ran into
/// that limit, otherwise <c>null</c>.
/// </param>
public sealed record ProcessResult(
    int? ExitCode,
    string Stdout,
    string Stderr,
    long DurationMs,
    bool TimedOut,
    bool Truncated,
    string? Error,
    string? LimitExceeded = null);
