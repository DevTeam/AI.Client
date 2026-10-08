namespace AI.Contracts.FileSystem;

/// <summary>
/// Everything the application does to files and directories, as an interface, so that the platform
/// is one implementation of it and tests get another. No member here is allowed to assume a disk:
/// the contract is written so that an in-memory implementation can answer every call.
/// </summary>
/// <remarks>
/// Conventions that hold for every member:
/// <list type="bullet">
/// <item>Text is UTF-8 without a BOM.</item>
/// <item>A read of a path that is not there answers with absence — null, or false, or an empty list —
/// rather than throwing; that is what the storage repositories already rely on.</item>
/// <item>Writers create the parent directory when it is missing.</item>
/// <item>A mutation that names something absent either does nothing, where nothing is what was
/// asked for, or throws where the caller's mistake has to be told apart from the request. The
/// per-member remarks say which.</item>
/// <item>Where a call takes options, the call without them means exactly what it meant before the
/// options existed. An overload that adds behavior never changes the default one.</item>
/// </list>
/// Implementations differ in what they can promise beyond this: the platform adapter follows links
/// and reports the platform's own errors, while a fake cannot model a second process or a real
/// link. Callers that care about such a difference must state the expectation in the contract tests,
/// which is where it is decided once instead of in every consumer. What a fake deliberately cannot
/// model is listed as a documented exception in the ADR rather than being papered over here.
/// </remarks>
public interface IFileSystem
{
    // existence and metadata

