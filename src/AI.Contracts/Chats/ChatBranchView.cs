namespace AI.Contracts.Chats;

public sealed record ChatBranchView(Guid Id, Guid? HeadMessageId, string Title,
    Guid? ParentBranchId = null, Guid? RootMessageId = null, long Revision = 0, TeamMember? Member = null,
    BranchSettings? Settings = null);

public sealed record BranchSettings(Guid? ConnectionId = null, ToolApprovalMode? ApprovalMode = null,
    IReadOnlyList<AI.Contracts.Projects.ToolPolicySettings>? ToolPolicies = null);

public sealed record UpdateBranchSettingsRequest(BranchSettings Settings);
