namespace AI.Application.Tools;

using System.Text.RegularExpressions;
using Chat;
using Contracts.Settings;

/// <summary>
/// Keeps tool schemas inside a bounded share of the context window. Selection is deterministic:
/// tools already used by this turn and user-facing escape hatches win, then textual relevance,
/// then smaller schemas so an oversized catalogue cannot consume the message budget by itself.
/// </summary>
public sealed partial class ToolDefinitionSelector(
    IContextTokenEstimator estimator,
    IConnectionContextLimitsResolver limitsResolver,
    IToolSelectionPriorityPolicy priorityPolicy) : IToolDefinitionSelector
{
    private const long AbsoluteBudget = 6_000;
    private const int MaximumTools = 16;

    public ToolSelection Choose(ConnectionSettings? connection, string request,
        IReadOnlyList<ChatCompletionMessage> context, IReadOnlyList<AgentTool> availableTools,
        IReadOnlySet<string>? pinnedTools = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(availableTools);
        var availableTokens = estimator.EstimateTools(availableTools.Select(item => item.ModelDefinition).ToArray());
        var limits = limitsResolver.Resolve(connection);
        var budget = Math.Min(AbsoluteBudget, Math.Max(1_024, limits.ContextWindowTokens / 4));
        if (availableTokens <= budget)
            return new ToolSelection(availableTools, availableTools.Count, availableTokens, availableTokens, budget);

        var query = Words(request).Concat(context.Where(item => item.Role == "user").TakeLast(2)
            .SelectMany(item => Words(item.ForModel))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var used = context.SelectMany(item => item.ToolCalls ?? [])
            .Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var candidates = availableTools.Select(tool =>
        {
            var definition = tool.ModelDefinition;
            var words = Words(definition.Name).Concat(Words(definition.Description)).Distinct(StringComparer.OrdinalIgnoreCase);
            var relevance = words.Count(query.Contains);
            var pinned = used.Contains(definition.Name) || pinnedTools?.Contains(definition.Name) == true
                || priorityPolicy.IsRequired(tool);
            var tokens = estimator.EstimateTools([definition]);
            return new Candidate(tool, tokens, relevance, pinned, priorityPolicy.IsPreferred(tool));
        }).OrderByDescending(item => item.Pinned)
          .ThenByDescending(item => item.Relevance)
          .ThenByDescending(item => item.Preferred)
          .ThenBy(item => item.Tokens)
          .ThenBy(item => item.Tool.ModelDefinition.Name, StringComparer.Ordinal)
          .ToArray();

        var selected = new List<AgentTool>();
        long selectedTokens = 0;
        foreach (var candidate in candidates)
        {
            if (selected.Count >= MaximumTools && !candidate.Pinned) continue;
            if (!candidate.Pinned && selectedTokens + candidate.Tokens > budget) continue;
            selected.Add(candidate.Tool);
            selectedTokens = SaturatingAdd(selectedTokens, candidate.Tokens);
        }

        return new ToolSelection(selected, availableTools.Count, availableTokens, selectedTokens, budget);
    }

    private static IEnumerable<string> Words(string? value) => string.IsNullOrWhiteSpace(value)
        ? []
        : WordPattern().Matches(value).Select(match => match.Value.ToLowerInvariant()).Where(word => word.Length >= 3);

    private static long SaturatingAdd(long left, long right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;

    [GeneratedRegex("[\\p{L}\\p{N}_-]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    private sealed record Candidate(AgentTool Tool, long Tokens, int Relevance, bool Pinned, bool Preferred);
}
