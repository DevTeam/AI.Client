namespace AI.Application.Tools;

using Chat;
using Contracts.Settings;

public interface IToolDefinitionSelector
{
    ToolSelection Choose(
        ConnectionSettings? connection,
        string request,
        IReadOnlyList<ChatCompletionMessage> context,
        IReadOnlyList<AgentTool> availableTools,
        IReadOnlySet<string>? pinnedTools = null);
}

public sealed record ToolSelection(
    IReadOnlyList<AgentTool> Tools,
    int AvailableCount,
    long AvailableTokens,
    long SelectedTokens,
    long BudgetTokens);
