namespace AI.Mcp.BuiltIn.Archives;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using AI.Contracts.FileSystem;
using Files;
using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class ZipExtractTool(IPathGuard guard, IBuiltInToolReply reply, IFileSystem files, IPath paths) : IToolFactory
{
    private const int EntryOverheadCharacters = 96;

    private readonly IPath _paths = paths;

    public McpServerTool Create() => McpServerTool.Create(
        ExtractAsync,
        new McpServerToolCreateOptions
        {
            Description = "Unpack entries of a zip archive into a directory, creating missing parents. An entry name never decides where a file "
                          + "lands: each one is re-based under the destination and checked for containment, so an entry that is rooted or climbs "
                          + "out with '..' is an error and nothing is written at all. The whole call is validated before the first write: a name "
                          + "that escapes, a file that already exists without `overwrite`, or an archive over the entry or size cap fails the call "
                          + $"as a whole rather than extracting part of it. Caps: {ArchiveLimits.ExtractEntries} entries and "
                          + $"{ArchiveLimits.TransferBytes} bytes; narrowing with `pattern` is the way through an archive that exceeds them. "
                          + "The archive path needs a grant with 'read' access and the destination one with 'write' access."
        });

    [McpServerTool(Name = "zip_extract", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ZipExtractResult))]
    private async Task<CallToolResult> ExtractAsync(
        [Description("Absolute path of the zip archive to unpack.")] [MaxLength(4096)] string path,
        [Description("Absolute path of the directory to unpack into. Missing parents are created.")] [MaxLength(4096)] string destination,
        [Description("Optional glob narrowing the entries to unpack: '*' matches within one path segment, '**' crosses segments. "
                     + "A pattern without a separator is matched against the entry name.")] [MaxLength(512)] string? pattern = null,
        [Description("Replace files that already exist. Without it an existing file fails the call before anything is written.")]
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        string resolved;
        string root;
        try
        {
            resolved = guard.Resolve(path, GrantCapability.Read);
            root = guard.Resolve(destination, GrantCapability.Write);
        }
        catch (GrantException error)
        {
            return reply.Reply(new ZipExtractResult(path, destination, [], 0, overwrite, false, error.Message), true);
        }

        if (await files.DirectoryExistsAsync(resolved, cancellationToken))
        {
            return reply.Reply(new ZipExtractResult(resolved, root, [], 0, overwrite, false, "Path is a directory."), true);
        }

        if (!await files.FileExistsAsync(resolved, cancellationToken))
        {
            return reply.Reply(new ZipExtractResult(resolved, root, [], 0, overwrite, false, "File does not exist."), true);
        }

        if (await files.FileExistsAsync(root, cancellationToken))
        {
            return reply.Reply(new ZipExtractResult(resolved, root, [], 0, overwrite, false,
                "Destination is a file, not a directory."), true);
        }

        try
        {
            return await ExtractCoreAsync(resolved, root, pattern, overwrite, cancellationToken);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return reply.Reply(new ZipExtractResult(resolved, root, [], 0, overwrite, false,
                error is InvalidDataException ? $"Not a valid zip archive: {error.Message}" : error.Message), true);
        }
    }

    /// <summary>
    /// Plans the whole extraction, then performs it. Planning first is what makes the call
    /// all-or-nothing: every entry is resolved, counted and checked against what is already on disk
    /// before a single directory is created.
    /// </summary>
    private async Task<CallToolResult> ExtractCoreAsync(string archivePath, string root, string? pattern, bool overwrite, CancellationToken cancellationToken)
    {
        // Archive IO stays with System.IO.Compression: a zip is a container format, not a file
        // operation, so only the calls around it belong to the contract.
        using var archive = ZipFile.OpenRead(archivePath);
        var glob = pattern is { Length: > 0 } ? GlobPattern.Parse(pattern) : null;
        var planned = new List<(ZipArchiveEntry Entry, string FullPath, long Length)>();
        // Two names that differ only in case describe one file on Windows, so the collision check
        // follows the file system rather than the archive's own case sensitivity.
        var taken = new HashSet<string>(_paths.IsCaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        var skipped = 0;
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ArchivePaths.IsDirectoryEntry(entry)) continue;
            var name = ArchivePaths.Normalize(entry.FullName);
            if (glob is not null && !glob.IsMatch(_paths.GetFileName(name), name))
            {
                skipped++;
                continue;
            }

            if (!ArchivePaths.TryResolve(_paths, root, entry.FullName, out var full))
            {
                return reply.Reply(new ZipExtractResult(archivePath, root, [], skipped, overwrite, false,
                    $"Entry '{name}' would be written outside the destination directory, so nothing was extracted."), true);
            }

            if (!taken.Add(full))
            {
                return reply.Reply(new ZipExtractResult(archivePath, root, [], skipped, overwrite, false,
                    $"Two entries extract to the same path: {full}"), true);
            }

            if (!overwrite && await files.FileExistsAsync(full, cancellationToken))
            {
                return reply.Reply(new ZipExtractResult(archivePath, root, [], skipped, overwrite, false,
                    $"File already exists: {full}. Pass overwrite: true to replace it, or unpack into another directory."), true);
            }

            planned.Add((entry, full, entry.Length));
            total += entry.Length;
            if (planned.Count > ArchiveLimits.ExtractEntries || total > ArchiveLimits.TransferBytes)
            {
                return reply.Reply(new ZipExtractResult(archivePath, root, [], skipped, overwrite, false,
                    $"Archive exceeds the extraction limit of {ArchiveLimits.ExtractEntries} entries and {ArchiveLimits.TransferBytes} "
                    + "uncompressed bytes. Narrow it with 'pattern'."), true);
            }
        }

        var extracted = new List<ExtractedFile>();
        var budget = new ResultBudget(ArchiveLimits.ListCharacters);
        var truncated = false;
        long written = 0;
        foreach (var (entry, full, length) in planned)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // The destination directory of an entry is asked for explicitly: a zip entry carries no
            // directories of its own, so without this an entry in a subdirectory would fail.
            if (_paths.GetDirectoryName(full) is { Length: > 0 } parent)
            {
                await files.CreateDirectoryAsync(parent, ownerOnly: false, cancellationToken);
            }

            entry.ExtractToFile(full, overwrite);
            written += length;

            if (truncated) continue;
            if (extracted.Count == ArchiveLimits.ResultEntries || !budget.TryReserve(full.Length + EntryOverheadCharacters))
            {
                truncated = true;
                continue;
            }

            extracted.Add(new ExtractedFile(full, length));
        }

        return reply.Reply(new ZipExtractResult(archivePath, root, extracted.ToArray(), skipped, overwrite, truncated, null));
    }
}
