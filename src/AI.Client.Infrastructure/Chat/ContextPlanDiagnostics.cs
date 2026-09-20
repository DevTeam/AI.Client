namespace AI.Client.Infrastructure.Chat;

using AI.Client.Application.Chat;
using Microsoft.Extensions.Logging;

public sealed partial class ContextPlanDiagnostics(ILogger<ContextPlanDiagnostics> logger) :
    IContextPlanDiagnostics, IModelInstructionDiagnostics
{
    public void Record(string model, ContextPlan plan, int messageCount, int toolCount) =>
        ContextPlanned(logger,
            model, plan.EstimatedInputTokens, plan.InputLimit, plan.ContextWindowTokens,
            plan.ContextWindowSource.ToString(), plan.ReservedOutputTokens, plan.ReservedOutputSource.ToString(),
            plan.ToolDefinitionTokens, messageCount, toolCount, plan.WasCompacted, plan.OmittedMessages);

    public void RecordToolSelection(string model, int availableCount, int selectedCount,
        long availableTokens, long selectedTokens, long budgetTokens) =>
        ToolsSelected(logger, model, availableCount, selectedCount, availableTokens, selectedTokens, budgetTokens);

    public void RecordInstructions(string model, IReadOnlyList<string> keys, long estimatedTokens) =>
        InstructionsComposed(logger, model, keys.Count, estimatedTokens, string.Join(",", keys));

    [LoggerMessage(1001, LogLevel.Information,
        "LLM context plan for {Model}: {EstimatedInputTokens}/{InputLimit} input tokens, "
        + "context window {ContextWindowTokens} ({ContextWindowSource}), "
        + "{ReservedOutputTokens} reserved output tokens ({ReservedOutputSource}), {ToolDefinitionTokens} tool-definition tokens, "
        + "{MessageCount} messages, {ToolCount} tools, compacted={WasCompacted}, omitted={OmittedMessages}")]
    private static partial void ContextPlanned(ILogger logger, string model, long estimatedInputTokens,
        long inputLimit, long contextWindowTokens, string contextWindowSource, long reservedOutputTokens,
        string reservedOutputSource, long toolDefinitionTokens, int messageCount, int toolCount,
        bool wasCompacted, int omittedMessages);

    [LoggerMessage(1002, LogLevel.Information,
        "LLM tool selection for {Model}: {SelectedCount}/{AvailableCount} tools, "
        + "{SelectedTokens}/{BudgetTokens} selected schema tokens from {AvailableTokens} available tokens")]
    private static partial void ToolsSelected(ILogger logger, string model, int availableCount, int selectedCount,
        long availableTokens, long selectedTokens, long budgetTokens);

    [LoggerMessage(1003, LogLevel.Information,
        "LLM hidden instructions for {Model}: {InstructionCount} instructions, {EstimatedTokens} tokens, keys={InstructionKeys}")]
    private static partial void InstructionsComposed(ILogger logger, string model, int instructionCount,
        long estimatedTokens, string instructionKeys);
}
