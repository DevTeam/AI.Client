namespace AI.Application.Chat;

using Contracts.Instructions;
using Contracts.Settings;
using Tools;

/// <summary>The single policy for context-dependent budgets, priorities and cache-stable selection.</summary>
public interface IAdaptiveContextPolicy
{
    AdaptiveContextBudget Resolve(ConnectionSettings? connection);
    ModelContextPreview PrepareStanding(ModelContextPreview preview, ConnectionSettings? connection, bool appToolsAvailable);
    IReadOnlyList<ModelInstruction> SelectInstructions(IReadOnlyList<ModelInstruction> instructions, ConnectionSettings? connection);
    ToolSelection Choose(ConnectionSettings? connection, string request,
        IReadOnlyList<ChatCompletionMessage> context, IReadOnlyList<AgentTool> availableTools,
        IReadOnlySet<string>? pinnedTools = null, IReadOnlyList<AgentTool>? previousTools = null,
        long trailingInstructionTokens = 0);
}

public sealed record AdaptiveContextBudget(long ContextWindowTokens, long ReservedOutputTokens,
    long OverheadTokens, long UsableTokens, long InstructionTokens, long RunInstructionTokens,
    long ToolTokens, int MaximumTools, bool Compact);

public sealed record ToolSelection(IReadOnlyList<AgentTool> Tools, int AvailableCount,
    long AvailableTokens, long SelectedTokens, long BudgetTokens);
