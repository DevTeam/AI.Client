namespace AI.Client.Contracts.Runs;

public sealed record ToolApproval(Guid Id, string Name, string Arguments, long TimeoutSeconds);
public sealed record ToolApprovalDecision(Guid ApprovalId, bool Allow);
