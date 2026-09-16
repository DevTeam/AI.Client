namespace AI.Client.Contracts.Runs;

/// <summary>
/// What the submission should do with the branch it lands on. <see cref="SendNow"/> is the only
/// mode that acts on work already in flight: it interrupts the current run, drops the interrupted
/// command, and puts this message at the head of the queue.
/// </summary>
public enum ChatSubmitMode { Send, Queue, Fork, Replace, SendNow }

public sealed record SubmitChatMessageRequest(Guid OperationId, Guid MessageId, string Content,
    ChatSubmitMode Mode = ChatSubmitMode.Send, Guid? BranchId = null,
    MessageParentMode ParentMode = MessageParentMode.BranchHead, Guid? ParentMessageId = null,
    Guid? ReplaceSourceId = null, long? ExpectedBranchRevision = null,
    bool HoldInQueue = false);
