---
id: code-performance-optimize
name: Code performance optimize
icon: code-performance-optimize
kind: playbook
description: Measure a selected performance problem, optimize its demonstrated cause and compare equivalent before-and-after runs while preserving behavior; never commits.
parameters: {"type":"object","properties":{"goal":{"type":"string","description":"The performance problem to improve"},"metric":{"type":"string","description":"Latency, throughput, allocations, memory or another metric to measure"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Files or directories to work on, only when provided or already selected"}},"additionalProperties":false}
tools: ["list_directory","directory_tree","search_files","grep_files","read_text_file","read_multiple_files","get_file_info","process_run","ask_user","write_file","edit_file","create_directory","move_file","delete_file","run_skill"]
---

The user's instructions take precedence over this playbook. Follow AGENTS.md, CLAUDE.md,
CONTRIBUTING and the repository's build, test and style conventions. Ask questions and report
results in the user's language. Use `process_run` with an executable and argument arrays, not
interpolated shell commands. Directory grants and ordinary tool approvals still apply.

1. Establish the scope from the user's request and `paths`, or infer it from named symbols,
   diagnostics and project structure. Search before reading. Ask only when the scope is genuinely
   ambiguous: use `ask_user` with `pathKind: "file"` for one file or `pathKind: "directories"`
   for modules/directories, `options: []` and `allowOther: false`. Read a file from
   `answers[].other` and directories from `answers[].paths`. Verify selected paths are inside
   granted directories; a picker does not grant access. Do not ask again for values already given.
   For choices with options put the recommendation first with " (Recommended)". Declined stops
   the work concerned; expired or interrupted leaves that decision unresolved. Dismissed permits
   a justified conventional choice, which must be stated; never invent a missing target.
2. Read relevant project instructions, source, callers and tests. If there is Git, record
   `git status --porcelain=v1 -uall` in the repository root. Existing modifications are the
   user's work: preserve them, and edit such files only as needed for this authorized task.
   Never discard or overwrite unrelated changes.
The request authorizes the specified edits. Do not ask for a second blanket confirmation.
Before a materially different scope, breaking public API change, new dependency or destructive
operation, show a concrete proposal and ask only for the decision still missing. Never deploy,
run database migrations, stage, commit, checkout, stash, reset or push as part of this skill.

3. Define the workload, metric, representative data, environment and acceptable behavior.
   Reproduce the symptom and establish a baseline with the repository's profiler/benchmark
   tools. Capture configuration, warmup, number of runs and variation; do not use production
   traffic, destructive loads or secret-bearing inputs without explicit authorization.
4. Locate the bottleneck from traces, profiles or measurements. Separate startup, I/O,
   contention, allocations and algorithmic work. If no reproducible problem or measurement
   tool is available, report the limitation and evidence-based candidates; do not invent a
   speedup or make speculative changes under this skill.
5. Make one focused optimization at a time. Preserve contracts, ordering and concurrency;
   check cache lifetime/invalidation and memory trade-offs explicitly when relevant. Avoid
   dependencies or public API changes unless authorized.
6. Repeat comparable measurements using the same workload, environment and configuration.
   Report absolute values, relative change and variation, and inspect behavior checks.
   If a change does not demonstrate a benefit or causes regressions, remove only that
   optimization while preserving all user work.
7. Verify with the repository's documented commands and tools. Run checks appropriate to the
actual change. For tests, start with a project/module/class/test filter; use a wider or full suite
only as a final check when relevant and feasible. Choose runner-supported quiet/minimal output or
a concise reporter and short traceback options without losing failures or counts. Set an explicit
finite timeout for each test run within the effective tool limit; split longer suites and report
a timeout as incomplete. Read output and exit codes, and distinguish passed, failed, skipped and zero tests.
Do not disable hooks, weaken tests, suppress useful diagnostics or claim a check was run when it
was not. Respect an explicit request not to add tests. Compare final status with the baseline;
remove only scratch files and temporary logging created by this task, preserving unrelated work.

8. The final answer contains the measured cause, changed files, before/after measurements,
   methodology, checks and remaining trade-offs. Label noisy or incomplete results as such;
   never present a single favorable run as proven improvement.
