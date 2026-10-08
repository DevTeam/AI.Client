using System.IO.Enumeration;
using System.Runtime.CompilerServices;

namespace AI.Contracts.FileSystem;

/// <summary>
/// <see cref="IFileSystem"/> over the real file system of the machine this build runs on.
/// </summary>
/// <remarks>
/// This is the only type besides <see cref="SystemPath"/> in this assembly that may touch
/// <c>System.IO</c>; anything else in the product that would reach for the disk has to take
/// <see cref="IFileSystem"/> and be handed one of these, or a fake in tests.
/// The behavior here is the behavior the storage layer already had: reads retry because a document
/// is briefly unopenable while it is being replaced, a save replaces rather than renames so that a
/// reader holding the target open cannot fail it, a save survives a failure from the platform when
/// the replacement was already requested, and a failure names the path it is about.
/// </remarks>
public sealed class SystemFileSystem : IFileSystem
{
    /// <summary>
    /// How many times a read may find a document mid-replacement before giving up. Five attempts
    /// spaced 1, 2, 3 and 4 milliseconds apart outlast that window many times over, and a document
    /// that is genuinely absent pays those ten milliseconds once.
    /// </summary>
    private const int MaxReadAttempts = 5;

    public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(File.Exists(path));

    public Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(Directory.Exists(path));

    public Task<FileSystemEntry?> GetEntryAsync(string path, CancellationToken cancellationToken)
    {
        // Asked of both kinds separately: File.Exists is already false for a directory, which is the
        // answer the contract wants, and the pair answers whether there is anything there at all.
        if (!File.Exists(path) && !Directory.Exists(path)) return Task.FromResult<FileSystemEntry?>(null);
        var attributes = Guarded(() => File.GetAttributes(path), path);
        var isDirectory = (attributes & FileAttributes.Directory) == FileAttributes.Directory;
        // A directory has no length of its own; a link is measured by its target, which is what a
        // person means by the size of what they are looking at.
        var length = isDirectory ? 0 : new FileInfo(path).Length;
        return Task.FromResult<FileSystemEntry?>(
            new FileSystemEntry(path, Path.GetFileName(path), isDirectory, length, attributes));
    }

    public Task<FileAttributes> GetAttributesAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(Guarded(() => File.GetAttributes(path), path));

