namespace AI.Client.Application.Chat;

using Contracts.Chat;

/// <summary>The measured model input produced before transport serialization.</summary>
public sealed record ContextPlan(
    long InputLimit,
    long EstimatedInputTokens,
    long ReservedOutputTokens,
    long ToolDefinitionTokens,
    bool WasCompacted,
    int OmittedMessages,
    IReadOnlyList<ChatCompletionMessage> Messages)
{
    public bool Fits => EstimatedInputTokens <= InputLimit;
}
