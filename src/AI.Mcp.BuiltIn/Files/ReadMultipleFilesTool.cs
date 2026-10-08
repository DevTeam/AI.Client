namespace AI.Mcp.BuiltIn.Files;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using AI.Contracts.FileSystem;
using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class ReadMultipleFilesTool(IPathGuard guard, IBuiltInToolReply reply, IFileSystem files) : IToolFactory
{
    public McpServerTool Create() => McpServerTool.Create(
        ReadAsync,
        new McpServerToolCreateOptions
        {
            Description = $"Read up to {FileLimits.MultipleFilesPaths} text files in one call. A failed path reports its own error and does not "
                          + $"fail the call. The combined content is limited to {FileLimits.MultipleFilesCharacters} characters. "
                          + "Every path must be absolute and covered by a directory grant with 'read' access."
        });

    [McpServerTool(Name = "read_multiple_files", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(MultipleFilesResult))]
    private async Task<CallToolResult> ReadAsync(
        [Description("Absolute paths of the files to read.")] [MinLength(1)] [MaxLength(FileLimits.MultipleFilesPaths)] string[] paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var results = new List<FileText>(paths.Length);
        var budget = FileLimits.MultipleFilesCharacters;
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string resolved;
            try
            {
                resolved = guard.Resolve(path, GrantCapability.Read);
            }
            catch (GrantException error)
            {
                results.Add(new FileText(path, "", false, error.Message));
                continue;
            }

            if (budget <= 0)
            {
                results.Add(new FileText(resolved, "", true, "Combined content limit reached before this file was read."));
                continue;
            }

            try
            {
                var content = await files.ReadTextAsync(resolved, cancellationToken);
                if (content is null)
                {
                    results.Add(new FileText(resolved, "", false, "File does not exist."));
                    continue;
                }

                var truncated = content.Length > budget;
                if (truncated)
                {
                    content = content[..budget];
                }

                budget -= content.Length;
                results.Add(new FileText(resolved, content, truncated, null));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                results.Add(new FileText(resolved, "", false, error.Message));
            }
        }

        return reply.Reply(new MultipleFilesResult(results.ToArray(), null), results.All(file => file.Error is not null));
    }
}
