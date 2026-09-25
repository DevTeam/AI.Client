namespace AI.Client.Web.Resources;

using AI.Client.Contracts.Resources;

public interface IReviewApi
{
    Task<IReadOnlyList<ChatReview>> ListAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);
    Task<ChatReview?> GetAsync(Guid projectId, Guid chatId, Guid reviewId, CancellationToken cancellationToken);
    Task<ChatReview> CreateAsync(Guid projectId, Guid chatId, CreateReviewRequest request, CancellationToken cancellationToken);
    Task<ChatReview> UpdateAsync(Guid projectId, Guid chatId, Guid reviewId, UpdateReviewRequest request,
        CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid projectId, Guid chatId, Guid reviewId, CancellationToken cancellationToken);
}
