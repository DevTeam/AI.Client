// ReSharper disable UseCollectionExpression
namespace AI.Client.Application.Settings;

using AI.Client.Contracts.Settings;
using System.Text.Json;

public sealed class GlobalSettingsService(
    IGlobalSettingsRepository repository,
    IGlobalSecretStore secretStore) : IGlobalSettingsService
{
    public async Task<GlobalSettings> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await repository.LoadAsync(cancellationToken);
        return new GlobalSettings(
            await Task.WhenAll(settings.Connections.Select(async item => item with
            {
                HasCredential = await secretStore.ExistsAsync("connection", item.Id, cancellationToken)
            })),
            await Task.WhenAll(settings.McpServers.Select(async item =>
            {
                var secretVariables = await LoadEnvironmentSecretsAsync(item.Id, cancellationToken);
                return item with
                {
                    HasCredential = await secretStore.ExistsAsync("mcp", item.Id, cancellationToken),
                    EnvironmentVariables = item.EnvironmentVariables.Select(variable => variable.IsSecret
                        ? variable with { Value = null, HasSecret = secretVariables.ContainsKey(variable.Name) }
                        : variable).ToArray()
                };
            })),
            settings.ToolPolicies);
    }

    public async Task<GlobalSettings> SaveAsync(
        SaveGlobalSettingsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var connections = request.Connections.Select(Normalize).ToArray();
        if (connections.GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Connection names must be unique.", nameof(request));
        }

        if (connections.Count(item => item.IsDefault) > 1)
        {
            throw new ArgumentException("Only one connection can be the default.", nameof(request));
        }

        connections = connections.Select(item => item.Enabled ? item : item with { IsDefault = false }).ToArray();

        if (connections.Length > 0 && connections.All(item => !item.IsDefault) && connections.Any(item => item.Enabled))
        {
            var firstEnabled = connections.First(item => item.Enabled);
            connections = connections.Select(item => item with { IsDefault = item.Id == firstEnabled.Id }).ToArray();
        }

        var mcpServers = new List<McpServerSettings>();
        foreach (var item in request.McpServers.Select(Normalize))
        {
            var secrets = await LoadEnvironmentSecretsAsync(item.Id, cancellationToken);
            var secretNames = item.EnvironmentVariables.Where(variable => variable.IsSecret)
                .Select(variable => variable.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var variable in item.EnvironmentVariables.Where(variable => variable.IsSecret && !string.IsNullOrEmpty(variable.Value)))
                secrets[variable.Name] = variable.Value!;
            foreach (var staleName in secrets.Keys.Where(name => !secretNames.Contains(name)).ToArray()) secrets.Remove(staleName);
            await secretStore.SetAsync(
                "mcp-env",
                item.Id,
                secrets.Count == 0 ? null : JsonSerializer.Serialize(secrets),
                cancellationToken);
            mcpServers.Add(item with
            {
                EnvironmentVariables = item.EnvironmentVariables.Select(variable =>
                    variable.IsSecret ? variable with { Value = null, HasSecret = secrets.ContainsKey(variable.Name) } : variable).ToArray()
            });
        }

        var toolPolicies = request.ToolPolicies.Select(Normalize).ToArray();
        var settings = new GlobalSettings(connections, mcpServers, toolPolicies);
        await repository.SaveAsync(settings, cancellationToken);
        return await GetAsync(cancellationToken);
    }

    public async Task<bool> SetConnectionCredentialAsync(Guid id, string? value, CancellationToken cancellationToken)
    {
        if ((await repository.LoadAsync(cancellationToken)).Connections.All(item => item.Id != id)) return false;
        await secretStore.SetAsync("connection", id, value, cancellationToken);
        return true;
    }

    public async Task<bool> SetMcpCredentialAsync(Guid id, string? value, CancellationToken cancellationToken)
    {
        if ((await repository.LoadAsync(cancellationToken)).McpServers.All(item => item.Id != id)) return false;
        await secretStore.SetAsync("mcp", id, value, cancellationToken);
        return true;
    }

    public async Task<GlobalSettings> SetToolPolicyAsync(McpToolPolicySettings policy, CancellationToken cancellationToken)
    {
        policy = Normalize(policy);
        var settings = await repository.LoadAsync(cancellationToken);
        var policies = settings.ToolPolicies.Where(item => item.ServerId != policy.ServerId
            || item.Name != policy.Name).Append(policy).ToArray();
        await repository.SaveAsync(settings with { ToolPolicies = policies }, cancellationToken);
        return await GetAsync(cancellationToken);
    }

    public async Task<GlobalSettings> RemoveToolPolicyAsync(Guid serverId, string name, string schemaHash,
        CancellationToken cancellationToken)
    {
        var settings = await repository.LoadAsync(cancellationToken);
        await repository.SaveAsync(settings with { ToolPolicies = settings.ToolPolicies.Where(item =>
            item.ServerId != serverId || item.Name != name || item.SchemaHash != schemaHash).ToArray() }, cancellationToken);
        return await GetAsync(cancellationToken);
    }

    private static ConnectionSettings Normalize(ConnectionSettings item)
    {
        if (string.IsNullOrWhiteSpace(item.Name) || !Uri.TryCreate(item.BaseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(item.Model))
        {
            throw new ArgumentException("Connection name, absolute HTTP base URL, and model are required.");
        }

        return item with { Name = item.Name.Trim(), BaseUrl = item.BaseUrl.TrimEnd('/'), Model = item.Model.Trim() };
    }

    private static McpServerSettings Normalize(McpServerSettings item)
    {
        if (string.IsNullOrWhiteSpace(item.Name)
            || item.Transport is not ("StreamableHttp" or "Stdio")
            || item.Policy is not ("Allow" or "Ask" or "Deny"))
        {
            throw new ArgumentException("MCP name, transport, and policy are required.");
        }

        return item with { Name = item.Name.Trim() };
    }

    private static McpToolPolicySettings Normalize(McpToolPolicySettings item)
    {
        if (string.IsNullOrWhiteSpace(item.Name)
            || string.IsNullOrWhiteSpace(item.SchemaHash)
            || item.Decision is not ("Allow" or "Ask" or "Deny")
            || item.MaxCallsPerRun is < 1 or > int.MaxValue
            || item.TimeoutSeconds is < 1 or > 600)
        {
            throw new ArgumentException("Tool name, schema, policy, call limit, and timeout are invalid.");
        }

        return item with { Name = item.Name.Trim(), SchemaHash = item.SchemaHash.Trim() };
    }

    private async Task<Dictionary<string, string>> LoadEnvironmentSecretsAsync(Guid id, CancellationToken cancellationToken)
    {
        var json = await secretStore.GetAsync("mcp-env", id, cancellationToken);
        return json is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : JsonSerializer.Deserialize<Dictionary<string, string>>(json)
              ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
