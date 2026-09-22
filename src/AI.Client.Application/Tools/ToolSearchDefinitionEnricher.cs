namespace AI.Client.Application.Tools;

using System.Text;
using Chat;
using Contracts.Tools;

/// <summary>
/// Adds the provider-facing names of omitted, permitted tools to app_tool_search without changing
/// the MCP descriptor kept for execution, policy, or presentation. Names are data from the active
/// sessions; descriptions and schemas are deliberately not copied into this compact index.
/// </summary>
public sealed class ToolSearchDefinitionEnricher(IContextTokenEstimator estimator) : IToolSearchDefinitionEnricher
{
    private const long MaximumCatalogTokens = 2_048;
    private static readonly string SearchName = ToolRef.AppPrefix + "tool_search";

    public IReadOnlyList<AgentTool> Enrich(IReadOnlyList<AgentTool> selectedTools,
        IReadOnlyList<AgentTool> permittedTools, long schemaBudgetTokens)
    {
        ArgumentNullException.ThrowIfNull(selectedTools);
        ArgumentNullException.ThrowIfNull(permittedTools);
        var searchIndex = selectedTools.Select((tool, index) => (tool, index))
            .FirstOrDefault(item => item.tool.ModelDefinition.Name == SearchName);
        if (searchIndex.tool is null) return selectedTools;

        var selectedNames = selectedTools.Select(item => item.ModelDefinition.Name).ToHashSet(StringComparer.Ordinal);
        var omittedNames = permittedTools.Select(item => item.ModelDefinition.Name)
            .Where(name => !selectedNames.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (omittedNames.Length == 0) return selectedTools;

        var original = searchIndex.tool.ModelDefinition;
        var prefix = original.Description.TrimEnd() + "\n\n"
            + "The following permitted tools are omitted from the current callable set:\n";
        var suffix = "\nThese names are not callable yet. Pass an exact name or a capability description to "
            + "app_tool_search; matching tools become callable on the next model step.";
        var catalog = new StringBuilder(prefix);
        var baseTokens = estimator.EstimateTools(selectedTools.Select(item => item.ModelDefinition).ToArray());
        var catalogBudget = Math.Min(MaximumCatalogTokens, Math.Max(512, schemaBudgetTokens / 3));
        var maximumTotal = baseTokens + catalogBudget;
        var included = 0;
        foreach (var name in omittedNames)
        {
            var candidate = catalog.ToString() + "- " + name + "\n" + suffix;
            var definition = original with { Description = candidate };
            var projected = selectedTools.Select((tool, index) =>
                index == searchIndex.index ? definition : tool.ModelDefinition).ToArray();
            if (estimator.EstimateTools(projected) > maximumTotal) break;
            catalog.Append("- ").Append(name).Append('\n');
            included++;
        }

        if (included == 0) return selectedTools;
        if (included < omittedNames.Length)
            catalog.Append("- … and ").Append(omittedNames.Length - included).Append(" more\n");
        catalog.Append(suffix);
        var enriched = searchIndex.tool with
        {
            ModelDefinition = original with { Description = catalog.ToString() }
        };
        var result = selectedTools.ToArray();
        result[searchIndex.index] = enriched;
        return result;
    }
}
