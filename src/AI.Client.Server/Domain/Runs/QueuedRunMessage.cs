namespace AI.Client.Domain.Runs;

public sealed record QueuedRunMessage(Guid Id, string Content, DateTimeOffset CreatedAt,
    MessageParentMode ParentMode = MessageParentMode.BranchHead, Guid? ParentMessageId = null,
    Guid? ReplaceSourceId = null, Guid? ParentBranchId = null, long? ExpectedBranchRevision = null,
    QueuedRunStage Stage = QueuedRunStage.Prepared,
    IReadOnlyList<AI.Client.Domain.Resources.ChatResourceRef>? Resources = null);
