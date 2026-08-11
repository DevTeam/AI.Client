namespace AI.Client.Contracts.Runs;

public sealed record ChatRunSnapshot(Guid ProjectId, Guid ChatId, Guid BranchId, ChatRunStatus Status, string StreamingContent,
    IReadOnlyList<QueuedChatMessage> Queue, bool HasUnreadResponse, string? Error, long Revision);
