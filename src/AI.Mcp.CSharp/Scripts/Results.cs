namespace AI.Mcp.CSharp.Scripts;

using System.Text.Json;

/// <summary>
/// Everything one script run needs. The working directory and the environment are applied to the
/// server process for the duration of the run and then restored, so they are a convenience for the
/// script rather than a sandbox: like <c>process_run</c>, a script has whatever access the server
/// process itself has.
/// </summary>
public sealed record ScriptRequest(
    string Code,
    string[] Arguments,
    string[] Imports,
    string[] References,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyDictionary<string, JsonElement> Globals,
    int TimeoutMs);

/// <summary>One compiler message, or a synthetic entry describing a failure at run time.</summary>
public sealed record ScriptDiagnostic(string Severity, string Id, string Message, int Line, int Column);

/// <summary>
/// A variable the script declared, captured after the run so the caller can see what the code
/// actually produced instead of only what it printed.
/// </summary>
public sealed record ScriptVariable(string Name, string Type, string Value);

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
    string? Error);
