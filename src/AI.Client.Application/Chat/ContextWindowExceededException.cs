namespace AI.Client.Application.Chat;

public sealed class ContextWindowExceededException(ContextPlan plan)
    : InvalidOperationException(
        $"The request is too large for the model context window: estimated input {plan.EstimatedInputTokens} tokens, "
        + $"input limit {plan.InputLimit}, reserved output {plan.ReservedOutputTokens}, "
        + $"tool definitions {plan.ToolDefinitionTokens}. Start a new branch, choose a model with a larger context window, "
        + "or enable fewer tools.")
{
    public ContextPlan Plan { get; } = plan;
}
