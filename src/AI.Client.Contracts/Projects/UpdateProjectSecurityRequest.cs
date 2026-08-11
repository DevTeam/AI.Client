namespace AI.Client.Contracts.Projects;

public sealed record UpdateProjectSecurityRequest(
    long Revision,
    IReadOnlyList<DirectoryGrantSettings> DirectoryGrants,
    IReadOnlyList<McpServerSettings> McpServers,
    IReadOnlyList<ToolPolicySettings> ToolPolicies);
