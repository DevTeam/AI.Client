namespace AI.Application.Resources;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using AI.Application.Projects;
using AI.Contracts.Resources;
using Microsoft.AspNetCore.StaticFiles;

public sealed class FilePreviewService(IProjectService projects, IWorkspacePathResolver resolver,
    IProjectPathAccess access) : IFilePreviewService
{
    private readonly ConcurrentDictionary<string, (Guid Project, string Path, DateTimeOffset Expires)> _tickets = new();
    private readonly FileExtensionContentTypeProvider _types = new();

    public async Task<FilePreview> DescribeAsync(Guid projectId, string path, CancellationToken cancellationToken)
    {
        var canonical = await ReadablePathAsync(projectId, path, cancellationToken);
        if (Directory.Exists(canonical))
        {
            var project = await projects.GetAsync(projectId, cancellationToken)
                ?? throw new FileNotFoundException("Project not found.");
            var entries = new List<FilePreviewEntry>();
            foreach (var item in Directory.EnumerateFileSystemEntries(canonical))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!access.CanRead(project, access.ResolveLinks(item))) continue;
                    entries.Add(new FilePreviewEntry(item, Path.GetFileName(item), Directory.Exists(item)));
                }
                catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException) { }
                if (entries.Count > 1000) break;
            }
            return new FilePreview(canonical, Path.GetFileName(canonical), "directory", "", 0, null,
                entries.Take(1000).OrderByDescending(item => item.IsDirectory).ThenBy(item => item.Name).ToArray(), entries.Count > 1000);
        }

        var info = new FileInfo(canonical);
        _types.TryGetContentType(canonical, out var contentType);
        contentType ??= "application/octet-stream";
        var kind = contentType.StartsWith("image/", StringComparison.Ordinal) ? "image"
            : contentType.StartsWith("video/", StringComparison.Ordinal) ? "video"
            : contentType.StartsWith("audio/", StringComparison.Ordinal) ? "audio"
            : contentType == "application/pdf" ? "pdf"
            : await IsTextAsync(canonical, cancellationToken) ? "text" : "binary";
        foreach (var ticket in _tickets.Where(item => item.Value.Expires <= DateTimeOffset.UtcNow))
            _tickets.TryRemove(ticket.Key, out _);
        if (_tickets.Count >= 4096) throw new InvalidOperationException("Too many file previews. Try again later.");
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _tickets[key] = (projectId, canonical, DateTimeOffset.UtcNow.AddHours(4));
        return new FilePreview(canonical, info.Name, kind, contentType, info.Length,
            $"file-preview-content/{key}", []);
    }

    public async Task<string?> ResolveContentAsync(string ticket, CancellationToken cancellationToken)
    {
        if (!_tickets.TryGetValue(ticket, out var value) || value.Expires <= DateTimeOffset.UtcNow) return null;
        // A ticket carries only one file and never bypasses a revoked grant or a changed symlink.
        var path = await ReadablePathAsync(value.Project, value.Path, cancellationToken);
        return File.Exists(path) ? path : null;
    }

    public async Task<FilePreviewText> ReadTextAsync(Guid projectId, string path, int offset, CancellationToken cancellationToken)
    {
        if (offset is < 0 or > 20_000_000) throw new ArgumentException("Text preview offset is out of range.");
        var canonical = await ReadablePathAsync(projectId, path, cancellationToken);
        if (!await IsTextAsync(canonical, cancellationToken)) throw new ArgumentException("This file is not supported as text.");
        using var reader = File.OpenText(canonical);
        var buffer = new char[32768];
        var skipped = 0;
        while (skipped < offset)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, offset - skipped)), cancellationToken);
            if (count == 0) return new FilePreviewText("", null);
            skipped += count;
        }
        var length = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
        var text = new string(buffer, 0, length);
        // JSON cannot carry half of a surrogate pair: keep an emoji on one side of a page.
        if (length > 0 && char.IsHighSurrogate(buffer[length - 1]) && reader.Peek() is >= 0xDC00 and <= 0xDFFF)
        {
            text += (char)reader.Read();
            length++;
        }
        return new FilePreviewText(text, reader.Peek() < 0 ? null : offset + length);
    }

    private async Task<string> ReadablePathAsync(Guid projectId, string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096) throw new ArgumentException("Invalid file path.");
        var results = await resolver.ResolveAsync(projectId, [path], cancellationToken);
        var found = results.Count > 0 ? results[0] : null;
        if (found?.Path is not { } resolved) throw new FileNotFoundException("File or directory not found.");
        var project = await projects.GetAsync(projectId, cancellationToken)
            ?? throw new FileNotFoundException("Project not found.");
        var canonical = access.ResolveLinks(resolved);
        if (!access.CanRead(project, canonical)) throw new UnauthorizedAccessException("The project does not have read access to this path.");
        return canonical;
    }

    private async Task<bool> IsTextAsync(string path, CancellationToken cancellationToken)
    {
        if (_types.TryGetContentType(path, out var type) && (type.StartsWith("image/", StringComparison.Ordinal)
            || type.StartsWith("video/", StringComparison.Ordinal) || type.StartsWith("audio/", StringComparison.Ordinal)
            || type == "application/pdf")) return false;
        using var reader = File.OpenText(path);
        var buffer = new char[4096];
        var length = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
        for (var index = 0; index < length; index++)
            if (buffer[index] == '\uFFFD' || buffer[index] < ' ' && buffer[index] is not ('\t' or '\r' or '\n' or '\f')) return false;
        return true;
    }
}
