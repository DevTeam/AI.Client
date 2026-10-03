# Tool selection and adaptive context compaction

Status: implemented.

Decisions: [ADR-009](decisions/ADR-009-adaptive-context-policy.md) and
[ADR-010](decisions/ADR-010-adaptive-compaction-and-estimation.md) and
[ADR-011](decisions/ADR-011-budgeted-summary-requests.md).

## Problem

An enabled MCP catalogue can consume most of a model context window before any chat messages are
sent. In the observed case, tool definitions used 14,269 estimated tokens and left 13,123 tokens
for messages. Deterministic history compaction alone could not make the current protocol turn fit.

## Request pipeline

For every model step `ChatAgent` now:

1. applies tool permissions;
2. publishes the permitted catalogue to the run-local tool registry;
3. composes the adaptive instructions and selects tools through `IAdaptiveContextPolicy`;
4. protects tools used by the current turn and consumes pending discovery priorities;
5. plans and, when necessary, adaptively compacts the model-facing message projection;
6. calls the endpoint only when the resulting `ContextPlan` fits.

## Central adaptive policy

`AdaptiveContextPolicy` implements `IAdaptiveContextPolicy`. All
adaptive budgets, instruction admission and variants, tool ranking, tool-set replacement,
compaction thresholds, summary acceptance and estimation safety
live in this class. `StandingInstructions` reads source data, `ModelInstructionComposer` formats
selected instructions, and `ChatContextPlanner` verifies the final request using the same policy's
protocol/safety allowance.

Let `U = max(0, context window - reserved output - 256 protocol - safety)`.
Safety starts at 1,024 tokens and can increase from reported provider input counts (see below).

| Component | Budget |
|---|---|
| All instructions | `min(24,576, 60% of U)` |
| Run/step instructions, within the instruction share | `min(2,048, 10% of U)` |
| Tool definitions | `min(24,576, 20% of U)` |
| Opportunistic tool admission count | 8 below 8,192 usable tokens; 16 below 65,536; otherwise 64 |

The tool share is further reduced by the complete projected conversation, including standing
instructions, tool results and trailing guidance. It is `min(schema share, max(0, U -
max(U / 4, estimated context messages + trailing guidance)))`. At least a quarter of `U` is left
for messages even when history is short. History and tool results are projected/compacted by the existing
planner. Connection overrides and the output reserve are respected; parameter count and model
names do not determine the profile.

The agent reselects tools after step guidance and automatic checkpoints are known, before final
planning. Definitions whose protocol groups were summarized away can then leave the request,
instead of retaining an oversized catalogue after a successful checkpoint.

Tool admission order is current-turn protocol definitions, the small control pair (`ask_user`,
`tool_search`), discovered/skill-routed tools, textual relevance, App-tool preference, smaller
schemas, and stable name. Discovered tools cannot bypass the budget. Only current-turn protocol
definitions can exceed the schema share; this does not bypass final request validation. Older
turns do not permanently pin every tool they once called.

Standing instructions use authored compact application text below 16,384 usable tokens,
or when the full source layers exceed their share. Project rules are retained in full. Memory
and skill indexes use whole entries and discovery pointers; compact skill entries omit parameter
lists and shorten descriptions. Required run instructions have explicit compact variants and
are retained even when the recommendation cannot accommodate them. Optional instructions are
admitted by priority, independently of where they will be serialized.

The selected standing content is fixed for the run. A carried tool set retains its exact order
while it fits, including sets already above the current admission count. Initial opportunistic
selection uses only 80% of the schema budget; core and discovered tools can fill the whole share.
Later steps append only newly required, discovered or core capabilities, rather than admitting
tools merely because the query relevance changed. Core and discovered additions may exceed the
opportunistic count while fitting the token budget; only protocol-required definitions may exceed
that token budget. Under pressure or a necessary discovery, the set is
chosen again within the hard share and surviving tools retain their order. A stalled run keeps
its fitting tools offered and receives trailing guidance to stop calling them.

## Progressive tool discovery

`app_tool_search` receives a capability query and searches only tools that already passed the run's
permission policy. It returns bounded names and descriptions, never full schemas. Matches are
pinned in the run-local registry and their definitions become available on the next model step.
All searches in a batch contribute to pending priorities, consumed by the next tool selection;
they do not accumulate permanent pins. A fitting carried tool set still retains the tools already offered.
Its reply explicitly says that admission depends on the schema budget. The registry is keyed by
project, chat and branch and is removed when the agent run ends. Search schemas do not contain a
changing index of omitted names; search results carry those names without changing definitions.

Automatic skill routing is also gated by the context planner. When its tool-free routing catalogue
cannot fit, the optional routing request is skipped and the main chat uses progressive discovery.

## Adaptive tool-result projection

