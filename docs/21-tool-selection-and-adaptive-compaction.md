# Tool selection and adaptive context compaction

Status: implemented.

## Problem

An enabled MCP catalogue can consume most of a model context window before any chat messages are
sent. In the observed case, tool definitions used 14,269 estimated tokens and left 13,123 tokens
for messages. Deterministic history compaction alone could not make the current protocol turn fit.

## Request pipeline

For every model step `ChatAgent` now:

1. applies tool permissions;
2. publishes the permitted catalogue to the run-local tool registry;
3. selects a bounded set through `IToolDefinitionSelector`;
4. keeps tools used by the current turn and tools discovered through `app_tool_search` pinned;
5. plans and, when necessary, adaptively compacts the model-facing message projection;
6. calls the endpoint only when the resulting `ContextPlan` fits.

The schema budget is the smaller of 6,000 tokens and 25% of the effective context window. A
selection contains at most 16 non-pinned tools. Pinned tools may exceed those limits because
removing a tool already involved in the current protocol turn would make continuation unreliable.

Within a run the list a step sends is carried on to the next one in its order, and only extended:
tools come before every message in the provider's cache key, so a list that is reordered or loses a
tool costs the whole cached conversation. A carried list that grows past one and a half times the
budget is chosen afresh, once. A step that advertises only `app_finish_run` is a detour and does not
replace the carried list.

## Progressive tool discovery

`app_tool_search` receives a capability query and searches only tools that already passed the run's
permission policy. It returns bounded names and descriptions, never full schemas. Matches are
pinned in the run-local registry and their definitions become available on the next model step.
The registry is keyed by project, chat and branch and is removed when the agent run ends.

## Adaptive tool-result projection

`IChatContextCompactor` still leaves stored `Content` and the visible transcript unchanged. If the
normal 3,000-character head plus 1,000-character tail projection does not fit, it retries with
progressively smaller model-only projections down to 384 head characters and 128 tail characters.
Tool-call groups remain intact and the current user request is never truncated.

## Prompt-cache stability

Providers cache a request by its prefix, so the request is laid out from what changes least to what
changes most:

1. standing instructions (base prompt, project instructions, memory, skill catalog);
2. run instructions that hold for the whole run (completion protocol, run ids);
3. the conversation, with any history checkpoint summary at its start;
4. a trailing note with this step's guidance — stalled run, empty response, completion required,
   tool discovery, the skill route and the active skill (`ModelInstructionPlacement.Trailing`, and
   any instruction whose lifetime is shorter than the run). It is one `user`-role message opened by
   `ModelInstructionComposer.TrailingPrefix`: several chat templates reject a system message
   anywhere but first. The planner counts it and never compacts it.

The deterministic compaction is carried on rather than redone (`ContextCompactionMemory`): while the
next request still starts with the previous input, the previous result plus the new messages is
used as long as it fits, so its cut, trimmed tool results and digests stay put. When it has to be
redone it compacts to 80% of the limit, leaving room for the following steps.

## Compaction by the model, ahead of the limit

When a request — instructions, tools and messages — reaches 70% of the connection's input window,
`ChatAgent` has the model summarize before anything has to be cut:

1. the earlier turns, kept as an automatic history checkpoint (below), when that frees at least a
   tenth of the window;
2. otherwise the completed steps of the turn in progress (`CompactTurnAheadAsync`): the latest steps
   that fit the keep budget stay in full, at least the last one, and an earlier summary of the turn
   is folded into the new one. This lasts for the run.

The summary costs one request and one cache miss; the requests after it share a stable, smaller
prefix. An attempt that frees too little or gets no summary is not repeated until the request has
grown by another tenth of the window. The deterministic compaction and the planner's LLM fallback
below remain for what is left: a summary that failed, or a single step larger than the window.

## Explicit checkpoints

`app_context_compact` exposes `Preview`, `Compact` and `Reset`, each with a `scope`:

- `Turn` (default) summarizes completed work of the current turn before the compaction call. The
  checkpoint is run-local: on the next model step it replaces only the covered model-facing
  messages, keeping the current user request and the compaction tool protocol group, and it is
  discarded when the run ends.
- `History` summarizes the chat's earlier turns — all but the current one, and the one before it
  when it fits the keep budget — and keeps the summary as a history checkpoint (below), so every
  later request of the branch starts from it.

Summaries are written by `ContextSummaryWriter`: a source that fits one request (about 40k
characters) is summarized in one, a longer one part by part and then merged, so nothing past a
character ceiling is silently dropped. Large tool results keep only their head and tail.

## History checkpoints

