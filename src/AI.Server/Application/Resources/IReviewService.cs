namespace AI.Application.Resources;

using AI.Contracts.Resources;

public interface IReviewService
{
    Task<IReadOnlyList<ChatReview>> ListAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);
    Task<ChatReview?> GetAsync(Guid projectId, Guid chatId, Guid reviewId, CancellationToken cancellationToken);
    Task<ChatReview> CreateAsync(Guid projectId, Guid chatId, CreateReviewRequest request, CancellationToken cancellationToken);
    Task<ChatReview?> UpdateAsync(Guid projectId, Guid chatId, Guid reviewId, UpdateReviewRequest request,
        CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid projectId, Guid chatId, Guid reviewId, CancellationToken cancellationToken);
    Task DeleteChatAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);
    Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken);
}
