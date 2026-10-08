// ReSharper disable UseCollectionExpression
namespace AI.Infrastructure.Tests.Storage;

using System.Collections.Concurrent;
using System.IO.Enumeration;
using System.Text;
using AI.Contracts.FileSystem;
using FileSystemEntry = AI.Contracts.FileSystem.FileSystemEntry;

/// <summary>
/// <see cref="IFileSystem"/> in memory, for tests that exercise logic rather than the platform.
/// </summary>
/// <remarks>
/// Content is kept under the exact path strings it is given, because tests read and write
/// <see cref="Files"/> directly to set up a document or to look at what a consumer saved: a fake
/// that canonicalized paths would answer a caller's own path with a key it never used.
/// The fake models directories, so a listing, a non-recursive delete and an existence check agree
/// with each other; it does not model links or a second process, and the ADR records what a test
/// therefore has to exercise against the real platform instead.
/// </remarks>
internal sealed class MemoryFileSystem : IFileSystem
{
    private readonly ConcurrentDictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _times = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _directoryPool = new();
    private long _ticks;

    public MemoryFileSystem() : this(new InMemoryPath(PathSemantics.Windows, "C:\\data"))
    {
    }

    public MemoryFileSystem(IPath path)
    {
        Path = path;
        var comparer = path.IsCaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        Files = new ConcurrentDictionary<string, string>(comparer);
    }

    /// <summary>The path algebra this fake was built with, exposed so a consumer's paths agree with it.</summary>
    public IPath Path { get; }

    /// <summary>Documents by path. Read and written directly by tests to preload or inspect content.</summary>
    public ConcurrentDictionary<string, string> Files { get; }

    /// <summary>Every path a read was asked for, in order, so a test can tell a cache from a re-read.</summary>
    public ConcurrentQueue<string> ReadPaths { get; } = new();

    /// <summary>Makes a write whose path ends this way fail, standing in for a full or read-only disk.</summary>
    public string? FailWriteSuffix { get; set; }

    /// <summary>
    /// Directories that were created or written into. A directory is also known by the files inside
    /// it, so a test that preloads <see cref="Files"/> does not have to register anything.
    /// </summary>
    public IReadOnlyCollection<string> Directories => KnownDirectories().ToArray();

    public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(!DirectoryExists(path) && Files.ContainsKey(path));

    public Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(DirectoryExists(path));

    public Task<FileSystemEntry?> GetEntryAsync(string path, CancellationToken cancellationToken)
    {
        if (DirectoryExists(path))
            return Task.FromResult<FileSystemEntry?>(
                new FileSystemEntry(path, Path.GetFileName(path), true, 0, FileAttributes.Directory));
        if (Files.TryGetValue(path, out var content))
            return Task.FromResult<FileSystemEntry?>(
                new FileSystemEntry(path, Path.GetFileName(path), false, Encoding.UTF8.GetByteCount(content)));
        return Task.FromResult<FileSystemEntry?>(null);
    }

    public Task<FileAttributes> GetAttributesAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(DirectoryExists(path) ? FileAttributes.Directory : Existing(path) ? FileAttributes.Normal
            : throw new FileNotFoundException($"'{path}' was not found.", path));

    public Task<DateTimeOffset> GetLastWriteTimeAsync(string path, CancellationToken cancellationToken)
    {
        if (!Existing(path)) throw new FileNotFoundException($"'{path}' was not found.", path);
        // Deterministic and ordered rather than read from a clock: a test asserts which write is
        // newer, never what time it is.
        return Task.FromResult(_times.TryGetValue(path, out var written) ? written : DateTimeOffset.UnixEpoch);
    }

    public Task<string?> ReadTextAsync(string path, CancellationToken cancellationToken) =>
        ReadTextAsync(path, FileReadOptions.Default, cancellationToken);

    public Task<string?> ReadTextAsync(string path, FileReadOptions options, CancellationToken cancellationToken)
    {
        ReadPaths.Enqueue(path);
        return Task.FromResult(DirectoryExists(path) ? null : Files.GetValueOrDefault(path));
    }

    public Task<byte[]?> ReadBytesAsync(string path, CancellationToken cancellationToken) =>
        ReadBytesAsync(path, FileReadOptions.Default, cancellationToken);

