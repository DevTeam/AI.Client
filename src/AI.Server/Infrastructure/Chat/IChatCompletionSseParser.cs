namespace AI.Infrastructure.Chat;

using AI.Application.Chat;

public interface IChatCompletionSseParser
{
    IAsyncEnumerable<ChatCompletionChunk> ParseAsync(Stream stream, CancellationToken cancellationToken);
}
