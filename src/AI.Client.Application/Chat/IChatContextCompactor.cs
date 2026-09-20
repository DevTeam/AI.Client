namespace AI.Client.Application.Chat;

using Contracts.Chat;

/// <summary>Builds a smaller, request-only view of chat history without changing stored messages.</summary>
public interface IChatContextCompactor
{
    ContextCompactionResult Compact(IReadOnlyList<ChatCompletionMessage> messages, long inputLimit);
}
