namespace AI.Mcp.BuiltIn.Archives;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using Files;
using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class ZipListTool(IPathGuard guard, IBuiltInToolReply reply) : IToolFactory
{
    // Approximate JSON overhead per entry beyond its own path — quotes, keys, commas, the sizes and
    // the timestamp — inflated the same way ListDirectoryTool inflates it, because the result is
    // serialized once as the tool's structured content and again as the chat message it is stored in.
    private const int EntryOverheadCharacters = 128;

    public McpServerTool Create() => McpServerTool.Create(
        ListAsync,
        new McpServerToolCreateOptions
        {
            Description = "List the entries of a zip archive with their uncompressed and compressed sizes and their modification times, "
                          + $"optionally narrowed by a glob `pattern`. At most {ArchiveLimits.Entries} entries are returned, and the result "
                          + "is also capped by total size; either cap sets `truncated: true`. Names are reported as stored, with forward "
                          + "slashes, and a directory entry is marked as such. TotalBytes and CompressedBytes describe the whole archive, "
                          + "not just the returned rows. Reading an entry's content is zip_read; unpacking entries is zip_extract. "
                          + "The path must be absolute and covered by a directory grant with 'read' access."
        });

    [McpServerTool(Name = "zip_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ZipListResult))]
    private Task<CallToolResult> ListAsync(
        [Description("Absolute path of the zip archive.")] [MaxLength(4096)] string path,
        [Description("Optional glob narrowing the entries: '*' matches within one path segment, '**' crosses segments and '?' matches one character. "
                     + "A pattern without a separator is matched against the entry name.")] [MaxLength(512)] string? pattern = null,
        CancellationToken cancellationToken = default)
    {
        string resolved;
        try
        {
            resolved = guard.Resolve(path, GrantCapability.Read);
        }
        catch (GrantException error)
        {
            return Task.FromResult(reply.Reply(new ZipListResult(path, [], 0, 0, 0, false, error.Message), true));
        }

        if (Directory.Exists(resolved))
        {
            return Task.FromResult(reply.Reply(new ZipListResult(resolved, [], 0, 0, 0, false, "Path is a directory."), true));
        }

        if (!File.Exists(resolved))
        {
            return Task.FromResult(reply.Reply(new ZipListResult(resolved, [], 0, 0, 0, false, "File does not exist."), true));
        }

        try
        {
            using var archive = ZipFile.OpenRead(resolved);
            var glob = pattern is { Length: > 0 } ? GlobPattern.Parse(pattern) : null;
            var entries = new List<ArchiveEntryInfo>();
            var budget = new ResultBudget(ArchiveLimits.ListCharacters);
            var truncated = false;
            var count = 0;
            long total = 0;
            long compressed = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // The totals keep covering the whole archive even after the listing was cut short,
                // so they are accumulated before anything can be skipped.
                var directory = ArchivePaths.IsDirectoryEntry(entry);
                var display = directory
                    ? ArchivePaths.Normalize(entry.FullName).TrimEnd('/')
                    : ArchivePaths.Normalize(entry.FullName);
                count++;
                total += entry.Length;
                compressed += entry.CompressedLength;

                if (display.Length == 0 || truncated) continue;
                if (glob is not null && !glob.IsMatch(Path.GetFileName(display), display)) continue;
                if (entries.Count == ArchiveLimits.Entries
                    || !budget.TryReserve(display.Length + EntryOverheadCharacters))
                {
                    truncated = true;
                    continue;
                }

                entries.Add(new ArchiveEntryInfo(display, directory ? "directory" : "file",
                    entry.Length, entry.CompressedLength, entry.LastWriteTime));
            }

            entries.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
            return Task.FromResult(reply.Reply(new ZipListResult(resolved, entries.ToArray(), count, total, compressed,
                truncated, null)));
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Task.FromResult(reply.Reply(new ZipListResult(resolved, [], 0, 0, 0, false,
                error is InvalidDataException ? $"Not a valid zip archive: {error.Message}" : error.Message), true));
        }
    }
}
