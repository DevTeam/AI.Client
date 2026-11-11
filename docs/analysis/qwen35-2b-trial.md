# Qwen3.5-2B desktop trial

Date: 2026-11-11. A desktop trial of a single candidate model for both PC and phones. It is not a
mobile measurement and not a release decision. It follows `local-ai-context-2026-11-11.md` and
`local-directory-dialog-trial.md`, where Qwen3-0.6B failed the Russian directory dialogue.

## Configuration

- Source: `unsloth/Qwen3.5-2B-GGUF`, revision `f6d5376be1edb4d416d56da11e5397a961aca8ae`.
- File: `Qwen3.5-2B-Q4_K_M.gguf`, **1280835840 bytes**, SHA-256
  `aaf42c8b7c3cab2bf3d69c355048d4a0ee9973d48f16c731c0520ee914699223`, verified after download.
- License: Apache 2.0 (`LICENSE-Model` copied from `Qwen/Qwen3.5-2B`). Text-only: no `mmproj` file.
- Architecture (upstream `config.json`): 24 layers, of which 6 use full attention (2 KV heads,
  head size 256) and 18 use Gated DeltaNet linear attention with a fixed-size state.
- Runtime: the same llama.cpp b12244 Windows CPU x64 build as the Qwen3-0.6B trial, one slot,
  `--jinja`, thinking disabled. AMD Ryzen 9 5900X, 12 runtime threads.
- Package: `artifacts/local-ai-trial/qwen35-2b`, port 52178 (live tests) or 52179 (direct tests).

To run the package through the Host, `LocalModelService` was relaxed for this trial: weights below
2 GB instead of 500 MB, and context up to 32768 instead of 8192. The live test now reads the
expected window from the package manifest, accepts `AI_LOCAL_AGENT_TIMEOUT_SECONDS`, and saves the
transcript on failure too. The relaxed limits were later committed; the live-test changes were not.

## Direct endpoint results

The same ten constrained scenarios and five seeds as `scripts/Evaluate-LocalAI.ps1`, sent directly
to llama-server with a 16384-token window:

| Check | Qwen3.5-2B | Qwen3-0.6B (earlier) |
|---|---|---|
| Structured tool calls, 10 scenarios x 5 seeds | **50/50** | 50/50 |
| Average request time | 1.12 s | ~0.8 s |
| Decode speed, warm | ~25.7 tokens/s | ~62 tokens/s |
| Tool round trip with result and final answer | passed, 1.82 s | passed, 0.80 s |
| Streaming call reconstruction | passed (`list_directory`, `{"path":"src"}`) | passed |
| Russian "what is rain" | correct definition | false definition |

Russian answers were correct and fluent, for example a definition of rain as an atmospheric
phenomenon with precipitation falling as drops, and a correct two-sentence file-versus-directory
explanation. Raw responses: `artifacts/local-ai-trial/qwen35-2b-eval/`.

## Memory and prompt processing

From the llama.cpp load log at a 16384-token window:

| Part | Size |
|---|---|
| KV cache (6 attention layers, f16) | 192 MiB (12 KiB per token) |
| Recurrent state | 19 MiB |
| Compute buffer | 72 MiB |
| Weights, memory-mapped | ~720 MiB host + file pages |
| `CPU_REPACK` copy of weights for AVX2 | 490 MiB |

Process measurements after a 5192-token prompt and two follow-up turns:

| Profile | Working set | Private bytes | First prompt (5192 tokens) |
|---|---|---|---|
| Defaults | 2158 MB | 1375 MB | 15.2 s |
| `--cache-ram 0 --ctx-checkpoints 2` | 2138 MB | 1356 MB | 15.5 s |
| `--no-repack` | **1667 MB** | **884 MB** | 15.1 s |

Repacking costs about 490 MB and gave no prompt-processing gain on this CPU. Generation speed
without repacking was not measured. Prompt processing is about 340 tokens/s on this desktop.
A phone will be several times slower, so large first prompts are the main latency risk.

Prefix reuse works for the hybrid model: with an unchanged prefix the second turn processed
15 new tokens in about 0.15 s instead of 5192 tokens in about 15 s.

## Production agent results