`IChatContextCompactor` leaves stored `Content` and the visible transcript unchanged. Its
`IToolResultContextProjector` retains bounded process outcomes, file/application paths and ids,
errors, and diagnostic lines found in the middle of stdout/stderr or plain text. Recognized JSON
results retain those facts before spending the remaining excerpt allowance on the beginning and
end. Malformed and unknown output uses diagnostic lines and excerpts. A retained path/resource or
the original tool can retrieve the full details; the projection never includes host metadata.

If the normal 3,000 + 1,000 character allowance does not fit, compaction retries with progressively
smaller allowances down to 384 + 128. These allowances cover facts and excerpts; the marker and
labels are additional and counted by the final estimator.

Deterministic compaction first projects results, then replaces older turns with an extractive
digest, then shortens the completed head of the current turn. It works without a summarizer.
The actual current user request, system preamble, and latest protocol exchange (including parallel
calls and all their results) remain. An impossible latest exchange fails the gate instead of being
discarded. Synthetic summaries carry `IsContextSummary` in the application; they retain the user
role on the wire but do not start a new user turn or displace the real current question.

## Compaction budget and frequency

All thresholds come from `IAdaptiveContextPolicy.ResolveCompaction`. Let
`M = max(0, U - selected tool tokens - trailing guidance tokens)`, the allowance for messages,
including standing and run instructions. The reserves and tools are removed before calculating
every threshold; the agent and planner use the same basis.

| Decision | Budget |
|---|---|
| Automatic summary trigger | 70% of `M` |
| Required actual savings from an automatic checkpoint | 10% of `M`, at least one token |
| Growth before another automatic attempt | 10% of `M`, at least one token |
| Deterministic target with run-local compaction memory | 80% of `M` |
| Recent history/turn keep allowance | 20% of `M`; always retain the current turn/latest step |
| Fallback/turn summary target | `min(1,500, 10% of M)` |
| History summary target | `min(3,000, 20% of M)` |

The summary policy clamps the target to the reserved output and a quarter of usable input.
Targets below 64 tokens disable summarization. The writer measures complete source and merge
prompts with the model-bound estimator; full summary framing is measured again at acceptance.
See [request budgets and evaluation](31-context-evaluation.md) for the complete formulas.

The budget is checked before every request. Deterministic compaction runs only when the original
or carried request exceeds `M`. There is no timer or every-N-messages schedule. Its carried
projection is reused while fitting; a new cut leaves room for subsequent append-only steps.

## Prompt-cache stability

Providers cache a request by its prefix, so the request is laid out from what changes least to what
changes most:

1. standing instructions (base prompt, project instructions, memory, skill catalog);
2. run instructions that hold for the whole run (how to finish a turn, run ids);
3. the conversation, with any history checkpoint summary at its start;
4. this step's guidance — stalled run, empty response, continuation after truncation, tool
   discovery, the skill route and the active skill (`ModelInstructionPlacement.Trailing`, and any
   instruction whose lifetime is shorter than the run) — as an `<application-guidance>` block
   added to the end of the request's last message, the user's question or a tool result. Several
   chat templates reject a system message anywhere but first, and others two messages of one role
   in a row, so it is not a message of its own; only after the model's own words (an answer being
   continued) does it follow as a separate user message. Stored messages never carry it. The
   planner counts it and never compacts it.

The deterministic compaction is carried on rather than redone (`ContextCompactionMemory`): while the
next request still starts with the previous input, the previous result plus the new messages is
used as long as it fits, so its cut, trimmed tool results and digests stay put. When it has to be
redone it compacts to 80% of the limit, leaving room for the following steps.

Preserving order prevents unnecessary changes; adding a definition still changes the tools prefix.
Discovery can therefore cause an intentional cache miss. Changing transient guidance stays at the
end of the messages. The existing `PromptPrefixTracker` measures application-level prefix changes;
an unchanged prefix does not promise a cache hit at every provider.

## Compaction by the model, ahead of the limit

When messages reach 70% of `M`,
`ChatAgent` has the model summarize before anything has to be cut:

1. the earlier turns, kept as an automatic history checkpoint (below), when that frees at least a
   tenth of `M`;
2. otherwise the completed steps of the turn in progress (`CompactTurnAheadAsync`): the latest steps
   that fit the keep budget stay in full, at least the last one, and an earlier summary of the turn
   is folded into the new one. This lasts for the run.

Before applying or storing an automatic checkpoint, the policy compares the full message lists
before and after, including the summary role, prefix and retained messages. A summary that grows
the request or frees too little is rejected without replacing the existing checkpoint. Preview
eligibility uses estimated message tokens, never a character-count approximation.

A summary costs model requests and may change the cache prefix; the requests after it share a
stable, smaller prefix. Both successful and unsuccessful attempts wait for growth by another
tenth of `M` before retrying. The deterministic compaction and the planner's LLM fallback
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

