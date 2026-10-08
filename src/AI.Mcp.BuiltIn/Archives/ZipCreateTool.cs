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
public sealed class ZipCreateTool(IPathGuard guard, IBuiltInToolReply reply, IFileSystem files, IPath paths) : IToolFactory
{
    private readonly IPath _paths = paths;

    public McpServerTool Create() => McpServerTool.Create(
        CreateAsync,
        new McpServerToolCreateOptions
        {
            Description = "Pack files and directories into a new zip archive. A directory contributes its files recursively, under its own name, "
                          + "with entry names using forward slashes; empty directories are not represented. Version control and build directories "
                          + $"(.git, bin, obj, node_modules, ...) are skipped unless `excludeDefaults` is false. Every path needs a grant with 'read' "
                          + $"access and the archive path one with 'write' access; at most {ArchiveLimits.Sources} paths and "
                          + $"{ArchiveLimits.TransferBytes} bytes are accepted in one call. The archive is written to a temporary file in the same "
                          + "directory and moved into place only once it is complete, so a failed call leaves no half-written archive behind. An "
                          + "existing archive is an error without `overwrite`."
        });

    [McpServerTool(Name = "zip_create", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ZipCreateResult))]
    private async Task<CallToolResult> CreateAsync(
        [Description("Absolute path of the zip archive to create. Its parent directory must already exist.")] [MaxLength(4096)] string path,
        [Description("Absolute paths of the files and directories to pack. A directory is packed recursively.")]
        [MinLength(1)] [MaxLength(ArchiveLimits.Sources)] string[] paths,
        [Description("Replace an existing archive. Without it an existing archive fails the call.")] bool overwrite = false,
        [Description("Skip version control and build directories the same way the file tools do by default. "
                     + "Set false to pack them too.")] bool excludeDefaults = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        string resolved;
        try
        {
            resolved = guard.Resolve(path, GrantCapability.Write);
        }
        catch (GrantException error)
        {
            return reply.Reply(new ZipCreateResult(path, 0, 0, 0, false, error.Message), true);
        }

        if (await files.DirectoryExistsAsync(resolved, cancellationToken))
        {
            return reply.Reply(new ZipCreateResult(resolved, 0, 0, 0, false, "Path is a directory."), true);
        }

        var parent = _paths.GetDirectoryName(resolved);
        if (string.IsNullOrEmpty(parent) || !await files.DirectoryExistsAsync(parent, cancellationToken))
        {
            return reply.Reply(new ZipCreateResult(resolved, 0, 0, 0, false, "Parent directory does not exist."), true);
        }

        var exists = await files.FileExistsAsync(resolved, cancellationToken);
        if (exists && !overwrite)
        {
            return reply.Reply(new ZipCreateResult(resolved, 0, 0, 0, false,
                "Archive already exists. Pass overwrite: true to replace it."), true);
        }

        List<PackEntry> entries;
        try
        {
            entries = await Collect(paths, excludeDefaults, cancellationToken);
        }
        catch (GrantException error)
        {
            return reply.Reply(new ZipCreateResult(resolved, 0, 0, 0, exists, error.Message), true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return reply.Reply(new ZipCreateResult(resolved, 0, 0, 0, exists, error.Message), true);
        }

        if (entries.Count == 0)
        {
            return reply.Reply(new ZipCreateResult(resolved, 0, 0, 0, exists,
                "None of the given paths contains a file to pack."), true);
        }

        var total = entries.Sum(entry => entry.Length);
        if (total > ArchiveLimits.TransferBytes)
        {
            return reply.Reply(new ZipCreateResult(resolved, 0, total, 0, exists,
                $"{total} bytes exceed the packing limit of {ArchiveLimits.TransferBytes} bytes. Pack a part of the paths instead."), true);
        }

        // Everything is written to a temporary sibling and moved into place at the end: a failure
        // half way through then leaves neither a corrupt archive nor a destroyed previous one. The
        // final move relies on the contract's replace semantics, which is what lets it succeed on
        // Windows while something still holds the old archive open.
        var temporary = _paths.Combine(parent, _paths.GetFileName(resolved) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    archive.CreateEntryFromFile(entry.Source, entry.Name, CompressionLevel.Optimal);
                }
            }

            await files.MoveAsync(temporary, resolved, overwrite: true, cancellationToken);
            var written = await files.GetEntryAsync(resolved, cancellationToken);
            return reply.Reply(new ZipCreateResult(resolved, entries.Count, total, written?.Length ?? 0, exists, null));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return reply.Reply(new ZipCreateResult(resolved, 0, total, 0, exists, error.Message), true);
        }
        finally
        {
            await TryDeleteAsync(temporary, cancellationToken);
        }
    }

    /// <summary>
    /// Expands the given paths into the entries to write, in the order they were given. Every path
    /// is checked against the grants here rather than trusted, including each file found under a
    /// directory, because a directory grant is what decides what may leave the machine.
    /// </summary>
    private async Task<List<PackEntry>> Collect(string[] paths, bool excludeDefaults, CancellationToken cancellationToken)
    {
        var entries = new List<PackEntry>();
        var taken = new HashSet<string>(_paths.IsCaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = guard.Resolve(path, GrantCapability.Read);
            if (await files.FileExistsAsync(resolved, cancellationToken))
            {
                await AddAsync(entries, taken, resolved, _paths.GetFileName(resolved), cancellationToken);
                continue;
            }

            if (!await files.DirectoryExistsAsync(resolved, cancellationToken))
            {
                throw new GrantException($"Path does not exist: {resolved}");
            }

            var prefix = _paths.GetFileName(_paths.TrimEndingDirectorySeparator(resolved));
            // Links are not followed: a pack should not be able to walk out of a granted directory
            // through a junction, nor spin on a cycle. The options say so explicitly, the same way
            // the file tools do, instead of relying on the platform's enumeration defaults.
            var options = new FileEnumerationOptions(
                Recursive: true,
                SearchPattern: "*",
                SkipInaccessible: true,
                AttributesToSkip: FileAttributes.ReparsePoint);
            foreach (var entry in await files.ListEntriesAsync(resolved, options, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.IsDirectory)
                {
                    continue;
                }

                var relative = _paths.GetRelativePath(resolved, entry.Path);
                if (excludeDefaults && IsExcluded(relative)) continue;
                var name = prefix.Length == 0 ? relative : prefix + "/" + relative;
                await AddAsync(entries, taken, guard.Resolve(entry.Path, GrantCapability.Read),
                    name.Replace('\\', '/'), cancellationToken);
            }
        }

        return entries;
    }

    private async Task AddAsync(
        List<PackEntry> entries, HashSet<string> taken, string source, string name, CancellationToken cancellationToken)
    {
        if (!taken.Add(name))
        {
            throw new GrantException($"Two paths would be packed as the same entry: {name}");
        }

        var entry = await files.GetEntryAsync(source, cancellationToken)
                    ?? throw new GrantException($"Path does not exist: {source}");
        entries.Add(new PackEntry(source, name, entry.Length));
    }

    /// <summary>True when any directory of the relative path is one the file tools skip by default.</summary>
    private static bool IsExcluded(string relative)
    {
        foreach (var segment in relative.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
            if (FileLimits.DefaultExcludedNames.Contains(segment))
                return true;
        return false;
    }

    private async Task TryDeleteAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await files.DeleteFileAsync(path, cancellationToken);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // The archive itself is already reported on; a leftover temporary file must not turn a
            // successful pack into a failed call.
        }
    }

    private readonly record struct PackEntry(string Source, string Name, long Length);
}
