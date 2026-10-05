namespace AI.Application.Resources;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using AI.Application.Projects;
using AI.Contracts.Resources;
using Microsoft.AspNetCore.StaticFiles;

public sealed class FilePreviewService(IProjectService projects, IWorkspacePathResolver resolver,
    IProjectPathAccess access, IFilePreviewFormats formats, IFilePreviewTextReader text) : IFilePreviewService
{
    private readonly ConcurrentDictionary<string, (Guid Project, string Path, DateTimeOffset Expires)> _tickets = new();
    private readonly FileExtensionContentTypeProvider _types = new();

    public async Task<FilePreview> DescribeAsync(Guid projectId, string path, CancellationToken cancellationToken)
    {
        var canonical = await ReadablePathAsync(projectId, path, cancellationToken);
        var project = await projects.GetAsync(projectId, cancellationToken)
            ?? throw new FileNotFoundException("Project not found.");
        _types.TryGetContentType(canonical, out var contentType);
        var preview = await formats.DescribeAsync(new FilePreviewContext(project, canonical,
            contentType ?? "application/octet-stream"), cancellationToken);
        if (Directory.Exists(canonical)) return preview;

        foreach (var ticket in _tickets.Where(item => item.Value.Expires <= DateTimeOffset.UtcNow))
            _tickets.TryRemove(ticket.Key, out _);
        if (_tickets.Count >= 4096) throw new InvalidOperationException("Too many file previews. Try again later.");
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _tickets[key] = (projectId, canonical, DateTimeOffset.UtcNow.AddHours(4));
        return preview with { ContentUrl = $"file-preview-content/{key}" };
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
        var canonical = await ReadablePathAsync(projectId, path, cancellationToken);
        return await text.ReadAsync(canonical, offset, cancellationToken);
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

}
