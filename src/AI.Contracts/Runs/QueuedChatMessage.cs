// ReSharper disable NotAccessedPositionalProperty.Global

namespace AI.Contracts.Runs;

public sealed record QueuedChatMessage(Guid Id, string Content, DateTimeOffset CreatedAt,
    MessageParentMode ParentMode = MessageParentMode.BranchHead, Guid? ParentMessageId = null,
    QueuedMessageStage Stage = QueuedMessageStage.Prepared,
    IReadOnlyList<Resources.ChatResource>? Resources = null, bool IsAside = false,
    Chats.MessageSender? Sender = null);
