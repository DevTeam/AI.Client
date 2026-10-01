namespace AI.Application.Tools;

using Contracts.Tools;

/// <summary>
/// Keeps the small control plane needed to discover tools, ask the user and operate the
/// application's primary project/subtask concepts in every model request. Other app tools are
/// preferred over unrelated tools when relevance is equal, while task-specific file, process,
/// web and third-party tools continue to win through textual relevance or app_tool_search.
/// </summary>
public sealed class ToolSelectionPriorityPolicy : IToolSelectionPriorityPolicy
{
    public bool IsRequired(AgentTool tool) => IsAppTool(tool) && tool.OriginalName is
        "ask_user" or
        "tool_search" or
        "context_compact" or
        "app_read" or
        "app_projects" or
        "app_security" or
        "skill_search" or
        "run_skill" or
        "spawn_subtask";

    public bool IsPreferred(AgentTool tool) => IsAppTool(tool);

    private static bool IsAppTool(AgentTool tool) =>
        tool.ModelDefinition.Name.StartsWith(ToolRef.AppPrefix, StringComparison.Ordinal);
}
