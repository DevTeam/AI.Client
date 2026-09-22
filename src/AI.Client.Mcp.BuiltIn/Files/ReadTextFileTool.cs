namespace AI.Client.Mcp.BuiltIn.Files;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class ReadTextFileTool(IPathGuard guard, IBuiltInToolReply reply) : IToolFactory
{
    public McpServerTool Create() => McpServerTool.Create(
        ReadAsync,
        new McpServerToolCreateOptions
        {
            Description = "Read a text file as UTF-8. Without 'head' or 'tail' the content is returned verbatim, so it can be written back "
                          + "unchanged; those two options instead return whole lines joined by LF and are mutually exclusive. Content is "
                          + $"limited to {FileLimits.ContentCharacters} characters. "
                          + "The path must be absolute and covered by a directory grant with 'read' access."
        });

    [McpServerTool(Name = "read_text_file", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(TextFileResult))]
    private async Task<CallToolResult> ReadAsync(
        [Description("Absolute path of the file to read.")] [MaxLength(4096)] string path,
        [Description("Return only the first N lines.")] [Range(1, 100000)] int? head = null,
        [Description("Return only the last N lines.")] [Range(1, 100000)] int? tail = null,
        CancellationToken cancellationToken = default)
    {
        if (head is not null && tail is not null)
        {
            return reply.Reply(new TextFileResult(path, "", 0, 0, false, "Specify either 'head' or 'tail', not both."), true);
        }

        string resolved;
        try
        {
            resolved = guard.Resolve(path, GrantCapability.Read);
        }
        catch (GrantException error)
        {
            return reply.Reply(new TextFileResult(path, "", 0, 0, false, error.Message), true);
        }

        if (Directory.Exists(resolved))
        {
            return reply.Reply(new TextFileResult(resolved, "", 0, 0, false, "Path is a directory. Use list_directory."), true);
        }

        try
        {
            return reply.Reply(head is null && tail is null
                ? await WholeAsync(resolved, cancellationToken)
                : await LinesAsync(resolved, head, tail, cancellationToken));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return reply.Reply(new TextFileResult(resolved, "", 0, 0, false, error.Message), true);
        }
    }

    private static async Task<TextFileResult> WholeAsync(string path, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(path);
        // One character beyond the limit tells truncation from a file that ends exactly at it.
        var buffer = new char[FileLimits.ContentCharacters + 1];
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(read), cancellationToken);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        var truncated = read > FileLimits.ContentCharacters;
        var content = new string(buffer, 0, Math.Min(read, FileLimits.ContentCharacters));
        return new TextFileResult(path, content, content.Length == 0 ? 0 : 1, Lines(content), truncated, null);
    }

    private static async Task<TextFileResult> LinesAsync(string path, int? head, int? tail, CancellationToken cancellationToken)
    {
        var selected = new List<string>();
        var firstLine = 1;
        var truncated = false;
        var characters = 0;
        await foreach (var line in File.ReadLinesAsync(path, cancellationToken))
        {
            if (head is { } limit)
            {
                if (selected.Count == limit || characters + line.Length + 1 > FileLimits.ContentCharacters)
                {
                    truncated = true;
                    break;
                }

                selected.Add(line);
                characters += line.Length + 1;
                continue;
            }

            selected.Add(line);
            characters += line.Length + 1;
            while (selected.Count > 1
                   && (characters > FileLimits.ContentCharacters || (tail is { } window && selected.Count > window)))
            {
                characters -= selected[0].Length + 1;
                selected.RemoveAt(0);
                firstLine++;
                truncated = true;
            }
        }

        return new TextFileResult(
            path,
            string.Join('\n', selected),
            selected.Count == 0 ? 0 : firstLine,
            selected.Count,
            truncated,
            null);
    }

    private static int Lines(string content)
    {
        if (content.Length == 0)
        {
            return 0;
        }

        var lines = 1;
        foreach (var character in content)
        {
            if (character == '\n')
            {
                lines++;
            }
        }

        return content.EndsWith('\n') ? lines - 1 : lines;
    }
}
