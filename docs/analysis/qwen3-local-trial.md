# Qwen3 0.6B Q4_K_M local trial

Date: 2026-11-10. This is a desktop protocol and constrained command smoke evaluation,
not a general agent benchmark or smartphone certification.

## Reproducible configuration

- Model: `bartowski/Qwen_Qwen3-0.6B-GGUF/Qwen_Qwen3-0.6B-Q4_K_M.gguf`.
- Verified SHA-256: `9acfc1e001311f34b4252001b626f2e466d592a42065f66571bff3790d4e1b14`.
- Weights: 484220320 bytes (484.22 decimal MB).
- Runtime: llama.cpp b12244, commit d5bbcd1f3, Windows x64 CPU, Clang 20.1.8.
- CPU: AMD Ryzen 9 5900X, 12 cores / 24 logical processors; 12 runtime threads.
- Context: 4096; one inference slot; `--jinja`; embedded template; thinking disabled
  with `--chat-template-kwargs '{"enable_thinking":false}'`.
- The runtime accepts this option but warns it is deprecated in favor of `--reasoning off`.
- Runtime distribution: 64352541 bytes. Complete trial component: 548584564 bytes (548.58 MB).
  This includes the untrimmed runtime and notices. It exceeds a 500 MB total-component budget.
  The weights-only budget passes. No compression or trimming benefit is assumed.

## Tool selection and arguments

`scripts/Evaluate-LocalAI.ps1` runs ten cases with five seeds each (1–5), temperature 0.8,
top_p 0.8, max_tokens 192, all through standard OpenAI Chat Completions with automatic tool choice.
Five representative functions are supplied: weather, file reading, directory listing, text
search, and a nested task filter. These are synthetic schemas representative of application
operations; the actual tools are not executed by this evaluation script.

| Case | Result |
|---|---:|
| Weather, English | 5/5 |
| Weather, Russian with exact English city identifier | 5/5 |
| File reading, English | 5/5 |
| File reading, Russian | 5/5 |
| Directory listing, English | 5/5 |
| Directory listing, Russian | 5/5 |
| Text search, English, two arguments | 5/5 |
| Nested filter, Russian, enum and integer | 5/5 |
| Greeting without unnecessary tool use | 5/5 |
| Missing path, clarification instead of a tool call, Russian | 5/5 |

Total: **50/50 seeded requests**, consisting of 40 structured calls with exact expected arguments
and 10 control requests without calls. These are ten scenarios, not fifty independent tasks.
No tools mutate real files. Only the ordinary HTTP protocol is used; no model-specific text parser
was added. Partial/raw reports: `artifacts/local-ai-trial/qwen-results.json` (generated, not committed).

## Application-client streaming and continuation

The opt-in `LocalModelLiveTests` exercises the real `OpenAiCompatibleChatCompletionClient` and
`ChatCompletionSseParser` against the running model. It passed:

1. Stream a structured `get_weather` call and reconstruct its JSON arguments.
2. Return a tool result reporting 12 degrees and rain using the call ID and assistant history.
3. Stream a final answer containing the supplied temperature, without another tool call.
4. Cancel an active long generation after receiving text and observe cancellation.

The non-streaming `Test-LocalAI.ps1` round trip also passed in English and with a natural Russian
request about Moscow. English: 0.446 seconds to the call, 0.797 seconds for the two-request cycle.
Russian: 0.506 seconds to the call, 0.819 seconds total. These are individual warm samples;
they do not establish tail latency, cold-load performance, or mobile performance.

To rerun the live test, start the model with the configuration above, then set
`AI_LOCAL_MODEL_LIVE_TEST=1`, `AI_LOCAL_MODEL_KEY`, and optionally `AI_LOCAL_MODEL_URL` /
`AI_LOCAL_MODEL_NAME`, before running the `LocalModelLiveTests` class. Without opt-in it is skipped.

## Speed, memory, and language quality

Across the 50 requests, server-reported decode speed averaged 61.9 tokens/s, range 9.6–72.6.
The minimum came from a very short greeting, where tokens/s is not a stable estimate.
An English sentence sample decoded at 76.7 tokens/s; a Russian factual answer at 70.4 tokens/s.
An application-client test briefly shared the single inference slot with the evaluation, so
evaluation wall-clock latencies can include queueing; use the isolated round-trip samples above.

After the requests, Windows reported 1272213504 bytes current working set and 1266696 KiB
peak working set (about 1.30 decimal GB). This is the inference process only, not the entire
AI.Client application or all memory commitment. Raw measurement: `qwen-memory.json` under the
trial directory. No long-duration thermal or parallel-load test was run.

The ordinary Russian factual question asking for a definition of rain produced a grammatically
recognizable but false answer describing rain as fog formed from rain on the ground. This fails
basic factual quality. By contrast, the Russian weather-tool continuation correctly reported
the supplied temperature and precipitation. Successful tool routing must not be interpreted as
general Russian knowledge quality.

## Decision and next gates

Retain Qwen as the default experimental preset for constrained local commands. It passes this
small protocol evaluation and is more compatible than the tested MNN-Pocket Q4_1 QAT combination.
Do not present it as a general offline assistant, certify real multi-step agent workflows, or
enable a public release solely on these results.

Before release: evaluate actual application tool schemas and longer histories, test cold load
and failure recovery, decide whether 500 MB means weights or the entire component, and validate
the same artifact on Android and iPhone. No connected phones or mobile executable projects were
available in this trial. Smartphone speed and memory remain unmeasured.
