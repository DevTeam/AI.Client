namespace AI.Client.Infrastructure.Chat;

using AI.Client.Contracts.Chat;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text;

public sealed class ChatCompletionSseParser : IChatCompletionSseParser
{
    private static readonly TimeSpan StreamIdleTimeout = TimeSpan.FromSeconds(10);

    public async IAsyncEnumerable<ChatCompletionChunk> ParseAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream);
        var hasContent = false;
        var calls = new SortedDictionary<int, (StringBuilder Id, StringBuilder Name, StringBuilder Arguments)>();
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
                    if (calls.Count > 0) throw new InvalidOperationException("Tool call stream timed out before completion.");
                    yield break;
                }

                line = await reader.ReadLineAsync(cancellationToken).AsTask()
                    .WaitAsync(readTimeout, cancellationToken);
            }
            catch (TimeoutException) when (hasContent)
            {
                if (calls.Count > 0) throw new InvalidOperationException("Tool call stream timed out before completion.");
                yield break;
            }

            if (line is null)
            {
                if (calls.Count > 0) throw new InvalidOperationException("Tool call stream ended before completion.");
                yield break;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (hasContent && contentIdleTimer.Elapsed >= StreamIdleTimeout)
                {
                    if (calls.Count > 0) throw new InvalidOperationException("Tool call stream timed out before completion.");
                    yield break;
                }

                continue;
            }

            var data = line[5..].TrimStart();
            if (data == "[DONE]")
            {
                if (calls.Count > 0) throw new InvalidOperationException("Tool call stream has no completion marker.");
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
            if (choice.TryGetProperty("delta", out var toolDelta) && toolDelta.TryGetProperty("tool_calls", out var toolCalls))
            {
                foreach (var fragment in toolCalls.EnumerateArray())
                {
                    var index = fragment.GetProperty("index").GetInt32();
                    if (index is < 0 or >= 20) throw new InvalidOperationException("Too many tool calls.");
                    if (!calls.TryGetValue(index, out var call)) calls[index] = call = (new(), new(), new());
                    if (fragment.TryGetProperty("id", out var id)) call.Id.Append(id.GetString());
                    if (fragment.TryGetProperty("function", out var function))
                    {
                        if (function.TryGetProperty("name", out var name)) call.Name.Append(name.GetString());
                        if (function.TryGetProperty("arguments", out var arguments)) call.Arguments.Append(arguments.GetString());
                    }
                    if (call.Arguments.Length > 65536 || call.Id.Length > 256 || call.Name.Length > 256)
                        throw new InvalidOperationException("Tool call exceeds size limits.");
                }
                hasContent = true;
                contentIdleTimer.Restart();
            }
            if (choice.TryGetProperty("delta", out var delta)
                && delta.TryGetProperty("content", out var contentElement)
                && contentElement.ValueKind == JsonValueKind.String
                && contentElement.GetString() is { Length: > 0 } content)
            {
                hasContent = true;
                contentIdleTimer.Restart();
                yield return new ChatCompletionChunk(content, model);
            }
            if (choice.TryGetProperty("finish_reason", out var finishReason)
                && finishReason.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(finishReason.GetString()))
            {
                if (calls.Count > 0)
                {
                    if (finishReason.GetString() != "tool_calls") throw new InvalidOperationException("Incomplete tool call response.");
                    var completed = calls.Values.Select(call => new ChatToolCall(call.Id.ToString(), call.Name.ToString(), call.Arguments.ToString())).ToArray();
                    if (completed.Any(call => string.IsNullOrWhiteSpace(call.Id) || string.IsNullOrWhiteSpace(call.Name))
                        || completed.Select(call => call.Id).Distinct(StringComparer.Ordinal).Count() != completed.Length)
                        throw new InvalidOperationException("Invalid tool call identity.");
                    yield return new ChatCompletionChunk("", model, completed);
                }
                yield break;
            }


        }
    }
}
