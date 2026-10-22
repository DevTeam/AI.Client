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

/// <summary>
/// Stores uploaded file bytes outside chat JSON, in the blob store, and retains their source on the
/// resource. Keys are <c>&lt;project&gt;/&lt;asset&gt;.bin</c> with its media type beside it in
/// <c>.json</c>, and <c>&lt;project&gt;/undo/blobs/&lt;sha256&gt;.bin</c> for workspace Undo.
/// </summary>
public sealed class ResourceAssetService(
    IBlobStore blobs,
    IProjectService projects,
    IPath path)
    : IResourceAssetService
{
    private const int MaximumBytes = 15 * 1024 * 1024;
    private const int MaximumTextBytes = 64 * 1024;
    private readonly ConcurrentDictionary<string, (Guid ProjectId, string AssetId, DateTimeOffset Expires)> _tickets = new();
    private readonly FileExtensionContentTypeProvider _contentTypes = new();

    private static string ProjectKey(Guid projectId) => projectId.ToString("N");

    private static string AssetKey(Guid projectId, string assetId, string extension) =>
        $"{ProjectKey(projectId)}/{assetId}{extension}";

    private static string UndoKey(Guid projectId, string assetId) => $"{ProjectKey(projectId)}/undo/blobs/{assetId}.bin";

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
        // Content addressed: an identical upload lands on the same key with the same bytes, which
        // the store then leaves alone.
        await blobs.PutAsync(AssetKey(projectId, assetId, ".bin"), data, cancellationToken);
        await blobs.PutAsync(AssetKey(projectId, assetId, ".json"),
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(mediaType)), cancellationToken);
        return new ChatResource(Guid.CreateVersion7(), imageFormat is null ? ChatResourceKind.File : ChatResourceKind.Image,
            sourceLocation, safeName,
            Source: source, AssetId: assetId, MediaType: mediaType, Size: data.Length);
    }

    public async Task<ResourceAsset?> ReadAsync(Guid projectId, string assetId, CancellationToken cancellationToken)
    {
        if (assetId.Length != 64 || assetId.Any(character => !Uri.IsHexDigit(character))) return null;
        if (await projects.GetAsync(projectId, cancellationToken) is null) return null;
        var normalized = assetId.ToLowerInvariant();
        if (await blobs.ReadAsync(AssetKey(projectId, normalized, ".json"), cancellationToken) is not { } metadata)
            return null;
        var mediaType = JsonSerializer.Deserialize<string>(metadata);
        var bytes = await blobs.ReadAsync(AssetKey(projectId, normalized, ".bin"), cancellationToken);
        return mediaType is null || bytes is null ? null : new ResourceAsset(bytes, mediaType);
    }

    public async Task<ResourceAssetText?> ReadTextAsync(Guid projectId, string assetId, CancellationToken cancellationToken)
    {
        if (assetId.Length != 64 || assetId.Any(character => !Uri.IsHexDigit(character))
            || await projects.GetAsync(projectId, cancellationToken) is null) return null;
        await using var stream = await blobs.OpenReadAsync(AssetKey(projectId, assetId.ToLowerInvariant(), ".bin"),
            cancellationToken);
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
        await blobs.PutAsync(UndoKey(projectId, assetId), data, cancellationToken);
        return assetId;
    }

    public async Task<byte[]?> ReadUndoBytesAsync(Guid projectId, string assetId, CancellationToken cancellationToken)
    {
        if (assetId.Length != 64 || assetId.Any(character => !Uri.IsHexDigit(character))
            || await projects.GetAsync(projectId, cancellationToken) is null) return null;
        var bytes = await blobs.ReadAsync(UndoKey(projectId, assetId.ToLowerInvariant()), cancellationToken);
        return bytes is not null
            && Convert.ToHexString(SHA256.HashData(bytes)).Equals(assetId, StringComparison.OrdinalIgnoreCase)
            ? bytes : null;
    }

    public async Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await blobs.DeletePrefixAsync(ProjectKey(projectId), cancellationToken);
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
