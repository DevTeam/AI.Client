namespace AI.Client.Domain.Runs;

public sealed record QueuedRunMessage(Guid Id, string Content, DateTimeOffset CreatedAt, Guid? ParentMessageId = null,
    Guid? ReplaceSourceId = null);
