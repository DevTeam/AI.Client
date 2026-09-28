namespace AI.Application.Chat;

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
    IReadOnlyList<ChatCompletionMessage> Messages,
    // The part of EstimatedInputTokens spent on system instructions; the rest is conversation.
    long InstructionTokens = 0,
    // Protocol framing and the tokenizer safety margin, held back like the output reserve.
    long OverheadTokens = 0)
{
    public bool Fits => EstimatedInputTokens <= InputLimit;
}
