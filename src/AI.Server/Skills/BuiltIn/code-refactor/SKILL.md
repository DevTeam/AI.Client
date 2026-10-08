---
id: code-refactor
name: Code refactor
icon: code-refactor
kind: playbook
description: Refactor selected code to improve its structure while preserving behavior and public contracts, then verify the changes; never commits.
parameters: {"type":"object","properties":{"goal":{"type":"string","description":"The structural improvement to make"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Files or directories to work on, only when provided or already selected"}},"additionalProperties":false}
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

3. State the existing behavior and contracts to preserve: public API, serialized data, exception
   behavior, side effects, ordering and concurrency where relevant. Inspect tests and comparable
   implementations; identify the actual duplication or responsibility problem before editing.
4. Propose a small sequence of structural edits. Reuse the repository's abstractions and DI
   conventions; avoid new frameworks, speculative generalization, unrelated renames or global
   formatting. If the requested improvement requires changing behavior, explain that decision
   and obtain authorization before doing it.
5. Establish a baseline with relevant existing checks. Add characterization tests only for
   meaningful behavior at risk and when permitted; do not write tests that merely mirror the
   new structure. Refactor in small steps and keep calling code and registrations consistent.
6. Verify with the repository's documented commands and tools. Run checks appropriate to the
actual change. For tests, start with a project/module/class/test filter; use a wider or full suite
only as a final check when relevant and feasible. Choose runner-supported quiet/minimal output or
a concise reporter and short traceback options without losing failures or counts. Set an explicit
finite timeout for each test run within the effective tool limit; split longer suites and report
a timeout as incomplete. Read output and exit codes, and distinguish passed, failed, skipped and zero tests.
Do not disable hooks, weaken tests, suppress useful diagnostics or claim a check was run when it
was not. Respect an explicit request not to add tests. Compare final status with the baseline;
remove only scratch files and temporary logging created by this task, preserving unrelated work.

7. The final answer names the structural improvement, preserved contracts, changed files,
   checks and any behavior that could not be verified. Attribute only this task's changes.
