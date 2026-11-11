# Local AI integration: complete working context

Snapshot date: 2026-11-11, Europe/Moscow.
Repository: `C:\Projects\DevTeam\AI.Client`.
Branch at capture: `master`.

This is a consolidated handoff of the conversation, decisions, implementation, evidence,
limitations, and current unfinished work. It is not a verbatim transcript of every tool call.
Historical test results below apply to the stated revision/scenario, not automatically to the
latest expanded acceptance test. No credentials or protected credential contents are included.

## 1. User objective and constraints

Bundle a small local model as an optional installation component. Requirements:

- Reuse the existing OpenAI-compatible protocol, with minimal custom model logic.
- Support real tool/function calls.
- Model weights below 500 MB; distinguish weights from the complete runtime component.
- Initial inference-memory ceiling below 8 GB; subsequently clarified as support for phones
  with **6–8 GB total device RAM**, not a model allocation of that size.
- Android and iPhone are both future targets. No exact phone models were supplied.
- Avoid Python, PyTorch, Docker, Ollama, or an additional managed inference wrapper in delivery
  when possible. A native inference engine is still necessary.
- Cross-platform desktop delivery, inexpensive maintenance and updates.
- Desktop must reproduce the mobile context budget so the user can test on PC.
- User explicitly rejected an 8192-token workaround: **keep the baseline at 4096**.
- Instructions should adapt to available context and active capabilities. Avoid maintaining
  independent Full/Compact/Mobile copies for every individual rule or adding another LLM call.

The proposed mobile engineering target is at most 2 GB peak for the **entire application**,
including UI, weights, runtime, caches and buffers. This is a target, not a measured phone result.
There are no delivered mobile executable projects or phone measurements yet.

## 2. Project conventions relevant to continuing

- Read `docs/30-dependency-injection.md` before changing services/composition.
- Services receive interface dependencies; default to transient, singleton only for shared state.
- Pure.DI bindings and only actual consumer roots; named roots use `nameof`.
- Read `docs/33-styles-and-palette.md` before changing UI styles. Reuse design tokens and existing
  component styles; no new Razor stylesheets, style blocks, or arbitrary colors/typography.
- Source comments, identifiers, UI messages and project documentation are English.
- Preserve existing local changes. Do not kill the user's running Desktop or its model process
  to free build outputs; use isolated build artifacts when necessary.
- User expects actual verification and clear disclosure of failed or incomplete scenarios.

## 3. Model/runtime decisions

### Selected model

Qwen3-0.6B, GGUF Q4_K_M, served with native llama.cpp.

- Repository: `https://huggingface.co/bartowski/Qwen_Qwen3-0.6B-GGUF`.
- File: `Qwen_Qwen3-0.6B-Q4_K_M.gguf`.
- Verified weights: **484220320 bytes**.
- SHA-256: `9acfc1e001311f34b4252001b626f2e466d592a42065f66571bff3790d4e1b14`.
- Evaluated runtime: llama.cpp **b12244**, commit `d5bbcd1f3`, Windows CPU x64.
- Windows archive checksum used by the preparation target:
  `318f11fadcbc3a86541ec55d9930cc76646c6b8d2ff18798c8477e9ff0dc452a`.
- Native OpenAI-compatible Chat Completions, SSE and tool calls, `--jinja`.
- One inference slot; thinking disabled via `enable_thinking=false` template argument.
- b12244 accepts that argument but warns it is deprecated in favor of `--reasoning off`.
- Context baseline **4096**. Reserved output **1024**; normal protocol/safety reserve **1280**.
- Base input allowance: **1792** before tool schemas and adaptive estimate corrections.
- A fresh per-launch API key is stored through the existing protected-secret service.

### Other candidates considered

IBM Granite 4.0 H 350M and Liquid LFM2.5-350M were investigated as smaller candidates;
Qwen was explicitly selected by the user. No alternative silently replaced it.

The user then requested a trial of official **MNN-Pocket-0.3B Q4_1 QAT**, not its LiteRT variant.

- Source: `https://huggingface.co/taobao-mnn/MNN-Pocket-0.3B-GGUF`.
- Revision: `791b7e92fa790a5ca5bbd7604feb1ce1d2429d14`.
- File: `MNN-Pocket-0.3B-Instruct-Q4_1.gguf`, **191896672 bytes**.
- SHA-256: `a281c127e388b16537fd3f5d31ab32d3f87739e6dc514ac1093c80d9eac8d097`.
- Weights plus untrimmed Windows runtime: **256261123 bytes**.
- Structured calls in its six-request trial: **0/6**; JSON appeared in ordinary assistant text.
- `tool_choice=required` did not establish correct structured output.
- A Russian factual sample was incoherent. It remains an explicit experimental preset.
- See `docs/analysis/mnn-pocket-qat-trial.md`.

