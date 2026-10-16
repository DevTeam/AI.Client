namespace AI.Contracts.Plans;

using System.Text.Json.Serialization;

/// <summary>Where one criterion of done stands.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ChatPlanCriterionStatus>))]
public enum ChatPlanCriterionStatus
{
    /// <summary>Not checked yet, or could not be checked.</summary>
    Unverified,
    Met,
    NotMet,

    /// <summary>A general rule — a project's definition of done — that does not concern this task.</summary>
    NotApplicable
}

/// <summary>
/// One thing that has to be true for the task to count as done ("the tests pass", "both themes
/// checked"), with what showed it. The model names the criteria when it lays out the plan and
/// settles each before its final answer; a criterion it could not check stays unverified and says so,
/// rather than being called met.
/// </summary>
/// <param name="Evidence">
/// What showed it — "1308 of 1309 tests passed" — or, for one not met or not applicable, why. Required
/// for every status but <see cref="ChatPlanCriterionStatus.Unverified"/>.
/// </param>
/// <param name="EvidenceCall">The tool call whose result shows it, when one does; the chat links to it.</param>
public sealed record ChatPlanCriterion(
    string Text,
    ChatPlanCriterionStatus Status = ChatPlanCriterionStatus.Unverified,
    string? Evidence = null,
    string? EvidenceCall = null);

/// <summary>How the plan's criteria stand; a criterion that does not apply counts for nothing.</summary>
public sealed record ChatPlanCriteriaSummary(int Met, int NotMet, int Unverified, int NotApplicable)
{
    /// <summary>The criteria that apply to the task.</summary>
    public int Total => Met + NotMet + Unverified;

    public bool Any => Total + NotApplicable > 0;

    public static ChatPlanCriteriaSummary Of(ChatPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var criteria = plan.Criteria ?? [];
        return new ChatPlanCriteriaSummary(
            criteria.Count(item => item.Status == ChatPlanCriterionStatus.Met),
            criteria.Count(item => item.Status == ChatPlanCriterionStatus.NotMet),
            criteria.Count(item => item.Status == ChatPlanCriterionStatus.Unverified),
            criteria.Count(item => item.Status == ChatPlanCriterionStatus.NotApplicable));
    }
}
