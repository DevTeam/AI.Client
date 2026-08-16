namespace AI.Client.Infrastructure.Chat;

using AI.Client.Contracts.Chat;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

public sealed class ChatCompletionSseParser : IChatCompletionSseParser
{
    private static readonly TimeSpan StreamIdleTimeout = TimeSpan.FromSeconds(10);

    public async IAsyncEnumerable<ChatCompletionChunk> ParseAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream);
        var hasContent = false;
        var contentIdleTimer = new Stopwatch();
        while (true)
        {
            string? line;
            try
            {
                var readTimeout = hasContent
                    ? StreamIdleTimeout - contentIdleTimer.Elapsed
                    : StreamIdleTimeout;
                if (hasContent && readTimeout <= TimeSpan.Zero)
                {
                    yield break;
                }

                line = await reader.ReadLineAsync(cancellationToken).AsTask()
                    .WaitAsync(readTimeout, cancellationToken);
            }
            catch (TimeoutException) when (hasContent)
            {
                yield break;
            }

            if (line is null)
            {
                yield break;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (hasContent && contentIdleTimer.Elapsed >= StreamIdleTimeout)
                {
                    yield break;
                }

                continue;
            }

            var data = line[5..].TrimStart();
            if (data == "[DONE]")
            {
                yield break;
            }

            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            var model = root.TryGetProperty("model", out var modelElement) ? modelElement.GetString() : null;
            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            {
                continue;
            }

            var choice = choices[0];
            if (choice.TryGetProperty("finish_reason", out var finishReason)
                && finishReason.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(finishReason.GetString()))
            {
                yield break;
            }

            if (!choice.TryGetProperty("delta", out var delta)
                || !delta.TryGetProperty("content", out var contentElement)
                || contentElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var content = contentElement.GetString();
            if (string.IsNullOrEmpty(content))
            {
                continue;
            }

            hasContent = true;
            contentIdleTimer.Restart();
            yield return new ChatCompletionChunk(content, model);
        }
    }
}