The LiteRT conversion at `https://huggingface.co/litert-community/MNN-Pocket-0.3B` was also
investigated. Its own card documented plain-text function calls and malformed arguments.
The user chose the official GGUF QAT trial instead. LiteRT was not integrated.

## 4. Desktop implementation already committed

### Commit 17ec32fc

`Add optional local AI runtime and development launch support`

Included the local component foundation and additional development-launch changes present in
the workspace when the user explicitly asked to commit everything.

Key behavior:

- `local-ai/manifest.json` describes the optional component.
- `AI_LOCAL_MODEL_DIRECTORY` explicitly chooses a prepared package.
- Source-tree Desktop launches can locate the build-prepared cache via `LocalModelPackageLocator`.
- Missing components do not trigger downloads during Host startup.
- Weights are checked against SHA-256 and the 500 MB weights ceiling before launch.
- Runtime listens only on IPv4 loopback and uses the normal connection catalog.
- Stable local connection ID: `9540ce18-a279-41ed-8419-e2dde100d1b0`.
- Alias: `qwen3-0.6b-local`; default display name: `Local Qwen3 0.6B`.
- Enabled saved connection starts the runtime. Disabling and saving unloads it within about
  two seconds. Re-enabling starts it with a new key.
- Normal shutdown kills the owned child process tree. Parent-death cleanup after an abrupt
  Host crash remains a limitation.
- Unexpected failures get at most three launch attempts with backoff.
- Child stdout/stderr are drained, preventing corruption of Host CLI/MCP/test protocol streams.
- A removed package disables its saved local connection and clears its stale credential.
- Read-only status endpoint: `GET /api/settings/local-ai`; UI has Check local AI status.
- States: `notInstalled`, `verifying`, `starting`, `ready`, `disabled`, `failed`, `stopped`.
- Windows installers can include the optional offline model component.
- Release packaging accepts `AI_LOCAL_MODEL_PACKAGE`, checks target platform metadata when
  available, and rejects sources under the release output that it cleans.
- Linux/macOS separate optional native packages, independent component updates/rollback, and
  mobile embedding remain unfinished.

Important source files:

- `src/AI.Server/Hosting/LocalModelService.cs`
- `src/AI.Server/Hosting/LocalModelPackageLocator.cs`
- `src/AI.Contracts/Settings/LocalModelStatus.cs`
- `src/AI.Server/Hosting/Endpoints/SettingsEndpoints.cs`
- `src/AI.Web/Components/GlobalSettingsPanel.razor`
- `build/Targets/PrepareLocalAiTarget.cs`
- `build/Targets/RunDesktopTarget.cs`
- `build/Targets/PackageReleaseTarget.cs`
- `scripts/Prepare-LocalAI.ps1`
- `scripts/Test-LocalAI.ps1`
- `scripts/Evaluate-LocalAI.ps1`

### Commit 5f9e41a0

`Adapt compact instructions to selected tool capabilities`

Implemented the adaptive instruction work after the user requested a low-maintenance solution:

- Compact stable core measured at **264 estimated tokens** in the original evaluation.
- Small contracts for discovery, writes, security, skills, navigation, subtasks and themes.
- Trusted application-server identity and tool identifiers determine module activation.
- Shared contracts are deduplicated across tools.
- Schema admission considers its required guidance in the shared request allowance.
- `ModelInstruction.AppliesToTools` gates dynamic run guidance, including execution-policy details.
- `UsesCapabilityInstructions` marks a compact standing core selected by the budget policy.
- User/project instructions and mandatory run controls are preserved, not silently clipped.
- Existing history compaction retains a necessary tool floor; final request validation remains.
- Per-instruction size/key diagnostics, with no instruction contents in those diagnostics.
- No added inference dependencies, embeddings, intent-classifier model or summarization call.
- Legacy full-window guide remains compatible. Compact capability contracts are not separate
  PC/phone/model prompt variants.

Primary source files:

- `src/AI.Application/Chat/AdaptiveContextPolicy.cs`
- `src/AI.Application/Chat/IAdaptiveContextPolicy.cs`
- `src/AI.Application/Chat/IModelInstructionRegistry.cs`
- `src/AI.Application/Chat/ModelInstructionComposer.cs`
- `src/AI.Application/Chat/IModelInstructionComposer.cs`
- `src/AI.Application/Tools/ChatAgent.cs`
- `src/AI.Infrastructure/Chat/ContextPlanDiagnostics.cs`

See `docs/21-tool-selection-and-adaptive-compaction.md` and `docs/41-local-ai.md`.

## 5. Context-overflow incident and explicit rollback

The user's greeting failed before a model call:

```text
estimated input 1817 tokens, input limit 1792, reserved output 1024,
tool definitions 0, over by 25. Context window 4096;
instructions 1796, conversation 21, protocol and safety margin 1280.
```

