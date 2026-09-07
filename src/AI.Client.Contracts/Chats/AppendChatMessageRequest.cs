namespace AI.Client.Contracts.Chats;

public sealed record AppendChatMessageRequest(
    Guid? Id,
    Guid? ParentId,
    string Role,
    string Content,
    long Revision,
    bool IsIncomplete = false,
    Guid? BranchId = null,
    Guid? ReplaceSourceId = null,
    IReadOnlyList<Chat.ChatToolCall>? ToolCalls = null,
    string? ToolCallId = null);
