namespace AI.Domain.Chats;

public sealed record ChatBranch(Guid Id, ChatMessageId? HeadMessageId, string Title,
    Guid? ParentBranchId = null, ChatMessageId? RootMessageId = null, long Revision = 0);
