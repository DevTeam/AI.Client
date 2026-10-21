namespace AI.Contracts.Settings;

/// <summary>
/// The retired optional C# scripting MCP server. Its only tool, <c>cs_run</c>, now belongs to the
/// built-in server (<see cref="DefaultMcpServer"/>). The id is kept only so that settings, projects
/// and chats written while the server existed can be migrated when they are read; nothing
/// registers or starts it anymore.
/// </summary>
public static class LegacyCSharpMcpServer
{
    public static readonly Guid Id = new("5a2e0d3c-8e7b-4a3f-9d1e-7c8b6a5f4e3d");

    /// <summary>The server's only tool, which kept its name when it moved to the built-in server.</summary>
    public const string ScriptToolName = "cs_run";
}
