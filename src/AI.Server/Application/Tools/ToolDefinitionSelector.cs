namespace AI.Application.Tools;

using System.Text.RegularExpressions;
using Chat;
using Contracts.Settings;

/// <summary>
/// Keeps tool schemas inside a bounded share of the context window. Selection is deterministic:
/// tools already used by this turn and user-facing escape hatches win, then textual relevance,
/// then smaller schemas so an oversized catalogue cannot consume the message budget by itself.
/// Within a run the list the previous step was given is kept in its order and only extended:
/// providers cache a request by its prefix, and the tools come before every message, so a list
/// that is reordered or loses a tool from one step to the next costs the whole cached conversation.
/// </summary>
public sealed partial class ToolDefinitionSelector(
    IContextTokenEstimator estimator,
    IConnectionContextLimitsResolver limitsResolver,
    IToolSelectionPriorityPolicy priorityPolicy) : IToolDefinitionSelector
{
    private const long AbsoluteBudget = 6_000;
    private const int MaximumTools = 16;

    /// <summary>How far past the budget a carried-on list may grow before it is chosen afresh.</summary>
    private const int CarriedOverPercent = 150;

    public ToolSelection Choose(ConnectionSettings? connection, string request,
        IReadOnlyList<ChatCompletionMessage> context, IReadOnlyList<AgentTool> availableTools,
        IReadOnlySet<string>? pinnedTools = null, IReadOnlyList<AgentTool>? previousTools = null)
    {
        var fresh = ChooseFresh(connection, request, context, availableTools, pinnedTools);
        if (previousTools is not { Count: > 0 }) return fresh;
        var current = availableTools.GroupBy(tool => tool.ModelDefinition.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var carried = previousTools.Select(tool => current.GetValueOrDefault(tool.ModelDefinition.Name))
            .OfType<AgentTool>().ToList();
        var names = carried.Select(tool => tool.ModelDefinition.Name).ToHashSet(StringComparer.Ordinal);
        carried.AddRange(fresh.Tools.Where(tool => names.Add(tool.ModelDefinition.Name)));
        var tokens = estimator.EstimateTools(carried.Select(tool => tool.ModelDefinition).ToArray());
        // A list that has outgrown its budget is chosen afresh: one cache miss, against a schema
        // share that would otherwise only grow for the rest of the run.
        return tokens <= Math.Max(fresh.AvailableTokens <= fresh.BudgetTokens ? fresh.AvailableTokens : 0,
                   fresh.BudgetTokens * CarriedOverPercent / 100)
               && carried.Count <= Math.Max(MaximumTools * CarriedOverPercent / 100, fresh.Tools.Count)
            ? fresh with { Tools = carried, SelectedTokens = tokens }
            : fresh;
    }

    private ToolSelection ChooseFresh(ConnectionSettings? connection, string request,
        IReadOnlyList<ChatCompletionMessage> context, IReadOnlyList<AgentTool> availableTools,
        IReadOnlySet<string>? pinnedTools)
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
