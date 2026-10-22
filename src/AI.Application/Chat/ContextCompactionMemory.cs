namespace AI.Application.Chat;

/// <summary>
/// What the deterministic compaction decided for the previous request of a run. A run only appends
/// to its context, so when the next request still starts with the previous input, the previous
/// result plus the new messages is the same compaction carried on: its cut, its trimmed tool
/// results and its digests stay as they were, and so does the prefix the provider has cached.
/// Computed afresh at every step, the cut would move and the trims tighten, and every request
/// would start differently.
/// </summary>
public sealed class ContextCompactionMemory
{
    private IReadOnlyList<ChatCompletionMessage>? _input;
    private ContextCompactionResult? _result;

    /// <summary>The previous result continued with what <paramref name="messages"/> added, or null.</summary>
    public ContextCompactionResult? Continue(IReadOnlyList<ChatCompletionMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (_input is not { } input || _result is not { } result || messages.Count < input.Count) return null;
        for (var index = 0; index < input.Count; index++)
            if (!ReferenceEquals(messages[index], input[index]) && !messages[index].Equals(input[index]))
                return null;
        return result with
        {
            Messages = result.Messages.Concat(messages.Skip(input.Count)).ToArray(),
            Summary = null
        };
    }

    public void Remember(IReadOnlyList<ChatCompletionMessage> input, ContextCompactionResult result)
    {
        _input = input.ToArray();
        _result = result;
    }

    public void Forget()
    {
        _input = null;
        _result = null;
    }
}
