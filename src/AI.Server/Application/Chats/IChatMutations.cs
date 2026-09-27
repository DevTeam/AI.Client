namespace AI.Application.Chats;

using AI.Contracts.Chats;

/// <summary>
/// The chat writes that take part in a larger transaction. They are separate from
/// <see cref="IChatService"/> because they deliberately do not take the chat's lease: the caller
/// holds it already, so a read, a decision and a write happen as one uninterrupted step instead of
/// racing another writer in the gap between them.
/// </summary>
/// <remarks>
/// Kept out of <see cref="IChatService"/> on purpose: that contract is what the Host, the Web
/// client and the app tools see, and none of them may open a transaction. Only the run dispatcher
/// does, which is why this is the interface it depends on.
/// </remarks>
public interface IChatMutations
{
    /// <summary>
    /// Appends a message and, for a replacement, removes the abandoned tail in the same save.
    /// The caller must already hold the chat's lease.
    /// </summary>
    Task<ChatDetails?> AppendMessageCoreAsync(
        Guid projectId, Guid chatId, AppendChatMessageRequest request, IReadOnlySet<Guid> retainedMessageIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves a branch head back and drops what the abandoned attempt left behind it. The caller
    /// must already hold the chat's lease.
    /// </summary>
    Task<ChatDetails?> RewindBranchCoreAsync(
        Guid projectId, Guid chatId, Guid branchId, Guid headMessageId, IReadOnlySet<Guid> retainedMessageIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Drops every message no branch reaches any more. The caller must already hold the chat's
    /// lease.
    /// </summary>
    Task<ChatDetails?> PruneMessagesCoreAsync(
        Guid projectId, Guid chatId, IReadOnlySet<Guid> retainedMessageIds, CancellationToken cancellationToken);

    /// <summary>Deletes a chat at the revision the caller read, taking the lease itself.</summary>
    Task<ChatDeleteResult> DeleteAsync(Guid projectId, Guid chatId, long revision, CancellationToken cancellationToken);

    /// <summary>Deletes one branch at the revision the caller read, taking the lease itself.</summary>
    Task<ChatBranchDeleteResult> DeleteBranchAsync(
        Guid projectId, Guid chatId, Guid branchId, long revision, IReadOnlySet<Guid> retainedMessageIds,
        CancellationToken cancellationToken);
}
