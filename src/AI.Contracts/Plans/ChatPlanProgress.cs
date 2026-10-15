namespace AI.Contracts.Plans;

/// <summary>
/// How far a plan has come, measured in step weight. A completed step counts its whole weight, an
/// in-progress one the share the model reported; skipped and failed steps leave the measure
/// altogether, so a plan that dropped a step can still reach its end. The count of completed steps
/// is kept apart from that: it says how many steps were done, not how much work was.
/// </summary>
/// <param name="Fraction">Done weight over the weight still in the plan, 0–1.</param>
/// <param name="Current">The step in progress, otherwise the first one waiting; null when the plan is finished.</param>
/// <param name="Remaining">
/// The time left at the pace so far: the plan's elapsed time per unit of done weight, applied to the
/// weight still to do. Null until some weight is done, and for a finished plan.
/// </param>
public sealed record ChatPlanProgress(
    double Fraction,
    int Completed,
    int Total,
    int? Current,
    bool IsFinished,
    TimeSpan? Elapsed,
    TimeSpan? Remaining)
{
    public int Percent => (int)Math.Floor(Math.Clamp(Fraction, 0, 1) * 100);

    public static ChatPlanProgress Of(ChatPlan plan, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var steps = plan.Steps;
        double total = 0, done = 0;
        foreach (var step in steps)
        {
            if (step.IsDropped) continue;
            var weight = Math.Max(1, step.Weight);
            total += weight;
            done += step.Status switch
            {
                ChatPlanStepStatus.Completed => weight,
                ChatPlanStepStatus.InProgress => weight * Math.Clamp(step.Progress, 0, 100) / 100.0,
                _ => 0
            };
        }

        var finished = steps.Count > 0 && steps.All(step => step.IsFinished);
        var current = -1;
        for (var index = 0; index < steps.Count && current < 0; index++)
            if (steps[index].Status == ChatPlanStepStatus.InProgress) current = index;
        for (var index = 0; index < steps.Count && current < 0; index++)
            if (steps[index].Status == ChatPlanStepStatus.Pending) current = index;

        var fraction = total > 0 ? done / total : finished ? 1 : 0;
        var started = steps.Where(step => step.StartedAt is not null).Select(step => step.StartedAt!.Value)
            .DefaultIfEmpty().Min();
        TimeSpan? elapsed = started == default ? null : (finished ? plan.UpdatedAt : now) - started;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        TimeSpan? remaining = !finished && elapsed is { } spent && done > 0 && total > done
            ? TimeSpan.FromTicks((long)(spent.Ticks * (total - done) / done))
            : null;

        return new ChatPlanProgress(fraction, steps.Count(step => step.Status == ChatPlanStepStatus.Completed),
            steps.Count, current < 0 ? null : current, finished, elapsed, remaining);
    }
}
