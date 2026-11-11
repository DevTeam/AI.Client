# Optional local AI component

## Automatic development launch

Use the build application to download, verify, cache, and connect Qwen without setting environment
variables or running preparation scripts manually:

```powershell
dotnet run --project build -- run-desktop --local-ai --data-dir .data
```

The IDE configuration **AI.Desktop with Data Dir** runs this target. It prepares the model first,
publishes Desktop into `artifacts/run-desktop`, and launches it with the package path in the child
process's environment. `.data` is resolved relative to the solution directory and follows the
existing Desktop development profile. Other run configurations remain available.

Other build entry points:

```powershell
dotnet run --project build -- prepare-local-ai
dotnet run --project build -- prepare-local-ai --runtime win-arm64
dotnet run --project build -- run --local-ai
dotnet run --project build -- run-both --local-ai
```

`--local-ai` is opt-in, so ordinary Host/Web runs do not download model files. Preparation caches
shared downloads in `artifacts/local-ai/downloads` and complete packages in
`artifacts/local-ai/qwen3-q4-k-m-b12244/<runtime>`. Repeated preparation verifies and reuses the
package without network access. Downloads use temporary files; the ready manifest is published
last. Concurrent preparations share a file lock. The weights are pinned by SHA-256; the Windows
x64 runtime archive is also pinned by SHA-256 to the artifact used in the live evaluation.
Other platform archives come from the fixed official llama.cpp b12244 release. No Python,
PowerShell script, or separately installed inference engine is required by these build targets.
The cache can be removed when no local runtime is using it; the next preparation recreates it.

A Desktop executable launched directly from this checkout's `src/AI.Desktop/bin/...` also
discovers the prepared Qwen cache. This supports IDE profiles that still launch the project
directly instead of the build target. Explicit `AI_LOCAL_MODEL_DIRECTORY` and an installed
`local-ai/manifest.json` take precedence. Test hosts and installed applications do not search
the development cache. Discovery never downloads files; the build target owns preparation.

The preparation target supports Windows, Linux, and macOS x64/ARM64 packages. Actual preparation
and cache reuse were exercised on Windows x64; this does not certify mobile deployment.

## Implemented desktop foundation

The Host and Desktop recognize `local-ai/manifest.json` next to the executable. The optional
`AI_LOCAL_MODEL_DIRECTORY` environment variable selects a package outside the installation.
Without a manifest, nothing is started or downloaded. With a package, the background service
verifies the weights, starts its own llama-server on IPv4 loopback, and waits up to two minutes
for its authenticated model catalog. It then registers `Local Qwen3 0.6B` for the existing local
users through the normal settings and protected-secret services. Existing default connections
and the local connection's enabled state are preserved. The local service currently starts on
Host startup when enabled, rather than on the first completion. Saving the connection as disabled
unloads the runtime within approximately two seconds; enabling it starts the runtime again.

The process uses a fresh API key on each launch, a single inference slot, a 4096-token context,
and the Qwen template's `enable_thinking=false` setting. The normal OpenAI Chat Completions
client handles streaming and tool calls. Image input is disabled. A process failure is logged
without stopping the application. Unexpected runtime failures receive at most three launch attempts
with backoff; after repeated failures restart the Host to retry. Normal Host shutdown
terminates the child process tree. Abrupt termination of the parent process is not yet covered
by a platform-specific parent-death mechanism.

## Preparing a component

An experimental official MNN-Pocket Q4_1 QAT preset is also available:

```powershell
./scripts/Prepare-LocalAI.ps1 -RuntimeDirectory /path/to/runtime -RuntimeVersion <pinned-version> -OutputDirectory /path/to/new-mnn-package -ModelPreset MnnPocketQat
```

This selects exactly `MNN-Pocket-0.3B-Instruct-Q4_1.gguf` (191896672 bytes), not Q8 or LiteRT.
The upstream revision and SHA-256 are pinned in the preparer. The package manifest supplies its
model alias and connection name; old Qwen manifests remain compatible. Qwen is still the default
preset because the MNN trial did not pass structured tool calling. See [the trial report](analysis/mnn-pocket-qat-trial.md).

