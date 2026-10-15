namespace AI.Contracts.Plans;

using System.Text.Json.Serialization;

/// <summary>Where one step of a plan stands.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ChatPlanStepStatus>))]
public enum ChatPlanStepStatus
{
    Pending,
    InProgress,
    Completed,
    Skipped,
    Failed
}

/// <summary>
/// The model's working plan for a task: ordered steps with an estimated share of the work each.
/// It is the structured result of the plan tool and lives in the branch's history, so a fork or a
/// regenerated answer carries the plan its path had, and nothing outside the history keeps a copy.
/// </summary>
/// <param name="UpdatedAt">When the plan tool last changed it.</param>
/// <param name="Clock">
/// The plan's working clock at <paramref name="UpdatedAt"/>, in seconds. It runs only while a run
/// works on the branch: time spent waiting for the person — an approval, a question — and the time
/// between turns are left out, so step times and the forecast measure work rather than waiting. Only
/// differences on it mean anything.
/// </param>
public sealed record ChatPlan(string? Title, IReadOnlyList<ChatPlanStep> Steps, DateTimeOffset UpdatedAt, double Clock = 0)
{
    /// <summary>
    /// A moment on the working clock for a wall-clock time, counted back from the last update. Used
    /// for steps stamped before the plan had a clock, as if nothing had waited since.
    /// </summary>
    public double ClockAt(DateTimeOffset time) => Clock - (UpdatedAt - time).TotalSeconds;
}

/// <param name="Weight">
/// The model's estimate of the step's share of the work, 1–10, relative to the other steps. Progress
/// is measured in weight, so a long step moves the bar further than a short one.
/// </param>
/// <param name="Progress">
/// The model's estimate of how far an in-progress step has come, 0–100. A completed step is 100, a
/// pending one 0; the value of a skipped or failed step is not counted.
/// </param>
/// <param name="Minutes">The model's estimate of the working time the step needs, in minutes; null when it gave none.</param>
/// <param name="StartedAt">Set by the application when the step first goes in progress or finishes.</param>
/// <param name="FinishedAt">Set by the application when the step is completed, skipped or failed.</param>
/// <param name="StartedClock">The plan's working clock when the step started; see <see cref="ChatPlan.Clock"/>.</param>
/// <param name="FinishedClock">The plan's working clock when the step finished.</param>
public sealed record ChatPlanStep(
    string Title,
    int Weight = 1,
    ChatPlanStepStatus Status = ChatPlanStepStatus.Pending,
    int Progress = 0,
    string? Note = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? FinishedAt = null,
    double? Minutes = null,
    double? StartedClock = null,
    double? FinishedClock = null)
{
    [JsonIgnore]
    public bool IsFinished => Status is ChatPlanStepStatus.Completed or ChatPlanStepStatus.Skipped or ChatPlanStepStatus.Failed;

    /// <summary>A skipped or failed step no longer stands between the plan and its end.</summary>
    [JsonIgnore]
    public bool IsDropped => Status is ChatPlanStepStatus.Skipped or ChatPlanStepStatus.Failed;

    /// <summary>The working time the step was expected to take, from the model's estimate.</summary>
    [JsonIgnore]
    public TimeSpan? Estimate => Minutes is { } minutes && minutes > 0 ? TimeSpan.FromMinutes(minutes) : null;

    public double? Start(ChatPlan plan) => StartedClock ?? (StartedAt is { } at ? plan.ClockAt(at) : null);

    public double? End(ChatPlan plan) => FinishedClock ?? (FinishedAt is { } at ? plan.ClockAt(at) : null);

    /// <summary>The working time the step took, or has taken so far at <paramref name="clock"/>; null until it starts.</summary>
    public TimeSpan? Spent(ChatPlan plan, double clock)
    {
        if (Start(plan) is not { } start) return null;
        var end = End(plan) ?? (Status == ChatPlanStepStatus.InProgress ? clock : start);
        return TimeSpan.FromSeconds(Math.Max(0, end - start));
    }
}

/// <summary>What the plan tool returns; the plan is what the chat renders.</summary>
public sealed record ChatPlanToolResult(
    ChatPlan? Plan,
    int Percent,
    int Completed,
    int Total,
    string Guidance,
    string? Error = null);
