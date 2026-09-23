namespace AI.Client.Application.Chat;

using AI.Client.Contracts.Chat;

/// <param name="FinishReason">
/// Why the endpoint stopped, as the last chunk of a stream reports it, or null when the stream
/// ended without saying. It matters because "the model has said everything it wanted to" and "the
/// model was cut off at the token ceiling" look identical from the content alone, and treating the
/// second as the first ends a run silently in the middle of an answer.
/// </param>
public sealed record ChatCompletionChunk(
    string Content,
    string? Model = null,
    IReadOnlyList<ChatToolCall>? ToolCalls = null,
    string? FinishReason = null,
    // Raised on the first streamed tool-call delta, before its arguments are complete. This lets
    // presentation code classify preceding text as a preamble without waiting for a potentially
    // large arguments payload to finish streaming.
    bool ToolCallsStarted = false);
