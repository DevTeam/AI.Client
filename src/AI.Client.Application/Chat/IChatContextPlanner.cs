namespace AI.Client.Application.Chat;

using Contracts.Chat;

public interface IChatContextPlanner
{
    ContextPlan Plan(
        string model,
        IReadOnlyList<ChatCompletionMessage> messages,
        IReadOnlyList<ChatToolDefinition> tools);
}
