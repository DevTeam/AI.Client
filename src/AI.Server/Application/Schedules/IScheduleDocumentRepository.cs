namespace AI.Application.Schedules;

using AI.Contracts.Schedules;

/// <summary>
/// The stored schedule of each branch, one document per branch. It knows nothing of chats: whether a
/// branch still exists is <see cref="IChatScheduleStore"/>'s question.
/// </summary>
public interface IScheduleDocumentRepository
{
    /// <summary>The branch's schedule as stored, or <c>null</c> when it has none.</summary>
    Task<ChatSchedule?> ReadAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken);

    /// <summary>Replaces the branch's schedule; a crash leaves the old one or the new one.</summary>
    Task WriteAsync(Guid projectId, Guid chatId, Guid branchId, ChatSchedule schedule, CancellationToken cancellationToken);

    Task DeleteAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken);

    /// <summary>The branches of the project that have a stored schedule, chat by chat.</summary>
    Task<IReadOnlyList<(Guid ChatId, Guid BranchId)>> ListAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>The branches of one chat that have a stored schedule.</summary>
    Task<IReadOnlyList<Guid>> ListBranchesAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);
}
