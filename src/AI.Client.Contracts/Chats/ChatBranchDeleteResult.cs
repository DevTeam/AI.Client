namespace AI.Client.Contracts.Chats;

public sealed record ChatBranchDeleteResult(bool IsDeleted, long Revision, Guid? ParentBranchId, Guid? ParentHeadMessageId);
