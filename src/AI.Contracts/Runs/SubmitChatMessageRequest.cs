namespace AI.Contracts.Runs;

/// <summary>
/// What the submission should do with the branch it lands on. <see cref="SendNow"/> is the only
/// mode that promotes a new message over work already in flight. <see cref="Replace"/> also acts
/// on the selected branch atomically: it interrupts the run and abandons that branch's old queue.
/// <see cref="Aside"/> never starts a turn: a running turn reads it at its next step, an idle branch
/// just records it (docs/34-asides-and-team-messages.md).
/// </summary>
public enum ChatSubmitMode { Send, Queue, Fork, Replace, SendNow, Aside }

public sealed record SubmitChatMessageRequest(Guid OperationId, Guid MessageId, string Content,
    ChatSubmitMode Mode = ChatSubmitMode.Send, Guid? BranchId = null,
    MessageParentMode ParentMode = MessageParentMode.BranchHead, Guid? ParentMessageId = null,
    Guid? ReplaceSourceId = null, long? ExpectedBranchRevision = null,
    IReadOnlyList<Resources.ChatResource>? Resources = null,
    // The title of the branch a Fork creates; without one it is named after its first message.
    string? BranchTitle = null);
