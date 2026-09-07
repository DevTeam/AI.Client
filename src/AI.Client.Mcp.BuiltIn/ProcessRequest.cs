namespace AI.Client.Mcp.BuiltIn;

public sealed record ProcessRequest(
    string Executable,
    string[] Arguments,
    string WorkingDirectory,
    int TimeoutMs = 120000);