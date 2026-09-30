namespace AI.Application.Chat;


/// <param name="Summary">
/// The model-written summary the messages open with, when one was written for this request. It is
/// returned so the caller can keep it: written once, it can stand in for the same history in every
/// request after this one instead of being written again for each of them.
/// </param>
public sealed record ContextCompactionResult(
    IReadOnlyList<ChatCompletionMessage> Messages,
    int OmittedMessages,
    bool WasCompacted,
    ContextHistorySummary? Summary = null);

/// <summary>A summary of the history up to and including <paramref name="UpToMessageId"/>.</summary>
public sealed record ContextHistorySummary(string Text, Guid UpToMessageId, int CoveredMessages, long SourceCharacters);
