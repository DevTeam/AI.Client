namespace AI.Domain.Chats;

using AI.Domain.Projects;

public sealed record ChatBranch(Guid Id, ChatMessageId? HeadMessageId, string Title,
    Guid? ParentBranchId = null, ChatMessageId? RootMessageId = null, long Revision = 0,
    ChatBranchMember? Member = null, ChatBranchSettings? Settings = null);

public sealed record ChatBranchSettings(ConnectionId? ConnectionId = null,
    ChatApprovalMode? ApprovalMode = null, IReadOnlyList<ToolPolicy>? ToolPolicies = null);
