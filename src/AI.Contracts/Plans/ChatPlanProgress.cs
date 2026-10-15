namespace AI.Contracts.Plans;

/// <summary>A step's time against the time it was expected to take.</summary>
/// <param name="Spent">The working time it took, or has taken so far; null until it starts.</param>
/// <param name="Expected">
/// What it was expected to take: for a finished step the model's estimate, for the rest the estimate
/// corrected by how the finished steps went, or the plan's pace per unit of weight when the model gave
/// none; null when there is nothing to go on yet.
/// </param>
/// <param name="IsOver">The step took, or is taking, well over what was expected: half as long again, and a minute more.</param>
public sealed record ChatPlanStepTiming(TimeSpan? Spent, TimeSpan? Expected, bool IsOver);

/// <summary>
/// How far a plan has come, measured in step weight, and how long it is likely to take still. A
/// completed step counts its whole weight, an in-progress one the share the model reported; skipped
/// and failed steps leave the measure altogether, so a plan that dropped a step can still reach its
/// end. The count of completed steps is kept apart from that: it says how many steps were done, not
/// how much work was.
/// </summary>
/// <remarks>
/// The forecast starts from the model's estimates, when it gave them, and moves towards what the work
/// has actually taken as the work gets done: at the start the estimates are all there is, near the
/// end the observed pace is. Without estimates it is the observed time per unit of weight from the
/// start. Times are on the plan's working clock (<see cref="ChatPlan.Clock"/>), so waiting for the
/// person does not slow the forecast down. The range around it narrows as the plan nears its end.
/// </remarks>
/// <param name="Fraction">Done weight over the weight still in the plan, 0–1.</param>
/// <param name="Current">The step in progress, otherwise the first one waiting; null when the plan is finished.</param>
/// <param name="Elapsed">Working time from the first step's start to now, or to the last step's end once finished.</param>
/// <param name="Remaining">The likely working time left; null for a finished plan and when nothing is known yet.</param>
public sealed record ChatPlanProgress(
    double Fraction,
    int Completed,
    int Total,
    int? Current,
    bool IsFinished,
    TimeSpan? Elapsed,
    TimeSpan? Remaining,
    TimeSpan? RemainingLow,
    TimeSpan? RemainingHigh,
    IReadOnlyList<ChatPlanStepTiming> Steps)
{
    /// <summary>How many times its expected time a step may take before it counts as over.</summary>
    public const double OverFactor = 1.5;

    /// <summary>The least overrun that counts, so a step of seconds is not flagged for taking a few more.</summary>
    public static readonly TimeSpan OverMinimum = TimeSpan.FromMinutes(1);

    public int Percent => (int)Math.Floor(Math.Clamp(Fraction, 0, 1) * 100);

    /// <param name="clock">Now, on the plan's working clock.</param>
    public static ChatPlanProgress Of(ChatPlan plan, double clock)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var steps = plan.Steps;
        double total = 0, done = 0;
        foreach (var step in steps)
        {
            if (step.IsDropped) continue;
            var weight = Weight(step);
            total += weight;
            done += step.Status switch
            {
                ChatPlanStepStatus.Completed => weight,
                ChatPlanStepStatus.InProgress => weight * Share(step),
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

        var spent = steps.Select(step => step.Spent(plan, clock)).ToArray();
        var expected = Expect(plan, spent, fraction);

        var starts = steps.Select(step => step.Start(plan)).OfType<double>().ToArray();
        var ends = steps.Select(step => step.End(plan)).OfType<double>().ToArray();
        TimeSpan? elapsed = starts.Length == 0
            ? null
            : TimeSpan.FromSeconds(Math.Max(0, (finished && ends.Length > 0 ? ends.Max() : clock) - starts.Min()));

        TimeSpan? remaining = finished ? null : TimeSpan.Zero;
        for (var index = 0; index < steps.Count && remaining is not null; index++)
        {
            var step = steps[index];
            if (step.IsFinished) continue;
            if (expected[index] is not { } expect) remaining = null;
            else if (step.Status == ChatPlanStepStatus.Pending) remaining += expect;
            else
            {
                // A step running over its time is not done the moment it reaches it: what it reports
                // still to do stands, at the pace it was expected to go.
                var left = expect - (spent[index] ?? TimeSpan.Zero);
                var reported = expect * (1 - Share(step));
                remaining += left > reported ? left : reported;
            }
        }

        // Wide while the forecast rests on estimates, narrowing as the measured work grows.
        var observed = steps.Where((step, index) => step.Status == ChatPlanStepStatus.Completed && spent[index] is not null).Any();
        var spread = 0.15 + 0.5 * (1 - fraction) + (observed ? 0 : 0.2);

        var timings = steps.Select((step, index) => new ChatPlanStepTiming(spent[index], expected[index],
            step.Status is ChatPlanStepStatus.InProgress or ChatPlanStepStatus.Completed or ChatPlanStepStatus.Failed
            && spent[index] is { } took && expected[index] is { } expect
            && took > expect * OverFactor && took - expect >= OverMinimum)).ToArray();

        return new ChatPlanProgress(fraction, steps.Count(step => step.Status == ChatPlanStepStatus.Completed),
            steps.Count, current < 0 ? null : current, finished, elapsed, remaining,
            remaining * Math.Max(0, 1 - spread), remaining * (1 + spread), timings);
    }

    /// <summary>
    /// What each step is expected to take. A finished step is held to the model's own estimate. The
    /// rest take the estimate times how much longer or shorter the finished steps ran than theirs,
    /// trusted as far as the plan has come; a step with no estimate takes its weight at the plan's
    /// pace — measured on the completed steps, else the estimated steps' minutes per weight, else the
    /// running step's own reported share.
    /// </summary>
    private static TimeSpan?[] Expect(ChatPlan plan, TimeSpan?[] spent, double fraction)
    {
        var steps = plan.Steps;
        double took = 0, estimated = 0, measuredWeight = 0, measured = 0;
        for (var index = 0; index < steps.Count; index++)
        {
            if (steps[index].Status != ChatPlanStepStatus.Completed || spent[index] is not { } time) continue;
            measured += time.TotalSeconds;
            measuredWeight += Weight(steps[index]);
            if (steps[index].Estimate is not { } estimate) continue;
            took += time.TotalSeconds;
            estimated += estimate.TotalSeconds;
        }

        var ratio = estimated > 0 ? 1 - fraction + fraction * (took / estimated) : 1;
        var withEstimates = steps.Where(step => !step.IsDropped && step.Estimate is not null).ToArray();
        double? pace = measuredWeight > 0 ? measured / measuredWeight
            : withEstimates.Length > 0
                ? withEstimates.Sum(step => step.Estimate!.Value.TotalSeconds * ratio) / withEstimates.Sum(Weight)
                : null;
        if (pace is null)
            for (var index = 0; index < steps.Count && pace is null; index++)
                if (steps[index] is { Status: ChatPlanStepStatus.InProgress } running && Share(running) > 0
                    && spent[index] is { TotalSeconds: > 0 } time)
                    pace = time.TotalSeconds / (Weight(running) * Share(running));

        return steps.Select<ChatPlanStep, TimeSpan?>(step => step.Estimate is { } estimate
                ? step.IsFinished ? estimate : estimate * ratio
                : pace is { } perWeight ? TimeSpan.FromSeconds(perWeight * Weight(step)) : null)
            .ToArray();
    }

    private static int Weight(ChatPlanStep step) => Math.Max(1, step.Weight);

    private static double Share(ChatPlanStep step) => Math.Clamp(step.Progress, 0, 100) / 100.0;
}
