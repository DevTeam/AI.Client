namespace AI.Mcp.BuiltIn.Files;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using AI.Contracts.FileSystem;
using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class WriteFileTool(IPathGuard guard, IBuiltInToolReply reply, IFileSystem files) : IToolFactory
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public McpServerTool Create() => McpServerTool.Create(
        WriteAsync,
        new McpServerToolCreateOptions
        {
            Description = "Create a file or replace its entire content with UTF-8 text. This overwrites an existing file; use edit_file to "
                          + "change part of one. Missing parent directories are created. LF content written over a file that uses CRLF "
                          + "is written with CRLF. "
                          + "The path must be absolute and covered by a directory grant with 'write' access."
        });

    [McpServerTool(Name = "write_file", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(WriteFileResult))]
    private async Task<CallToolResult> WriteAsync(
        [Description("Absolute path of the file to write.")] [MaxLength(4096)] string path,
        [Description("Full new content of the file.")] [MaxLength(FileLimits.WriteCharacters)] string content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        string resolved;
        try
        {
            resolved = guard.Resolve(path, GrantCapability.Write);
        }
        catch (GrantException error)
        {
            return reply.Reply(new WriteFileResult(path, 0, false, error.Message), true);
        }

        if (await files.DirectoryExistsAsync(resolved, cancellationToken))
        {
            return reply.Reply(new WriteFileResult(resolved, 0, false, "Path is a directory."), true);
        }

        var existed = await files.FileExistsAsync(resolved, cancellationToken);
        try
        {
            // Reads hand the model LF text, so LF content written over a CRLF file gets its CRLF back
            // rather than a whole-file line-ending change.
            if (existed && content.Contains('\n', StringComparison.Ordinal) && !content.Contains('\r', StringComparison.Ordinal)
                && await LineEndings.IsCrlfAsync(files, resolved, cancellationToken))
                content = content.Replace("\n", "\r\n", StringComparison.Ordinal);
            await files.WriteTextAsync(resolved, content, cancellationToken);
            return reply.Reply(new WriteFileResult(resolved, Utf8.GetByteCount(content), !existed, null));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return reply.Reply(new WriteFileResult(resolved, 0, false, error.Message), true);
        }
    }
}
