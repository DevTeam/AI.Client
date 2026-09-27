namespace AI.Application.Runs;

using AI.Domain.Runs;

public interface IChatRunRepository
{
    Task<ChatRunState?> GetAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken);
    Task SaveAsync(ChatRunState state, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatRunState>> ListAsync(CancellationToken cancellationToken);
    Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken);
    Task DeleteChatAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);
    Task DeleteExceptAsync(Guid projectId, Guid chatId, IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken);
}
