namespace AI.Application.Resources;

using AI.Contracts.Resources;

public interface IFilePreviewService
{
    Task<FilePreview> DescribeAsync(Guid projectId, string path, CancellationToken cancellationToken);
    Task<FilePreviewText> ReadTextAsync(Guid projectId, string path, int offset, CancellationToken cancellationToken);
    Task<string?> ResolveContentAsync(string ticket, CancellationToken cancellationToken);

    /// <summary>Opens a readable file or directory of the project with the system's default action.</summary>
    Task OpenAsync(Guid projectId, string path, CancellationToken cancellationToken);
}
