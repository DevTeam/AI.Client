namespace AI.Application.Resources;

using AI.Contracts.Resources;

public interface IFilePreviewTextReader
{
    Task<bool> IsTextAsync(string path, CancellationToken cancellationToken);
    Task<FilePreviewText> ReadAsync(string path, int offset, CancellationToken cancellationToken);
}
