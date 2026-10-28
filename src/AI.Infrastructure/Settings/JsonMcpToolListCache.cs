namespace AI.Infrastructure.Settings;

using System.Text.Json;
using AI.Application.Tools;
using AI.Contracts.FileSystem;
using Storage;

/// <summary>
/// Keeps each external MCP server's last tool list in <c>mcp-tools.json</c> next to
/// <c>settings.json</c>: one entry per server, for the configuration it was listed under. It is a
/// cache, so a file that cannot be read counts as empty rather than as an error.
/// </summary>
public sealed class JsonMcpToolListCache(IFileSystem fileSystem, IGlobalSettingsPaths paths)
    : IMcpToolListCache, IDisposable
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly AsyncGate _writes = new();

    public void Dispose() => _writes.Dispose();

    private string FilePath => Path.Combine(Path.GetDirectoryName(paths.SettingsPath)!, "mcp-tools.json");

    public async Task<IReadOnlyList<JsonElement>?> GetAsync(Guid serverId, string fingerprint, CancellationToken cancellationToken) =>
        (await LoadAsync(cancellationToken)).TryGetValue(serverId.ToString("N"), out var entry) && entry.Fingerprint == fingerprint
            ? entry.Tools
            : null;

    public async Task SetAsync(Guid serverId, string fingerprint, IReadOnlyList<JsonElement> tools, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tools);
        using var lease = await _writes.EnterAsync(cancellationToken);
        var entries = await LoadAsync(cancellationToken);
        entries[serverId.ToString("N")] = new Entry(fingerprint, tools.ToArray());
        await fileSystem.WriteTextAsync(FilePath + ".tmp", JsonSerializer.Serialize(entries, Options), cancellationToken);
        await fileSystem.MoveAsync(FilePath + ".tmp", FilePath, true, cancellationToken);
    }

    private async Task<Dictionary<string, Entry>> LoadAsync(CancellationToken cancellationToken)
    {
        var json = await fileSystem.ReadTextAsync(FilePath, cancellationToken);
        if (json is null) return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, Entry>>(json, Options) ?? [];
        }
        catch (JsonException)
        {
            // Rewritten whole on the next store; until then every server is simply started.
            return [];
        }
    }

    private sealed record Entry(string Fingerprint, JsonElement[] Tools);
}
