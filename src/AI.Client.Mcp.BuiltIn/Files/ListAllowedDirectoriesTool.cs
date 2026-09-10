namespace AI.Client.Mcp.BuiltIn.Files;

using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class ListAllowedDirectoriesTool(IPathGuard guard) : IToolFactory
{
    public McpServerTool Create() => McpServerTool.Create(
        List,
        new McpServerToolCreateOptions
        {
            Description = "List the directory grants of the current project: canonical root, whether it covers subdirectories and which of "
                          + "'read', 'write', 'edit' and 'delete' it allows. File system tools reject any path outside these roots, so call "
                          + "this first when a path is uncertain. An empty list means no file system access is granted."
        });

    [McpServerTool(Name = "list_allowed_directories", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(AllowedDirectoriesResult))]
    private CallToolResult List() => ToolReply.Of(new AllowedDirectoriesResult(
        guard.Grants.Select(grant => new AllowedDirectory(
            grant.Root,
            grant.Recursive,
            grant.Capabilities.Select(capability => capability.ToString().ToLowerInvariant()).Order(StringComparer.Ordinal).ToArray())).ToArray()));
}
