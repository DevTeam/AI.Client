namespace AI.Client.Contracts.Chats;

public sealed record AppendChatMessageRequest(
    Guid? Id,
    Guid? ParentId,
    string Role,
    string Content,
    long Revision,
    bool IsIncomplete = false);
