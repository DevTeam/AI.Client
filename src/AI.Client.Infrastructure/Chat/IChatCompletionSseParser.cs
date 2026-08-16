namespace AI.Client.Infrastructure.Chat;

using AI.Client.Contracts.Chat;

public interface IChatCompletionSseParser
{
    IAsyncEnumerable<ChatCompletionChunk> ParseAsync(Stream stream, CancellationToken cancellationToken);
}