Obtain a trusted, platform-matched llama.cpp runtime distribution containing `llama-server`
(or `llama-server.exe`) and its dependencies and license notices. Pin the runtime version in
the release pipeline; do not use a floating latest version. The runtime must support
`--jinja`, `--chat-template-kwargs`, and the Qwen3 tool template. No Python, Ollama, or Docker
is needed on the user's machine.

Run with PowerShell 7 on the target platform:

```powershell
./scripts/Prepare-LocalAI.ps1 -RuntimeDirectory /path/to/runtime -RuntimeVersion <pinned-version> -OutputDirectory /path/to/new-local-ai
```

The preparer downloads one GGUF, validates its pinned SHA-256, and writes the manifest last.
It never overwrites an existing component. Preserve the runtime's original notices; the script
also includes Qwen's license. Model: `bartowski/Qwen_Qwen3-0.6B-GGUF`,
`Qwen_Qwen3-0.6B-Q4_K_M.gguf`, 484220320 bytes,
SHA-256 `9acfc1e001311f34b4252001b626f2e466d592a42065f66571bff3790d4e1b14`.
The Host accepts a package whose weights are below 2 GB and whose manifest context is
1024–32768 tokens, so that larger candidates such as Qwen3.5-2B can be run (see
[the Qwen3.5-2B trial](analysis/qwen35-2b-trial.md)). The bundled Qwen3 weights stay below 500 MB.
Total component size is reported separately.

Set `AI_LOCAL_MODEL_PACKAGE` to the prepared directory before running the existing release
packaging command. Both products receive the component. Windows installers expose an optional
Local AI component, selected for Full installations. Without the environment variable, existing
release payloads remain small. Linux/macOS packages currently include the component when staged;
separate optional native packages are still required. Do not place a component below the release
output directories, which the packaging target cleans before publishing.

For development, set `AI_LOCAL_MODEL_DIRECTORY` before launching the Host. Select the new
connection in the existing Connections editor after it becomes ready. The Host owns the runtime;
run the HTTP probe against that same machine, including when Web is opened on a phone.

## Validation and remaining release gates

The Qwen preset has now passed a desktop protocol trial with the production streaming client
and 50 seeded command requests. See [the Qwen trial report](analysis/qwen3-local-trial.md) for
the configuration, results, factual-language limitation, and remaining gates. Its untrimmed
Windows component is 548.58 MB; only the weights fit below 500 MB.

`scripts/Evaluate-LocalAI.ps1 -ApiKey <key> -ReportPath /path/to/report.json` runs the ten
representative tool/control scenarios with five seeds each against a separately started server.
It never executes the requested file or task operations. Pass `-Model` for another model alias.

`scripts/Test-LocalAI.ps1` performs a non-streaming function-call/result/final-answer round trip
against a separately started runtime with an explicitly supplied API key. It reports end-to-end
latencies, not a decode tokens-per-second benchmark. Production-client SSE, cancellation,
concurrent request queueing, cold load, long prompts, and real application tools must also be
checked against the selected runtime before shipping. Keep device, runtime commit, model hash,
prompt lengths, peak resident memory, and timings in benchmark reports. Do not compare Q4_0
measurements directly with Q4_K_M.

1. Pin and validate a desktop runtime, including streaming tool calls through the existing client.
2. Evaluate 50–100 actual tool scenarios in English and Russian, including nested arguments,
   missing information, unnecessary calls, and several sequential calls. Define acceptance by
   scenario risk before release; protocol compatibility alone is insufficient.
3. Build early Android and iOS prototypes using the same GGUF and an in-process native runtime
   with the server/tool-template layer. There are no mobile executable projects in this repository
   yet. JNI/XCFramework integration and a mobile application shell remain to be implemented.
4. Measure cold/warm start, first token, complete tool-call latency, prefill, decode, memory,
   and thermal degradation over ten minutes on named devices. Start with 4K context and one slot.
   Proposed targets: at least 10 decode tokens/s and below 2 GB for inference, subject to measurement.
5. Add on-demand loading/unloading, installation health feedback, child-process crash cleanup,
   and explicit handling of a removed component's saved connection.
6. Add separate Linux/macOS optional packages and versioned component updates with rollback.
   Current application updates must not silently remove an installed component; verify this on
   each installer before enabling the component in a published release.

No mobile performance or full release readiness is established by the desktop foundation.

## Connection lifecycle and status