A temporary fix increased the desktop baseline to 8192 and migrated old managed 4096 presets.
It passed a regression and a live lifecycle test but was **explicitly rejected by the user**:
the PC must exercise the same budget intended for low-memory phones.

All 8192 defaults, automatic migration and associated tests were reverted. The prepared cache
and trial manifests were checked and remained 4096. Do not reintroduce that workaround without
a new user decision. The server's validation may still accept other explicit context values;
that is not an automatic increase of the selected baseline.

The subsequent compact-instruction integration allowed a real greeting to complete at 4096.
An isolated fresh-project measurement: input **830/1503**, plus **289 schema tokens**.
This is not a promise that arbitrary user/project instructions or every schema will fit.

## 6. Historical verification and its limits

Qwen evaluation on AMD Ryzen 9 5900X, 12 runtime threads, Windows CPU:

- 50/50 constrained requests: **ten scenarios with five seeds**, not 50 independent tasks.
- Direct synthetic schemas tested weather, file paths, directory listing, search, nested filters,
  unnecessary-call avoidance and asking for a missing path.
- Those tests did not execute the actual filesystem operations or establish broker/discovery quality.
- Real application client passed streaming call reconstruction, tool-result continuation and
  cancellation after generation started.
- Isolated production-agent greeting passed at 4096.
- Rough warm speed: 61.9 tokens/s average over the earlier 50 requests; short English sample
  76.7 tokens/s. Individual tool cycle around 0.8 seconds. Not phone benchmarks.
- Earlier process peak working set about 1.30 decimal GB. Not entire-app RAM or a mobile guarantee.
- Ordinary Russian factual sample gave a false definition of rain, despite valid-looking Russian.
- Complete Qwen component with untrimmed Windows runtime: **548584564 bytes**. Weights fit 500 MB;
  the whole component does not.

Historical native packages, version `0.0.0-local-ai`, compiled but were not installed or published:

- `artifacts/release/packages/AI.Host-win-x64.exe`, 595744315 bytes.
- `artifacts/release/packages/AI.Desktop-win-x64.exe`, 613218011 bytes.
- They predate the latest adaptive-instruction source changes; do not call them up-to-date installers.

The published Host was tested with isolated data, and API enable/disable/re-enable worked.
Existing user settings were not changed by that test.

After adaptive integration: solution built without warnings/errors in an isolated artifacts
directory; complete server suite had 1588 passes and four conditional skips. Web suite earlier
had 780 passes. These results predate the expanded directory-dialogue live acceptance test.

## 7. Current user-reported failure

The user tested:

```text
User: Привет
Assistant: Hello! How can I assist you today?
User: какие файлы у менят есть
Assistant: Invented general categories of files, without reading them.
User: в мой директории
Assistant: Repeated unverified descriptions of the directory.
The application then compacted six messages.
Assistant: <tools> {"name":"mcp_app__tool_search","arguments":{"query":"directory","limit":5}} </tools>
```

Actual `.data/logs/ai-client-20261111.jsonl` confirmed:

- The relevant requests fit: approximately 829–1194 estimated input tokens under their limits.
- One tool schema was offered out of 47 permitted capabilities: tool search.
- The XML-looking text was not a structured call and was not executed.
- This failure is not the original 4096 overflow and must not be described as fixed by compaction.

User data was inspected read-only for this diagnosis. Do not copy credentials or broadly export
other chat histories. This handoff contains only the relevant supplied conversation and findings.

## 8. Expanded reproduction and unsuccessful experiments

Current modified test:
`tests/AI.Server.Tests/Hosting/LocalAgentGreetingLiveTests.cs`.

It now has a baseline greeting case and an inspect-directory case. The latter:

1. Starts the actual Host/agent against a prepared isolated 4K runtime.
2. Creates temporary `workspace/alpha.txt` and `workspace/notes.md`.
3. Adds the directory path to the project's description and grants `read` access.
4. Sends the supplied Russian sequence, including the typo and clarification.
5. Requires a structured `list_directory` call, successful tool result and final filenames.

The Russian case is **still failing**. The baseline greeting passed. More explicit evidence/
function-call instructions did not make the file chain work. Increasing the schema share,
scoping out unnecessary project ids and removing inactive wait/stop bootstrap priorities also
failed this acceptance test. All those production experiments were reverted to commit 5f9e41a0.

English control:

```text
What files do I have?
In my working directory.
```

This produced a real structured `mcp_app__tool_search` call with `query="files", limit=5`.
Search results included delete/edit/file-info/search operations. The model did not reach
`list_directory` and ultimately said it could not inspect files. The problem therefore includes
weak discovery/selection for vague queries and model behavior, not only Russian understanding.

