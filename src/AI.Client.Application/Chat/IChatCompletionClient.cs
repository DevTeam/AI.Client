namespace AI.Client.Application.Chat;

using AI.Client.Contracts.Chat;

public interface IChatCompletionClient
{
    Task<ChatCompletionResponse> CompleteAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken);

    IAsyncEnumerable<ChatCompletionChunk> StreamAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken);
}
