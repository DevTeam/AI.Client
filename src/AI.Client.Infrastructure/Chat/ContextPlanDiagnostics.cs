namespace AI.Client.Infrastructure.Chat;

using AI.Client.Application.Chat;
using Microsoft.Extensions.Logging;

public sealed partial class ContextPlanDiagnostics(ILogger<ContextPlanDiagnostics> logger) : IContextPlanDiagnostics
{
    public void Record(string model, ContextPlan plan, int messageCount, int toolCount) =>
        ContextPlanned(logger,
            model, plan.EstimatedInputTokens, plan.InputLimit, plan.ContextWindowTokens,
            plan.ContextWindowSource.ToString(), plan.ReservedOutputTokens, plan.ReservedOutputSource.ToString(),
            plan.ToolDefinitionTokens, messageCount, toolCount, plan.WasCompacted, plan.OmittedMessages);

    [LoggerMessage(1001, LogLevel.Information,
        "LLM context plan for {Model}: {EstimatedInputTokens}/{InputLimit} input tokens, "
        + "context window {ContextWindowTokens} ({ContextWindowSource}), "
        + "{ReservedOutputTokens} reserved output tokens ({ReservedOutputSource}), {ToolDefinitionTokens} tool-definition tokens, "
        + "{MessageCount} messages, {ToolCount} tools, compacted={WasCompacted}, omitted={OmittedMessages}")]
    private static partial void ContextPlanned(ILogger logger, string model, long estimatedInputTokens,
        long inputLimit, long contextWindowTokens, string contextWindowSource, long reservedOutputTokens,
        string reservedOutputSource, long toolDefinitionTokens, int messageCount, int toolCount,
        bool wasCompacted, int omittedMessages);
}
