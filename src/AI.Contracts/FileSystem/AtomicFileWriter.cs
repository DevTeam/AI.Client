namespace AI.Contracts.FileSystem;

/// <summary>
/// <see cref="IAtomicFileWriter"/> over any <see cref="IFileSystem"/>: the content lands in a
/// temporary sibling of the target, and the move that follows puts it in the target's place with
/// the replace semantics that let a reader hold the document open meanwhile.
/// </summary>
/// <remarks>
/// Written against the contract rather than the disk on purpose, so the atomicity holds for the
/// fake too and a test can watch the two steps instead of taking the real platform's word for it.
/// Naming the temporary after the target (<c>chat.json.tmp</c>) keeps it beside the target: a
/// rename across devices is not a rename, and a crash then leaves a file a human can recognize.
/// </remarks>
public sealed class AtomicFileWriter(IFileSystem files) : IAtomicFileWriter
{
    public Task WriteTextAsync(string path, string content, CancellationToken cancellationToken) =>
        SaveAsync(path, token => files.WriteTextAsync(Temporary(path), content, token), cancellationToken);

    public Task WriteBytesAsync(string path, byte[] content, CancellationToken cancellationToken) =>
        SaveAsync(path, token => files.WriteBytesAsync(Temporary(path), content, token), cancellationToken);

    private static string Temporary(string path) => path + ".tmp";

    private async Task SaveAsync(string path, Func<CancellationToken, Task> write, CancellationToken cancellationToken)
    {
        await write(cancellationToken);
        await files.MoveAsync(Temporary(path), path, true, cancellationToken);
    }
}
