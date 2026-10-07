namespace AI.Domain.Runs;

public sealed record QueuedRunMessage(Guid Id, string Content, DateTimeOffset CreatedAt,
    MessageParentMode ParentMode = MessageParentMode.BranchHead, Guid? ParentMessageId = null,
    Guid? ReplaceSourceId = null, Guid? ParentBranchId = null, long? ExpectedBranchRevision = null,
    QueuedRunStage Stage = QueuedRunStage.Prepared,
    IReadOnlyList<AI.Domain.Resources.ChatResource>? Resources = null, bool Interactive = true,
    // An aside is delivered at the running turn's next step boundary, or after its reply, and
    // never starts a turn of its own; see docs/34-asides-and-team-messages.md.
    bool IsAside = false, AI.Domain.Chats.ChatMessageSender? Sender = null, string? BranchTitle = null);
