namespace AI.Infrastructure.Chat;

using AI.Application.Chat;
using AI.Contracts.Chat;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text;

public sealed class ChatCompletionSseParser : IChatCompletionSseParser
{
    // Once the model has started streaming, a gap this long between chunks means the connection
    // has stalled. Waiting for the very first chunk is a different situation — the endpoint may
    // legitimately spend a while "thinking" (a large tool-result context, a slow provider, a
    // reasoning model) before sending anything at all, so that wait gets a much longer allowance
    // below rather than reusing this one; conflating the two used to fail a merely-slow-to-start
    // response with a bare "The operation has timed out." after only 10 seconds.
    /// <summary>How many tool calls one assistant message may carry before the stream is rejected as malformed.</summary>
    private const int MaxParallelToolCalls = 1024;

    private static readonly TimeSpan StreamIdleTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FirstTokenTimeout = TimeSpan.FromSeconds(120);

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
                    : FirstTokenTimeout;
                if (hasContent && readTimeout <= TimeSpan.Zero)
                {
                    if (calls.Count > 0) throw new InvalidOperationException("Tool call stream timed out before completion.");
                    yield break;
                }

                line = await reader.ReadLineAsync(cancellationToken).AsTask()
                    .WaitAsync(readTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                if (!hasContent) throw new TimeoutException($"The model did not send a response within {FirstTokenTimeout.TotalSeconds:0} seconds.");
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
                var firstToolCallDelta = calls.Count == 0;
                var receivedToolCallDelta = false;
                foreach (var fragment in toolCalls.EnumerateArray())
                {
                    receivedToolCallDelta = true;
                    var index = fragment.GetProperty("index").GetInt32();
                    // A ceiling on parallel tool calls in one assistant message, not on a run: it
                    // exists to stop a malformed stream from allocating without bound, so it sits
                    // far above any plausible batch rather than anywhere near it.
                    if (index is < 0 or >= MaxParallelToolCalls) throw new InvalidOperationException("Too many tool calls.");
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
                if (firstToolCallDelta && receivedToolCallDelta)
                    yield return new ChatCompletionChunk("", model, ToolCallsStarted: true);
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
                && finishReason.GetString() is { } reason
                && !string.IsNullOrWhiteSpace(reason))
            {
                if (calls.Count > 0)
                {
                    if (reason != "tool_calls") throw new InvalidOperationException("Incomplete tool call response.");
                    var completed = calls.Values.Select(call => new ChatToolCall(call.Id.ToString(), call.Name.ToString(), call.Arguments.ToString())).ToArray();
                    if (completed.Any(call => string.IsNullOrWhiteSpace(call.Id) || string.IsNullOrWhiteSpace(call.Name))
                        || completed.Select(call => call.Id).Distinct(StringComparer.Ordinal).Count() != completed.Length)
                        throw new InvalidOperationException("Invalid tool call identity.");
                    yield return new ChatCompletionChunk("", model, completed, reason);
                }
                // An empty chunk carrying nothing but the reason. The caller has already been given
                // every character of the answer; what it still lacks is whether that answer is the
                // whole one, and this is the only place the stream says so.
                else yield return new ChatCompletionChunk("", model, null, reason);
                yield break;
            }


        }
    }
}
