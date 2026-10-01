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
2. Pick the right tool for the cost of the run, then execute it in the directory it belongs to,
   narrowed by `filter` when given. The executable is resolved through PATH and takes no shell
   syntax: pass the test runner as an executable with an argument array (for example,
   dotnet test with ["test","--filter","..."], pytest with ["-q","path::Test"], cargo test
   with ["test","--","--exact"]), never one shell string.

   - **Quick and focused** — a single test class or file, a narrow `filter`, or a known-fast test
     project expected to finish in tens of seconds. Use `process_run`. Read the whole output and
     the exit code in the next step.
   - **Long or broad** — the whole solution, several test projects at once, integration or E2E
     suites, no `filter`, or any run that has a real chance of exceeding `process_run`'s timeout
     or producing so much output it would crowd the main context. Use `spawn_subtask` with a
     task that names the runner, the working directory, the exact command and arguments, the
     `filter` if any, and asks for the totals (passed/failed/skipped), the exit code and, for
     every failure, the test name plus the assertion or error in one line plus the file link;
     tell the subtask to read the output itself and return only that summary.

   Split a multi-project run into projects or folders; apply the same tool choice to each chunk.
3. Read the result and the exit code from the chosen path.
   - For a `process_run` run, read the whole output. A command that found no tests, or failed to
     build, is not a pass: report it as such.
   - For a `spawn_subtask` run, the answer is already the structured summary — keep it intact
     and do not re-run locally. Only fall back to `process_run` when the summary is missing,
     empty, or contradicts itself, and say you re-ran and why.
4. The final answer contains: the runner used (`process_run` or `spawn_subtask`), the commands
   run or the subtask task description, the totals (passed, failed, skipped) per run, and for
   each failure its test name, the assertion or error in one line and the file as a link. Point
   out a failure that looks environmental (missing tool, port, network) rather than a bug.
5. When a test failed, call `ask_user` labelled "Failures" with "Fix them (Recommended)" and "Only
   report"; dismissed, expired or interrupted only reports. On fix, run `code-bug-fix` with
   `run_skill`, passing the failures as `problem`.
