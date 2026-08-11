namespace AI.Client.Contracts.Projects;

public sealed record ProjectDetails(
    Guid Id,
    string Name,
    string Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Revision,
    IReadOnlyList<DirectoryGrantSettings> DirectoryGrants,
    IReadOnlyList<McpServerSettings> McpServers,
    IReadOnlyList<ToolPolicySettings> ToolPolicies,
    IReadOnlyList<EndpointProfileSettings> EndpointProfiles,
    Guid? DefaultEndpointProfileId,
    Guid? ConnectionId = null);
