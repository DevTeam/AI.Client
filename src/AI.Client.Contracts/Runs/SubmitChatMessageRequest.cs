namespace AI.Client.Contracts.Runs;

public enum ChatSubmitMode { Send, Queue, Fork, Replace }

public sealed record SubmitChatMessageRequest(Guid OperationId, Guid MessageId, string Content,
    ChatSubmitMode Mode = ChatSubmitMode.Send, Guid? BranchId = null,
    MessageParentMode ParentMode = MessageParentMode.BranchHead, Guid? ParentMessageId = null,
    Guid? ReplaceSourceId = null, long? ExpectedBranchRevision = null,
    bool HoldInQueue = false);
