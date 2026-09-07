namespace AI.Client.Contracts.Chat;

public sealed record ChatCompletionMessage(string Role, string Content,
    IReadOnlyList<ChatToolCall>? ToolCalls = null, string? ToolCallId = null);
