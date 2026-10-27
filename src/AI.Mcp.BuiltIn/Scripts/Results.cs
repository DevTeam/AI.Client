namespace AI.Mcp.BuiltIn.Scripts;

using System.Text.Json;
using AI.Sandbox;

/// <summary>
/// Everything one script run needs. The working directory and the environment are applied to the
/// worker process for the run; they are a convenience for the script, not a boundary. What the
/// script may reach is decided by the session's sandbox, exactly as for <c>process_run_sandboxed</c>.
/// </summary>
public sealed record ScriptRequest(
    string Code,
    string[] Arguments,
    string[] Imports,
    string[] References,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyDictionary<string, JsonElement> Globals,
    int TimeoutMs,
    ChildProcessLimits? Limits = null);

/// <summary>One compiler message, or a synthetic entry describing a failure at run time.</summary>
public sealed record ScriptDiagnostic(string Severity, string Id, string Message, int Line, int Column);

/// <summary>
/// A variable the script declared, captured after the run so the caller can see what the code
/// actually produced instead of only what it printed.
/// </summary>
public sealed record ScriptVariable(string Name, string Type, string Value);

/// <param name="Sandbox">The OS sandbox the run was confined by, or <c>null</c> when it was not confined.</param>
public sealed record ScriptResult(
    bool Success,
    string? ReturnValue,
    string? ReturnType,
    ScriptVariable[] Variables,
    string Stdout,
    string Stderr,
    ScriptDiagnostic[] Diagnostics,
    long DurationMs,
    bool TimedOut,
    bool Truncated,
    string? Error,
    string? LimitExceeded = null,
    string? Sandbox = null);
