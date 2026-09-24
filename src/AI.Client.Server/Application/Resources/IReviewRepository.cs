namespace AI.Client.Application.Resources;

using AI.Client.Contracts.Resources;

public interface IReviewRepository
{
    Task<IReadOnlyList<ChatReview>> ListAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);
    Task<ChatReview> CreateAsync(ChatReview review, CancellationToken cancellationToken);
    Task<ChatReview?> UpdateAsync(Guid projectId, Guid chatId, Guid reviewId, long expectedRevision,
        Func<ChatReview, ChatReview> update, CancellationToken cancellationToken);
    Task DeleteChatAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);
    Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken);
}
