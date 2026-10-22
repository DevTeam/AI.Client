namespace AI.Application.Resources;

/// <summary>
/// The current user's binary content — uploaded files, images, workspace Undo snapshots — under
/// immutable keys (ADR-014). A key is a relative path of plain segments such as
/// <c>&lt;project&gt;/&lt;sha256&gt;.bin</c>; content under a key never changes once written, which is
/// what lets the same port stand on a directory or on an object store such as S3.
/// </summary>
public interface IBlobStore
{
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Stores <paramref name="data"/> under <paramref name="key"/> unless something is there already,
    /// which is then left alone: keys are content addressed, so it holds the same bytes. A write is
    /// whole or absent, never half.
    /// </summary>
    Task PutAsync(string key, byte[] data, CancellationToken cancellationToken);

    /// <summary>The content under <paramref name="key"/>, or <c>null</c> when there is none.</summary>
    Task<byte[]?> ReadAsync(string key, CancellationToken cancellationToken);

    /// <summary>A stream over the content, or <c>null</c> when there is none; for reading a prefix of it.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken);

    /// <summary>Removes every key that starts with the <paramref name="prefix"/> segment path.</summary>
    Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken);
}
