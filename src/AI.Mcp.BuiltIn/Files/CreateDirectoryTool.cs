namespace AI.Mcp.BuiltIn.Files;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class CreateDirectoryTool(IPathGuard guard, IBuiltInToolReply reply) : IToolFactory
{
    public McpServerTool Create() => McpServerTool.Create(
        CreateAsync,
        new McpServerToolCreateOptions
        {
            Description = "Create a directory, including missing parents. Succeeds if it already exists. "
                          + "The path must be absolute and covered by a directory grant with 'write' access."
        });

    [McpServerTool(Name = "create_directory", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(CreateDirectoryResult))]
    private Task<CallToolResult> CreateAsync(
        [Description("Absolute path of the directory to create.")] [MaxLength(4096)] string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string resolved;
        try
        {
            resolved = guard.Resolve(path, GrantCapability.Write);
        }
        catch (GrantException error)
        {
            return Task.FromResult(reply.Reply(new CreateDirectoryResult(path, false, error.Message), true));
        }

        if (Directory.Exists(resolved))
        {
            return Task.FromResult(reply.Reply(new CreateDirectoryResult(resolved, false, null)));
        }

        try
        {
            Directory.CreateDirectory(resolved);
            return Task.FromResult(reply.Reply(new CreateDirectoryResult(resolved, true, null)));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Task.FromResult(reply.Reply(new CreateDirectoryResult(resolved, false, error.Message), true));
        }
    }
}
