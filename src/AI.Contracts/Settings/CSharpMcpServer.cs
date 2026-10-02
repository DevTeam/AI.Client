namespace AI.Contracts.Settings;

/// <summary>
/// The optional C# scripting MCP server. The binary ships only when the user opts into installing
/// it — the Host then starts it as a child process on demand. A settings entry is registered so
/// that the discovery endpoints and the settings UI know about it; the entry stays even when the
/// binary is missing, so the UI can still show what is available.
/// </summary>
public static class CSharpMcpServer
{
    public static readonly Guid Id = new("5a2e0d3c-8e7b-4a3f-9d1e-7c8b6a5f4e3d");

    /// <summary>Transport name reserved for the optional C# scripting server.</summary>
    public const string Transport = "Stdio";

    public static McpServerSettings Settings => new(Id, "C# scripts", Transport, true, "Ask", null,
        null, [], null, [], false);
}
