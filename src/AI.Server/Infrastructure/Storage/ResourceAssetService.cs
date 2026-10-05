namespace AI.Infrastructure.Storage;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AI.Application.Projects;
using AI.Application.Resources;
using AI.Contracts.Resources;
using Microsoft.AspNetCore.StaticFiles;

/// <summary>Stores uploaded file bytes outside chat JSON and retains their source on the resource.</summary>
public sealed class ResourceAssetService(IProjectStorageLocation location, IProjectService projects)
    : IResourceAssetService
{
    private const int MaximumBytes = 15 * 1024 * 1024;
    private const int MaximumTextBytes = 64 * 1024;
    private readonly ConcurrentDictionary<string, (Guid ProjectId, string AssetId, DateTimeOffset Expires)> _tickets = new();
    private readonly FileExtensionContentTypeProvider _contentTypes = new();

    public async Task<ChatResource> StoreAsync(Guid projectId, byte[] data, string name,
        ChatResourceSource source, string sourceLocation, CancellationToken cancellationToken)
    {
        if (await projects.GetAsync(projectId, cancellationToken) is null)
            throw new FileNotFoundException("Project not found.");
        if (data.Length is 0 or > MaximumBytes) throw new InvalidDataException("File size must be between 1 byte and 15 MB.");
        var safeName = Path.GetFileName(name.Trim());
        if (safeName.Length > 200) safeName = safeName[..200];
        if (safeName.Length == 0) safeName = "File";
        var imageFormat = DetectFormat(data);
        var mediaType = imageFormat?.MediaType ??
            (_contentTypes.TryGetContentType(safeName, out var detected) ? detected : "application/octet-stream");
        var fingerprint = new byte[data.Length + Encoding.UTF8.GetByteCount(mediaType)];
        data.CopyTo(fingerprint, 0);
        Encoding.UTF8.GetBytes(mediaType, fingerprint.AsSpan(data.Length));
        var assetId = Convert.ToHexString(SHA256.HashData(fingerprint)).ToLowerInvariant();
        var directory = Path.Combine(location.RootDirectory, "assets", projectId.ToString("N"));
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, assetId + ".bin");
        if (!File.Exists(destination))
        {
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, data, cancellationToken);
                try { File.Move(temporary, destination); }
                catch (IOException) when (File.Exists(destination)) { }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        var metadata = Path.Combine(directory, assetId + ".json");
        if (!File.Exists(metadata))
        {
            var temporary = metadata + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(mediaType), cancellationToken);
                try { File.Move(temporary, metadata); }
                catch (IOException) when (File.Exists(metadata)) { }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return new ChatResource(Guid.CreateVersion7(), imageFormat is null ? ChatResourceKind.File : ChatResourceKind.Image,
            sourceLocation, safeName,
            Source: source, AssetId: assetId, MediaType: mediaType, Size: data.Length);
    }

    public async Task<ResourceAsset?> ReadAsync(Guid projectId, string assetId, CancellationToken cancellationToken)
    {
        if (assetId.Length != 64 || assetId.Any(character => !Uri.IsHexDigit(character))) return null;
        if (await projects.GetAsync(projectId, cancellationToken) is null) return null;
        var directory = Path.Combine(location.RootDirectory, "assets", projectId.ToString("N"));
        var normalized = assetId.ToLowerInvariant();
        var path = Path.Combine(directory, normalized + ".bin");
        var metadata = Path.Combine(directory, normalized + ".json");
        if (!File.Exists(path) || !File.Exists(metadata)) return null;
        var mediaType = JsonSerializer.Deserialize<string>(await File.ReadAllTextAsync(metadata, cancellationToken));
        return mediaType is null ? null
            : new ResourceAsset(await File.ReadAllBytesAsync(path, cancellationToken), mediaType);
    }

    public async Task<ResourceAssetText?> ReadTextAsync(Guid projectId, string assetId, CancellationToken cancellationToken)
    {
        if (assetId.Length != 64 || assetId.Any(character => !Uri.IsHexDigit(character))
            || await projects.GetAsync(projectId, cancellationToken) is null) return null;
        var path = Path.Combine(location.RootDirectory, "assets", projectId.ToString("N"),
            assetId.ToLowerInvariant() + ".bin");
        if (!File.Exists(path)) return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous);
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

    public Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(Path.Combine(location.RootDirectory, "assets"));
        var target = Path.GetFullPath(Path.Combine(root, projectId.ToString("N")));
        if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid resource asset directory.");
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        foreach (var ticket in _tickets.Where(item => item.Value.ProjectId == projectId))
            _tickets.TryRemove(ticket.Key, out _);
        return Task.CompletedTask;
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
