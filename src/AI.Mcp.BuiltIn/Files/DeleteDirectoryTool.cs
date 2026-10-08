namespace AI.Mcp.BuiltIn.Files;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using AI.Contracts.FileSystem;
using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class DeleteDirectoryTool(IPathGuard guard, IBuiltInToolReply reply, IFileSystem files) : IToolFactory
{
    public McpServerTool Create() => McpServerTool.Create(
        DeleteAsync,
        new McpServerToolCreateOptions
        {
            Description = "Delete a directory. Without 'recursive' only an empty directory is removed, and a non-empty one is refused; "
                          + "'recursive: true' deletes the directory and everything inside it, which cannot be undone. A file is never "
                          + "removed by this tool — use delete_file for that. "
                          + "The path must be absolute and covered by a directory grant with 'delete' access."
        });

    [McpServerTool(Name = "delete_directory", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(DeleteDirectoryResult))]
    private async Task<CallToolResult> DeleteAsync(
        [Description("Absolute path of the directory to delete.")] [MaxLength(4096)] string path,
        [Description("Delete the directory and everything inside it. Without this a non-empty directory is refused.")]
        bool recursive = false,
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
            return reply.Reply(new DeleteDirectoryResult(path, false, recursive, error.Message), true);
        }

        if (await files.FileExistsAsync(resolved, cancellationToken))
        {
            return reply.Reply(
                new DeleteDirectoryResult(resolved, false, recursive, "Path is a file. Use delete_file."), true);
        }

        if (!await files.DirectoryExistsAsync(resolved, cancellationToken))
        {
            return reply.Reply(
                new DeleteDirectoryResult(resolved, false, recursive, "Directory does not exist."), true);
        }

        try
        {
            // Asking whether it is empty is what makes the default safe: a call that did not pass
            // `recursive` can never turn out to have removed more than the directory it named.
            if (!recursive && (await files.ListEntriesAsync(resolved, recursive: false, cancellationToken)).Count > 0)
            {
                return reply.Reply(new DeleteDirectoryResult(resolved, false, recursive,
                    "Directory is not empty. Pass recursive: true to delete it and its contents."), true);
            }

            await files.DeleteDirectoryAsync(resolved, recursive, cancellationToken);
            return reply.Reply(new DeleteDirectoryResult(resolved, true, recursive, null));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return reply.Reply(new DeleteDirectoryResult(resolved, false, recursive, error.Message), true);
        }
    }
}
