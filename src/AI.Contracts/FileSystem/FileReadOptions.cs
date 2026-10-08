namespace AI.Contracts.FileSystem;

/// <summary>
/// How a read opens the document. Answers the two things callers genuinely vary today: how much
/// the platform may share the file while it is open, and how it should buffer the sequential scan.
/// </summary>
/// <param name="Share">
/// What other openers may do with the document meanwhile. The default shares read, write and
/// delete, because a save of the same document finishes by replacing it: without share-delete,
/// Windows refuses that replacement and somebody else's save fails because this reader exists.
/// A narrower share is for callers that deliberately want to be left alone, such as a
/// single-instance lock — see the ADR's documented exceptions.
/// </param>
/// <param name="BufferSize">
/// The buffer to read through, in bytes. Zero asks for the platform's own default, which is right
/// for a document that is read once; a large scan raises it instead of paying many small reads.
/// </param>
/// <param name="Options">
/// Platform hints for the access pattern, such as a sequential scan over a file that will not be
/// revisited. <see cref="FileOptions.None"/> leaves the choice to the platform.
/// </param>
public sealed record FileReadOptions(
    FileShare Share = FileShare.ReadWrite | FileShare.Delete,
    int BufferSize = 0,
    FileOptions Options = FileOptions.None)
{
    /// <summary>Today's behavior, and what a read without options does.</summary>
    public static FileReadOptions Default { get; } = new();
}
