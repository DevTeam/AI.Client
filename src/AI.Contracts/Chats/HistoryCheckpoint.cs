namespace AI.Contracts.Chats;

/// <summary>Who decided that earlier history should be summarized.</summary>
public enum HistoryCheckpointOrigin
{
    /// <summary>The person pressed Compact.</summary>
    Manual,
    /// <summary>The model called context_compact for the history.</summary>
    Model,
    /// <summary>The history no longer fit the window and was summarized to make room.</summary>
    Automatic
}

/// <summary>
/// A summary that stands in for a chat's history up to one message, in every request that
/// follows it. The stored messages and the visible transcript are untouched: the model sees the
/// summary where the covered messages would have been, and deleting the checkpoint brings them back.
/// </summary>
/// <param name="UpToMessageId">
/// The last message the summary covers. It applies to every branch whose history passes through
/// that message, so a fork taken before it keeps the full history.
/// </param>
/// <param name="CoveredMessages">How many messages the summary replaces, counting an earlier summary as one.</param>
/// <param name="SourceCharacters">The size of what was summarized, for comparing with the summary.</param>
public sealed record HistoryCheckpoint(
    Guid Id,
    Guid UpToMessageId,
    string Summary,
    int CoveredMessages,
    long SourceCharacters,
    string Model,
    DateTimeOffset CreatedAt,
    HistoryCheckpointOrigin Origin);

public enum HistoryCompactionStatus
{
    Compacted,
    /// <summary>Everything that could be summarized already is: only the recent turns are left.</summary>
    NothingToCompact,
    /// <summary>The branch is running; its history is compacted between turns, not under one.</summary>
    Busy,
    Failed
}

public sealed record HistoryCompactionResponse(HistoryCompactionStatus Status, HistoryCheckpoint? Checkpoint = null,
    string? Error = null);
