# LLM context management

Status: Proposed

Implementation note: the current request policy, model-bound estimates, summary gates and
diagnostics are specified in [adaptive context selection](21-tool-selection-and-adaptive-compaction.md)
and [context evaluation](31-context-evaluation.md). The broader persistence/cache proposals below
are not all implemented and must not be read as the current storage contract.

## Goal

The client must complete long conversations predictably: neither the size of the history nor large tool results should lead to an endless `Generating`, an endpoint rejection on the context window, or a repeated transmission of data that has already been reduced for the model.

The change does not affect the full history shown to the user. The chat document JSON remains the source of truth and continues to store complete messages and tool results.

## Solution constraints

- no migration of existing chats is performed;
- `ChatDocumentSerializer.SchemaVersion` is not changed;
- stored `ChatMessage` and `ChatMessageDocument` do not get a `ModelContent` field;
- existing JSON files are not rewritten by a background process;
- compaction applies only to the request projection sent to the LLM;
- original branch nodes, tool results, and the visible history are not modified;
- the `assistant(tool_calls)` and corresponding `tool` message sequence remains protocol-intact.

## Observed problem

`ChatContext.Get` currently builds the entire branch path from root to head. Every stored tool result is restored as a regular `ChatCompletionMessage` with only the full `Content`. Within the current run, `ChatAgent` uses a reduced `ModelContent`, but after saving and starting the next user turn this projection is lost.

As a result, the request size grows monotonically. Especially fast it grows because of file reads, process output, search results, and nested tasks. The endpoint may take a long time to process such a request, return a context length error, or fail to send headers before the overall hourly deadline.

## Target scheme

```text
Full chat branch
        |
        v
Restoring model projection of tool results
        |
        v
Token estimation of messages and tool schemas
        |
        +-- fits --> send
        |
        v
Deterministic compaction of the old part of context
        |
        +-- fits --> send
        |
        v
Budgeted LLM summary of older turns (tool-free, user-role, checkpoint eligible)
        |
        +-- fits --> send
        |
        v
Clear local error before the HTTP request
```

The automatic LLM summary is the last step before failing the request. It exists so a runaway
history does not need a human to call `app_context_compact` manually: the planner reuses the same
endpoint as the run, sends one user message with the older turns and no tool list, and replaces
the covered turns with the reply in the model-only projection. The full history shown to the
user is unchanged. See `21-tool-selection-and-adaptive-compaction.md` for the diagnostics contract
and how to surface the event to the user.

## 1. Input context budget

### Settings

The connection needs two effective values:

- `ContextWindowTokens` — the full context window of the model;
- `ReservedOutputTokens` — the reserve for the new model response.

Values are resolved in the following order:

1. explicit override in the connection settings;
2. a conservative default for an OpenAI-compatible endpoint.

The current version shows the source as `Override` or `Default`. The resolver interface allows a built-in model catalog to be added later without changing the planner and UI contracts.

The new settings must be nullable and have working defaults, so the old `settings.json` is read without a separate migration. Saving settings may write the new fields; existing files load.

### Effective input budget

```text
EffectiveInputBudget = ContextWindowTokens - ReservedOutputTokens - ProtocolAndSafetyReserve
```

Without overrides the current defaults are a 32,768-token window and a 4,096-token output reserve.
The baseline protocol/safety reserve is 1,280 tokens, yielding 27,392 usable input tokens before
tools and trailing guidance. Reported underestimation can increase the safety reserve. Output
reserve is local; no unverified generation-limit parameter is sent to a compatible provider.

## 2. Model projection of tool results

### Restoring projection

When the agent builds the request, every saved tool result is checked for a saved `ModelContent`. If it exists, the projection from there is taken; otherwise a new projection is computed by `IToolResultProjector` from the original `Content`. The visible transcript and JSON are unchanged: the result is not rewritten on disk.

The projection is computed once per saved result and reused across runs of the same branch. The cache is invalidated by the message revision.

### Projection rules

For each saved `Content`:

