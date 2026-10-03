namespace AI.Application.Chat;

using System.Text;

/// <summary>
/// Estimates framing and counts payload text with a recognized offline model tokenizer.
/// Unknown models use two UTF-8 bytes per estimated token, retaining a conservative fallback.
/// </summary>
public sealed class ContextTokenEstimator : IContextTokenEstimator
{
    private readonly IContextTextTokenizer? _tokenizer;
    private readonly string? _model;

    public ContextTokenEstimator(IContextTextTokenizer? tokenizer = null) => _tokenizer = tokenizer;
    private ContextTokenEstimator(IContextTextTokenizer? tokenizer, string? model) => (_tokenizer, _model) = (tokenizer, model);

    public IContextTokenEstimator ForModel(string? model) =>
        string.Equals(model, _model, StringComparison.Ordinal) ? this : new ContextTokenEstimator(_tokenizer, model);

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

    private long AddText(long estimate, string? value)
    {
        if (string.IsNullOrEmpty(value)) return estimate;
        if (_tokenizer?.TryCount(_model, value, out var tokens) == true) return Add(estimate, tokens);
        var bytes = Encoding.UTF8.GetByteCount(value);
        return Add(estimate, (bytes + 1L) / 2L);
    }

    private static long Add(long left, long right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;
}
