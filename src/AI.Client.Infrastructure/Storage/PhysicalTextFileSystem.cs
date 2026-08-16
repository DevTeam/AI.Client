namespace AI.Client.Infrastructure.Storage;

public sealed class PhysicalTextFileSystem : ITextFileSystem
{
    public Task<IReadOnlyList<string>> ListFilesRecursivelyAsync(string directoryPath, string searchPattern, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>(Directory.Exists(directoryPath)
            ? Directory.GetFiles(directoryPath, searchPattern, SearchOption.AllDirectories) : []);
    }
    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(File.Exists(path));

    public Task<IReadOnlyList<string>> ListFilesAsync(
        string directoryPath,
        string searchPattern,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(
            Directory.Exists(directoryPath)
                ? Directory.GetFiles(directoryPath, searchPattern, SearchOption.TopDirectoryOnly)
                : []);

    public async Task<string?> ReadTextAsync(string path, CancellationToken cancellationToken) =>
        File.Exists(path)
            ? await File.ReadAllTextAsync(path, cancellationToken)
            : null;

    public async Task WriteTextAsync(string path, string content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Storage path must include a directory.");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, content, cancellationToken);
    }

    public Task MoveAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken)
    {
        File.Move(sourcePath, destinationPath, overwrite);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        File.Delete(path);
        return Task.CompletedTask;
    }
}
