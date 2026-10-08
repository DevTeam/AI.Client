---
id: code-dependencies-update
name: Code dependencies update
icon: code-dependencies-update
kind: playbook
description: Update chosen dependencies and lockfiles, adapt affected code to documented API changes and verify compatibility; never commits or deploys.
parameters: {"type":"object","properties":{"packages":{"type":"array","items":{"type":"string"},"description":"Packages explicitly selected by the user"},"versions":{"type":"object","additionalProperties":{"type":"string"},"description":"Exact target versions keyed by package name when specified"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Files or directories to work on, only when provided or already selected"}},"additionalProperties":false}
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

3. Read manifests, central version files, lockfiles, runtime/toolchain constraints and the
   package manager's documented commands. List current direct dependencies and relevant
   transitive constraints. Respect pinned versions and the user's selected `packages`/
   `versions`; do not update every dependency because the request names one.
4. For unspecified packages or versions inspect the configured registry and official release/
   migration notes through available permission-checked tools. Never assume a version is the
   latest from memory. Show a bounded concrete proposal with current/target versions, breaking
   changes and runtime requirements; use ordinary `ask_user` options for missing choices,
   at most eight options per question. Never solicit or print registry credentials. A
   declined, expired or interrupted version decision leaves packages unchanged; dismissal
   may select a stated compatible stable target, never an unexplained major upgrade.
5. Apply the authorized update with the repository's package manager or central manifest
   convention, maintaining lockfiles with the matching tool/version. Honor offline, frozen
   lockfile and dependency-installation restrictions. Never delete a lockfile to make
   resolution succeed, disable integrity checks or switch registries silently.
6. Adapt affected imports, APIs, configuration and tests using the actual documented migration.
   Inspect the dependency graph/diff for unexpected transitive churn, duplicate versions,
   incompatibilities and extra packages; explain necessary changes and avoid unrelated updates.
7. Verify with the repository's documented commands and tools. Run checks appropriate to the
actual change. For tests, start with a project/module/class/test filter; use a wider or full suite
only as a final check when relevant and feasible. Choose runner-supported quiet/minimal output or
a concise reporter and short traceback options without losing failures or counts. Set an explicit
finite timeout for each test run within the effective tool limit; split longer suites and report
a timeout as incomplete. Read output and exit codes, and distinguish passed, failed, skipped and zero tests.
Do not disable hooks, weaken tests, suppress useful diagnostics or claim a check was run when it
was not. Respect an explicit request not to add tests. Compare final status with the baseline;
remove only scratch files and temporary logging created by this task, preserving unrelated work.

8. The final answer lists old/new versions, manifest and lockfile changes, source adaptations,
   build/test results, breaking behavior and anything not verified. Do not publish, run
   database migrations or claim absence of vulnerabilities without an actual audit.
