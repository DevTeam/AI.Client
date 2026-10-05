namespace AI.Application.Resources;

using System.IO.Compression;
using AI.Contracts.Resources;

/// <summary>Lists archive metadata without extracting or decompressing any entry.</summary>
public sealed class ArchiveFilePreviewFormat : IFilePreviewFormat
{
    public Task<FilePreview?> DescribeAsync(FilePreviewContext context, CancellationToken cancellationToken)
    {
        if (Directory.Exists(context.Path) || !Path.GetExtension(context.Path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<FilePreview?>(null);
        using var archive = ZipFile.OpenRead(context.Path);
        var entries = new List<FilePreviewEntry>();
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = entry.FullName.Replace('\\', '/');
            // Archive names are virtual paths, never paths to open on the host.
            if (path.StartsWith('/') || path.Contains(':') || path.Split('/').Any(part => part is ".." or ".")) continue;
            var directory = path.EndsWith('/');
            var name = path.TrimEnd('/').Split('/')[^1];
            if (name.Length == 0) continue;
            entries.Add(new FilePreviewEntry(path, name, directory, entry.Length, entry.CompressedLength));
            if (entries.Count > 1000) break;
        }
        return Task.FromResult<FilePreview?>(context.Describe("archive", entries.Take(1000).ToArray(), entries.Count > 1000));
    }
}
