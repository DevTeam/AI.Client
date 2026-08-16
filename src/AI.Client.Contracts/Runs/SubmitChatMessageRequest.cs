namespace AI.Client.Contracts.Runs;

public enum ChatSubmitMode { Send, Queue, Fork, Replace }

public sealed record SubmitChatMessageRequest(Guid OperationId, Guid MessageId, string Content,
    ChatSubmitMode Mode = ChatSubmitMode.Send, Guid? BranchId = null, Guid? ParentMessageId = null,
    Guid? ReplaceSourceId = null, long? ExpectedRevision = null, bool HoldInQueue = false);
