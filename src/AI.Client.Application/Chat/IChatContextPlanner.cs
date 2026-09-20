namespace AI.Client.Application.Chat;

using Contracts.Chat;
using Contracts.Settings;

public interface IChatContextPlanner
{
    ContextPlan Plan(
        ConnectionSettings? connection,
        string model,
        IReadOnlyList<ChatCompletionMessage> messages,
        IReadOnlyList<ChatToolDefinition> tools);
}
