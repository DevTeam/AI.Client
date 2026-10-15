namespace AI.Mcp.App;

using AI.Application.Plans;
using AI.Application.Projects;
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
public sealed class AppPlanTool(IChatPlans plans, IClock clock) : IAppTool
{
    public McpServerTool Create(ToolRunContext run, IAppToolReply reply) => new Session(plans, clock, run, reply).Create();

    private sealed class Session(IChatPlans plans, IClock clock, ToolRunContext run, IAppToolReply reply)
    {
        public McpServerTool Create() => McpServerTool.Create(Execute,
            new McpServerToolCreateOptions
            {
                SerializerOptions = reply.Json,
                Description = "The plan the user watches while you work on a task of three or more steps. Pass 'steps' "
                              + "to lay out a new plan, replacing the current one: each step has a short imperative 'title' "
                              + "(at most ~7 words), a 'weight' 1-10 estimating its share of the work relative to the other "
                              + "steps, and optionally a 'status' and a 'note'. Pass 'updates' to move the current plan: "
                              + "'step' is the step's number from 1, with a new 'status' (Pending, InProgress, Completed, "
                              + "Skipped, Failed), a 'progress' 0-100 estimating how far an in-progress step has come, and "
                              + "an optional one-line 'note' (what is happening, or why a step was skipped or failed). "
                              + "Keep exactly one step InProgress while working; finish the plan by completing or skipping "
                              + "every step. The application stamps the times and computes the overall progress."
            });

        [McpServerTool(Name = ChatPlanResults.ToolName, ReadOnly = false, Destructive = false, Idempotent = false,
            OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ChatPlanToolResult))]
        private CallToolResult Execute(string? title = null, ChatPlanStepInput[]? steps = null,
            ChatPlanStepUpdate[]? updates = null)
        {
            var current = plans.Current(run);
            try
            {
                if (steps is { Length: > 0 } && updates is { Length: > 0 })
                    throw new ArgumentException("Pass either 'steps' for a new plan or 'updates' for the current one, not both.");
                var now = clock.UtcNow;
                var plan = steps is { Length: > 0 }
                    ? ChatPlanEditor.Replace(current, title, steps, now)
                    : updates is { Length: > 0 }
                        ? ChatPlanEditor.Update(current ?? throw new ArgumentException(
                            "There is no plan yet. Lay one out with 'steps' first."), updates, now)
                        : throw new ArgumentException("Pass 'steps' to lay out a plan or 'updates' to move the current one.");
                if (!plans.TrySet(run, current, plan))
                    throw new InvalidOperationException(
                        "The plan changed in a call made at the same time, or the run has ended. Read the result of that call and update again.");
                var progress = ChatPlanProgress.Of(plan, now);
                return reply.Reply(new ChatPlanToolResult(plan, progress.Percent, progress.Completed, progress.Total,
                    progress.IsFinished
                        ? "The plan is finished."
                        : $"Plan at {progress.Percent}%. Update it as each step finishes; do not repeat it in the answer."));
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            {
                var progress = current is null ? null : ChatPlanProgress.Of(current, clock.UtcNow);
                return reply.Reply(new ChatPlanToolResult(null, progress?.Percent ?? 0, progress?.Completed ?? 0,
                    progress?.Total ?? 0, "The plan was not changed. Correct the call and try again.", error.Message), true);
            }
        }
    }
}
