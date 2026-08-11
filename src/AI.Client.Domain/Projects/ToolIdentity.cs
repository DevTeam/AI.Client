using AI.Client.Domain.Common;

namespace AI.Client.Domain.Projects;

public sealed record ToolIdentity
{
    public ToolIdentity(McpServerId serverId, string name, string schemaHash)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Tool name cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(schemaHash))
        {
            throw new DomainException("Tool schema hash cannot be empty.");
        }

        ServerId = serverId;
        Name = name.Trim();
        SchemaHash = schemaHash.Trim().ToLowerInvariant();
    }

    public McpServerId ServerId { get; }

    public string Name { get; }

    public string SchemaHash { get; }
}
