// ReSharper disable UnusedParameter.Global
namespace AI.Client.Infrastructure.Storage;

public interface ITextFileSystem
{
    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListFilesAsync(string directoryPath, string searchPattern, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListFilesRecursivelyAsync(string directoryPath, string searchPattern, CancellationToken cancellationToken);

    Task<string?> ReadTextAsync(string path, CancellationToken cancellationToken);

    Task WriteTextAsync(string path, string content, CancellationToken cancellationToken);

    Task MoveAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken);

    Task DeleteAsync(string path, CancellationToken cancellationToken);
}
