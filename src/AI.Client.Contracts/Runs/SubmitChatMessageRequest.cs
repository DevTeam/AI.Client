namespace AI.Client.Contracts.Runs;

/// <summary>
/// What the submission should do with the branch it lands on. <see cref="SendNow"/> is the only
/// mode that promotes a new message over work already in flight. <see cref="Replace"/> also acts
/// on the selected branch atomically: it interrupts the run and abandons that branch's old queue.
/// </summary>
public enum ChatSubmitMode { Send, Queue, Fork, Replace, SendNow }

public sealed record SubmitChatMessageRequest(Guid OperationId, Guid MessageId, string Content,
    ChatSubmitMode Mode = ChatSubmitMode.Send, Guid? BranchId = null,
    MessageParentMode ParentMode = MessageParentMode.BranchHead, Guid? ParentMessageId = null,
    Guid? ReplaceSourceId = null, long? ExpectedBranchRevision = null);
