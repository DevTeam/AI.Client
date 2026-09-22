namespace AI.Client.Mcp.BuiltIn.Files;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class DeleteFileTool(IPathGuard guard, IBuiltInToolReply reply) : IToolFactory
{
    public McpServerTool Create() => McpServerTool.Create(
        DeleteAsync,
        new McpServerToolCreateOptions
        {
            Description = "Delete a file. This cannot be undone. A directory is never removed by this tool — use delete_directory for that. "
                          + "The path must be absolute and covered by a directory grant with 'delete' access."
        });

    [McpServerTool(Name = "delete_file", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(DeleteFileResult))]
    private Task<CallToolResult> DeleteAsync(
        [Description("Absolute path of the file to delete.")] [MaxLength(4096)] string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string resolved;
        try
        {
            resolved = guard.Resolve(path, GrantCapability.Delete);
        }
        catch (GrantException error)
        {
            return Task.FromResult(reply.Reply(new DeleteFileResult(path, false, 0, error.Message), true));
        }

        if (Directory.Exists(resolved))
        {
            return Task.FromResult(reply.Reply(
                new DeleteFileResult(resolved, false, 0, "Path is a directory. Use delete_directory."), true));
        }

        try
        {
            var info = new FileInfo(resolved);
            if (!info.Exists)
            {
                return Task.FromResult(reply.Reply(new DeleteFileResult(resolved, false, 0, "File does not exist."), true));
            }

            // Measured before the delete, because afterwards there is nothing left to measure.
            var bytes = info.Length;
            File.Delete(resolved);
            return Task.FromResult(reply.Reply(new DeleteFileResult(resolved, true, bytes, null)));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Task.FromResult(reply.Reply(new DeleteFileResult(resolved, false, 0, error.Message), true));
        }
    }
}