The local connection now controls runtime loading through its existing Enabled switch. Disabling
it and saving unloads inference; re-enabling it starts inference with a fresh per-launch key.
Deleting the connection also unloads it for this Host session. At the next Host startup an installed
component registers its connection again. The existing default provider, name customizations,
and enabled choice are preserved. A missing package disables its saved connection and clears
its stale credential without removing other providers.

The connection editor exposes Check local AI status. The backing read-only endpoint is
`GET /api/settings/local-ai`, with states `notInstalled`, `verifying`, `starting`, `ready`,
`disabled`, `failed`, and `stopped`. The response contains no credentials or package paths.
Child output is drained separately so it cannot corrupt Host CLI/MCP/test protocol streams.

The explicit `LocalModelLifecycleLiveTests` check launches the actual prepared runtime using
isolated mock settings, verifies authenticated readiness, disables/unloads it, enables/restarts
it, checks key rotation, and stops it. Set `AI_LOCAL_MODEL_PACKAGE_LIVE_TEST` to a prepared package
to opt in. It does not alter user settings. This desktop lifecycle is verified separately from
mobile embedding, native package installation, and application-update rollback.
## Windows integration verification (2026-11-10)

Both self-contained Windows x64 products were published and their Inno Setup installers compiled
with the optional Qwen package. Trial version: `0.0.0-local-ai`. Generated installers:
`artifacts/release/packages/AI.Host-win-x64.exe` (595744315 bytes) and
`artifacts/release/packages/AI.Desktop-win-x64.exe` (613218011 bytes). These are development
artifacts, not a published release; native installation/uninstallation was not executed.

The published Host was run on loopback with a new data directory under
`artifacts/local-ai-trial/host-lifecycle`. Its settings API registered the local model with a
protected credential. The status endpoint reached ready, disabling the connection through the
normal settings API reached disabled, and re-enabling it returned to ready. The trial runtime
was then unloaded and the isolated Host stopped. Existing user settings were not used.

Final solution build: zero warnings/errors. Web tests: 780 passed. Server tests: 1577 passed,
3 skipped (two opt-in live checks and one Unix-only check). The actual runtime lifecycle check
was also run explicitly and passed. Android/iPhone embedding and measurements remain separate
work; no mobile release artifacts were created.

When preparing a runtime for a different architecture, pass `-RuntimeIdentifier` explicitly.
New package manifests record the target platform; release packaging checks it before publishing
and rejects component sources inside the release directory that is cleaned by the build.

## Mobile memory requirement

Android and iPhone devices with 6 GB total physical RAM are the minimum target; 8 GB devices
must also be covered. This is device RAM, not an inference allocation. Target peak memory for
the complete mobile application, including UI, runtime, weights, caches, and buffers: at most
2 GB, subject to measurement on physical devices and OS memory-pressure behavior. Passing a
Windows working-set measurement does not certify this requirement.

Desktop and mobile testing share the same 4096-token context baseline. A compact instruction
profile and a small tool catalog are required to fit the agent within that budget. It must
fit the agent request, output reserve, and safety allowance before dispatch; silently dropping
safety reserves is not an acceptable way to fit. Inference should unload on memory pressure and
when the OS requires background resources to be released. Increasing mobile context or changing
KV-cache precision requires measured memory and tool-quality validation. No mobile executable
or phone memory certification is delivered by the current desktop service.

## Compact instruction integration (2026-11-11)

Compact agent requests now use a short core and capability-specific instructions selected with
the offered tool schemas. Runtime command-policy details are omitted when command/script tools
are not offered. User/project instructions and existing output/safety reserves are preserved.
See [capability-scoped instructions](21-tool-selection-and-adaptive-compaction.md#capability-scoped-instructions-in-compact-requests).

An isolated production-agent greeting with the real Qwen model completed at context 4096.
Its final input estimate was 830/1503 tokens, with 289 additional schema tokens; the core's
per-layer estimate was 264 tokens. These are measurements of a fresh test project, not a promise
that arbitrary project instructions or all tool schemas will fit. Instruction diagnostics now
report individual block sizes without logging their contents. The live test uses a separate
runtime port and does not change the running Desktop's settings.

The complete server suite passed with 1588 tests and 4 environment-dependent skips. The isolated
solution build passed without warnings or errors. Rebuild/restart Desktop to apply the source
changes; an already running Desktop continues using the old assemblies.
