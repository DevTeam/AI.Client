using AI.Client.Contracts.Chat;

namespace AI.Client.Web.Chat;

public interface IChatApi
{
    Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken cancellationToken);

    IAsyncEnumerable<ChatCompletionChunk> StreamAsync(ChatCompletionRequest request, CancellationToken cancellationToken);
}
