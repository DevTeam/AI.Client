---
id: code-review
name: Code review
icon: code-review
kind: playbook
description: Review selected code, working-tree changes, branches or commits for correctness, compatibility and concurrency with actionable source evidence; changes nothing.
parameters: {"type":"object","properties":{"focus":{"type":"string","description":"The review focus or acceptance criteria"},"paths":{"type":"array","items":{"type":"string"},"maxItems":200,"description":"Files or directories to work on, only when provided or already selected"},"scope":{"type":"string","enum":["files","working-tree","branches","commits"]},"branches":{"type":"array","items":{"type":"string"},"maxItems":200},"commits":{"type":"array","items":{"type":"string"},"maxItems":200},"base":{"type":"string","description":"The comparison base branch or commit, if specified"}},"additionalProperties":false}
tools: ["list_directory","directory_tree","search_files","grep_files","read_text_file","read_multiple_files","get_file_info","process_run","ask_user"]
---

The user's instructions take precedence over this playbook. Follow AGENTS.md, CLAUDE.md,
CONTRIBUTING and the repository's build, test and style conventions. Ask questions and report
results in the user's language. Use `process_run` with an executable and argument arrays, not
interpolated shell commands. Directory grants and ordinary tool approvals still apply.

1. Establish the scope from the user's request and `paths`, or infer it from named symbols,
   diagnostics and project structure. For a Git review, defer branch/commit selection to step 3;
   do not open a path picker merely because a Git reference is missing. Search before reading.
   For file/module reviews ask only when the scope is genuinely
   ambiguous: use `ask_user` with `pathKind: "file"` for one file or `pathKind: "directories"`
   for modules/directories, `options: []` and `allowOther: false`. Read a file from
   `answers[].other` and directories from `answers[].paths`. Verify selected paths are inside
   granted directories; a picker does not grant access. Do not ask again for values already given.
   For choices with options put the recommendation first with " (Recommended)". Declined stops
   the work concerned; expired or interrupted leaves that decision unresolved. Dismissed permits
   a justified conventional choice, which must be stated; never invent a missing target.
2. Read relevant project instructions; source, callers and tests must match the review input.
   For historical reviews read them from the resolved commits in step 4 rather than assuming the
   local checkout represents those revisions. If there is Git, record
   `git status --porcelain=v1 -uall` in the repository root. Existing modifications are the
   user's work: preserve them, and edit such files only as needed for this authorized task.
   Never discard or overwrite unrelated changes.
Keep this skill read-only: do not edit source, install dependencies, run migrations, stage,
commit, checkout, stash, reset or push. Use investigation commands that do not mutate application
data or repository files. Report any unavailable evidence instead of treating it as verified.

3. Determine the review source from `scope` and the request. For uncommitted changes inspect
   both staged and unstaged diffs plus relevant untracked source; keep existing changes visible
   as review input. For files/modules read the implementation and callers. For branches or
   commits use exact values already supplied, otherwise call `ask_user` with
   `pickerKind: "branch"` or `pickerKind: "commit"`, the absolute `repositoryPath`,
   `multiSelect: true`, `options: []` and `allowOther: false`. Read full values from
   `answers[].values`. Without a required selection, stop and report it, never guess targets.
4. Resolve Git revisions as commits with `git rev-parse --verify --end-of-options` before
   constructing comparisons. For a branch use its merge base with the specified `base`, or
   the known target branch; clarify when the base is ambiguous. Review each selected branch
   separately unless the user asked for a comparison. For selected commits inspect their
   patches and surrounding committed code, including merge parents when relevant. Use
   `git show`/diff on resolved hashes; never checkout or mix local source into a historical
   review as though it were the reviewed revision.
5. Trace suspicious behavior through callers, contracts and tests. Focus on functional errors,
   boundary cases, compatibility, resource lifetime, error handling, authorization and races.
   Establish a concrete trigger and consequence for each finding; separate verified defects
   from questions needing evidence. Read-only inspection is the default; do not execute
   untrusted reviewed code or run a check that mutates application data.
6. Prioritize findings by impact. Give each actionable finding a verified file/line at the
   reviewed revision, the failing scenario, consequence and focused correction. Link verified
   source locations with absolute file URIs ending in `#L42` or `#L42-L48`. Omit cosmetic
   preferences unless the user requested style review. This is a correctness review;
   pre-commit housekeeping belongs to `code-changes-review`.
7. The final answer contains findings first, with severity and source references, followed by
   review scope and validation limits. When no supported findings exist, say so without
   claiming the code is defect-free. Do not apply fixes; a later request can authorize them.
