namespace AI.Client.Domain.Projects;

using Common;

public sealed class McpServerBinding
{
    public McpServerBinding(
        McpServerId id,
        string displayName,
        McpTransportKind transport,
        bool enabled)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new DomainException("MCP server display name cannot be empty.");
        }

        Id = id;
        DisplayName = displayName.Trim();
        Transport = transport;
        Enabled = enabled;
    }

    public McpServerId Id { get; }

    public string DisplayName { get; }

    public McpTransportKind Transport { get; }

    public bool Enabled { get; private set; }

    public void SetEnabled(bool enabled) => Enabled = enabled;
}
