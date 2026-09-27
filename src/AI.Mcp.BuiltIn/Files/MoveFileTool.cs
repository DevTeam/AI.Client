namespace AI.Mcp.BuiltIn.Files;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class MoveFileTool(IPathGuard guard, IBuiltInToolReply reply) : IToolFactory
{
    public McpServerTool Create() => McpServerTool.Create(
        MoveAsync,
        new McpServerToolCreateOptions
        {
            Description = "Move or rename a file or directory. The source needs a directory grant with 'delete' access and the destination "
                          + "one with 'write' access, so a read-only grant cannot be used to move data out of it. An existing destination "
                          + "is never overwritten. Both paths must be absolute."
        });

    [McpServerTool(Name = "move_file", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(MoveFileResult))]
    private Task<CallToolResult> MoveAsync(
        [Description("Absolute path to move from.")] [MaxLength(4096)] string source,
        [Description("Absolute path to move to. Must not exist.")] [MaxLength(4096)] string destination,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string resolvedSource;
        string resolvedDestination;
        try
        {
            resolvedSource = guard.Resolve(source, GrantCapability.Delete);
            resolvedDestination = guard.Resolve(destination, GrantCapability.Write);
        }
        catch (GrantException error)
        {
            return Task.FromResult(reply.Reply(new MoveFileResult(source, destination, error.Message), true));
        }

        var directory = Directory.Exists(resolvedSource);
        if (!directory && !File.Exists(resolvedSource))
        {
            return Task.FromResult(reply.Reply(new MoveFileResult(resolvedSource, resolvedDestination, "Source does not exist."), true));
        }

        if (File.Exists(resolvedDestination) || Directory.Exists(resolvedDestination))
        {
            return Task.FromResult(reply.Reply(new MoveFileResult(resolvedSource, resolvedDestination, "Destination already exists."), true));
        }

        try
        {
            if (directory)
            {
                Directory.Move(resolvedSource, resolvedDestination);
            }
            else
            {
                File.Move(resolvedSource, resolvedDestination, overwrite: false);
            }

            return Task.FromResult(reply.Reply(new MoveFileResult(resolvedSource, resolvedDestination, null)));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Task.FromResult(reply.Reply(new MoveFileResult(resolvedSource, resolvedDestination, error.Message), true));
        }
    }
}
