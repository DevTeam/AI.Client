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
/// <param name="UpdatedAt">When the plan tool last changed it; the end of the plan's clock for a finished plan.</param>
public sealed record ChatPlan(string? Title, IReadOnlyList<ChatPlanStep> Steps, DateTimeOffset UpdatedAt);

/// <param name="Weight">
/// The model's estimate of the step's share of the work, 1–10, relative to the other steps. Progress
/// is measured in weight, so a long step moves the bar further than a short one.
/// </param>
/// <param name="Progress">
/// The model's estimate of how far an in-progress step has come, 0–100. A completed step is 100, a
/// pending one 0; the value of a skipped or failed step is not counted.
/// </param>
/// <param name="StartedAt">Set by the application when the step first goes in progress or finishes.</param>
/// <param name="FinishedAt">Set by the application when the step is completed, skipped or failed.</param>
public sealed record ChatPlanStep(
    string Title,
    int Weight = 1,
    ChatPlanStepStatus Status = ChatPlanStepStatus.Pending,
    int Progress = 0,
    string? Note = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? FinishedAt = null)
{
    [JsonIgnore]
    public bool IsFinished => Status is ChatPlanStepStatus.Completed or ChatPlanStepStatus.Skipped or ChatPlanStepStatus.Failed;

    /// <summary>A skipped or failed step no longer stands between the plan and its end.</summary>
    [JsonIgnore]
    public bool IsDropped => Status is ChatPlanStepStatus.Skipped or ChatPlanStepStatus.Failed;

    /// <summary>How long the step took, or has taken so far; null until it starts.</summary>
    public TimeSpan? Duration(DateTimeOffset now) => StartedAt is { } started
        ? (FinishedAt ?? (Status == ChatPlanStepStatus.InProgress ? now : started)) - started
        : null;
}

/// <summary>What the plan tool returns; the plan is what the chat renders.</summary>
public sealed record ChatPlanToolResult(
    ChatPlan? Plan,
    int Percent,
    int Completed,
    int Total,
    string Guidance,
    string? Error = null);
