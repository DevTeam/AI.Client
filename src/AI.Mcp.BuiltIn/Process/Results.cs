namespace AI.Mcp.BuiltIn.Process;

using AI.Sandbox;

/// <param name="Limits">The resources the process tree may use; <c>null</c> means <see cref="ChildProcessLimits.Default"/>.</param>
/// <param name="Sandboxed">
/// Whether the tree is confined to the session's grants: <c>process_run_sandboxed</c> always is,
/// <c>process_run</c> never.
/// </param>
public sealed record ProcessRequest(
    string Executable,
    string[] Arguments,
    string WorkingDirectory,
    int TimeoutMs = ProcessRunner.MaxTimeoutMs,
    ChildProcessLimits? Limits = null,
    bool Sandboxed = true);

/// <param name="LimitExceeded">
/// <see cref="ChildProcessJob.Memory"/> or <see cref="ChildProcessJob.Processes"/> when the tree ran into
/// that limit, otherwise <c>null</c>.
/// </param>
/// <param name="Sandbox">The OS sandbox the run was confined by, or <c>null</c> when it was not confined.</param>
public sealed record ProcessResult(
    int? ExitCode,
    string Stdout,
    string Stderr,
    long DurationMs,
    bool TimedOut,
    bool Truncated,
    string? Error,
    string? LimitExceeded = null,
    string? Sandbox = null,
    string Status = "completed",
    string? ProcessId = null,
    string? StdoutPath = null,
    string? StderrPath = null,
    long StdoutOffset = 0,
    long StderrOffset = 0,
    bool HasMoreOutput = false);
