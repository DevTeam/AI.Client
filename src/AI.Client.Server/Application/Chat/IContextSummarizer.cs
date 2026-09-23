namespace AI.Client.Application.Chat;

/// <summary>
/// An isolated, tool-free summarizer used by the planner when deterministic truncation cannot
/// shrink a request below the model context limit. Implementations must not invoke any other
/// tools and must treat the prompt as data, so a malicious excerpt cannot promote itself to a
/// system instruction by hiding in compacted history.
/// </summary>
public interface IContextSummarizer
{
    Task<string> SummarizeAsync(string prompt, CancellationToken cancellationToken);
}
