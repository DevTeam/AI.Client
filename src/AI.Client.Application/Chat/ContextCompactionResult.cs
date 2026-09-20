namespace AI.Client.Application.Chat;

using Contracts.Chat;

public sealed record ContextCompactionResult(
    IReadOnlyList<ChatCompletionMessage> Messages,
    int OmittedMessages,
    bool WasCompacted);
