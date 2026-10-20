namespace AI.Web.Resources;

using AI.Contracts.Resources;

public interface IFilePreviewApi
{
    Task<FilePreview> DescribeAsync(Guid projectId, string path, CancellationToken cancellationToken);
    Task<FilePreviewText> ReadTextAsync(Guid projectId, string path, int offset, CancellationToken cancellationToken);

    /// <summary>Asks the Host to open the file or directory with its system default action.</summary>
    Task OpenAsync(Guid projectId, string path, CancellationToken cancellationToken);
}