A history checkpoint (`HistoryCheckpoint`, stored in `{chat}.context.json` beside the chat) is a
summary that stands in for the history up to one message. When a run starts, the branch context is
built in full and the deepest checkpoint whose covered message is on the branch replaces
everything up to the first user message after it; a fork taken before that message is unaffected.
Stored messages and the visible transcript never change, and deleting the checkpoint restores the
full history for the next request.

Checkpoints are made four ways:

- the person presses Compact in the chat usage widget (`POST .../branches/{branchId}/compact`),
  which summarizes the history with the chat's connection, between turns only;
- the model calls `app_context_compact` with scope `History`;
- the agent compacts ahead of the limit (above), origin `Automatic`;
- the planner's LLM fallback: a summary it writes so that a request fits is kept and pinned for the
  rest of the run, instead of being written again for every step that would not fit without it.

The web client reads the checkpoints again as soon as a running turn reports a new summary
request, so the transcript mark appears while the turn is still going.

What stays in full is decided by size, not by count (`HistoryKeepPolicy`): recent turns are kept
from the newest back while together they fit a fifth of the connection's input window, at most
two. A chat of a few huge tool-heavy turns therefore keeps only its small last turn, or nothing
between turns — the next question then starts from the summary. The model's `History` scope always
keeps the turn in progress.

The transcript marks where the model's view begins, before the question that follows the covered
history, with the summary behind a toggle and an Undo.

## Diagnostics

Structured logs contain available and selected tool counts, available and selected schema-token
estimates, and the effective schema budget. A final context-window failure reports whether
compaction ran, how many messages were omitted and the remaining token deficit.

Every usage record of a chat request carries a `PromptPrefix`: the estimated tokens it shared with
the previous request of the same branch and purpose, and what broke the shared start — `Tools`,
`Instructions` or `History` — or nothing (`PromptPrefixTracker`, fingerprints only, in memory). The
previous request's last message is allowed to be missing, since the trailing note and a continued
answer go every step. The usage widget shows, for the turn, the cached share against what could
have been cached (a large gap with no change means the provider dropped its cache) and the count of
resets by cause.

## Automatic LLM fallback

When deterministic projection and turn omission together still leave the request above the input
budget, `IChatContextPlanner.PlanAsync` falls back to a tool-free LLM summary of the older
turns. `ChatContextCompactor.CompactWithLlmAsync` reuses the same `IChatCompletionClient` as the
run, sends one user message containing the older turns and no tool list, and inserts the reply as
a `user`-role summary so untrusted history cannot be promoted to a system instruction. Token
budget for the summary is `ChatAgent.SummaryTargetTokens` (1500 tokens). If the summarizer
returns empty or throws a non-cancellation exception the deterministic result is kept.

## User-visible status of automatic fallback

The fallback is silent in the UI today: the chat feed shows nothing, the run status does not
change, and no message is persisted. The fact that it happened is visible only through the
`IContextPlanDiagnostics.Record` log line `LLM context plan for {Model}` with
`compacted=true`, `omitted=N`, and the post-compaction estimate.

## Making the fallback visible to the user

Three layered options, in order of effort. Each is described as a separate change so they can be
reviewed independently.

1. **Log + run journal only** (recommended first step). Add `RecordLlmFallback(string model,
   long inputLimit, long projectedTokens, int omittedMessages, int summaryCharacters)` to
   `IContextPlanDiagnostics`. Call it from `ChatContextPlanner.PlanAsync` when the LLM step
   changes the plan. The infrastructure `ContextPlanDiagnostics` logs the event under a new
   `LoggerMessage(1005, LogLevel.Information, "LLM context fallback for {Model}: …")`. No UI
   change. The fallback stays invisible in the chat feed but every run now leaves a single
   distinguishing line in the run journal, which is enough to answer "did it just compact?".
2. **Transport-side notification.** Add a `ChatTransportWait`-style record
   `ContextCompactionNotice(int step, string kind, int omittedMessages, long estimatedTokens)`
   and report it through the existing `IChatTransportActivity.BeginScope` callback. The web
   surface already mirrors these notices in the run banner; adding one more kind is mechanical.
3. **Chat feed item.** Persist a synthetic system message in the run state, not the chat JSON,
   through `ChatRunDispatcher.ReportToolActivityAsync`. Render it as a `FeedItem` of a new
   `MessageKind.SystemNote` next to the assistant reply. Visible inline with the answer; needs
   a `MessageKind` enum entry, a `ChatFeed` renderer branch and an opt-out setting.

The order matters: option 1 lands first because it requires no UI work and gives a name to the
event that options 2 and 3 reuse. Option 3 is the only one that surfaces the event to a user
who is not looking at logs or the run banner.

## Compatibility

No chat or settings migration is required. No existing stored message is rewritten. Connections
without explicit context limits continue to use the existing defaults.
