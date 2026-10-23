namespace AI.Application.Agents;

using AI.Contracts.Settings;

/// <summary>
/// How deep delegated work may nest. The chat's own run is at depth 0, and a run may delegate only
/// while its depth is below the limit, so the default limit of 1 lets a chat delegate and stops its
/// subtasks from delegating again.
/// </summary>
public static class SubtaskNesting
{
    /// <summary>The configured limit; anything below 1 is taken as 1, since a chat that may not delegate at all is what a Deny policy on spawn_subtask is for.</summary>
    public static int MaxDepth(GlobalSettings settings) =>
        Math.Max(1, (settings.ChatAutomation ?? new ChatAutomationSettings()).MaxSubtaskDepth);

    public static bool CanDelegate(int depth, GlobalSettings settings) => depth < MaxDepth(settings);
}
