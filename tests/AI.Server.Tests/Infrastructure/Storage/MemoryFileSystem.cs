// ReSharper disable UseCollectionExpression
namespace AI.Infrastructure.Tests.Storage;

using AI.Infrastructure.Storage;
using System.Collections.Concurrent;
using System.IO.Enumeration;

internal sealed class MemoryFileSystem : ITextFileSystem
{
    public ConcurrentDictionary<string, string> Files { get; } = new(StringComparer.Ordinal);
    public ConcurrentQueue<string> ReadPaths { get; } = new();
    public string? FailWriteSuffix { get; set; }
    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken) => Task.FromResult(Files.ContainsKey(path));
    public Task<IReadOnlyList<string>> ListFilesAsync(string directoryPath, string searchPattern, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(Files.Keys.Where(path => Path.GetDirectoryName(path) == directoryPath
            && FileSystemName.MatchesSimpleExpression(searchPattern, Path.GetFileName(path))).ToArray());
    public Task<IReadOnlyList<string>> ListFilesRecursivelyAsync(string directoryPath, string searchPattern, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(Files.Keys.Where(path => path.StartsWith(directoryPath + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && FileSystemName.MatchesSimpleExpression(searchPattern, Path.GetFileName(path))).ToArray());
    public Task<string?> ReadTextAsync(string path, CancellationToken cancellationToken)
    {
        ReadPaths.Enqueue(path);
        return Task.FromResult(Files.GetValueOrDefault(path));
    }
    public async Task WriteTextAsync(string path, string content, CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        if (FailWriteSuffix is { } suffix && path.EndsWith(suffix, StringComparison.Ordinal)) throw new IOException("Simulated storage failure.");
        Files[path] = content;
    }
    public Task MoveAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Files.TryRemove(sourcePath, out var value)) throw new FileNotFoundException(sourcePath);
        // ReSharper disable once ConvertIfStatementToSwitchStatement
        if (!overwrite && !Files.TryAdd(destinationPath, value)) throw new IOException("Destination exists.");
        if (overwrite) Files[destinationPath] = value;
        return Task.CompletedTask;
    }
    public Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        Files.TryRemove(path, out _);
        return Task.CompletedTask;
    }
}
