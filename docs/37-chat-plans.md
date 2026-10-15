# Chat plans

Status: Accepted

While the model works on a task of several steps, it keeps a plan the user can follow: ordered
steps, each with an estimated share of the work, and an overall progress measured in that share.

## Model

A plan (`AI.Contracts.Plans.ChatPlan`) has an optional title, its steps and the time it was last
changed. A step (`ChatPlanStep`) has:

| Field | Set by | Meaning |
| --- | --- | --- |
| `Title` | model | A short imperative phrase, at most 160 characters |
| `Weight` | model | Its share of the work, 1–10, relative to the other steps |
| `Status` | model | `Pending`, `InProgress`, `Completed`, `Skipped` or `Failed` |
| `Progress` | model | How far an in-progress step has come, 0–99; a completed step is 100 |
| `Note` | model | One line: what is happening, or why the step was skipped or failed |
| `StartedAt`, `FinishedAt` | application | Stamped when the status changes |

A step moved straight from `Pending` to `Completed` or `Failed` was worked on since the plan's
previous change, so its start is the later of that change and the last earlier finish of another
step. A step laid out as already finished, or skipped before it began, has no start and shows no
time, rather than a measured-looking "0s".

`ChatPlanProgress.Of(plan, now)` measures the plan:

- **Fraction** is done weight over the weight still in the plan. A completed step counts its whole
  weight, an in-progress one `weight × progress`. Skipped and failed steps leave the measure, so a
  plan that dropped a step still reaches 100%.
- **Completed / Total** counts steps, not weight: "2 of 5 steps done".
- **Current** is the step in progress, or the first one waiting.
- **Elapsed** runs from the first start to now, or to the last change of a finished plan.
- **Remaining** applies the pace so far (elapsed time per unit of done weight) to the weight left.
  It is shown only while a run is in progress and some weight is done.

## Storage

A plan has no store of its own. It is the structured result of the `plan_update` tool, kept in the
branch's history like every tool result, so a fork or a regenerated answer has the plan of its own
path and deleting a branch deletes its plan. The branch's plan is the newest successful
`plan_update` result along its chain (`ChatPlanHistory.Latest`); a failed call leaves the previous
plan in place.

The transcript and the live message deltas leave tool results' bodies behind. For a plan result
the server reads the plan out of the body and sends it as `ChatMessageView.Plan`, so the page gets
every plan without loading the results.

During a run, `IChatPlans` holds the run's working copy: read from the branch once when the run
begins, changed by the tool, read before every model step. It spares those reads of the whole chat;
the history stays the record.

## Tool

`mcp_app__plan_update` (`AppPlanTool`) takes either `steps`, laying out a new plan that replaces the
current one, or `updates`, moving steps of the current plan by number; never both. Laying out again
keeps the times of steps whose titles stay the same. The application stamps the times, computes the
progress and returns the plan with `percent`, `completed` and `total`.

It runs without approval (`ToolDefaultDecision`), is always among the offered tools
(`AdaptiveContextPolicy` core tools), and is available in every chat kind that allows it.

## In the chat

`ChatPlanCard` is a summary card on the shared `chat-summary-card` shell, collapsed by default.

- **Collapsed** it is one line: the step in progress (or the plan's title and how it ended), the
  completed count ("2/5"), the percentage and, for a live plan, the estimated time left; a hairline
  track along the header's bottom edge fills by the weighted progress.
- **Expanded** it lists every step with a status mark, its title, its note, the reported progress
  of the step in progress and its time.
- **Above the composer** (`Home.razor`, `composer-dock`), while the branch's run is generating or
  paused, the card shows the branch's plan if the current turn moved it or an earlier turn left it
  unfinished. It opens upward like the live file-changes card, and its clock ticks every second.
- **Under the turn**, once the run stops, `MessageFeed` adds the card after the answer of every turn
  that moved the plan, with the state that turn left it in.
- The activity row of a call reads "Lay out plan · N steps" or "Update plan", with the result
  "P% · C of N steps done" (`AppPlanPresentationAdapter`).

The **Plan** widget (`chat-plan`) shows the branch's plan beside the conversation, including a
plan set aside by an earlier turn; see [Chat widgets](29-chat-widgets.md#plan-widget).

## Model guidance

- The standing prompt's application tools guide says to lay out a plan for a task of three or more
  steps and to keep it moving.
- The result of loading any playbook (`SkillRunner`) says to lay a playbook of three or more steps
  out as a plan before its first step, so the built-in playbooks keep one without each repeating it.
- `skill-route` never routes a task to `chat-plan` in place of the skill that does the work; it
  picks it only for a request to track progress or for a to-do list. Planning a change without
  doing it stays with `code-plan` and `qa-plan`.
- The `chat-plan` playbook (`/todo`, `/progress`) says how: 3–8 steps, honest weights, one step in
  progress, several moves per call, every step finished before the final answer, and what to do
  when the next message is another task.
- Before every model step `ChatAgent` adds the trailing instruction `run.plan` with the unfinished
  plan: each step's status, weight, progress and note, and how to keep it current. A finished plan
  removes it. This keeps the plan in front of the model when its last result is far back,
  compacted away or in an earlier turn.
