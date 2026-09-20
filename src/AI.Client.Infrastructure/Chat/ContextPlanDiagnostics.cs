namespace AI.Client.Infrastructure.Chat;

using AI.Client.Application.Chat;
using Microsoft.Extensions.Logging;

public sealed partial class ContextPlanDiagnostics(ILogger<ContextPlanDiagnostics> logger) : IContextPlanDiagnostics
{
    public void Record(string model, ContextPlan plan, int messageCount, int toolCount) =>
        ContextPlanned(logger,
            model, plan.EstimatedInputTokens, plan.InputLimit, plan.ReservedOutputTokens,
            plan.ToolDefinitionTokens, messageCount, toolCount, plan.WasCompacted, plan.OmittedMessages);

    [LoggerMessage(1001, LogLevel.Information,
        "LLM context plan for {Model}: {EstimatedInputTokens}/{InputLimit} input tokens, "
        + "{ReservedOutputTokens} reserved output tokens, {ToolDefinitionTokens} tool-definition tokens, "
        + "{MessageCount} messages, {ToolCount} tools, compacted={WasCompacted}, omitted={OmittedMessages}")]
    private static partial void ContextPlanned(ILogger logger, string model, long estimatedInputTokens,
        long inputLimit, long reservedOutputTokens, long toolDefinitionTokens, int messageCount,
        int toolCount, bool wasCompacted, int omittedMessages);
}