    public Task<DateTimeOffset> GetLastWriteTimeAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(Guarded(() => new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero), path));

    public Task<string?> ReadTextAsync(string path, CancellationToken cancellationToken) =>
        ReadTextAsync(path, FileReadOptions.Default, cancellationToken);

    public Task<string?> ReadTextAsync(string path, FileReadOptions options, CancellationToken cancellationToken) =>
        WithRetryAsync<string>(path, async () =>
        {
            await using var stream = Open(path, options);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(cancellationToken);
        }, cancellationToken);

    public Task<byte[]?> ReadBytesAsync(string path, CancellationToken cancellationToken) =>
        ReadBytesAsync(path, FileReadOptions.Default, cancellationToken);

    public Task<byte[]?> ReadBytesAsync(string path, FileReadOptions options, CancellationToken cancellationToken) =>
        WithRetryAsync<byte[]>(path, async () =>
        {
            await using var stream = Open(path, options);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            return buffer.ToArray();
        }, cancellationToken);

    public IAsyncEnumerable<string> ReadLinesAsync(string path, CancellationToken cancellationToken) =>
        ReadLinesAsync(path, FileReadOptions.Default, cancellationToken);

    /// <summary>
    /// Reads the document line by line through one open stream, so a caller searching a large file
    /// pays for the buffer it asked for rather than for the whole document in memory. Lines are
    /// split on <c>\n</c> with a trailing <c>\r</c> dropped, which is also what the platform's own
    /// line reader does with either ending.
    /// </summary>
    public async IAsyncEnumerable<string> ReadLinesAsync(string path, FileReadOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stream = await OpenReadAsync(path, options, cancellationToken);
        if (stream is null) yield break;
        await using var owned = stream;
        using var reader = new StreamReader(owned);
        while (await reader.ReadLineAsync(cancellationToken) is { } line) yield return line;
    }

    public Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken) =>
        OpenReadAsync(path, FileReadOptions.Default, cancellationToken);

    /// <summary>
    /// An open stream over the document. Absence is answered with null rather than an exception,
    /// matching every other read. Opening retries for the same reason reading does: a replacement in
    /// flight makes the document briefly unopenable, and a caller that opened it a moment earlier
    /// would never have noticed.
    /// </summary>
    public Task<Stream?> OpenReadAsync(string path, FileReadOptions options, CancellationToken cancellationToken) =>
        WithRetryAsync<Stream>(path,
            () => Task.FromResult<Stream?>(Open(path, options)),
            cancellationToken);

    /// <summary>
    /// Reads a document while a save of it may be landing. Sharing must include delete by default —
    /// a save finishes by replacing this file, and Windows refuses to replace a file that somebody
    /// holds open without it. A read that finds the document briefly unopenable, or briefly absent,
    /// waits and tries again, because either complaint is untrue a moment later. Only a failure that
    /// outlasts every attempt is reported, and only then does a missing file read as missing.
    /// </summary>
    private static async Task<T?> WithRetryAsync<T>(string path, Func<Task<T?>> read,
        CancellationToken cancellationToken) where T : class
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await read();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                if (attempt == MaxReadAttempts)
                    return error is FileNotFoundException or DirectoryNotFoundException ? null : throw Named(error, path);
                await Task.Delay(attempt, cancellationToken);
            }
        }
    }

    private static FileStream Open(string path, FileReadOptions options) => new(path, new FileStreamOptions
    {
        Mode = FileMode.Open,
        Access = FileAccess.Read,
        Share = options.Share,
        BufferSize = options.BufferSize,
        Options = options.Options | FileOptions.Asynchronous,
    });

    public async Task WriteTextAsync(string path, string content, CancellationToken cancellationToken)
    {
        var directory = DirectoryOf(path);
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path, content, cancellationToken);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            throw Named(error, path);
        }
    }

    public async Task WriteBytesAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        var directory = DirectoryOf(path);
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(path, content, cancellationToken);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            throw Named(error, path);
        }
    }

    public async Task AppendTextAsync(string path, string content, CancellationToken cancellationToken)
    {
        var directory = DirectoryOf(path);
        try
        {
            Directory.CreateDirectory(directory);
            await File.AppendAllTextAsync(path, content, cancellationToken);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            throw Named(error, path);
        }
    }

    /// <summary>
    /// A stream to write the document through. Sharing read is deliberate — a reader must not be
    /// shut out by this writer — which is exactly why an exclusive lock for the lifetime of a
    /// process is not expressed through this member: see the ADR's documented exception on
    /// single-instance protection.
    /// </summary>
    public Task<Stream> OpenWriteAsync(string path, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        try
        {
            return Task.FromResult<Stream>(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read));
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            throw Named(error, path);
        }
    }

    /// <summary>
    /// Creates the directory. <paramref name="ownerOnly"/> is honoured only where the platform has
    /// the notion: Unix gets mode 700, so the scratch and credential directories are closed to
    /// everybody but their owner, while Windows has no per-directory mode to set and ignores it.
    /// </summary>
    public Task CreateDirectoryAsync(string path, bool ownerOnly, CancellationToken cancellationToken)
    {
        try
        {
            if (ownerOnly && !OperatingSystem.IsWindows())
                Directory.CreateDirectory(path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            else Directory.CreateDirectory(path);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            throw Named(error, path);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListFilesAsync(string directoryPath, string searchPattern,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(Directory.Exists(directoryPath)
            ? Directory.GetFiles(directoryPath, searchPattern, SearchOption.TopDirectoryOnly)
            : []);

    public Task<IReadOnlyList<string>> ListFilesRecursivelyAsync(string directoryPath, string searchPattern,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(Directory.Exists(directoryPath)
            ? Directory.GetFiles(directoryPath, searchPattern, SearchOption.AllDirectories)
            : []);

    public Task<IReadOnlyList<FileSystemEntry>> ListEntriesAsync(string directoryPath, bool recursive,
        CancellationToken cancellationToken) =>
        ListEntriesAsync(directoryPath, new FileEnumerationOptions(Recursive: recursive), cancellationToken);

    /// <summary>
    /// Walks the directory itself rather than asking the platform to recurse, because how the walk
    /// is allowed to behave has to be the caller's decision applied at every level: an entry the
    /// caller asked to skip, a link that must not be followed back up the tree, a directory that
    /// will not open. The platform's blanket enumeration would decide all three on the caller's
    /// behalf.
    /// </summary>
    public Task<IReadOnlyList<FileSystemEntry>> ListEntriesAsync(string directoryPath,
        FileEnumerationOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(directoryPath)) return Task.FromResult<IReadOnlyList<FileSystemEntry>>([]);
        var entries = new List<FileSystemEntry>();
        Collect(directoryPath, options, entries);
        return Task.FromResult<IReadOnlyList<FileSystemEntry>>(entries);
    }

    private static void Collect(string directory, FileEnumerationOptions options, List<FileSystemEntry> entries)
    {
        IEnumerator<FileSystemInfo> children;
        try
        {
            children = new DirectoryInfo(directory).EnumerateFileSystemInfos().GetEnumerator();
        }
        catch (Exception error) when (options.SkipInaccessible
                                     && error is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
        {
            return;
        }

        using (children)
        {
            while (true)
            {
                FileAttributes attributes;
                FileSystemInfo child;
                try
                {
                    if (!children.MoveNext()) break;
                    child = children.Current;
                    attributes = child.Attributes;
                }
                catch (Exception error) when (options.SkipInaccessible
                                             && error is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
                {
                    // One entry nobody may look at should not cost the caller every other entry.
                    break;
                }

                var isDirectory = (attributes & FileAttributes.Directory) == FileAttributes.Directory;
                var isLink = (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
                if (options.AttributesToSkip != 0 && (attributes & options.AttributesToSkip) != 0) continue;
                if (options.SkipReparsePoints && isLink) continue;
                // The pattern decides what is reported, never whether the walk descends. Testing it
                // before the recursion would make a walk with a file pattern stop at the first level:
                // a subdirectory is named "inner", not "inner\*.md", so filtering the descent by the
                // pattern hides everything below every directory that does not itself match.
                if (FileSystemName.MatchesSimpleExpression(options.SearchPattern, child.Name))
                    entries.Add(new FileSystemEntry(child.FullName, child.Name, isDirectory,
                        child is FileInfo file ? file.Length : 0, attributes));
                // A link is never followed: walking through one leads back to a directory already
                // visited, and a listing of a tree with a link to itself would never end.
                if (options.Recursive && isDirectory && !isLink) Collect(child.FullName, options, entries);
            }
        }
    }

    /// <summary>
    /// Completes a save by putting the source in the destination's place.
    /// </summary>
    /// <remarks>
    /// Replacing an existing document goes through <see cref="File.Replace(string, string, string?)"/>
    /// rather than a move. Windows refuses to rename over a file that anybody holds open — sharing
    /// the read does not help, the rename is denied all the same — and the save then failed with an
    /// access denial while nothing was wrong with the directory or its permissions. Replacing is the
    /// operation the platform provides for exactly this: it swaps the contents under readers that
    /// already have the file open, and keeps the destination's own attributes.
    /// </remarks>
    public Task MoveAsync(string sourcePath, string destinationPath, bool overwrite,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        try
        {
            if (overwrite && File.Exists(destinationPath))
                File.Replace(sourcePath, destinationPath, null, ignoreMetadataErrors: true);
            else File.Move(sourcePath, destinationPath, overwrite);
        }
        catch (FileNotFoundException) when (overwrite)
        {
            // The document was deleted between the test and the replace. There is nothing to replace
            // any more, and putting the new one in its place is what was being asked for regardless.
            Retry(() => File.Move(sourcePath, destinationPath, true), destinationPath);
        }
        catch (FileNotFoundException error) when (!overwrite)
        {
            // The contract names this type because a caller tells "there is nothing to move" apart
            // from "this failed"; an IOException with the same text loses that distinction.
            throw new FileNotFoundException($"'{sourcePath}' could not be moved: {error.Message}", error);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            throw Named(error, destinationPath);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Moves a directory. The destination must not exist: the platform's directory move fails
    /// rather than merging, and merging two trees would silently lose which entry came from where.
    /// </summary>
    public Task MoveDirectoryAsync(string sourcePath, string destinationPath,
        CancellationToken cancellationToken)
    {
        try
        {
            Directory.Move(sourcePath, destinationPath);
        }
        catch (DirectoryNotFoundException error)
        {
            throw new DirectoryNotFoundException($"'{sourcePath}' was not found to move.", error);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            throw Named(error, destinationPath);
        }

        return Task.CompletedTask;
    }

    public Task CopyAsync(string sourcePath, string destinationPath, bool overwrite,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        try
        {
            File.Copy(sourcePath, destinationPath, overwrite);
        }
        catch (FileNotFoundException error)
        {
            // The type is part of the contract — a caller tells "there is nothing there" from "this
            // failed" — and the name of the missing file has to be in the message either way.
            throw new FileNotFoundException($"'{sourcePath}' could not be copied: {error.Message}", error);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            throw Named(error, destinationPath);
        }

        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            File.Delete(path);
        }
        catch (DirectoryNotFoundException)
        {
            // A document in a missing directory is already absent.
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            throw Named(error, path);
        }

        return Task.CompletedTask;
    }

    public Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken cancellationToken)
    {
        try
        {
            Directory.Delete(path, recursive);
        }
        catch (DirectoryNotFoundException)
        {
            // A directory that is not there is already in the state the caller asked for.
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            // A non-recursive delete of a directory that still has contents lands here, with the
            // path in the message: the caller has to learn that it was not empty rather than lose
            // what was inside it.
            throw Named(error, path);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The canonical path of the entry, with every link in it followed to its final target.
    /// </summary>
    /// <remarks>
    /// This is a containment primitive, not a convenience: a caller asks it whether a file is inside
    /// a directory it may touch, and path algebra alone answers that question wrongly. A junction
    /// planted inside a granted directory points outside it, and canonicalizing the path as text
    /// leaves it looking like an ordinary child of the grant — the one way a containment check can be
    /// widened without anybody noticing. So the walk is real: each segment is resolved against the
    /// already-resolved parent, and where a segment is a link the link's own final target replaces it,
    /// which is also where the rest of the path continues from.
    /// Asking about a path that names nothing is normal: the walk stops at the first segment that is
    /// not there and appends what follows, because a file that has not been created yet still has to
    /// be judged by where it would land.
    /// </remarks>
    public Task<string> ResolveLinkTargetAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A path cannot be empty or whitespace.", nameof(path));
        return Task.FromResult(Guarded(() => Resolve(path), path));
    }

    private static string Resolve(string path)
    {
        var canonical = Path.GetFullPath(path);
        var root = Path.GetPathRoot(canonical) ?? string.Empty;
        var resolved = root;
        var segments = canonical[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            var candidate = Path.Combine(resolved, segments[index]);
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                // Nothing below an absent segment can be resolved either, so the rest of the path is
                // appended as it was asked for and the walk ends here.
                for (; index < segments.Length; index++) resolved = Path.Combine(resolved, segments[index]);
                break;
            }

            resolved = LinkTarget(candidate) ?? candidate;
        }

        return resolved;
    }

    /// <summary>
    /// Where a link points, or null when the entry is not one. Asked of the platform rather than
    /// inferred: only the platform knows a junction from a directory, and a chain of links is
    /// followed to its end because a caller wants the place the bytes actually live in.
    /// </summary>
    private static string? LinkTarget(string candidate)
    {
        var target = Directory.Exists(candidate)
            ? Directory.ResolveLinkTarget(candidate, returnFinalTarget: true)?.FullName
            : File.ResolveLinkTarget(candidate, returnFinalTarget: true)?.FullName;
        return target is null ? null : Path.GetFullPath(target);
    }

    private static void Retry(Action action, string path)
    {
        try
        {
            action();
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            throw Named(error, path);
        }
    }

    private static T Guarded<T>(Func<T> action, string path)
    {
        try
        {
            return action();
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            throw Named(error, path);
        }
    }

    private static string DirectoryOf(string path) => Path.GetDirectoryName(path)
        ?? throw new InvalidOperationException("Storage path must include a directory.");

    /// <summary>
    /// Puts the path back into the failure. "Access to the path is denied" names no path, so what
    /// reached the user was a stack frame and a verdict with nothing to act on — the one fact that
    /// turns it into a fixable problem, which file, is the fact the framework leaves out. The type is
    /// preserved because callers classify storage failures by it.
    /// </summary>
    private static Exception Named(Exception error, string path) => error switch
    {
        UnauthorizedAccessException => new UnauthorizedAccessException(
            $"Access to '{path}' is denied. Another program may be holding it open, or it is read-only, or the account "
            + "running this application may not write to that directory.", error),
        _ => new IOException($"'{path}' could not be written: {error.Message}", error),
    };
}
