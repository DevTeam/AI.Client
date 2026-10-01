# Token usage

Status: implemented.

## What is measured

Every request that reaches a model passes `MeteringChatCompletionClient`, the outermost decorator
of `IChatCompletionClient`. It records one `TokenUsageRecord` per answer however many retries it
took, in `<data-dir>/usage/yyyy-MM.jsonl`. Counts come from the endpoint's `usage` (asked for with
`stream_options.include_usage`); an endpoint that reports none is measured by estimate and the
record is marked `Estimated`.

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

Each record of a chat request carries a `PromptPrefix`: how much it shared with the previous request
of the same branch and purpose (estimated tokens of tools and leading messages), and what broke
the shared start — `Tools`, `Instructions` or `History` — or nothing. `PromptPrefixTracker` keeps
only fingerprints, in memory, for the last 512 branches. Totals add up the shared tokens
(`ReusableInputTokens`) and the resets by cause.

Reading it: a low cached share with resets names the application's change; a low cached share
without resets against a high reusable share means the provider did not serve its cache (expired
or not supported). How the request is kept stable is described in
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
