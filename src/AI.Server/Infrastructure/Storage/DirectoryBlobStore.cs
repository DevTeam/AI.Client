namespace AI.Infrastructure.Storage;

using AI.Application.Resources;
using AI.Contracts.FileSystem;

/// <summary>
/// Blobs as files under the current user's <c>assets</c> directory: a key's segments are the path below
/// it, so the layout is the one uploads always had.
/// </summary>
public sealed class DirectoryBlobStore(IUserStorageLocation location, IFileSystem files, IPath path,
    IAtomicFileWriter atomicWriter) : IBlobStore
{
    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken) =>
        files.FileExistsAsync(PathOf(key), cancellationToken);

    public async Task PutAsync(string key, byte[] data, CancellationToken cancellationToken)
    {
        var target = PathOf(key);
        // Content addressed: what is there already holds these bytes. The atomic writer keeps a first
        // save whole: a crash leaves no blob or the whole one, never half.
        if (!await files.FileExistsAsync(target, cancellationToken))
            await atomicWriter.WriteBytesAsync(target, data, cancellationToken);
    }

    public Task<byte[]?> ReadAsync(string key, CancellationToken cancellationToken) =>
        files.ReadBytesAsync(PathOf(key), cancellationToken);

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken) =>
        files.OpenReadAsync(PathOf(key), cancellationToken);

    public async Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        var target = PathOf(prefix);
        if (await files.DirectoryExistsAsync(target, cancellationToken))
            await files.DeleteDirectoryAsync(target, recursive: true, cancellationToken);
    }

    /// <summary>
    /// The file a key names. Only plain segments are accepted, so no key reaches outside the user's
    /// assets, whatever a caller passes. The contract's combining member takes two paths, so the path is
    /// composed one segment at a time.
    /// </summary>
    private string PathOf(string key)
    {
        var segments = key.Split('/');
        if (segments.Length == 0 || segments.Any(segment => segment.Length == 0 || segment is "." or ".."
                || segment.IndexOfAny(['\\', ':']) >= 0 || segment.Any(char.IsControl)))
            throw new ArgumentException($"'{key}' is not a blob key.", nameof(key));
        var result = path.Combine(location.RootDirectory, "assets");
        foreach (var segment in segments) result = path.Combine(result, segment);
        return result;
    }
}
