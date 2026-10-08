---
id: code-tests-run
name: Code tests run
icon: flask
kind: playbook
description: Run focused and final test suites with concise output and bounded timeouts, then report verified totals and failure causes; changes nothing unless the user asks for a fix.
parameters: {"type":"object","properties":{"filter":{"type":"string","description":"Test project, file, class or name pattern, if the user named one"}},"additionalProperties":false}
tools: ["list_directory","directory_tree","search_files","grep_files","read_text_file","read_multiple_files","list_allowed_directories","process_run","trigger_wait","ask_user","run_skill"]
---

For verbose test output, use the `purpose: "chatTemporary"` root from
`list_allowed_directories` for task-only logs or reports when available. Keep the console
summary concise, preserve exit codes and failure counts, and inspect relevant failure excerpts.
Remove task-created scratch files when finished.

1. Find how the project runs its tests, in this order: the project instructions and instruction
   files, README or CONTRIBUTING, CI workflow files, then manifests (package.json `test` script,
   *.sln or test *.csproj, pyproject.toml or pytest.ini, Cargo.toml, go.mod, Makefile targets).
   Do not guess a runner the project does not use.
2. Run tests in their project directory. Start with the narrowest relevant project, module,
   file, class or test-name filter, including an inferred filter when the user did not supply
   one. Run a broad/full suite as a final check when relevant and feasible; label a filtered
   result as filtered. Pass the runner as an executable with an argument array, not one shell
   string. Check that its filter matches the installed runner: for example, Microsoft.Testing.Platform
   and VSTest accept different filtering and output options.

   Use the actual runner's supported quiet/minimal verbosity, concise reporter and short
   traceback options to avoid sending passing-test chatter into the model context. Inspect the
   installed runner's help or project configuration before choosing flags; runner versions and
   test platforms can differ. Examples when supported are pytest `-q --tb=short`, VSTest's
   `--logger console;verbosity=minimal`, and Cargo `--quiet`. Keep test counts and complete
   failure diagnostics available through a report/artifact or a focused
   rerun; never suppress failures or rely on truncated output. Set an explicit finite timeout
   for each `process_run`, no greater than the effective tool limit, and a runner-native per-test
   timeout where supported and appropriate. Prefer a native report file in the chat temporary
   directory when console output would be large; read its summary and only the relevant failing
   cases. Do not read the entire log into context. A broad suite that fits the deadline can run
   directly with concise output. Split longer suites by project or module; use an existing
   managed job only when it exposes status, cancellation and a final report.

   A test host that cannot start or build because another process holds the test binaries —
   another chat's run, a build, an IDE — is a blocked check: report the holding process and name
   it, because freeing it is the user's decision. Never stop, kill or restart a process this run
   did not start. For a test host this run started that has stalled and holds a locked report
   file, call `ask_user` naming that process or PID and act only on an explicit affirmative
   answer; a declined, dismissed, expired or interrupted answer leaves it running.
3. If an independent test job is already running and exposes a known local PID or a read-granted
   report path, use one bounded `trigger_wait` call when offered for process exit or file change
   instead of repeated status queries. Use `stableForMs` if the report is written incrementally.
   The trigger does not start a job or survive chat cancellation. After it fires, check the
   managed job's exit status and complete report; if neither is available, completion is unverified.
   For a direct `process_run` call, use its result without an extra trigger.
4. Read the concise output, exit code and any failure report. A command that found no tests,
   failed to build, timed out, or returned truncated output without recoverable failure details
   is not a pass: report it as such.
5. The final answer contains: the commands run, the totals (passed, failed, skipped) per run, and for
   each distinct failure its representative test name, assertion or error and file link. Group
   repeated failures by cause and retain the full report when available. Point out a failure
   that looks environmental (missing tool, port, network) rather than a bug.
6. When a test failed, call `ask_user` labelled "Failures" with "Fix them (Recommended)" and "Only
   report"; dismissed, expired or interrupted only reports. On fix, run `code-bug-fix` with
   `run_skill`, passing the failures as `problem`.
