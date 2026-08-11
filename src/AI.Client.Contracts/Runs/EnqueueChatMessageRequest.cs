namespace AI.Client.Contracts.Runs;

public sealed record EnqueueChatMessageRequest(Guid OperationId, Guid MessageId, string Content, Guid? ParentMessageId, Guid BranchId);
