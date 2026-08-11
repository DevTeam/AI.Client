using AI.Client.Contracts.Chat;

namespace AI.Client.Infrastructure.Chat;

public interface IChatCompletionSseParser
{
    IAsyncEnumerable<ChatCompletionChunk> ParseAsync(Stream stream, CancellationToken cancellationToken);
}
