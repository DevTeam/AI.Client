namespace AI.Client.Application.Chat;


/// <summary>Builds a smaller, request-only view of chat history without changing stored messages.</summary>
public interface IChatContextCompactor
{
    ContextCompactionResult Compact(IReadOnlyList<ChatCompletionMessage> messages, long inputLimit);

    /// <summary>
    /// Falls back to a tool-free LLM summary of the oldest turns when deterministic truncation
    /// still does not fit the request below <paramref name="inputLimit"/>. The summary is added as
    /// a user-role message so untrusted history cannot be promoted to a system instruction.
    /// </summary>
    Task<ContextCompactionResult> CompactWithLlmAsync(
        IReadOnlyList<ChatCompletionMessage> messages,
        long inputLimit,
        int targetTokens,
        IContextSummarizer summarizer,
        CancellationToken cancellationToken);
}
