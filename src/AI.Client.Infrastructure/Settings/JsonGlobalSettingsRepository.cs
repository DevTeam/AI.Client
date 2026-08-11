using AI.Client.Application.Settings;
using AI.Client.Contracts.Settings;
using AI.Client.Infrastructure.Storage;
using System.Text.Json;

namespace AI.Client.Infrastructure.Settings;

public sealed class JsonGlobalSettingsRepository(
    ITextFileSystem fileSystem,
    GlobalSettingsPaths paths) : IGlobalSettingsRepository
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public async Task<GlobalSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var connectionsJson = await fileSystem.ReadTextAsync(paths.ConnectionsPath, cancellationToken);
        var mcpJson = await fileSystem.ReadTextAsync(paths.McpServersPath, cancellationToken);
        return new GlobalSettings(
            connectionsJson is null ? [] : JsonSerializer.Deserialize<ConnectionSettings[]>(connectionsJson, Options) ?? [],
            mcpJson is null ? [] : JsonSerializer.Deserialize<McpServerSettings[]>(mcpJson, Options) ?? []);
    }

    public async Task SaveAsync(GlobalSettings settings, CancellationToken cancellationToken)
    {
        await fileSystem.WriteTextAsync(
            paths.ConnectionsPath,
            JsonSerializer.Serialize(settings.Connections, Options),
            cancellationToken);
        await fileSystem.WriteTextAsync(
            paths.McpServersPath,
            JsonSerializer.Serialize(settings.McpServers, Options),
            cancellationToken);
    }
}
