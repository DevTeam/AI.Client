---
id: code-tests-add
name: Code tests add
icon: code-tests-add
kind: playbook
description: Add tests for selected behavior, uncovered edge cases or regressions using the project's existing test framework, then run them; never commits.
parameters: {"type":"object","properties":{"behavior":{"type":"string","description":"The behavior or regression to cover"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Files or directories to work on, only when provided or already selected"}},"additionalProperties":false}
tools: ["list_directory","directory_tree","search_files","grep_files","read_text_file","read_multiple_files","get_file_info","list_allowed_directories","process_run","ask_user","write_file","edit_file","create_directory","move_file","delete_file","run_skill"]
---

For verbose diagnostics or intermediate files, use the `purpose: "chatTemporary"` root
from `list_allowed_directories` when available. Preserve command exit codes, inspect only
relevant excerpts of saved output, and keep final artifacts in their intended location.
Remove task-created scratch files during cleanup.

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

3. Find the existing test framework, fixtures, helpers and documented runner. Read production
   behavior and nearby tests; identify observable contracts and real coverage gaps. Do not add
   a test framework or package without presenting the concrete need first.
4. Select meaningful cases: normal behavior, boundaries, invalid input, failure paths, state
   transitions or concurrency as applicable. Prefer assertions about results and effects over
   private implementation details. Use deterministic data and controlled time/resources;
   avoid sleeps, real credentials or external services for ordinary unit tests.
5. Implement tests at the appropriate level with existing helpers. For a regression reproduce
   the failure for the right reason before a fix; for existing correct behavior, do not
   deliberately break production code merely to obtain a failing run. Do not change production
   behavior under a request only to add tests. Report a discovered bug with reproduction evidence
   and hand it to `code-bug-fix` only when a fix is authorized.
6. Verify with the repository's documented commands and tools. Run checks appropriate to the
actual change: start with the new tests using a project/module/class/test filter, then run a wider
or full suite as a final check when relevant and feasible. Use runner-supported quiet/minimal
output or a concise reporter and short traceback options, retaining failure details and counts.
Set an explicit finite timeout for each test run within the effective tool limit; split longer
suites and report a timeout as incomplete. Read output and exit codes, and distinguish passed,
failed, skipped and zero tests.
Do not disable hooks, weaken tests, suppress useful diagnostics or claim a check was run when it
was not. Respect an explicit request not to add tests. Compare final status with the baseline;
remove only scratch files and temporary logging created by this task, preserving unrelated work.

7. The final answer contains the behavior now covered, test files and cases, commands and results,
   remaining coverage gaps and any discovered defects. Do not claim broader coverage than measured.
