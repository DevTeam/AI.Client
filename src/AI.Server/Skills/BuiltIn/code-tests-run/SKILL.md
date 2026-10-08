---
id: code-tests-run
name: Code tests run
icon: flask
kind: playbook
description: Run the project's tests with the right tool — process_run for quick focused suites, spawn_subtask for long or broad ones — and report passed, failed and skipped with the cause of each failure; changes nothing unless the user then asks for a fix.
parameters: {"type":"object","properties":{"filter":{"type":"string","description":"Test project, file, class or name pattern, if the user named one"}},"additionalProperties":false}
tools: ["list_directory","directory_tree","search_files","grep_files","read_text_file","read_multiple_files","process_run","spawn_subtask","ask_user","run_skill"]
---

1. Find how the project runs its tests, in this order: the project instructions and instruction
   files, README or CONTRIBUTING, CI workflow files, then manifests (package.json `test` script,
   *.sln or test *.csproj, pyproject.toml or pytest.ini, Cargo.toml, go.mod, Makefile targets).
   Do not guess a runner the project does not use.
2. Pick the right tool for the cost of the run, then execute it in the directory it belongs to.
   Start with the narrowest relevant project, module, file, class or test-name filter, including
   an inferred filter when the user did not supply one. Run a broad/full suite as a final check
   when the request and available time warrant it; label a filtered result as filtered. The
   executable is resolved through PATH and takes no shell syntax: pass the test runner as an
   executable with an argument array (for example,
   dotnet test with ["test","--filter","..."], pytest with ["-q","path::Test"], cargo test
   with ["test","--","--exact"]), never one shell string.

   Use the actual runner's supported quiet/minimal verbosity, concise reporter and short
   traceback options to avoid sending passing-test chatter into the model context. Inspect the
   installed runner's help or project configuration before choosing flags; runner versions and
   test platforms can differ. Examples when supported are pytest `-q --tb=short`, VSTest's
   `--logger console;verbosity=minimal`, and Cargo `--quiet`. Keep test counts and complete
   failure diagnostics available through a report/artifact or a focused
   rerun; never suppress failures or rely on truncated output. Set an explicit finite timeout
   for each `process_run`, no greater than the effective tool limit, and a runner-native per-test
   timeout where supported and appropriate. Split or delegate a suite that cannot fit the limit.

   - **Quick and focused** — a single test class or file, a narrow `filter`, or a known-fast test
     project expected to finish in tens of seconds. Use `process_run`. Read its concise output,
     test counts and exit code in the next step.
   - **Long or broad** — the whole solution, several test projects at once, integration or E2E
     suites, no `filter`, or any run that has a real chance of exceeding `process_run`'s timeout
     or producing so much output it would crowd the main context. Use `spawn_subtask` with a
     task that names the runner, the working directory, the exact command and arguments, the
     filter, timeout and concise runner options, and asks for the totals (passed/failed/skipped),
     the exit code and each distinct failure cause with representative test names and file links;
     tell the subtask to read the output itself and return only that summary.

   Split a multi-project run into projects or folders; apply the same tool choice to each chunk.
3. Read the result and the exit code from the chosen path.
   - For a `process_run` run, read its concise output and any failure report. A command that found
     no tests, failed to build, timed out, or returned truncated output without recoverable
     failure details is not a pass: report it as such.
   - For a `spawn_subtask` run, the answer is already the structured summary — keep it intact
     and do not re-run locally. Only fall back to `process_run` when the summary is missing,
     empty, or contradicts itself, and say you re-ran and why.
4. The final answer contains: the runner used (`process_run` or `spawn_subtask`), the commands
   run or the subtask task description, the totals (passed, failed, skipped) per run, and for
   each distinct failure its representative test name, assertion or error and file link. Group
   repeated failures by cause and retain the full report when available. Point out a failure
   that looks environmental (missing tool, port, network) rather than a bug.
5. When a test failed, call `ask_user` labelled "Failures" with "Fix them (Recommended)" and "Only
   report"; dismissed, expired or interrupted only reports. On fix, run `code-bug-fix` with
   `run_skill`, passing the failures as `problem`.
