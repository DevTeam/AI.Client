namespace AI.Mcp.BuiltIn.Files;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using AI.Contracts.FileSystem;
using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class GetFileInfoTool(IPathGuard guard, IBuiltInToolReply reply, IFileSystem files) : IToolFactory
{
    public McpServerTool Create() => McpServerTool.Create(
        InfoAsync,
        new McpServerToolCreateOptions
        {
            Description = "Report kind, size, timestamps, read-only flag and link target of a file or directory without reading its content. "
                          + "The path must be absolute and covered by a directory grant with 'read' access."
        });

    [McpServerTool(Name = "get_file_info", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(FileInfoResult))]
    private async Task<CallToolResult> InfoAsync(
        [Description("Absolute path of the file or directory to inspect.")] [MaxLength(4096)] string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string resolved;
        try
        {
            resolved = guard.Resolve(path, GrantCapability.Read);
        }
        catch (GrantException error)
        {
            return Failure(path, error.Message);
        }

        try
        {
            var entry = await files.GetEntryAsync(resolved, cancellationToken);
            if (entry is null)
            {
                return Failure(resolved, "Path does not exist.");
            }

            var attributes = await files.GetAttributesAsync(resolved, cancellationToken);
            // Creation time and the link target are the two facts the contract does not expose yet
            // (no member answers either); they keep their direct platform call and are reported to
            // the lead as a charter gap rather than widening the frozen interface here.
            var info = new FileInfo(resolved);
            return reply.Reply(new FileInfoResult(
                resolved,
                entry.IsDirectory ? "directory" : "file",
                entry.Length,
                info.CreationTimeUtc,
                await files.GetLastWriteTimeAsync(resolved, cancellationToken),
                (attributes & FileAttributes.ReadOnly) != 0,
                info.LinkTarget,
                null));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Failure(resolved, error.Message);
        }
    }

    private CallToolResult Failure(string path, string message) => reply.Reply(
        new FileInfoResult(path, "unknown", 0, default, default, false, null, message),
        true);
}
