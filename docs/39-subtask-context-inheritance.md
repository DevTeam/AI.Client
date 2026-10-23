# Subtask context inheritance (proposal)

Status: Proposal

`spawn_subtask` (see [application management tools](17-app-tools.md#spawn_subtask)) starts every task from an empty conversation: the subtask receives `sharedInstructions`, the task text and its acceptance criteria, and nothing of the parent's history. This document plans three changes that let delegated work reuse what the parent already paid for — the provider's prompt cache — and, where it pays off, the parent's whole context.

## Why the cache misses today

A provider caches a byte-exact prefix in the order tools → system → messages. A subtask differs from its parent in the first two blocks even when it runs on the same model:

- **Tools.** Every App tool except `spawn_subtask` is filtered out of a subtask's list (`ChatAgent`, added by "Optimize general subtask execution" for focus and per-step tool tokens), and `AdaptiveContextPolicy` selects tools per request.
- **System.** Run instructions are composed into the leading system message: `run.subtask`, `run.context` with the subtask's own `branchId`, and standing instructions built with `appToolsAvailable: false`.

So the first differing token is near the very start, and nothing after it is reused.

## Stage 1. Shared cache key

Requests carry no cache routing key today. OpenAI-style providers route prompt caching by `prompt_cache_key` (and some by a `session-id` header); without it, two requests with the same prefix may land on different cache shards.

- Every request of a chat sends `prompt_cache_key` derived from the chat id: the main branch, its branches and every subtask started from them share one key. This is what opencode does (`SessionAffinity`: children use the parent's key, forks the fork source's).
- The key is sent only to connections known to accept it. A strict OpenAI-compatible server may reject an unknown field, so the capability is per connection, detected or switched off on a `400` that names the field, and remembered.
- `PromptPrefixTracker` keeps measuring overlap; provider-reported cached tokens in `TokenUsage` show whether the key helps.

Done when parent and subtask requests to a supporting provider carry the same key, an unsupporting endpoint keeps working unchanged, and the usage widget shows cached input for subtask requests whose prefix matches.

## Stage 2. No recursive delegation by default

A subtask that delegates again multiplies cost and slots, and a forked subtask (stage 3) sees the parent's own decision to delegate, which invites it to repeat the same `spawn_subtask` call. opencode denies `subagent` to its subagents and limits nesting with `experimental.subagent_depth`, default 1.

- `spawn_subtask` called from a subtask is refused at call time with a message telling the subtask to do the work itself or report it as unresolved. The tool stays in the list so the tool block of the prefix does not change.
- A global setting `MaxSubtaskDepth` (default 1) lifts the limit for users who want nested fan-out; the slot logic for nested runs stays as it is.
- `run.subtask` loses "Keep spawn_subtask available…" when the depth is exhausted.

Done when a subtask's `spawn_subtask` call returns the refusal at depth 1, nested delegation works at depth 2, and the existing nested-slot test still passes.

## Stage 3. Inherited context

### Parameter

`spawn_subtask` gets `context: "auto" | "inherit" | "fresh"` (default `auto`), per call and optionally per task.

- `inherit` — the caller wants the subtask to start from its own context.
- `fresh` — today's behaviour.
- `auto` — the Host decides by the rules below.

Changing the schema changes the tool hash, to which saved permissions are tied, so the change resets an existing `spawn_subtask` permission to `Ask`. The release note says so; the parameter description is kept short and stable so later wording edits do not reset it again.

### When the Host inherits

`inherit` is possible only when all of these hold; `auto` inherits exactly when they hold, an explicit `inherit` that fails them falls back to `fresh`:

1. **Same connection.** The task resolves to the parent's connection: no `connectionId` that names another one, and either no connection is marked for subtasks or the parent's connection is one of the marked ones (then it is pinned instead of taking a round-robin turn). Base URL, model, key and reasoning effort must be equal; a changed effort invalidates the message cache with some providers.
2. **Room.** The parent's last request plus a working reserve fits the context window: prefix ≤ 50% of the window, reserve `max(30k, 25% of the window)`.
3. **Narrow fan-out.** At most `MaxInheritedTasks` tasks in the call (default 4). Every step of every forked task reads the whole prefix from cache, roughly N × steps × 0.1 × prefix in full-price tokens; a wide fan-out of independent parts is cheaper fresh.

The outcome reports the mode actually used and, for a fallback, why: `context: "inherited"` or `context: "fresh (parent context too full)"`, so the caller and evaluations can see it.

### How the fork is built

- `ChatAgent` publishes the exact last request it sent — tools as sent, the composed system message and the messages — in a run-scoped snapshot, the same way it scopes instructions per run. `AppSubtaskTool` reads it through the `ToolRunContext`.
- The forked request is that snapshot unchanged, followed by one user message: the worker role, the task, its acceptance criteria, the evidence envelope and the subtask's own ids. The parent's assistant message with the `spawn_subtask` call is not part of the prefix.
- For the rest of the subtask the tools and system block stay frozen: no per-step tool selection, no instruction recomposition in the leading system message, no compaction. Run instructions the subtask needs later go into the trailing note. If the subtask would outgrow the window it stops and reports, as it does on its step budget.
- The trailing instruction says plainly that the subtask is a worker for one task, that the parent's plan and conversation are context and not its work, and that it ends with its own result.

### Tools in a forked subtask

The App tool filter for subtasks is removed in inherited mode — removing tools would break the prefix — and is kept for `fresh` until an evaluation shows whether removing it there too costs focus. Tools that cannot work without a person or a parent state answer at call time:

- **`ask_user`** answers itself instead of `dismissed` alone: when every question carries recommended options, they are returned as the answer with outcome `answered` and a note that they were chosen automatically, as `timeoutBehavior: "submit_defaults"` already does for an interactive run; otherwise the subtask is told to make the decision itself, state it in its answer and list it under `unresolved`. [Asking the user](19-ask-user.md) currently says the tool stays in a subtask's list, which the filter has contradicted since it was added; the document is corrected with this change.
- **Plan tools** do not change the plan. A subtask has no plan scope (`plansAllowed` is false), so the call returns "the plan belongs to the conversation that started you and is not available in a subtask; report progress in your answer".
- **`spawn_subtask`** follows stage 2.
- **Confirmations** stay as they are: anything that would ask is refused.

### Verification

- Unit tests: the decision table (connection, window, fan-out, explicit modes and fallbacks); the forked request starts with the parent's snapshot byte for byte; `ask_user`, plan and `spawn_subtask` answers in a subtask.
- Agent evaluation: the same suite with `context` forced to `fresh` and to `auto`, comparing pass rate, total and cached input tokens, and wall time. `auto` becomes the default only if it is not worse on pass rate and cheaper or faster overall; otherwise the default stays `fresh` and `inherit` remains available on request.
