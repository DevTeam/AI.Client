# Hidden model instructions and run completion

Status: implemented. No storage migration is required.

## Problem

The agent previously treated any non-empty model response without tool calls as the final answer.
After a long tool-driven run, a provisional sentence such as “I will now update the tests” could
therefore complete the run even though work remained. The same streamed prose was temporarily
rendered as a normal assistant answer outside the compact turn presentation.

Several agent mechanisms also need trusted, model-only guidance. Representing that guidance as a
synthetic user message would give it the wrong authority, expose implementation details in the
transcript, and make future mechanisms invent separate storage exceptions.

## Model-only instruction pipeline

The application owns one reusable pipeline behind dependency-injected interfaces:

- `IModelInstructionRegistry` is a run-local mailbox. Producers upsert instructions by stable key
  and choose `Request`, `UntilAcknowledged`, or `Run` lifetime.
- `IModelInstructionComposer` orders and deduplicates the active instructions, applies a bounded
  token budget, and prepends them as `system` messages to the model-facing projection only.
- `IModelInstructionDiagnostics` records keys, count, and estimated tokens. It never logs
  instruction content.
- `ChatAgent` is the orchestrator: it opens and disposes the run scope, composes instructions before
  context planning, and acknowledges transient instructions after a successful provider response.

Hidden instructions never enter `ChatDetails`, `ChatRunState`, tool-result persistence, or the final
assistant message. They are counted by the normal context planner and remain in the system preamble
during deterministic compaction.

The existing continuation after `finishReason=length` now uses this pipeline instead of appending a
synthetic user message. Future mechanisms should publish through `IModelInstructionRegistry` rather
than add special branches to persistence or UI code.

## Definition-of-done control tool

`IRunCompletionProtocol` exposes the model-only `app_finish_run` control tool. It is implemented by
`RunCompletionProtocol` and injected into `ChatAgent`; it is not an MCP side effect and does not pass
through tool permissions or execution.

The decision contains only `status` (`complete` or `blocked`) and a user-facing `finalAnswer`.
Ordinary tool calls already continue the run, so the control tool has no `continue` status. The
former `completed`, `evidence`, `remaining`, and `nextAction` fields were model-written claims that
the application could not verify; old calls carrying those fields are still parsed. `blocked`
includes missing information, unavailable capabilities, and questions the model cannot answer
reliably. Its answer reports partial work, if any, and the limitation.

After the run has used a normal tool, ordinary model prose is provisional. A plain response cannot
complete the run: the agent adds a transient hidden correction and asks the model to continue with
an ordinary tool or call `app_finish_run`. The user never saw the provisional prose, so the correction
and the protocol tell the model to put its answer in full into `finalAnswer` and never to refer back to
earlier text; an earlier wording ("do not repeat the text") led models to publish "see the previous
answer" pointing at nothing. The first correction still offers ordinary tools; later
corrections offer only `app_finish_run`. Three consecutive invalid decisions fail the run. If an
OpenAI-compatible endpoint never emits the control call, its latest prose is published after three
corrections rather than discarded. The same prose is the fallback after repeated empty responses.
The parser accepts a capitalised status and arguments encoded once more as a JSON string.

The agent counts tool errors and repeated identical tool results as steps without new information.
A new successful result, including a new answer to a user question, resets the count. After four stalled steps,
the next request offers only `app_finish_run` and asks for an honest `complete` or `blocked` answer.
If the model calls another tool, the agent stops with a deterministic explanation instead of
continuing the loop.

An empty provider response does not acknowledge model-only instructions. The agent retains every
pending correction and adds `response.empty`. When completion is already required, the recovery
request advertises only `app_finish_run`, forcing an explicit `complete` or `blocked` decision.
Diagnostics record the retry number, `finish_reason`, chunk count, and whether this restricted
recovery mode was active, without logging model or instruction content.

For a direct answer which used no tools, the existing plain-text completion remains supported for
OpenAI-compatible endpoints with incomplete tool-call support.

## Presentation

Provisional provider text is buffered only inside `ChatAgent`. It is not copied into a run snapshot,
rendered as a second live response, or persisted as an incomplete answer when interrupted. Text
becomes visible only when it is durable tool intent (an assistant message carrying tool calls) or a
publishable final answer. `StreamingContent` therefore has one meaning and needs no separate
intermediate/tool-call placement state.

Chat and run events can arrive in either order during completion. Once the newest turn contains a
durable workspace-change receipt, the Web client suppresses the live run copy so that the same edit
statistics are never rendered twice. A complete plain assistant message likewise wins over a stale
`Generating` snapshot during that transition.

## Verification

Tests cover instruction ordering and lifetime, completion-decision validation, hidden-message
non-persistence, rejection and non-publication of a premature final response after a tool call,
workspace-receipt deduplication, truncated-answer continuation, and the existing chat/tool execution
suite.
