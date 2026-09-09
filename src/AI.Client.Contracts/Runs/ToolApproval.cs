namespace AI.Client.Contracts.Runs;

public sealed record ToolApproval(
    Guid Id,
    Guid ServerId,
    string Name,
    string SchemaHash,
    string Arguments,
    long TimeoutSeconds);

public enum ToolApprovalAction
{
    Allow,
    AllowForChat,
    AllowForProject,
    AllowGlobally,
    Deny
}

public sealed record ToolApprovalDecision(Guid ApprovalId, ToolApprovalAction Action);
