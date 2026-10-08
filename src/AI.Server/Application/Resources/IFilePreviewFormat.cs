namespace AI.Application.Resources;

using AI.Contracts.Projects;
using AI.Contracts.Resources;

/// <summary>
/// Everything a format needs to describe a path. Whether it is a directory and how large it is are
/// resolved once by <see cref="FilePreviewService"/> and carried here, so a format stays a decision
/// about a kind rather than a question to the file system.
/// </summary>
public sealed record FilePreviewContext(ProjectDetails Project, string Path, string ContentType,
    bool IsDirectory, long Size)
{
    public FilePreview Describe(string kind, IReadOnlyList<FilePreviewEntry>? entries = null, bool hasMore = false) =>
        new(Path, System.IO.Path.GetFileName(Path), kind, ContentType,
            IsDirectory ? 0 : Size, null, entries ?? [], hasMore);
}

/// <summary>Specialized formats are tried before the generic text/binary fallback.</summary>
public interface IFilePreviewFormat
{
    Task<FilePreview?> DescribeAsync(FilePreviewContext context, CancellationToken cancellationToken);
}

public interface IFilePreviewFormats
{
    Task<FilePreview> DescribeAsync(FilePreviewContext context, CancellationToken cancellationToken);
}

public interface ITextFilePreviewFormat
{
    Task<FilePreview> DescribeAsync(FilePreviewContext context, CancellationToken cancellationToken);
}
