namespace AI.Application.Resources;

using AI.Contracts.Projects;
using AI.Contracts.Resources;

public sealed record FilePreviewContext(ProjectDetails Project, string Path, string ContentType)
{
    public FilePreview Describe(string kind, IReadOnlyList<FilePreviewEntry>? entries = null, bool hasMore = false) =>
        new(Path, System.IO.Path.GetFileName(Path), kind, ContentType,
            Directory.Exists(Path) ? 0 : new FileInfo(Path).Length, null, entries ?? [], hasMore);
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
