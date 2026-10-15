# Chat plans

Status: Accepted

While the model works on a task of several steps, it keeps a plan the user can follow: ordered
steps, each with an estimated share of the work, and an overall progress measured in that share.

## Model

A plan (`AI.Contracts.Plans.ChatPlan`) has an optional title, its steps, the time it was last
changed and its working clock at that time (`Clock`). A step (`ChatPlanStep`) has:

| Field | Set by | Meaning |
| --- | --- | --- |
| `Title` | model | A short imperative phrase, at most 160 characters |
| `Weight` | model | Its share of the work, 1–10, relative to the other steps |
| `Status` | model | `Pending`, `InProgress`, `Completed`, `Skipped` or `Failed` |
| `Progress` | model | How far an in-progress step has come, 0–99; a completed step is 100 |
| `Note` | model | One line: what is happening, or why the step was skipped or failed |
| `Minutes` | model | The working time it is expected to take; it can be revised when it proves wrong |
| `StartedAt`, `FinishedAt` | application | Stamped by the wall clock when the status changes |
| `StartedClock`, `FinishedClock` | application | The same moments on the plan's working clock |

### The working clock

Step times and the forecast measure work, not waiting. The plan's working clock runs only while a
run works on the branch: it stands still while the run waits for the person (a tool approval, an
`ask_user` question) and between turns. On the Host, `IChatPlans` keeps it for the run: it goes on
from the plan's `Clock` when the run begins, and `ChatAgent` stops it around every wait for a person
(`TurnDeadline.WhileWaitingForAPersonAsync`, which already stops the turn's own deadline there).
Each plan update stores the reading. Between updates the page runs the clock on itself
(`ChatPlanClock`): from the last update, or from the start of the turn for a plan an earlier turn
left, holding it while the run snapshot shows a pending approval, a question or a pause. The next
update brings the Host's reading. Steps stamped before plans had a clock are put on it by their wall
times, counted back from the last update.

A step moved straight from `Pending` to `Completed` or `Failed` was worked on since the plan's
previous change, so its start is the later of that change and the last earlier finish of another
step. A step laid out as already finished, or skipped before it began, has no start and shows no
time, rather than a measured-looking "0s".

`ChatPlanProgress.Of(plan, clock)` measures the plan at a moment on its working clock:

- **Fraction** is done weight over the weight still in the plan. A completed step counts its whole
  weight, an in-progress one `weight × progress`. Skipped and failed steps leave the measure, so a
  plan that dropped a step still reaches 100%.
- **Completed / Total** counts steps, not weight: "2 of 5 steps done".
- **Current** is the step in progress, or the first one waiting.
- **Elapsed** is working time from the first start to now, or to the last step's end once finished.
- **Steps** gives each step its time spent and its expected time, and whether it is well over
  (`IsOver`: half as long again as expected, and at least a minute more).
- **Remaining** is the sum of the expected time still to go. The forecast starts from the model's
  estimates and moves towards what the work actually takes: the estimates are multiplied by how
  much longer or shorter the completed steps ran than theirs, trusted as far as the plan has come
  (`1 - f + f * taken / estimated`, with `f` the done fraction). A step without an estimate takes
  its weight at the plan's pace, measured on the completed steps, else the estimated steps' time
  per weight, else the running step's own reported share. A running step has left the larger of
  its expected time minus the time spent and what its reported progress leaves of it, so running
  over does not make it "done". With nothing to go on yet, there is no forecast.
- **RemainingLow / RemainingHigh** widen the forecast by `0.15 + 0.5 * (1 - f)`, and by 0.2 more
  while no step has been completed: wide on estimates alone, narrow near the end.

A finished step is held to the model's own estimate, so its time against the estimate shows how the
estimate went.

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
current one, or `updates`, moving steps of the current plan by number; never both. Each step can
carry `minutes`, its expected working time; an update can revise it. Laying out again keeps the
times of steps whose titles stay the same. The application stamps the times on both clocks,
computes the progress and returns the plan with `percent`, `completed` and `total`.

It runs without approval (`ToolDefaultDecision`), is always among the offered tools
(`AdaptiveContextPolicy` core tools), and is available in every chat kind that allows it.

## In the chat

`ChatPlanCard` is a summary card on the shared `chat-summary-card` shell, collapsed by default.

- **Collapsed** it is one line: the step in progress (or the plan's title and how it ended), the
  completed count ("2/5"), the percentage and, for a live plan, the forecast as a range ("8-15m
  left", with the likely finish by the clock in its tooltip), or "Waiting for you" while the run
  waits; a hairline track along the header's bottom edge fills by the weighted progress.
- **Expanded** it lists every step with a status mark, its title, its note, the reported progress
  of the step in progress and its time: taken for a finished step, "4m / ~5m" for the step in
  progress, "~5m" expected for a waiting one. A step well over its expected time shows it in the
  warning text colour.
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
  plan: the work left, each step's status, weight, estimate, progress, time so far and note, and how
  to keep it current. When the step in progress is well over its expected time, it adds that the
  model should say in a note what holds it up and re-estimate it, or revise the plan, and ask the
  user if the approach itself is in doubt. A finished plan
  removes it. This keeps the plan in front of the model when its last result is far back,
  compacted away or in an earlier turn.
