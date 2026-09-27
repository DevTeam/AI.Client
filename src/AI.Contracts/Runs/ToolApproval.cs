namespace AI.Contracts.Runs;

/// <param name="CallIndex">Which call of the model's batch this is, counting from one.</param>
/// <param name="BatchSize">
/// How many calls that batch holds. Confirmations are asked one at a time on purpose — allowing one
/// call must never imply allowing its neighbours — but a user facing the first of several deserves
/// to know that before deciding.
/// </param>
public sealed record ToolApproval(
    Guid Id,
    Guid ServerId,
    string Name,
    string SchemaHash,
    string Arguments,
    long TimeoutSeconds,
    int CallIndex = 1,
    int BatchSize = 1);

public enum ToolApprovalAction
{
    Allow,
    AllowForChat,
    AllowForProject,
    AllowGlobally,
    Deny
}

public sealed record ToolApprovalDecision(Guid ApprovalId, ToolApprovalAction Action);
