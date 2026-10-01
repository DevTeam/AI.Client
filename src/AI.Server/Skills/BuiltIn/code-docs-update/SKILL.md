---
id: code-docs-update
name: Code docs update
icon: code-docs-update
kind: playbook
description: Update documentation and examples to match inspected code and verified behavior, then check links and applicable examples; never commits.
parameters: {"type":"object","properties":{"goal":{"type":"string","description":"The documentation change or behavior to document"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Files or directories to work on, only when provided or already selected"}},"additionalProperties":false}
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

3. Locate the documentation destination and conventions: README, guides, API comments,
   changelog rules and generated reference sources. Inspect the actual implementation,
   configuration and public contracts for `goal`, plus existing examples.
4. Identify stale statements, missing steps and affected cross-references. Write for the
   document's audience with concrete behavior and examples. Preserve language, structure
   and established terminology; do not fabricate supported platforms, defaults, commands,
   benchmark results or migration guarantees.
5. Make the smallest complete documentation update. Keep examples and links consistent
   across affected pages. For generated documentation edit its source/template and use the
   documented generator, rather than hand-editing generated output. Do not change application
   behavior merely to match the prose.
6. Check referenced paths/symbols, local links and documentation build/lint when available.
   Run safe examples or compile sample code in an isolated temporary location when useful;
   never execute examples that deploy, migrate or delete real data. Distinguish verified
   examples from ones that need credentials or unavailable services.
7. Compare final status with the baseline and remove only task-created scratch artifacts.
   The final answer names the updated documents and what they now explain, links to them,
   validation results and examples or links that could not be verified.