- an empty or whitespace content becomes an empty projection;
- a long text is replaced by `head` (default 3000 characters) + marker + `tail` (default 1000 characters); the original length is recorded in `originalLength`;
- a `McpToolResult` with a structured `Content` of array of text and image parts is projected independently for each text part; images are reduced to metadata `{type: "image", mediaType: …, omitted: true, originalBytes: …, sha256: …}`;
- JSON results of the MCP server are converted to compact single-line JSON; indentation and whitespace are removed without changing keys;
- an object whose only field is a single long string (for example a wrapped trace) is unwrapped to the string with a one-line marker.

If the tool declared `outputSchema`, the projection must remain valid against it: a schema is provided as `StructuredContent` with truncated values, and the result of a structured tool is not reduced to a plain string. The protocol pair `tool_calls`/`tool` remains valid.

The projection is part of the request and is not persisted. The visible transcript shows the full result.

## 3. Token estimation

The estimator receives:

- a list of `ChatCompletionMessage` (including role names, `tool_call_id`, and arguments);
- JSON schema of available tools;
- service JSON wrapping of Chat Completions;
- a margin for tokenizer inaccuracy.

In the first stage a conservative estimate without a provider-specific tokenizer is allowed. The estimator must be a separate interface, so that the implementation can later be replaced without changing the agent loop. The estimate must work correctly with Russian text, code, and JSON; a simple division of string length by four is not safe enough for these data.

The result of planning is a `ContextPlan`:

