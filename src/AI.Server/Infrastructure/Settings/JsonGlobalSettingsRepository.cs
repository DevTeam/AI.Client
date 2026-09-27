namespace AI.Infrastructure.Settings;

using AI.Application.Settings;
using AI.Contracts.Settings;
using Storage;
using System.Text.Json;

public sealed class JsonGlobalSettingsRepository(ITextFileSystem fileSystem, IGlobalSettingsPaths paths) : IGlobalSettingsRepository, IDisposable
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly AsyncGate _writes = new();
    public void Dispose() => _writes.Dispose();

    public async Task<GlobalSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var json = await fileSystem.ReadTextAsync(paths.SettingsPath, cancellationToken);
        var settings = json is null ? new GlobalSettings([], [], []) :
            JsonSerializer.Deserialize<GlobalSettings>(json, Options) ?? throw new JsonException("Settings are empty.");
        settings = settings with { ToolPolicies = settings.ToolPolicies ?? [] };
        // The Host's own servers are always present in the settings the rest of the application
        // reads, whether or not the stored document has caught up with them yet.
        var servers = settings.McpServers;
        if (servers.All(server => server.Id != DefaultMcpServer.Id)) servers = [DefaultMcpServer.Settings, .. servers];
        if (servers.All(server => server.Id != AppMcpServer.Id)) servers = [.. servers, AppMcpServer.Settings];
        return ReferenceEquals(servers, settings.McpServers) ? settings : settings with { McpServers = servers };
    }

    public async Task SaveAsync(GlobalSettings settings, CancellationToken cancellationToken)
    {
        using var lease = await _writes.EnterAsync(cancellationToken);
        await fileSystem.WriteTextAsync(paths.SettingsPath + ".tmp", JsonSerializer.Serialize(settings, Options), cancellationToken);
        await fileSystem.MoveAsync(paths.SettingsPath + ".tmp", paths.SettingsPath, true, cancellationToken);
    }
}
