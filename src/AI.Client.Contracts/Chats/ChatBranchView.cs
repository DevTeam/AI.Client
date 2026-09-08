namespace AI.Client.Contracts.Chats;

public sealed record ChatBranchView(Guid Id, Guid? HeadMessageId, string Title,
    Guid? ParentBranchId = null, Guid? RootMessageId = null, long Revision = 0);
