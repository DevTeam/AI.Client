namespace AI.Client.Cli;

using System.Text.Json;

internal interface IHeadlessSessionStore
{
    Task<HeadlessSession?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task SaveAsync(HeadlessSession session, CancellationToken cancellationToken);
    Task AppendTranscriptAsync(Guid id, object entry, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

internal sealed class HeadlessSessionStore(HeadlessPaths paths) : IHeadlessSessionStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<HeadlessSession?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var path = paths.GetSessionPath(id);
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<HeadlessSession>(stream, Options, cancellationToken);
    }

    public async Task SaveAsync(HeadlessSession session, CancellationToken cancellationToken)
    {
        var path = paths.GetSessionPath(session.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, session, Options, cancellationToken);
    }

    public Task AppendTranscriptAsync(Guid id, object entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = paths.GetTranscriptPath(id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return File.AppendAllTextAsync(path, JsonSerializer.Serialize(entry, Options) + Environment.NewLine, cancellationToken);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(paths.GetSessionPath(id))!;
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
        return Task.CompletedTask;
    }
}
