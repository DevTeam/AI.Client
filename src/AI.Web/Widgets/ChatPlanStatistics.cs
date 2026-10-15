namespace AI.Web.Widgets;

using AI.Contracts.Chats;
using AI.Contracts.Plans;
using Components;

/// <summary>One step of the plan with its place in the whole.</summary>
/// <param name="Number">The step's number from one, as the model refers to it.</param>
/// <param name="Share">
/// The step's weight over the weight still in the plan, 0–1, the measure the plan's progress uses:
/// a skipped or failed step has none, so the shares of the others add up to the whole plan.
/// </param>
/// <param name="Done">How much of the step is done, 0–1: all of a completed step, the reported part of one in progress.</param>
/// <param name="Timing">The step's working time against what it was expected to take.</param>
public sealed record ChatPlanStepEntry(int Number, ChatPlanStep Step, double Share, double Done, ChatPlanStepTiming Timing);

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
    /// <param name="working">Whether the branch's run is working: the plan's clock runs from its last update.</param>
    /// <param name="waiting">Whether that run is waiting for the person: the clock stands still.</param>
    ChatPlanStatistics Calculate(IReadOnlyList<ChatMessageView> branch, bool working, bool waiting);
}

public sealed class ChatPlanStatisticsCalculator(IChatFeedProjection feed, IChatPlanClock clock) : IChatPlanStatisticsCalculator
{
    public ChatPlanStatistics Calculate(IReadOnlyList<ChatMessageView> branch, bool working, bool waiting)
    {
        ArgumentNullException.ThrowIfNull(branch);
        if (feed.LatestPlan(branch) is not { } sighting) return ChatPlanStatistics.Empty;
        var plan = sighting.Plan;
        // A run that goes on with a plan an earlier turn left started working when its turn began.
        var turnStart = branch.LastOrDefault(message => message.StartsTurn)?.CreatedAt;
        var progress = ChatPlanProgress.Of(plan, working ? clock.Now(plan, turnStart ?? plan.UpdatedAt, waiting) : plan.Clock);
        var total = plan.Steps.Where(step => !step.IsDropped).Sum(step => Math.Max(1, step.Weight));
        var steps = plan.Steps.Select((step, index) => new ChatPlanStepEntry(index + 1, step,
            total == 0 || step.IsDropped ? 0 : Math.Max(1, step.Weight) / (double)total,
            step.Status switch
            {
                ChatPlanStepStatus.Completed => 1,
                ChatPlanStepStatus.InProgress => Math.Clamp(step.Progress, 0, 100) / 100.0,
                _ => 0
            }, progress.Steps[index])).ToArray();
        return new ChatPlanStatistics(plan, progress, steps, sighting.InLastTurn);
    }
}
