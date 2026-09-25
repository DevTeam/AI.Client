namespace AI.Client.Web.Resources;

using System.Net.Http.Json;
using AI.Client.Contracts.Resources;

public sealed class ReviewApi(HttpClient http) : IReviewApi
{
    public async Task<IReadOnlyList<ChatReview>> ListAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken) =>
        await http.GetFromJsonAsync<ChatReview[]>($"api/projects/{projectId}/chats/{chatId}/reviews", cancellationToken) ?? [];

    public async Task<ChatReview?> GetAsync(Guid projectId, Guid chatId, Guid reviewId, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync($"api/projects/{projectId}/chats/{chatId}/reviews/{reviewId}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ChatReview>(cancellationToken);
    }

    public async Task<ChatReview> CreateAsync(Guid projectId, Guid chatId, CreateReviewRequest request, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync($"api/projects/{projectId}/chats/{chatId}/reviews", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ChatReview>(cancellationToken)
            ?? throw new InvalidOperationException("Review service returned no review.");
    }

    public async Task<ChatReview> UpdateAsync(Guid projectId, Guid chatId, Guid reviewId, UpdateReviewRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await http.PutAsJsonAsync($"api/projects/{projectId}/chats/{chatId}/reviews/{reviewId}", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ChatReview>(cancellationToken)
            ?? throw new InvalidOperationException("Review service returned no review.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Review request failed ({(int)response.StatusCode}): {body[..Math.Min(body.Length, 600)]}");
    }
}
