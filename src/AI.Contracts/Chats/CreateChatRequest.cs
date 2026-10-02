namespace AI.Contracts.Chats;

public sealed record CreateChatRequest(string Title, Guid? ConnectionId = null, bool AutoTitlePending = false,
    ToolApprovalMode ApprovalMode = ToolApprovalMode.Ask, bool IsGuide = false, string GuideMode = "show");
