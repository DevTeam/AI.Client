namespace AI.Client.Domain.Projects;

using Common;

public sealed class Project
{
    private readonly Dictionary<DirectoryGrantId, DirectoryGrant> _directoryGrants = [];
    private readonly Dictionary<McpServerId, McpServerBinding> _mcpServers = [];
    private readonly Dictionary<ToolIdentity, ToolPolicy> _toolPolicies = [];

    public Project(
        ProjectId id,
        string name,
        string? description,
        DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Project name cannot be empty.");
        }

        Id = id;
        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public ProjectId Id { get; }

    public string Name { get; private set; }

    public string Description { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<DirectoryGrant> DirectoryGrants => _directoryGrants.Values;

    public IReadOnlyCollection<McpServerBinding> McpServers => _mcpServers.Values;

    public IReadOnlyCollection<ToolPolicy> ToolPolicies => _toolPolicies.Values;



    public ConnectionId? ConnectionId { get; private set; }

    public void SetConnection(ConnectionId? connectionId, DateTimeOffset updatedAt)
    {
        EnsureTimestampDoesNotMoveBackwards(updatedAt);
        ConnectionId = connectionId;
        UpdatedAt = updatedAt;
    }





    public void UpdateDetails(string name, string? description, DateTimeOffset updatedAt)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Project name cannot be empty.");
        }

        EnsureTimestampDoesNotMoveBackwards(updatedAt);
        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
        UpdatedAt = updatedAt;
    }

    public void AddDirectoryGrant(DirectoryGrant grant, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(grant);
        EnsureTimestampDoesNotMoveBackwards(updatedAt);

        if (!_directoryGrants.TryAdd(grant.Id, grant))
        {
            throw new DomainException($"Directory grant '{grant.Id}' already exists.");
        }

        UpdatedAt = updatedAt;
    }

    public void AddMcpServer(McpServerBinding server, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(server);
        EnsureTimestampDoesNotMoveBackwards(updatedAt);

        if (!_mcpServers.TryAdd(server.Id, server))
        {
            throw new DomainException($"MCP server '{server.Id}' already exists.");
        }

        UpdatedAt = updatedAt;
    }

    public void SetToolPolicy(ToolPolicy policy, DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(policy);
        EnsureTimestampDoesNotMoveBackwards(updatedAt);

        if (!_mcpServers.ContainsKey(policy.Tool.ServerId))
        {
            throw new DomainException(
                $"MCP server '{policy.Tool.ServerId}' must be connected before a tool policy is configured.");
        }

        foreach (var staleIdentity in _toolPolicies.Keys
                     .Where(i => i.ServerId == policy.Tool.ServerId
                                 && string.Equals(i.Name, policy.Tool.Name, StringComparison.Ordinal)
                                 && !string.Equals(i.SchemaHash, policy.Tool.SchemaHash, StringComparison.Ordinal))
                     .ToArray())
        {
            _toolPolicies.Remove(staleIdentity);
        }

        _toolPolicies[policy.Tool] = policy;
        UpdatedAt = updatedAt;
    }

    public void ReplaceSecuritySettings(
        IEnumerable<DirectoryGrant> directoryGrants,
        IEnumerable<McpServerBinding> mcpServers,
        IEnumerable<ToolPolicy> toolPolicies,
        DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(directoryGrants);
        ArgumentNullException.ThrowIfNull(mcpServers);
        ArgumentNullException.ThrowIfNull(toolPolicies);
        EnsureTimestampDoesNotMoveBackwards(updatedAt);

        var grants = directoryGrants.ToDictionary(grant => grant.Id);
        var servers = mcpServers.ToDictionary(server => server.Id);
        var policies = toolPolicies.ToDictionary(policy => policy.Tool);
        // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
        foreach (var policy in policies.Values)
        {
            if (!servers.ContainsKey(policy.Tool.ServerId))
            {
                throw new DomainException(
                    $"MCP server '{policy.Tool.ServerId}' must be connected before a tool policy is configured.");
            }
        }

        _directoryGrants.Clear();
        _mcpServers.Clear();
        _toolPolicies.Clear();
        foreach (var grant in grants)
        {
            _directoryGrants.Add(grant.Key, grant.Value);
        }

        foreach (var server in servers)
        {
            _mcpServers.Add(server.Key, server.Value);
        }

        foreach (var policy in policies)
        {
            _toolPolicies.Add(policy.Key, policy.Value);
        }

        UpdatedAt = updatedAt;
    }

    private void EnsureTimestampDoesNotMoveBackwards(DateTimeOffset updatedAt)
    {
        if (updatedAt < UpdatedAt)
        {
            throw new DomainException("Project update timestamp cannot move backwards.");
        }
    }
}
