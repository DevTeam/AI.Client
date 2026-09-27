namespace AI.Mcp.BuiltIn.Files;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class GetFileInfoTool(IPathGuard guard, IBuiltInToolReply reply) : IToolFactory
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
    private Task<CallToolResult> InfoAsync(
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
            return Task.FromResult(Failure(path, error.Message));
        }

        try
        {
            var directory = Directory.Exists(resolved);
            FileSystemInfo info = directory ? new DirectoryInfo(resolved) : new FileInfo(resolved);
            if (!info.Exists)
            {
                return Task.FromResult(Failure(resolved, "Path does not exist."));
            }

            return Task.FromResult(reply.Reply(new FileInfoResult(
                resolved,
                directory ? "directory" : "file",
                directory ? 0 : ((FileInfo)info).Length,
                info.CreationTimeUtc,
                info.LastWriteTimeUtc,
                (info.Attributes & FileAttributes.ReadOnly) != 0,
                info.LinkTarget,
                null)));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(Failure(resolved, error.Message));
        }
    }

    private CallToolResult Failure(string path, string message) => reply.Reply(
        new FileInfoResult(path, "unknown", 0, default, default, false, null, message),
        true);
}
