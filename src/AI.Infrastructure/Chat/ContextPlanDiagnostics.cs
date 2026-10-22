namespace AI.Infrastructure.Chat;

using AI.Application.Chat;
using Microsoft.Extensions.Logging;

public sealed partial class ContextPlanDiagnostics(ILogger<ContextPlanDiagnostics> logger) :
    IContextPlanDiagnostics, IModelInstructionDiagnostics, IContextSummaryDiagnostics
{
    public void RecordSummary(string model, ContextSummaryObservation observation) =>
        SummaryWritten(logger, model, observation.Outcome, observation.Calls, observation.SourceTokens,
            observation.SentTokens, observation.ResultTokens, observation.InputLimit, observation.ElapsedMilliseconds);

    [LoggerMessage(1005, LogLevel.Information,
        "LLM context summary for {Model}: outcome={Outcome}, calls={Calls}, source={SourceTokens} tokens, "
        + "sent={SentTokens} tokens, result={ResultTokens} tokens, inputLimit={InputLimit}, elapsedMs={ElapsedMilliseconds}")]
    private static partial void SummaryWritten(ILogger logger, string model, string outcome, int calls,
        long sourceTokens, long sentTokens, long resultTokens, long inputLimit, long elapsedMilliseconds);

    public void Record(string model, ContextPlan plan, int messageCount, int toolCount) =>
        ContextPlanned(logger,
            model, plan.EstimatedInputTokens, plan.InputLimit, plan.ContextWindowTokens,
            plan.ContextWindowSource.ToString(), plan.ReservedOutputTokens, plan.ReservedOutputSource.ToString(),
            plan.ToolDefinitionTokens, messageCount, toolCount, plan.WasCompacted, plan.OmittedMessages,
            plan.FreedInputTokens, plan.CompactionReason);

    public void RecordToolSelection(string model, ToolSelection selection) =>
        ToolsSelected(logger, model, selection.AvailableCount, selection.Tools.Count,
            selection.AvailableTokens, selection.SelectedTokens, selection.BudgetTokens,
            selection.Reason, selection.AddedCount, selection.RemovedCount, selection.DefinitionChangedCount, selection.Reordered);

    public void RecordInstructions(string model, IReadOnlyList<string> keys, long estimatedTokens) =>
        InstructionsComposed(logger, model, keys.Count, estimatedTokens, string.Join(",", keys));

    public void RecordEmptyResponse(string model, int attempt, string? finishReason, int chunkCount, bool usedTools) =>
        EmptyResponse(logger, model, attempt, finishReason ?? "none", chunkCount, usedTools);

    [LoggerMessage(1001, LogLevel.Information,
        "LLM context plan for {Model}: {EstimatedInputTokens}/{InputLimit} input tokens, "
        + "context window {ContextWindowTokens} ({ContextWindowSource}), "
        + "{ReservedOutputTokens} reserved output tokens ({ReservedOutputSource}), {ToolDefinitionTokens} tool-definition tokens, "
        + "{MessageCount} messages, {ToolCount} tools, compacted={WasCompacted}, omitted={OmittedMessages}, "
        + "freed={FreedInputTokens}, reason={CompactionReason}")]
    private static partial void ContextPlanned(ILogger logger, string model, long estimatedInputTokens,
        long inputLimit, long contextWindowTokens, string contextWindowSource, long reservedOutputTokens,
        string reservedOutputSource, long toolDefinitionTokens, int messageCount, int toolCount,
        bool wasCompacted, int omittedMessages, long freedInputTokens, string compactionReason);

    [LoggerMessage(1002, LogLevel.Information,
        "LLM tool selection for {Model}: {SelectedCount}/{AvailableCount} tools, "
        + "{SelectedTokens}/{BudgetTokens} selected schema tokens from {AvailableTokens} available tokens, "
        + "reason={Reason}, added={AddedCount}, removed={RemovedCount}, definitionsChanged={DefinitionChangedCount}, reordered={Reordered}")]
    private static partial void ToolsSelected(ILogger logger, string model, int availableCount, int selectedCount,
        long availableTokens, long selectedTokens, long budgetTokens, string reason,
        int addedCount, int removedCount, int definitionChangedCount, bool reordered);

    [LoggerMessage(1003, LogLevel.Information,
        "LLM hidden instructions for {Model}: {InstructionCount} instructions, {EstimatedTokens} tokens, keys={InstructionKeys}")]
    private static partial void InstructionsComposed(ILogger logger, string model, int instructionCount,
        long estimatedTokens, string instructionKeys);

    [LoggerMessage(1004, LogLevel.Warning,
        "LLM empty response for {Model}: attempt={Attempt}, finishReason={FinishReason}, chunks={ChunkCount}, "
        + "usedTools={UsedTools}")]
    private static partial void EmptyResponse(ILogger logger, string model, int attempt, string finishReason,
        int chunkCount, bool usedTools);
}
