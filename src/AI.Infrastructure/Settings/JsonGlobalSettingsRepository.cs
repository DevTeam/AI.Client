namespace AI.Infrastructure.Settings;

using AI.Application.Settings;
using AI.Contracts.FileSystem;
using AI.Contracts.Settings;
using Storage;
using System.Text.Json;

public sealed class JsonGlobalSettingsRepository(IFileSystem fileSystem, IGlobalSettingsPaths paths) : IGlobalSettingsRepository, IDisposable
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly AsyncGate _writes = new();
    public void Dispose() => _writes.Dispose();

    public async Task<GlobalSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var json = await fileSystem.ReadTextAsync(paths.SettingsPath, cancellationToken);
        var settings = json is null ? new GlobalSettings([], [], []) :
            JsonSerializer.Deserialize<GlobalSettings>(json, Options) ?? throw new JsonException("Settings are empty.");
        settings = MigrateLegacyCSharpServer(settings with { ToolPolicies = settings.ToolPolicies ?? [] });
        // The Host's own servers are always present in the settings the rest of the application
        // reads, whether or not the stored document has caught up with them yet.
        var servers = settings.McpServers;
        if (servers.All(server => server.Id != DefaultMcpServer.Id)) servers = [DefaultMcpServer.Settings, .. servers];
        if (servers.All(server => server.Id != AppMcpServer.Id)) servers = [.. servers, AppMcpServer.Settings];
        return ReferenceEquals(servers, settings.McpServers) ? settings : settings with { McpServers = servers };
    }

    /// <summary>
    /// Drops the retired C# scripting server's entry and moves its <c>cs_run</c> policies to the
    /// built-in server, denying the tool there when the old server was switched off or denied.
    /// </summary>
    private static GlobalSettings MigrateLegacyCSharpServer(GlobalSettings settings)
    {
        var legacy = settings.McpServers.SingleOrDefault(server => server.Id == LegacyCSharpMcpServer.Id);
        if (legacy is null && settings.ToolPolicies.All(policy => policy.ServerId != LegacyCSharpMcpServer.Id))
            return settings;

        var denied = legacy is { Enabled: false } or { Policy: "Deny" };
        return settings with
        {
            McpServers = settings.McpServers.Where(server => server.Id != LegacyCSharpMcpServer.Id).ToArray(),
            ToolPolicies = LegacyCSharpToolMigration.Migrate(settings.ToolPolicies, denied,
                policy => (policy.ServerId, policy.Name, policy.SchemaHash),
                (policy, deny) => policy with { ServerId = DefaultMcpServer.Id, Decision = deny ? "Deny" : policy.Decision },
                schemaHash => new McpToolPolicySettings(DefaultMcpServer.Id, LegacyCSharpMcpServer.ScriptToolName, schemaHash,
                    "Deny", McpToolPolicySettings.DefaultMaxCallsPerRun, McpToolPolicySettings.DefaultTimeoutSeconds))
        };
    }

    public async Task SaveAsync(GlobalSettings settings, CancellationToken cancellationToken)
    {
        using var lease = await _writes.EnterAsync(cancellationToken);
        await WriteAsync(settings, cancellationToken);
    }

    public async Task<GlobalSettings> UpdateAsync(
        Func<GlobalSettings, CancellationToken, Task<GlobalSettings>> update,
        CancellationToken cancellationToken)
    {
        using var lease = await _writes.EnterAsync(cancellationToken);
        var settings = await update(await LoadAsync(cancellationToken), cancellationToken);
        await WriteAsync(settings, cancellationToken);
        return settings;
    }

    private async Task WriteAsync(GlobalSettings settings, CancellationToken cancellationToken)
    {
        await fileSystem.WriteTextAsync(paths.SettingsPath + ".tmp", JsonSerializer.Serialize(settings, Options), cancellationToken);
        await fileSystem.MoveAsync(paths.SettingsPath + ".tmp", paths.SettingsPath, true, cancellationToken);
    }
}
