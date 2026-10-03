# Token usage

Status: implemented.

## What is measured

Every request that reaches a model passes `MeteringChatCompletionClient`, the outermost decorator
of `IChatCompletionClient`. It records one `TokenUsageRecord` per answer however many retries it
took, in `<data-dir>/usage/yyyy-MM.jsonl`. Counts come from the endpoint's `usage` (asked for with
`stream_options.include_usage`); an endpoint that reports none is measured by estimate and the
record is marked `Estimated`.

Estimates now use a model-bound offline text tokenizer for recognized Cl100k/O200k models and
the conservative UTF-8 fallback for unknown names. Protocol framing remains estimated. Usage
calibration and prefix-token measurements use the same requested-model view. Summary operations
also publish numeric call/input/result/latency diagnostics; see
[context budgets and evaluation](31-context-evaluation.md). The separate opt-in evaluation
project does not write to the production ledger.

`ITokenUsageMeter` scopes say what a request was for (`TokenUsagePurpose`) and which project,
chat, branch and turn it served. They follow the asynchronous flow, so a nested run, a skill or a
summarizer is attributed without knowing it is measured.

## Cost

In order of preference:

1. the cost the endpoint quoted (`usage.cost`, as OpenRouter-style gateways send it);
2. the connection's prices, per million fresh input, cached input and output tokens;
3. an estimate from the endpoint's earlier quotes for the same connection (`UsageCostEstimator`),
   also for a request whose tokens are themselves estimated: a least-squares fit over the last
   200 quotes of the three kinds of tokens, or one blended rate when the quotes do not support a
   fit. The quotes of the last month
   are read from the ledger once, so it works right after a restart. Such a record has
   `CostEstimated`, and the widget marks the sum "≈ … estimated".

A gateway that quotes cost on only some responses therefore still gives a whole chat a cost.

## Prompt-cache diagnostics

Each compared chat request carries a `PromptPrefix`: estimated shared tokens, estimated full
current input (`EstimatedInputTokens`), and the changed part — `Tools`, `Instructions` or `History`.
Both token estimates use the same requested-model view. Comparisons are isolated by chat, branch,
purpose, connection id, requested model and endpoint. `PromptPrefixTracker` keeps only fingerprints
and numeric counts, in memory, for the last 512 comparison keys. First requests have no comparison.

The widget separates provider-reported `Cached` from approximate `Prefix overlap`. The latter is
`100 * ReusableInputTokens / PrefixInputTokens`, summed only over requests carrying the estimation
denominator. It never divides a local estimate by provider input. Older ledger records remain
readable and contribute usage and change counts; their overlap estimates without denominators
are excluded. No migration or reconstruction of old prompts is needed.

`Prefix changes` counts application changes, not complete provider cache resets. A changed tool
catalogue may still receive partial hits. High overlap with low cache hits does not establish
expiry, lack of support or a provider fault: serialization and eligibility are provider-dependent.
`Input in → Output out` is cumulative request traffic, including cached input and supporting
requests in the selected scope; it is not a compression ratio. The context estimate is separately
marked `≈` and includes reserved space.

How the request is kept stable is described in
[21-tool-selection-and-adaptive-compaction.md](21-tool-selection-and-adaptive-compaction.md).

## Rate limits

`RateLimitHeaderReader` reads the limits an endpoint states in its response headers — OpenAI's
`x-ratelimit-*-requests` / `*-tokens`, Anthropic's `anthropic-ratelimit-*`, and the single-window
`x-ratelimit-*` and `ratelimit-*` forms gateways use — from every response, refusals included.
`IConnectionRateLimits` keeps the latest per connection for the life of the process, and the chat
usage response carries those of the connection the chat last used. The widget shows them while
they are under 30 minutes old and highlights a window down to its last tenth.

## API

- `GET /api/projects/{projectId}/chats/{chatId}/usage` — `ChatTokenUsage`: totals, by purpose, by
  turn, and the rate limits.
- `GET /api/usage?from&to&projectId` — `TokenUsageReport` by day, model and purpose.
