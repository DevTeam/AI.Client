namespace AI.Client.Application.Chat;

using Contracts.Chat;
using Contracts.Settings;

/// <summary>The measured model input produced before transport serialization.</summary>
public sealed record ContextPlan(
    long InputLimit,
    long EstimatedInputTokens,
    long ReservedOutputTokens,
    long ToolDefinitionTokens,
    long ContextWindowTokens,
    ContextLimitSource ContextWindowSource,
    ContextLimitSource ReservedOutputSource,
    bool WasCompacted,
    int OmittedMessages,
    IReadOnlyList<ChatCompletionMessage> Messages)
{
    public bool Fits => EstimatedInputTokens <= InputLimit;
}
