namespace AI.Application.Tools;

using Chat;
using Contracts.Tools;

/// <summary>Recovery instructions reference only capabilities offered in the current request.</summary>
public interface IToolDiscoveryGuidance
{
    string? ForSelection(ToolSelection selection);
    string ForUnavailableCall(string name, IReadOnlyList<AgentTool> selected, IReadOnlyList<AgentTool> permitted);
}

public sealed class ToolDiscoveryGuidance : IToolDiscoveryGuidance
{
    private const string CurrentDefinitions =
        "Call only exact tool names shown in the current request. Names in earlier messages and summaries may be stale; "
        + "they are not the current tool catalogue. Do not invent or guess tool names.";

    public string? ForSelection(ToolSelection selection) => selection.AvailableCount > selection.Tools.Count
        ? $"Only {selection.Tools.Count} of {selection.AvailableCount} permitted tools are shown. "
          + CurrentDefinitions + " " + Discovery(selection.Tools)
        : selection.Tools.Count > 0 ? CurrentDefinitions : null;

    public string ForUnavailableCall(string name, IReadOnlyList<AgentTool> selected, IReadOnlyList<AgentTool> permitted) =>
        (permitted.Any(tool => tool.ModelDefinition.Name == name)
            ? $"Tool '{name}' is not available in this request. Its schema was omitted to fit the model's context budget. "
            : $"There is no tool named '{name}' in the permitted tool catalogue. ")
        + CurrentDefinitions + " " + Discovery(selected);

    private static string Discovery(IReadOnlyList<AgentTool> tools)
    {
        var search = tools.FirstOrDefault(tool => tool.OriginalName == "tool_search"
            && tool.ModelDefinition.Name.StartsWith(ToolRef.AppPrefix, StringComparison.Ordinal));
        return search is not null
            ? $"For a missing capability, call {search.ModelDefinition.Name} with a short English capability description "
              + "in query (for example: 'read text file', 'list directory', 'grep in files') and limit=1. "
              + "Matching permitted schemas are prioritized for the next model step when they fit; "
              + "use a returned name only after its definition is offered."
            : "No tool-discovery tool is offered in this request. Use the tools that are shown, or explain the missing capability.";
    }
}
