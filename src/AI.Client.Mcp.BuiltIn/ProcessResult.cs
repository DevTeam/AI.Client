namespace AI.Client.Mcp.BuiltIn;

public sealed record ProcessResult(
    int? ExitCode,
    string Stdout,
    string Stderr,
    long DurationMs,
    bool TimedOut,
    bool Truncated,
    string? Error);