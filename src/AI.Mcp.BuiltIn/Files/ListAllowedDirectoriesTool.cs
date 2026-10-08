namespace AI.Mcp.BuiltIn.Files;

using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class ListAllowedDirectoriesTool(IPathGuard guard, IBuiltInToolReply reply) : IToolFactory
{
    public McpServerTool Create() => McpServerTool.Create(
        List,
        new McpServerToolCreateOptions
        {
            Description = "List allowed directories with their purpose, canonical root, subdirectory coverage and 'read', 'write', 'edit', "
                          + "'delete' capabilities. The entry with purpose 'chatTemporary' is this chat's scratch directory. Use its root "
                          + "for intermediate files and large tool output you need to inspect in parts; it is removed when the chat or "
                          + "project is deleted, so do not use it for durable results. File system tools reject paths outside these roots."
        });

    [McpServerTool(Name = "list_allowed_directories", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(AllowedDirectoriesResult))]
    private CallToolResult List() => reply.Reply(new AllowedDirectoriesResult(
        guard.Grants.Select(grant => new AllowedDirectory(
            grant.Root,
            grant.Recursive,
            grant.Capabilities.Select(capability => capability.ToString().ToLowerInvariant()).Order(StringComparer.Ordinal).ToArray(),
            grant.Purpose)).ToArray()));
}