`LocalAgentGreetingLiveTests` through the real Host and agent, Russian dialogue, 600 s timeout:

| Window | Greeting test | Directory dialogue |
|---|---|---|
| 4096 | passed, ~4 s | failed: budget exhausted after two searches (1653/1503 input tokens) |
| 8192 | passed, ~5 s | failed: wrong paths, repeated calls, timeout |
| 16384 | passed, 46 s with an unnecessary tool search | failed: greeting treated as a task, timeout |

Every model step produced genuine structured tool calls. No textual pseudo-calls were seen, unlike
Qwen3-0.6B.

- **4096.** Greetings were answered directly. For the files question the model searched for
  `project workspace files directory` and found `list_allowed_directories`. The two search
  results then exhausted the input allowance, and compaction could not free enough.
- **8192.** The model called `list_allowed_directories` and received the real workspace root.
  It then kept calling `list_directory` and `read_text_file` with `/workspace` or `/data`,
  which were rejected as non-absolute paths. Later it called `subtask_stop` with invented ids.
  Context summaries ran repeatedly and took 24–29 s each.
- **16384.** Policy added the skills catalog and memory index and offered up to 13 schemas
  (about 7000 tokens), including application navigation. The model interpreted "Привет!" as a
  task and spent 25 steps searching tools and calling `app_navigate`.

Transcripts and logs: `artifacts/local-ai-trial/qwen35-directory-ru{-4k,-8k,}/`.

### English control

