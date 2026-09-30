namespace AI.Infrastructure.Chat;

using AI.Application.Chat;
using AI.Contracts.Chat;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text;

public sealed class ChatCompletionSseParser(IChatCompletionUsageReader usageReader) : IChatCompletionSseParser
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

    /// <summary>
    /// How long the stream is read past its finish reason for the usage report. Endpoints asked for
    /// usage send it in one more chunk right after the finish and then close; one that never does
    /// must not hold the run for longer than a blink.
    /// </summary>
    private static readonly TimeSpan UsageWait = TimeSpan.FromSeconds(2);

    public async IAsyncEnumerable<ChatCompletionChunk> ParseAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream);
        var hasContent = false;
        var calls = new SortedDictionary<int, (StringBuilder Id, StringBuilder Name, StringBuilder Arguments)>();
        var contentIdleTimer = new Stopwatch();
        var toolNameAnnounced = false;
        // Past the finish reason the answer is complete and only the usage report may still come.
        var finished = false;
        var finishTimer = new Stopwatch();
        ChatCompletionUsage? usage = null;
        string? lastModel = null;
        while (true)
        {
            string? line;
            try
            {
                var readTimeout = finished
                    ? UsageWait - finishTimer.Elapsed
                    : hasContent
                        ? StreamIdleTimeout - contentIdleTimer.Elapsed
                        : FirstTokenTimeout;
                if (finished && readTimeout <= TimeSpan.Zero) break;
                if (hasContent && readTimeout <= TimeSpan.Zero)
                {
                    if (calls.Count > 0) throw new InvalidOperationException("Tool call stream timed out before completion.");
                    break;
                }

                line = await reader.ReadLineAsync(cancellationToken).AsTask()
                    .WaitAsync(readTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                if (finished) break;
                if (!hasContent) throw new TimeoutException($"The model did not send a response within {FirstTokenTimeout.TotalSeconds:0} seconds.");
                if (calls.Count > 0) throw new InvalidOperationException("Tool call stream timed out before completion.");
                break;
            }

            if (line is null)
            {
                if (calls.Count > 0) throw new InvalidOperationException("Tool call stream ended before completion.");
                break;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (!finished && hasContent && contentIdleTimer.Elapsed >= StreamIdleTimeout)
                {
                    if (calls.Count > 0) throw new InvalidOperationException("Tool call stream timed out before completion.");
                    break;
                }

                continue;
            }

            var data = line[5..].TrimStart();
            if (data == "[DONE]")
            {
                if (calls.Count > 0) throw new InvalidOperationException("Tool call stream has no completion marker.");
                break;
            }

            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            var model = root.TryGetProperty("model", out var modelElement) ? modelElement.GetString() : null;
            lastModel = model ?? lastModel;
            // Usage comes in a chunk with no choices after the finish, or, with some endpoints, on
            // the finishing chunk itself; either way it describes the whole request.
            if (usageReader.Read(root) is { } reported) usage = reported;
            if (finished)
            {
                if (usage is not null) break;
                continue;
            }
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
                // The name usually rides on the first delta; an endpoint that sends it later gets
                // a second start carrying it, so the transcript can say which tool is coming.
                var firstName = calls.Count > 0 && calls.First().Value.Name.Length > 0
                    ? calls.First().Value.Name.ToString()
                    : null;
                if (receivedToolCallDelta && (firstToolCallDelta || (!toolNameAnnounced && firstName is not null)))
                {
                    toolNameAnnounced = firstName is not null;
                    yield return new ChatCompletionChunk("", model, ToolCallsStarted: true, ToolCallName: firstName);
                }
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
                    calls.Clear();
                    yield return new ChatCompletionChunk("", model, completed, reason);
                }
                // An empty chunk carrying nothing but the reason. The caller has already been given
                // every character of the answer; what it still lacks is whether that answer is the
                // whole one, and this is the only place the stream says so.
                else yield return new ChatCompletionChunk("", model, null, reason);
                if (usage is not null) break;
                finished = true;
                finishTimer.Start();
            }
        }

        if (usage is not null) yield return new ChatCompletionChunk("", lastModel, Usage: usage);
    }
}
