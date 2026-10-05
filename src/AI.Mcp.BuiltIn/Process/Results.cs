namespace AI.Mcp.BuiltIn.Process;

public sealed record ProcessRequest(
    string Executable,
    string[] Arguments,
    string WorkingDirectory,
    int TimeoutMs = ProcessRunner.MaxTimeoutMs);

public sealed record ProcessResult(
    int? ExitCode,
    string Stdout,
    string Stderr,
    long DurationMs,
    bool TimedOut,
    bool Truncated,
    string? Error);
