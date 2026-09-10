namespace AI.Client.Mcp.BuiltIn.Process;

public sealed record ProcessRequest(
    string Executable,
    string[] Arguments,
    string WorkingDirectory,
    int TimeoutMs = 120000);

public sealed record ProcessResult(
    int? ExitCode,
    string Stdout,
    string Stderr,
    long DurationMs,
    bool TimedOut,
    bool Truncated,
    string? Error);