Do not parse and execute textual XML/JSON as a workaround. Do not force a tool call for every
message, add a special case for the exact supplied phrase, raise context, or silently substitute
a model. None of those changes is currently present.

Detailed report: `docs/analysis/local-directory-dialog-trial.md`.

## 9. Exact working-tree state at capture

Already committed:

- `17ec32fc` — optional local runtime/development launch integration.
- `5f9e41a0` — capability-scoped compact instructions.

Uncommitted before creating this handoff:

- Modified `tests/AI.Server.Tests/Hosting/LocalAgentGreetingLiveTests.cs` — expanded live dialogue
  gate, English-control option, synthetic transcript and separate log capture.
- Untracked `docs/analysis/local-directory-dialog-trial.md` — unresolved-regression report.

This handoff adds another untracked Markdown file. No production source changes remain from
the failed latest experiments. The last targeted policy/composer run passed 48 tests after
production rollback. The directory live gate remains red; do not hide it behind ordinary CI
skips or report the overall goal as achieved.

## 10. Running the reproduction

Prepared isolated package used in the trials:

```text
C:\Projects\DevTeam\AI.Client\artifacts\local-ai-trial\qwen3
```

Its manifest uses port **52178** and context **4096**. This deliberately avoids the user's
active development-cache runtime on 52176. Never terminate the user's runtime to run the test.

```powershell
$env:AI_LOCAL_AGENT_LIVE_TEST = '1'
$env:AI_LOCAL_MODEL_DIRECTORY = (Resolve-Path artifacts/local-ai-trial/qwen3).Path
$env:AI_LOCAL_AGENT_REPORT_DIRECTORY = [IO.Path]::GetFullPath('artifacts/local-ai-trial/directory-dialog')
dotnet test --project tests/AI.Server.Tests/AI.Server.Tests.csproj -- --filter-class '*LocalAgentGreetingLiveTests'
```

For the English control, additionally set `AI_LOCAL_AGENT_ENGLISH_CONTROL=1` and choose a separate
report directory such as `artifacts/local-ai-trial/directory-english`. Leave it unset for Russian.
The test timeout is 180 seconds; report capture is from synthetic data only.

Generated evidence:

- `artifacts/local-ai-trial/directory-dialog/directory-transcript.json`
- `artifacts/local-ai-trial/directory-dialog/directory-ai-client-20261111.jsonl`
- `artifacts/local-ai-trial/directory-english/directory-transcript.json`
- `artifacts/local-ai-trial/directory-english/directory-ai-client-20261111.jsonl`
- Earlier `artifacts/local-ai-trial/qwen-results.json`, `qwen-text-results.json`, `qwen-roundtrip.json`.
- Earlier `artifacts/local-ai-trial/adaptive-greeting` logs.

Generated files are not committed and may be overwritten by repeated trials. Reports distinguish
baseline and directory logs; transcripts are synthetic and contain no real directory listing.

Normal development launch configuration:
`.run/AI.Desktop with Data Dir.run.xml` calls `run-desktop --local-ai --data-dir .data` through
the build project. Model preparation is available through `build/Targets/PrepareLocalAiTarget.cs`.

When Desktop locks its build outputs, do not kill it; verify using:

```powershell
dotnet build AI.slnx --artifacts-path artifacts/adaptive-instructions-build --nologo -v:q -m:1
```

Applying source changes to the user's actual running Desktop still requires rebuilding and
restarting that application. An isolated successful build does not update an existing process.

## 11. Next work and acceptance criteria

The immediate unresolved objective is reliable discovery-to-directory-reading behavior with
real Qwen at 4K, without interpreting text as execution and without model-specific phrase hacks.

Promising investigation directions, not approved outcomes or implemented fixes:

- Test a small set of directly offered read-only tools instead of mandatory two-stage discovery.
  This changes the local capability tradeoff and must be evaluated beyond one exact phrase.
- Inspect tool-search scoring/pinning for vague queries. In `ToolCatalogRegistry.SearchAndPin`,
  lexical scores are followed by name ordering; broad file queries can prioritize inappropriate
  operations. Avoid an unsafe automatic write/delete path and verify retrieval independently.
- Measure the real selected schemas, mandatory rules, tool results and output budget at each
  step. Do not claim budget success implies semantic task success.
- Retest Russian and English, ambiguous/missing paths, allowed and denied directories, and
  actual filenames unknown to the model before the read. Verify no invented names are reported.
- Keep the current failing dialogue as a release gate. A proposed fix must produce genuine
  tool-call ids and successful directory results, not merely a plausible final answer.

Future mobile work remains unstarted: application shells, JNI/XCFramework/native engine
integration, lifecycle/memory pressure, Android and iPhone measurements on 6–8 GB devices.
Do not equate desktop results or supported runtime platforms with delivered mobile support.
