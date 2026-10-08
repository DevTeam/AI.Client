namespace AI.Application.Resources;

using AI.Contracts.FileSystem;
using AI.Contracts.Resources;

public sealed class DirectoryFilePreviewFormat(IProjectPathAccess access, IFileSystem files) : IFilePreviewFormat
{
    public async Task<FilePreview?> DescribeAsync(FilePreviewContext context, CancellationToken cancellationToken)
    {
        if (!context.IsDirectory) return null;
        var entries = new List<FilePreviewEntry>();
        foreach (var item in await files.ListEntriesAsync(context.Path, recursive: false, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!access.CanRead(context.Project, access.ResolveLinks(item.Path))) continue;
                entries.Add(new FilePreviewEntry(item.Path, Path.GetFileName(item.Path), item.IsDirectory));
            }
            catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException) { }
            if (entries.Count > 1000) break;
        }
        return context.Describe("directory",
            entries.Take(1000).OrderByDescending(item => item.IsDirectory).ThenBy(item => item.Name).ToArray(),
            entries.Count > 1000);
    }
}
