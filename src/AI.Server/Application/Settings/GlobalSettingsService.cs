// ReSharper disable UseCollectionExpression
namespace AI.Application.Settings;

using AI.Contracts.Settings;
using System.Text.Json;

public sealed class GlobalSettingsService(
    IGlobalSettingsRepository repository,
    IGlobalSecretStore secretStore,
    IConnectionContextLimitsResolver contextLimits,
    IConnectionModelsResolver modelsResolver) : IGlobalSettingsService
{
    // The endpoint advertises its catalog through OpenAI's GET /v1/models. Providers time out the
    // call after varying windows; we pick a short ceiling so the UI's "Refresh" button never blocks
    // the editor for more than what the user is willing to wait.
    private static readonly TimeSpan ModelsRequestTimeout = TimeSpan.FromSeconds(15);

    public async Task<IReadOnlyList<ResolvedModelInfo>> ResolveConnectionModelsAsync(
        Guid connectionId, ResolveConnectionModelsRequest request, CancellationToken cancellationToken)
    {
        var baseUrl = request.BaseUrl?.Trim();
        if (string.IsNullOrEmpty(baseUrl))
        {
            throw new ArgumentException("Base URL is empty.");
        }

        // A provider requires the key to even list its models. A key typed in the editor is the one
        // the user means; failing that, the saved one. A connection that is not saved yet has none.
        var apiKey = string.IsNullOrWhiteSpace(request.ApiKey)
            ? await LoadSavedConnectionKeyAsync(connectionId, cancellationToken)
            : request.ApiKey;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ModelsRequestTimeout);
        try
        {
            return await modelsResolver.ResolveAsync(baseUrl, apiKey, timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Our own ceiling, not the caller giving up: report it as a failure the UI can show,
            // rather than a cancellation it would silently swallow.
            throw new InvalidOperationException(
                $"The endpoint did not answer within {ModelsRequestTimeout.TotalSeconds:0} seconds.");
        }
    }

    private async Task<string?> LoadSavedConnectionKeyAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        var settings = await repository.LoadAsync(cancellationToken);
        return settings.Connections.Any(item => item.Id == connectionId)
            ? await secretStore.GetAsync("connection", connectionId, cancellationToken)
            : null;
    }

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
            settings.ToolPolicies,
            settings.ChatAutomation ?? new ChatAutomationSettings());
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

        connections = connections
            .Select(item => item.Enabled ? item : item with { IsDefault = false, ForSubtasks = false }).ToArray();

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
        // The request carries only what the connections and MCP editors own; the rest is kept.
        var stored = await repository.LoadAsync(cancellationToken);
        var settings = new GlobalSettings(connections, mcpServers, toolPolicies, stored.ChatAutomation);
        await repository.SaveAsync(settings, cancellationToken);
        return await GetAsync(cancellationToken);
    }

    public async Task<GlobalSettings> UpsertConnectionAsync(ConnectionSettings connection, ConnectionSettings? expected,
        CancellationToken cancellationToken)
    {
        connection = Normalize(connection);
        await repository.UpdateAsync((stored, _) =>
        {
            var current = stored.Connections.SingleOrDefault(item => item.Id == connection.Id);
            CheckExpected(current, expected, SameConnection);
            if (stored.Connections.Any(item => item.Id != connection.Id
                && string.Equals(item.Name, connection.Name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Connection names must be unique.");

            var connections = stored.Connections.Where(item => item.Id != connection.Id)
                .Select(item => connection.IsDefault ? item with { IsDefault = false } : item).ToList();
            var index = current is null ? connections.Count : stored.Connections.ToList().FindIndex(item => item.Id == connection.Id);
            connections.Insert(index, connection with { HasCredential = current?.HasCredential ?? false });
            return Task.FromResult(stored with { Connections = NormalizeConnectionDefaults(connections) });
        }, cancellationToken);
        return await GetAsync(cancellationToken);
    }

    public async Task<GlobalSettings> RemoveConnectionAsync(Guid id, ConnectionSettings expected,
        CancellationToken cancellationToken)
    {
        await repository.UpdateAsync((stored, _) =>
        {
            CheckExpected(stored.Connections.SingleOrDefault(item => item.Id == id), expected, SameConnection);
            return Task.FromResult(stored with
            {
                Connections = NormalizeConnectionDefaults(stored.Connections.Where(item => item.Id != id).ToArray())
            });
        }, cancellationToken);
        return await GetAsync(cancellationToken);
    }

    public async Task<GlobalSettings> UpsertMcpServerAsync(McpServerSettings server, McpServerSettings? expected,
        CancellationToken cancellationToken)
    {
        server = Normalize(server);
        await repository.UpdateAsync(async (stored, token) =>
        {
            var current = stored.McpServers.SingleOrDefault(item => item.Id == server.Id);
            CheckExpected(current, expected, SameMcpServer);
            if ((current is null || !string.Equals(current.Name, server.Name, StringComparison.OrdinalIgnoreCase))
                && stored.McpServers.Any(item => item.Id != server.Id
                    && string.Equals(item.Name, server.Name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("MCP server names must be unique.");

            var secrets = await LoadEnvironmentSecretsAsync(server.Id, token);
            var secretNames = server.EnvironmentVariables.Where(variable => variable.IsSecret)
                .Select(variable => variable.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var variable in server.EnvironmentVariables.Where(variable => variable.IsSecret && !string.IsNullOrEmpty(variable.Value)))
                secrets[variable.Name] = variable.Value!;
            foreach (var staleName in secrets.Keys.Where(name => !secretNames.Contains(name)).ToArray()) secrets.Remove(staleName);
            await secretStore.SetAsync("mcp-env", server.Id,
                secrets.Count == 0 ? null : JsonSerializer.Serialize(secrets), token);
            var updated = server with
            {
                HasCredential = current?.HasCredential ?? false,
                EnvironmentVariables = server.EnvironmentVariables.Select(variable => variable.IsSecret
                    ? variable with { Value = null, HasSecret = secrets.ContainsKey(variable.Name) }
                    : variable).ToArray()
            };
            var servers = stored.McpServers.Where(item => item.Id != server.Id).ToList();
            var index = current is null ? servers.Count : stored.McpServers.ToList().FindIndex(item => item.Id == server.Id);
            servers.Insert(index, updated);
            return stored with { McpServers = servers };
        }, cancellationToken);
        return await GetAsync(cancellationToken);
    }

    public async Task<GlobalSettings> RemoveMcpServerAsync(Guid id, McpServerSettings expected,
        CancellationToken cancellationToken)
    {
        if (id == DefaultMcpServer.Id || id == AppMcpServer.Id || id == CSharpMcpServer.Id)
            throw new InvalidOperationException("Host-provided MCP servers cannot be removed. Disable the server instead.");
        await repository.UpdateAsync((stored, _) =>
        {
            CheckExpected(stored.McpServers.SingleOrDefault(item => item.Id == id), expected, SameMcpServer);
            return Task.FromResult(stored with
            {
                McpServers = stored.McpServers.Where(item => item.Id != id).ToArray(),
                ToolPolicies = stored.ToolPolicies.Where(item => item.ServerId != id).ToArray()
            });
        }, cancellationToken);
        return await GetAsync(cancellationToken);
    }

    private static void CheckExpected<T>(T? current, T? expected, Func<T, T, bool> same) where T : class
    {
        if (current is null && expected is null) return;
        if (current is null || expected is null || !same(current, expected))
            throw new SettingsConflictException("Settings changed since they were read. Reload and review this item before saving.");
    }

    private static bool SameConnection(ConnectionSettings left, ConnectionSettings right) =>
        left with { HasCredential = false } == right with { HasCredential = false };

    private static bool SameMcpServer(McpServerSettings left, McpServerSettings right) =>
        JsonSerializer.Serialize(ComparableMcpServer(left)) == JsonSerializer.Serialize(ComparableMcpServer(right));

    private static McpServerSettings ComparableMcpServer(McpServerSettings server) => server with
    {
        HasCredential = false,
        EnvironmentVariables = server.EnvironmentVariables.Select(variable => variable with
        {
            Value = variable.IsSecret ? null : variable.Value,
            HasSecret = false
        }).ToArray()
    };

    private static ConnectionSettings[] NormalizeConnectionDefaults(IEnumerable<ConnectionSettings> source)
    {
        var connections = source.Select(item => item.Enabled ? item : item with { IsDefault = false, ForSubtasks = false }).ToArray();
        if (connections.Count(item => item.IsDefault) > 1)
        {
            var chosen = connections.Last(item => item.IsDefault).Id;
            connections = connections.Select(item => item with { IsDefault = item.Id == chosen }).ToArray();
        }
        if (connections.Length > 0 && connections.All(item => !item.IsDefault) && connections.Any(item => item.Enabled))
        {
            var chosen = connections.First(item => item.Enabled).Id;
            connections = connections.Select(item => item with { IsDefault = item.Id == chosen }).ToArray();
        }
        return connections;
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
        await repository.UpdateAsync((settings, _) =>
        {
            var policies = settings.ToolPolicies.Where(item => item.ServerId != policy.ServerId
                || item.Name != policy.Name).Append(policy).ToArray();
            return Task.FromResult(settings with { ToolPolicies = policies });
        }, cancellationToken);
        return await GetAsync(cancellationToken);
    }

    public async Task<GlobalSettings> RemoveToolPolicyAsync(Guid serverId, string name, string schemaHash,
        CancellationToken cancellationToken)
    {
        await repository.UpdateAsync((settings, _) => Task.FromResult(settings with
        {
            ToolPolicies = settings.ToolPolicies.Where(item => item.ServerId != serverId
                || item.Name != name || item.SchemaHash != schemaHash).ToArray()
        }), cancellationToken);
        return await GetAsync(cancellationToken);
    }

    public async Task<GlobalSettings> SetChatAutomationAsync(ChatAutomationSettings automation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(automation);
        await repository.UpdateAsync((settings, _) => Task.FromResult(settings with { ChatAutomation = automation }), cancellationToken);
        return await GetAsync(cancellationToken);
    }

    private ConnectionSettings Normalize(ConnectionSettings item)
    {
        if (string.IsNullOrWhiteSpace(item.Name) || !Uri.TryCreate(item.BaseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(item.Model))
        {
            throw new ArgumentException("Connection name, absolute HTTP base URL, and model are required.");
        }

        if (item.ContextWindowTokens is < 1_024 or > 4_000_000
            || item.ReservedOutputTokens is < 256 or > 1_000_000)
        {
            throw new ArgumentException("Context window and reserved output overrides are outside supported limits.");
        }

        if (item.Prices is { } prices && (prices.Input is < 0 or > MaxPricePerMillion
                || prices.Output is < 0 or > MaxPricePerMillion || prices.CachedInput is < 0 or > MaxPricePerMillion))
        {
            throw new ArgumentException($"Token prices must be between 0 and {MaxPricePerMillion} per million tokens.");
        }

        var effective = contextLimits.Resolve(item);
        if (effective.ReservedOutputTokens >= effective.ContextWindowTokens)
        {
            throw new ArgumentException("Reserved output tokens must be smaller than the context window.");
        }

        return item with
        {
            Name = item.Name.Trim(),
            BaseUrl = item.BaseUrl.TrimEnd('/'),
            Model = item.Model.Trim(),
            // A rating is one of five words or nothing at all. Anything outside that is not a
            // judgement anyone made, and a stored zero would read as "the weakest there is".
            Capability = Rating(item.Capability),
            GoodFor = string.IsNullOrWhiteSpace(item.GoodFor) ? null : item.GoodFor.Trim()
        };
    }

    /// <summary>
    /// A ceiling on a price per million tokens: far above anything sold today, low enough that a
    /// price typed per token instead of per million is refused rather than stored.
    /// </summary>
    private const decimal MaxPricePerMillion = 10_000m;

    private static int? Rating(int? value) => value is >= 1 and <= 5 ? value : null;

    private static McpServerSettings Normalize(McpServerSettings item)
    {
        if (string.IsNullOrWhiteSpace(item.Name)
            || item.Transport is not ("StreamableHttp" or "Stdio" or AppMcpServer.Transport)
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
            || item.TimeoutSeconds is < 1 or > McpToolPolicySettings.MaxTimeoutSeconds)
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
