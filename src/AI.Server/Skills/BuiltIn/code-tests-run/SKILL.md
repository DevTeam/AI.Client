---
id: code-tests-run
name: Code tests run
icon: flask
kind: playbook
description: Run the project's tests, or the ones the user names, and report passed, failed and skipped with the cause of each failure; changes nothing unless the user then asks for a fix.
parameters: {"type":"object","properties":{"filter":{"type":"string","description":"Test project, file, class or name pattern, if the user named one"}},"additionalProperties":false}
tools: ["list_directory","directory_tree","search_files","grep_files","read_text_file","read_multiple_files","process_run","ask_user","run_skill"]
---

1. Find how the project runs its tests, in this order: the project instructions and instruction
   files, README or CONTRIBUTING, CI workflow files, then manifests (package.json `test` script,
   *.sln or test *.csproj, pyproject.toml or pytest.ini, Cargo.toml, go.mod, Makefile targets).
   Do not guess a runner the project does not use.
2. Run it with `process_run` in the directory it belongs to, narrowed by `filter` when given. The
   executable is resolved through PATH and takes no shell syntax: pass `npm` with
   `["test","--","..."]`, not one string. Split a suite that would outlive the timeout into
   projects or folders and run each.
3. Read the whole output and the exit code. A command that found no tests, or failed to build, is
   not a pass: report it as such.
4. The final answer contains: the commands run, the totals (passed, failed, skipped) per run, and
   for each failure its test name, the assertion or error in one line and the file as a link.
   Point out a failure that looks environmental (missing tool, port, network) rather than a bug.
5. When a test failed, call `ask_user` labelled "Failures" with "Fix them (Recommended)" and "Only
   report"; dismissed, expired or interrupted only reports. On fix, run `code-bug-fix` with
   `run_skill`, passing the failures as `problem`.
