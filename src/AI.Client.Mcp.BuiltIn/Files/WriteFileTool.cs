namespace AI.Client.Mcp.BuiltIn.Files;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class WriteFileTool(IPathGuard guard, IBuiltInToolReply reply) : IToolFactory
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public McpServerTool Create() => McpServerTool.Create(
        WriteAsync,
        new McpServerToolCreateOptions
        {
            Description = "Create a file or replace its entire content with UTF-8 text. This overwrites an existing file; use edit_file to "
                          + "change part of one. The parent directory must already exist. "
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

        if (Directory.Exists(resolved))
        {
            return reply.Reply(new WriteFileResult(resolved, 0, false, "Path is a directory."), true);
        }

        var existed = File.Exists(resolved);
        try
        {
            await File.WriteAllTextAsync(resolved, content, Utf8, cancellationToken);
            return reply.Reply(new WriteFileResult(resolved, Utf8.GetByteCount(content), !existed, null));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return reply.Reply(new WriteFileResult(resolved, 0, false, error.Message), true);
        }
    }
}
