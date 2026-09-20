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

## Explicit current-turn checkpoint

`app_context_compact` exposes `Preview`, `Compact` and `Reset`. `Compact` starts an isolated
completion request with no tools and asks it for a bounded summary of completed work before the
compaction call. The resulting checkpoint is run-local: on the next model step it replaces only
the covered model-facing messages, while keeping the current user request and the compaction tool
protocol group. It is discarded automatically when the run ends, so no storage migration or
background rewrite is required.

## Diagnostics

Structured logs contain available and selected tool counts, available and selected schema-token
estimates, and the effective schema budget. A final context-window failure reports whether
compaction ran, how many messages were omitted and the remaining token deficit.

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
