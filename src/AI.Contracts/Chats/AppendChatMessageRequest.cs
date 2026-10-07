namespace AI.Contracts.Chats;

public sealed record AppendChatMessageRequest(
    Guid? Id,
    Guid? ParentId,
    string Role,
    string Content,
    long Revision,
    bool IsIncomplete = false,
    Guid? BranchId = null,
    Guid? ParentBranchId = null,
    Guid? ReplaceSourceId = null,
    IReadOnlyList<Chat.ChatToolCall>? ToolCalls = null,
    string? ToolCallId = null,
    Workspace.WorkspaceChangeSet? WorkspaceChanges = null,
    IReadOnlyList<Resources.ChatResource>? Resources = null,
    MessageDelivery Delivery = MessageDelivery.Turn,
    MessageSender? Sender = null,
    string? BranchTitle = null,
    TeamMember? BranchMember = null);
