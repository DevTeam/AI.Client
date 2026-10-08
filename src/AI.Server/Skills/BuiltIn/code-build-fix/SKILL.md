---
id: code-build-fix
name: Code build fix
icon: code-build-fix
kind: playbook
description: Fix build failures, compiler warnings or analyzer diagnostics by finding their causes and rerunning the affected checks; never commits.
parameters: {"type":"object","properties":{"diagnostics":{"type":"string","description":"Build errors, warnings or analyzer output supplied by the user"},"project":{"type":"string","description":"The project, target or configuration to build"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Files or directories to work on, only when provided or already selected"}},"additionalProperties":false}
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

3. Find the documented build/analyzer command and configuration in instructions, manifests or CI.
   Reproduce the supplied `diagnostics`, keeping complete output and the exit code. If the
   supplied output is stale, report the current result. Identify toolchain or environmental
   problems separately from source defects.
4. Group diagnostics by root cause, then fix upstream causes before cascading errors. Match
   existing code conventions and use the smallest relevant changes to source, project files,
   generated-source inputs or configuration. Never edit generated output as the permanent fix.
   Do not hide a problem by disabling an analyzer, adding blanket suppression, ignoring errors
   or weakening warning policy.
5. Rerun the affected build/analyzer, inspect residual diagnostics and continue until the
   requested diagnostics are eliminated or the remaining blocker is demonstrably external.
   Do not upgrade unrelated packages or the whole toolchain. If the same failure recurs without
   new evidence after three repair attempts, report what was tried rather than guessing.
6. Verify with the repository's documented commands and tools. Run checks appropriate to the
actual change. For tests, start with a project/module/class/test filter; use a wider or full suite
only as a final check when relevant and feasible. Choose runner-supported quiet/minimal output or
a concise reporter and short traceback options without losing failures or counts. Set an explicit
finite timeout for each test run within the effective tool limit; split longer suites and report
a timeout as incomplete. Read output and exit codes, and distinguish passed, failed, skipped and zero tests.
Do not disable hooks, weaken tests, suppress useful diagnostics or claim a check was run when it
was not. Respect an explicit request not to add tests. Compare final status with the baseline;
remove only scratch files and temporary logging created by this task, preserving unrelated work.

7. The final answer groups resolved diagnostics by cause, links changed files, reports the
   actual final command/configuration and results, and names every remaining diagnostic or
   environmental blocker. A build that failed to start is not a successful build.
