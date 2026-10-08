namespace AI.Infrastructure.Storage;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AI.Application.Projects;
using AI.Application.Resources;
using AI.Contracts.FileSystem;
using AI.Contracts.Resources;
using Microsoft.AspNetCore.StaticFiles;

/// <summary>Stores uploaded file bytes outside chat JSON and retains their source on the resource.</summary>
public sealed class ResourceAssetService(
    IProjectStorageLocation location,
    IProjectService projects,
    IFileSystem files,
    IPath path,
    IAtomicFileWriter atomicWriter)
    : IResourceAssetService
{
    private const int MaximumBytes = 15 * 1024 * 1024;
    private const int MaximumTextBytes = 64 * 1024;
    private readonly ConcurrentDictionary<string, (Guid ProjectId, string AssetId, DateTimeOffset Expires)> _tickets = new();
    private readonly FileExtensionContentTypeProvider _contentTypes = new();

    /// <summary>
    /// The asset directory of one project. The contract's combining member takes two paths, so a
    /// deeper path is composed by nesting, which also keeps the containment checks that read these
    /// paths judged on the same canonical form they were built from.
    /// </summary>
    private string AssetDirectory(Guid projectId) =>
        path.Combine(path.Combine(location.RootDirectory, "assets"), projectId.ToString("N"));

    /// <summary>The undo blobs directory of one project, composed the same way.</summary>
    private string UndoBlobsDirectory(Guid projectId) =>
        path.Combine(path.Combine(AssetDirectory(projectId), "undo"), "blobs");

    public async Task<ChatResource> StoreAsync(Guid projectId, byte[] data, string name,
        ChatResourceSource source, string sourceLocation, CancellationToken cancellationToken)
    {
        if (await projects.GetAsync(projectId, cancellationToken) is null)
            throw new FileNotFoundException("Project not found.");
        if (data.Length is 0 or > MaximumBytes) throw new InvalidDataException("File size must be between 1 byte and 15 MB.");
        var safeName = path.GetFileName(name.Trim());
        if (safeName.Length > 200) safeName = safeName[..200];
        if (safeName.Length == 0) safeName = "File";
        var imageFormat = DetectFormat(data);
        var mediaType = imageFormat?.MediaType ??
            (_contentTypes.TryGetContentType(safeName, out var detected) ? detected : "application/octet-stream");
        var fingerprint = new byte[data.Length + Encoding.UTF8.GetByteCount(mediaType)];
        data.CopyTo(fingerprint, 0);
        Encoding.UTF8.GetBytes(mediaType, fingerprint.AsSpan(data.Length));
        var assetId = Convert.ToHexString(SHA256.HashData(fingerprint)).ToLowerInvariant();
        var directory = AssetDirectory(projectId);
        // Content addressed: an identical upload lands on the same name with the same bytes, so an
        // existing blob already holds what would be written and is left alone. The atomic writer
        // keeps a first save whole: a crash leaves the previous blob or the new one, never half.
        var destination = path.Combine(directory, assetId + ".bin");
        if (!await files.FileExistsAsync(destination, cancellationToken))
            await atomicWriter.WriteBytesAsync(destination, data, cancellationToken);
        var metadata = path.Combine(directory, assetId + ".json");
        if (!await files.FileExistsAsync(metadata, cancellationToken))
            await atomicWriter.WriteTextAsync(metadata, JsonSerializer.Serialize(mediaType), cancellationToken);
        return new ChatResource(Guid.CreateVersion7(), imageFormat is null ? ChatResourceKind.File : ChatResourceKind.Image,
            sourceLocation, safeName,
            Source: source, AssetId: assetId, MediaType: mediaType, Size: data.Length);
    }

    public async Task<ResourceAsset?> ReadAsync(Guid projectId, string assetId, CancellationToken cancellationToken)
    {
        if (assetId.Length != 64 || assetId.Any(character => !Uri.IsHexDigit(character))) return null;
        if (await projects.GetAsync(projectId, cancellationToken) is null) return null;
        var directory = AssetDirectory(projectId);
        var normalized = assetId.ToLowerInvariant();
        var blob = path.Combine(directory, normalized + ".bin");
        var metadata = path.Combine(directory, normalized + ".json");
        if (!await files.FileExistsAsync(blob, cancellationToken)
            || !await files.FileExistsAsync(metadata, cancellationToken)) return null;
        var mediaType = JsonSerializer.Deserialize<string>(await files.ReadTextAsync(metadata, cancellationToken) ?? "null");
        var bytes = await files.ReadBytesAsync(blob, cancellationToken);
        return mediaType is null || bytes is null ? null : new ResourceAsset(bytes, mediaType);
    }

    public async Task<ResourceAssetText?> ReadTextAsync(Guid projectId, string assetId, CancellationToken cancellationToken)
    {
        if (assetId.Length != 64 || assetId.Any(character => !Uri.IsHexDigit(character))
            || await projects.GetAsync(projectId, cancellationToken) is null) return null;
        var blob = path.Combine(AssetDirectory(projectId), assetId.ToLowerInvariant() + ".bin");
        await using var stream = await files.OpenReadAsync(blob, cancellationToken);
        if (stream is null) return null;
        var truncated = stream.Length > MaximumTextBytes;
        var data = new byte[Math.Min(stream.Length, MaximumTextBytes)];
        await stream.ReadExactlyAsync(data, cancellationToken);
        if (data.AsSpan().Contains((byte)0)) return null;
        var length = data.Length;
        var decoder = new UTF8Encoding(false, true);
        while (length > 0)
        {
            try
            {
                var text = decoder.GetString(data, 0, length).TrimStart('\uFEFF');
                return text.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t'))
                    ? null : new ResourceAssetText(text, truncated);
            }
            catch (DecoderFallbackException) when (truncated && length > MaximumTextBytes - 4)
            { length--; }
            catch (DecoderFallbackException) { return null; }
        }
        return null;
    }

    public async Task<ResourceTicket?> CreateTicketAsync(Guid projectId, string assetId, CancellationToken cancellationToken)
    {
        if (await ReadAsync(projectId, assetId, cancellationToken) is null) return null;
        foreach (var item in _tickets.Where(item => item.Value.Expires <= DateTimeOffset.UtcNow))
            _tickets.TryRemove(item.Key, out _);
        var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _tickets[ticket] = (projectId, assetId, DateTimeOffset.UtcNow.AddHours(4));
        return new ResourceTicket($"asset-content/{ticket}");
    }

    public Task<ResourceAsset?> ReadTicketAsync(string ticket, CancellationToken cancellationToken) =>
        _tickets.TryGetValue(ticket, out var entry) && entry.Expires > DateTimeOffset.UtcNow
            ? ReadAsync(entry.ProjectId, entry.AssetId, cancellationToken)
            : Task.FromResult<ResourceAsset?>(null);

    public async Task<string> StoreUndoBytesAsync(Guid projectId, byte[] data, CancellationToken cancellationToken)
    {
        if (await projects.GetAsync(projectId, cancellationToken) is null)
            throw new FileNotFoundException("Project not found.");
        if (data.Length > MaximumBytes) throw new InvalidDataException("Undo snapshot is too large.");
        var assetId = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var destination = path.Combine(UndoBlobsDirectory(projectId), assetId + ".bin");
        if (await files.FileExistsAsync(destination, cancellationToken)) return assetId;
        await atomicWriter.WriteBytesAsync(destination, data, cancellationToken);
        return assetId;
    }

    public async Task<byte[]?> ReadUndoBytesAsync(Guid projectId, string assetId, CancellationToken cancellationToken)
    {
        if (assetId.Length != 64 || assetId.Any(character => !Uri.IsHexDigit(character))
            || await projects.GetAsync(projectId, cancellationToken) is null) return null;
        var blob = path.Combine(UndoBlobsDirectory(projectId), assetId.ToLowerInvariant() + ".bin");
        var bytes = await files.ReadBytesAsync(blob, cancellationToken);
        return bytes is not null
            && Convert.ToHexString(SHA256.HashData(bytes)).Equals(assetId, StringComparison.OrdinalIgnoreCase)
            ? bytes : null;
    }

    public async Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var root = path.GetFullPath(path.Combine(location.RootDirectory, "assets"));
        var target = path.GetFullPath(path.Combine(root, projectId.ToString("N")));
        if (!path.IsInside(target, root, recursive: true) || string.Equals(target, root, path.Comparison))
            throw new InvalidOperationException("Invalid resource asset directory.");
        if (await files.DirectoryExistsAsync(target, cancellationToken))
            await files.DeleteDirectoryAsync(target, recursive: true, cancellationToken);
        foreach (var ticket in _tickets.Where(item => item.Value.ProjectId == projectId))
            _tickets.TryRemove(ticket.Key, out _);
    }

    private static (string MediaType, string Extension)? DetectFormat(byte[] data)
    {
        if (data.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ("image/png", ".png");
        if (data.AsSpan().StartsWith(new byte[] { 255, 216, 255 })) return ("image/jpeg", ".jpg");
        if (data.Length >= 12 && data.AsSpan().StartsWith("RIFF"u8) && data.AsSpan(8).StartsWith("WEBP"u8))
            return ("image/webp", ".webp");
        if (data.AsSpan().StartsWith("GIF87a"u8) || data.AsSpan().StartsWith("GIF89a"u8))
            return ("image/gif", ".gif");
        return null;
    }

}
