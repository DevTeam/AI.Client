namespace AI.Mcp.BuiltIn.Archives;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class ZipReadTool(IPathGuard guard, IBuiltInToolReply reply) : IToolFactory
{
    public McpServerTool Create() => McpServerTool.Create(
        ReadAsync,
        new McpServerToolCreateOptions
        {
            Description = "Read one entry of a zip archive as UTF-8 text, without unpacking the archive. The content is returned verbatim "
                          + $"and limited to {ArchiveLimits.ReadCharacters} characters, which sets `truncated`; a truncated entry holds whole "
                          + "characters but may end mid-line. Decoding is UTF-8 with BOM detection, and an entry whose first characters contain a "
                          + "NUL byte is reported as binary rather than returned as garbled text. Entry names are case-sensitive and use forward "
                          + "slashes. The archive path must be absolute and covered by a directory grant with 'read' access."
        });

    [McpServerTool(Name = "zip_read", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ZipReadResult))]
    private async Task<CallToolResult> ReadAsync(
        [Description("Absolute path of the zip archive.")] [MaxLength(4096)] string path,
        [Description("Name of the entry inside the archive, as reported by zip_list.")] [MaxLength(4096)] string entryPath,
        CancellationToken cancellationToken = default)
    {
        string resolved;
        try
        {
            resolved = guard.Resolve(path, GrantCapability.Read);
        }
        catch (GrantException error)
        {
            return reply.Reply(new ZipReadResult(path, entryPath, "", 0, false, error.Message), true);
        }

        if (Directory.Exists(resolved))
        {
            return reply.Reply(new ZipReadResult(resolved, entryPath, "", 0, false, "Path is a directory."), true);
        }

        if (!File.Exists(resolved))
        {
            return reply.Reply(new ZipReadResult(resolved, entryPath, "", 0, false, "File does not exist."), true);
        }

        var wanted = ArchivePaths.Normalize(entryPath).TrimStart('/').TrimEnd('/');
        if (wanted.Length == 0)
        {
            return reply.Reply(new ZipReadResult(resolved, entryPath, "", 0, false, "Entry name cannot be empty."), true);
        }

        try
        {
            using var archive = ZipFile.OpenRead(resolved);
            foreach (var entry in archive.Entries)
            {
                // Entry names are compared exactly: a zip is a case-sensitive namespace even where
                // the file system it was unpacked onto would not be.
                if (!string.Equals(ArchivePaths.Normalize(entry.FullName).TrimEnd('/'), wanted, StringComparison.Ordinal))
                {
                    continue;
                }

                if (ArchivePaths.IsDirectoryEntry(entry))
                {
                    return reply.Reply(new ZipReadResult(resolved, wanted, "", entry.Length, false, "Entry is a directory."), true);
                }

                return await ReadEntryAsync(resolved, wanted, entry, cancellationToken);
            }

            return reply.Reply(new ZipReadResult(resolved, wanted, "", 0, false, "Entry is not in the archive. Use zip_list."), true);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return reply.Reply(new ZipReadResult(resolved, wanted, "", 0, false,
                error is InvalidDataException ? $"Not a valid zip archive: {error.Message}" : error.Message), true);
        }
    }

    private async Task<CallToolResult> ReadEntryAsync(
        string archivePath, string entryName, ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        // One character beyond the limit tells a truncated entry from one that ends exactly at it,
        // exactly as read_text_file does for a file on disk.
        var buffer = new char[ArchiveLimits.ReadCharacters + 1];
        var read = 0;
        await using (var stream = entry.Open())
        {
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            while (read < buffer.Length)
            {
                var count = await reader.ReadAsync(buffer.AsMemory(read), cancellationToken);
                if (count == 0) break;
                read += count;
            }
        }

        var truncated = read > ArchiveLimits.ReadCharacters;
        var content = new string(buffer, 0, Math.Min(read, ArchiveLimits.ReadCharacters));

        // A NUL byte is the usual cheap tell for binary content: decoding it as UTF-8 produces text
        // that is worse than useless to the model, so it is reported as an entry it cannot read.
        if (content.Contains('\0'))
        {
            return reply.Reply(new ZipReadResult(archivePath, entryName, "", entry.Length, false,
                "Entry appears to be binary."), true);
        }

        return reply.Reply(new ZipReadResult(archivePath, entryName, content, entry.Length, truncated, null));
    }
}
