namespace AI.Contracts.FileSystem;

/// <summary>
/// Saves a document in one step as far as anybody watching can tell: the content goes to a
/// temporary sibling first and is then put in the target's place, so a crash or a power loss leaves
/// either the previous document or the new one, never half of either.
/// </summary>
/// <remarks>
/// Readers that already hold the target open must not be disturbed and must not block the save;
/// that is why the step that puts the temporary file in place carries the platform replace
/// semantics described on <see cref="IFileSystem.MoveAsync"/>.
/// </remarks>
public interface IAtomicFileWriter
{
    /// <summary>Writes UTF-8 text without a BOM, creating the parent directory when it is missing.</summary>
    Task WriteTextAsync(string path, string content, CancellationToken cancellationToken);

    /// <summary>Writes raw bytes, creating the parent directory when it is missing.</summary>
    Task WriteBytesAsync(string path, byte[] content, CancellationToken cancellationToken);
}