    /// <summary>Whether a file is there. False when the path names a directory, or nothing.</summary>
    Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken);

    /// <summary>Whether a directory is there. False when the path names a file, or nothing.</summary>
    Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// What is at the path, or null when nothing is. A link is reported under the path given, with
    /// the metadata of what it points at.
    /// </summary>
    Task<FileSystemEntry?> GetEntryAsync(string path, CancellationToken cancellationToken);

    /// <summary>The attributes of the entry at the path.</summary>
    Task<FileAttributes> GetAttributesAsync(string path, CancellationToken cancellationToken);

    /// <summary>When the entry at the path was last written.</summary>
    Task<DateTimeOffset> GetLastWriteTimeAsync(string path, CancellationToken cancellationToken);

    // reading

    /// <summary>
    /// The whole document as text, or null when it is not there. Reading a document that is being
    /// replaced succeeds with one version or the other and never fails for being in flight; a
    /// failure that outlasts the retries is reported with the path in it.
    /// </summary>
    /// <remarks>
    /// Opens with <see cref="FileReadOptions.Default"/>: the sharing includes delete, so this read
    /// cannot cause somebody else's save to fail.
    /// </remarks>
    Task<string?> ReadTextAsync(string path, CancellationToken cancellationToken);

    /// <summary>As <see cref="ReadTextAsync(string, CancellationToken)"/>, opened as the options ask.</summary>
    Task<string?> ReadTextAsync(string path, FileReadOptions options, CancellationToken cancellationToken);

    /// <summary>The whole document as bytes, or null when it is not there.</summary>
    Task<byte[]?> ReadBytesAsync(string path, CancellationToken cancellationToken);

    /// <summary>As <see cref="ReadBytesAsync(string, CancellationToken)"/>, opened as the options ask.</summary>
    Task<byte[]?> ReadBytesAsync(string path, FileReadOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// The document's lines, one by one, without buffering the whole file. Splits on <c>\n</c> and
    /// drops a trailing <c>\r</c>, so a document written with either line ending yields the same
    /// lines. An absent file yields nothing rather than throwing.
    /// </summary>
    IAsyncEnumerable<string> ReadLinesAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// As <see cref="ReadLinesAsync(string, CancellationToken)"/>, opened as the options ask. A
    /// caller walking a large file for a pattern raises <see cref="FileReadOptions.BufferSize"/>
    /// and asks for a sequential scan rather than re-reading it in small pieces.
    /// </summary>
    IAsyncEnumerable<string> ReadLinesAsync(string path, FileReadOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// An open stream over the document, or null when it is not there. The caller owns the stream
    /// and disposes it; the implementation must not close anything it was given.
    /// </summary>
    /// <remarks>
    /// A document being replaced during the open is retried, because the replacement makes it
    /// briefly unopenable; once the stream is open the caller sees one version or the other.
    /// </remarks>
    Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken);

    /// <summary>As <see cref="OpenReadAsync(string, CancellationToken)"/>, opened as the options ask.</summary>
    Task<Stream?> OpenReadAsync(string path, FileReadOptions options, CancellationToken cancellationToken);

    // writing

    /// <summary>
    /// Writes the document whole, creating the parent directory when it is missing. This overwrites
    /// in place and is not atomic — a reader may see a partial document. Use
    /// <see cref="IAtomicFileWriter"/> when that matters.
    /// </summary>
    Task WriteTextAsync(string path, string content, CancellationToken cancellationToken);

    /// <summary>Writes the bytes whole, creating the parent directory when it is missing.</summary>
    Task WriteBytesAsync(string path, byte[] content, CancellationToken cancellationToken);

    /// <summary>
    /// Adds the text to the end of the document, creating the document and its directory when they
    /// are missing.
    /// </summary>
    Task AppendTextAsync(string path, string content, CancellationToken cancellationToken);

    /// <summary>
    /// A stream to write the document through, creating the parent directory when it is missing.
    /// The caller owns the stream and disposes it.
    /// </summary>
    /// <remarks>
    /// This is not the way to take an exclusive lock on a file for the lifetime of a process:
    /// sharing is fixed here so that a reader is not shut out. Single-instance protection keeps its
    /// own platform call — see the ADR's documented exceptions.
    /// </remarks>
    Task<Stream> OpenWriteAsync(string path, CancellationToken cancellationToken);

    // directories

    /// <summary>
    /// Creates the directory and any parent it needs. An existing directory is not an error: callers
    /// ask for it to be there, not for it to be new.
    /// </summary>
    /// <param name="ownerOnly">
    /// Asks for a directory only its owner may enter, which Unix grants with mode 700. Platforms
    /// without that notion ignore the flag, so a caller must not depend on the restriction for
    /// security on Windows.
    /// </param>
    Task CreateDirectoryAsync(string path, bool ownerOnly, CancellationToken cancellationToken);

    /// <summary>
    /// The files directly inside the directory that match the pattern. Directories are never
    /// returned, whatever their names look like, and an absent directory yields an empty list
    /// rather than an error.
    /// </summary>
    Task<IReadOnlyList<string>> ListFilesAsync(string directoryPath, string searchPattern, CancellationToken cancellationToken);

    /// <summary>
    /// Every file below the directory, at any depth, matching the pattern. The directory does not
    /// have to exist.
    /// </summary>
    Task<IReadOnlyList<string>> ListFilesRecursivelyAsync(string directoryPath, string searchPattern, CancellationToken cancellationToken);

    /// <summary>
    /// What is inside the directory, either its immediate children or everything below it. Each
    /// entry carries the last segment of its path as <see cref="FileSystemEntry.Name"/> and reports
    /// no length for a directory. A link is listed as an entry and is not followed, so a link that
    /// points back up the tree cannot turn this into an endless walk.
    /// </summary>
    Task<IReadOnlyList<FileSystemEntry>> ListEntriesAsync(string directoryPath, bool recursive, CancellationToken cancellationToken);

    /// <summary>
    /// As <see cref="ListEntriesAsync(string, bool, CancellationToken)"/>, with the walk decided by
    /// the options. Use this when entries have to be filtered or skipped: a walk that loses the
    /// caller's skip rules quietly returns entries the caller is not allowed to touch, and a walk
    /// that cannot skip an unreadable directory fails over one entry out of thousands.
    /// </summary>
    Task<IReadOnlyList<FileSystemEntry>> ListEntriesAsync(string directoryPath, FileEnumerationOptions options, CancellationToken cancellationToken);

    // mutation

    /// <summary>
    /// Moves or renames the file, creating the destination's parent directory when it is missing.
    /// Moving a directory is <see cref="MoveDirectoryAsync"/>; the two are kept apart so that neither
    /// has to guess what the source is.
    /// </summary>
    /// <param name="overwrite">
    /// True replaces an existing destination, which on Windows must succeed even while a reader
    /// holds the destination open — the platform operation for this has to be used instead of a
    /// plain rename. False leaves an existing destination alone and throws instead.
    /// </param>
    /// <exception cref="FileNotFoundException">There is no source to move.</exception>
    /// <exception cref="IOException">The destination exists and <paramref name="overwrite"/> is false.</exception>
    Task MoveAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken);

    /// <summary>
    /// Moves a directory, with everything inside it, to a destination that must not already exist:
    /// the platform provides no replacing directory move, and a move that silently merged two
    /// directories would lose which entry came from where. This is also a rename.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">There is no source directory to move.</exception>
    /// <exception cref="IOException">The destination already exists, or lies inside the source.</exception>
    Task MoveDirectoryAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken);

    /// <summary>Copies the file, creating the destination's parent directory when it is missing.</summary>
    /// <exception cref="FileNotFoundException">There is no source to copy.</exception>
    /// <exception cref="IOException">The destination exists and <paramref name="overwrite"/> is false.</exception>
    Task CopyAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the file at the path. A file that is not there, or one whose directory is not there,
    /// is already in the state the caller asked for, so this completes without error.
    /// </summary>
    Task DeleteFileAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the directory. An absent directory is already gone and is not an error.
    /// </summary>
    /// <param name="recursive">
    /// False refuses to delete a directory that still has contents; true deletes them with it. The
    /// two are told apart because a caller that means to remove one empty directory must find out
    /// that it was not empty rather than lose what was inside it.
    /// </param>
    /// <exception cref="IOException">The directory is not empty and <paramref name="recursive"/> is false.</exception>
    Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken cancellationToken);

    /// <summary>
    /// The canonical, fully resolved path of the entry at the given path, with every link followed.
    /// A path that names nothing is resolved all the same, to where the entry would be: callers use
    /// this to decide containment, and an absent file inside a directory must still be judged to be
    /// inside it.
    /// </summary>
    /// <remarks>
    /// Containment checks in the product are synchronous, and the answer here is computed without
    /// waiting for anything, so a completed task's result may be read directly. That is a decision
    /// recorded in the ADR, not an invitation to block on asynchronous work elsewhere.
    /// </remarks>
    Task<string> ResolveLinkTargetAsync(string path, CancellationToken cancellationToken);
}
