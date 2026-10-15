namespace AI.Web.Widgets;

using AI.Contracts.Chats;
using AI.Contracts.Plans;
using Components;

/// <summary>One step of the plan with its place in the whole.</summary>
/// <param name="Number">The step's number from one, as the model refers to it.</param>
/// <param name="Share">The step's weight over the weight of every step, 0–1; dropped steps keep theirs, so the rows add up.</param>
/// <param name="Done">How much of the step is done, 0–1: all of a completed step, the reported part of one in progress.</param>
public sealed record ChatPlanStepEntry(int Number, ChatPlanStep Step, double Share, double Done);

/// <summary>The branch's plan and how far it has come, for the Plan widget.</summary>
/// <param name="InLastTurn">Whether the branch's last turn moved the plan; an older one may be set aside.</param>
public sealed record ChatPlanStatistics(
    ChatPlan? Plan,
    ChatPlanProgress? Progress,
    IReadOnlyList<ChatPlanStepEntry> Steps,
    bool InLastTurn)
{
    public static ChatPlanStatistics Empty { get; } = new(null, null, [], false);

    public bool HasPlan => Plan is not null;
}

public interface IChatPlanStatisticsCalculator
{
    /// <param name="branch">The visible branch, root to leaf.</param>
    /// <param name="now">The clock the elapsed time and the estimate are measured against.</param>
    ChatPlanStatistics Calculate(IReadOnlyList<ChatMessageView> branch, DateTimeOffset now);
}

public sealed class ChatPlanStatisticsCalculator(IChatFeedProjection feed) : IChatPlanStatisticsCalculator
{
    public ChatPlanStatistics Calculate(IReadOnlyList<ChatMessageView> branch, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(branch);
        if (feed.LatestPlan(branch) is not { } sighting) return ChatPlanStatistics.Empty;
        var plan = sighting.Plan;
        var total = plan.Steps.Sum(step => Math.Max(1, step.Weight));
        var steps = plan.Steps.Select((step, index) => new ChatPlanStepEntry(index + 1, step,
            total == 0 ? 0 : Math.Max(1, step.Weight) / (double)total,
            step.Status switch
            {
                ChatPlanStepStatus.Completed => 1,
                ChatPlanStepStatus.InProgress => Math.Clamp(step.Progress, 0, 100) / 100.0,
                _ => 0
            })).ToArray();
        return new ChatPlanStatistics(plan, ChatPlanProgress.Of(plan, now), steps, sighting.InLastTurn);
    }
}
