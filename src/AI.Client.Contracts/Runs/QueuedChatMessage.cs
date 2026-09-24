// ReSharper disable NotAccessedPositionalProperty.Global

namespace AI.Client.Contracts.Runs;

public sealed record QueuedChatMessage(Guid Id, string Content, DateTimeOffset CreatedAt,
    MessageParentMode ParentMode = MessageParentMode.BranchHead, Guid? ParentMessageId = null,
    QueuedMessageStage Stage = QueuedMessageStage.Prepared,
    IReadOnlyList<Resources.ChatResourceRef>? Resources = null);
