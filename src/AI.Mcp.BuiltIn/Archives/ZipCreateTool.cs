namespace AI.Mcp.BuiltIn.Archives;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using Files;
using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class ZipCreateTool(IPathGuard guard, IBuiltInToolReply reply) : IToolFactory
{
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
    private Task<CallToolResult> CreateAsync(
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
            return Task.FromResult(reply.Reply(new ZipCreateResult(path, 0, 0, 0, false, error.Message), true));
        }

        if (Directory.Exists(resolved))
        {
            return Task.FromResult(reply.Reply(new ZipCreateResult(resolved, 0, 0, 0, false, "Path is a directory."), true));
        }

        var parent = Path.GetDirectoryName(resolved);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
        {
            return Task.FromResult(reply.Reply(new ZipCreateResult(resolved, 0, 0, 0, false, "Parent directory does not exist."), true));
        }

        var exists = File.Exists(resolved);
        if (exists && !overwrite)
        {
            return Task.FromResult(reply.Reply(new ZipCreateResult(resolved, 0, 0, 0, false,
                "Archive already exists. Pass overwrite: true to replace it."), true));
        }

        List<PackEntry> entries;
        try
        {
            entries = Collect(paths, excludeDefaults, cancellationToken);
        }
        catch (GrantException error)
        {
            return Task.FromResult(reply.Reply(new ZipCreateResult(resolved, 0, 0, 0, exists, error.Message), true));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Task.FromResult(reply.Reply(new ZipCreateResult(resolved, 0, 0, 0, exists, error.Message), true));
        }

        if (entries.Count == 0)
        {
            return Task.FromResult(reply.Reply(new ZipCreateResult(resolved, 0, 0, 0, exists,
                "None of the given paths contains a file to pack."), true));
        }

        var total = entries.Sum(entry => entry.Length);
        if (total > ArchiveLimits.TransferBytes)
        {
            return Task.FromResult(reply.Reply(new ZipCreateResult(resolved, 0, total, 0, exists,
                $"{total} bytes exceed the packing limit of {ArchiveLimits.TransferBytes} bytes. Pack a part of the paths instead."), true));
        }

        // Everything is written to a temporary sibling and moved into place at the end: a failure
        // half way through then leaves neither a corrupt archive nor a destroyed previous one.
        var temporary = Path.Combine(parent, Path.GetFileName(resolved) + "." + Guid.NewGuid().ToString("N") + ".tmp");
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

            File.Move(temporary, resolved, overwrite: true);
            return Task.FromResult(reply.Reply(new ZipCreateResult(resolved, entries.Count, total,
                new FileInfo(resolved).Length, exists, null)));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return Task.FromResult(reply.Reply(new ZipCreateResult(resolved, 0, total, 0, exists, error.Message), true));
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    /// <summary>
    /// Expands the given paths into the entries to write, in the order they were given. Every path
    /// is checked against the grants here rather than trusted, including each file found under a
    /// directory, because a directory grant is what decides what may leave the machine.
    /// </summary>
    private List<PackEntry> Collect(string[] paths, bool excludeDefaults, CancellationToken cancellationToken)
    {
        var entries = new List<PackEntry>();
        var taken = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = guard.Resolve(path, GrantCapability.Read);
            if (File.Exists(resolved))
            {
                Add(entries, taken, resolved, Path.GetFileName(resolved));
                continue;
            }

            if (!Directory.Exists(resolved))
            {
                throw new GrantException($"Path does not exist: {resolved}");
            }

            var prefix = Path.GetFileName(resolved.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                // Links are not followed: a pack should not be able to walk out of a granted
                // directory through a junction, nor spin on a cycle.
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = true,
            };
            foreach (var file in Directory.EnumerateFiles(resolved, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(resolved, file);
                if (excludeDefaults && IsExcluded(relative)) continue;
                var name = prefix.Length == 0 ? relative : prefix + "/" + relative;
                Add(entries, taken, guard.Resolve(file, GrantCapability.Read), name.Replace('\\', '/'));
            }
        }

        return entries;
    }

    private static void Add(List<PackEntry> entries, HashSet<string> taken, string source, string name)
    {
        if (!taken.Add(name))
        {
            throw new GrantException($"Two paths would be packed as the same entry: {name}");
        }

        entries.Add(new PackEntry(source, name, new FileInfo(source).Length));
    }

    /// <summary>True when any directory of the relative path is one the file tools skip by default.</summary>
    private static bool IsExcluded(string relative)
    {
        foreach (var segment in relative.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
            if (FileLimits.DefaultExcludedNames.Contains(segment))
                return true;
        return false;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // The archive itself is already reported on; a leftover temporary file must not turn a
            // successful pack into a failed call.
        }
    }

    private readonly record struct PackEntry(string Source, string Name, long Length);
}
