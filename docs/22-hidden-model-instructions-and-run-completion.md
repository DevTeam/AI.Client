# Hidden model instructions and run completion

Status: implemented. No storage migration is required.

## Problem

Several agent mechanisms need trusted, model-only guidance: how to end a turn, a stalled run, an
empty response, an answer cut off at the token limit, tool discovery, the skill in use. Writing
that guidance into the chat as user messages would put implementation details in the transcript
and make every mechanism invent its own storage exception.

## Model-only instruction pipeline

The application owns one reusable pipeline behind dependency-injected interfaces:

- `IModelInstructionRegistry` is a run-local mailbox. Producers upsert instructions by stable key
  and choose `Request`, `UntilAcknowledged`, or `Run` lifetime, and a placement.
- `IAdaptiveContextPolicy` selects instructions by priority within the connection-dependent
  budget, chooses authored compact variants, and retains required protocol instructions.
- `IModelInstructionComposer` formats the selected instructions. Standing layers and run-long instructions lead the request as `system`
  messages; step guidance — any instruction shorter-lived than the run, or placed `Trailing` —
  becomes one `<application-guidance>` block that the planner adds to the end of the request's
  last message. That keeps the provider's cached prefix intact when the guidance changes, and the
  request valid for chat templates that accept a system message only first or no two messages of
  one role in a row (see [21](21-tool-selection-and-adaptive-compaction.md#prompt-cache-stability)).
- `IModelInstructionDiagnostics` records keys, count, and estimated tokens. It never logs
  instruction content.
- `ChatAgent` is the orchestrator: it opens and disposes the run scope, composes instructions before
  context planning, and acknowledges transient instructions after a successful provider response.
- `IToolDiscoveryGuidance` formats `run.tool-discovery` and unavailable-call recovery in one
  transient service. Current schemas override historical names; search is referenced only when
  its actual provider-facing definition is offered. The agent refreshes this guidance after
  checkpoint/final selection and recomposes before the final planner gate.

Hidden instructions never enter `ChatDetails`, `ChatRunState`, tool-result persistence, or the final
assistant message. They are counted by the normal context planner and are never compacted.

`ModelInstruction.Required` retains essential protocol guidance even when it exceeds the recommended
share; final request validation still applies. `CompactContent` supplies an authored alternative
for small windows. Optional instructions are selected by priority before placement, so transient
guidance does not displace a higher-priority run instruction merely by appearing last.

## Ending a turn

A model response without tool calls ends the turn and is published as the answer, as the models
are trained to work and as other agents do. Text that comes with tool calls is that step's
preamble and is published with them. The run-wide `run.finishing` instruction tells the model to
keep working until the request is done, not to stop to announce a next step or to ask permission
to take it ("Shall I…?"), to put a decision that is genuinely the user's through `ask_user` rather
than into the reply, and, when blocked, to say what it did, what is left and what blocks it.

What remains of the earlier machinery:

- **Truncation.** An answer cut off at the output token limit (`length`, or the Anthropic spelling)
  is fed back and continued, up to five times, into one message.
- **Empty responses.** A response with neither text nor calls is asked for again with a
  `response.empty` note, at most twice, then the run fails with the finish reason and chunk count.
- **Stalls.** A step is counted as bringing no new information when none of its calls returned
  something the turn had not already seen: the same tool, arguments and result again. An error is
  information like any other result unless it repeats exactly, so failing tests with changing
  output or a missing file followed by a search are progress, and a batch of parallel calls is one
  step, however many of them repeat. What was seen is forgotten when the history is summarized,
  since reading it again is then legitimate. After three such steps in a row the `run.stalled` note
  asks for an answer without another tool. The tools stay offered so the request keeps its cached
  start; a tool called anyway is not run, and the model is asked once more with no tools to give
  its own account of what it found and what blocks it. Only an endpoint that cannot answer that
  gets the fixed explanation. In a team's teammate branch the account also goes to the main branch
  as a `blocker` ([Asides and team messages](34-asides-and-team-messages.md)). A message added to the
  turn as an aside resets the count.

Until 2026-10 a control tool, `app_finish_run`, was the only way to finish a turn that had used
tools: plain text after a tool was held back and the model was corrected until it called the tool
with `complete` or `blocked`. It guarded against a turn ending on "I will now update the tests",
but it worked against how current models are trained: turns took extra full-context requests for
the corrections, needed a restricted step that offered only the control tool (and cost the cached
prefix), and grew rules for held-back text, `includePreviousText`, invalid decisions and empty
responses during the protocol. It was removed; should a model end turns early in practice, the
fix belongs in that model's instructions.

## Presentation

Streamed text is a draft until its step ends: with tool calls it becomes their preamble, without
them it becomes the answer. It is not persisted as an incomplete answer when interrupted.

Chat and run events can arrive in either order during completion. Once the newest turn contains a
durable workspace-change receipt, the Web client suppresses the live run copy so that the same edit
statistics are never rendered twice. A complete plain assistant message likewise wins over a stale
`Generating` snapshot during that transition.

## Verification

Tests cover instruction ordering, placement and lifetime, hidden-message non-persistence, plain
answers after tools, preambles published with their calls, stalled runs, empty responses,
workspace-receipt deduplication, truncated-answer continuation, and the existing chat/tool
execution suite.
