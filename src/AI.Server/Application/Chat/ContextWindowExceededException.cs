namespace AI.Application.Chat;

public sealed class ContextWindowExceededException(ContextPlan plan)
    : InvalidOperationException(
        $"The request is too large for the model context window{Compaction(plan)}: "
        + $"estimated input {plan.EstimatedInputTokens} tokens, "
        + $"input limit {plan.InputLimit}, reserved output {plan.ReservedOutputTokens}, "
        + $"tool definitions {plan.ToolDefinitionTokens}, over by {Math.Max(0, plan.EstimatedInputTokens - plan.InputLimit)}. "
        + "Choose a model with a larger context window, reduce the request, or make fewer tools available.")
{
    public ContextPlan Plan { get; } = plan;

    private static string Compaction(ContextPlan value) => value.WasCompacted
        ? $" after compaction (omitted {value.OmittedMessages} messages)"
        : string.Empty;
}
