namespace AI.Client.Infrastructure.Settings;

using AI.Client.Application.Settings;
using AI.Client.Contracts.Settings;
using Storage;
using System.Text.Json;

public sealed class JsonGlobalSettingsRepository(ITextFileSystem fileSystem, GlobalSettingsPaths paths) : IGlobalSettingsRepository, IDisposable
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly AsyncGate _writes = new();
    public void Dispose() => _writes.Dispose();

    public async Task<GlobalSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var json = await fileSystem.ReadTextAsync(paths.SettingsPath, cancellationToken);
        var settings = json is null ? new GlobalSettings([], []) :
            JsonSerializer.Deserialize<GlobalSettings>(json, Options) ?? throw new JsonException("Settings are empty.");
        return settings.McpServers.Any(server => server.Id == DefaultMcpServer.Id) ? settings
            : settings with { McpServers = [DefaultMcpServer.Settings, .. settings.McpServers] };
    }

    public async Task SaveAsync(GlobalSettings settings, CancellationToken cancellationToken)
    {
        using var lease = await _writes.EnterAsync(cancellationToken);
        await fileSystem.WriteTextAsync(paths.SettingsPath + ".tmp", JsonSerializer.Serialize(settings, Options), cancellationToken);
        await fileSystem.MoveAsync(paths.SettingsPath + ".tmp", paths.SettingsPath, true, cancellationToken);
    }
}