Same test with `AI_LOCAL_AGENT_ENGLISH_CONTROL=1` ("What files do I have?", "In my working
directory."). The first message is still "Привет!".

| Window | Directory dialogue |
|---|---|
| 4096 | failed: one `tool_search`, then "I cannot directly read files", budget exhausted on the next turn |
| 8192 | **passed** in about 75 s |
| 16384 | failed: greeting treated as a task (`app_navigate`, `app_read`), timeout |

At 8192 the model called `directory_tree` immediately. It corrected the relative-path error with
the absolute workspace path, reported exactly `alpha.txt` and `notes.md`, and then listed and read
both files. Every step was a genuine structured call with a successful result.

The difference between languages comes from the application, not from the model. Initial tool
selection in `AdaptiveContextPolicy` ranks tools lexically against their English descriptions.
A request in another language matches nothing, so it receives the unranked fill instead of the
file tools. The English request got `directory_tree` and `list_directory` in its first schema set.
The Russian request had to go through `tool_search`, and the search results then consumed the
budget and the model's attention.

Transcripts and logs: `artifacts/local-ai-trial/qwen35-directory-en-{4k,8k,16k}/`.

### Tool-name catalog and reading-first selection

Two harness changes were tried at 8192 with the same gate (600 s timeout):

1. **Reading tools first.** When no tool matches the request lexically, `AdaptiveContextPolicy`
   now orders the read-only workspace tools (`list_directory`, `directory_tree`, `read_text_file`,
   `grep_files`, `search_files`) ahead of the application tools inside the core set. The initial
   set became `tool_search` plus two reading tools.
2. **Names of omitted tools in the instructions.** A compact request listed the names of all
   permitted tools whose schemas were omitted, grouped by server (about 410 tokens for 44 names),
   with "call tool_search with the exact name to load one". `tool_search` accepted several exact
   names at once.

In the first series the catalog was silently dropped: at 8192 the run-guidance allowance
(`usable / 10`, about 590 tokens) is already taken by `run.finishing`, `capability.discovery` and
`run.context`, so even the ordinary `run.tool-discovery` note never reaches the model. That series
therefore measured change 1 alone. The second series made the catalog required.

| Variant | Russian dialogue | English control |
|---|---|---|
| Before (lexical selection) | 0/1 | 1/1 |
| Reading tools first | **2/3** (~60–90 s each) | 0/1 (`/workspace` loop, timeout) |
| Reading tools first + name catalog | 0/3 (2 timeouts, 1 wrong answer) | 1/1 (~420 s) |

- With reading tools first, both passing Russian runs searched with English words themselves
  (`list project files`), then called `list_allowed_directories` or `list_directory` with the
  absolute root. The failing run listed the parent of the workspace in the second turn and was
  refused by the grant.
- The name catalog made a small model worse in every run. It started calling `tool_search` for
  the greeting, looped on searches by name (`app_read`, `read_text_file` repeated up to ten
  times), passed tool names as file paths, and in one run wrote the project instructions through
  `app_instructions` in reply to "Привет!". This is the same distraction as at 16384: what the
  model sees as available, it tries to use. The catalog was removed; change 1 is kept.

The samples are small, and the English failure with reading tools first is the absolute-path
problem rather than tool selection. The remaining failures in every variant are path errors
(`/workspace`, relative paths, parent directories), which supports putting the allowed roots in
the standing context.

Transcripts and logs: `artifacts/local-ai-trial/qwen35-catalog-8k-*` (reading tools first) and
`qwen35-catalog2-8k-*` (with the catalog).

### Concept keywords

The reading-first ordering was then replaced by language-independent concepts (`IToolConcepts`).
`src/AI.Application/Search/ToolConcepts*.resx` hold keyword stems for 23 capability concepts
(File, Directory, List, Read, Write, Delete, Search, Archive, Web, Run, Chat, Theme and others) in
English and the 16 other languages of the Pure.DI resources (ar, bn, de, es, fr, he, hi, id, it, ja,
ko, pt, ru, th, vi, zh). Each built-in and application tool lists the concepts it serves. A concept
named in the request adds 2 to a tool's relevance; `tool_search` answers a non-English query by its
concepts and still refuses one that names none.

| Variant | Russian dialogue | English control |
|---|---|---|
| Concept keywords | **2/3** (70 s, 235 s) | 1/1 (130 s) |

- In two of three Russian runs, and in the English run, the first call of the files turn was
  `list_directory` from the initial schemas, with no `tool_search` step. The model first tried
  `workspace` or `/workspace`, then the absolute root.
- In the other passing Russian run the model ignored the offered `list_directory` and searched
  tools and skills, answered with skill names, and listed the directory only after "в мой
  директории".
- The failing run repeated `/workspace`, then wrote invented text into the project instructions
  through `app_instructions` and ran out of time. A write tool invoked without a request is a
  separate risk for small models and should be gated.

Selection now works in Russian as it does in English; the remaining failures are about paths and
unrequested application tools. Transcripts: `artifacts/local-ai-trial/qwen35-concepts-8k-*`.

## Conclusions

1. Qwen3.5-2B fixes the protocol failure: structured calls, correct Russian, sensible first
   discovery queries. On this evidence it is a better single model for PC and phone than
   Qwen3-0.6B, at about 1.3 GB of weights and roughly 0.9–1.7 GB process memory on desktop.
2. The directory dialogue still fails, and now the failure is mostly in the harness. The model
   does not know the workspace root before acting. Discovery results are long. Summaries
   cost a full local generation each. A larger window exposes tools that distract a small model.
3. A larger window alone does not help. 8192 is the only window where the dialogue passed
   (in English); 16384 failed in both languages, 4096 ran out of budget in both.
4. The Russian failure at 8192 is explained by language-blind lexical tool preselection: the same
   model, window and workspace pass when the request words match English tool descriptions.

Recommended next experiments, measured with this same gate:

- Make initial tool ranking language-independent, for example with Russian keywords in tool
  metadata or a small multilingual embedding model, and rerun the Russian 8192 dialogue.
  Listing omitted tool names in the instructions was tried and made results worse; concept
  keywords in 17 languages were then added (see above).
- Put the allowed directory roots, and optionally a short listing of the project root, into
  the standing context so that the first path is correct without discovery.
- Offer a small read-only file set (`list_allowed_directories`, `list_directory`,
  `read_text_file`, `grep_files`) directly for local models instead of two-stage discovery.
  Keep navigation, subtask and write tools out unless requested.
- Shorten `tool_search` results for small windows: names and one-line purposes, not full
  descriptions.
- Avoid LLM summarization on local models where possible, or limit how often it runs.
- Run with `--no-repack` (or measure repacking on ARM separately) and keep the tool prefix stable
  between steps so that prefix reuse applies.
- Measure on an actual Android and iPhone device before fixing the window and memory budget.