    public Task<byte[]?> ReadBytesAsync(string path, FileReadOptions options, CancellationToken cancellationToken)
    {
        ReadPaths.Enqueue(path);
        if (DirectoryExists(path)) return Task.FromResult<byte[]?>(null);
        if (_blobs.TryGetValue(path, out var bytes)) return Task.FromResult<byte[]?>(bytes.ToArray());
        return Task.FromResult<byte[]?>(Files.TryGetValue(path, out var text) ? Encoding.UTF8.GetBytes(text) : null);
    }

    public IAsyncEnumerable<string> ReadLinesAsync(string path, CancellationToken cancellationToken) =>
        ReadLinesAsync(path, FileReadOptions.Default, cancellationToken);

    /// <summary>
    /// Splits on <c>\n</c> and drops a trailing <c>\r</c>, so a document written with either line
    /// ending reads the same here as it does through the platform adapter. A newline at the very end
    /// terminates the last line rather than opening an empty one, which is what a line reader does
    /// and what a caller counting lines expects: "a\nb\n" is two lines, not three.
    /// </summary>
    public async IAsyncEnumerable<string> ReadLinesAsync(string path, FileReadOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var text = await ReadTextAsync(path, options, cancellationToken);
        if (text is null) yield break;
        var parts = text.Split('\n');
        var count = parts.Length > 0 && parts[^1].Length == 0 ? parts.Length - 1 : parts.Length;
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = parts[index];
            yield return line.EndsWith('\r') ? line[..^1] : line;
        }
    }

    public Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken) =>
        OpenReadAsync(path, FileReadOptions.Default, cancellationToken);

    public async Task<Stream?> OpenReadAsync(string path, FileReadOptions options, CancellationToken cancellationToken)
    {
        var bytes = await ReadBytesAsync(path, options, cancellationToken);
        return bytes is null ? null : new MemoryStream(bytes);
    }

    public async Task WriteTextAsync(string path, string content, CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        if (FailWriteSuffix is { } suffix && path.EndsWith(suffix, StringComparison.Ordinal))
            throw new IOException("Simulated storage failure.");
        Adopt(path);
        Files[path] = content;
    }

    public async Task WriteBytesAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        if (FailWriteSuffix is { } suffix && path.EndsWith(suffix, StringComparison.Ordinal))
            throw new IOException("Simulated storage failure.");
        Adopt(path);
        _blobs[path] = content.ToArray();
    }

    public Task AppendTextAsync(string path, string content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Adopt(path);
        Files.AddOrUpdate(path, content, (_, existing) => existing + content);
        return Task.CompletedTask;
    }

    /// <summary>
    /// A stream the caller writes through. What it wrote lands in the document when the stream is
    /// disposed, which is what makes this member usable at all in a fake.
    /// </summary>
    public Task<Stream> OpenWriteAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Adopt(path);
        return Task.FromResult<Stream>(new PendingWriteStream(bytes =>
        {
            if (FailWriteSuffix is { } suffix && path.EndsWith(suffix, StringComparison.Ordinal))
                throw new IOException("Simulated storage failure.");
            _blobs[path] = bytes;
            Files[path] = Encoding.UTF8.GetString(bytes);
        }));
    }

    public Task CreateDirectoryAsync(string path, bool ownerOnly, CancellationToken cancellationToken)
    {
        RegisterDirectory(path);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListFilesAsync(string directoryPath, string searchPattern,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(DirectoryExists(directoryPath)
            ? Files.Keys.Where(file => Equals(Path.GetDirectoryName(file), directoryPath)
                && FileSystemName.MatchesSimpleExpression(searchPattern, Path.GetFileName(file))).ToArray()
            : []);

    public Task<IReadOnlyList<string>> ListFilesRecursivelyAsync(string directoryPath, string searchPattern,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(Files.Keys.Where(file =>
            IsBelow(file, directoryPath)
            && FileSystemName.MatchesSimpleExpression(searchPattern, Path.GetFileName(file))).ToArray());

    public Task<IReadOnlyList<FileSystemEntry>> ListEntriesAsync(string directoryPath, bool recursive,
        CancellationToken cancellationToken) =>
        ListEntriesAsync(directoryPath, new FileEnumerationOptions(Recursive: recursive), cancellationToken);

    /// <summary>
    /// What is inside the directory. A link cannot be modelled here — the fake has no separate entry
    /// kind — so nothing is ever followed and nothing is ever reported as a link, which is exactly
    /// what the contract asks a listing to do with one.
    /// </summary>
    public Task<IReadOnlyList<FileSystemEntry>> ListEntriesAsync(string directoryPath,
        FileEnumerationOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!DirectoryExists(directoryPath)) return Task.FromResult<IReadOnlyList<FileSystemEntry>>([]);
        var entries = new List<FileSystemEntry>();
        Collect(directoryPath, options, entries);
        return Task.FromResult<IReadOnlyList<FileSystemEntry>>(entries);
    }

    private void Collect(string directory, FileEnumerationOptions options, List<FileSystemEntry> entries)
    {
        foreach (var child in KnownDirectories()
                     .Where(candidate => Equals(Path.GetDirectoryName(candidate), directory)))
        {
            if ((options.AttributesToSkip & FileAttributes.Directory) != 0) continue;
            // The pattern is asked about what is reported, not about what is walked: a directory is
            // descended into whether or not its own name matches, because otherwise a walk with a file
            // pattern would find nothing below the first level.
            if (FileSystemName.MatchesSimpleExpression(options.SearchPattern, Path.GetFileName(child)))
                entries.Add(new FileSystemEntry(child, Path.GetFileName(child), true, 0, FileAttributes.Directory));
            if (options.Recursive) Collect(child, options, entries);
        }

        foreach (var file in Files.Keys.Where(candidate => Equals(Path.GetDirectoryName(candidate), directory)))
        {
            if ((options.AttributesToSkip & FileAttributes.Normal) != 0) continue;
            if (!FileSystemName.MatchesSimpleExpression(options.SearchPattern, Path.GetFileName(file))) continue;
            entries.Add(new FileSystemEntry(file, Path.GetFileName(file), false,
                Encoding.UTF8.GetByteCount(Files[file])));
        }
    }

    public Task MoveAsync(string sourcePath, string destinationPath, bool overwrite,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (DirectoryExists(sourcePath)) throw new IOException($"'{sourcePath}' is a directory.");
        // The destination is judged before the source is touched: a refused move has to leave what
        // the caller had exactly where it was — losing the source to a failed rename would be a
        // worse outcome than the failure itself.
        if (!overwrite && (Files.ContainsKey(destinationPath) || DirectoryExists(destinationPath)))
            throw new IOException("Destination exists.");
        if (!Files.TryRemove(sourcePath, out var value)) throw new FileNotFoundException(sourcePath, sourcePath);
        _blobs.TryRemove(sourcePath, out var blob);
        Adopt(destinationPath);
        Files[destinationPath] = value;
        if (blob is not null) _blobs[destinationPath] = blob;
        return Task.CompletedTask;
    }

    /// <summary>A directory move takes its contents with it, and refuses a destination that exists.</summary>
    public Task MoveDirectoryAsync(string sourcePath, string destinationPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!DirectoryExists(sourcePath)) throw new DirectoryNotFoundException($"'{sourcePath}' was not found.");
        if (DirectoryExists(destinationPath)) throw new IOException("Destination exists.");
        Relocate(sourcePath, destinationPath);
        return Task.CompletedTask;
    }

    public Task CopyAsync(string sourcePath, string destinationPath, bool overwrite,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (DirectoryExists(sourcePath)) throw new IOException($"'{sourcePath}' is a directory.");
        if (!Files.TryGetValue(sourcePath, out var value)) throw new FileNotFoundException(sourcePath, sourcePath);
        if (!overwrite && Files.ContainsKey(destinationPath)) throw new IOException("Destination exists.");
        Adopt(destinationPath);
        Files[destinationPath] = value;
        if (_blobs.TryGetValue(sourcePath, out var blob)) _blobs[destinationPath] = blob;
        _times[destinationPath] = NextWrite();
        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A directory is not a file, and saying so is the point: a caller that deletes the wrong
        // kind of entry has to hear about it rather than lose a tree it meant to keep.
        if (DirectoryExists(path)) throw new IOException($"'{path}' is a directory, not a file.");
        Files.TryRemove(path, out _);
        _blobs.TryRemove(path, out _);
        _times.TryRemove(path, out _);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes the directory. With <paramref name="recursive"/> off, a directory that still has
    /// contents is refused rather than emptied: a caller that meant to remove one empty directory
    /// has to find out it was not empty.
    /// </summary>
    public Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!recursive && (Files.Keys.Any(file => IsBelow(file, path))
                           || KnownDirectories().Any(directory => IsBelow(directory, path))))
            throw new IOException($"'{path}' is not empty.");
        foreach (var file in Files.Keys.Where(file => IsBelow(file, path)).ToArray())
        {
            Files.TryRemove(file, out _);
            _blobs.TryRemove(file, out _);
            _times.TryRemove(file, out _);
        }

        foreach (var directory in KnownDirectories().Where(directory => IsBelow(directory, path)).ToArray())
            UnregisterDirectory(directory);
        UnregisterDirectory(path);
        return Task.CompletedTask;
    }

    /// <summary>
    /// The canonical path of an entry: normalized, with <c>.</c> and <c>..</c> folded in and this
    /// fake's own root semantics applied, so a containment test written against the fake asserts the
    /// same shape of path the platform adapter produces.
    /// </summary>
    /// <remarks>
    /// What this cannot model is said plainly: a real link, junction or symlink of the host system.
    /// The fake has no separate entry kind for one, so a path the platform would resolve somewhere
    /// else is canonicalized here as the ordinary path it looks like. A test that cares about a link
    /// being followed — a containment check that must survive a junction planted inside a granted
    /// directory — belongs against <c>SystemFileSystem</c> and is tagged <c>Category=Integration</c>.
    /// An entry that does not exist is still resolved, because a caller uses this to judge where a
    /// file it has not created yet would land.
    /// </remarks>
    public Task<string> ResolveLinkTargetAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Path.GetFullPath(path));
    }

    private bool Existing(string path) => Files.ContainsKey(path) || DirectoryExists(path);

    private bool DirectoryExists(string path) => KnownDirectories().Contains(path, PathComparer);

    private StringComparer PathComparer => Path.IsCaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Every directory the fake knows: the ones created or written into, plus the ones implied by
    /// the paths of the documents in it. The second half is what makes a test that preloads a file
    /// through <see cref="Files"/> see its directory as existing too.
    /// </summary>
    private HashSet<string> KnownDirectories()
    {
        var known = new HashSet<string>(_directoryPool, PathComparer);
        foreach (var file in Files.Keys)
        {
            for (var parent = Path.GetDirectoryName(file); parent is not null; parent = Path.GetDirectoryName(parent))
                if (!known.Add(parent)) break;
        }

        return known;
    }

    private bool IsBelow(string path, string directory) =>
        path.StartsWith(directory, Path.IsCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)
        && path.Length > directory.Length
        && path[directory.Length] == Path.DirectorySeparator;

    /// <summary>Records the document's directory and every directory above it, as a write would create them.</summary>
    private void Adopt(string path)
    {
        RegisterDirectory(Path.GetDirectoryName(path));
        _times[path] = NextWrite();
    }

    private void RegisterDirectory(string? path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if (!_directoryPool.Contains(current, PathComparer)) _directoryPool.Enqueue(current);
    }

    private void UnregisterDirectory(string path)
    {
        var kept = _directoryPool.Where(directory => !PathComparer.Equals(directory, path)).ToArray();
        while (_directoryPool.TryDequeue(out _)) { }
        foreach (var directory in kept) _directoryPool.Enqueue(directory);
    }

    /// <summary>
    /// Moves everything the source contains under the destination, keeping the shape of the tree.
    /// </summary>
    private void Relocate(string sourcePath, string destinationPath)
    {
        foreach (var file in Files.Keys.Where(file => IsBelow(file, sourcePath)).ToArray())
        {
            var moved = destinationPath + file[sourcePath.Length..];
            Files.TryRemove(file, out var content);
            Files[moved] = content!;
            if (_blobs.TryRemove(file, out var blob)) _blobs[moved] = blob;
        }

        foreach (var directory in KnownDirectories().Where(directory => IsBelow(directory, sourcePath)).ToArray())
        {
            UnregisterDirectory(directory);
            RegisterDirectory(destinationPath + directory[sourcePath.Length..]);
        }

        UnregisterDirectory(sourcePath);
        RegisterDirectory(destinationPath);
    }

    private DateTimeOffset NextWrite() => DateTimeOffset.UnixEpoch.AddTicks(Interlocked.Increment(ref _ticks));

    /// <summary>A stream whose content reaches the fake when the writer is done with it.</summary>
    private sealed class PendingWriteStream(Action<byte[]> commit) : MemoryStream
    {
        private bool _committed;

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_committed)
            {
                _committed = true;
                commit(ToArray());
            }

            base.Dispose(disposing);
        }
    }
}
