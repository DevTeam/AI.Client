namespace AI.Client.Mcp.BuiltIn.Files;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class EditFileTool(IPathGuard guard, IBuiltInToolReply reply) : IToolFactory
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public McpServerTool Create() => McpServerTool.Create(
        EditAsync,
        new McpServerToolCreateOptions
        {
            Description = "Apply exact text replacements to a file. Each 'oldText' must occur exactly once in the current content, so include "
                          + "enough surrounding lines to make it unique; edits are applied in order and a single failure applies nothing. "
                          + "Line endings are compared as LF and the file keeps its original style. Pass 'dryRun' to get the diff without "
                          + $"writing. At most {FileLimits.Edits} edits per call. "
                          + "The path must be absolute and covered by a directory grant with 'edit' access."
        });

    [McpServerTool(Name = "edit_file", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(EditFileResult))]
    private async Task<CallToolResult> EditAsync(
        [Description("Absolute path of the file to edit.")] [MaxLength(4096)] string path,
        [Description("Replacements to apply in order.")] [MinLength(1)] [MaxLength(FileLimits.Edits)] FileEdit[] edits,
        [Description("Return the diff without changing the file.")] bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edits);
        string resolved;
        try
        {
            resolved = guard.Resolve(path, GrantCapability.Edit);
        }
        catch (GrantException error)
        {
            return reply.Reply(new EditFileResult(path, 0, "", dryRun, error.Message), true);
        }

        string original;
        try
        {
            original = await File.ReadAllTextAsync(resolved, cancellationToken);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return reply.Reply(new EditFileResult(resolved, 0, "", dryRun, error.Message), true);
        }

        var crlf = original.Contains("\r\n", StringComparison.Ordinal);
        var content = Normalize(original);
        var diff = new StringBuilder();
        for (var index = 0; index < edits.Length; index++)
        {
            var oldText = Normalize(edits[index].OldText ?? "");
            var newText = Normalize(edits[index].NewText ?? "");
            if (oldText.Length == 0)
            {
                return reply.Reply(new EditFileResult(resolved, 0, "", dryRun, $"Edit {index + 1} has empty 'oldText'."), true);
            }

            var occurrences = Occurrences(content, oldText);
            if (occurrences != 1)
            {
                var reason = occurrences == 0 ? "was not found" : $"matches {occurrences} times";
                return reply.Reply(new EditFileResult(resolved, 0, "", dryRun,
                    $"Edit {index + 1} {reason}. Include more surrounding context to make it unique."), true);
            }

            var at = content.IndexOf(oldText, StringComparison.Ordinal);
            content = string.Concat(content.AsSpan(0, at), newText, content.AsSpan(at + oldText.Length));
            diff.Append("@@ edit ").Append(index + 1).AppendLine(" @@");
            Append(diff, '-', oldText);
            Append(diff, '+', newText);
        }

        if (!dryRun)
        {
            try
            {
                await File.WriteAllTextAsync(resolved, crlf ? content.Replace("\n", "\r\n", StringComparison.Ordinal) : content, Utf8, cancellationToken);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return reply.Reply(new EditFileResult(resolved, 0, "", dryRun, error.Message), true);
            }
        }

        return reply.Reply(new EditFileResult(resolved, edits.Length, diff.ToString(), dryRun, null));
    }

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static int Occurrences(string content, string value)
    {
        var count = 0;
        var at = 0;
        while ((at = content.IndexOf(value, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += value.Length;
            if (count > 1)
            {
                break;
            }
        }

        return count;
    }

    private static void Append(StringBuilder diff, char marker, string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        foreach (var line in text.Split('\n'))
        {
            diff.Append(marker).AppendLine(line);
        }
    }
}
