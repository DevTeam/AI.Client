namespace AI.Client.Application.Chat;

using System.Text;
using Contracts.Chat;

/// <summary>
/// Provider-independent conservative estimator. It uses two UTF-8 bytes per estimated token,
/// rather than the optimistic four characters commonly used for English prose, so code, JSON and
/// non-ASCII text retain a substantial safety margin without making normal tool schemas unusable.
/// </summary>
public sealed class ContextTokenEstimator : IContextTokenEstimator
{
    public long EstimateMessages(IReadOnlyList<ChatCompletionMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        long estimate = 0;
        foreach (var message in messages)
        {
            estimate = Add(estimate, 12); // role/content keys and message delimiters
            estimate = AddText(estimate, message.Role);
            estimate = AddText(estimate, message.ForModel);
            estimate = AddText(estimate, message.ToolCallId);
            foreach (var call in message.ToolCalls ?? [])
            {
                estimate = Add(estimate, 20); // call envelope and function/type keys
                estimate = AddText(estimate, call.Id);
                estimate = AddText(estimate, call.Name);
                estimate = AddText(estimate, call.Arguments);
            }
        }

        return estimate;
    }

    public long EstimateTools(IReadOnlyList<ChatToolDefinition> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        long estimate = 0;
        foreach (var tool in tools)
        {
            estimate = Add(estimate, 24); // tool/function/parameters envelope
            estimate = AddText(estimate, tool.Name);
            estimate = AddText(estimate, tool.Description);
            estimate = AddText(estimate, tool.InputSchema.GetRawText());
        }

        return estimate;
    }

    private static long AddText(long estimate, string? value)
    {
        if (string.IsNullOrEmpty(value)) return estimate;
        var bytes = Encoding.UTF8.GetByteCount(value);
        return Add(estimate, (bytes + 1L) / 2L);
    }

    private static long Add(long left, long right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;
}
