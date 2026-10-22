namespace AI.Application.Plans;

using Contracts.Plans;
using Domain.Chats;

/// <summary>Finds the plan a branch's history leaves it with.</summary>
public static class ChatPlanHistory
{
    /// <summary>The ids of the plan tool calls among <paramref name="messages"/>.</summary>
    public static HashSet<string> PlanCallIds(IEnumerable<ChatMessage> messages) => messages
        .SelectMany(message => message.ToolCalls ?? [])
        .Where(call => ChatPlanResults.IsPlanCall(call.Name))
        .Select(call => call.Id)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>The newest plan along a branch's chain, oldest message first.</summary>
    public static ChatPlan? Latest(IReadOnlyList<ChatMessage> branch)
    {
        ArgumentNullException.ThrowIfNull(branch);
        var planCalls = PlanCallIds(branch);
        if (planCalls.Count == 0) return null;
        for (var index = branch.Count - 1; index >= 0; index--)
            if (branch[index] is { Role: ChatMessageRole.Tool, ToolCallId: { } id } message && planCalls.Contains(id)
                && ChatPlanResults.TryRead(message.Content) is { } plan)
                return plan;
        return null;
    }
}
