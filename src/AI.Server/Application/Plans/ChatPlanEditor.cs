namespace AI.Application.Plans;

using Contracts.Plans;

/// <summary>A step as the model writes it when it lays out a plan.</summary>
public sealed record ChatPlanStepInput(
    string Title,
    int Weight = 1,
    ChatPlanStepStatus Status = ChatPlanStepStatus.Pending,
    int? Progress = null,
    string? Note = null);

/// <summary>A change to one step of the current plan, by its number counting from one.</summary>
public sealed record ChatPlanStepUpdate(
    int Step,
    ChatPlanStepStatus? Status = null,
    int? Progress = null,
    string? Note = null);

/// <summary>
/// Applies the plan tool's edits. The model says what each step is and where it stands; the clock is
/// the application's: a step's start and finish are stamped when its status changes, so the times
/// the chat shows were measured rather than reported.
/// </summary>
public static class ChatPlanEditor
{
    public const int MaxSteps = 30;
    public const int MaxTitleLength = 160;
    public const int MaxNoteLength = 240;

    /// <summary>
    /// Lays out a new plan. A step that keeps its title from the previous plan keeps its times, so
    /// rewording the rest of a plan does not reset the clock of the work already done.
    /// </summary>
    public static ChatPlan Replace(ChatPlan? previous, string? title, IReadOnlyList<ChatPlanStepInput> steps,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count == 0) throw new ArgumentException("A plan needs at least one step.");
        if (steps.Count > MaxSteps) throw new ArgumentException($"A plan has at most {MaxSteps} steps; merge smaller ones.");
        var earlier = previous?.Steps.GroupBy(step => step.Title, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var result = new List<ChatPlanStep>(steps.Count);
        foreach (var input in steps)
        {
            var stepTitle = Text(input.Title, MaxTitleLength) ?? throw new ArgumentException("Every step needs a title.");
            var start = earlier?.GetValueOrDefault(stepTitle) is { } kept
                ? kept with { Title = stepTitle }
                : new ChatPlanStep(stepTitle);
            start = start with { Weight = Weight(input.Weight), Note = Text(input.Note, MaxNoteLength) };
            result.Add(Transition(start, input.Status, input.Progress, null, now));
        }
        return new ChatPlan(Text(title, MaxTitleLength) ?? Text(previous?.Title, MaxTitleLength), result, now);
    }

    /// <summary>Moves steps of the current plan; anything a change leaves out stays as it was.</summary>
    public static ChatPlan Update(ChatPlan current, IReadOnlyList<ChatPlanStepUpdate> updates, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(updates);
        if (updates.Count == 0) throw new ArgumentException("Name at least one step to update.");
        var steps = current.Steps.ToArray();
        foreach (var update in updates)
        {
            if (update.Step < 1 || update.Step > steps.Length)
                throw new ArgumentException($"Step {update.Step} does not exist; the plan has steps 1–{steps.Length}.");
            var step = steps[update.Step - 1];
            if (update.Note is not null) step = step with { Note = Text(update.Note, MaxNoteLength) };
            steps[update.Step - 1] = Transition(step, update.Status ?? step.Status, update.Progress,
                WorkedSince(current, now), now);
        }
        return current with { Steps = steps, UpdatedAt = now };
    }

    /// <summary>
    /// When the work on a step that finishes without ever being reported in progress can have begun:
    /// after the plan's previous change and after the last step that finished before this call. Null
    /// when nothing earlier is known, so the step shows no time rather than a made-up "0s".
    /// </summary>
    private static DateTimeOffset? WorkedSince(ChatPlan plan, DateTimeOffset now) =>
        plan.Steps.Select(step => step.FinishedAt).Append(plan.UpdatedAt).Where(at => at < now).Max();

    private static ChatPlanStep Transition(ChatPlanStep step, ChatPlanStepStatus status, int? progress,
        DateTimeOffset? since, DateTimeOffset now)
    {
        var changed = step.Status != status;
        // A step that finishes straight from waiting was worked on since the plan last moved; with
        // nothing earlier known, or already finished without a start, it has none, and so no duration.
        var start = step.StartedAt ?? (step.Status == ChatPlanStepStatus.Pending ? since : null);
        return status switch
        {
            ChatPlanStepStatus.Pending => step with { Status = status, Progress = 0, StartedAt = null, FinishedAt = null },
            ChatPlanStepStatus.InProgress => step with
            {
                Status = status,
                Progress = Math.Clamp(progress ?? (changed ? 0 : step.Progress), 0, 99),
                StartedAt = step.StartedAt ?? now,
                FinishedAt = null
            },
            ChatPlanStepStatus.Completed => step with
            {
                Status = status, Progress = 100, StartedAt = start,
                FinishedAt = changed || step.FinishedAt is null ? now : step.FinishedAt
            },
            // A step skipped before it began has no duration to show; one that failed was tried.
            _ => step with
            {
                Status = status, Progress = Math.Clamp(progress ?? step.Progress, 0, 100),
                StartedAt = status == ChatPlanStepStatus.Skipped && step.Status == ChatPlanStepStatus.Pending ? null : start,
                FinishedAt = changed || step.FinishedAt is null ? now : step.FinishedAt
            }
        };
    }

    private static int Weight(int weight) => Math.Clamp(weight, 1, 10);

    private static string? Text(string? value, int limit)
    {
        var text = value is null ? null : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length <= limit ? text : text[..(limit - 1)].TrimEnd() + "…";
    }
}
