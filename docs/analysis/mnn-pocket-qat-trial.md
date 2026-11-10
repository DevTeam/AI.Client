# MNN-Pocket Q4_1 QAT desktop trial

Date: 2026-11-10. This is a small smoke trial, not a quality benchmark or mobile certification.

## Configuration

- Official repository: `taobao-mnn/MNN-Pocket-0.3B-GGUF`.
- Revision: `791b7e92fa790a5ca5bbd7604feb1ce1d2429d14`.
- File: `MNN-Pocket-0.3B-Instruct-Q4_1.gguf`, 191896672 bytes.
- SHA-256: `a281c127e388b16537fd3f5d31ab32d3f87739e6dc514ac1093c80d9eac8d097`, verified after download.
- llama.cpp Windows CPU x64 build b12244, commit d5bbcd1f3, Clang 20.1.8.
- CPU: AMD Ryzen 9 5900X, 12 cores / 24 logical processors. Runtime used 12 threads.
- Context 4096, one slot, `--jinja`, embedded model template, OpenAI Chat Completions API.
- Trial component including the downloaded runtime: 256261123 bytes. Runtime trimming was not attempted.

## Results

Ordinary English completion worked: for `Write one sentence about rain.` the answer was
`Rain taps gently against the leaves, turning the world into a soft, silver hush.`
The server reported 173.1 decode tokens/s for this short, warm completion. A Russian prompt
asking what rain is produced incoherent Russian mixed with English, reaching the 100-token cap;
its 156.3 tokens/s does not represent a useful answer.

Tool requests defined `get_weather(city: string)` using the OpenAI function schema and asked
for Moscow. Three fixed seeds (1, 2, 3), temperature 0.8, top_p 0.8, and max_tokens 128 were
tested for each of `tool_choice=auto` and `tool_choice=required`.

- Structured `message.tool_calls`: **0/6**. The model emitted JSON in `message.content`.
- `required` requests also emitted unrelated explanations and reached the output cap.
- Server-reported decode speed for those six requests: **62.5–75.2 tokens/s**.
- An additional Russian tool request substituted `city` for the requested Moscow.
- Another exploratory English request produced an argument list instead of the schema's object.
- A separate streaming request reached `[DONE]` but contained no structured `tool_calls`.

Raw local trial responses are under `artifacts/local-ai-trial/`: `tool-results.json`,
`text-results.json`, and `tool-stream.sse`. These generated artifacts are not committed.
Timings come from the server, do not include cold-start latency, and are not smartphone estimates.
No reliable peak-memory measurement was established in this trial.

## Decision

Keep MNN-Pocket Q4_1 QAT as an explicit experimental preset, not the default shipped model.
Its size is attractive and ordinary English generation works, but this exact model/runtime/template
combination fails the application's structured-tool protocol. Do not interpret or execute JSON
found in ordinary assistant text as a workaround. A later server/template configuration may be
evaluated separately; no model-specific parser was added to the application.

Qwen remains the default preset, not a certified alternative: its live tool quality still needs
the same evaluation. Android/iPhone inference, Russian quality, multi-step tools, and comparative
latencies have not been established by this desktop trial.
