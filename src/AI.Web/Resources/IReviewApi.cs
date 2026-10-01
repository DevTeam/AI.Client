namespace AI.Web.Resources;

using AI.Contracts.Resources;

public interface IReviewApi
{
    Task<IReadOnlyList<ChatReview>> ListAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);
    Task<ChatReview?> GetAsync(Guid projectId, Guid chatId, Guid reviewId, CancellationToken cancellationToken);
    Task<ChatReview> CreateAsync(Guid projectId, Guid chatId, CreateReviewRequest request, CancellationToken cancellationToken);
    Task<ChatReview> UpdateAsync(Guid projectId, Guid chatId, Guid reviewId, UpdateReviewRequest request,
        CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid projectId, Guid chatId, Guid reviewId, CancellationToken cancellationToken);

    /// <summary>The Host's draft of the comment being written, or null when it has none to offer.</summary>
    Task<ReviewCommentSuggestion?> SuggestCommentAsync(Guid projectId, Guid chatId, ReviewCommentSuggestionRequest request,
        CancellationToken cancellationToken);
}