```text
ContextPlan
|- InputLimit
|- EstimatedInputTokens
|- ReservedOutputTokens
|- WasCompacted
|- OmittedMessages
`- Messages
```

If the original context exceeds the budget, the planner invokes compaction. If the resulting context still does not fit, the HTTP request is not made, and the run ends with a diagnosable error containing the actual estimate and the model's limit.

Before history compaction, [the adaptive context policy](21-tool-selection-and-adaptive-compaction.md)
selects instruction variants and a bounded tool catalogue using the resolved connection's window
and output reserve. The planner uses that policy's protocol/safety allowance for final validation.

## 4. Deterministic compaction

When the budget is exceeded, the planner compacts the older part of the request deterministically:

1. The current user request and the current turn's `assistant(tool_calls)` + `tool` pairs remain unchanged.
2. Older `assistant` messages without tool calls are joined by `[conversation summary]` with the number of joined messages and the model that wrote them.
3. Older `user` messages are kept in their entirety if possible; otherwise they are joined by a short summary.
4. Older `assistant(tool_calls)` + `tool` pairs are compacted by replacing the `tool` content with the projection's `head`/`tail` while preserving the call IDs.
5. `system` messages are kept in full; they are short and important for instruction following.

The compacted message contains a marker `[compacted: N messages]`, so the model can tell what it sees. The order of remaining messages is preserved. The number of compacted messages and the resulting size are recorded in the run snapshot for diagnostics.

Compaction does not rewrite `ChatMessage` on disk. It produces only the model-facing projection.

## 5. Failure before the HTTP request

If the request cannot be reduced to the budget even after compaction, the agent:

- does not perform the HTTP request;
- returns `RunFailed` with `ErrorCode: ContextWindowExceeded`;
- publishes diagnostics: `estimatedInputTokens`, `inputLimit`, `reservedOutputTokens`, `wasCompacted: true`, `omittedMessages`, and the identifiers of the last messages included in the projection;
- shows the size breakdown in the run failure: total window, message allowance, instructions,
  conversation, schemas, output reserve and protocol/safety allowance. If instructions and schemas
  alone exhaust the window, it explicitly explains that history compaction cannot help.

The error is final for the run; the agent does not make repeat attempts with an even more aggressive compaction in the same turn.

## 6. Recovery and history continuity

A compacted request is only the request projection. The visible history, saved messages, and tool results remain complete. The next user turn starts from the same branch root with the same JSON, and the projection is recomputed for the new request.

When `previous_response_id` is not supported by the connection, the planner always sends the full root-to-head chain (with projection); the model's response is treated as the next message of the conversation, and the local history is the only source of truth.

## 7. Endpoints without Responses API

For Chat Completions summaries retain user-role authority. The application-only `IsContextSummary`
marker distinguishes synthetic continuation state from a real user request; it is not a wire
`name` field. Compaction never promotes source text to system instructions.

## Implementation notes

### Where to place the projection

`IToolResultProjector.Project(savedResult, callId)` is called from `ChatContext.Build` before token estimation. The projection is cached by `(messageId, revision)` in the chat document cache. The cache is shared by runs of the same chat and invalidated when a node is replaced.

### Where to place the planner

`IContextPlanner.PlanAsync(request, availableTools, cancellationToken)` is called by `ChatAgent` before the HTTP request. The planner owns projection, estimation, compaction, and the failure decision. The agent calls it once per model step.

### Diagnostics

Structured logs contain:

- `connection.id`, `connection.source` (Override/Default), `contextWindowTokens`, `reservedOutputTokens`;
- the number of messages and tool results, the number of messages after projection and after compaction;
- `estimatedInputTokens`, `inputLimit`, `wasCompacted`, `omittedMessages`, `compactedMessages`;
- for the failure case: `errorCode = ContextWindowExceeded`, the same fields.

Prompt content and tool result contents are not logged. The projection size, not the original size, is logged.

### UI

When the connection has no explicit override, the connection card shows `Context window: Default (16k)`. When an override is set, the card shows `Context window: Override (N tokens)`. The diagnostic toast after a failure shows the cause and the recommended action without revealing prompt content.

### Context ring in the composer

The send button carries a ring that shows how the last request of the branch filled the context
window; the button's tooltip repeats it as a donut with a legend above the keyboard shortcuts.
100% is the effective `ContextWindowTokens` of the connection selected in the composer. Layers,
clockwise from 12 o'clock:

| Layer | Source |
|---|---|
| Instructions | `system` messages of the planned request (`ContextPlan.InstructionTokens`) |
| Tools | `ContextPlan.ToolDefinitionTokens` |
| Conversation | the rest of `EstimatedInputTokens`, plus the final answer once the run ends |
| Your message | the composer text, estimated on the client with the same UTF-8 rule |
| Answer reserve | `ReservedOutputTokens` of the selected connection (drawn translucent) |
| Overhead | protocol overhead and safety margin (drawn translucent) |

The Host publishes the measurement as `ChatRunSnapshot.Context` every time the planner runs and
once more when the answer is published. It is kept in memory only: after a Host restart, or on a
branch that has not sent anything yet, the measured layers read as unknown until the next request.
The ring is grey at rest, takes the layer colours on hover, turns amber from 75% and red with a
slow pulse from 90%.

## Verification

- unit tests of `IToolResultProjector` on fixtures of saved results;
- unit tests of `IContextPlanner.PlanAsync` with a fake estimator and fake projection;
- integration tests of `ChatAgent` through a mock `IAIEndpoint`: a request with a long history fits after projection, a request with a very long history fits after compaction, a request exceeding the budget after compaction fails locally without an HTTP call;
- regression test: `ChatContext.Get` still returns the full visible history and tool results;
- regression test: existing JSON documents of chats and settings load without migration.

## Acceptance criteria

- a repeated turn after a large tool result sends `ModelContent`, not the full saved `Content`;
- the size of each request is checked before the HTTP call;
- the request does not exceed the model's effective input budget;
- compaction does not change the chat history and does not require migration;
- the tool-call protocol remains valid after reduction;
- the user receives a clear error instead of a long unexplained `Generating`;
- logs allow distinguishing context compaction, rate limit, header timeout, and first-token timeout without recording prompt/tool content;
- existing chat and settings documents continue to load;
- when the automatic LLM fallback fits the request, no HTTP error is returned for context size and the run continues without the model being told to call `app_context_compact`;
- when the automatic LLM fallback still does not fit, the run ends with `ContextWindowExceededException` carrying the post-fallback estimate, the model's limit and the omitted count.

## Not in the first release

- provider-side conversation state and `previous_response_id`;
- background rewriting of old chats;
- mandatory LLM summarization;
- exact tokenizer for each OpenAI-compatible provider;
- changing the visible transcript or removing full tool results.