Summaries are written by `ContextSummaryWriter`: a source fitting the connection's usable token
allowance is summarized once, and a longer source is partitioned and merged within that same
allowance. Every part and merge is checked before sending. The operation has at most 64 calls and
four merge rounds. Empty parts, failures or exhausted budgets leave the original source intact.
Large tool results retain bounded diagnostics/facts and excerpts before partitioning. Summary
prompts request Goal, Constraints, Decisions, Evidence, Failures and Remaining work sections.

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

Context-window failures also report the total window, instruction tokens, conversation tokens and
protocol/safety allowance. When instructions and tools already exhaust the window, the error
explicitly states that history compaction cannot help. LLM fallback is skipped when the immutable
system messages and current user message cannot fit its conversation allowance.

The project settings preview shows the Compact/Full profile, window and instruction/tool shares
using existing settings rows. It uses the project/default connection and identifies that scope;
actual runs pass their chosen chat connection. Project rules exceeding the recommendation are
shown as retained, rather than silently clipped. Instruction files still have a 64 KiB read limit.

## Adaptive verification

Policy tests cover 4,096, 8,192, 16,384, 32,768, 131,072 and 250,000-token windows, growing output reserves,
instruction priorities and variants, intact project rules, current-turn protocol protection,
oversized discovered schemas, pressure eviction, and exact schema/prefix stability across steps.
Existing composition-backed chat tests run on small windows, and App-tool session tests validate
complete requests using the actual generated MCP schemas. Tests use scripted completions rather
than an external LLM.

Compared usage records carry a `PromptPrefix`: the estimated tokens shared with
the previous request of the same branch, purpose and model connection, the full estimated current
input on the same scale, and what broke the shared start — `Tools`,
`Instructions` or `History` — or nothing (`PromptPrefixTracker`, fingerprints and counts only, in memory). The
previous request's last message is allowed to be missing, since the trailing note and a continued
answer go every step. The usage widget separates actual cached input, approximate application
prefix overlap and change counts. It cannot infer provider cache expiry or total invalidation.
Older records without an overlap denominator retain usage/change counts but are excluded from
the overlap percentage. See [token usage](28-token-usage.md).

Tool selection diagnostics include `reason` (`initial`, `retained`, `expanded`, `pressure`,
`discovery`, `catalog_changed`), added/removed/changed-definition counts and an order-change flag.
They never contain prompt text, tool arguments or schema contents.

## Automatic LLM fallback

When deterministic projection and turn omission together still leave the request above the input
budget, `IChatContextPlanner.PlanAsync` falls back to a tool-free LLM summary of the older
turns. `ChatContextCompactor.CompactWithLlmAsync` reuses the same `IChatCompletionClient` as the
run, sends one user message containing the older turns and no tool list, and inserts the reply as
a `user`-role summary so untrusted history cannot be promoted to a system instruction. Token
budget for the summary comes from the policy (up to 1,500 tokens). An empty, failed or
non-improving fallback keeps the deterministic result. The final gate can fail when mandatory
input still does not fit. A persisted fallback checkpoint contains the exact summary sent,
including any final shortening, so later requests do not restore an oversized version.

## Estimation observations

`ContextTokenEstimator` counts recognized model text offline with packaged Cl100k/O200k
vocabularies; unknown models retain the conservative UTF-8 estimate. Protocol framing remains
estimated. `ContextSummaryWriter` uses the same model-bound view to budget source and merge
requests and bound multilingual results instead of multiplying tokens by two characters.
`MeteringChatCompletionClient` passes only provider-reported input usage to the adaptive policy;
estimated usage never calibrates another estimate. Reported input already includes its cached
share and is not added to cached tokens again.

The singleton `IContextEstimateSamples` holds at most 16 estimate/report pairs for each connection,
endpoint and requested model, with at most 256 keys. It stores numbers only, in memory. The policy
adds the largest positive reported-minus-estimated difference in the recent samples to the baseline
safety reserve, bounded by the window. Lower reports never reduce the baseline. Provider aliases
are attributed to the requested model; endpoint/model changes use independent observations. When
the samples expire or the application restarts the baseline applies again. This protection is
observational, not an exact tokenizer or a guarantee against every provider-specific count.

## User-visible status of automatic fallback

The fallback does not add a chat-feed notification. Diagnostics distinguish `llm_fallback` from
`input_pressure` and `reused_projection`, report saved projection tokens, and record every summary
operation's numeric outcome, transmitted tokens, call count and latency. Existing history marks
still show persisted checkpoints. Stored conversation content is unchanged.

## Making the fallback visible to the user

Three layered options, in order of effort. Each is described as a separate change so they can be
reviewed independently.

1. **Log diagnostics** (implemented). `LLM context plan` records the reason and savings;
   `LLM context summary` records the summary operation and its cost/latency inputs. No UI change.
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
