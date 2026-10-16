namespace AI.Mcp.App;

using AI.Application.Plans;
using AI.Application.Tools;
using AI.Contracts.Plans;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

/// <summary>
/// Lays out and moves the model's plan for the task in hand. The plan is the tool's result: it is
/// stored with the branch's history like any other result, which is what the chat renders and what
/// the next run reads back. The run's working copy only spares each call a read of the whole chat.
/// </summary>
[McpServerToolType]
public sealed class AppPlanTool(IChatPlans plans) : IAppTool
{
    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) => new Session(plans, run, reply).Create();

    private sealed class Session(IChatPlans plans, ToolRunContext run, IAppToolReply reply)
    {
        public McpServerTool Create() => McpServerTool.Create(Execute,
            new McpServerToolCreateOptions
            {
                SerializerOptions = reply.Json,
                Description = "The plan the user watches while you work on a task of three or more steps. Pass 'steps' "
                              + "to lay out a new plan, replacing the current one: each step has a short imperative 'title' "
                              + "(at most ~7 words), a 'weight' 1-10 estimating its share of the work relative to the other "
                              + "steps, 'minutes' estimating the working time it needs, and optionally a 'status' and a "
                              + "'note'. Pass 'updates' to move the current plan: "
                              + "'step' is the step's number from 1, with a new 'status' (Pending, InProgress, Completed, "
                              + "Skipped, Failed), a 'progress' 0-100 estimating how far an in-progress step has come, "
                              + "an optional one-line 'note' (what is happening, or why a step was skipped or failed), and "
                              + "new 'minutes' when a step's estimate proved wrong. "
                              + "Keep exactly one step InProgress while working; finish the plan by completing or skipping "
                              + "every step. 'criteria' says what has to be true for the task to be done, when it has a "
                              + "checkable outcome (code, configuration, files, data; requirements the user named; checks a "
                              + "skill or the project's definition of done asks for): 2-5 items of 'text', each Unverified "
                              + "until checked. Leave criteria out for an explanation, a draft the user will judge, or an "
                              + "exploration; pass [] to drop them for a new task. Pass 'checks' to settle them: 'criterion' "
                              + "is its number from 1, 'status' Met, NotMet, NotApplicable (a general rule that does not "
                              + "concern this task) or Unverified, 'evidence' what showed it or why, and 'evidenceCall' the "
                              + "id of the tool call whose result shows it. Settle every criterion before the final answer; "
                              + "one you could not check stays Unverified and the answer says so. The application stamps "
                              + "the times, leaving out time spent waiting for the user, and computes the overall progress "
                              + "and the time left."
            });

        [McpServerTool(Name = ChatPlanResults.ToolName, ReadOnly = false, Destructive = false, Idempotent = false,
            OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ChatPlanToolResult))]
        private CallToolResult Execute(string? title = null, ChatPlanStepInput[]? steps = null,
            ChatPlanStepUpdate[]? updates = null, ChatPlanCriterionInput[]? criteria = null,
            ChatPlanCriterionCheck[]? checks = null)
        {
            var current = plans.Current(run);
            try
            {
                if (steps is { Length: > 0 } && updates is { Length: > 0 })
                    throw new ArgumentException("Pass either 'steps' for a new plan or 'updates' for the current one, not both.");
                if (steps is not { Length: > 0 } && updates is not { Length: > 0 } && criteria is null && checks is not { Length: > 0 })
                    throw new ArgumentException("Pass 'steps' to lay out a plan, 'updates' to move it, or 'criteria' and 'checks' for its criteria.");
                var now = plans.Now(run);
                var plan = steps is { Length: > 0 }
                    ? ChatPlanEditor.Replace(current, title, steps, now)
                    : current ?? throw new ArgumentException("There is no plan yet. Lay one out with 'steps' first.");
                if (updates is { Length: > 0 }) plan = ChatPlanEditor.Update(plan, updates, now);
                // A cited call the branch never made is a slip, not a reason to refuse the check: the
                // criterion is settled, the link left out and the model told.
                var unknown = new List<string>();
                string? Cited(string? call)
                {
                    if (string.IsNullOrWhiteSpace(call) || plans.KnowsCall(run, call.Trim())) return call;
                    unknown.Add(call.Trim());
                    return null;
                }
                if (criteria is not null)
                    plan = ChatPlanEditor.Criteria(plan, criteria.Select(item => item with { EvidenceCall = Cited(item.EvidenceCall) }).ToArray());
                if (checks is { Length: > 0 })
                    plan = ChatPlanEditor.Check(plan, checks.Select(item => item with { EvidenceCall = Cited(item.EvidenceCall) }).ToArray());
                plan = plan with { UpdatedAt = now.At, Clock = now.Clock };
                if (!plans.TrySet(run, current, plan))
                    throw new InvalidOperationException(
                        "The plan changed in a call made at the same time, or the run has ended. Read the result of that call and update again.");
                var progress = ChatPlanProgress.Of(plan, now.Clock);
                var summary = ChatPlanCriteriaSummary.Of(plan);
                var guidance = progress.IsFinished
                    ? summary.Unverified > 0
                        ? $"The steps are done; {summary.Unverified} criteria are still Unverified. Check them, or say in the answer which were not verified and why."
                        : "The plan is finished."
                    : $"Plan at {progress.Percent}%. Update it as each step finishes; do not repeat it in the answer.";
                if (unknown.Count > 0)
                    guidance += $" No tool call {string.Join(", ", unknown)} was made on this branch, so the evidence link was left out.";
                return reply.Reply(new ChatPlanToolResult(plan, progress.Percent, progress.Completed, progress.Total, guidance,
                    CriteriaMet: summary.Met, CriteriaTotal: summary.Total));
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            {
                var progress = current is null ? null : ChatPlanProgress.Of(current, plans.Now(run).Clock);
                return reply.Reply(new ChatPlanToolResult(null, progress?.Percent ?? 0, progress?.Completed ?? 0,
                    progress?.Total ?? 0, "The plan was not changed. Correct the call and try again.", error.Message), true);
            }
        }
    }
}
