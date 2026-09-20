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

The structured decision contains:

- `status`: `complete`, `continue`, or `blocked`;
- `finalAnswer`: required for `complete` and `blocked`;
- `completed` and `evidence`;
- `remaining`;
- `nextAction`: required for `continue`.

After the run has used a normal tool, ordinary model prose is provisional. A plain response cannot
complete the run: the agent adds a transient hidden correction and asks the model to continue. Only
`complete.finalAnswer` or `blocked.finalAnswer` becomes the durable final assistant message.
`continue` is returned to the model as an internal tool result and starts the next step. Three
consecutive missing or invalid completion decisions fail the run instead of looping indefinitely;
before that limit, malformed decisions receive a hidden structured rejection and correction request.

An empty provider response does not acknowledge model-only instructions. The agent retains every
pending correction and adds `response.empty`. When completion is already required, the recovery
request advertises only `app_finish_run`, forcing an explicit `complete`, `continue`, or `blocked`
decision before the normal tool catalogue is restored. Diagnostics record the retry number,
`finish_reason`, chunk count, and whether this restricted recovery mode was active, without logging
model or instruction content.

For a direct answer which used no tools, the existing plain-text completion remains supported for
OpenAI-compatible endpoints with incomplete tool-call support.

## Presentation

Provisional text travels separately from `StreamingContent` as `ChatRunSnapshot.IntermediateContent`.
The compact turn summary can be expanded to inspect it while the run is active. It is never promoted
to a final dialogue message. If the run is interrupted or fails, the latest provisional text is
preserved as an incomplete assistant message, matching the existing recovery behavior.

## Verification

Tests cover instruction ordering and lifetime, completion-decision validation, hidden-message
non-persistence, rejection of a premature final response after a tool call, compact intermediate
state, truncated-answer continuation, and the existing chat/tool execution suite.
