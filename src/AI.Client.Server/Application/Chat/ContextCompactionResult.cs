namespace AI.Client.Application.Chat;


public sealed record ContextCompactionResult(
    IReadOnlyList<ChatCompletionMessage> Messages,
    int OmittedMessages,
    bool WasCompacted);
