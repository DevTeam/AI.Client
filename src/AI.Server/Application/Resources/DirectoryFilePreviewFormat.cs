namespace AI.Application.Resources;

using AI.Contracts.Resources;

public sealed class DirectoryFilePreviewFormat(IProjectPathAccess access) : IFilePreviewFormat
{
    public Task<FilePreview?> DescribeAsync(FilePreviewContext context, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(context.Path)) return Task.FromResult<FilePreview?>(null);
        var entries = new List<FilePreviewEntry>();
        foreach (var item in Directory.EnumerateFileSystemEntries(context.Path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!access.CanRead(context.Project, access.ResolveLinks(item))) continue;
                entries.Add(new FilePreviewEntry(item, Path.GetFileName(item), Directory.Exists(item)));
            }
            catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException) { }
            if (entries.Count > 1000) break;
        }
        return Task.FromResult<FilePreview?>(context.Describe("directory",
            entries.Take(1000).OrderByDescending(item => item.IsDirectory).ThenBy(item => item.Name).ToArray(),
            entries.Count > 1000));
    }
}
